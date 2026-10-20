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
/// switching through GameFlow (blocking and LoadSceneAsync), and GameInput's menu navigation.
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
        // Materials need a graphics device, so the neon's material is read from the file itself.
        var menuEntities = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(mainMenuFile))!["Entities"]!.AsArray();
        var neon = menuEntities.Single(e => (string?)e!["Name"] == "Neon Tube")!["Material"]!;
        // The skinned player isn't built without a graphics device either.
        var player = menuEntities.Single(e => (string?)e!["Name"] == "Player")!;
        var dance = player["Components"]!.AsArray().Single(c => (string?)c!["Type"] == AnimatorComponent.TypeId)!["Data"]!;
        Check((string?)player["ModelKey"] == "PlayerYBot" && (string?)dance["ClipSource"] == "GameObjects/Player/Silly Dancing" &&
              menu.PointLights.Single(l => l.Name == "Backlight").CastShadows &&
              menu.BasicEntities.Any(e => e.GetComponent<FogVolumeComponent>() != null) &&
              (int)neon["MaterialType"]! == (int)MaterialEffect.MaterialTypes.Emissive &&
              menu.DirectionalLights.Count == 0 && !menu.Environment.DayNightCycle,
            "the MainMenu scene has the dancing player, a shadowed backlight in a fog volume, an emissive neon and no sun");
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

        // ---- Intro videos (Content/System/IntroVideos.json) + Game Settings > Intro Videos ----
        var intros = IntroVideoList.Read().Videos;
        Check(intros.Count > 0 && intros.All(v => File.Exists(IntroVideoList.ResolvePath(v))) &&
              IntroVideoList.ToEntry(IntroVideoList.ResolvePath(intros[0])) == intros[0],
            "the shipped intro video list exists on disk and maps back to its Content entries");
        settings.IntroVideos.Add(new IntroVideoEntryViewModel("Video/NoSuchVideo.mp4", settings.IntroVideos.Count));
        settings.SelectedIntroVideo = settings.IntroVideos[^1];
        Check(settings.IntroVideos[^1].Note == "Missing" && settings.MoveIntroVideoUpCommand.CanExecute(null) &&
              !settings.MoveIntroVideoDownCommand.CanExecute(null),
            "a missing intro video is flagged and the last one can only move up");
        settings.MoveIntroVideoUpCommand.Execute(null);
        Check(settings.IntroVideos[^2].Entry == "Video/NoSuchVideo.mp4" &&
              settings.IntroVideos.Select(v => v.Index).SequenceEqual(Enumerable.Range(0, settings.IntroVideos.Count)),
            "moving an intro video renumbers the play order");
        settings.RemoveIntroVideoCommand.Execute(null);
        Check(settings.IntroVideos.Select(v => v.Entry).SequenceEqual(intros), "removing an intro video restores the list");

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

            // ---- GameFlow.LoadSceneAsync: spread over frames while the running scene plays ----
            string arenaFile = SceneList.ResolvePath(SceneList.Scenes[SceneList.Find("SceneLoadingArena")]);
            int arenaEntities = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(arenaFile))!["Entities"]!.AsArray().Count;
            Scene before = logic.ActiveScene;
            int loadedEvents = 0;
            Action<SceneLoadOperation> onLoaded = _ => loadedEvents++;
            GameFlow.LoadBudgetMs = 0.5f;
            var load = GameFlow.LoadSceneAsync("SceneLoadingArena", loadingScreen: false)!;
            // Subscribed after the request: starting it reported the blocking load before, which was still finishing.
            GameFlow.SceneLoaded += onLoaded;
            Check(load != null && load.Status == SceneLoadStatus.Reading && !load.Blocking && ReferenceEquals(GameFlow.CurrentLoad, load) &&
                  ReferenceEquals(logic.ActiveScene, before),
                "LoadSceneAsync starts reading on a worker thread and leaves the running scene alone");
            int buildingFrames = 0, frames = 0;
            float lastProgress = 0f;
            bool monotonic = true, untouched = true;
            int completed = 0;
            load.Completed += _ => completed++;
            while (!load.IsReady && !load.IsDone && frames++ < 20000)
            {
                Frame(logic);
                if (load.Status == SceneLoadStatus.Reading) Thread.Sleep(1);
                if (load.Status == SceneLoadStatus.Building) buildingFrames++;
                monotonic &= load.Progress >= lastProgress;
                lastProgress = load.Progress;
                untouched &= ReferenceEquals(logic.ActiveScene, before);
            }
            Check(buildingFrames >= 3 && untouched && monotonic && load.ObjectsBuilt == load.ObjectCount && load.ObjectCount > arenaEntities &&
                  load.TotalBytes == new FileInfo(arenaFile).Length && load.BytesRead == load.TotalBytes,
                $"the arena ({load.ObjectCount} objects, {load.TotalBytes / 1024} KB) builds over {buildingFrames} frames with rising progress, the old scene still active");
            Check(load.Status == SceneLoadStatus.WaitingForActivation && Math.Abs(load.Progress - 0.9f) < 1e-4f && load.Scene != null,
                "once built it swaps on a frame of its own, at 90%");
            Frame(logic);
            Check(logic.ActiveScene.Name == "SceneLoadingArena" && logic.ActiveScene.BasicEntities.Count == arenaEntities &&
                  logic.PlayMode.Mode == GameMode.Play && load.Status == SceneLoadStatus.Finishing && load.ActivationMs > 0,
                "the swap puts the built scene in and keeps Play running");
            Check(logic.ActiveScene.BasicEntities[0].Name == "Ground" && logic.ActiveScene.BasicEntities[0].Id == 1 &&
                  logic.ActiveScene.BasicEntities.Select(e => e.Id).Distinct().Count() == arenaEntities,
                "objects keep the IDs saved in the file when built across frames");
            for (int i = 0; i < 10 && !load.IsDone; i++) Frame(logic);
            Check(load.Status == SceneLoadStatus.Done && load.Progress == 1f && ReferenceEquals(GameFlow.LastLoad, load) &&
                  GameFlow.CurrentLoad == null && loadedEvents == 1 && completed == 1 && load.Frames >= buildingFrames && load.LongestFrameMs > 0,
                $"a few frames later it is Done: {load.Seconds:0.000} s, {load.Frames} frames, longest {load.LongestFrameMs:0.0} ms");

            var oneFrame = SceneSerialization.LoadFromFile(arenaFile, assets);
            Check(oneFrame.BasicEntities.Select(e => (e.Id, e.Name, e.Position)).SequenceEqual(logic.ActiveScene.BasicEntities.Select(e => (e.Id, e.Name, e.Position))) &&
                  oneFrame.PointLights.Count == logic.ActiveScene.PointLights.Count && oneFrame.MainCamera.Components.Count == 1,
                "LoadFromFile and the frame-by-frame builder produce the same scene");

            // Held at ready: the scene waits, built, until AllowSceneActivation.
            Scene arena = logic.ActiveScene;
            var held = GameFlow.LoadSceneAsync("SceneLoadingTest", loadingScreen: false)!;
            held.AllowSceneActivation = false;
            for (int i = 0; i < 20000 && !held.IsReady && !held.IsDone; i++) { Frame(logic); Thread.Sleep(held.Status == SceneLoadStatus.Reading ? 1 : 0); }
            for (int i = 0; i < 20; i++) Frame(logic);
            Check(held.Status == SceneLoadStatus.WaitingForActivation && Math.Abs(held.Progress - 0.9f) < 1e-4f && ReferenceEquals(logic.ActiveScene, arena),
                "AllowSceneActivation = false holds a built scene at 90% while the old one keeps running");
            held.AllowSceneActivation = true;
            Frame(logic);
            Check(logic.ActiveScene.Name == "SceneLoadingTest" && held.WaitedSeconds > 0, "setting it swaps on the next frame");
            Check(!held.Cancel() && held.Status == SceneLoadStatus.Finishing, "a load can't be canceled once it swapped");

            // Cancel, replace, Stop.
            Scene hub = logic.ActiveScene;
            var canceled = GameFlow.LoadSceneAsync("SceneLoadingArena", loadingScreen: false)!;
            Check(held.Status == SceneLoadStatus.Done, "a new load finishes the previous one that already swapped");
            int canceledEvents = 0;
            canceled.Completed += op => canceledEvents += op.Status == SceneLoadStatus.Canceled ? 1 : 0;
            Check(GameFlow.CancelLoad() && canceled.Status == SceneLoadStatus.Canceled && canceledEvents == 1, "a load can be canceled while the worker reads it");
            var heldBack = GameFlow.LoadSceneAsync("SceneLoadingArena", loadingScreen: false)!;
            heldBack.AllowSceneActivation = false;
            for (int i = 0; i < 20000 && !heldBack.IsReady && !heldBack.IsDone; i++) { Frame(logic); Thread.Sleep(1); }
            Check(heldBack.Scene != null && heldBack.Cancel() && heldBack.Status == SceneLoadStatus.Canceled, "and once built, until it swaps");
            for (int i = 0; i < 5; i++) Frame(logic);
            Check(ReferenceEquals(logic.ActiveScene, hub) && GameFlow.CurrentLoad == null && ReferenceEquals(GameFlow.LastLoad, held),
                "after a cancel the running scene stays and nothing is reported as loaded");
            var first = GameFlow.LoadSceneAsync("SceneLoadingArena", loadingScreen: false)!;
            var second = GameFlow.LoadSceneAsync("MainMenu", loadingScreen: false)!;
            Check(first.Status == SceneLoadStatus.Canceled && ReferenceEquals(GameFlow.CurrentLoad, second), "a newer load replaces one that hasn't swapped");
            Check(GameFlow.LoadScene("SceneLoadingTest") && second.Status == SceneLoadStatus.Canceled && GameFlow.CurrentLoad!.Blocking,
                "so does a blocking LoadScene");
            Frame(logic);
            Frame(logic);
            Check(GameFlow.CurrentLoad!.Status == SceneLoadStatus.Finishing && logic.ActiveScene.Name == "SceneLoadingTest" &&
                  GameFlow.CurrentLoad.Frames == 1 && GameFlow.CurrentLoad.LongestFrameMs > 0,
                "a blocking load does everything in one frame and measures that frame");
            for (int i = 0; i < 10; i++) Frame(logic);
            Check(GameFlow.LastLoad!.Blocking && GameFlow.LastLoad.Status == SceneLoadStatus.Done, "blocking loads report through LastLoad too");

            string broken = Path.Combine(Path.GetTempPath(), "obsidian-broken-scene.obsc");
            File.WriteAllText(broken, "{ \"Version\": 2, \"Entities\": [ {");
            var failed = GameFlow.LoadSceneAsync(broken, loadingScreen: false)!;
            for (int i = 0; i < 20000 && !failed.IsDone; i++) { Frame(logic); Thread.Sleep(1); }
            File.Delete(broken);
            Check(failed.Status == SceneLoadStatus.Failed && failed.Error!.Contains("Invalid .obsc JSON") && logic.ActiveScene.Name == "SceneLoadingTest",
                "a broken file fails the load with the reason and the running scene carries on");

            var interrupted = GameFlow.LoadSceneAsync("SceneLoadingArena")!;
            Frame(logic);
            logic.PlayMode.Stop();
            Check(interrupted.Status == SceneLoadStatus.Canceled && GameFlow.CurrentLoad == null, "Stop cancels a load in progress");
            GameFlow.SceneLoaded -= onLoaded;
            GameFlow.LoadBudgetMs = 4f;
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

    // A blocking load runs whole in the first frame after the request.
    private static void ApplyPending(MainSceneLogic logic) => Frame(logic);

    /// <summary>The scene-load step at the start of MainSceneLogic.Update (GameFlow.UpdateLoad), without the rest of the frame.</summary>
    internal static void Frame(MainSceneLogic logic)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var activate = (Func<SceneLoadOperation, Scene, bool>)Delegate.CreateDelegate(
            typeof(Func<SceneLoadOperation, Scene, bool>), logic, typeof(MainSceneLogic).GetMethod("ActivateLoadedScene", flags)!);
        var assets = (Assets)typeof(MainSceneLogic).GetField("_assets", flags)!.GetValue(logic)!;
        typeof(GameFlow).GetMethod("UpdateLoad", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { assets, activate });
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
