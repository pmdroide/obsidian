using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engine.Editor;

namespace Engine.Logic
{
    /// <summary>Where a <see cref="SceneLoadOperation"/> is. The order is the order a load goes through.</summary>
    public enum SceneLoadStatus
    {
        /// <summary>The .obsc (and its baked .probes) streams in and is parsed on a worker thread.</summary>
        Reading,
        /// <summary>Gameobjects, lights and decals are created on the game thread, a few milliseconds per frame.</summary>
        Building,
        /// <summary>Built and waiting for <see cref="SceneLoadOperation.AllowSceneActivation"/> (or the loading screen to cover the view).</summary>
        WaitingForActivation,
        /// <summary>The running scene is being replaced (one frame).</summary>
        Activating,
        /// <summary>The new scene runs; its first frames (physics bodies, shadow and environment maps) are under way.</summary>
        Finishing,
        Done,
        Failed,
        Canceled,
    }

    /// <summary>
    /// One scene load, started by <see cref="GameFlow.LoadSceneAsync(string, bool, bool)"/> (spread over frames, the
    /// running scene keeps playing) or <see cref="GameFlow.LoadScene(string, bool)"/> (everything in one frame).
    ///
    /// Progress is the real work done: bytes of the scene files read by the worker thread (0 - 0.3), objects built
    /// (0.3 - 0.9), then the swap and the new scene's first frames (0.9 - 1). Like Unity, a load held by
    /// <see cref="AllowSceneActivation"/> = false stops at 0.9 until it is set.
    ///
    /// The timing fields report what the load cost: <see cref="LongestFrameMs"/> is the worst hitch the player saw
    /// while it ran (a blocking load puts the whole load into one frame).
    /// </summary>
    public sealed class SceneLoadOperation
    {
        private const float ReadShare = 0.3f, BuildShare = 0.6f;
        /// <summary>Frames the new scene runs before the load is Done (the loading screen stays up over them).</summary>
        internal const int FinishFrames = 3;

        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private Task<SceneSerialization.SceneFile> _reading;
        private SceneSerialization.SceneBuilder _builder;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastTick = -1;
        private long _bytesRead, _totalBytes;
        private double _waitStarted = -1;
        private int _finishFrames;

        internal SceneLoadOperation(string path, bool carryPersistent, bool blocking, bool loadingScreen)
        {
            ScenePath = path;
            SceneName = Path.GetFileNameWithoutExtension(path);
            CarryPersistent = carryPersistent;
            Blocking = blocking;
            ShowLoadingScreen = loadingScreen && !blocking;
            if (!blocking) _reading = Task.Run(Read);
        }

        /// <summary>Full path of the scene file.</summary>
        public string ScenePath { get; }
        /// <summary>File name without extension ("WaterTest").</summary>
        public string SceneName { get; }
        public bool CarryPersistent { get; }
        /// <summary>True for <see cref="GameFlow.LoadScene(string, bool)"/>: read, build and swap in a single frame.</summary>
        public bool Blocking { get; }
        public bool ShowLoadingScreen { get; }

        /// <summary>
        /// While false the load stops at <see cref="SceneLoadStatus.WaitingForActivation"/> (progress 0.9) with the scene
        /// built, and the running scene carries on. Set it true to swap ("Press any key to continue").
        /// </summary>
        public bool AllowSceneActivation { get; set; } = true;

        /// <summary>Line the loading screen shows while the load waits for <see cref="AllowSceneActivation"/>.</summary>
        public string ReadyMessage { get; set; } = "Ready";

        public SceneLoadStatus Status { get; private set; } = SceneLoadStatus.Reading;
        /// <summary>The load stopped: <see cref="SceneLoadStatus.Done"/>, Failed or Canceled.</summary>
        public bool IsDone => Status >= SceneLoadStatus.Done;
        /// <summary>The scene is built (it may still be waiting for <see cref="AllowSceneActivation"/>).</summary>
        public bool IsReady => Status >= SceneLoadStatus.WaitingForActivation && Status <= SceneLoadStatus.Done;
        /// <summary>Why the load failed (Failed only).</summary>
        public string Error { get; private set; }
        /// <summary>The loaded scene, once built.</summary>
        public Scene Scene => _builder?.IsDone == true ? _builder.Scene : null;

