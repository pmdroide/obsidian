using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Vista;

namespace Engine.Logic
{
    /// <summary>
    /// The engine's loading screen for <see cref="GameFlow.LoadSceneAsync(string, bool, bool)"/>: an overlay layer
    /// (<see cref="GameFlow.LoadingScreenDocument"/>, default <c>Content/UI/LoadingScreen.xml</c> + <c>.css</c>) that fades
    /// in over the running scene, stays up through the swap and the new scene's first frames, then fades out.
    /// Every number on it comes from the <see cref="SceneLoadOperation"/>; the sweep on the bar moves with real time,
    /// so a hitch shows as a stutter.
    ///
    /// Element ids it fills (a custom document may leave any out): <c>#root</c> (faded), <c>#scene</c>, <c>#status</c>,
    /// <c>#fill</c> (width = progress), <c>#sweep</c> (slides along the track), <c>#percent</c>, <c>#detail</c>, <c>#time</c>;
    /// <c>#root</c> gets the class <c>ready</c> while waiting for activation and <c>failed</c> after an error.
    /// </summary>
    internal static class LoadingScreen
    {
        private const float FadeInSeconds = 0.15f, FadeOutSeconds = 0.35f, FailedHoldSeconds = 2.5f;
        private const float TrackWidth = 960f, SweepWidth = 160f;

        private static UIManager _ui;
        private static UIElement _root, _sweep;
        private static SceneLoadOperation _load;
        private static float _opacity, _failedHold, _sweepTime;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _lastTime;

        /// <summary>Fully faded in (or unavailable, so loads never wait on it): the swap can happen unseen.</summary>
        internal static bool IsCovering => _ui == null || _opacity >= 1f;

        internal static void Show(SceneLoadOperation load)
        {
            if (!ReferenceEquals(_load, load))
            {
                _load = load;
                _failedHold = 0f;
            }
            if (_ui != null) return;
            _ui = GameUI.OpenOverlay(GameFlow.LoadingScreenDocument);
            if (_ui == null) return; // logged by GameUI; the load carries on without a screen
            _ui.Loaded += Bind;
            Bind();
            _opacity = 0f;
            _lastTime = Clock.Elapsed.TotalSeconds;
        }

        internal static void Hide()
        {
            _load = null;
            if (_ui == null) return;
            _ui.Loaded -= Bind;
            GameUI.Close(_ui);
            _ui = null;
            _root = _sweep = null;
            _opacity = 0f;
        }

        private static void Bind()
        {
            _root = _ui.Get("#root");
            _sweep = _ui.Get("#sweep");
        }

        /// <summary>Fades and fills the screen (after the load advanced this frame); it keeps showing the last load while it fades out.</summary>
        internal static void Update()
        {
            if (_ui == null) return;
            double now = Clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, now - _lastTime);
            _lastTime = now;

            SceneLoadOperation load = _load;
            bool failed = load?.Status == SceneLoadStatus.Failed;
            if (failed) _failedHold += dt;
            // A newer load with a screen took over in Show; a blocking one leaves this one canceled, so it fades.
            bool visible = load != null && load.ShowLoadingScreen &&
                           (!load.IsDone || (failed && _failedHold < FailedHoldSeconds));

            _opacity = visible ? Math.Min(1f, _opacity + dt / FadeInSeconds) : Math.Max(0f, _opacity - dt / FadeOutSeconds);
            if (!visible && _opacity <= 0f)
            {
                Hide();
                return;
            }
            if (_root != null) _root.RuntimeOpacity = _opacity;
            if (load != null) Fill(load, dt);
        }

        private static void Fill(SceneLoadOperation load, float dt)
        {
            float progress = load.Progress;
            _ui.SetText("#scene", Spaced(load.SceneName));
            _ui.SetText("#status", StatusText(load));
            _ui.SetText("#percent", ((int)(progress * 100f)).ToString(CultureInfo.InvariantCulture) + "%");
            _ui.SetText("#detail", Detail(load));
            _ui.SetText("#time", load.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s");
            _ui.SetVariable("#fill", "style", "width: " + (progress * 100f).ToString("0.###", CultureInfo.InvariantCulture) + "%");
            _ui.SetClass("#root", "ready", load.Status == SceneLoadStatus.WaitingForActivation && !load.AllowSceneActivation);
            _ui.SetClass("#root", "failed", load.Status == SceneLoadStatus.Failed);

            if (_sweep != null)
            {
                // Glides along the track and back (Vista doesn't clip, so it stays inside).
                _sweepTime = (_sweepTime + dt * 0.6f) % 1f;
                float along = 0.5f - 0.5f * MathF.Cos(_sweepTime * MathHelper.TwoPi);
                _sweep.RuntimeOffset = new Vector2(along * (TrackWidth - SweepWidth), 0f);
                _sweep.RuntimeOpacity = load.IsDone ? 0f : 1f;
            }
        }

        private static string StatusText(SceneLoadOperation load)
        {
            switch (load.Status)
            {
                case SceneLoadStatus.Reading: return "Reading scene file";
                case SceneLoadStatus.Building: return "Building scene";
                case SceneLoadStatus.WaitingForActivation: return load.AllowSceneActivation ? "Starting" : load.ReadyMessage;
                case SceneLoadStatus.Activating:
                case SceneLoadStatus.Finishing: return "Starting";
                case SceneLoadStatus.Done: return "Done";
                case SceneLoadStatus.Failed: return "Couldn't load the scene";
                default: return "Canceled";
            }
        }

        private static string Detail(SceneLoadOperation load)
        {
            switch (load.Status)
            {
                case SceneLoadStatus.Reading:
                    return load.TotalBytes > 0 ? $"{Size(load.BytesRead)} of {Size(load.TotalBytes)}" : "Opening";
                case SceneLoadStatus.Building:
                    return $"{load.ObjectsBuilt:N0} of {load.ObjectCount:N0} objects";
                case SceneLoadStatus.Failed:
                    return load.Error ?? "";
                case SceneLoadStatus.WaitingForActivation:
                case SceneLoadStatus.Activating:
                case SceneLoadStatus.Finishing:
                case SceneLoadStatus.Done:
                    return $"{load.ObjectCount:N0} objects  ·  {Size(load.TotalBytes)}";
                default:
                    return "";
            }
        }

        private static string Size(long bytes) =>
            bytes >= 1 << 20 ? (bytes / (double)(1 << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture) + " KB";

        // "SceneLoadingArena" -> "Scene Loading Arena"
        private static string Spaced(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var text = new System.Text.StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                    text.Append(' ');
                text.Append(c == '_' ? ' ' : c);
            }
            return text.ToString();
        }
    }
}
