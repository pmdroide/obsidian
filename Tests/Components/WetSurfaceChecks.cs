using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
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

/// <summary>
/// Wet Surface material type: weather wetness, Material component settings, the Inspector, save/load, the
/// WeatherTest ground textures and (with --graphics) what the G-buffer writes for wet and dry surfaces.
/// </summary>
internal static class WetSurfaceChecks
{
    private static readonly string[] GroundSets = { "Bricks", "Cobblestone", "RockyDirt" };
    private static readonly string[] GroundMaps = { "BaseColor", "Normal", "Roughness", "Height" };

    public static void Run()
    {
        CheckWeatherWetness();
        CheckEditorAndSaving();
        CheckSampleScene();
    }

    private static void CheckWeatherWetness()
    {
        Check((int)MaterialEffect.MaterialTypes.WetSurface == 7, "Wet Surface is material type 7 (the Shader list index)");

        var rain = new EnvironmentSettings { Weather = WeatherType.Rain, WeatherIntensity = 0.5f };
        var snow = new EnvironmentSettings { Weather = WeatherType.Snow, WeatherIntensity = 1 };
        var sand = new EnvironmentSettings { Weather = WeatherType.Sandstorm, WeatherIntensity = 1 };
        Check(WeatherProfile.TargetWetness(rain) == 0.5f && WeatherProfile.TargetRipples(rain) == 0.5f,
            "rain soaks wet surfaces and ripples their puddles with its intensity");
        Check(Math.Abs(WeatherProfile.TargetWetness(snow) - 0.3f) < 1e-5f && WeatherProfile.TargetRipples(snow) == 0 &&
              WeatherProfile.TargetWetness(sand) == 0 && WeatherProfile.TargetWetness(new EnvironmentSettings()) == 0 &&
              WeatherProfile.TargetWetness(null) == 0,
            "snow only dampens, sandstorms and clear skies stay dry");

        var wetness = new SurfaceWetness();
        object sceneA = new object(), sceneB = new object();
        wetness.Update(sceneA, rain, 0);
        Check(wetness.Wetness == 0.5f && wetness.Ripples == 0.5f, "a newly shown scene starts as wet as its weather");
        var clear = new EnvironmentSettings();
        wetness.Update(sceneA, clear, 1);
        Check(Math.Abs(wetness.Wetness - (0.5f - 1 / SurfaceWetness.DrySeconds)) < 1e-5f && wetness.Ripples < 0.5f,
            "when the rain stops surfaces dry slowly");
        float held = wetness.Wetness;
        wetness.Update(sceneA, clear, 0);
        Check(wetness.Wetness == held, "paused, the wetness holds");
        var downpour = new EnvironmentSettings { Weather = WeatherType.Rain, WeatherIntensity = 1 };
        wetness.Update(sceneA, downpour, 1);
        Check(Math.Abs(wetness.Wetness - (held + 1 / SurfaceWetness.WetSeconds)) < 1e-5f,
            "rain soaks surfaces faster than they dry");
        wetness.Update(sceneA, downpour, 1000);
        Check(wetness.Wetness == 1 && wetness.Ripples == 1, "wetness settles at the weather's target");
        wetness.Update(sceneB, clear, 0.1);
        Check(wetness.Wetness == 0 && wetness.Ripples == 0, "switching scenes snaps to the new scene's weather");
        wetness.Update(sceneB, downpour, double.NaN);
        Check(wetness.Wetness == 0, "invalid frame times leave the wetness alone");
    }

