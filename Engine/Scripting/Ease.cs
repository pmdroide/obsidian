namespace Engine.Scripting;

/// <summary>
/// The shape of a tween (see easings.net): In starts slow, Out ends slow, InOut does both.
/// Back overshoots, Elastic wobbles and Bounce bounces at the end. Usable as a serialized script field.
/// </summary>
public enum Ease
{
    Linear,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InQuart, OutQuart, InOutQuart,
    InSine, OutSine, InOutSine,
    InExpo, OutExpo, InOutExpo,
    InCirc, OutCirc, InOutCirc,
    InBack, OutBack, InOutBack,
    InElastic, OutElastic, InOutElastic,
    InBounce, OutBounce, InOutBounce,
    SmoothStep,
}

public static class Easing
{
    private const float Back = 1.70158f;
    private const float BackInOut = Back * 1.525f;

    /// <summary>Maps <paramref name="t"/> (0 to 1, clamped) through the curve: 0 at the start, 1 at the end.</summary>
    public static float Evaluate(Ease ease, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        switch (ease)
        {
            case Ease.InQuad: return t * t;
            case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
            case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;
            case Ease.InCubic: return t * t * t;
            case Ease.OutCubic: return 1f - MathF.Pow(1f - t, 3f);
            case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
            case Ease.InQuart: return t * t * t * t;
            case Ease.OutQuart: return 1f - MathF.Pow(1f - t, 4f);
            case Ease.InOutQuart: return t < 0.5f ? 8f * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 4f) / 2f;
            case Ease.InSine: return 1f - MathF.Cos(t * MathF.PI / 2f);
            case Ease.OutSine: return MathF.Sin(t * MathF.PI / 2f);
            case Ease.InOutSine: return -(MathF.Cos(MathF.PI * t) - 1f) / 2f;
            case Ease.InExpo: return t <= 0f ? 0f : MathF.Pow(2f, 10f * t - 10f);
            case Ease.OutExpo: return t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
            case Ease.InOutExpo:
                if (t <= 0f || t >= 1f) return t;
                return t < 0.5f ? MathF.Pow(2f, 20f * t - 10f) / 2f : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f;
            case Ease.InCirc: return 1f - MathF.Sqrt(1f - t * t);
            case Ease.OutCirc: return MathF.Sqrt(1f - (t - 1f) * (t - 1f));
            case Ease.InOutCirc:
                return t < 0.5f
                    ? (1f - MathF.Sqrt(1f - 4f * t * t)) / 2f
                    : (MathF.Sqrt(1f - MathF.Pow(-2f * t + 2f, 2f)) + 1f) / 2f;
            case Ease.InBack: return (Back + 1f) * t * t * t - Back * t * t;
            case Ease.OutBack: return 1f + (Back + 1f) * MathF.Pow(t - 1f, 3f) + Back * MathF.Pow(t - 1f, 2f);
            case Ease.InOutBack:
                return t < 0.5f
                    ? MathF.Pow(2f * t, 2f) * ((BackInOut + 1f) * 2f * t - BackInOut) / 2f
                    : (MathF.Pow(2f * t - 2f, 2f) * ((BackInOut + 1f) * (t * 2f - 2f) + BackInOut) + 2f) / 2f;
            case Ease.InElastic:
                if (t <= 0f || t >= 1f) return t;
                return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * (2f * MathF.PI / 3f));
            case Ease.OutElastic:
                if (t <= 0f || t >= 1f) return t;
                return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * (2f * MathF.PI / 3f)) + 1f;
            case Ease.InOutElastic:
                if (t <= 0f || t >= 1f) return t;
                const float c5 = 2f * MathF.PI / 4.5f;
                return t < 0.5f
                    ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * c5)) / 2f
                    : MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * c5) / 2f + 1f;
            case Ease.InBounce: return 1f - OutBounce(1f - t);
            case Ease.OutBounce: return OutBounce(t);
            case Ease.InOutBounce: return t < 0.5f ? (1f - OutBounce(1f - 2f * t)) / 2f : (1f + OutBounce(2f * t - 1f)) / 2f;
            case Ease.SmoothStep: return t * t * (3f - 2f * t);
            default: return t;
        }
    }

    private static float OutBounce(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1f / d) return n * t * t;
        if (t < 2f / d) return n * (t -= 1.5f / d) * t + 0.75f;
        if (t < 2.5f / d) return n * (t -= 2.25f / d) * t + 0.9375f;
        return n * (t -= 2.625f / d) * t + 0.984375f;
    }
}
