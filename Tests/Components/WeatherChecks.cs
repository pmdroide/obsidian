using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DirectionalLight = Engine.Entities.DirectionalLight;

/// <summary>
/// Weather in Inspector > Environment (rain, sandstorm, snow): settings, profiles, save/load, the bridge and
/// Inspector, the WeatherTest sample scene and its script, and (with --graphics) the GPU particles and haze.
/// </summary>
internal static class WeatherChecks
{
    public static void Run()
    {
        CheckSettings();
        CheckEditor();
        CheckSampleScene();
    }

    private static void CheckSettings()
    {
        var defaults = new EnvironmentSettings();
        Check(defaults.Weather == WeatherType.None && !WeatherProfile.IsActive(defaults),
            "scenes have no weather by default");
        Check(Enum.GetNames<WeatherType>().SequenceEqual(new[] { "None", "Rain", "Sandstorm", "Snow" }),
            "the weather types are None, Rain, Sandstorm and Snow (saved by name)");

        var messy = new EnvironmentSettings
        {
            Weather = (WeatherType)42, WeatherIntensity = 3, WeatherHaze = float.NaN, WindSpeed = -5, WindDirection = -30,
        };
        messy.Normalize();
        Check(messy.Weather == WeatherType.None && messy.WeatherIntensity == 1 && messy.WeatherHaze == 0.6f &&
              messy.WindSpeed == 0 && Math.Abs(messy.WindDirection - 330) < 1e-3f,
            "out-of-range weather settings are clamped, unknown types fall back to None and wind angles wrap");

        var rain = new EnvironmentSettings { Weather = WeatherType.Rain, WeatherIntensity = 0 };
        Check(!WeatherProfile.IsActive(rain), "intensity 0 draws no weather");
        rain.WeatherIntensity = 0.5f;
        rain.WindSpeed = 4;
        rain.WindDirection = 90;
        Vector2 wind = WeatherProfile.Wind(rain);
        Check(WeatherProfile.IsActive(rain) && Math.Abs(wind.X) < 1e-4f && Math.Abs(wind.Y - 4) < 1e-4f,
            "the wind angle is the direction it blows towards (90° = +Y)");
        Vector3 velocity = WeatherProfile.Rain.Velocity(rain);
        Check(velocity.Z < -5 && Math.Abs(velocity.Y - 4) < 1e-4f, "rain falls fast and takes on the wind");
        Check(WeatherProfile.Snow.Velocity(rain).Z > -2 && WeatherProfile.Snow.StreakSeconds == 0 &&
              WeatherProfile.Rain.StreakSeconds > 0 && WeatherProfile.Sandstorm.StreakSeconds > 0,
            "snow drifts down as round flakes; rain and sand are drawn as streaks");

        var sand = new EnvironmentSettings { Weather = WeatherType.Sandstorm, WindSpeed = 0, WindDirection = 0 };
        Check(WeatherProfile.Wind(sand).X >= WeatherProfile.Sandstorm.MinWind - 1e-4f,
            "a sandstorm always blows, even with the wind at 0");
        Check(WeatherProfile.Sandstorm.HazeDistance < WeatherProfile.Snow.HazeDistance &&
              WeatherProfile.Snow.HazeDistance < WeatherProfile.Rain.HazeDistance,
            "a sandstorm hides the most, then snow, then rain");

        var clouds = new EnvironmentSettings { CloudCoverage = 0.2f, Weather = WeatherType.Rain, WeatherIntensity = 1 };
        Check(Math.Abs(WeatherProfile.SkyCloudCoverage(clouds) - WeatherProfile.Rain.CloudCoverage) < 1e-4f &&
              WeatherProfile.SkyCloudCoverage(new EnvironmentSettings { CloudCoverage = 0.2f }) == 0.2f,
            "rain overcasts the day/night sky; no weather leaves the scene's clouds alone");

        // Save/load: the type is written by name, and scenes from before weather load with none.
        var assets = new Assets();
        var logic = new MainSceneLogic();
        logic.ActiveScene.Environment = new EnvironmentSettings
        {
            Weather = WeatherType.Snow, WeatherIntensity = 0.4f, WeatherHaze = 0.2f, WindSpeed = 7, WindDirection = 200,
        };
        string file = Path.Combine(Path.GetTempPath(), "obsidian-weather-" + Guid.NewGuid().ToString("N") + ".obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            Check(json["Environment"]!["Weather"]!.GetValue<string>() == "Snow", "the weather type is saved by name");
            var loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.Weather == WeatherType.Snow && loaded.WeatherIntensity == 0.4f && loaded.WeatherHaze == 0.2f &&
                  loaded.WindSpeed == 7 && loaded.WindDirection == 200, "weather settings survive scene save/load");
            var environment = json["Environment"]!.AsObject();
            foreach (string key in new[] { "Weather", "WeatherIntensity", "WeatherHaze", "WindSpeed", "WindDirection" })
                environment.Remove(key);
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.Weather == WeatherType.None && loaded.WeatherIntensity == 0.7f && loaded.WindDirection == 45,
                "scenes saved before weather load with clear skies and the default weather settings");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static void CheckEditor()
    {
        var logic = new MainSceneLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), new Assets() });
        var vm = new EnvironmentViewModel();
        vm.AttachBridge(bridge);
        Check(vm.WeatherTypes.SequenceEqual(new[] { "None", "Rain", "Sandstorm", "Snow" }) && !vm.HasWeather,
            "the Environment Inspector lists the weather types and hides the weather sliders while it is None");

        vm.SelectedWeather = 2;
        vm.WeatherIntensity = 0.9;
        vm.WeatherHaze = 0.3;
        vm.WindSpeed = 12;
        vm.WindDirection = 270;
        Check(logic.ActiveScene.Environment.Weather == WeatherType.None, "weather edits wait for the game thread");
        Drain(bridge);
        var settings = logic.ActiveScene.Environment;
        Check(vm.HasWeather && settings.Weather == WeatherType.Sandstorm && settings.WeatherIntensity == 0.9f &&
              settings.WeatherHaze == 0.3f && settings.WindSpeed == 12 && settings.WindDirection == 270 &&
              logic.ActiveScene.IsDirty,
            "Inspector weather edits reach the scene and mark it dirty");
        vm.SelectedWeather = -1;
        Drain(bridge);
        Check(logic.ActiveScene.Environment.Weather == WeatherType.Sandstorm, "a cleared ComboBox selection leaves the weather alone");

        bridge.EnqueueMutateEnvironment(s => s.Weather = WeatherType.Snow);
        Drain(bridge);
        typeof(EnvironmentViewModel).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        Check(vm.SelectedWeather == 3 && logic.ActiveScene.Environment.Weather == WeatherType.Snow,
            "the Inspector shows weather set elsewhere without pushing it back");
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "WeatherTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var scripts = scene.MainCamera?.GetComponents<ScriptBehaviourComponent>().Select(s => s.ScriptId).ToList();
        Check(scene.BasicEntities.Count == 58 && scene.PointLights.Count == 4 && scene.DirectionalLights.Count == 1 &&
              scene.BasicEntities.All(e => e.IsEnabled),
            "the weather sample scene loads its village, pond, trees, dunes, markers and street lamps");
        Check(scripts != null && scripts.SequenceEqual(new[] { FreecamScript.ScriptId, WeatherTestScript.ScriptId }),
            "the weather sample's camera runs Freecam and the Weather Test script");
        Check(scene.Environment.Weather == WeatherType.Rain && scene.Environment.DayNightCycle &&
              WeatherProfile.IsActive(scene.Environment),
            "the weather sample starts in rain under the day/night sky");
        Check(new[] { 10, 25, 50, 100, 150 }.All(d => scene.BasicEntities.Any(e => e.Name == $"Marker {d} m")),
            "the weather sample has visibility markers to judge the haze");
        SceneList.Load();
        Check(SceneList.Find("WeatherTest") == SceneList.Scenes.Count - 1, "the weather sample is last in the scene list");
        string ui = GameInfo.ResolveContentFile("UI/WeatherTest");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the weather sample's HUD ships with the scene");

        // ---- Play: switch weathers from the keyboard, then stop ----
        KeyboardState keyboard = Input.keyboardState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool editor = GameSettings.e_enableeditor;
        const float Dt = 1 / 60f;
        try
        {
            GameSettings.e_enableeditor = false;
            var logic = new MainSceneLogic();
            logic.SceneManager.Assets = assets;
            logic.PlayMode = new PlayModeController(logic);
            GameFlow.SceneLogic = logic;
            logic.SceneManager.SetActiveScene(scene);
            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
            void Frames(int count, params Keys[] held)
            {
                Input.keyboardState = new KeyboardState(held);
                for (int i = 0; i < count; i++)
                {
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(frame);
                }
                Input.keyboardState = new KeyboardState();
            }

            logic.PlayMode.Play();
            Frames(70);
            Check(scene.Environment.Weather == WeatherType.Rain && Math.Abs(scene.Environment.WeatherIntensity - 0.7f) < 1e-3f,
                "Play keeps the scene's rain at its intensity");

            Frames(1, Keys.D4);
            Frames(10);
            Check(scene.Environment.Weather == WeatherType.Rain && scene.Environment.WeatherIntensity < 0.7f,
                "pressing 4 fades the rain out first");
            Frames(60);
            Check(scene.Environment.Weather == WeatherType.Snow && scene.Environment.WindSpeed == WeatherTestScript.PresetWind(WeatherType.Snow),
                "then swaps to snow with snow's wind");
            Frames(60);
            Check(Math.Abs(scene.Environment.WeatherIntensity - 0.7f) < 1e-3f, "and fades the snow in to the chosen intensity");

            Frames(30, Keys.Down);
            Check(scene.Environment.WeatherIntensity < 0.6f, "Down lowers the intensity");
            float direction = scene.Environment.WindDirection;
            Frames(30, Keys.Right);
            Check(Math.Abs(scene.Environment.WindDirection - (direction + 30) % 360) < 0.5f, "Right turns the wind");
            Frames(1, Keys.H);
            Check(scene.Environment.WeatherHaze == 1, "H steps the haze");
            Frames(1, Keys.D1);
            Frames(60);
            Check(scene.Environment.Weather == WeatherType.None, "1 clears the sky");
            Frames(1, Keys.D3);
            Frames(1);
            Check(scene.Environment.Weather == WeatherType.Sandstorm && WeatherProfile.Wind(scene.Environment).Length() >= 14 - 1e-3f,
                "from a clear sky, 3 starts the sandstorm at once with a strong wind");

            logic.PlayMode.Stop();
            Check(scene.Environment.Weather == WeatherType.Rain && scene.Environment.WeatherIntensity == 0.7f &&
                  scene.Environment.WindSpeed == 5 && scene.Environment.WindDirection == 60 && scene.Environment.WeatherHaze == 0.6f,
                "Stop puts back the weather the scene was saved with");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(0);
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editor;
        }
    }

    /// <summary>Draws each weather over a cleared HDR target: haze tints the far pixels, particles land on screen.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var effect = content.Load<Effect>("Shaders/Forward/Weather");
        Check(effect.Techniques["Particles"] != null && effect.Techniques["Haze"] != null &&
              new[] { "Offsets", "Box", "Velocity", "HazeAmount", "HazeDensity", "DepthMap" }.All(p => effect.Parameters[p] != null),
            "the weather shader has its particle and haze techniques and parameters");

        const int Size = 96;
        using var module = new WeatherRenderModule(content);
        var triangle = new FullScreenTriangle(graphics);
        using var target = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        using var depth = new RenderTarget2D(graphics, Size, Size, false, SurfaceFormat.Single, DepthFormat.None);
        // Top half is sky (depth 1); bottom half is a wall 20 m away.
        var depthValues = new float[Size * Size];
        for (int i = 0; i < depthValues.Length; i++) depthValues[i] = i < depthValues.Length / 2 ? 1 : 20f / 500f;
        depth.SetData(depthValues);

        var camera = new Camera(new Vector3(0, 0, 2), new Vector3(0, 10, 2));
        Matrix view = Matrix.CreateLookAt(camera.Position, new Vector3(0, 10, 2), Vector3.UnitZ);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 500);
        var sun = new DirectionalLight(Color.White, 100, new Vector3(0, 0.3f, -1), Vector3.UnitZ, false);
        var lights = new List<DirectionalLight> { sun };

        Vector4[] Render(EnvironmentSettings settings)
        {
            graphics.SetRenderTarget(target);
            graphics.Clear(Color.Black);
            for (int i = 0; i < 30; i++) module.Update(settings, 1 / 60.0);
            module.Draw(graphics, settings, camera, view, view * projection, null, lights, depth, 500, triangle);
            graphics.SetRenderTarget(null);
            var half = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[Size * Size];
            target.GetData(half);
            return half.Select(h => h.ToVector4()).ToArray();
        }

        static float Luma(Vector4 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        var none = Render(new EnvironmentSettings());
        Check(none.All(c => c.X == 0 && c.Y == 0 && c.Z == 0) && module.LastParticleCount == 0,
            "with no weather nothing is drawn");

        foreach (WeatherType type in new[] { WeatherType.Rain, WeatherType.Sandstorm, WeatherType.Snow })
        {
            var settings = new EnvironmentSettings { Weather = type, WeatherIntensity = 1, WeatherHaze = 1 };
            var pixels = Render(settings);
            float sky = pixels.Take(Size * Size / 2).Average(Luma);
            float wall = pixels.Skip(Size * Size / 2).Average(Luma);
            Check(module.LastParticleCount == WeatherProfile.For(type).MaxParticles && pixels.All(c => float.IsFinite(Luma(c))) &&
                  sky > 0 && wall > 0,
                $"{type} draws all its particles and haze over both the sky and the wall");
            Check(sky > wall, $"{type}'s haze is thicker over the far sky than over the near wall");

            var particlesOnly = Render(new EnvironmentSettings { Weather = type, WeatherIntensity = 1, WeatherHaze = 0 });
            Check(particlesOnly.Average(Luma) < pixels.Average(Luma) && particlesOnly.Any(c => Luma(c) > 0),
                $"{type} with Haze 0 leaves only the particles");
        }

        var a = new EnvironmentSettings { Weather = WeatherType.Rain, WeatherIntensity = 1, WindSpeed = 0 };
        Vector3 before = module.GroupOffset(0);
        module.Update(a, 0.1);
        Vector3 after = module.GroupOffset(0);
        float fall = before.Z - after.Z;
        if (fall < 0) fall += WeatherProfile.Rain.Box.Z;
        Check(Math.Abs(fall - WeatherProfile.Rain.FallSpeed * WeatherRenderModule.GroupSpeeds[0] * 0.1f) < 1e-3f,
            "particle offsets advance by the fall speed and wrap inside the box");
        triangle.Dispose();
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
