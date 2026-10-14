using System;
using Engine.Editor;
using Engine.Recources;

namespace Engine.Logic
{
    /// <summary>
    /// Runtime game flow for scripts: the build scene list (<see cref="SceneList"/>), switching
    /// scenes during Play, and quitting.
    ///
    /// Scene loads are queued and start at the beginning of the next frame, so a script can call
    /// <see cref="LoadScene(int, bool)"/> from its own Update. <see cref="LoadScene(int, bool)"/> does the whole
    /// load in that frame; <see cref="LoadSceneAsync(int, bool, bool)"/> spreads it over frames behind a loading
    /// screen while the running scene keeps playing. In Play the new scene starts playing straight away. Inside
    /// Anvil, pressing Stop returns to the scene you were editing.
    /// </summary>
    public static class GameFlow
    {
        // Bound by MainSceneLogic.Initialize / Engine.
        internal static MainSceneLogic SceneLogic;
        internal static Action QuitHandler;

        /// <summary>The load in progress (queued, reading, building, waiting or finishing), or null.</summary>
        public static SceneLoadOperation CurrentLoad { get; private set; }
        /// <summary>The last load that finished (Done), blocking or not: its timings say what it cost.</summary>
        public static SceneLoadOperation LastLoad { get; private set; }
        /// <summary>True while a scene load runs (<see cref="CurrentLoad"/>).</summary>
        public static bool IsLoading => CurrentLoad != null;

        /// <summary>Raised when a load is Done, a few frames after the new scene started (game thread).</summary>
        public static event Action<SceneLoadOperation> SceneLoaded;

        /// <summary>
        /// Milliseconds per frame <see cref="LoadSceneAsync(string, bool, bool)"/> spends creating gameobjects. Higher
        /// loads faster with longer frames; the swap itself always takes a frame of its own.
        /// </summary>
        public static float LoadBudgetMs { get; set; } = 4f;

        /// <summary>
        /// Vista document of the loading screen (Content/&lt;path&gt;.xml + .css). A replacement keeps the element ids
        /// that <see cref="LoadingScreen"/> fills.
        /// </summary>
        public static string LoadingScreenDocument { get; set; } = "UI/LoadingScreen";

        /// <summary>True when the engine runs inside the Anvil editor (Play mode), false in the standalone game.</summary>
        public static bool IsEditor => Input.HostBridge?.IsHostedByEditor == true;

        /// <summary>When false, Escape no longer closes the standalone game (set by menus that use Escape for Back).</summary>
        public static bool EscapeQuits { get; set; } = true;

        /// <summary>
        /// Raised when Play stops (not when a script switches scenes). Inside Anvil, scripts that
        /// changed engine-wide state such as <see cref="GameSettings"/> restore it here.
        /// </summary>
        public static event Action PlayStopped;

        /// <summary>
        /// Freezes the game during Play (a pause menu): gameobject and camera components stop updating,
        /// physics doesn't step, and weather particles hold still. Scripts that return true from
        /// <see cref="Scripting.ScriptBehaviour.UpdateWhilePaused"/> keep running, and so do game UI
        /// layers. A scene load or Stop unpauses.
        /// </summary>
        public static bool Paused { get; set; }

        internal static void RaisePlayStopped()
        {
            try { PlayStopped?.Invoke(); }
            catch (Exception ex) { EditorBridge.Log("GameFlow.PlayStopped handler threw: " + ex); }
        }

        /// <summary>Number of scenes in the build list.</summary>
        public static int SceneCount => SceneList.Scenes.Count;

        /// <summary>Name of the scene at <paramref name="index"/> in the build list ("MainMenu").</summary>
        public static string SceneName(int index) =>
            index >= 0 && index < SceneList.Scenes.Count ? SceneList.DisplayName(SceneList.Scenes[index]) : null;

        /// <summary>Name of the running scene.</summary>
        public static string ActiveSceneName => SceneLogic?.ActiveScene?.Name;

        /// <summary>Build-list index of the running scene, or -1 when it isn't in the list.</summary>
        public static int ActiveSceneIndex => SceneList.IndexOf(SceneLogic?.ActiveScene?.FilePath);

        /// <summary>
        /// Queues the scene at <paramref name="index"/> in the build list. False when out of range or missing.
        /// With <paramref name="carryPersistent"/> false, persistent gameobjects stay behind and unload with
        /// the departing scene (for example when going back to the main menu); inside Anvil, Stop still puts
        /// the edited scene's ones back.
        /// The whole load happens at the start of the next frame, a hitch on big scenes;
        /// <see cref="LoadSceneAsync(int, bool, bool)"/> spreads it out.
        /// </summary>
        public static bool LoadScene(int index, bool carryPersistent = true) =>
            Begin(ResolveIndex(index, "LoadScene"), carryPersistent, blocking: true, loadingScreen: false) != null;

        /// <summary>
        /// Queues a scene by list name ("GodRayTest") or Content-relative path ("Scenes/GodRayTest.obsc").
        /// <paramref name="carryPersistent"/> works as in <see cref="LoadScene(int, bool)"/>.
        /// </summary>
        public static bool LoadScene(string nameOrPath, bool carryPersistent = true) =>
            Begin(ResolveName(nameOrPath, "LoadScene"), carryPersistent, blocking: true, loadingScreen: false) != null;

