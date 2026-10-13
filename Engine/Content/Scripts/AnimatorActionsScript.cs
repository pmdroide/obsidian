using Engine.Components;
using Engine.Logic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Scripting;

/// <summary>
/// The AnimationTest sample's Animator actions: drives a character that has Idle, Walk, Run, Walk Back
/// and Dash actions on its Animator, and plays the one that matches what it does.
///   Up / Down      walk forward / back (Shift runs)      Left / Right   turn
///   Space          dash: a one-shot action, then back to what played before
///   P              pause / resume the animation          K              stop (bind pose) / play
/// The Mixamo bots face their local -Y.
/// </summary>
public sealed class AnimatorActionsScript : ScriptBehaviour
{
    public const string ScriptId = "animator-actions";

    [Tooltip("Animator actions this script plays.")]
    public string IdleAction = "Idle";
    public string WalkAction = "Walk";
    public string RunAction = "Run";
    public string BackAction = "Walk Back";
    public string DashAction = "Dash";

    [Range(0.1f, 10)] [Tooltip("Metres per second while walking; match the clip's stride so the feet don't slide.")]
    public float WalkSpeed = 1.5f;
    [Range(0.1f, 15)] public float RunSpeed = 2.7f;
    [Range(0.1f, 15)] public float DashSpeed = 3.8f;
    [Range(10, 720)] [Tooltip("Degrees per second.")]
    public float TurnSpeed = 140f;
    [Range(0.5f, 20)] [Tooltip("How far from its start the character may go, in metres.")]
    public float Leash = 1.8f;

    private Vector3 _start;
    private AnimatorComponent _animator;

    public override void Start()
    {
        _start = Position;
        _animator = Animator;
        if (_animator == null)
        {
            Log("Animator Actions needs an Animator component on this gameobject.");
            return;
        }
        // Scripts may start before the Animator: Play works either way.
        _animator.Play(IdleAction);
        _animator.ActionFinished += OnActionFinished;
        Log("Animator Actions: arrow keys walk (Shift runs), Space dashes, P pauses, K stops the animation.");
    }

    public override void Stop()
    {
        if (_animator != null) _animator.ActionFinished -= OnActionFinished;
    }

    public override void Update()
    {
        if (_animator == null || DebugScreen.ConsoleOpen) return;

        if (GameInput.WasPressed(Keys.P))
        {
            if (_animator.IsPaused) _animator.Resume();
            else _animator.Pause();
        }
        if (GameInput.WasPressed(Keys.K))
        {
            if (_animator.IsStopped) _animator.Play();
            else _animator.Stop();
        }
        // A frozen character doesn't slide around either.
        if (_animator.IsStopped || _animator.IsPaused) return;

        float turn = (GameInput.IsDown(Keys.Left) ? 1 : 0) - (GameInput.IsDown(Keys.Right) ? 1 : 0);
        if (turn != 0) Rotate(Vector3.UnitZ, turn * TurnSpeed * DeltaTime);

        if (GameInput.WasPressed(Keys.Space) && !_animator.IsPlayingAction(DashAction))
            _animator.PlayOnce(DashAction); // returns to the previous action when the clip ends

        bool forward = GameInput.IsDown(Keys.Up), back = GameInput.IsDown(Keys.Down);
        bool run = GameInput.IsDown(Keys.LeftShift) || GameInput.IsDown(Keys.RightShift);
        float speed;
        if (_animator.IsPlayingAction(DashAction)) speed = DashSpeed;
        else if (forward && !back)
        {
            _animator.Play(run ? RunAction : WalkAction); // no-op while it already plays
            speed = run ? RunSpeed : WalkSpeed;
        }
        else if (back && !forward)
        {
            _animator.Play(BackAction);
            speed = -WalkSpeed;
        }
        else
        {
            _animator.Play(IdleAction);
            speed = 0;
        }

        Vector3 next = Position - Forward * speed * DeltaTime;
        Vector3 offset = next - _start;
        offset.Z = 0;
        if (offset.Length() > Leash) next = new Vector3(_start.X, _start.Y, next.Z) + offset * (Leash / offset.Length());
        Position = next;
    }

    // Raised when a non-looping action ends (here the Dash), before the Animator returns to the previous one.
    private void OnActionFinished(AnimatorComponent animator, string action) =>
        Log($"Animator Actions: '{action}' finished.");
}
