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
        Check(!logic.ActiveScene.Environment.DayNightCycle, "environment edits wait for the game thread");
        Drain(bridge);
        Check(logic.ActiveScene.Environment.DayNightCycle && logic.ActiveScene.Environment.TimeOfDay == 18 &&
            logic.ActiveScene.Environment.CycleDurationMinutes == 2 && logic.ActiveScene.IsDirty,
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
                loaded.SkyboxPath == logic.ActiveScene.Environment.SkyboxPath, "environment settings survive scene save/load");
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            json.Remove("Environment");
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(!loaded.DayNightCycle && loaded.SkyboxPath == null, "older scenes retain the default sky");
        }
        finally { File.Delete(file); }
        vm.ResetSkyboxCommand.Execute(null);
        Drain(bridge);
        Check(!logic.ActiveScene.Environment.DayNightCycle && logic.ActiveScene.Environment.SkyboxPath == null,
            "reset restores the default sky and disables the cycle");
        bridge.EnqueueMutateEnvironment(s => { s.TimeOfDay = float.NaN; s.CycleDurationMinutes = -1; });
        Drain(bridge);
        Check(logic.ActiveScene.Environment.TimeOfDay == 12 && logic.ActiveScene.Environment.CycleDurationMinutes == 0.1f,
            "invalid cycle settings are normalized");
        Check(Math.Abs(EnvironmentSettings.AdvanceHour(18, 30, 2)) < 0.001 &&
            EnvironmentSettings.SunDirection(12).Z > 0.9 && EnvironmentSettings.SunDirection(0).Z < -0.9,
            "cycle wraps at midnight and the sun follows the Z-up sky");
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
            bridge.EnqueueMutateEnvironment(s => { s.DayNightCycle = true; s.TimeOfDay = 12; });
            Drain(bridge);
            var lights = Apply();
            var noon = Pixel();
            Check(lights.Count == 1 && logic.DirectionalLights.Count == 0, "cycle sun stays outside saved scene lights");
            bridge.EnqueueMutateEnvironment(s => s.TimeOfDay = 0);
            Drain(bridge);
            Apply();
            var midnight = Pixel();
            Check(noon.B > midnight.B + 80, "day/night shader visibly darkens the night sky");
            bridge.EnqueueMutateEnvironment(s => { s.DayNightCycle = false; s.SkyboxPath = null; });
            Drain(bridge);
            Check(Apply().Count == 0 && texture.IsDisposed, "reset removes the cycle sun and disposes the imported texture");
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

        List<Engine.Entities.DirectionalLight> Apply() => (List<Engine.Entities.DirectionalLight>)runtimeType.GetMethod("Apply")!
            .Invoke(runtime, new object[] { logic.ActiveScene, new GameTime(), device, assets, module })!;

        Color Pixel()
        {
            device.SetRenderTargets(target, specular);
            device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque;
            module.DrawSky(device, triangle);
            device.SetRenderTarget(null);
            var pixels = new Color[16];
            target.GetData(pixels);
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