        /// <summary>
        /// Loads the scene at <paramref name="index"/> in the build list in the background while the running scene
        /// keeps playing: the file is read on a worker thread, its gameobjects are created <see cref="LoadBudgetMs"/>
        /// per frame, then the scenes swap (Play carries on in the new one). With <paramref name="loadingScreen"/> the
        /// engine's loading screen (<see cref="LoadingScreenDocument"/>) covers the view and shows the real progress;
        /// without it, show <see cref="SceneLoadOperation.Progress"/> yourself.
        /// Returns the operation, or null when the scene doesn't exist. A new load cancels one that hasn't swapped yet.
        /// </summary>
        public static SceneLoadOperation LoadSceneAsync(int index, bool carryPersistent = true, bool loadingScreen = true) =>
            Begin(ResolveIndex(index, "LoadSceneAsync"), carryPersistent, blocking: false, loadingScreen);

        /// <summary>
        /// <see cref="LoadSceneAsync(int, bool, bool)"/> by list name ("GodRayTest") or Content-relative path ("Scenes/GodRayTest.obsc").
        /// </summary>
        public static SceneLoadOperation LoadSceneAsync(string nameOrPath, bool carryPersistent = true, bool loadingScreen = true) =>
            Begin(ResolveName(nameOrPath, "LoadSceneAsync"), carryPersistent, blocking: false, loadingScreen);

        /// <summary>Queues the next scene in the build list (wrapping to 0 after the last).</summary>
        public static bool LoadNextScene()
        {
            if (SceneCount == 0) return false;
            int index = ActiveSceneIndex;
            return LoadScene(index < 0 ? 0 : (index + 1) % SceneCount);
        }

        /// <summary>Queues the running scene again, from its file.</summary>
        public static bool ReloadScene() =>
            Begin(SceneLogic?.ActiveScene?.FilePath, carryPersistent: true, blocking: true, loadingScreen: false) != null;

        /// <summary>Stops the load in progress if it hasn't swapped scenes yet. False when there is none or it's too late.</summary>
        public static bool CancelLoad() => CurrentLoad?.Cancel() == true;

        /// <summary>Closes the standalone game. Inside Anvil this only logs (use Stop to leave Play mode).</summary>
        public static void Quit()
        {
            if (IsEditor || QuitHandler == null)
            {
                EditorBridge.Log("GameFlow.Quit: ignored inside the editor — press Stop to leave Play mode");
                return;
            }
            QuitHandler();
        }

        private static string ResolveIndex(int index, string caller)
        {
            if (index >= 0 && index < SceneList.Scenes.Count) return SceneList.ResolvePath(SceneList.Scenes[index]);
            EditorBridge.Log($"GameFlow.{caller}: index {index} is outside the scene list (0..{SceneList.Scenes.Count - 1})");
            return null;
        }

        private static string ResolveName(string nameOrPath, string caller)
        {
            int index = SceneList.Find(nameOrPath);
            if (index >= 0) return ResolveIndex(index, caller);
            if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
            string entry = nameOrPath.EndsWith(SceneSerialization.Extension, StringComparison.OrdinalIgnoreCase)
                ? nameOrPath : nameOrPath + SceneSerialization.Extension;
            return SceneList.ResolvePath(entry);
        }

        private static SceneLoadOperation Begin(string path, bool carryPersistent, bool blocking, bool loadingScreen)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                EditorBridge.Log($"GameFlow.LoadScene: scene file '{path}' not found");
                return null;
            }
            // One load at a time: a newer request replaces one that hasn't swapped yet.
            SceneLoadOperation previous = CurrentLoad;
            if (previous != null && !previous.Cancel())
            {
                previous.FinishNow();
                Report(previous);
            }
            CurrentLoad = new SceneLoadOperation(path, carryPersistent, blocking, loadingScreen);
            return CurrentLoad;
        }

        /// <summary>
        /// Advances <see cref="CurrentLoad"/> (start of each scene update, before input and scripts).
        /// <paramref name="activate"/> swaps the built scene in.
        /// </summary>
        internal static void UpdateLoad(Assets assets, Func<SceneLoadOperation, Scene, bool> activate)
        {
            SceneLoadOperation load = CurrentLoad;
            if (load != null)
            {
                if (load.ShowLoadingScreen) LoadingScreen.Show(load);
                load.Update(assets, activate, covered: !load.ShowLoadingScreen || LoadingScreen.IsCovering);
                if (load.IsDone)
                {
                    if (ReferenceEquals(CurrentLoad, load)) CurrentLoad = null;
                    Report(load);
                }
            }
            LoadingScreen.Update();
        }

        private static void Report(SceneLoadOperation load)
        {
            if (load.Status != SceneLoadStatus.Done) return;
            LastLoad = load;
            try { SceneLoaded?.Invoke(load); }
            catch (Exception ex) { EditorBridge.Log("GameFlow.SceneLoaded handler threw: " + ex); }
        }

        /// <summary>Play stopped or the editor opened another scene: drop the load and its loading screen.</summary>
        internal static void AbortLoad()
        {
            CurrentLoad?.Cancel();
            CurrentLoad = null;
            LoadingScreen.Hide();
        }
    }
}
