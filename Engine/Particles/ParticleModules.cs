using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace Engine.Particles;

// Settings of a particle system, grouped into modules like Unity's Shuriken. Everything here is saved in the
// scene (System.Text.Json, default options), so enums carry a string converter and XNA vectors/colours carry
// the compact array converters below. Enum values are saved by name: never rename one.

/// <summary>Where particles live: Local ones move and turn with the gameobject, World ones stay where they were emitted.</summary>
public enum ParticleSimulationSpace { Local, World }

/// <summary>The volume or surface new particles start from (local axes; Z is up).</summary>
public enum ParticleShapeType
{
    /// <summary>A disc of <c>Radius</c> in X/Y, directions opening up to <c>Angle</c> around +Z.</summary>
    Cone,
    /// <summary>A ball of <c>Radius</c>, directions outwards.</summary>
    Sphere,
    /// <summary>The upper (+Z) half of a ball, directions outwards.</summary>
    Hemisphere,
    /// <summary>A box of <c>BoxSize</c>, directions +Z.</summary>
    Box,
    /// <summary>A ring/disc of <c>Radius</c> in X/Y, directions outwards in the plane.</summary>
    Circle,
    /// <summary>A line of length 2 x <c>Radius</c> along X, directions +Z.</summary>
    Edge,
}

public enum ParticleRenderMode
{
    /// <summary>Quads facing the camera.</summary>
    Billboard,
    /// <summary>Quads stretched along their velocity (sparks, rain).</summary>
    StretchedBillboard,
    /// <summary>Quads lying flat (ground ripples, shockwaves).</summary>
    HorizontalBillboard,
    /// <summary>Quads facing the camera but standing upright.</summary>
    VerticalBillboard,
}

public enum ParticleBlendMode
{
    /// <summary>Alpha blended: smoke, dust, leaves.</summary>
    Alpha,
    /// <summary>Adds light: fire, sparks, magic. Above 1 Brightness feeds the bloom.</summary>
    Additive,
}

public enum ParticleSortMode { None, Distance, OldestFirst, YoungestFirst }

/// <summary>Particle images. All but Custom are painted at startup (<see cref="ParticleTextures"/>).</summary>
public enum ParticleTexture { SoftCircle, Glow, Spark, Smoke, SmokeSheet, Flame, Star, Ring, Square, Custom }

public enum ParticleCollisionType
{
    /// <summary>A flat floor at <c>PlaneHeight</c> (world Z). Cheap.</summary>
    Plane,
    /// <summary>Every Physics collider of the scene (a ray per moving particle).</summary>
    World,
}

public enum ParticleSubEmitterTrigger
{
    /// <summary>Once when a particle is born.</summary>
    Birth,
    /// <summary>Continuously along each living particle's path; Count is per second.</summary>
    Trail,
    /// <summary>Each time a particle bounces off a collider.</summary>
    Collision,
    /// <summary>When a particle's lifetime ends (not when cleared).</summary>
    Death,
}

/// <summary>Where the density of a volumetric system (<see cref="ParticleVolumetricModule"/>) comes from.</summary>
public enum ParticleVolumeSource
{
    /// <summary>Every particle adds a soft ball of density (its size, colour and alpha): any effect becomes a volume.</summary>
    Particles,
    /// <summary>
    /// Counter-Strike 2 style: the smoke floods a voxel grid from the emitter, filling rooms and flowing through
    /// doorways and around corners but never through Static colliders.
    /// </summary>
    Voxels,
}

/// <summary>A constant or a random value between two constants (Min = Max is a constant).</summary>
public struct ParticleRange
{
    public float Min { get; set; }
    public float Max { get; set; }

    public ParticleRange(float min, float max)
    {
        Min = min;
        Max = max;
    }

    public ParticleRange(float value) : this(value, value) { }

    public static implicit operator ParticleRange(float value) => new(value);

    [JsonIgnore] public bool IsConstant => Min == Max;

    /// <summary>The value at <paramref name="t"/> (0..1) between Min and Max; non-finite ends count as 0.</summary>
    public float Lerp(float t) => Finite(Min) + (Finite(Max) - Finite(Min)) * t;

