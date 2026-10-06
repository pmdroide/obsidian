using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Anvil.ViewModels;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

internal static class EnvironmentChecks
{
    public static void Run()
    {
        var logic = new MainSceneLogic();
        var assets = new Assets();
        var bridge = Bind(logic, assets);
        var vm = new EnvironmentViewModel();
        vm.AttachBridge(bridge);
        vm.SelectedSkyMode = 1;
        vm.TimeOfDay = 18;
        vm.CycleDurationMinutes = 2;
        vm.CloudCoverage = 0.8;
        vm.CloudSpeed = 3;
        vm.DaySkyColor = Avalonia.Media.Color.FromRgb(10, 20, 30);
        vm.MoonBrightness = 2;
        vm.NightExposure = -3;
        Check(!logic.ActiveScene.Environment.DayNightCycle, "environment edits wait for the game thread");
        Drain(bridge);
        Check(logic.ActiveScene.Environment.DayNightCycle && logic.ActiveScene.Environment.TimeOfDay == 18 &&
            logic.ActiveScene.Environment.CycleDurationMinutes == 2 && logic.ActiveScene.Environment.CloudCoverage == 0.8f &&
            logic.ActiveScene.Environment.CloudSpeed == 3 && logic.ActiveScene.Environment.DaySkyColor == new Color(10, 20, 30) &&
            logic.ActiveScene.Environment.MoonBrightness == 2 && logic.ActiveScene.Environment.NightExposure == -3 &&
            logic.ActiveScene.IsDirty,
            "environment controls update scene settings and mark the scene dirty");
        var copy = bridge.GetEnvironmentSettings();
        copy.TimeOfDay = 4;
        Check(logic.ActiveScene.Environment.TimeOfDay == 18, "environment reads are independent copies");
        bridge.EnqueueMutateEnvironment(s => s.SkyboxPath = "Environment/Skyboxes/example/sky.png");
        Drain(bridge);
        string file = Path.Combine(Path.GetTempPath(), $"anvil-environment-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.DayNightCycle && loaded.TimeOfDay == 18 && loaded.CycleDurationMinutes == 2 &&
                loaded.CloudCoverage == 0.8f && loaded.CloudSpeed == 3 && loaded.DaySkyColor == new Color(10, 20, 30) &&
                loaded.MoonBrightness == 2 && loaded.NightExposure == -3 &&
                loaded.SkyboxPath == logic.ActiveScene.Environment.SkyboxPath, "environment settings survive scene save/load");
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            json.Remove("Environment");
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            var defaults = new EnvironmentSettings();
            Check(!loaded.DayNightCycle && loaded.SkyboxPath == null && loaded.CloudCoverage == 0.45f && loaded.CloudSpeed == 1 &&
                loaded.DaySkyColor == defaults.DaySkyColor && loaded.SunBrightness == 1 && loaded.DayExposure == -1 &&
                loaded.NightExposure == -2.5f,
                "older scenes retain the default sky and clouds");
        }
        finally { File.Delete(file); }
        vm.ResetSkyLookCommand.Execute(null);
        Drain(bridge);
        Check(logic.ActiveScene.Environment.DaySkyColor == new EnvironmentSettings().DaySkyColor &&
            logic.ActiveScene.Environment.MoonBrightness == 1 && logic.ActiveScene.Environment.NightExposure == -2.5f &&
            logic.ActiveScene.Environment.CloudCoverage == 0.8f && vm.MoonBrightness == 1,
            "reset sky look restores colours, sun/moon and exposure but keeps clouds");
        vm.ResetSkyboxCommand.Execute(null);
        Drain(bridge);
        Check(!logic.ActiveScene.Environment.DayNightCycle && logic.ActiveScene.Environment.SkyboxPath == null,
            "reset restores the default sky and disables the cycle");
        bridge.EnqueueMutateEnvironment(s =>
        {
            s.TimeOfDay = float.NaN; s.CycleDurationMinutes = -1; s.CloudCoverage = float.NaN; s.CloudSpeed = -1;
            s.SunSize = 100; s.MoonBrightness = float.NaN; s.DayExposure = -10;
        });
        Drain(bridge);
        Check(logic.ActiveScene.Environment.TimeOfDay == 12 && logic.ActiveScene.Environment.CycleDurationMinutes == 0.1f &&
            logic.ActiveScene.Environment.CloudCoverage == 0.45f && logic.ActiveScene.Environment.CloudSpeed == 0 &&
            logic.ActiveScene.Environment.SunSize == 4 && logic.ActiveScene.Environment.MoonBrightness == 1 &&
            logic.ActiveScene.Environment.DayExposure == -4,
            "invalid cycle settings are normalized");
        Check(Math.Abs(EnvironmentSettings.AdvanceHour(18, 30, 2)) < 0.001 &&
            EnvironmentSettings.SunDirection(12).Z > 0.9 && EnvironmentSettings.SunDirection(0).Z < -0.9,
            "cycle wraps at midnight and the sun follows the Z-up sky");
        Check(EnvironmentSettings.MoonDirection(0).Z > 0.9 && EnvironmentSettings.MoonDirection(12).Z < -0.9,
            "the moon rises opposite the sun");
        var main = new MainWindowViewModel();
        main.SetInspectorViewCommand.Execute("Environment");
        Check(main.IsInspectorEnvironmentView && !main.IsInspectorSelectionView && !main.IsInspectorLightingView,
            "Environment button opens the Environment Inspector section");
    }

    public static void RunGraphics(GraphicsDevice device, ContentManager content)
    {
        var previousContent = Globals.content;
        Globals.content = content;
        var logic = new MainSceneLogic();
        logic.EnvironmentSample = new EnvironmentSample(Vector3.Zero);
        var assets = new Assets();
        var bridge = Bind(logic, assets);
        using var module = new DeferredEnvironmentMapRenderModule(content, "Shaders/Deferred/DeferredEnvironmentMap");
        using var normal = new Texture2D(device, 1, 1);
        normal.SetData(new[] { Color.Black });
        module.NormalMap = normal;
        module.Resolution = new Vector2(4, 4);
        module.FrustumCornersWS = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        module.DiffuseStrength = 1;
        module.SpecularStrength = 1;
        using var target = new RenderTarget2D(device, 4, 4);
        using var specular = new RenderTarget2D(device, 4, 4);
        var triangle = new FullScreenTriangle(device);
        var runtimeType = typeof(Engine.Renderer.Renderer).Assembly.GetType("Engine.Renderer.EnvironmentSky")!;
        using var runtime = (IDisposable)Activator.CreateInstance(runtimeType, nonPublic: true)!;
        string fixture = Path.Combine(Path.GetTempPath(), $"anvil-sky-{Guid.NewGuid():N}.png");
        string? imported = null;
        try
        {
            using (var image = new Texture2D(device, 4, 2))
            {
                image.SetData(Enumerable.Repeat(Color.Red, 8).ToArray());
                using var stream = File.Create(fixture);
                image.SaveAsPng(stream, 4, 2);
            }
            string? error = "No callback";
            bridge.EnqueueImportSkybox(fixture, result => error = result);
            Drain(bridge);
            Check(error == null, "custom panorama imports without rebuilding content");
            imported = bridge.GetEnvironmentSettings().SkyboxPath;
            Apply();
            var texture = (Texture2D)runtimeType.GetField("_customSky", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
            Check(Pixel().R > 240 && Pixel().G < 5, "custom skybox renders the imported panorama");
            bridge.EnqueueMutateEnvironment(s => { s.DayNightCycle = true; s.TimeOfDay = 12; s.CloudCoverage = 0; });
            Drain(bridge);
            var lights = Apply();
            var noon = Pixel();
            Check(lights.Count == 1 && logic.DirectionalLights.Count == 0, "cycle sun stays outside saved scene lights");
            bridge.EnqueueMutateEnvironment(s => s.TimeOfDay = 0);
            Drain(bridge);
            Apply();
            var midnight = Pixel();
            Check(noon.B > midnight.B + 80, "day/night shader visibly darkens the night sky");
            var sceneSun = new Engine.Entities.DirectionalLight(Color.White, 100, new Vector3(0.2f, 0.2f, -1),
                Vector3.UnitZ * 2, castShadows: true, shadowSize: 450);
            logic.DirectionalLights.Add(sceneSun);
            lights = Apply();
            var cycleLight = lights.Count == 1 ? lights[0] : null;
            float ambient = (float)runtimeType.GetProperty("AmbientScale")!.GetValue(runtime)!;
            Check(cycleLight != null && cycleLight != sceneSun && cycleLight.CastShadows && cycleLight.ShadowSize == 450 &&
                cycleLight.Intensity > 0 && cycleLight.Intensity <= 10 && cycleLight.Color.B > cycleLight.Color.R && cycleLight.Direction.Z < 0 &&
                ambient < 0.1f && sceneSun.Intensity == 100,
                "night replaces the scene sun with dim, cool, shadowed moonlight and dims baked ambient");
            float Exposure() => (float)runtimeType.GetProperty("ExposureOffset")!.GetValue(runtime)!;
            Check(Math.Abs(Exposure() - logic.ActiveScene.Environment.NightExposure) < 0.001f, "night uses the night exposure");
            float moonlight = cycleLight!.Intensity;
            bridge.EnqueueMutateEnvironment(s => s.MoonBrightness = 2);
            Drain(bridge);
            lights = Apply();
            Check(Math.Abs(lights[0].Intensity - moonlight * 2) < 0.01f, "moon brightness scales the moonlight");
            bridge.EnqueueMutateEnvironment(s => s.MoonBrightness = 1);
            Drain(bridge);
            bridge.EnqueueMutateEnvironment(s => s.TimeOfDay = 12);
            Drain(bridge);
            lights = Apply();
            ambient = (float)runtimeType.GetProperty("AmbientScale")!.GetValue(runtime)!;
            Check(lights.Count == 1 && lights[0].Intensity > 90 && ambient == 1,
                "noon sun takes the scene sun's intensity");
            Check(Math.Abs(Exposure() - logic.ActiveScene.Environment.DayExposure) < 0.001f, "noon uses the day exposure");
            var plainNoon = Pixel();
            bridge.EnqueueMutateEnvironment(s => s.DaySkyColor = new Color(255, 0, 0));
            Drain(bridge);
            Apply();
            var redNoon = Pixel();
            Check(redNoon.R > plainNoon.R + 40 && redNoon.B < plainNoon.B, "day sky colour drives the procedural sky");
            bridge.EnqueueMutateEnvironment(s => s.DaySkyColor = new EnvironmentSettings().DaySkyColor);
            Drain(bridge);
            Apply();
            logic.DirectionalLights.Remove(sceneSun);
            var clear = Pixel();
            bridge.EnqueueMutateEnvironment(s => { s.CloudCoverage = 1; s.CloudSpeed = 2; });
            Drain(bridge);
            lights = Apply(5);
            var overcast = Pixel();
            var elapsed = (double)runtimeType.GetField("_elapsed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
            var drift = (double)runtimeType.GetField("_cloudOffsetX", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
            Check(Math.Abs(elapsed - 5) < 0.001 && drift > 0, "cloud edits keep the cycle running and clouds drift with time");
            Check(overcast.R > clear.R + 20 && overcast != clear, "overcast clouds cover the blue noon sky");
            Check(lights[0].Intensity > 45 && lights[0].Intensity < 55, "full overcast halves direct sunlight");
            bridge.EnqueueMutateEnvironment(s => s.TimeOfDay = 0);
            Drain(bridge);
            Apply();
            var cloudyNight = Pixel();
            Check(cloudyNight.R < 30 && cloudyNight.G < 30 && cloudyNight.B < 40 && cloudyNight.B >= cloudyNight.R,
                "night clouds stay dark and cool");
            bridge.EnqueueMutateEnvironment(s => { s.DayNightCycle = false; s.SkyboxPath = null; });
            Drain(bridge);
            Check(Apply().Count == 0 && texture.IsDisposed && (float)runtimeType.GetProperty("ExposureOffset")!.GetValue(runtime)! == 0,
                "reset removes the cycle sun, its exposure offset and disposes the imported texture");
            error = null;
            bridge.EnqueueImportSkybox(fixture + ".hdr", result => error = result);
            Drain(bridge);
            Check(error != null && bridge.GetEnvironmentSettings().SkyboxPath == null,
                "unsupported skybox files report failure without changing the scene");
        }
        finally
        {
            device.SetRenderTarget(null);
            triangle.Dispose();
            Globals.content = previousContent;
            File.Delete(fixture);
            if (imported != null)
                foreach (string root in new[] { content.RootDirectory, bridge.ContentSourceRoot }.Distinct())
                {
                    string path = Path.Combine(root, imported);
                    File.Delete(path);
                    Directory.Delete(Path.GetDirectoryName(path)!);
                }
        }

        List<Engine.Entities.DirectionalLight> Apply(double seconds = 0) => (List<Engine.Entities.DirectionalLight>)runtimeType.GetMethod("Apply")!
            .Invoke(runtime, new object[] { logic.ActiveScene, new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)), device, assets, module })!;

        Color Pixel()
        {
            device.SetRenderTargets(target, specular);
            device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque;
            module.DrawSky(device, triangle);
            device.SetRenderTarget(null);
            var pixels = new Color[16];
            specular.GetData(pixels);
            return pixels[5];
        }
    }

    private static EditorBridge Bind(MainSceneLogic logic, Assets assets)
    {
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        return bridge;
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
