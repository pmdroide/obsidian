using Engine.Components;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Particles;

/// <summary>An expanding ring on the water surface where something hit it, drawn by the water shader.</summary>
public struct WaterRipple
{
    /// <summary>Where it hit the surface.</summary>
    public Vector3 Point;
    /// <summary>Seconds since the hit.</summary>
    public float Age;
    /// <summary>Wave height scale, about 0.05 (a pebble) .. 2 (a crate from high up).</summary>
    public float Strength;
    /// <summary>Half-size of what hit, metres: the ring starts this wide.</summary>
    public float Radius;
}

/// <summary>
/// Splashes on water: <see cref="ScenePhysics"/> reports every dynamic body crossing a Water-role surface
/// (<see cref="ScenePhysics.WaterEntered"/>), and each fast enough entry throws up a crown of droplets, a central
/// jet for hard hits and a puff of spray, while every entry and exit leaves an expanding ring with foam in the
/// water shader (<see cref="Ripples"/>, Shaders/Forward/Water.fx). Scripts splash anywhere with <see cref="Splash"/>.
/// The droplets are engine-owned particle systems (no gameobject) drawn by the particle pass; a small pool of
/// slots lets several splashes play at once, each with its own surface height that droplets die on.
/// Owned by <see cref="Logic.MainSceneLogic.WaterSplashes"/>; stepped on the game thread, frozen while paused.
/// </summary>
public sealed class WaterSplashes
{
    /// <summary>Rings the water shader draws at once; keep equal to MAX_SPLASHES in Water.fx.</summary>
    public const int MaxRipples = 16;
    /// <summary>Seconds a ring lasts.</summary>
    public const float RippleLifetime = 4f;
    /// <summary>Entries slower than this (m/s) only ripple; a 1.2 m/s fall is about 7 cm.</summary>
    public const float MinSplashSpeed = 1.2f;
    /// <summary>Exits faster than this (m/s) shed a few droplets.</summary>
    public const float MinExitSplashSpeed = 2f;
    private const int SlotCount = 12;

    private sealed class Slot
    {
        public readonly ParticleSystemComponent Crown = CreateCrown();
        public readonly ParticleSystemComponent Jet = CreateJet();
        public readonly ParticleSystemComponent Mist = CreateMist();
        public Matrix Pose = Matrix.Identity;
        public float Age;

        public bool IsAlive => Crown.ParticleCount + Jet.ParticleCount + Mist.ParticleCount > 0;
        public IEnumerable<ParticleSystemComponent> Systems { get { yield return Mist; yield return Crown; yield return Jet; } }
    }

    private readonly Slot[] _slots = new Slot[SlotCount];
    private readonly List<WaterRipple> _ripples = new();
    private readonly List<ParticleSystemComponent> _drawn = new();
    private readonly ParticleStepContext _context = new();

    /// <summary>Off: bodies still float and scripts still get OnWaterEnter, but nothing splashes.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>The rings on the water now, oldest first (at most <see cref="MaxRipples"/>).</summary>
    public IReadOnlyList<WaterRipple> Ripples => _ripples;
    /// <summary>Splashes thrown since the last <see cref="Clear"/> (entries and script splashes; not plain ripples).</summary>
    public int SplashCount { get; private set; }
    /// <summary>Droplets and spray alive now.</summary>
    public int ParticleCount
    {
        get
        {
            int count = 0;
            foreach (Slot slot in _slots)
                if (slot != null) count += slot.Crown.ParticleCount + slot.Jet.ParticleCount + slot.Mist.ParticleCount;
            return count;
        }
    }

    /// <summary>A splash was thrown: its point, impact speed (m/s) and radius (m). Raised on the game thread.</summary>
    public event Action<Vector3, float, float> Splashed;

    /// <summary>What <see cref="ScenePhysics.WaterEntered"/> calls: splash and ring by how fast the body fell in.</summary>
    public void OnWaterEntered(WaterContact contact)
    {
        if (!Enabled) return;
        if (contact.Speed >= MinSplashSpeed) Splash(contact.Point, contact.Speed, contact.Radius, contact.Velocity);
        else if (contact.Speed > 0.2f) AddRipple(contact.Point, contact.Speed / 6f * SizeFactor(contact.Radius), contact.Radius);
    }