    public float Average => (Finite(Min) + Finite(Max)) * 0.5f;
    public float Largest => Math.Max(Finite(Min), Finite(Max));

    private static float Finite(float v) => float.IsFinite(v) ? v : 0;

    public override string ToString() => IsConstant ? Min.ToString("0.###") : $"{Min:0.###}..{Max:0.###}";
}

public sealed class ParticleCurveKey
{
    /// <summary>Share of the particle's lifetime, 0..1.</summary>
    public float Time { get; set; }
    public float Value { get; set; } = 1f;

    public ParticleCurveKey() { }
    public ParticleCurveKey(float time, float value)
    {
        Time = time;
        Value = value;
    }
}

/// <summary>A value over a particle's lifetime: keys joined by straight lines (any order), flat before the first and after the last.</summary>
public sealed class ParticleCurve
{
    public const int MaxKeys = 8;
    public List<ParticleCurveKey> Keys { get; set; } = new();

    public ParticleCurve() { }
    public ParticleCurve(params (float Time, float Value)[] keys) =>
        Keys = keys.Select(k => new ParticleCurveKey(k.Time, k.Value)).ToList();

    public float Evaluate(float t)
    {
        var keys = Keys;
        if (keys == null || keys.Count == 0) return 1f;
        // One pass for the keys on each side of t, so unsorted keys from a script still work.
        ParticleCurveKey before = null, after = null;
        for (int i = 0; i < keys.Count; i++)
        {
            ParticleCurveKey key = keys[i];
            if (key == null || !float.IsFinite(key.Time) || !float.IsFinite(key.Value)) continue;
            if (key.Time <= t && (before == null || key.Time >= before.Time)) before = key;
            if (key.Time >= t && (after == null || key.Time < after.Time)) after = key;
        }
        if (before == null) return after?.Value ?? 1f;
        if (after == null || after.Time <= before.Time) return before.Value;
        return MathHelper.Lerp(before.Value, after.Value, (t - before.Time) / (after.Time - before.Time));
    }
}

public sealed class ParticleGradientKey
{
    /// <summary>Share of the particle's lifetime, 0..1.</summary>
    public float Time { get; set; }
    /// <summary>Multiplies the start colour; alpha is opacity.</summary>
    [JsonConverter(typeof(ParticleColorJsonConverter))]
    public Color Color { get; set; } = Color.White;

    public ParticleGradientKey() { }
    public ParticleGradientKey(float time, Color color)
    {
        Time = time;
        Color = color;
    }
}

/// <summary>A colour over a particle's lifetime, blended between keys like <see cref="ParticleCurve"/>.</summary>
public sealed class ParticleGradient
{
    public const int MaxKeys = 8;
    public List<ParticleGradientKey> Keys { get; set; } = new();

    public ParticleGradient() { }
    public ParticleGradient(params (float Time, Color Color)[] keys) =>
        Keys = keys.Select(k => new ParticleGradientKey(k.Time, k.Color)).ToList();

    /// <summary>RGBA 0..1 (sRGB, like the colour picker).</summary>
    public Vector4 Evaluate(float t)
    {
        var keys = Keys;
        if (keys == null || keys.Count == 0) return Vector4.One;
        ParticleGradientKey before = null, after = null;
        for (int i = 0; i < keys.Count; i++)
        {
            ParticleGradientKey key = keys[i];
            if (key == null || !float.IsFinite(key.Time)) continue;
            if (key.Time <= t && (before == null || key.Time >= before.Time)) before = key;
            if (key.Time >= t && (after == null || key.Time < after.Time)) after = key;
        }
        if (before == null) return after?.Color.ToVector4() ?? Vector4.One;
        if (after == null || after.Time <= before.Time) return before.Color.ToVector4();
        return Vector4.Lerp(before.Color.ToVector4(), after.Color.ToVector4(), (t - before.Time) / (after.Time - before.Time));
    }
}

/// <summary>Duration, looping and what every particle starts with.</summary>
public sealed class ParticleMainModule
{
    public const int MaxParticlesLimit = 100000;

