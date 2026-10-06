using System;
using Engine.Editor;
using Engine.Recources;

namespace Engine.Logic
{
    /// <summary>
    /// Runtime game flow for scripts: the build scene list (<see cref="SceneList"/>), switching
    /// scenes during Play, and quitting.
    ///
    /// Scene loads are queued and happen at the start of the next frame, so a script can call
    /// <see cref="LoadScene(int)"/> from its own Update. In Play the new scene starts playing straight
    /// away. Inside Anvil, pressing Stop returns to the scene you were editing.
    /// </summary>
    public static class GameFlow
    {
        // Bound by MainSceneLogic.Initialize / Engine.
        internal static MainSceneLogic SceneLogic;
        internal static Action QuitHandler;

        internal static string PendingScenePath { get; private set; }

        /// <summary>True when the engine runs inside the Anvil editor (Play mode), false in the standalone game.</summary>
        public static bool IsEditor => Input.HostBridge?.IsHostedByEditor == true;

        /// <summary>When false, Escape no longer closes the standalone game (set by menus that use Escape for Back).</summary>
        public static bool EscapeQuits { get; set; } = true;

        /// <summary>
        /// Raised when Play stops (not when a script switches scenes). Inside Anvil, scripts that
        /// changed engine-wide state such as <see cref="GameSettings"/> restore it here.
        /// </summary>
        public static event Action PlayStopped;

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

        /// <summary>Queues the scene at <paramref name="index"/> in the build list. False when out of range or missing.</summary>
        public static bool LoadScene(int index)
        {
            if (index < 0 || index >= SceneList.Scenes.Count)
            {
                EditorBridge.Log($"GameFlow.LoadScene: index {index} is outside the scene list (0..{SceneList.Scenes.Count - 1})");
                return false;
            }
            return QueuePath(SceneList.ResolvePath(SceneList.Scenes[index]));
        }

        /// <summary>Queues a scene by list name ("GodRayTest") or Content-relative path ("Scenes/GodRayTest.obsc").</summary>
        public static bool LoadScene(string nameOrPath)
        {
            int index = SceneList.Find(nameOrPath);
            if (index >= 0) return LoadScene(index);
            if (string.IsNullOrWhiteSpace(nameOrPath)) return false;
            string entry = nameOrPath.EndsWith(SceneSerialization.Extension, StringComparison.OrdinalIgnoreCase)
                ? nameOrPath : nameOrPath + SceneSerialization.Extension;
            return QueuePath(SceneList.ResolvePath(entry));
        }

        /// <summary>Queues the next scene in the build list (wrapping to 0 after the last).</summary>
        public static bool LoadNextScene()
        {
            if (SceneCount == 0) return false;
            int index = ActiveSceneIndex;
            return LoadScene(index < 0 ? 0 : (index + 1) % SceneCount);
        }

        /// <summary>Queues the running scene again, from its file.</summary>
        public static bool ReloadScene() => QueuePath(SceneLogic?.ActiveScene?.FilePath);

        /// <summary>Closes the standalone game. Inside Anvil this only logs (use Stop to leave Play).</summary>
        public static void Quit()
        {
            if (IsEditor || QuitHandler == null)
            {
                EditorBridge.Log("GameFlow.Quit: ignored inside the editor — press Stop to leave Play mode");
                return;
            }
            QuitHandler();
        }

        private static bool QueuePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                EditorBridge.Log($"GameFlow.LoadScene: scene file '{path}' not found");
                return false;
            }
            PendingScenePath = path;
            return true;
        }

        internal static string TakePendingScene()
        {
            string path = PendingScenePath;
            PendingScenePath = null;
            return path;
        }
    }
}
