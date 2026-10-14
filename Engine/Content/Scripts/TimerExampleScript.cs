using System.Collections;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Timers, a coroutine and tweens on any gameobject: it breathes (a looping ping-pong tween), hops every few
/// seconds (a coroutine yielding MoveTo tweens) and logs a short countdown (After/Every timers).
/// </summary>
public sealed class TimerExampleScript : ScriptBehaviour
{
    public const string ScriptId = "timer-example";

    [SerializeField, Range(0.2f, 10f), Tooltip("Seconds of rest between hops.")]
    private float _hopInterval = 2f;
    [SerializeField, Range(0f, 5f), Tooltip("How high each hop goes (metres).")]
    private float _hopHeight = 1f;
    [SerializeField, Tooltip("The shape of the fall back down.")]
    private Ease _landing = Ease.OutBounce;
    [SerializeField, Range(0f, 0.5f), Tooltip("How much the scale breathes (0.1 = 10%).")]
    private float _pulse = 0.1f;

    private Vector3 _start;
    private Vector3 _scale;

    public override void Start()
    {
        _start = Position;
        _scale = Scale;

        // A tween that plays forwards then backwards, for good.
        Tween(1f, 1f + _pulse, 0.6f, s => Scale = _scale * s, Ease.InOutSine).Loop(pingPong: true);

        // A one-shot timer, then a repeating one that runs three times: 3, 2, 1, Go! as the first hop starts.
        After(0.5f, () => Log("Timer Example: first hop in 3 s"));
        int count = 3;
        Every(1f, () => Log(count-- + "..."), times: 3).OnComplete(() => Log("Go!"));

        // A coroutine reads top to bottom; each yield waits without blocking the game.
        StartCoroutine(Hop());
    }

    private IEnumerator Hop()
    {
        yield return new WaitForSeconds(3f);
        while (true)
        {
            yield return MoveTo(_start + Vector3.UnitZ * _hopHeight, 0.35f, Ease.OutQuad);
            yield return MoveTo(_start, 0.6f, _landing);
            yield return new WaitForSeconds(_hopInterval);
        }
    }
}