        /// <summary>Bytes of the .obsc and .probes files read so far, and in total (known once reading started).</summary>
        public long BytesRead => Interlocked.Read(ref _bytesRead);
        public long TotalBytes => Interlocked.Read(ref _totalBytes);
        /// <summary>Gameobjects, lights and decals created so far, and in the file.</summary>
        public int ObjectsBuilt => _builder == null ? 0 : Math.Min(_builder.StepsDone, ObjectCount);
        public int ObjectCount => _builder == null ? 0 : _builder.TotalSteps - 1;

        /// <summary>Frames the load spanned (from the first frame after the request to Done).</summary>
        public int Frames { get; private set; }
        /// <summary>Real time from the request to now / Done.</summary>
        public double Seconds { get; private set; }
        /// <summary>Time the worker thread spent reading and parsing.</summary>
        public double ReadMs { get; private set; }
        /// <summary>Game-thread time spent creating objects (summed over the frames).</summary>
        public double BuildMs { get; private set; }
        /// <summary>Game-thread time of the swap (old scene out, new one registered, its scripts started).</summary>
        public double ActivationMs { get; private set; }
        /// <summary>Longest whole frame while the load ran: the hitch the player saw.</summary>
        public double LongestFrameMs { get; private set; }
        /// <summary>Time held at <see cref="SceneLoadStatus.WaitingForActivation"/>.</summary>
        public double WaitedSeconds { get; private set; }