    private static void CheckEditorAndSaving()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, new Vector3(10, 10, 0.1f)) { Name = "Yard" };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        bridge.EnqueueAddComponent(entity.Id, MaterialComponent.TypeId);
        Drain(bridge);
        var objects = new ObservableCollection<SceneObjectViewModel>();
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var vm = objects.Single(o => o.EngineId == entity.Id).Components.OfType<MaterialInfo>().Single();
        var material = entity.GetComponent<MaterialComponent>();

        Check(material.Wetness == 0 && material.PuddleAmount == 0.5f && material.PuddleSize == 3 && material.WeatherWetness &&
              material.TilingU == 1 && material.TilingV == 1 && !vm.IsWetSurface,
            "materials start dry, untiled and follow the weather once they are Wet Surface");

        vm.WetFloorExampleCommand.Execute(null);
        vm.PuddleAmount = 0.7;
        vm.PuddleSize = 6;
        vm.WeatherWetness = false;
        vm.TilingU = 4;
        vm.TilingV = 8;
        Drain(bridge);
        Check(vm.IsWetSurface && material.MaterialType == MaterialEffect.MaterialTypes.WetSurface && material.Wetness == 1 &&
              material.PuddleAmount == 0.7f && material.PuddleSize == 6 && !material.WeatherWetness &&
              material.TilingU == 4 && material.TilingV == 8,
            "the Wet Floor example and the wet/tiling Inspector fields reach the material component");

        material.Wetness = 0.25f;
        material.TilingU = 2;
        vm.Apply(material, false);
        Check(vm.Wetness == 0.25 && vm.TilingU == 2, "the Inspector shows wet and tiling values set in the engine");

        var clone = ((BasicEntity)entity.Clone).GetComponent<MaterialComponent>();
        Check(clone != material && clone.MaterialType == MaterialEffect.MaterialTypes.WetSurface && clone.PuddleSize == 6 &&
              clone.TilingV == 8, "copies keep their wet surface settings");

        string path = Path.Combine(Path.GetTempPath(), $"anvil-wet-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, path, assets);
            var data = JsonNode.Parse(File.ReadAllText(path))!["Entities"]![0]!["Components"]![0]!["Data"]!.AsObject();
            Check((int)data["MaterialType"]! == 7 && (float)data["PuddleAmount"]! == 0.7f && (float)data["TilingV"]! == 8,
                "scenes save the wet surface and tiling settings");
            var loaded = SceneSerialization.LoadFromFile(path, assets).BasicEntities.Single().GetComponent<MaterialComponent>();
            Check(loaded.MaterialType == MaterialEffect.MaterialTypes.WetSurface && loaded.Wetness == 0.25f &&
                  loaded.PuddleAmount == 0.7f && loaded.PuddleSize == 6 && !loaded.WeatherWetness &&
                  loaded.TilingU == 2 && loaded.TilingV == 8,
                "scene save/load keeps the wet surface and tiling settings");

            foreach (string key in new[] { "Wetness", "PuddleAmount", "PuddleSize", "WeatherWetness", "TilingU", "TilingV" })
                data.Remove(key);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["Entities"]![0]!["Components"]![0]!["Data"] = data.DeepClone();
            File.WriteAllText(path, json.ToJsonString());
            var older = SceneSerialization.LoadFromFile(path, assets).BasicEntities.Single().GetComponent<MaterialComponent>();
            Check(older.TilingU == 1 && older.TilingV == 1 && older.PuddleAmount == 0.5f && older.WeatherWetness,
                "materials saved before wet surfaces load untiled with the default puddles");
        }
        finally { File.Delete(path); }
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        string root = new EditorBridge().ContentSourceRoot;
        var scene = SceneSerialization.LoadFromFile(Path.Combine(root, "Scenes", "WeatherTest.obsc"), assets);
        foreach (var (name, set) in new[] { ("Ground", "RockyDirt"), ("Street", "Bricks"), ("Plaza", "Cobblestone") })
        {
            var material = scene.BasicEntities.Single(e => e.Name == name).GetComponent<MaterialComponent>();
            Check(material.MaterialType == MaterialEffect.MaterialTypes.WetSurface && material.WeatherWetness &&
                  material.BaseColorTexture == $"GameObjects/Textures/Ground/{set}_BaseColor.jpg" &&
                  material.DisplacementTexture == $"GameObjects/Textures/Ground/{set}_Height.jpg" &&
                  material.TilingU > 1 && material.TilingV > 1,
                $"the weather sample's {name} is a tiled, weather-driven wet {set} surface");
        }

        string mgcb = File.ReadAllText(Path.Combine(root, "Content.mgcb"));
        Check(GroundSets.All(set => GroundMaps.All(map =>
                  File.Exists(Path.Combine(root, "GameObjects", "Textures", "Ground", $"{set}_{map}.jpg")) &&
                  mgcb.Contains($"#begin GameObjects/Textures/Ground/{set}_{map}.jpg"))),
            "the brick, cobblestone and rocky dirt textures ship in Content and the content manifest");
    }

    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var gbufferEffect = content.Load<Effect>("Shaders/GBufferSetup/Gbuffer");
        Check(new[] { "World", "View", "TextureTiling", "WetSurface", "WetParams", "Ripples", "Time" }
                  .All(p => gbufferEffect.Parameters[p] != null),
            "the G-buffer shader has the wet surface and tiling parameters");
        Check(content.Load<Effect>("Shaders/Deferred/DeferredEnvironmentMap").Techniques["Basic"] != null,
            "the environment shader with the wet fresnel compiles");

        var model = content.Load<Model>("GameObjects/Default/cube");
        var definition = new ModelDefinition(model, new BoundingBox(-Vector3.One, Vector3.One));
        using var basicEffect = new BasicEffect(graphics);
        using var source = new MaterialEffect(basicEffect);
        source.Initialize(Color.White, 0.9f, 0);
        var component = new MaterialComponent
        {
            MaterialType = MaterialEffect.MaterialTypes.WetSurface,
            Roughness = 0.9f,
            PuddleAmount = 0.6f,
            PuddleSize = 2,
            WeatherWetness = true,
            TilingU = 3,
            TilingV = 3,
            BaseColorTexture = "GameObjects/Textures/Ground/Cobblestone_BaseColor.jpg",
            NormalTexture = "GameObjects/Textures/Ground/Cobblestone_Normal.jpg",
            DisplacementTexture = "GameObjects/Textures/Ground/Cobblestone_Height.jpg",
        };

        using (var applied = source.Clone())
        {
            component.ApplyTo(applied, content);
            Check(applied.Type == MaterialEffect.MaterialTypes.WetSurface && applied.TextureTiling == new Vector2(3, 3) &&
                  applied.PuddleAmount == 0.6f && applied.PuddleSize == 2 && applied.WeatherWetness &&
                  applied.HasDiffuse && applied.HasNormalMap && applied.HasDisplacement,
                "a wet Material component reaches the material with its ground textures and tiling");
            using var copy = applied.Clone();
            Check(copy.TextureTiling == applied.TextureTiling && copy.PuddleSize == 2 && copy.WeatherWetness,
                "material clones keep wet surface and tiling settings");
            var roundTrip = MaterialComponent.FromMaterial(applied);
            Check(roundTrip.TilingU == 3 && roundTrip.PuddleAmount == 0.6f && roundTrip.MaterialType == MaterialEffect.MaterialTypes.WetSurface,
                "wet surface settings round-trip from the material");
            var messy = new MaterialComponent { PuddleSize = -4, TilingU = float.NaN, TilingV = 1e9f, Wetness = 7 };
            messy.ApplyTo(applied, content);
            Check(applied.PuddleSize == MaterialComponent.MinPuddleSize && applied.TextureTiling.X == MaterialComponent.MinTiling &&
                  applied.TextureTiling.Y == MaterialComponent.MaxTiling && applied.Wetness == 1,
                "out-of-range wetness, puddle size and tiling are clamped");
        }

        // Material components load their maps through the global content manager when registered.
        var previousContent = Globals.content;
        Globals.content = content;
        try
        {
            RenderGBuffer(graphics, content, definition, source, component);
        }
        finally { Globals.content = previousContent; }
    }

    private static void RenderGBuffer(GraphicsDevice graphics, ContentManager content, ModelDefinition definition,
        MaterialEffect source, MaterialComponent component)
    {
        var entity = new BasicEntity(definition, source, Vector3.Zero, Matrix.Identity, new Vector3(4, 4, 0.1f)) { IsEnabled = true };
        entity.Components.Add(component);
        var meshes = new MeshMaterialLibrary(graphics);
        entity.RegisterInLibrary(meshes);

        const int Size = 64;
        // Straight down onto the top face, which looks up: puddles can form there.
        var cameraPosition = new Vector3(0, -0.5f, 6);
        Matrix view = Matrix.CreateLookAt(cameraPosition, Vector3.Zero, Vector3.UnitY);
        Matrix viewProjection = view * Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 50);
        // Not disposed: its effects are the content manager's cached instances, shared with later checks.
        var gbuffer = new GBufferRenderModule(content, "Shaders/GBufferSetup/ClearGBuffer", "Shaders/GBufferSetup/Gbuffer");
        gbuffer.Initialize(graphics);
        gbuffer.FarClip = 50;
        gbuffer.Camera = cameraPosition;
        using var albedo = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        using var normals = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        using var depth = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Single, DepthFormat.None);

        (Color[] Albedo, Vector4[] Normal) Render(float sceneWetness, float ripples = 0, float time = 0)
        {
            gbuffer.SceneWetness = sceneWetness;
            gbuffer.SceneRipples = ripples;
            gbuffer.Time = time;
            meshes.FrustumCulling(new List<BasicEntity> { entity }, new BoundingFrustum(viewProjection), true, cameraPosition);
            gbuffer.Draw(graphics, new[] { new RenderTargetBinding(albedo), new RenderTargetBinding(normals),
                new RenderTargetBinding(depth) }, meshes, viewProjection, view);
            graphics.SetRenderTarget(null);
            var colors = new Color[Size * Size];
            albedo.GetData(colors);
            var half = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[Size * Size];
            normals.GetData(half);
            return (colors, half.Select(h => h.ToVector4()).ToArray());
        }

        static IEnumerable<int> Surface(Vector4[] normal) => Enumerable.Range(0, normal.Length).Where(i => normal[i].X + normal[i].Y > 0.001f);
        var dry = Render(0);
        var dryPixels = Surface(dry.Normal).ToList();
        // normal.b = metallic * 0.9 + material type; type 7 flags wet pixels (albedo alpha is the material AO now)
        Check(dryPixels.Count > Size * Size / 2 && dryPixels.All(i => Math.Abs(dry.Normal[i].Z - 7) < 0.01f),
            "wet surfaces flag their pixels (material type 7) for the view-dependent fresnel");
        Check(dryPixels.All(i => Math.Abs(dry.Normal[i].W - 0.9f) < 0.01f),
            "a dry wet surface keeps its own roughness");

        var soaked = Render(1);
        var soakedPixels = Surface(soaked.Normal).ToList();
        double Brightness((Color[] Albedo, Vector4[] Normal) frame, List<int> pixels) =>
            pixels.Average(i => frame.Albedo[i].R + frame.Albedo[i].G + frame.Albedo[i].B);
        Check(Brightness(soaked, soakedPixels) < Brightness(dry, dryPixels) * 0.7,
            "soaked surfaces darken like wet stone");
        int puddles = soakedPixels.Count(i => soaked.Normal[i].W < 0.08f);
        Check(puddles > 0 && puddles < soakedPixels.Count,
            "rain fills puddles with mirror-smooth water between drier patches");
        component.PuddleAmount = 1;
        component.OnChanged(entity);
        var flooded = Render(1);
        Check(Surface(flooded.Normal).Count(i => flooded.Normal[i].W < 0.08f) > puddles,
            "a higher Puddle Amount covers more of the surface");
        Check(soakedPixels.All(i => soaked.Normal[i].W < 0.9f), "the whole soaked surface gets glossier");
        Check(soakedPixels.All(i => Math.Abs(soaked.Normal[i].Z - 7) < 0.01f),
            "wet surfaces stay dielectric and light like a basic material");

        var rippled = Render(1, 1, 0.3f);
        var later = Render(1, 1, 0.9f);
        Check(soakedPixels.Any(i => rippled.Normal[i].X != later.Normal[i].X || rippled.Normal[i].Y != later.Normal[i].Y),
            "raindrop ripples animate the puddle normals");

        component.WeatherWetness = false;
        component.OnChanged(entity);
        var ignoresRain = Render(1);
        Check(Surface(ignoresRain.Normal).All(i => Math.Abs(ignoresRain.Normal[i].W - 0.9f) < 0.01f),
            "a wet surface that does not follow the weather stays at its own wetness");

        var technique = typeof(GBufferRenderModule).GetField("_gbufferShader", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(((Effect)technique.GetValue(gbuffer)!).CurrentTechnique.Name != "DrawTextureDisplacement",
            "wet surfaces use the displacement map as puddle height, without parallax");

        component.MaterialType = MaterialEffect.MaterialTypes.Basic;
        component.DisplacementTexture = ""; // Basic would switch to the parallax technique
        component.OnChanged(entity);
        var basic = Render(1);
        Check(Surface(basic.Normal).All(i => basic.Normal[i].Z < 0.01f && basic.Albedo[i].A == 255 && Math.Abs(basic.Normal[i].W - 0.9f) < 0.01f),
            "other materials ignore the weather and leave the wet flag clear (no AO map: unoccluded)");

        component.TilingU = component.TilingV = 1;
        component.OnChanged(entity);
        var untiled = Render(0);
        Check(Surface(untiled.Normal).Count(i => untiled.Albedo[i] != basic.Albedo[i]) > Size,
            "texture tiling repeats the maps across the mesh");
        entity.Dispose(meshes);
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 6; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