    /// <summary>Seconds of one emission cycle (bursts are timed within it).</summary>
    public float Duration { get; set; } = 5f;
    public bool Looping { get; set; } = true;
    /// <summary>Looping systems start as if they had already run a whole cycle.</summary>
    public bool Prewarm { get; set; }
    /// <summary>Seconds before the first emission.</summary>
    public ParticleRange StartDelay { get; set; }
    /// <summary>Seconds each particle lives.</summary>
    public ParticleRange StartLifetime { get; set; } = 5f;
    /// <summary>Metres per second along the shape's direction.</summary>
    public ParticleRange StartSpeed { get; set; } = 5f;
    /// <summary>Metres across.</summary>
    public ParticleRange StartSize { get; set; } = 1f;
    /// <summary>Degrees.</summary>
    public ParticleRange StartRotation { get; set; }
    /// <summary>Each particle picks a colour between this and <see cref="StartColor2"/>.</summary>
    [JsonConverter(typeof(ParticleColorJsonConverter))]
    public Color StartColor { get; set; } = Color.White;
    [JsonConverter(typeof(ParticleColorJsonConverter))]
    public Color StartColor2 { get; set; } = Color.White;
    /// <summary>Share of the scene's gravity: 1 falls like a stone, negative rises.</summary>
    public float GravityModifier { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleSimulationSpace SimulationSpace { get; set; } = ParticleSimulationSpace.Local;
    /// <summary>Time scale of this system.</summary>
    public float SimulationSpeed { get; set; } = 1f;
    /// <summary>Starts with Play (and plays in the editor); otherwise a script or a parent's sub-emitter starts it.</summary>
    public bool PlayOnAwake { get; set; } = true;
    public int MaxParticles { get; set; } = 1000;

    [JsonIgnore] public int ClampedMaxParticles => Math.Clamp(MaxParticles, 0, MaxParticlesLimit);
    [JsonIgnore] public float ClampedDuration => float.IsFinite(Duration) ? Math.Clamp(Duration, 0.05f, 3600f) : 5f;
    [JsonIgnore] public float ClampedSimulationSpeed => float.IsFinite(SimulationSpeed) ? Math.Clamp(SimulationSpeed, 0, 100) : 1f;
}

/// <summary>A number of particles at once, at <see cref="Time"/> into each cycle.</summary>
public sealed class ParticleBurst
{
    public float Time { get; set; }
    public ParticleRange Count { get; set; } = 30f;
    /// <summary>How many times it fires per cycle; 0 = every <see cref="Interval"/> until the cycle ends.</summary>
    public int Cycles { get; set; } = 1;
    /// <summary>Seconds between repeats.</summary>
    public float Interval { get; set; } = 0.1f;
    /// <summary>Chance (0..1) each repeat fires.</summary>
    public float Probability { get; set; } = 1f;

