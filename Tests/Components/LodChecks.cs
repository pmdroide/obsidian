using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

/// <summary>LOD component: level selection, editor bridge and inspector, saving, cloning, and the mesh library.</summary>
internal static class LodChecks
{
    public static void Run()
    {
        CheckSelection();
        CheckComponent();
        CheckCullWithoutModels();
        CheckSampleScene();
    }

    private static void CheckSampleScene()
    {
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            IsoSphere = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
        };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "LodTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var spheres = scene.BasicEntities.Where(e => e.Name.StartsWith("LOD Sphere")).ToList();
        Check(spheres.Count == 8 && spheres.All(e => e.ModelDefinition == assets.IsoSphere &&
                  e.GetComponent<LodComponent>() is { CullDistance: 50, Levels: [{ Model: "Cube", Distance: 20 }] }),
            "the LOD sample has a row of spheres that turn into cubes at 20 m and cull at 50 m");
    }

    private static void CheckSelection()
    {
        var distances = new List<float> { 10, 30 };
        Check(LodComponent.SelectLevel(5, distances, 0, 0) == 0 && LodComponent.SelectLevel(10, distances, 0, 0) == 1 &&
              LodComponent.SelectLevel(29, distances, 0, 1) == 1 && LodComponent.SelectLevel(31, distances, 0, 1) == 2,
            "the level is the last one whose distance the camera has passed");
        Check(LodComponent.SelectLevel(9.8f, distances, 0, 1) == 1 && LodComponent.SelectLevel(9.4f, distances, 0, 1) == 0 &&
              LodComponent.SelectLevel(9.8f, distances, 0, 0) == 0,
            "going back to a finer level needs the camera 5% closer (no flicker on a boundary)");
        Check(LodComponent.SelectLevel(100, distances, 80, 2) == -1 && LodComponent.SelectLevel(78, distances, 80, -1) == -1 &&
              LodComponent.SelectLevel(75, distances, 80, -1) == 2 && LodComponent.SelectLevel(1000, distances, 0, 2) == 2,
            "beyond the cull distance nothing draws (with the same hysteresis); 0 never culls");
        Check(LodComponent.SelectLevel(50, new List<float>(), 0, 0) == 0, "without levels the model always draws");
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(LodComponent.TypeId)?.ComponentType == typeof(LodComponent) &&
              ComponentEditorRegistry.Supports(LodComponent.TypeId),
            "LOD is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Statue", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "LOD").AddCommand.Execute(null);
        Publish();
        var lod = entity.GetComponent<LodComponent>();
        var editor = owner.Components.OfType<LodComponentViewModel>().Single();
        Check(lod is { CullDistance: 0, CurrentLevel: 0 } && lod.Levels.Count == 1 && lod.Levels[0] is { Model: "", Distance: 20 } &&
              editor.Levels.Count == 1 && !owner.AddableComponents.Single(c => c.DisplayName == "LOD").AddCommand.CanExecute(null),
            "LOD starts with one empty level at 20 m, no culling, and is unique per gameobject");
        Check(editor.Levels[0].ModelKeys.Contains("Cube"), "a level's model list offers the engine's model keys");

        editor.Levels[0].Model = "Cube";
        editor.Levels[0].Distance = 15;
        editor.AddLevelCommand.Execute(null);
        Publish();
        Check(lod.Levels.Count == 2 && lod.Levels[0] is { Model: "Cube", Distance: 15 } && lod.Levels[1] is { Model: "", Distance: 30 } &&
              editor.Levels.Count == 2,
            "inspector level edits reach the engine; Add Level starts twice as far as the farthest level");
        editor.Levels[1].Model = "Cube";
        editor.Levels[1].Model = null; // the ComboBox clears its selection while its items change
        editor.Levels[1].Distance = -5;
        editor.CullDistance = 120;
        Publish();
        Check(lod.Levels[1] is { Model: "Cube", Distance: 30 } && lod.CullDistance == 120,
            "transient empty selections and invalid distances are ignored; cull distance edits apply");
        editor.Levels[0].RemoveCommand.Execute(null);
        Publish();
        Check(lod.Levels.Count == 1 && lod.Levels[0] is { Model: "Cube", Distance: 30 } && editor.Levels.Count == 1,
            "removing a level removes it in the engine");

        lod.Levels.Add(new LodLevel { Model = "Cube", Distance = 60 });
        lod.CullDistance = 200;
        Publish();
        Check(editor.Levels.Count == 2 && editor.Levels[1] is { Model: "Cube", Distance: 60 } && editor.CullDistance == 200,
            "engine-side level changes show in the inspector");

        var record = ComponentRegistry.Capture(lod);
        Check(!record.Data.TryGetProperty(nameof(LodComponent.CurrentLevel), out _) &&
              ComponentRegistry.Restore(record) is LodComponent { CullDistance: 200 } restored &&
              restored.Levels.Select(l => (l.Model, l.Distance)).SequenceEqual(new[] { ("Cube", 30f), ("Cube", 60f) }),
            "saved LOD records round-trip their levels and exclude runtime state");
        var copied = ((BasicEntity)entity.Clone).GetComponent<LodComponent>();
        copied.Levels[0].Distance = 5;
        Check(!ReferenceEquals(copied, lod) && lod.Levels[0].Distance == 30, "cloning copies the LOD levels independently");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    // The library only touches models; with none loaded an uninitialized one is enough for the culling plumbing.
    private static void CheckCullWithoutModels()
    {
        var library = (MeshMaterialLibrary)RuntimeHelpers.GetUninitializedObject(typeof(MeshMaterialLibrary));
        var definition = new ModelDefinition(null, new BoundingBox(new Vector3(-1, -1, 0), new Vector3(1, 1, 2)));
        var entity = new BasicEntity(definition, null, new Vector3(0, 0, 0), Matrix.Identity, Vector3.One) { Name = "Tree" };
        var lod = new LodComponent { CullDistance = 50 };
        entity.AddComponent(lod);
        entity.RegisterInLibrary(library);
        entity.WorldTransform.HasChanged = false;
        entity.SelectLevelOfDetail(new Vector3(0, 30, 1));
        Check(lod.CurrentLevel == 0 && !entity.WorldTransform.HasChanged, "inside the cull distance the model draws");
        entity.SelectLevelOfDetail(new Vector3(0, 52, 1));
        Check(lod.CurrentLevel == -1 && entity.WorldTransform.HasChanged,
            "past the cull distance (measured to the bounds' centre) the gameobject is culled and redrawn");
        lod.CullDistance = 0;
        lod.OnChanged(entity);
        Check(lod.CurrentLevel == 0, "an inspector edit re-registers the levels and shows the model again");
        lod.CullDistance = 50;
        lod.OnChanged(entity);
        entity.SelectLevelOfDetail(new Vector3(0, 52, 1));
        entity.RemoveComponent(lod);
        Check(entity.WorldTransform.LodLevel == 0, "removing the component shows the model again");
    }

    /// <summary>The mesh library registers each level's meshes and removes them all again (needs a GPU for real models).</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var cube = new ModelDefinition(content.Load<Model>("GameObjects/Default/cube"), new BoundingBox(-Vector3.One, Vector3.One));
        var dragon = new ModelDefinition(content.Load<Model>("GameObjects/default/dragon_lowpoly"), new BoundingBox(-Vector3.One, Vector3.One));
        var models = new Dictionary<string, ModelDefinition> { ["Cube"] = cube, ["StanfordDragonLowpoly"] = dragon };
        var previous = LodComponent.FindModel;
        LodComponent.FindModel = key => models.GetValueOrDefault(key);
        try
        {
            var library = new MeshMaterialLibrary(graphics);
            var entity = new BasicEntity(cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "LOD cube" };
            // Unsorted, with an unknown model and the gameobject's own model as a level.
            var lod = new LodComponent
            {
                Levels = new List<LodLevel>
                {
                    new() { Model = "StanfordDragonLowpoly", Distance = 40 },
                    new() { Model = "Missing", Distance = 20 },
                    new() { Model = "Cube", Distance = 10 },
                },
            };
            entity.AddComponent(lod);
            entity.RegisterInLibrary(library);
            Check(entity.LodModels.SequenceEqual(new[] { cube.Model, dragon.Model }),
                "LOD models register nearest first and unknown models are skipped");
            Check(Levels(library, entity, cube.Model).OrderBy(l => l).SequenceEqual(new[] { 0, 1 }) &&
                  Levels(library, entity, dragon.Model).SequenceEqual(new[] { 2 }),
                "each level's meshes are in the library under their level");
            entity.SelectLevelOfDetail(new Vector3(0, 45, 0));
            Check(lod.CurrentLevel == 2, "far away the farthest level draws");

            lod.Levels.RemoveAt(0);
            lod.OnChanged(entity);
            Check(Levels(library, entity, dragon.Model).Length == 0 && Levels(library, entity, cube.Model).OrderBy(l => l).SequenceEqual(new[] { 0, 1 }) &&
                  lod.CurrentLevel == 0,
                "an edit replaces the registered levels");
            entity.Dispose(library);
            Check(library.Index == 0, "disposing the gameobject removes every level's meshes");
        }
        finally
        {
            LodComponent.FindModel = previous;
        }
    }

    // The LOD level of each library entry of this gameobject for the parts of model.
    private static int[] Levels(MeshMaterialLibrary library, BasicEntity entity, Model model)
    {
        var parts = model.Meshes.SelectMany(m => m.MeshParts).ToHashSet();
        var levels = new List<int>();
        for (int i = 0; i < library.Index; i++)
        {
            MaterialLibrary materials = library.MaterialLib[i];
            for (int j = 0; j < materials.Index; j++)
            {
                MeshLibrary meshes = materials.GetMeshLibrary()[j];
                if (!parts.Contains(meshes.GetMesh())) continue;
                for (int k = 0; k < meshes.Index; k++)
                    if (meshes.GetWorldMatrices()[k] == entity.WorldTransform) levels.Add(meshes.LodLevels[k]);
            }
        }
        return levels.ToArray();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
