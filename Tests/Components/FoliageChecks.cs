using System.Collections.ObjectModel;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

/// <summary>
/// Foliage Wind component and the Tree model: wind state, editor bridge and inspector, saving, the FoliageTest
/// sample, and (with a graphics device) the wind shader in the G-buffer and shadow passes.
/// </summary>
internal static class FoliageChecks
{
    public static void Run()
    {
        CheckWindState();
        CheckComponent();
        CheckSampleScene();
    }

    private static void CheckWindState()
    {
        var definition = new ModelDefinition(null, new BoundingBox(new Vector3(-3, -3, 0), new Vector3(3, 3, 8)));
        var tree = new BasicEntity(definition, null, new Vector3(2, 5, 0), Matrix.Identity, new Vector3(1.5f)) { IsEnabled = true };
        var other = new BasicEntity(definition, null, new Vector3(-6, 9, 0), Matrix.Identity, Vector3.One) { IsEnabled = true };
        var wind = new FoliageWindComponent();
        tree.AddComponent(wind);
        other.AddComponent(new FoliageWindComponent());
        var entities = new List<BasicEntity> { tree, other };

        tree.WorldTransform.HasChanged = false;
        FoliageWindComponent.UpdateAll(entities, 0.1f, new Vector2(3, 4));
        FoliageWind state = tree.WorldTransform.Wind;
        Check(state == wind.State && Math.Abs(state.Height - 12) < 0.001f &&
              Vector2.Distance(state.Direction, new Vector2(0.6f, 0.8f)) < 0.001f &&
              Math.Abs(state.Strength - 5 / FoliageWind.StrengthReferenceSpeed) < 0.001f && state.Clock > 0,
            "the scene wind (5 m/s) reaches the tree's shader state with its height from the model bounds x scale");
        Check(tree.WorldTransform.HasChanged, "a swaying tree is flagged as moved every frame, so its shadows redraw");
        Check(Math.Abs(other.WorldTransform.Wind.Phase - state.Phase) > 0.01f, "trees at different places sway out of step");

        float clock = state.Clock;
        FoliageWindComponent.UpdateAll(entities, 0, new Vector2(3, 4));
        Check(state.Clock == clock, "a zero time step (pause menu) holds the motion");
        FoliageWindComponent.UpdateAll(entities, 0.1f, new Vector2(30, 0));
        Check(state.Clock - clock > (clock - 0) * 1.5f, "stronger wind runs the tree's wind clock faster");

        FoliageWindComponent.UpdateAll(entities, 0.1f, Vector2.Zero);
        Check(Math.Abs(state.Strength - 1 / FoliageWind.StrengthReferenceSpeed) < 0.001f && Math.Abs(state.Direction.Length() - 1) < 0.001f,
            "in a calm scene Min Wind (1 m/s) keeps the tree moving");
        wind.WindScale = 0.4f;
        wind.MinWind = 0;
        wind.Height = 5;
        FoliageWindComponent.UpdateAll(entities, 0.1f, new Vector2(5, 0));
        Check(Math.Abs(state.Strength - 2 / FoliageWind.StrengthReferenceSpeed) < 0.001f && Math.Abs(state.Height - 7.5f) < 0.001f,
            "Wind Scale shelters a tree and Height overrides the model's height");
        wind.TrunkBend = float.NaN;
        wind.LeafFlutter = 10;
        FoliageWindComponent.UpdateAll(entities, 0.1f, new Vector2(5, 0));
        Check(state.Bend == 0 && state.Flutter == 3, "invalid and out-of-range settings are clamped");

        wind.Enabled = false;
        FoliageWindComponent.UpdateAll(entities, 0.1f, new Vector2(5, 0));
        Check(tree.WorldTransform.Wind == null, "a disabled component leaves the mesh static");
        wind.Enabled = true;
        FoliageWindComponent.UpdateAll(entities, 0.1f, new Vector2(5, 0));
        tree.RemoveComponent(wind);
        Check(tree.WorldTransform.Wind == null && other.WorldTransform.Wind != null, "removing the component stops the swaying");
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(FoliageWindComponent.TypeId)?.ComponentType == typeof(FoliageWindComponent) &&
              ComponentEditorRegistry.Supports(FoliageWindComponent.TypeId),
            "Foliage Wind is registered and offered in Add Component");

