namespace Engine.Scripting;

/// <summary>
/// A timer or tween a script started (<see cref="ScriptBehaviour.After"/>, <see cref="ScriptBehaviour.Every"/>,
/// <see cref="ScriptBehaviour.Tween(float, float, float, Action{float}, Ease, bool)"/>, ...). It counts in game time
/// (held while <see cref="Logic.GameFlow.Paused"/>) unless <see cref="Realtime"/>, advances after each Update of its
/// script and ends with it. Keep the handle to pause, cancel or restart it, or <c>yield return</c> it in a coroutine
/// to wait until it is done.
/// </summary>
public sealed class ScriptTimer
{
    // Catch-up limit for short intervals on a long frame (e.g. Every(0.001f) after a hitch).
    private const int MaxFiresPerFrame = 100;

    private readonly ScriptBehaviour _owner;
    private readonly Action _action;
    private readonly Action<float> _apply;
    private readonly Ease _ease;
    private Action _onComplete;
    private int _cycles;
    private bool _pingPong;
    private bool _reversed;
    private int _generation; // bumped by Restart, so an action that restarts its own timer ends the old cycle loop

    internal ScriptTimer(ScriptBehaviour owner, float seconds, Action action, int cycles, bool realtime,
                         Action<float> apply = null, Ease ease = Ease.Linear)
    {
        _owner = owner;
        Duration = Math.Max(0f, seconds);
        _action = action;
        _cycles = Math.Max(0, cycles);
        Realtime = realtime;
        _apply = apply;
        _ease = ease;
        _apply?.Invoke(Easing.Evaluate(_ease, 0f));
    }

    /// <summary>Seconds per cycle: the delay, the interval of a repeating timer or the length of a tween.</summary>
    public float Duration { get; }
    /// <summary>Keeps counting while the game is paused (only matters in scripts that update while paused).</summary>
    public bool Realtime { get; }
    /// <summary>Seconds into the current cycle.</summary>
    public float Elapsed { get; private set; }
    public float Remaining => Math.Max(0f, Duration - Elapsed);
    /// <summary>0 to 1 through the current cycle (1 once done).</summary>
    public float Progress => IsDone || Duration <= 0f ? 1f : Math.Clamp(Elapsed / Duration, 0f, 1f);
    /// <summary>Cycles finished so far (times the action ran).</summary>
    public int Completed { get; private set; }
    public bool IsPaused { get; private set; }
    /// <summary>Finished every cycle, or was completed or cancelled. Restart() runs it again.</summary>
    public bool IsDone { get; private set; }
    public bool IsCancelled { get; private set; }
    public bool IsRunning => !IsDone && !IsPaused;

    /// <summary>In the owner's list of timers to advance (false once done and dropped).</summary>
    internal bool Scheduled { get; set; }

    /// <summary>Runs <paramref name="action"/> once the last cycle finished (or <see cref="Complete"/>), not on Cancel.</summary>
    public ScriptTimer OnComplete(Action action)
    {
        _onComplete += action;
        return this;
    }

    /// <summary>
    /// Repeats: <paramref name="cycles"/> runs in total, 0 = forever. A tween with <paramref name="pingPong"/> plays
    /// backwards every other cycle (Loop(pingPong: true) breathes back and forth for good).
    /// </summary>
    public ScriptTimer Loop(int cycles = 0, bool pingPong = false)
    {
        _cycles = Math.Max(0, cycles);
        _pingPong = pingPong;
        return this;
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    /// <summary>Stops without running the action or OnComplete. A tween keeps the value it reached.</summary>
    public void Cancel()
    {
        if (IsDone) return;
        IsDone = true;
        IsCancelled = true;
    }

    /// <summary>Finishes now: a tween jumps to the end of this cycle, the action runs once, then OnComplete.</summary>
    public void Complete()
    {
        if (IsDone) return;
        Elapsed = Duration;
        FinishCycle(last: true);
    }

    /// <summary>Starts over from zero (also after it finished or was cancelled), unpaused.</summary>
    public ScriptTimer Restart()
    {
        _generation++;
        Elapsed = 0f;
        Completed = 0;
        _reversed = false;
        IsDone = IsCancelled = IsPaused = false;
        _apply?.Invoke(Easing.Evaluate(_ease, 0f));
        _owner.Schedule(this);
        return this;
    }

    internal void Advance(float gameDelta, float realDelta)
    {
        if (!IsRunning) return;
        Elapsed += Realtime ? realDelta : gameDelta;
        // A zero-length cycle runs once per frame, so Every(0, ...) is "every frame" rather than a hang.
        int fires = 0, limit = Duration > 0f ? MaxFiresPerFrame : 1;
        while (Elapsed >= Duration && !IsDone && !IsPaused && fires++ < limit)
        {
            bool last = _cycles > 0 && Completed + 1 >= _cycles;
            int generation = _generation;
            FinishCycle(last);
            if (generation != _generation) return;
            if (!last && !IsDone) Elapsed -= Duration;
        }
        if (Duration > 0f && Elapsed >= Duration && !IsDone) Elapsed %= Duration;
        if (!IsDone && _apply != null) Apply(Progress);
    }

    private void FinishCycle(bool last)
    {
        Apply(1f);
        Completed++;
        if (last) IsDone = true;
        else if (_pingPong) _reversed = !_reversed;
        int generation = _generation;
        _action?.Invoke();
        if (last && !IsCancelled && generation == _generation) _onComplete?.Invoke();
    }

    private void Apply(float t)
    {
        if (_apply == null) return;
        _apply(Easing.Evaluate(_ease, _reversed ? 1f - t : t));
    }
}