    /// <summary>What <see cref="ScenePhysics.WaterExited"/> calls: a body leaving fast sheds droplets and a small ring.</summary>
    public void OnWaterExited(WaterContact contact)
    {
        if (!Enabled || contact.Speed < 0.5f) return;
        AddRipple(contact.Point, contact.Speed / 12f * SizeFactor(contact.Radius), contact.Radius);
        if (contact.Speed >= MinExitSplashSpeed)
            Throw(contact.Point, contact.Speed * 0.35f, contact.Radius * 0.7f, contact.Velocity, jet: false);
    }

    /// <summary>
    /// Throws a splash at <paramref name="point"/> (on the water surface) as if something <paramref name="radius"/>
    /// metres wide hit it at <paramref name="speed"/> m/s: droplets, spray and a ring. <paramref name="velocity"/>
    /// (the body's) leans the droplets the way it was moving.
    /// </summary>
    public void Splash(Vector3 point, float speed, float radius = 0.3f, Vector3 velocity = default)
    {
        if (!Enabled || !IsFinite(point)) return;
        speed = Limit(speed, 0, 40, 0);
        radius = Limit(radius, 0.02f, 5, 0.3f);
        AddRipple(point, speed / 6f * SizeFactor(radius), radius);
        if (speed < MinSplashSpeed) return;
        Throw(point, speed, radius, velocity, jet: true);
        SplashCount++;
        Splashed?.Invoke(point, speed, radius);
    }

    /// <summary>Adds only a ring (and its foam) to the water at <paramref name="point"/>.</summary>
    public void AddRipple(Vector3 point, float strength, float radius = 0.2f)
    {
        if (!IsFinite(point)) return;
        strength = Limit(strength, 0, 2, 0);
        if (strength < 0.01f) return;
        // The shader takes the newest rings; a full list drops the oldest (they have faded the most).
        if (_ripples.Count >= MaxRipples) _ripples.RemoveAt(0);
        _ripples.Add(new WaterRipple { Point = point, Strength = strength, Radius = Limit(radius, 0.02f, 5, 0.2f) });
    }

    /// <summary>Ages the rings and moves the droplets. <paramref name="seconds"/> = 0 (paused) holds everything still.</summary>
    public void Update(float seconds, Vector3 gravity, Vector2 wind)
    {
        float dt = Limit(seconds, 0, ParticleSimulation.MaxStep, 0);
        if (dt <= 0) return;

        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            WaterRipple ripple = _ripples[i];
            ripple.Age += dt;
            if (ripple.Age >= RippleLifetime) _ripples.RemoveAt(i);
            else _ripples[i] = ripple;
        }