        var assets = new Assets { Tree = new ModelDefinition(null, new BoundingBox(new Vector3(-3, -3, 0), new Vector3(3, 3, 8))) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Tree, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Oak", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Foliage Wind").AddCommand.Execute(null);
        Publish();
        var wind = entity.GetComponent<FoliageWindComponent>();
        var editor = owner.Components.OfType<FoliageWindComponentViewModel>().Single();
        Check(wind is { Height: 0, TrunkBend: 1, BranchSway: 1, LeafFlutter: 1, WindScale: 1, MinWind: 1, Speed: 1, Enabled: true } &&
              editor.TrunkBend == 1 && !owner.AddableComponents.Single(c => c.DisplayName == "Foliage Wind").AddCommand.CanExecute(null),
            "Foliage Wind starts with neutral settings and is unique per gameobject");

        editor.TrunkBend = 1.5;
        editor.LeafFlutter = 0.4;
        editor.MinWind = 2;
        editor.Speed = double.NaN;
        editor.Height = 7;
        Publish();
        Check(wind is { TrunkBend: 1.5f, LeafFlutter: 0.4f, MinWind: 2, Speed: 1, Height: 7 },
            "inspector edits reach the engine and invalid input is ignored");
        wind.BranchSway = 2;
        Publish();
        Check(editor.BranchSway == 2, "engine-side changes show in the inspector");