    public ParticleBurst() { }
    public ParticleBurst(float time, ParticleRange count, int cycles = 1, float interval = 0.1f)
    {
        Time = time;
        Count = count;
        Cycles = cycles;
        Interval = interval;
    }
}

public sealed class ParticleEmissionModule
{
    public bool Enabled { get; set; } = true;
    /// <summary>Particles per second.</summary>
    public float RateOverTime { get; set; } = 10f;
    /// <summary>Particles per metre the gameobject moves (trails behind moving emitters).</summary>
    public float RateOverDistance { get; set; }
    public List<ParticleBurst> Bursts { get; set; } = new();
}

public sealed class ParticleShapeModule
{
    /// <summary>Off: every particle starts at the gameobject, moving along +Z.</summary>
    public bool Enabled { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleShapeType Shape { get; set; } = ParticleShapeType.Cone;
    /// <summary>Cone opening, degrees (0 = straight up, 90 = flat).</summary>
    public float Angle { get; set; } = 25f;
    /// <summary>Metres.</summary>
    public float Radius { get; set; } = 1f;
    /// <summary>1 emits from the whole volume, 0 only from the surface/rim.</summary>
    public float RadiusThickness { get; set; } = 1f;
    /// <summary>Degrees of the circle used by Cone, Circle, Sphere and Hemisphere (360 = all around).</summary>
    public float Arc { get; set; } = 360f;
    [JsonConverter(typeof(ParticleVector3JsonConverter))]
    public Vector3 BoxSize { get; set; } = Vector3.One;
    /// <summary>Shape offset from the gameobject (local metres).</summary>
    [JsonConverter(typeof(ParticleVector3JsonConverter))]
    public Vector3 Position { get; set; }
    /// <summary>Shape rotation on the gameobject (local degrees, X then Y then Z).</summary>
    [JsonConverter(typeof(ParticleVector3JsonConverter))]
    public Vector3 Rotation { get; set; }
    /// <summary>Blends each direction towards a random one, 0..1.</summary>
    public float RandomizeDirection { get; set; }
}

/// <summary>Velocity added on top of each particle's own, every frame of its life (it doesn't accumulate).</summary>
public sealed class ParticleVelocityModule
{
    public bool Enabled { get; set; }
    /// <summary>Metres per second.</summary>
    [JsonConverter(typeof(ParticleVector3JsonConverter))]
    public Vector3 Linear { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleSimulationSpace Space { get; set; } = ParticleSimulationSpace.Local;
    /// <summary>Degrees per second around the gameobject's local X, Y and Z axes (swirls, vortices).</summary>
    [JsonConverter(typeof(ParticleVector3JsonConverter))]
    public Vector3 Orbital { get; set; }
    /// <summary>Metres per second away from the gameobject (negative pulls in).</summary>
    public float Radial { get; set; }
    /// <summary>Multiplies the particle's own velocity over its lifetime.</summary>
    public ParticleCurve SpeedModifier { get; set; } = new((0, 1), (1, 1));
}

public sealed class ParticleLimitVelocityModule
{
    public bool Enabled { get; set; }
    /// <summary>Metres per second above which particles slow down; 0 = no limit.</summary>
    public float Speed { get; set; } = 1f;
    /// <summary>Share of the excess speed removed (about per 1/30 s), 0..1.</summary>
    public float Dampen { get; set; } = 0.5f;
    /// <summary>Air resistance, per second.</summary>
    public float Drag { get; set; }
}

/// <summary>Constant acceleration, e.g. smoke rising, leaves blown sideways.</summary>
public sealed class ParticleForceModule
{
    public bool Enabled { get; set; }
    /// <summary>Metres per second squared.</summary>
    [JsonConverter(typeof(ParticleVector3JsonConverter))]
    public Vector3 Force { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleSimulationSpace Space { get; set; } = ParticleSimulationSpace.World;
}

/// <summary>The scene's wind (Environment > Wind Speed/Direction) carries the particles along.</summary>
public sealed class ParticleExternalForcesModule
{
    public bool Enabled { get; set; }
    /// <summary>Multiplies the scene's wind.</summary>
    public float Wind { get; set; } = 1f;
    /// <summary>How fast particles pick up the wind's speed, per second (light dust high, sparks low).</summary>
    public float Response { get; set; } = 1.5f;
}

/// <summary>Smooth turbulence added to the movement.</summary>
public sealed class ParticleNoiseModule
{
    public bool Enabled { get; set; }
    /// <summary>Metres per second of wander.</summary>
    public float Strength { get; set; } = 1f;
    /// <summary>Swirls per metre: low is lazy, high is jittery.</summary>
    public float Frequency { get; set; } = 0.5f;
    /// <summary>How fast the pattern changes over time.</summary>
    public float ScrollSpeed { get; set; } = 0.5f;
}

public sealed class ParticleColorModule
{
    public bool Enabled { get; set; }
    public ParticleGradient Color { get; set; } = new((0, Microsoft.Xna.Framework.Color.White), (1, new Color(255, 255, 255, 0)));
}

public sealed class ParticleSizeModule
{
    public bool Enabled { get; set; }
    /// <summary>Multiplies the start size.</summary>
    public ParticleCurve Size { get; set; } = new((0, 1), (1, 0));
}

public sealed class ParticleRotationModule
{
    public bool Enabled { get; set; }
    /// <summary>Degrees per second.</summary>
    public ParticleRange AngularVelocity { get; set; } = 45f;
}

public sealed class ParticleCollisionModule
{
    public bool Enabled { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleCollisionType Type { get; set; } = ParticleCollisionType.World;
    /// <summary>Plane only: world height (Z) of the floor.</summary>
    public float PlaneHeight { get; set; }
    /// <summary>Share of the speed into the surface kept as bounce, 0..1+.</summary>
    public float Bounce { get; set; } = 0.5f;
    /// <summary>Share of the speed along the surface lost per hit, 0..1.</summary>
    public float Dampen { get; set; } = 0.1f;
    /// <summary>Share of the lifetime each hit uses up, 0..1 (1 kills on contact).</summary>
    public float LifetimeLoss { get; set; }
    /// <summary>Collision radius as a share of half the particle size.</summary>
    public float RadiusScale { get; set; } = 1f;
    /// <summary>Particles slower than this after a hit (m/s) die.</summary>
    public float MinKillSpeed { get; set; }
}

/// <summary>Plays a flipbook: the texture is a grid of TilesX x TilesY frames, read left to right, top to bottom.</summary>
public sealed class ParticleTextureSheetModule
{
    public bool Enabled { get; set; }
    public int TilesX { get; set; } = 4;
    public int TilesY { get; set; } = 4;
    /// <summary>Times the whole sheet plays over a particle's life.</summary>
    public float Cycles { get; set; } = 1f;
    public bool RandomStartFrame { get; set; }

