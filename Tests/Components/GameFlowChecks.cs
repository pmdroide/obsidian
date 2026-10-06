using System.Reflection;
using Anvil.ViewModels;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

/// <summary>
/// The scene list (Content/System/SceneList.json), the MainMenu sample scene, runtime scene
/// switching through GameFlow, and GameInput's menu navigation.
/// </summary>
internal static class GameFlowChecks
{
    public static void Run()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };

        // ---- Scene list ----
        SceneList.Load();
        Check(SceneList.Scenes.Count >= 2 && SceneList.Scenes[0] == "Scenes/MainMenu.obsc" &&
              SceneList.Scenes.All(s => File.Exists(SceneList.ResolvePath(s))),
            "the shipped scene list starts with MainMenu and every entry exists");
        Check(SceneList.Find("godraytest") == SceneList.Current.Scenes.IndexOf("Scenes/GodRayTest.obsc") &&
              SceneList.Find("Scenes/MainMenu.obsc") == 0 && SceneList.Find("NoSuchScene") == -1,
            "scene list entries are found by name (any case) or by Content path");
        string mainMenuFile = SceneList.ResolvePath(SceneList.Scenes[0]);
        Check(SceneList.ToEntry(mainMenuFile) == "Scenes/MainMenu.obsc" && SceneList.IndexOf(mainMenuFile) == 0 &&
              SceneList.ToEntry(Path.Combine(Path.GetTempPath(), "x.obsc")) == null,
            "absolute scene paths map back to their list entry; files outside Content have none");

        // ---- MainMenu sample scene ----
        var menu = SceneSerialization.LoadFromFile(mainMenuFile, assets);
        Check(menu.MainCamera.GetComponent<ScriptBehaviourComponent>() is { ScriptId: MainMenuScript.ScriptId, Enabled: true } &&
              ScriptRegistry.Find(MainMenuScript.ScriptId) != null,
            "the MainMenu scene's camera runs the registered Main Menu script");
        // Materials need a graphics device, so the core's material is read from the file itself.
        var core = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(mainMenuFile))!["Entities"]!.AsArray()
            .Single(e => (string?)e!["Name"] == "Ember Core")!["Material"]!;
        Check(menu.BasicEntities.Count(e => e.Name.StartsWith("Monolith")) == 9 &&
              (int)core["MaterialType"]! == (int)MaterialEffect.MaterialTypes.Emissive && (float)core["EmissiveStrength"]! > 0 &&
              menu.DirectionalLights.Single().CastShadows && menu.Environment.DayNightCycle,
            "the MainMenu scene has the monolith ring, the emissive core and a shadowed sun on the day/night cycle");
        string ui = GameInfo.ResolveContentFile("UI/MainMenu");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the menu's Vista document ships next to the scene");

        // ---- Anvil > Game Settings > Scenes (edited in memory only; Apply would save) ----
        var settings = new GameSettingsViewModel();
        Check(settings.Scenes.Select(s => s.Entry).SequenceEqual(SceneList.Scenes) && settings.Scenes[0].Note == "Loads first",
            "Game Settings lists the scene list in build order");
        settings.SelectedScene = settings.Scenes[0];
        Check(!settings.MoveSceneUpCommand.CanExecute(null) && settings.MoveSceneDownCommand.CanExecute(null),
            "the first scene can only move down");
        settings.MoveSceneDownCommand.Execute(null);
        Check(settings.Scenes[1].Entry == "Scenes/MainMenu.obsc" && settings.Scenes[1].Index == 1 &&
              settings.Scenes[0].Index == 0 && settings.Scenes[0].Note == "Loads first" && settings.SelectedScene == settings.Scenes[1],
            "moving a scene renumbers the list and keeps it selected");
        settings.RemoveSceneCommand.Execute(null);
        Check(settings.Scenes.All(s => s.Entry != "Scenes/MainMenu.obsc") && settings.Scenes.Select(s => s.Index).SequenceEqual(Enumerable.Range(0, settings.Scenes.Count)),
            "removing a scene renumbers the rest");

        // ---- GameFlow: switching scenes during Play ----
        IEditorBridge previousHost = Input.HostBridge;
        var host = new EditorBridge();
        Input.HostBridge = host;
        var logic = new MainSceneLogic();
        try
        {
            Wire(logic, assets);
            var editScene = new Scene { Name = "Edited" };
            var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
            editScene.BasicEntities.Add(entity);
            logic.SceneManager.SetActiveScene(editScene);
            int stopped = 0;
            Action onStopped = () => stopped++;
            GameFlow.PlayStopped += onStopped;

            host.SetHostedByEditor(true);
            logic.PlayMode.Play();
            entity.Position = new Vector3(5, 0, 0); // moved by "gameplay"
            int godRays = SceneList.Find("GodRayTest");
            Check(GameFlow.IsEditor && GameFlow.LoadScene(godRays) && logic.ActiveScene == editScene,
                "LoadScene only queues the switch; the scene changes at the start of the next frame");
            ApplyPending(logic);
            Check(logic.ActiveScene.Name == "GodRayTest" && logic.PlayMode.Mode == GameMode.Play &&
                  GameFlow.ActiveSceneIndex == godRays && GameFlow.ActiveSceneName == "GodRayTest",
                "the queued scene loads and keeps playing");
            Check(GameFlow.LoadScene("AutoExposureTest"), "scenes load by name as well");
            ApplyPending(logic);
            Check(logic.ActiveScene.Name == "AutoExposureTest" && logic.PlayMode.Mode == GameMode.Play && stopped == 0,
                "switching again stays in the same Play session");
            Check(!GameFlow.LoadScene(99) && !GameFlow.LoadScene("NoSuchScene"), "unknown scenes are refused");

            logic.PlayMode.Stop();
            Check(ReferenceEquals(logic.ActiveScene, editScene) && logic.PlayMode.Mode == GameMode.Edit &&
                  entity.Position == Vector3.Zero && stopped == 1,
                "inside Anvil, Stop returns to the edited scene with its transforms rewound");

            // An external load (Anvil's Open Scene) during Play replaces the edited scene for good.
            logic.PlayMode.Play();
            GameFlow.LoadScene(godRays);
            ApplyPending(logic);
            logic.SceneManager.SetActiveScene(SceneSerialization.LoadFromFile(mainMenuFile, assets));
            logic.PlayMode.Stop();
            Check(logic.ActiveScene.Name == "MainMenu", "a scene opened from the editor during Play is not undone by Stop");

            // Standalone: no edited scene to return to.
            host.SetHostedByEditor(false);
            logic.PlayMode.Play();
            GameFlow.LoadNextScene();
            ApplyPending(logic);
            Check(!GameFlow.IsEditor && GameFlow.ActiveSceneIndex == 1 && logic.PlayMode.Mode == GameMode.Play,
                "LoadNextScene moves from scene 0 to scene 1 in the standalone game");
            GameFlow.PlayStopped -= onStopped;
        }
        finally
        {
            logic.PlayMode?.Stop();
            host.SetHostedByEditor(false);
            Input.HostBridge = previousHost;
            GameFlow.SceneLogic = null;
        }

        // ---- GameInput ----
        KeyboardState keyboard = Input.keyboardState;
        try
        {
            Input.keyboardState = new KeyboardState(Keys.Down);
            GameInput.Update(0.016f);
            Check(GameInput.MenuDown && GameInput.AnyInputPressed && GameInput.LastDevice == InputDevice.Keyboard,
                "a key press moves menus and counts as \"any button\"");
            GameInput.Update(0.016f);
            Check(!GameInput.MenuDown && !GameInput.AnyInputPressed, "a held key does not fire again straight away");
            int repeats = 0;
            for (int i = 0; i < 60; i++) // 0.6 s held
            {
                GameInput.Update(0.01f);
                if (GameInput.MenuDown) repeats++;
            }
            Check(repeats >= 2 && repeats <= 4, $"holding a direction repeats after the delay ({repeats} repeats in 0.6 s)");
            Input.keyboardState = new KeyboardState(Keys.Enter);
            GameInput.Update(0.016f);
            Check(GameInput.MenuConfirm && !GameInput.MenuBack, "Enter confirms");
            Input.keyboardState = new KeyboardState(Keys.Escape);
            GameInput.Update(0.016f);
            Check(GameInput.MenuBack && !GameInput.MenuConfirm, "Escape goes back");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(0.016f);
        }
    }

    /// <summary>The parts of MainSceneLogic.Initialize that scene switching needs (no GPU or physics).</summary>
    private static void Wire(MainSceneLogic logic, Assets assets)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(MainSceneLogic).GetField("_assets", flags)!.SetValue(logic, assets);
        logic.SceneManager.Assets = assets;
        logic.SceneManager.SceneChanged += (Action<Scene, Scene>)Delegate.CreateDelegate(
            typeof(Action<Scene, Scene>), logic, typeof(MainSceneLogic).GetMethod("OnSceneChanged", flags)!);
        logic.PlayMode = new PlayModeController(logic);
        logic.PlayMode.ModeChanged += (Action<GameMode>)Delegate.CreateDelegate(
            typeof(Action<GameMode>), logic, typeof(MainSceneLogic).GetMethod("OnPlayModeChanged", flags)!);
        GameFlow.SceneLogic = logic;
    }

    private static void ApplyPending(MainSceneLogic logic) =>
        typeof(MainSceneLogic).GetMethod("ApplyPendingSceneLoad", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(logic, null);

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
