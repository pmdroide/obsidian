using System.Reflection;
using Anvil.ViewModels;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;

/// <summary>The title bar's GameObject menu and the Hierarchy "+" menu create scene objects.</summary>
internal static class GameObjectMenuChecks
{
    public static void Run()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds),
            Tree = new ModelDefinition(null, bounds), Bottle = new ModelDefinition(null, bounds), Vase = new ModelDefinition(null, bounds) };
        // Decals need a texture registered on Assets to be saved; a texture without a GraphicsDevice is enough here.
        assets.DefaultDecal = (Microsoft.Xna.Framework.Graphics.Texture2D)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.Texture2D));
        GC.SuppressFinalize(assets.DefaultDecal);
        var logic = new MainSceneLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var vm = new MainWindowViewModel();
        vm.AttachBridge(bridge);

        int entities = logic.BasicEntities.Count, suns = logic.DirectionalLights.Count, points = logic.PointLights.Count;
        vm.AddEntityCommand.Execute("Cube");
        vm.AddEntityCommand.Execute("IsoSphere");
        vm.AddDirectionalLightCommand.Execute(null);
        vm.AddPointLightCommand.Execute(null);
        vm.AddSpotLightCommand.Execute(null);
        Check(logic.BasicEntities.Count == entities, "GameObject menu commands wait for the game thread");
        Drain();
        Check(logic.BasicEntities.Count == entities + 2 &&
              ReferenceEquals(logic.BasicEntities[^2].ModelDefinition, assets.Cube) &&
              ReferenceEquals(logic.BasicEntities[^1].ModelDefinition, assets.IsoSphere),
            "GameObject > 3D Object creates a Cube and a Sphere");
        Check(logic.DirectionalLights.Count == suns + 1 && logic.DirectionalLights[^1].Intensity >= 100 &&
              logic.PointLights.Count == points + 2 && logic.PointLights[^2] is not SpotLight &&
              logic.PointLights[^1] is SpotLight { SpotAngle: 60f } spot && spot.Direction == -Vector3.UnitZ,
            "GameObject > Light creates a visible directional light, a point light and a downward spot light");

        vm.AddParticleSystemCommand.Execute(null);
        Drain();
        Check(logic.BasicEntities.Count == entities + 3 && logic.BasicEntities[^1] is
              {
                  Name: "Particle System", ModelDefinition: var marker, Scale: { X: 0.15f }
              } particles && ReferenceEquals(marker, assets.IsoSphere) &&
              particles.Components.Single() is ParticleSystemComponent,
            "GameObject > Effects > Particle System creates a small marker sphere with a Particle System");

        vm.AddTreeCommand.Execute(null);
        Drain();
        Check(logic.BasicEntities.Count == entities + 4 && logic.BasicEntities[^1] is { Name: "Tree", Material: null } tree &&
              ReferenceEquals(tree.ModelDefinition, assets.Tree) && tree.Components.Single() is FoliageWindComponent { Enabled: true },
            "GameObject > Nature > Tree creates the built-in tree (its own materials) with Foliage Wind");

        vm.AddLiquidBottleCommand.Execute(null);
        Drain();
        Check(logic.BasicEntities.Count == entities + 5 && logic.BasicEntities[^1] is { Name: "Bottle", Material: null } bottle &&
              ReferenceEquals(bottle.ModelDefinition, assets.Bottle) && bottle.Components.Single() is LiquidComponent { Enabled: true },
            "GameObject > Effects > Liquid Bottle creates the built-in glass bottle (its own material) with a Liquid component");

        vm.AddBreakableVaseCommand.Execute(null);
        Drain();
        Check(logic.BasicEntities.Count == entities + 6 && logic.BasicEntities[^1] is { Name: "Vase", Material: null } vase &&
              ReferenceEquals(vase.ModelDefinition, assets.Vase) && vase.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Dynamic } &&
              vase.GetComponent<BreakableComponent>() is { Enabled: true },
            "GameObject > Effects > Breakable Vase creates the built-in vase on a Dynamic body with a Breakable component");

        int decals = logic.Decals.Count;
        Vector3 spawn = bridge.SpawnPoint;
        vm.AddDecalCommand.Execute(null);
        Drain();
        Check(logic.Decals.Count == decals + 1 && logic.Decals[^1] is { IsEnabled: true, Scale: { X: 1f, Y: 1f, Z: 1f } } decal &&
              ReferenceEquals(decal.Texture, assets.DefaultDecal) && decal.Position == spawn &&
              decal.RotationMatrix == Matrix.Identity,
            "GameObject > Effects > Decal creates an enabled 2 m decal box projecting the default texture down -Z");
        DecalTextureChecks(logic.Decals[^1]);

        Check(vm.AddableObjects.Select(o => o.DisplayName).SequenceEqual(new[] { "Cube", "Sphere", "Directional Light", "Point Light", "Spot Light", "Particle System", "Decal", "Liquid Bottle", "Breakable Vase", "Tree" }),
            "the Hierarchy + menu offers the same objects as the GameObject menu");
        foreach (var option in vm.AddableObjects) option.AddCommand.Execute(null);
        Drain();
        Check(logic.BasicEntities.Count == entities + 12 && logic.DirectionalLights.Count == suns + 2 && logic.PointLights.Count == points + 4 &&
              logic.Decals.Count == decals + 2,
            "every Hierarchy + menu entry creates its object");

        void Drain() =>
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);

        // Inspector > Decal > Texture: Content-relative paths, saved with the scene.
        void DecalTextureChecks(Decal decal)
        {
            bridge.EnqueueSetDecalTexture(decal.Id, "Missing/no_such_stain.png");
            Drain();
            Check(decal.TexturePath == null && ReferenceEquals(decal.Texture, assets.DefaultDecal),
                "a decal texture that can't load leaves the decal on its current texture");

            decal.TexturePath = "Textures/stain.png"; // as if Content had built it
            var copy = (Decal)decal.Clone;
            Check(copy.TexturePath == "Textures/stain.png" && copy.IsEnabled, "copying a decal keeps its texture path");

            var snapshots = new List<EditorObjectSnapshot>();
            bridge.SnapshotUpdated += list => snapshots = list.ToList();
            for (int i = 0; i < 6 && snapshots.Count == 0; i++) Drain(); // published every 6th frame
            Check(snapshots.Any(s => s.Id == decal.Id && s.Kind == EditorObjectKind.Decal && s.DecalTexture == "Textures/stain.png"),
                "the editor snapshot carries the decal texture path to the Inspector");

            var scene = new Scene();
            scene.Decals.Add(decal);
            scene.Decals.Add(new Decal(assets.DefaultDecal, Vector3.One, Matrix.Identity, Vector3.One) { IsEnabled = true });
            string path = Path.Combine(Path.GetTempPath(), $"anvil-decal-{Guid.NewGuid():N}.obsc");
            try
            {
                SceneSerialization.SaveToFile(scene, path, assets);
                var loaded = SceneSerialization.LoadFromFile(path, assets).Decals;
                // The test has no built Content, so the picked texture falls back to the default but keeps its path.
                Check(loaded.Count == 2 && loaded[0].TexturePath == "Textures/stain.png" && ReferenceEquals(loaded[0].Texture, assets.DefaultDecal) &&
                      loaded[1].TexturePath == null && ReferenceEquals(loaded[1].Texture, assets.DefaultDecal),
                    "scene save/load keeps a decal's picked texture path and the default decal texture");
            }
            finally { File.Delete(path); }

            bridge.EnqueueSetDecalTexture(decal.Id, null);
            Drain();
            Check(decal.TexturePath == null && ReferenceEquals(decal.Texture, assets.DefaultDecal),
                "Reset puts the decal back on the default texture");
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