    [JsonIgnore] public int ClampedTilesX => Math.Clamp(TilesX, 1, 64);
    [JsonIgnore] public int ClampedTilesY => Math.Clamp(TilesY, 1, 64);
}

/// <summary>Particles of another particle system's gameobject, spawned where this system's particles are born, trail, hit or die.</summary>
public sealed class ParticleSubEmitter
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleSubEmitterTrigger Trigger { get; set; } = ParticleSubEmitterTrigger.Death;
    /// <summary>Name of the gameobject whose Particle System emits (give it Play On Awake off and no Rate).</summary>
    public string Target { get; set; } = "";
    /// <summary>Particles per event (per second for Trail).</summary>
    public ParticleRange Count { get; set; } = 10f;
    /// <summary>Share of the parent particle's velocity the new ones get.</summary>
    public float InheritVelocity { get; set; }
    /// <summary>Tints the new particles with the parent particle's colour.</summary>
    public bool InheritColor { get; set; }
}

public sealed class ParticleSubEmittersModule
{
    public bool Enabled { get; set; }
    public List<ParticleSubEmitter> Emitters { get; set; } = new();
}

public sealed class ParticleRendererModule
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleRenderMode RenderMode { get; set; } = ParticleRenderMode.Billboard;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleBlendMode Blend { get; set; } = ParticleBlendMode.Alpha;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleTexture Texture { get; set; } = ParticleTexture.SoftCircle;
    /// <summary>Custom only: content path without extension, e.g. "GameObjects/Textures/fire_sheet".</summary>
    public string TexturePath { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleSortMode SortMode { get; set; } = ParticleSortMode.None;
    /// <summary>Multiplies the colour; above 1 glows through the bloom.</summary>
    public float Brightness { get; set; } = 1f;
    /// <summary>Shaded by the sun and sky (smoke, dust) instead of drawn at full colour (fire, magic).</summary>
    public bool Lit { get; set; }
    /// <summary>Metres over which particles fade into geometry instead of cutting into it.</summary>
    public float SoftParticles { get; set; } = 0.3f;
    /// <summary>Metres from the camera within which particles fade out.</summary>
    public float CameraFade { get; set; } = 0.5f;
    /// <summary>Stretched only: length as a multiple of the size.</summary>
    public float LengthScale { get; set; } = 2f;
    /// <summary>Stretched only: extra length per metre per second of speed.</summary>
    public float SpeedScale { get; set; }
}

/// <summary>
/// Draws the system as a lit volume (raymarched density) instead of flat billboards: thick smoke you can walk
/// into, which hides what is behind it and which bullets (<see cref="Components.ParticleSystemComponent.Disturb"/>)
/// punch holes through. The density comes from the particles or from a voxel flood fill (<see cref="Source"/>).
/// </summary>
public sealed class ParticleVolumetricModule
{
    public const int MaxResolution = 64;
    public const int MaxCarves = 16;

