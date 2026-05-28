using System;
using Engine.Editor;
using Engine.Recources;

namespace Engine.Logic
{
    /// <summary>
    /// Owns the active <see cref="Scene"/> and notifies on swap. The bridge, MainSceneLogic,
    /// and Play/Stop all route through this so swaps stay centralised.
    /// </summary>
    public class SceneManager
    {
        public Scene ActiveScene { get; private set; }
        public Assets Assets;

        /// <summary>
        /// Fires after <see cref="ActiveScene"/> has been swapped. (oldScene, newScene).
        /// Subscribers (MainSceneLogic) detach old runtime state and reattach new.
        /// </summary>
        public event Action<Scene, Scene> SceneChanged;

        public SceneManager(Scene initial)
        {
            ActiveScene = initial;
        }

        public void SetActiveScene(Scene newScene)
        {
            if (newScene == null) throw new ArgumentNullException(nameof(newScene));
            if (ReferenceEquals(newScene, ActiveScene)) return;

            Scene old = ActiveScene;
            ActiveScene = newScene;
            try { SceneChanged?.Invoke(old, newScene); }
            catch (Exception ex) { EditorBridge.Log("SceneManager.SceneChanged handler threw: " + ex); }
        }

        public Scene NewScene(string name = "Untitled")
        {
            Scene s = new Scene { Name = name };
            SetActiveScene(s);
            return s;
        }

        public Scene LoadScene(string path)
        {
            if (Assets == null) { EditorBridge.Log("SceneManager.LoadScene: assets not bound"); return null; }
            try
            {
                Scene loaded = SceneSerialization.LoadFromFile(path, Assets);
                SetActiveScene(loaded);
                return loaded;
            }
            catch (Exception ex)
            {
                EditorBridge.Log("SceneManager.LoadScene threw: " + ex);
                return null;
            }
        }

        public bool SaveScene(string path)
        {
            if (Assets == null) { EditorBridge.Log("SceneManager.SaveScene: assets not bound"); return false; }
            try
            {
                SceneSerialization.SaveToFile(ActiveScene, path, Assets);
                return true;
            }
            catch (Exception ex)
            {
                EditorBridge.Log("SceneManager.SaveScene threw: " + ex);
                return false;
            }
        }
    }
}
