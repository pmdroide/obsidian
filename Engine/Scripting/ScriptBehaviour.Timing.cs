using System.Collections;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

public abstract partial class ScriptBehaviour
{
    ////////////////////////////////////////////////////////////////////////////////
    //  TIME, TIMERS, COROUTINES & TWEENS
    //  All advance after this script's Update, in game time (held while GameFlow.Paused)
    //  unless realtime, and end when it stops. Callbacks run on the game thread; one
    //  that throws stops the script like a throwing Update.
    ////////////////////////////////////////////////////////////////////////////////

    private readonly List<ScriptTimer> _timers = new();
    private readonly List<ScriptCoroutine> _coroutines = new();

    /// <summary>Seconds of game time since this script started (held while the game is paused).</summary>
    public float Time { get; private set; }
    /// <summary>Updates this script has run, counting the current one.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Runs <paramref name="action"/> once after <paramref name="seconds"/> (0 = next frame).</summary>
    public ScriptTimer After(float seconds, Action action, bool realtime = false) =>
        Schedule(new ScriptTimer(this, seconds, action, cycles: 1, realtime));

    /// <summary>
    /// Runs <paramref name="action"/> every <paramref name="seconds"/>, the first time one interval from now,
    /// <paramref name="times"/> times in total (0 = until cancelled). A long frame catches up on missed runs.
    /// </summary>
    public ScriptTimer Every(float seconds, Action action, int times = 0, bool realtime = false) =>
        Schedule(new ScriptTimer(this, seconds, action, times, realtime));

    /// <summary>
    /// A countdown with no action: poll <c>IsDone</c>/<c>Remaining</c>/<c>Progress</c>, <c>Restart()</c> it, or
    /// yield it in a coroutine. As a cooldown: <c>if (_reload.IsDone) { Fire(); _reload.Restart(); }</c>.
    /// </summary>
    public ScriptTimer StartTimer(float seconds, bool realtime = false) => After(seconds, null, realtime);

    /// <summary>Cancels every timer and tween this script started (also happens automatically when it stops).</summary>
    public void CancelAllTimers()
    {
        foreach (var timer in _timers)
        {
            timer.Cancel();
            timer.Scheduled = false;
        }
        _timers.Clear();
    }

    /// <summary>Starts an iterator method as a coroutine; it runs to its first <c>yield</c> now. See <see cref="ScriptCoroutine"/>.</summary>
    public ScriptCoroutine StartCoroutine(IEnumerator routine)
    {
        if (routine == null) throw new ArgumentNullException(nameof(routine));
        var coroutine = new ScriptCoroutine(routine);
        _coroutines.Add(coroutine);
        coroutine.Step(0f, 0f);
        return coroutine;
    }

    public void StopCoroutine(ScriptCoroutine coroutine) => coroutine?.Stop();

    /// <summary>Stops every coroutine this script started (also happens automatically when it stops).</summary>
    public void StopAllCoroutines()
    {
        foreach (var coroutine in _coroutines.ToArray()) coroutine.Stop();
        _coroutines.Clear();
    }

    // Tweens: apply(value) runs every frame from `from` to `to` over `seconds`, shaped by `ease`.
    // They return a ScriptTimer: .Loop(pingPong: true), .OnComplete(...), Cancel(), or yield it.

    /// <summary>Animates a number, e.g. <c>Tween(0f, 1f, 0.5f, v => _light.Intensity = v, Ease.OutQuad)</c>.</summary>
    public ScriptTimer Tween(float from, float to, float seconds, Action<float> apply, Ease ease = Ease.Linear, bool realtime = false) =>
        Schedule(new ScriptTimer(this, seconds, null, 1, realtime, t => apply(MathHelper.Lerp(from, to, t)), ease));

    public ScriptTimer Tween(Vector3 from, Vector3 to, float seconds, Action<Vector3> apply, Ease ease = Ease.Linear, bool realtime = false) =>
        Schedule(new ScriptTimer(this, seconds, null, 1, realtime, t => apply(Vector3.Lerp(from, to, t)), ease));

    public ScriptTimer Tween(Color from, Color to, float seconds, Action<Color> apply, Ease ease = Ease.Linear, bool realtime = false) =>
        Schedule(new ScriptTimer(this, seconds, null, 1, realtime,
            t => apply(new Color(Vector4.Lerp(from.ToVector4(), to.ToVector4(), t))), ease));

    /// <summary>Calls <paramref name="apply"/> with the eased 0-to-1 progress every frame for <paramref name="seconds"/>.</summary>
    public ScriptTimer Tween(float seconds, Action<float> apply, Ease ease = Ease.Linear, bool realtime = false) =>
        Schedule(new ScriptTimer(this, seconds, null, 1, realtime, apply, ease));

    /// <summary>
    /// Slides this gameobject (or camera) from where it is now to <paramref name="target"/>. Meant for objects
    /// without a Dynamic body; a Dynamic one is moved by its velocity instead.
    /// </summary>
    public ScriptTimer MoveTo(Vector3 target, float seconds, Ease ease = Ease.InOutQuad) =>
        Tween(Position, target, seconds, p => Position = p, ease);

    /// <summary>Slides this gameobject by <paramref name="offset"/> (world axes) from where it is now.</summary>
    public ScriptTimer MoveBy(Vector3 offset, float seconds, Ease ease = Ease.InOutQuad) =>
        MoveTo(Position + offset, seconds, ease);

    /// <summary>Grows or shrinks this gameobject to <paramref name="target"/> scale.</summary>
    public ScriptTimer ScaleTo(Vector3 target, float seconds, Ease ease = Ease.InOutQuad) =>
        Tween(Scale, target, seconds, s => Scale = s, ease);

    /// <summary>Turns this gameobject (or camera) the short way round to the <paramref name="target"/> rotation.</summary>
    public ScriptTimer RotateTo(Matrix target, float seconds, Ease ease = Ease.InOutQuad)
    {
        Quaternion from = Quaternion.CreateFromRotationMatrix(Rotation), to = Quaternion.CreateFromRotationMatrix(target);
        return Tween(seconds, t => Rotation = Matrix.CreateFromQuaternion(Quaternion.Slerp(from, to, t)), ease);
    }

    /// <summary>Turns to angles around the world X, Y, then Z axes (like <see cref="SetRotation"/>).</summary>
    public ScriptTimer RotateTo(float xDegrees, float yDegrees, float zDegrees, float seconds, Ease ease = Ease.InOutQuad) =>
        RotateTo(Matrix.CreateRotationX(MathHelper.ToRadians(xDegrees))
               * Matrix.CreateRotationY(MathHelper.ToRadians(yDegrees))
               * Matrix.CreateRotationZ(MathHelper.ToRadians(zDegrees)), seconds, ease);

    internal ScriptTimer Schedule(ScriptTimer timer)
    {
        if (!timer.Scheduled)
        {
            timer.Scheduled = true;
            _timers.Add(timer);
        }
        return timer;
    }

    private void AdvanceTiming(float gameDelta, float realDelta)
    {
        // Timers and coroutines started during this pass wait for the next frame; ones that a callback
        // cancels or stops are skipped and dropped at the end.
        int count = _timers.Count;
        for (int i = 0; i < count && i < _timers.Count; i++) _timers[i].Advance(gameDelta, realDelta);
        _timers.RemoveAll(t =>
        {
            if (!t.IsDone) return false;
            t.Scheduled = false;
            return true;
        });

        count = _coroutines.Count;
        for (int i = 0; i < count && i < _coroutines.Count; i++) _coroutines[i].Step(gameDelta, realDelta);
        _coroutines.RemoveAll(c => c.IsDone);
    }
}