        _context.Gravity = gravity;
        _context.Wind = wind;
        foreach (Slot slot in _slots)
        {
            if (slot == null || !slot.IsAlive) continue;
            slot.Age += dt;
            foreach (ParticleSystemComponent system in slot.Systems)
                system.Simulation?.Step(dt, slot.Pose, _context);
        }
    }

    /// <summary>Removes every ring and droplet (scene change, Stop).</summary>
    public void Clear()
    {
        _ripples.Clear();
        foreach (Slot slot in _slots)
            if (slot != null)
                foreach (ParticleSystemComponent system in slot.Systems) system.Clear();
        SplashCount = 0;
    }

    /// <summary>The systems with droplets to draw (the particle pass draws them after the water).</summary>
    public IReadOnlyList<ParticleSystemComponent> VisibleSystems()
    {
        _drawn.Clear();
        foreach (Slot slot in _slots)
        {
            if (slot == null) continue;
            foreach (ParticleSystemComponent system in slot.Systems)
                if (system.ParticleCount > 0) _drawn.Add(system);
        }
        return _drawn;
    }

    /// <summary>
    /// Packs the rings for Water.fx: <paramref name="positions"/> = xyz hit point, w age; <paramref name="shapes"/> =
    /// x strength, y radius. Returns how many were written.
    /// </summary>
    public int FillRipples(Vector4[] positions, Vector4[] shapes)
    {
        int count = Math.Min(_ripples.Count, Math.Min(positions.Length, shapes.Length));
        int first = _ripples.Count - count;
        for (int i = 0; i < count; i++)
        {
            WaterRipple ripple = _ripples[first + i];
            positions[i] = new Vector4(ripple.Point, ripple.Age);
            shapes[i] = new Vector4(ripple.Strength, ripple.Radius, 0, 0);
        }
        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Droplets
    // ---------------------------------------------------------------------------------------------

    private void Throw(Vector3 point, float speed, float radius, Vector3 velocity, bool jet)
    {
        Slot slot = TakeSlot();
        slot.Pose = Matrix.CreateTranslation(point);
        slot.Age = 0;
        // 1 at 6 m/s (a fall of about 1.8 m); bigger bodies throw more, wider and higher.
        float energy = Math.Min(speed / 6f, 2.5f);
        float size = SizeFactor(radius);
        float thrown = Math.Min(speed, 14f) * MathHelper.Clamp(0.55f + 0.3f * size, 0.6f, 1.3f);
        // The droplets lean a little with the body's sideways motion.
        var drift = new Vector3(velocity.X, velocity.Y, 0) * 0.15f;
        // Droplets die when they fall back onto the water they came from.
        float surface = point.Z - 0.03f;

        ParticleSystemComponent crown = slot.Crown;
        crown.Shape.Radius = radius * 0.9f;
        crown.Shape.Angle = 22f + 14f * Math.Min(energy, 1f);
        crown.Main.StartSpeed = new ParticleRange(0.3f * thrown, 0.62f * thrown);
        crown.Main.StartSize = Scaled(0.018f, 0.045f, MathHelper.Clamp(size, 0.6f, 2f));
        crown.Collision.PlaneHeight = surface;
        crown.EmitAt(point, Math.Min((int)(20 + 70 * energy * size), 260), drift);

        if (jet && energy > 0.45f)
        {
            // The Worthington jet: a column that shoots up where the cavity behind the body closes.
            ParticleSystemComponent column = slot.Jet;
            column.Shape.Radius = radius * 0.22f;
            column.Main.StartSpeed = new ParticleRange(0.5f * thrown, 0.95f * thrown);
            column.Main.StartSize = Scaled(0.028f, 0.065f, MathHelper.Clamp(size, 0.6f, 2f));
            column.Collision.PlaneHeight = surface;
            column.EmitAt(point, Math.Min((int)(6 + 22 * energy), 80), drift * 0.5f);
        }

        ParticleSystemComponent mist = slot.Mist;
        mist.Shape.Radius = radius * 0.8f;
        mist.Main.StartSpeed = Scaled(0.3f, 1.4f, MathHelper.Clamp(energy, 0.4f, 1.6f));
        mist.Main.StartSize = Scaled(0.2f, 0.45f, MathHelper.Clamp(size, 0.5f, 2f));
        mist.Collision.PlaneHeight = surface;
        mist.EmitAt(point + new Vector3(0, 0, 0.05f), Math.Min((int)(4 + 14 * energy * size), 60), drift);
    }

    // A free slot, else the one that splashed longest ago.
    private Slot TakeSlot()
    {
        Slot oldest = null;
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] ??= new Slot();
            Slot slot = _slots[i];
            if (!slot.IsAlive) return slot;
            if (oldest == null || slot.Age > oldest.Age) oldest = slot;
        }
        foreach (ParticleSystemComponent system in oldest.Systems) system.Clear();
        return oldest;
    }

    private static ParticleSystemComponent CreateCrown() => new()
    {
        Main = new ParticleMainModule
        {
            Looping = false, PlayOnAwake = false, Duration = 1, MaxParticles = 400,
            StartLifetime = new ParticleRange(0.9f, 1.6f),
            StartColor = new Color(216, 236, 255, 205), StartColor2 = new Color(190, 220, 245, 170),
            GravityModifier = 1, SimulationSpace = ParticleSimulationSpace.World,
        },
        Emission = new ParticleEmissionModule { Enabled = false, RateOverTime = 0 },
        // A ring at the rim of the body, thrown up and out.
        Shape = new ParticleShapeModule { Shape = ParticleShapeType.Cone, RadiusThickness = 0.2f, RandomizeDirection = 0.08f },
        LimitVelocity = new ParticleLimitVelocityModule { Enabled = true, Speed = 100, Drag = 0.4f },
        SizeOverLifetime = new ParticleSizeModule { Enabled = true, Size = new ParticleCurve((0, 1), (1, 0.55f)) },
        Collision = Landing(),
        Renderer = new ParticleRendererModule
        {
            RenderMode = ParticleRenderMode.StretchedBillboard, Texture = ParticleTexture.SoftCircle,
            LengthScale = 1.3f, SpeedScale = 0.022f, Lit = true, Brightness = 1f, SoftParticles = 0.05f, CameraFade = 0.3f,
        },
    };

    private static ParticleSystemComponent CreateJet() => new()
    {
        Main = new ParticleMainModule
        {
            Looping = false, PlayOnAwake = false, Duration = 1, MaxParticles = 200,
            StartLifetime = new ParticleRange(1.2f, 2f),
            StartColor = new Color(225, 240, 255, 215), StartColor2 = new Color(200, 228, 250, 185),
            GravityModifier = 1, SimulationSpace = ParticleSimulationSpace.World,
        },
        Emission = new ParticleEmissionModule { Enabled = false, RateOverTime = 0 },
        Shape = new ParticleShapeModule { Shape = ParticleShapeType.Cone, Angle = 6, RadiusThickness = 1 },
        LimitVelocity = new ParticleLimitVelocityModule { Enabled = true, Speed = 100, Drag = 0.3f },
        SizeOverLifetime = new ParticleSizeModule { Enabled = true, Size = new ParticleCurve((0, 1), (1, 0.6f)) },
        Collision = Landing(),
        Renderer = new ParticleRendererModule
        {
            RenderMode = ParticleRenderMode.StretchedBillboard, Texture = ParticleTexture.SoftCircle,
            LengthScale = 1.6f, SpeedScale = 0.03f, Lit = true, Brightness = 1f, SoftParticles = 0.05f, CameraFade = 0.3f,
        },
    };

    private static ParticleSystemComponent CreateMist() => new()
    {
        Main = new ParticleMainModule
        {
            Looping = false, PlayOnAwake = false, Duration = 1, MaxParticles = 160,
            StartLifetime = new ParticleRange(0.5f, 1f),
            StartColor = new Color(235, 244, 255, 40), StartColor2 = new Color(220, 235, 250, 24),
            StartRotation = new ParticleRange(0, 360),
            GravityModifier = 0.12f, SimulationSpace = ParticleSimulationSpace.World,
        },
        Emission = new ParticleEmissionModule { Enabled = false, RateOverTime = 0 },
        Shape = new ParticleShapeModule { Shape = ParticleShapeType.Hemisphere, RadiusThickness = 0.5f },
        LimitVelocity = new ParticleLimitVelocityModule { Enabled = true, Speed = 100, Drag = 2.5f },
        ExternalForces = new ParticleExternalForcesModule { Enabled = true, Wind = 0.6f, Response = 1 },
        SizeOverLifetime = new ParticleSizeModule { Enabled = true, Size = new ParticleCurve((0, 0.5f), (0.4f, 1.1f), (1, 1.6f)) },
        ColorOverLifetime = new ParticleColorModule
        {
            Enabled = true,
            Color = new ParticleGradient((0, Color.White), (0.3f, Color.White), (1, new Color(255, 255, 255, 0))),
        },
        Collision = Landing(),
        Renderer = new ParticleRendererModule
        {
            RenderMode = ParticleRenderMode.Billboard, Texture = ParticleTexture.Smoke,
            Lit = true, Brightness = 1, SoftParticles = 0.4f, CameraFade = 0.5f,
        },
    };

    // A floor at the splash's water level that kills what lands on it (PlaneHeight is set per splash).
    private static ParticleCollisionModule Landing() => new()
    {
        Enabled = true, Type = ParticleCollisionType.Plane, Bounce = 0, Dampen = 1, LifetimeLoss = 1, RadiusScale = 0,
    };

    // 1 for a body 0.5 m across (radius 0.25); grows with the square root of its size.
    private static float SizeFactor(float radius) => MathHelper.Clamp((float)Math.Sqrt(Math.Max(radius, 0) / 0.25f), 0.3f, 3f);

    private static ParticleRange Scaled(float min, float max, float scale) => new(min * scale, max * scale);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static float Limit(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
