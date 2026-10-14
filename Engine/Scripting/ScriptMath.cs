using Microsoft.Xna.Framework;

namespace Engine.Scripting;

/// <summary>Frame-rate independent smoothing and small number helpers for scripts (pass <c>DeltaTime</c>).</summary>
public static class ScriptMath
{
    /// <summary>Steps <paramref name="current"/> towards <paramref name="target"/> by at most <paramref name="maxDelta"/>, never past it.</summary>
    public static float MoveTowards(float current, float target, float maxDelta) =>
        Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
    {
        Vector3 offset = target - current;
        float distance = offset.Length();
        return distance <= maxDelta || distance < 1e-6f ? target : current + offset / distance * maxDelta;
    }

    /// <summary>
    /// Eases towards a target the same at any frame rate: <paramref name="sharpness"/> ~ how many times per second
    /// the remaining gap shrinks by e. <c>Position = ScriptMath.Damp(Position, goal, 8f, DeltaTime)</c>.
    /// </summary>
    public static float Damp(float current, float target, float sharpness, float deltaTime) =>
        MathHelper.Lerp(current, target, 1f - MathF.Exp(-sharpness * deltaTime));

    public static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float deltaTime) =>
        Vector3.Lerp(current, target, 1f - MathF.Exp(-sharpness * deltaTime));

    /// <summary>Damps an angle in degrees the short way round.</summary>
    public static float DampAngle(float current, float target, float sharpness, float deltaTime) =>
        current + DeltaAngle(current, target) * (1f - MathF.Exp(-sharpness * deltaTime));

    /// <summary>
    /// A spring that reaches <paramref name="target"/> in about <paramref name="smoothTime"/> seconds without
    /// overshooting (a follow camera). Keep <paramref name="velocity"/> in a field between frames.
    /// </summary>
    public static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float deltaTime,
                                   float maxSpeed = float.PositiveInfinity)
    {
        smoothTime = Math.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * deltaTime;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float maxChange = maxSpeed * smoothTime;
        float change = Math.Clamp(current - target, -maxChange, maxChange);
        float goal = current - change;
        float temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        float result = goal + (change + temp) * exp;
        // Don't overshoot the target.
        if (target - current > 0f == result > target)
        {
            result = target;
            velocity = 0f;
        }
        return result;
    }

    public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime, float deltaTime,
                                     float maxSpeed = float.PositiveInfinity)
    {
        smoothTime = Math.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * deltaTime;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        Vector3 change = current - target;
        float maxChange = maxSpeed * smoothTime;
        if (change.LengthSquared() > maxChange * maxChange) change = Vector3.Normalize(change) * maxChange;
        Vector3 goal = current - change;
        Vector3 temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        Vector3 result = goal + (change + temp) * exp;
        if (Vector3.Dot(target - current, result - target) > 0f)
        {
            result = target;
            velocity = Vector3.Zero;
        }
        return result;
    }

    /// <summary>Where <paramref name="value"/> lies between <paramref name="a"/> and <paramref name="b"/> (0 to 1, clamped).</summary>
    public static float InverseLerp(float a, float b, float value) =>
        Math.Abs(b - a) < 1e-12f ? 0f : Math.Clamp((value - a) / (b - a), 0f, 1f);

    /// <summary>Maps <paramref name="value"/> from one range to another (clamped), e.g. a distance to a volume.</summary>
    public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax) =>
        MathHelper.Lerp(toMin, toMax, InverseLerp(fromMin, fromMax, value));

    /// <summary>Wraps <paramref name="t"/> into 0 to <paramref name="length"/> (also for negative values).</summary>
    public static float Repeat(float t, float length) =>
        length <= 0f ? 0f : Math.Clamp(t - MathF.Floor(t / length) * length, 0f, length);

    /// <summary>Goes 0 to <paramref name="length"/> and back as <paramref name="t"/> grows, e.g. <c>PingPong(Time, 1)</c>.</summary>
    public static float PingPong(float t, float length)
    {
        t = Repeat(t, length * 2f);
        return length - Math.Abs(t - length);
    }

    /// <summary>The shortest turn from angle <paramref name="current"/> to <paramref name="target"/>, in degrees (-180 to 180).</summary>
    public static float DeltaAngle(float current, float target)
    {
        float delta = Repeat(target - current, 360f);
        return delta > 180f ? delta - 360f : delta;
    }

    public static bool Approximately(float a, float b, float tolerance = 1e-5f) => Math.Abs(a - b) <= tolerance;
}

/// <summary>Random numbers for scripts (game thread). <see cref="Seed"/> makes runs repeatable.</summary>
public static class ScriptRandom
{
    private static Random _random = new();

    /// <summary>Restarts the sequence: the same seed gives the same numbers.</summary>
    public static void Seed(int seed) => _random = new Random(seed);

    /// <summary>0 (inclusive) to 1 (exclusive).</summary>
    public static float Value => (float)_random.NextDouble();

    /// <summary>A float from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static float Range(float min, float max) => min + (max - min) * Value;

    /// <summary>An int from <paramref name="min"/> up to, not including, <paramref name="maxExclusive"/> (Range(0, list.Count)).</summary>
    public static int Range(int min, int maxExclusive) => maxExclusive <= min ? min : _random.Next(min, maxExclusive);

    /// <summary>True with the given probability (0 to 1): <c>Chance(0.25f)</c> one time in four.</summary>
    public static bool Chance(float probability) => Value < probability;

    /// <summary>-1 or 1.</summary>
    public static float Sign => _random.Next(2) == 0 ? -1f : 1f;

    /// <summary>A uniformly random direction (length 1).</summary>
    public static Vector3 OnUnitSphere
    {
        get
        {
            float z = Range(-1f, 1f), angle = Range(0f, MathHelper.TwoPi), r = MathF.Sqrt(1f - z * z);
            return new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), z);
        }
    }

    /// <summary>A uniformly random point inside a sphere of radius 1.</summary>
    public static Vector3 InsideUnitSphere => OnUnitSphere * MathF.Cbrt(Value);

    /// <summary>A uniformly random point inside a circle of radius 1 (X/Y, the ground plane).</summary>
    public static Vector2 InsideUnitCircle
    {
        get
        {
            float angle = Range(0f, MathHelper.TwoPi), r = MathF.Sqrt(Value);
            return new Vector2(r * MathF.Cos(angle), r * MathF.Sin(angle));
        }
    }

    /// <summary>A random colour with the given saturation and brightness (0 to 1), full alpha.</summary>
    public static Color Color(float saturation = 1f, float value = 1f)
    {
        float h = Value * 6f, c = value * saturation, x = c * (1f - Math.Abs(h % 2f - 1f)), m = value - c;
        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x),
            3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x),
        };
        return new Color(r + m, g + m, b + m);
    }

    /// <summary>A random element, or default for an empty list.</summary>
    public static T Pick<T>(IReadOnlyList<T> items) => items == null || items.Count == 0 ? default : items[_random.Next(items.Count)];

    /// <summary>Shuffles the list in place (Fisher-Yates).</summary>
    public static void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
