using System.Collections;

namespace Engine.Scripting;

/// <summary>
/// A coroutine a script started with <see cref="ScriptBehaviour.StartCoroutine"/>: an iterator method that runs
/// until its first <c>yield</c> straight away, then resumes after its script's Update once what it yielded is over:
/// <list type="bullet">
/// <item><c>null</c>: the next frame.</item>
/// <item>a number or <see cref="WaitForSeconds"/>: that many seconds of game time (held while paused).</item>
/// <item><see cref="WaitForSecondsRealtime"/>, <see cref="WaitForFrames"/>, <see cref="WaitUntil"/>, <see cref="WaitWhile"/>.</item>
/// <item>a <see cref="ScriptTimer"/> (e.g. a tween) or another <see cref="ScriptCoroutine"/>: until it is done.</item>
/// <item>a <see cref="Logic.SceneLoadOperation"/>: until the scene is built (ready to swap) or the load ended.</item>
/// <item>an <see cref="IEnumerator"/>: runs it as a nested step, then carries on.</item>
/// </list>
/// It ends with its script (Stop, disable, removal); an exception inside it stops the script like one in Update.
/// </summary>
public sealed class ScriptCoroutine
{
    private enum WaitKind { None, Frames, GameSeconds, RealSeconds, Until, While, Timer, Coroutine }

    private readonly Stack<IEnumerator> _stack = new();
    private WaitKind _wait;
    private float _seconds;
    private int _frames;
    private Func<bool> _condition;
    private ScriptTimer _timer;
    private ScriptCoroutine _other;

    internal ScriptCoroutine(IEnumerator routine) => _stack.Push(routine);

    public bool IsDone { get; private set; }
    public bool IsRunning => !IsDone;

    /// <summary>Ends the coroutine where it is; it never resumes.</summary>
    public void Stop()
    {
        IsDone = true;
        _stack.Clear();
        _condition = null;
        _timer = null;
        _other = null;
    }

    /// <summary>Resumes once the current wait is over, running the iterator to its next yield.</summary>
    internal void Step(float gameDelta, float realDelta)
    {
        if (IsDone || !WaitOver(gameDelta, realDelta)) return;
        while (!IsDone)
        {
            if (_stack.Count == 0) { Stop(); return; }
            IEnumerator top = _stack.Peek();
            bool more = top.MoveNext();
            if (IsDone) return; // stopped itself (or its script) inside the step
            if (!more)
            {
                // A finished nested routine hands straight back to its caller, in the same frame.
                _stack.Pop();
                continue;
            }
            if (top.Current is IEnumerator nested)
            {
                _stack.Push(nested);
                continue;
            }
            SetWait(top.Current);
            return;
        }
    }

    private void SetWait(object yielded)
    {
        _condition = null;
        _timer = null;
        _other = null;
        switch (yielded)
        {
            case float f: Seconds(WaitKind.GameSeconds, f); break;
            case double d: Seconds(WaitKind.GameSeconds, (float)d); break;
            case int i: Seconds(WaitKind.GameSeconds, i); break;
            case WaitForSeconds w: Seconds(WaitKind.GameSeconds, w.Seconds); break;
            case WaitForSecondsRealtime w: Seconds(WaitKind.RealSeconds, w.Seconds); break;
            case WaitForFrames w: _wait = WaitKind.Frames; _frames = Math.Max(1, w.Frames); break;
            case WaitUntil w: _wait = WaitKind.Until; _condition = w.Condition; break;
            case WaitWhile w: _wait = WaitKind.While; _condition = w.Condition; break;
            case ScriptTimer t: _wait = WaitKind.Timer; _timer = t; break;
            case ScriptCoroutine c when c != this: _wait = WaitKind.Coroutine; _other = c; break;
            case Logic.SceneLoadOperation load: _wait = WaitKind.Until; _condition = () => load.IsReady || load.IsDone; break;
            default: _wait = WaitKind.Frames; _frames = 1; break; // null and anything else: next frame
        }
    }

    private void Seconds(WaitKind kind, float seconds)
    {
        _wait = kind;
        _seconds = seconds;
    }

    private bool WaitOver(float gameDelta, float realDelta)
    {
        switch (_wait)
        {
            case WaitKind.Frames: return --_frames <= 0;
            case WaitKind.GameSeconds: return (_seconds -= gameDelta) <= 0f;
            case WaitKind.RealSeconds: return (_seconds -= realDelta) <= 0f;
            case WaitKind.Until: return _condition == null || _condition();
            case WaitKind.While: return _condition == null || !_condition();
            case WaitKind.Timer: return _timer == null || _timer.IsDone;
            case WaitKind.Coroutine: return _other == null || _other.IsDone;
            default: return true;
        }
    }
}

/// <summary><c>yield return new WaitForSeconds(2)</c>: resume after 2 seconds of game time (held while paused). Reusable.</summary>
public sealed class WaitForSeconds
{
    public WaitForSeconds(float seconds) => Seconds = seconds;
    public float Seconds { get; }
}

/// <summary>Like <see cref="WaitForSeconds"/>, but keeps counting while paused (in scripts that update while paused).</summary>
public sealed class WaitForSecondsRealtime
{
    public WaitForSecondsRealtime(float seconds) => Seconds = seconds;
    public float Seconds { get; }
}

/// <summary>Resume after this many frames (1 = the next frame, like <c>yield return null</c>).</summary>
public sealed class WaitForFrames
{
    public WaitForFrames(int frames) => Frames = frames;
    public int Frames { get; }
}

/// <summary>Resume on the first frame the condition is true (checked after each Update).</summary>
public sealed class WaitUntil
{
    public WaitUntil(Func<bool> condition) => Condition = condition;
    public Func<bool> Condition { get; }
}

/// <summary>Resume on the first frame the condition is false (checked after each Update).</summary>
public sealed class WaitWhile
{
    public WaitWhile(Func<bool> condition) => Condition = condition;
    public Func<bool> Condition { get; }
}
