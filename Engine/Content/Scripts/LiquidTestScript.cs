using Engine.Components;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>
/// Moves a bottle of the LiquidTest scene so its Liquid component shows what it does: Swing rocks it like a hand
/// carrying it, Jolt knocks it sideways now and then, Pour tips it over and empties it, then stands it back up full.
/// </summary>
public sealed class LiquidTestScript : ScriptBehaviour
{
    public const string ScriptId = "liquid-test";

    public enum Motion { Swing, Jolt, Pour }

    [Tooltip("Swing rocks it, Jolt knocks it sideways now and then, Pour tips it over and empties it.")]
    public Motion Mode = Motion.Swing;
    [Range(0f, 120f), Tooltip("Swing: rocking angle (degrees). Pour: how far it tips.")]
    public float Angle = 25f;
    [Range(0.2f, 20f), Tooltip("Seconds per swing, between jolts, or per pour.")]
    public float Period = 2f;

    private LiquidComponent _liquid;
    private Vector3 _home;
    private Matrix _rest;
    private float _time;
    private float _fill;

    public override void Start()
    {
        _liquid = GetComponent<LiquidComponent>();
        _home = Position;
        _rest = Rotation;
        _time = 0;
        _fill = _liquid?.Fill ?? 0.6f;
    }

    public override void Update()
    {
        _time += DeltaTime;
        float period = MathHelper.Max(Period, 0.2f);
        switch (Mode)
        {
            case Motion.Swing:
            {
                // Rock about X with a little sideways sway, like a bottle carried in a hand.
                float phase = _time / period * MathHelper.TwoPi;
                Rotation = _rest * Matrix.CreateRotationX(MathHelper.ToRadians(Angle) * MathF.Sin(phase));
                Position = _home + new Vector3(0.08f * MathF.Sin(phase * 0.5f), 0, 0);
                break;
            }
            case Motion.Jolt:
            {
                // A quick shove and back every period: the liquid sloshes, foams and slowly settles.
                float t = _time % period;
                float shove = t < 0.12f ? MathF.Sin(t / 0.12f * MathHelper.Pi) : 0;
                Position = _home + new Vector3(0.12f * shove, 0, 0);
                break;
            }
            case Motion.Pour:
            {
                // Tip over (0 -> Angle), hold while it empties, stand back up and refill.
                float t = _time % (period * 2) / period;
                float tip = t < 0.35f ? Easing.Evaluate(Ease.InOutSine, t / 0.35f)
                    : t < 1.2f ? 1
                    : t < 1.6f ? 1 - Easing.Evaluate(Ease.InOutSine, (t - 1.2f) / 0.4f)
                    : 0;
                // Turn about the middle of the bottle, not its base.
                Matrix turned = _rest * Matrix.CreateRotationY(MathHelper.ToRadians(Angle) * tip);
                Vector3 pivot = new Vector3(0, 0, 0.15f);
                Rotation = turned;
                Position = _home + Vector3.Transform(pivot, _rest) - Vector3.Transform(pivot, turned);
                if (_liquid == null) break;
                // Liquid runs out once the neck points below level.
                if (tip > 0.8f && Angle > 95f) _liquid.Fill = MathHelper.Max(_liquid.Fill - DeltaTime * 0.35f, 0);
                if (t >= 1.8f) _liquid.Fill = _fill;
                break;
            }
        }
    }

    public override void Stop()
    {
        if (_liquid != null) _liquid.Fill = _fill;
    }
}
