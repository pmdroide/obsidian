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
using Engine.Renderer.RenderModules.DeferredLighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using DirectionalLight = Engine.Entities.DirectionalLight;

/// <summary>Scene volumetric fog (Environment), Fog Volume components and culling of stretched meshes.</summary>
internal static class FogChecks
{
    public static void Run()
    {
        CheckSceneFog();
        CheckFroxelLightRanking();
        CheckFogVolume();
        CheckStretchedCulling();
    }

    private static void CheckSceneFog()
    {
        var defaults = new EnvironmentSettings();
        Check(defaults.FogDensity == 0.03f && defaults.FogAbsorption == 0.1f && defaults.FogSunScatter == 0.1f &&
              defaults.FogLightScatter == 1.5f && defaults.FogAnisotropy == 0.45f && defaults.FogStartDistance == 30 &&
              defaults.FogFullDistance == 400 && defaults.FogNearDensity == 0 && defaults.FogSkyStrength == 0.1f &&
              defaults.FogSkyShaftDistance == 200 && defaults.MaxAutoExposure == 3 && defaults.FogIndirectScatter == 0.1f,
            "a new scene's fog is the previous global outdoor haze, without a near-camera floor or exposure cap");

        var logic = new MainSceneLogic();
        var assets = new Assets();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var vm = new EnvironmentViewModel();
        vm.AttachBridge(bridge);
        vm.FogDensity = 0.05; vm.FogSunScatter = 0.4; vm.FogStartDistance = 5; vm.FogFullDistance = 50;
        vm.FogNearDensity = 0.5; vm.FogSkyShaftDistance = 80; vm.MaxAutoExposure = 0.5; vm.FogIndirectScatter = 0.6;
        Drain(bridge);
        var fog = logic.ActiveScene.Environment;
        Check(fog.FogDensity == 0.05f && fog.FogSunScatter == 0.4f && fog.FogStartDistance == 5 && fog.FogFullDistance == 50 &&
              fog.FogNearDensity == 0.5f && fog.FogSkyShaftDistance == 80 && fog.MaxAutoExposure == 0.5f &&
              fog.FogIndirectScatter == 0.6f && logic.ActiveScene.IsDirty,
            "Environment > Volumetric Fog edits reach the scene and mark it dirty");

        Engine.Renderer.Renderer.ApplySceneFog(fog);
        Check(GameSettings.g_FroxelDensity == 0.05f && GameSettings.g_FroxelDirectionalScatter == 0.4f &&
              GameSettings.g_FroxelFogDistanceStart == 5 && GameSettings.g_FroxelFogDistanceFull == 50 &&
              GameSettings.g_FroxelNearDensity == 0.5f && GameSettings.g_FroxelSkyShaftDistance == 80 &&
              GameSettings.g_FroxelIndirectScatter == 0.6f,
            "the renderer copies the scene fog into the settings the froxel passes read");

        string file = Path.Combine(Path.GetTempPath(), $"anvil-fog-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.FogDensity == 0.05f && loaded.FogNearDensity == 0.5f && loaded.FogFullDistance == 50 &&
                  loaded.MaxAutoExposure == 0.5f && loaded.FogIndirectScatter == 0.6f, "scene fog survives save/load");
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            json["Environment"]!.AsObject().Remove("FogDensity");
            json["Environment"]!.AsObject().Remove("MaxAutoExposure");
            json["Environment"]!.AsObject().Remove("FogIndirectScatter");
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.FogDensity == 0.03f && loaded.MaxAutoExposure == 3 && loaded.FogIndirectScatter == 0.1f,
                "older scenes get the default fog");
        }
        finally { File.Delete(file); }

        bridge.EnqueueMutateEnvironment(s => { s.FogDensity = float.NaN; s.FogStartDistance = 100; s.FogFullDistance = 20; s.FogNearDensity = 3; s.FogIndirectScatter = -1; });
        Drain(bridge);
        fog = logic.ActiveScene.Environment;
        Check(fog.FogDensity == 0.03f && fog.FogFullDistance == 101 && fog.FogNearDensity == 1 && fog.FogIndirectScatter == 0,
            "invalid fog values are normalized (full distance stays past the start)");
        vm.ResetFogCommand.Execute(null);
        Drain(bridge);
        fog = logic.ActiveScene.Environment;
        Check(fog.FogDensity == 0.03f && fog.FogStartDistance == 30 && fog.FogFullDistance == 400 && fog.MaxAutoExposure == 3 &&
              fog.FogIndirectScatter == 0.1f, "Reset fog restores the default outdoor haze");
    }

    // The fog takes at most 8 point lights: the ones that reach into the view, strongest at the camera first.
    private static void CheckFroxelLightRanking()
    {
        Matrix view = Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ);
        var frustum = new BoundingFrustum(view * Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 16 / 9f, 1, 500));
        PointLight Light(float x, float radius, float intensity, bool enabled = true) =>
            new PointLight(new Vector3(x, 0, 0), radius, Color.White, intensity, false, false, 256, 0, false, 1, enabled);
        var farBright = Light(100, 5, 10);
        var nearDim = Light(3, 5, 1);
        var disabled = Light(4, 5, 50, enabled: false);
        var behind = Light(-40, 5, 50);
        var twinA = Light(20, 5, 2);
        var twinB = Light(20, 5, 2);
        var lights = new List<PointLight> { farBright, twinB, disabled, behind, nearDim, twinA };
        var ranked = new List<PointLight>();

        FroxelRenderModule.RankPointLights(lights, Vector3.Zero, frustum, ranked);
        Check(ranked.SequenceEqual(new[] { nearDim, twinB, twinA, farBright }),
            "fog lights are ranked by brightness reaching the camera; disabled and out-of-view lights are left out; ties keep the scene order");
        FroxelRenderModule.RankPointLights(lights, Vector3.Zero, frustum, ranked, maxCount: 2);
        Check(ranked.SequenceEqual(new[] { nearDim, twinB }), "only the strongest lights fill the fog's light slots");
        var many = Enumerable.Range(0, 12).Select(i => Light(10 + i, 5, 1)).ToList();
        FroxelRenderModule.RankPointLights(many, Vector3.Zero, frustum, ranked);
        Check(ranked.Count == FroxelRenderModule.MAX_FROXEL_POINT_LIGHTS && ranked[0] == many[0],
            "at most 8 point lights reach the fog shader");
    }

    /// <summary>
    /// Renders the froxel passes for a uniform sunlit medium and compares the integrated fog with the exact solution:
    /// optical depth sigmaT * s and in-scattering q / sigmaT * (1 - T) over the ray length s to the far end of
    /// each view-Z slice, at the centre and at the corners of the screen. Then the history lifetime rules.
    /// </summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var compose = content.Load<Effect>("Shaders/Deferred/DeferredCompose");
        Check(compose.Parameters["FroxelAccumulationTexture"] != null && compose.Parameters["FroxelInjectionTexture"] != null &&
              compose.Parameters["InverseProjection"] != null,
            "DeferredCompose samples the fog prefixes and the medium of the partial slice along the view ray");

        const float Near = FroxelRenderModule.CameraNearPlane, Far = 200, Density = 0.02f;
        float density = GameSettings.g_FroxelDensity, absorption = GameSettings.g_FroxelAbsorption,
            sun = GameSettings.g_FroxelDirectionalScatter, near = GameSettings.g_FroxelNearDensity,
            anisotropy = GameSettings.g_FroxelAnisotropy, history = GameSettings.g_FroxelHistoryBlend;
        var module = new FroxelRenderModule(new ShaderManager(content, graphics), "Shaders/Deferred/Froxel");
        using var noise = new Texture2D(graphics, 1, 1);
        try
        {
            GameSettings.g_FroxelDensity = Density;
            GameSettings.g_FroxelAbsorption = 1;
            GameSettings.g_FroxelDirectionalScatter = 0.5f;
            GameSettings.g_FroxelNearDensity = 1; // no distance ramp: the same medium everywhere
            GameSettings.g_FroxelAnisotropy = 0.3f;
            GameSettings.g_FroxelHistoryBlend = 0.7f;
            noise.SetData(new[] { new Color(128, 128, 128) }); // (almost) no jitter
            module.Initialize(graphics, new FullScreenTriangle(graphics), 1600, 900);
            module.SetNoiseMap(noise);
            module.SetFogVolumes(new List<BasicEntity>());
            module.SetProbeVolume(null, 1);

            // Unshadowed sun: a tiny orthographic light box away from the frustum leaves every froxel lit. Bright enough
            // that the first slices' in-scattering is above the half-float denormal range (6e-5), where precision drops.
            var light = new DirectionalLight(Color.White, 50, new Vector3(0.3f, 0.2f, -1))
            {
                LightView = Matrix.Identity,
                LightViewProjection = Matrix.CreateOrthographic(0.001f, 0.001f, 0, 1) * Matrix.CreateTranslation(50, 50, 0),
            };
            var suns = new List<DirectionalLight> { light };
            Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 16 / 9f, Near, Far);
            Matrix inverseProjection = Matrix.Invert(projection);
            Matrix View(Vector3 position, Vector3 forward) => Matrix.CreateLookAt(position, position + forward, Vector3.UnitZ);

            void Render(Vector3 position, Vector3 forward)
            {
                Matrix view = View(position, forward);
                module.UpdateMatrices(view, projection, Matrix.Invert(view), inverseProjection, Near, Far);
                module.RenderFroxelFog(new List<PointLight>(), suns, position, 1600, 900, 0);
            }

            void CompareWithExact(Matrix view, string description)
            {
                var atlas = new HalfVector4[FroxelRenderModule.AtlasWidth * FroxelRenderModule.AtlasHeight];
                ((RenderTarget2D)module.FroxelAccumulation).GetData(atlas);
                Vector3 lightVS = Vector3.Normalize(Vector3.TransformNormal(light.Direction, view));
                double worst = 0;
                foreach (var (fx, fy) in new[] { (80, 45), (0, 0), (159, 89), (5, 80) })
                {
                    float u = (fx + 0.5f) / FroxelRenderModule.FROXEL_GRID_X, v = (fy + 0.5f) / FroxelRenderModule.FROXEL_GRID_Y;
                    Vector4 h = Vector4.Transform(new Vector4(u * 2 - 1, 1 - v * 2, 1, 1), inverseProjection);
                    Vector3 ray = Vector3.Normalize(new Vector3(h.X, h.Y, h.Z) / h.W);
                    double q = light.ColorV3.Y * light.Intensity * 0.1 * HenyeyGreenstein(Vector3.Dot(lightVS, -ray), 0.3f) * Density * 0.5;
                    foreach (int k in new[] { 0, 9, 63, 127 })
                    {
                        double boundary = Near * Math.Pow(Far / Near, (k + 1) / (double)FroxelRenderModule.FROXEL_GRID_Z);
                        double s = (boundary - Near) / -ray.Z;
                        double tau = Density * s;
                        double c = q / Density * (1 - Math.Exp(-tau));
                        Vector4 texel = atlas[(k * FroxelRenderModule.FROXEL_GRID_Y + fy) * FroxelRenderModule.FROXEL_GRID_X + fx].ToVector4();
                        // Alpha is optical depth
                        worst = Math.Max(worst, Math.Max(Math.Abs(texel.W - tau) / tau, Math.Abs(texel.Y - c) / c));
                    }
                }
                Check(worst < 0.01, $"{description} (worst relative error {worst:0.0000})");
            }

            Render(Vector3.Zero, Vector3.UnitX);
            Check(!module.UsedHistory && module.HasHistory, "the first fog frame has no history to blend");
            CompareWithExact(View(Vector3.Zero, Vector3.UnitX),
                "the fog transport matches the exact integral of a uniform medium, at the screen centre and its corners");
            Render(Vector3.Zero, Vector3.UnitX);
            Check(module.UsedHistory, "the next frame blends the reprojected history");
            var turned = Vector3.Normalize(new Vector3(1, 0.1f, 0.05f));
            Render(Vector3.Zero, turned);
            CompareWithExact(View(Vector3.Zero, turned), "after turning the camera the reprojected history keeps the medium exact");

            GameSettings.g_FroxelDensity = Density * 2;
            Render(Vector3.Zero, turned);
            Check(!module.UsedHistory, "changing the fog settings drops the history");
            GameSettings.g_FroxelDensity = Density;
            Render(Vector3.Zero, turned);
            Render(new Vector3(25, 0, 0), turned);
            Check(!module.UsedHistory, "a camera cut (a jump of more than 10 m) drops the history");
            Render(new Vector3(26, 0, 0), turned);
            Check(module.UsedHistory, "walking keeps the history");
            module.InvalidateHistory();
            Render(new Vector3(26, 0, 0), turned);
            Check(!module.UsedHistory, "InvalidateHistory (scene swap, fog switched off) drops the history");
            Render(new Vector3(26, 0, 0), turned);
            projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(70), 16 / 9f, Near, Far);
            inverseProjection = Matrix.Invert(projection);
            Render(new Vector3(26, 0, 0), turned);
            Check(!module.UsedHistory, "a new projection (FOV) drops the history");
            CompareWithExact(View(new Vector3(26, 0, 0), turned), "the transport stays exact with the new projection");
        }
        finally
        {
            module.Dispose();
            GameSettings.g_FroxelDensity = density;
            GameSettings.g_FroxelAbsorption = absorption;
            GameSettings.g_FroxelDirectionalScatter = sun;
            GameSettings.g_FroxelNearDensity = near;
            GameSettings.g_FroxelAnisotropy = anisotropy;
            GameSettings.g_FroxelHistoryBlend = history;
        }
    }

    // Same as HenyeyGreenstein in Froxel.fx (floored denominator, capped peak).
    private static double HenyeyGreenstein(double cosTheta, double g)
    {
        double denominator = Math.Max(1 + g * g - 2 * g * cosTheta, 0.05);
        return Math.Min(1 / (4 * Math.PI) * (1 - g * g) / Math.Pow(denominator, 1.5), 4);
    }

    private static void CheckFogVolume()
    {
        Check(ComponentRegistry.Find(FogVolumeComponent.TypeId)?.ComponentType == typeof(FogVolumeComponent) &&
              ComponentEditorRegistry.Supports(FogVolumeComponent.TypeId),
            "Fog Volume is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var room = new BasicEntity(assets.Cube, null, new Vector3(10, 0, 5), Matrix.Identity, new Vector3(4, 6, 5))
            { Name = "Dust", IsEnabled = true };
        logic.BasicEntities.Add(room);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Fog Volume").AddCommand.Execute(null);
        Publish();
        var volume = room.GetComponent<FogVolumeComponent>();
        var editor = owner.Components.OfType<FogVolumeComponentViewModel>().Single();
        Check(volume is { Density: 0.15f, EdgeFade: 0.1f } && room.WorldTransform.Hidden && editor.Density == 0.15,
            "a new Fog Volume adds 0.15 density with a soft edge and hides its box mesh");

        editor.Density = 0.4;
        editor.EdgeFade = 0;
        Publish();
        Check(volume.Density == 0.4f && volume.EdgeFade == 0, "inspector edits reach the engine");
        Check(volume.DensityAt(room, new Vector3(10, 0, 5)) == 0.4f && volume.DensityAt(room, new Vector3(13.9f, 5.9f, 9.9f)) == 0.4f &&
              volume.DensityAt(room, new Vector3(14.1f, 0, 5)) == 0, "the box is the gameobject's position and scale (a Cube)");
        volume.EdgeFade = 0.5f;
        Check(Math.Abs(volume.DensityAt(room, new Vector3(13, 0, 5)) - 0.4f * 0.25f / 0.5f) < 1e-4f,
            "Edge Fade ramps the density in from the faces");

        var other = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Off", IsEnabled = true };
        other.AddComponent(new FogVolumeComponent { Enabled = false });
        var plain = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
        var worldToBox = new Matrix[FogVolumeComponent.MaxVolumes];
        var settings = new Vector4[FogVolumeComponent.MaxVolumes];
        room.WorldTransform.Hidden = false; // as after a scene load, which skips the component hooks
        int count = FogVolumeComponent.Collect(new List<BasicEntity> { plain, other, room }, worldToBox, settings);
        Check(count == 1 && settings[0] == new Vector4(0.4f, 0.5f, 0, 0) &&
              Vector3.Distance(Vector3.Transform(new Vector3(14, 6, 10), worldToBox[0]), Vector3.One) < 1e-4f &&
              room.WorldTransform.Hidden && !other.WorldTransform.Hidden && !plain.WorldTransform.Hidden,
            "the renderer collects enabled volumes as world-to-box matrices and keeps their meshes hidden");
        var many = Enumerable.Range(0, 12).Select(_ =>
        {
            var e = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
            e.AddComponent(new FogVolumeComponent());
            return e;
        }).ToList();
        Check(FogVolumeComponent.Collect(many, worldToBox, settings) == FogVolumeComponent.MaxVolumes,
            "at most 8 volumes are sent to the shader");

        editor.Enabled = false;
        Publish();
        Check(!volume.Enabled && !room.WorldTransform.Hidden, "disabling the volume shows its mesh again");
        editor.Enabled = true;
        Publish();
        var record = ComponentRegistry.Capture(volume);
        Check(!record.Data.TryGetProperty(nameof(FogVolumeComponent.ClampedDensity), out _) &&
              ComponentRegistry.Restore(record) is FogVolumeComponent { Density: 0.4f, EdgeFade: 0.5f },
            "saved Fog Volume records round-trip");
        var copied = ((BasicEntity)room.Clone).GetComponent<FogVolumeComponent>();
        copied.Density = 2;
        Check(!ReferenceEquals(copied, volume) && volume.Density == 0.4f, "cloning copies the volume independently");
        editor.RemoveCommand.Execute(null);
        Publish();
        Check(room.GetComponent<FogVolumeComponent>() == null && !room.WorldTransform.Hidden,
            "removing the component shows the mesh again");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    // A 6 x 120 m roof slat whose centre is off screen but whose end is in view must still draw (and cast shadows).
    private static void CheckStretchedCulling()
    {
        var library = new MeshLibrary { MeshBoundingSphere = new BoundingSphere(Vector3.Zero, (float)Math.Sqrt(3)) };
        var slat = new TransformMatrix(Matrix.CreateScale(3, 60, 0.5f), 1);
        library.Register(slat);
        Matrix view = Matrix.CreateLookAt(new Vector3(0, 55, 20), new Vector3(0, 55, 0), Vector3.UnitY);
        var frustum = new BoundingFrustum(view * Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(20), 1, 0.1f, 100));
        library.UpdatePositionAndCheckRender(true, frustum, new Vector3(0, 55, 20), new BoundingSphere());
        Check(library.Rendered[0], "culling sizes the bounds by the largest axis scale, not Scale.X");
        var away = new BoundingFrustum(Matrix.CreateLookAt(new Vector3(0, 200, 20), new Vector3(0, 200, 0), Vector3.UnitY) *
            Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(20), 1, 0.1f, 100));
        library.UpdatePositionAndCheckRender(true, away, new Vector3(0, 200, 20), new BoundingSphere());
        Check(!library.Rendered[0], "a mesh far outside the view is still culled");
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 4; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
