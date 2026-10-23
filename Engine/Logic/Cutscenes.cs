using System;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Logic
{
    /// <summary>
    /// Plays <see cref="Cutscene"/> assets in Play mode: drives the scene's game camera along the camera track, plays
    /// the events (subtitles, fades, sounds, script messages) and draws the letterbox, subtitle and fade over the game.
    /// From a script: <c>Cutscenes.Play("Intro")</c>, <see cref="IsPlaying"/>, <see cref="Finished"/>, <see cref="EventFired"/>.
    /// Anvil's Cutscenes window previews a cutscene in Edit mode through <see cref="Preview"/> (editor camera, overlay only).
    /// Everything runs on the game thread.
    /// </summary>
    public static class Cutscenes
    {
        /// <summary>The cutscene playing, or null.</summary>
        public static Cutscene Current { get; private set; }
        /// <summary>Seconds into <see cref="Current"/>.</summary>
        public static float Time { get; private set; }
        public static bool IsPlaying => Current != null;

        /// <summary>Raised when a cutscene ends (played to the end, skipped or stopped).</summary>
        public static event Action<Cutscene> Finished;
        /// <summary>Raised with a Message event's text when the playhead reaches it.</summary>
        public static event Action<string> EventFired;

        // Game camera before the cutscene took it (RestoreCamera).
        private static Vector3 _savedPosition, _savedForward;
        private static float _savedFov;
        private static Camera _camera;

        // Edit-mode preview (Anvil): the overlay at a time, without playing events.
        private static Cutscene _preview;
        private static float _previewTime;
        // Editor camera pose before the preview first moved it (EndPreview puts it back).
        private static (Vector3 Position, float Yaw, float Pitch, float Fov)? _editorBeforePreview;

        private static Texture2D _pixel;

        /// <summary>Plays the cutscene at a Content path or name in Content/Cutscenes ("Intro"). False when it can't be read.</summary>
        public static bool Play(string nameOrPath)
        {
            var cutscene = Cutscene.Load(nameOrPath);
            return cutscene != null && Play(cutscene);
        }

        /// <summary>Plays <paramref name="cutscene"/> from the start, ending one already playing.</summary>
        public static bool Play(Cutscene cutscene)
        {
            if (cutscene == null) return false;
            if (Current != null) Stop();
            Current = cutscene;
            Time = 0f;
            _camera = GameFlow.SceneLogic?.Camera;
            if (_camera != null)
            {
                _savedPosition = _camera.Position;
                _savedForward = _camera.Forward;
                _savedFov = _camera.FieldOfView;
            }
            FireEvents(-1f, 0f);
            ApplyCamera();
            return true;
        }

        /// <summary>Jumps to the end: fires the remaining sounds/messages and finishes.</summary>
        public static void Skip()
        {
            if (Current == null) return;
            float from = Time;
            Time = Current.Duration;
            FireEvents(from, Time, skipping: true);
            Finish();
        }

        /// <summary>Ends the cutscene where it is (no more events).</summary>
        public static void Stop()
        {
            if (Current == null) return;
            Finish();
        }

        /// <summary>Play stopped in Anvil: drop the cutscene without events (PlayMode restores the camera itself).</summary>
        internal static void Reset()
        {
            Current = null;
            _camera = null;
            Time = 0f;
        }

        private static void Finish()
        {
            var ended = Current;
            if (ended.RestoreCamera && _camera != null)
            {
                _camera.Position = _savedPosition;
                _camera.Forward = _savedForward;
                _camera.FieldOfView = _savedFov;
            }
            Current = null;
            _camera = null;
            try { Finished?.Invoke(ended); }
            catch (Exception ex) { EditorBridge.Log("Cutscenes.Finished threw: " + ex.Message); }
        }

        /// <summary>Advances the playing cutscene (MainSceneLogic.Update, after the scripts, so it wins the camera).</summary>
        internal static void Update(float deltaSeconds)
        {
            if (Current == null || GameFlow.Paused) return;
            // A scene load swapped the game camera: the shots belong to the old scene, so end it there.
            if (_camera != null && GameFlow.SceneLogic?.Camera != _camera)
            {
                _camera = null;
                Finish();
                return;
            }
            if (Current.Skippable && GameInput.MenuConfirm)
            {
                Skip();
                return;
            }
            float from = Time;
            Time = Math.Min(Time + deltaSeconds, Current.Duration);
            FireEvents(from, Time);
            ApplyCamera();
            if (Current != null && Time >= Current.Duration) Finish();
        }

        // Sound and Message events with from < Time <= to (a skip only fires Messages, so scripts reach their end state).
        private static void FireEvents(float from, float to, bool skipping = false)
        {
            var cutscene = Current;
            foreach (var e in cutscene.Events.ToArray())
            {
                if (e.Time <= from || e.Time > to) continue;
                if (e.Kind == CutsceneEventKind.Sound && !skipping && !string.IsNullOrWhiteSpace(e.Text))
                    Audio.PlaySound(e.Text.Trim());
                else if (e.Kind == CutsceneEventKind.Message)
                {
                    try { EventFired?.Invoke(e.Text ?? ""); }
                    catch (Exception ex) { EditorBridge.Log($"Cutscene '{cutscene.Name}' message '{e.Text}' threw: {ex.Message}"); }
                }
                if (Current != cutscene) return; // a handler stopped or replaced it
            }
        }

        private static void ApplyCamera()
        {
            if (Current == null || _camera == null) return;
            var pose = Current.EvaluateCamera(Time);
            if (pose is { } p) Apply(_camera, p);
        }

        private static void Apply(Camera camera, CutsceneCameraPose pose)
        {
            camera.Position = pose.Position;
            camera.FieldOfView = MathHelper.ToRadians(MathHelper.Clamp(pose.Fov, 5f, 150f));
            if (camera is EditorCamera editor)
            {
                editor.Yaw = MathHelper.ToRadians(pose.Yaw);
                editor.Pitch = MathHelper.ToRadians(pose.Pitch);
                editor.ApplyYawPitch();
            }
            else camera.Forward = pose.Forward;
        }

        // ------------------------------------------------------------------
        //  Edit-mode preview (Anvil > Window > Cutscenes)
        // ------------------------------------------------------------------

        /// <summary>
        /// Shows <paramref name="cutscene"/> at <paramref name="time"/> in the editor viewport (moves the editor camera).
        /// While the preview plays, <paramref name="playedFrom"/> is the previous time: Sound events in between play.
        /// </summary>
        public static void Preview(Cutscene cutscene, float time, bool moveCamera = true, float? playedFrom = null)
        {
            _preview = cutscene;
            _previewTime = time;
            if (cutscene != null && playedFrom is { } from)
                foreach (var e in cutscene.Events)
                    if (e.Kind == CutsceneEventKind.Sound && e.Time > from && e.Time <= time && !string.IsNullOrWhiteSpace(e.Text))
                        PlayPreviewSound(e.Text.Trim());
            var editorCamera = GameFlow.SceneLogic?.EditorCamera;
            if (cutscene == null || editorCamera == null || !moveCamera) return;
            var pose = cutscene.EvaluateCamera(time);
            if (pose == null) return;
            _editorBeforePreview ??= (editorCamera.Position, editorCamera.Yaw, editorCamera.Pitch, editorCamera.FieldOfView);
            Apply(editorCamera, pose.Value);
        }

        // Sounds the preview started, so pausing or closing it silences them.
        private static readonly System.Collections.Generic.List<FmodForFoxes.Channel> PreviewChannels = new();

        private static void PlayPreviewSound(string name)
        {
            var channel = Audio.PlaySound(name);
            if (channel is { } c) PreviewChannels.Add(c);
            else if (Audio.Instance != null)
                EditorBridge.Log($"Cutscene preview: sound '{name}' didn't play (missing from Content/Audio, or audio is off)");
        }

        /// <summary>Stops the sounds the preview started (Anvil: Pause, scrubbing, closing the window).</summary>
        public static void StopPreviewSounds()
        {
            foreach (var channel in PreviewChannels)
                try { channel.Stop(); } catch { /* already finished: the handle is invalid */ }
            PreviewChannels.Clear();
        }

        /// <summary>Ends the preview overlay and puts the editor camera back where it was before the preview moved it.</summary>
        public static void EndPreview()
        {
            StopPreviewSounds();
            _preview = null;
            var editorCamera = GameFlow.SceneLogic?.EditorCamera;
            if (editorCamera != null && _editorBeforePreview is { } before)
            {
                editorCamera.Position = before.Position;
                editorCamera.Yaw = before.Yaw;
                editorCamera.Pitch = before.Pitch;
                editorCamera.ApplyYawPitch();
                editorCamera.FieldOfView = before.Fov;
            }
            _editorBeforePreview = null;
        }

        /// <summary>The editor camera's pose as a key (yaw/pitch in degrees, FOV as previewed).</summary>
        public static CutsceneCameraKey CaptureEditorCamera(float time)
        {
            var camera = GameFlow.SceneLogic?.EditorCamera;
            if (camera == null) return null;
            return new CutsceneCameraKey
            {
                Time = time,
                Position = camera.Position,
                Yaw = MathHelper.ToDegrees(camera.Yaw),
                Pitch = MathHelper.ToDegrees(camera.Pitch),
                Fov = (float)Math.Round(MathHelper.ToDegrees(camera.FieldOfView), 1),
            };
        }

        // ------------------------------------------------------------------
        //  Overlay
        // ------------------------------------------------------------------

        /// <summary>Letterbox, subtitle and fade over the scene (ScreenManager.Draw, over the script UI).</summary>
        internal static void Draw(SpriteBatch spriteBatch, SpriteFont font)
        {
            Cutscene cutscene = Current ?? _preview;
            if (cutscene == null) return;
            float time = Current != null ? Time : _previewTime;

            var device = spriteBatch.GraphicsDevice;
            if (_pixel == null || _pixel.IsDisposed)
            {
                _pixel = new Texture2D(device, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            int width = device.Viewport.Width, height = device.Viewport.Height;
            float scale = height / 1080f;

            spriteBatch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp);
            // 2.39:1 bars, never more than an eighth of the screen each.
            int bar = cutscene.Letterbox ? (int)Math.Min(height * 0.125f, Math.Max(0, (height - width / 2.39f) / 2f)) : 0;
            if (bar > 0)
            {
                spriteBatch.Draw(_pixel, new Rectangle(0, 0, width, bar), Color.Black);
                spriteBatch.Draw(_pixel, new Rectangle(0, height - bar, width, bar), Color.Black);
            }

            float fade = cutscene.FadeAt(time);
            if (fade > 0.001f) spriteBatch.Draw(_pixel, new Rectangle(0, 0, width, height), Color.Black * fade);

            string subtitle = cutscene.SubtitleAt(time);
            if (!string.IsNullOrWhiteSpace(subtitle) && font != null)
            {
                // Never smaller than readable in a small editor viewport; sits above the bottom bar.
                float textScale = Math.Max(scale * 1.25f, 0.75f);
                Vector2 size = font.MeasureString(subtitle) * textScale;
                float bottom = height - Math.Max(bar, height * 0.06f) - 24f * scale;
                var position = new Vector2((width - size.X) / 2f, bottom - size.Y);
                spriteBatch.DrawString(font, subtitle, position + new Vector2(2, 2) * scale, Color.Black * 0.8f, 0, Vector2.Zero, textScale, SpriteEffects.None, 0);
                spriteBatch.DrawString(font, subtitle, position, Color.White, 0, Vector2.Zero, textScale, SpriteEffects.None, 0);
            }
            spriteBatch.End();
        }
    }
}
