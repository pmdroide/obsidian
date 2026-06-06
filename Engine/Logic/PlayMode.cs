using System;
using System.Collections.Generic;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Logic
{
    public enum GameMode { Edit, Play }

    /// <summary>
    /// Owns Edit/Play transitions. On <see cref="Play"/> snapshots the active scene's
    /// transforms so <see cref="Stop"/> can restore them. Camera selection, physics
    /// gating, and script ticks all read <see cref="Mode"/>.
    /// </summary>
    public class PlayModeController
    {
        public GameMode Mode { get; private set; } = GameMode.Edit;
        public event Action<GameMode> ModeChanged;

        private readonly MainSceneLogic _sceneLogic;

        // Snapshot of the editor-side transforms, captured when Play() starts so
        // Stop() can rewind. Lights store position only (their other settings can't
        // be mutated at runtime from scripts in this build).
        private readonly List<TransformSnap> _entitySnap = new();
        private readonly List<TransformSnap> _decalSnap = new();
        private readonly List<TransformSnap> _pointLightSnap = new();
        private readonly List<TransformSnap> _dirLightSnap = new();

        // Cached editor-mode value so Stop() can restore whatever the user had set.
        private bool _editorFlagBeforePlay;

        public PlayModeController(MainSceneLogic sceneLogic)
        {
            _sceneLogic = sceneLogic ?? throw new ArgumentNullException(nameof(sceneLogic));
        }

        public void Play()
        {
            if (Mode == GameMode.Play) return;
            EditorBridge.Log("PlayModeController.Play");

            CaptureSnapshot();

            // Drop editor-mode so physics & game systems run.
            _editorFlagBeforePlay = GameSettings.e_enableeditor;
            GameSettings.e_enableeditor = false;

            Mode = GameMode.Play;

            // OnStart for every attached script. Ordering: scene insertion order.
            Scene s = _sceneLogic.ActiveScene;
            if (s != null)
            {
                var ctx = new ScriptContext { Scene = s };
                foreach (var e in s.BasicEntities)
                {
                    if (e.Scripts.Count == 0) continue;
                    ctx.Owner = e;
                    for (int i = 0; i < e.Scripts.Count; i++)
                    {
                        try { e.Scripts[i].OnStart(ctx); }
                        catch (Exception ex) { EditorBridge.Log("Script.OnStart threw: " + ex); }
                    }
                }
            }

            try { ModeChanged?.Invoke(Mode); }
            catch (Exception ex) { EditorBridge.Log("ModeChanged threw: " + ex); }
        }

        /// <summary>
        /// Drives <see cref="IScript.OnUpdate"/> for every script on every entity in the
        /// active scene. Called from <see cref="MainSceneLogic.Update"/> only when
        /// <see cref="Mode"/> is <see cref="GameMode.Play"/>.
        /// </summary>
        public void UpdateScripts(GameTime gameTime)
        {
            if (Mode != GameMode.Play) return;
            Scene s = _sceneLogic.ActiveScene;
            if (s == null) return;
            var ctx = new ScriptContext { Scene = s };
            foreach (var e in s.BasicEntities)
            {
                if (e.Scripts.Count == 0) continue;
                ctx.Owner = e;
                for (int i = 0; i < e.Scripts.Count; i++)
                {
                    try { e.Scripts[i].OnUpdate(ctx, gameTime); }
                    catch (Exception ex) { EditorBridge.Log("Script.OnUpdate threw: " + ex); }
                }
            }
        }

        public void Stop()
        {
            if (Mode == GameMode.Edit) return;
            EditorBridge.Log("PlayModeController.Stop");

            RestoreSnapshot();
            GameSettings.e_enableeditor = _editorFlagBeforePlay;

            Mode = GameMode.Edit;
            try { ModeChanged?.Invoke(Mode); }
            catch (Exception ex) { EditorBridge.Log("ModeChanged threw: " + ex); }
        }

        private void CaptureSnapshot()
        {
            _entitySnap.Clear(); _decalSnap.Clear();
            _pointLightSnap.Clear(); _dirLightSnap.Clear();

            Scene s = _sceneLogic.ActiveScene;
            if (s == null) return;
            foreach (var e in s.BasicEntities)
                _entitySnap.Add(new TransformSnap(e.Id, e.Position, e.RotationMatrix, e.Scale));
            foreach (var d in s.Decals)
                _decalSnap.Add(new TransformSnap(d.Id, d.Position, d.RotationMatrix, d.Scale));
            foreach (var pl in s.PointLights)
                _pointLightSnap.Add(new TransformSnap(pl.Id, pl.Position, Matrix.Identity, Vector3.One));
            foreach (var dl in s.DirectionalLights)
                _dirLightSnap.Add(new TransformSnap(dl.Id, dl.Position, dl.RotationMatrix, Vector3.One));
        }

        private void RestoreSnapshot()
        {
            Scene s = _sceneLogic.ActiveScene;
            if (s == null) return;
            ApplyById(_entitySnap, s.BasicEntities);
            ApplyById(_decalSnap, s.Decals);
            ApplyById(_pointLightSnap, s.PointLights);
            ApplyById(_dirLightSnap, s.DirectionalLights);
        }

        private static void ApplyById<T>(List<TransformSnap> snaps, List<T> live)
            where T : TransformableObject
        {
            // O(N*M) but N, M are scene-size — typically tens, not thousands.
            foreach (var snap in snaps)
            {
                foreach (var obj in live)
                {
                    if (obj.Id != snap.Id) continue;
                    obj.Position = snap.Position;
                    obj.RotationMatrix = snap.Rotation;
                    obj.Scale = snap.Scale;
                    break;
                }
            }
        }

        private readonly struct TransformSnap
        {
            public readonly int Id;
            public readonly Vector3 Position;
            public readonly Matrix Rotation;
            public readonly Vector3 Scale;
            public TransformSnap(int id, Vector3 pos, Matrix rot, Vector3 scale)
            {
                Id = id; Position = pos; Rotation = rot; Scale = scale;
            }
        }
    }
}