        /// <summary>
        /// Real progress, 0 - 1 (see the class summary). Stays at 0.9 while waiting for activation.
        /// </summary>
        public float Progress
        {
            get
            {
                switch (Status)
                {
                    case SceneLoadStatus.Reading:
                        long total = TotalBytes;
                        return total <= 0 ? 0f : ReadShare * Math.Min(1f, BytesRead / (float)total);
                    case SceneLoadStatus.Building:
                        return ReadShare + BuildShare * (_builder == null || _builder.TotalSteps == 0 ? 0f : _builder.StepsDone / (float)_builder.TotalSteps);
                    case SceneLoadStatus.WaitingForActivation:
                    case SceneLoadStatus.Activating:
                        return ReadShare + BuildShare;
                    case SceneLoadStatus.Finishing:
                        return ReadShare + BuildShare + (1f - ReadShare - BuildShare) * _finishFrames / (FinishFrames + 1f);
                    case SceneLoadStatus.Done:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>Raised once when the load ends (Done, Failed or Canceled), on the game thread.</summary>
        public event Action<SceneLoadOperation> Completed;

        /// <summary>Stops a load that hasn't swapped scenes yet; the running scene carries on. False when too late.</summary>
        public bool Cancel()
        {
            if (Status >= SceneLoadStatus.Activating) return false;
            _cancel.Cancel();
            End(SceneLoadStatus.Canceled);
            return true;
        }

        public override string ToString() => $"{SceneName} {Status} {Progress:P0}";

        // ---------------------------------------------------------------------------------------------
        // Driven by GameFlow.UpdateLoad at the start of each scene update (game thread)
        // ---------------------------------------------------------------------------------------------

        private SceneSerialization.SceneFile Read()
        {
            long start = _clock.ElapsedTicks;
            try
            {
                return SceneSerialization.ReadFile(ScenePath, (read, total) =>
                {
                    Interlocked.Exchange(ref _totalBytes, total);
                    Interlocked.Exchange(ref _bytesRead, read);
                }, _cancel.Token);
            }
            finally { ReadMs = Ms(_clock.ElapsedTicks - start); }
        }

        /// <param name="assets">Models, textures and materials the scene file refers to by name.</param>
        /// <param name="activate">Swaps the built scene in; false when the swap failed.</param>
        /// <param name="covered">False while a loading screen is still fading in (the swap waits for it).</param>
        internal void Update(Recources.Assets assets, Func<SceneLoadOperation, Scene, bool> activate, bool covered)
        {
            if (IsDone) return;
            long now = _clock.ElapsedTicks;
            if (_lastTick >= 0)
            {
                Frames++;
                LongestFrameMs = Math.Max(LongestFrameMs, Ms(now - _lastTick));
            }
            _lastTick = now;
            Seconds = _clock.Elapsed.TotalSeconds;

            try
            {
                if (Blocking) { RunBlocking(assets, activate); return; }

                if (Status == SceneLoadStatus.Reading)
                {
                    if (!_reading.IsCompleted) return;
                    if (_reading.IsFaulted) throw _reading.Exception.GetBaseException();
                    if (_reading.IsCanceled) return;
                    _builder = new SceneSerialization.SceneBuilder(_reading.Result, assets);
                    Status = SceneLoadStatus.Building;
                }

                if (Status == SceneLoadStatus.Building)
                {
                    long start = _clock.ElapsedTicks;
                    long budget = (long)(Math.Max(0.5f, GameFlow.LoadBudgetMs) * Stopwatch.Frequency / 1000.0);
                    while (_clock.ElapsedTicks - start < budget && _builder.Step()) { }
                    BuildMs += Ms(_clock.ElapsedTicks - start);
                    // The swap is heavy too, so it gets a frame of its own.
                    if (_builder.IsDone) Status = SceneLoadStatus.WaitingForActivation;
                    return;
                }

                if (Status == SceneLoadStatus.WaitingForActivation)
                {
                    if (!AllowSceneActivation || !covered)
                    {
                        if (_waitStarted < 0) _waitStarted = _clock.Elapsed.TotalSeconds;
                        return;
                    }
                    if (_waitStarted >= 0) WaitedSeconds = _clock.Elapsed.TotalSeconds - _waitStarted;
                    Activate(activate);
                    return;
                }

                if (Status == SceneLoadStatus.Finishing && ++_finishFrames > FinishFrames)
                    End(SceneLoadStatus.Done);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                EditorBridge.Log($"GameFlow: couldn't load '{ScenePath}': {ex.Message}");
                End(SceneLoadStatus.Failed);
            }
        }

        private void RunBlocking(Recources.Assets assets, Func<SceneLoadOperation, Scene, bool> activate)
        {
            if (Status == SceneLoadStatus.Finishing)
            {
                if (++_finishFrames > FinishFrames) End(SceneLoadStatus.Done);
                return;
            }
            long start = _clock.ElapsedTicks;
            SceneSerialization.SceneFile file = SceneSerialization.ReadFile(ScenePath);
            _bytesRead = _totalBytes = file.Bytes;
            ReadMs = Ms(_clock.ElapsedTicks - start);
            Status = SceneLoadStatus.Building;
            start = _clock.ElapsedTicks;
            _builder = new SceneSerialization.SceneBuilder(file, assets);
            while (_builder.Step()) { }
            BuildMs = Ms(_clock.ElapsedTicks - start);
            Activate(activate);
        }

        private void Activate(Func<SceneLoadOperation, Scene, bool> activate)
        {
            Status = SceneLoadStatus.Activating;
            long start = _clock.ElapsedTicks;
            bool swapped = activate(this, _builder.Scene);
            ActivationMs = Ms(_clock.ElapsedTicks - start);
            if (!swapped) throw new InvalidOperationException("the scene swap failed (see the log)");
            Status = SceneLoadStatus.Finishing;
            _finishFrames = 0;
        }

        /// <summary>Ends a load that already swapped scenes (another load replaces it during its last frames).</summary>
        internal void FinishNow()
        {
            if (Status == SceneLoadStatus.Finishing) End(SceneLoadStatus.Done);
        }

        private void End(SceneLoadStatus status)
        {
            if (IsDone) return;
            Status = status;
            Seconds = _clock.Elapsed.TotalSeconds;
            _clock.Stop();
            EditorBridge.Log($"GameFlow: load of '{SceneName}' {status} after {Seconds:0.000} s, {Frames} frames " +
                             $"(read {ReadMs:0.0} ms, build {BuildMs:0.0} ms, swap {ActivationMs:0.0} ms, longest frame {LongestFrameMs:0.0} ms)");
            try { Completed?.Invoke(this); }
            catch (Exception ex) { EditorBridge.Log("SceneLoadOperation.Completed handler threw: " + ex); }
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