        var record = ComponentRegistry.Capture(wind);
        Check(!record.Data.TryGetProperty(nameof(FoliageWindComponent.State), out _) &&
              ComponentRegistry.Restore(record) is FoliageWindComponent { TrunkBend: 1.5f, BranchSway: 2, LeafFlutter: 0.4f, Height: 7 },
            "saved records round-trip the settings and exclude the runtime wind state");
        var copied = ((BasicEntity)entity.Clone).GetComponent<FoliageWindComponent>();
        copied.TrunkBend = 0.2f;
        Check(!ReferenceEquals(copied, wind) && !ReferenceEquals(copied.State, wind.State) && wind.TrunkBend == 1.5f,
            "cloning copies the settings and gives the copy its own wind state");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), Tree = new ModelDefinition(null, bounds) };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "FoliageTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var trees = scene.BasicEntities.Where(e => e.ModelDefinition == assets.Tree).ToList();
        Check(trees.Count == 6 && trees.All(t => t.GetComponent<FoliageWindComponent>() is { Enabled: true } && t.Material == null) &&
              trees.Single(t => t.Name == "Tree Sheltered").GetComponent<FoliageWindComponent>().WindScale < 1,
            "the foliage sample has six swaying trees (their own bark and leaf materials), one sheltered from the wind");
        Check(scene.Environment.WindSpeed == 5 && scene.DirectionalLights.Single().CastShadows &&
              scene.MainCamera.GetComponent<ScriptBehaviourComponent>() is { Enabled: true },
            "the sample has a breeze, a shadowed sun and a flying camera");
    }

    /// <summary>The Tree model and the wind shader (needs a graphics device and built content).</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        Check(new[] { "WindEnabled", "WindParams0", "WindParams1", "WindTime", "WindLeaf", "ViewProjection" }
                  .All(p => content.Load<Effect>("Shaders/GBufferSetup/Gbuffer").Parameters[p] != null) &&
              content.Load<Effect>("Shaders/Shadow/ShadowMap") is var shadow && shadow.Techniques["DrawLinearDepthAlpha"] != null &&
              shadow.Parameters["WindEnabled"] != null && shadow.Parameters["View"] != null &&
              content.Load<Effect>("Shaders/Editor/IdRender").Parameters["ViewProjection"] != null &&
              content.Load<Effect>("Shaders/Deferred/DeferredDirectionalLight").Techniques["SSShadowed"] != null,
            "the G-buffer, shadow, picking and sun shaders compile with the foliage wind and translucency");

        var previousContent = Globals.content;
        Globals.content = content;
        try
        {
            var assets = new Assets();
            assets.Tree = (ModelDefinition)typeof(Assets).GetMethod("LoadTree", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(assets, new object[] { content, graphics });
            var parts = assets.Tree?.Model.Meshes.SelectMany(m => m.MeshParts).ToList();
            Check(parts is { Count: 2 } && parts.Count(p => p.Effect == assets.TreeLeavesMaterial) == 1 &&
                  parts.Count(p => p.Effect == assets.TreeBarkMaterial) == 1,
                "the Tree model has a bark part and a leaves part, each with its own material" + (assets.Tree == null ? " (not loaded)" :
                    " (" + string.Join(", ", assets.Tree.Model.Meshes.Select(m => m.Name + ": " +
                        string.Join("/", m.MeshParts.Select(p => p.Effect?.GetType().Name + " " + p.NumVertices)))) + ")"));
            Check(assets.TreeLeavesMaterial.Type == MaterialEffect.MaterialTypes.Foliage && assets.TreeLeavesMaterial.HasMask &&
                  assets.TreeLeavesMaterial.HasNormalMap && assets.TreeBarkMaterial.HasDiffuse && assets.TreeBarkMaterial.HasNormalMap,
                "the leaves are a Foliage material with a cut-out mask; the bark has albedo and normal maps");
            Check(assets.Tree.BoundingBox.Min.Z > -0.1f && assets.Tree.BoundingBox.Max.Z is > 7 and < 9,
                "the tree stands on its origin (Z up) and is about 8 m tall");
            RenderTree(graphics, content, assets);
        }
        finally { Globals.content = previousContent; }
    }

    private static void RenderTree(GraphicsDevice graphics, ContentManager content, Assets assets)
    {
        var tree = new BasicEntity(assets.Tree, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
        var meshes = new MeshMaterialLibrary(graphics);
        tree.RegisterInLibrary(meshes);
        var entities = new List<BasicEntity> { tree };

        const int Size = 128;
        var cameraPosition = new Vector3(0, -16, 4.2f);
        Matrix view = Matrix.CreateLookAt(cameraPosition, new Vector3(0, 0, 4.2f), Vector3.UnitZ);
        Matrix viewProjection = view * Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 60);
        // Not disposed: its effects are the content manager's cached instances, shared with later checks.
        var gbuffer = new GBufferRenderModule(content, "Shaders/GBufferSetup/ClearGBuffer", "Shaders/GBufferSetup/Gbuffer");
        gbuffer.Initialize(graphics);
        gbuffer.FarClip = 60;
        gbuffer.Camera = cameraPosition;
        using var albedo = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        using var normals = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        using var depth = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Single, DepthFormat.None);

        Vector4[] Render()
        {
            meshes.FrustumCulling(entities, new BoundingFrustum(viewProjection), true, cameraPosition);
            gbuffer.Draw(graphics, new[] { new RenderTargetBinding(albedo), new RenderTargetBinding(normals),
                new RenderTargetBinding(depth) }, meshes, viewProjection, view);
            graphics.SetRenderTarget(null);
            var half = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[Size * Size];
            normals.GetData(half);
            return half.Select(h => h.ToVector4()).ToArray();
        }
        static bool Drawn(Vector4 n) => n.X + n.Y > 0.001f;
        static int MaterialType(Vector4 n) => (int)Math.Truncate(n.Z);

        var still = Render();
        var stillLater = Render();
        var leafPixels = Enumerable.Range(0, still.Length).Where(i => Drawn(still[i]) && MaterialType(still[i]) == 8).ToList();
        var barkPixels = Enumerable.Range(0, still.Length).Where(i => Drawn(still[i]) && MaterialType(still[i]) == 0).ToList();
        Check(leafPixels.Count > Size * Size / 20 && barkPixels.Count > 50 && still.SequenceEqual(stillLater),
            "without Foliage Wind the tree draws still, its leaves flagged as Foliage (type 8) for the sun's translucency");
        // Inside the crown's outline some pixels stay empty: the mask cuts the leaf cards out.
        int top = leafPixels.Min(i => i / Size), bottom = leafPixels.Max(i => i / Size);
        int left = leafPixels.Min(i => i % Size), right = leafPixels.Max(i => i % Size);
        int crownGaps = 0;
        for (int y = top + (bottom - top) / 4; y < bottom - (bottom - top) / 4; y++)
            for (int x = left + (right - left) / 4; x < right - (right - left) / 4; x++)
                if (!Drawn(still[y * Size + x])) crownGaps++;
        Check(crownGaps > 0, "the leaf mask cuts gaps into the crown");

        var wind = new FoliageWindComponent { MinWind = 0 };
        tree.AddComponent(wind);
        FoliageWindComponent.UpdateAll(entities, 0, new Vector2(6, 0));
        wind.State.Clock = 1.0f;
        var gustA = Render();
        wind.State.Clock = 3.4f;
        var gustB = Render();
        int Changed(Vector4[] a, Vector4[] b, int fromRow, int toRow) =>
            Enumerable.Range(fromRow * Size, (toRow - fromRow) * Size).Count(i => a[i] != b[i]);
        int groundRow = barkPixels.Max(i => i / Size);
        Check(Changed(gustA, gustB, 0, Size) > Size * Size / 50, "with Foliage Wind the tree moves over time");
        Check(Changed(gustA, gustB, groundRow - 4, groundRow + 1) * 8 < Changed(gustA, gustB, top, top + 5),
            "the trunk base stays planted while the crown sways");

        wind.LeafFlutter = 0;
        wind.TrunkBend = 0;
        wind.BranchSway = 0;
        FoliageWindComponent.UpdateAll(entities, 0, new Vector2(6, 0));
        wind.State.Clock = 1.0f;
        var calmA = Render();
        wind.State.Clock = 3.4f;
        // Same pose, but projected through world space: allow a few edge pixels of rounding.
        Check(calmA.SequenceEqual(Render()) && Changed(calmA, still, 0, Size) < leafPixels.Count / 50,
            "with every layer at 0 the swaying tree holds still in its rest pose");
        wind.LeafFlutter = 1;
        FoliageWindComponent.UpdateAll(entities, 0, new Vector2(6, 0));
        wind.State.Clock = 1.0f;
        var flutterA = Render();
        wind.State.Clock = 1.25f;
        var flutterB = Render();
        Check(leafPixels.Any(i => flutterA[i] != flutterB[i]) &&
              barkPixels.Where(i => MaterialType(flutterA[i]) == 0 && MaterialType(flutterB[i]) == 0).All(i => flutterA[i] == flutterB[i]),
            "only the Foliage material flutters; the bark holds still without bend and sway");
        wind.TrunkBend = wind.BranchSway = 1;

        // Directional shadows: the leaves cast cut-out shadows that move with the wind.
        var shadows = new ShadowMapRenderModule(content, "Shaders/Shadow/ShadowMap");
        var sun = new DirectionalLight(Color.White, 10, Vector3.Normalize(new Vector3(0.2f, 0.1f, -1)), new Vector3(0, 0, 10),
            castShadows: true, shadowSize: 12, shadowDepth: 40, shadowResolution: 256);
        var camera = new Camera(cameraPosition, new Vector3(0, 0, 4.2f));
        float[] ShadowMap(float clock)
        {
            FoliageWindComponent.UpdateAll(entities, 0, new Vector2(6, 0));
            wind.State.Clock = clock;
            sun.HasChanged = true;
            shadows.Draw(graphics, meshes, entities, new List<PointLight>(), new List<DirectionalLight> { sun }, camera);
            graphics.SetRenderTarget(null);
            var texels = new float[256 * 256];
            sun.ShadowMap.GetData(texels);
            return texels;
        }
        // Texels the tree doesn't cover keep the clear value, the most common one.
        static int Covered(float[] texels)
        {
            float clear = texels.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
            return texels.Count(t => t != clear);
        }
        var shadowA = ShadowMap(1.0f);
        var shadowB = ShadowMap(3.4f);
        int occluded = Covered(shadowA);
        Check(occluded > 2000 && shadowA.Where((t, i) => t != shadowB[i]).Count() > 200,
            "the tree's shadow map follows the wind");
        tree.RemoveComponent(wind);
        var leafMask = assets.TreeLeavesMaterial.Mask;
        float[] masked = ShadowMap(0);
        assets.TreeLeavesMaterial.Mask = null;
        float[] solid = ShadowMap(0);
        assets.TreeLeavesMaterial.Mask = leafMask;
        Check(Covered(masked) < Covered(solid) * 0.9f,
            "the sun's shadow map cuts the leaves out with their mask (fewer texels than solid cards)");
        sun.ShadowMap?.Dispose();
        tree.Dispose(meshes);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