    public bool Enabled { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParticleVolumeSource Source { get; set; } = ParticleVolumeSource.Particles;
    /// <summary>Also draw the particles as billboards (Voxels: e.g. the jet of smoke from the canister).</summary>
    public bool DrawParticles { get; set; }

    /// <summary>Extinction per metre at full density: higher is thicker.</summary>
    public float Density { get; set; } = 4f;
    /// <summary>0..1: how far the detail noise eats into the volume (wispy edges, holes as it thins).</summary>
    public float NoiseErosion { get; set; } = 0.5f;
    /// <summary>Noise features per metre.</summary>
    public float NoiseScale { get; set; } = 0.7f;
    /// <summary>Metres per second the noise drifts upwards (rolling smoke).</summary>
    public float NoiseSpeed { get; set; } = 0.25f;
    /// <summary>Raymarch samples per pixel at most (quality vs. cost).</summary>
    public int Steps { get; set; } = 48;
    /// <summary>Seconds a bullet tunnel or blast hole takes to fill in again.</summary>
    public float RefillTime { get; set; } = 2.5f;

    /// <summary>Particles: grid cells along the longest side of the particles' bounds.</summary>
    public int Resolution { get; set; } = 32;

    /// <summary>Voxels: edge of one voxel, metres.</summary>
    public float VoxelSize { get; set; } = 0.35f;
    /// <summary>Voxels: cubic metres the smoke fills (a corridor gets a long thin smoke, a room a round one).</summary>
    public float Volume { get; set; } = 110f;
    /// <summary>Voxels: furthest the smoke spreads from the emitter, metres.</summary>
    public float MaxRadius { get; set; } = 7f;
    /// <summary>Voxels: seconds the smoke takes to bloom to its full volume.</summary>
    public float ExpandTime { get; set; } = 1.4f;
    /// <summary>Voxels: seconds it takes to dissipate once the system stops emitting (outside in).</summary>
    public float FadeTime { get; set; } = 3f;
    /// <summary>Voxels: cost of spreading upwards; above 1 it spreads wider than tall like a settling smoke.</summary>
    public float Flatten { get; set; } = 1.5f;

    [JsonIgnore] public int ClampedSteps => Math.Clamp(Steps, 8, 128);
    [JsonIgnore] public int ClampedResolution => Math.Clamp(Resolution, 4, MaxResolution - 2);
    [JsonIgnore] public float ClampedVoxelSize => float.IsFinite(VoxelSize) ? Math.Clamp(VoxelSize, 0.05f, 4f) : 0.35f;
    [JsonIgnore] public float ClampedMaxRadius => float.IsFinite(MaxRadius) ? Math.Clamp(MaxRadius, 0.25f, 100f) : 7f;
}

// --------------------------------------------------------------------------------------------------------
// Compact JSON for XNA types (components are saved with default options): [x,y,z] and [r,g,b,a].
// --------------------------------------------------------------------------------------------------------

public sealed class ParticleVector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [x,y,z]");
        var values = new float[3];
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            float value = reader.GetSingle();
            if (i < 3) values[i] = value;
            i++;
        }
        return new Vector3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}

public sealed class ParticleColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [r,g,b,a]");
        var values = new int[] { 255, 255, 255, 255 };
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int value = reader.GetInt32();
            if (i < 4) values[i] = Math.Clamp(value, 0, 255);
            i++;
        }
        return new Color(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.R);
        writer.WriteNumberValue(value.G);
        writer.WriteNumberValue(value.B);
        writer.WriteNumberValue(value.A);
        writer.WriteEndArray();
    }
}
