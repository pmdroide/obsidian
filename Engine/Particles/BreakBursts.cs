using Engine.Components;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Particles;

/// <summary>What a <see cref="BreakableComponent"/> throws out besides its pieces when it breaks.</summary>
public enum BreakEffect
{
    /// <summary>Only the pieces.</summary>
    None,
    /// <summary>A puff of dust and grit (pottery, plaster, stone).</summary>
    Dust,
    /// <summary>A spray of blood, a red mist and splats on the floor below (a zombie's head).</summary>
    Blood,
}

/// <summary>
/// The particles of breaking things (<see cref="BreakableComponent.Effect"/>): a spray of droplets or grit that dies
/// on the static colliders it hits, a mist or dust puff, and for blood a few splats on the floor below. Like
/// <see cref="WaterSplashes"/> they are engine-owned particle systems (no gameobject) in a small pool of slots,
/// drawn by the particle pass. Owned by <see cref="Logic.MainSceneLogic.BreakBursts"/>; stepped on the game thread,
/// frozen while paused, cleared on Stop and scene changes.
/// </summary>
public sealed class BreakBursts
{
    private const int SlotCount = 8;
    /// <summary>Seconds blood splats lie on the floor.</summary>
    public const float SplatLifetime = 12f;

    private sealed class Slot
    {
        public readonly ParticleSystemComponent Spray = CreateSpray();
        public readonly ParticleSystemComponent Mist = CreateMist();
        public readonly ParticleSystemComponent Splats = CreateSplats();
        public float Age;

        public bool IsAlive => Spray.ParticleCount + Mist.ParticleCount + Splats.ParticleCount > 0;
        public IEnumerable<ParticleSystemComponent> Systems { get { yield return Splats; yield return Mist; yield return Spray; } }
    }

    private readonly Slot[] _slots = new Slot[SlotCount];
    private readonly List<ParticleSystemComponent> _drawn = new();
    private readonly ParticleStepContext _context = new();
    private readonly Random _random = new(1234);

    /// <summary>Bursts thrown since the last <see cref="Clear"/>.</summary>
    public int BurstCount { get; private set; }
    /// <summary>Particles alive now.</summary>
    public int ParticleCount
    {
        get
        {
            int count = 0;
            foreach (Slot slot in _slots)
                if (slot != null) count += slot.Spray.ParticleCount + slot.Mist.ParticleCount + slot.Splats.ParticleCount;
            return count;
        }
    }

    /// <summary>
    /// Throws <paramref name="effect"/> from <paramref name="point"/>: <paramref name="size"/> is the broken object's
    /// size (m), <paramref name="direction"/> the way the hit went (the spray leans along it), <paramref name="amount"/>
    /// scales the particle count. <paramref name="physics"/> finds the floor for blood splats (null: none).
    /// </summary>
    public void Burst(BreakEffect effect, Vector3 point, Vector3 direction, float size, Color color, float amount = 1f,
        Vector3 velocity = default, ScenePhysics physics = null)
    {
        if (effect == BreakEffect.None || !IsFinite(point)) return;
        size = float.IsFinite(size) ? Math.Clamp(size, 0.02f, 4f) : 0.3f;
        amount = float.IsFinite(amount) ? Math.Clamp(amount, 0, 5) : 1f;
        if (!(amount > 0)) return;
        if (!IsFinite(direction)) direction = Vector3.Zero;
        Vector3 along = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : Vector3.Zero;
        Vector3 drift = (IsFinite(velocity) ? velocity : Vector3.Zero) * 0.6f;

        Slot slot = TakeSlot();
        slot.Age = 0;
        float scale = MathHelper.Clamp(size / 0.3f, 0.3f, 3f);
        bool blood = effect == BreakEffect.Blood;
        Color dark = Multiply(color, blood ? 0.45f : 0.7f);

        ParticleSystemComponent spray = slot.Spray;
        spray.Main.StartColor = new Color(color.R, color.G, color.B, (byte)(blood ? 235 : 255));
        spray.Main.StartColor2 = new Color(dark.R, dark.G, dark.B, (byte)(blood ? 215 : 255));
        spray.Main.StartSpeed = new ParticleRange(1.2f * scale, (blood ? 6.5f : 4.5f) * MathF.Sqrt(scale));
        spray.Main.StartSize = blood ? new ParticleRange(0.012f * scale, 0.035f * scale) : new ParticleRange(0.008f * scale, 0.025f * scale);
        spray.Main.StartLifetime = new ParticleRange(0.6f, blood ? 1.6f : 1.2f);
        spray.Shape.Radius = size * 0.35f;
        spray.Renderer.RenderMode = blood ? ParticleRenderMode.StretchedBillboard : ParticleRenderMode.Billboard;
        spray.Renderer.Texture = blood ? ParticleTexture.SoftCircle : ParticleTexture.Square;
        spray.EmitAt(point, Math.Min((int)((blood ? 140 : 90) * amount * MathF.Sqrt(scale)), 600), drift + along * 2.5f * scale);

        ParticleSystemComponent mist = slot.Mist;
        mist.Main.StartColor = new Color(color.R, color.G, color.B, (byte)(blood ? 70 : 150));
        mist.Main.StartColor2 = new Color(dark.R, dark.G, dark.B, (byte)(blood ? 45 : 110));
        mist.Main.StartSize = new ParticleRange(0.25f * scale, 0.6f * scale);
        mist.Main.StartSpeed = new ParticleRange(0.3f * scale, 1.4f * scale);
        mist.Main.StartLifetime = blood ? new ParticleRange(0.35f, 0.8f) : new ParticleRange(1.4f, 2.6f);
        mist.Main.GravityModifier = blood ? 0.25f : 0.04f;
        mist.Shape.Radius = size * 0.4f;
        mist.EmitAt(point, Math.Min((int)((blood ? 14 : 22) * amount * MathF.Sqrt(scale)), 120), drift * 0.5f + along * 0.8f);

        if (blood && physics != null)
        {
            // Splats on the floor below and ahead of the hit.
            int splats = Math.Clamp((int)(7 * amount), 1, 24);
            ParticleSystemComponent floor = slot.Splats;
            Color wet = Multiply(color, 0.8f);
            floor.Main.StartColor = new Color(color.R, color.G, color.B, (byte)230);
            floor.Main.StartColor2 = new Color(wet.R, wet.G, wet.B, (byte)210);
            Vector3 ahead = new Vector3(along.X, along.Y, 0) * 0.8f * scale;
            for (int i = 0; i < splats; i++)
            {
                float angle = (float)_random.NextDouble() * MathHelper.TwoPi, reach = (float)Math.Sqrt(_random.NextDouble()) * 1.1f * scale;
                Vector3 from = point + ahead * (float)_random.NextDouble() + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * reach;
                if (!physics.RaycastStatic(from + Vector3.UnitZ * 0.05f, -Vector3.UnitZ, 6f, out RaycastHit hit) || hit.Normal.Z < 0.6f) continue;
                floor.Main.StartSize = new ParticleRange(0.08f * scale, 0.32f * scale);
                floor.EmitAt(hit.Point + hit.Normal * 0.006f, 1);
            }
        }
        BurstCount++;
    }

    /// <summary>Moves the particles. <paramref name="seconds"/> = 0 (paused) holds everything still.</summary>
    public void Update(float seconds, Vector3 gravity, Vector2 wind, ScenePhysics physics)
    {
        float dt = float.IsFinite(seconds) ? Math.Clamp(seconds, 0, ParticleSimulation.MaxStep) : 0;
        if (dt <= 0) return;
        _context.Gravity = gravity;
        _context.Wind = wind;
        // Droplets and grit die on walls and floors; the flying pieces don't stop them.
        _context.Raycast = physics == null ? null : (Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out Entities.BasicEntity hit) =>
        {
            bool found = physics.RaycastStatic(origin, direction, maxDistance, out RaycastHit result);
            point = result.Point;
            normal = result.Normal;
            hit = result.GameObject;
            return found;
        };
        foreach (Slot slot in _slots)
        {
            if (slot == null || !slot.IsAlive) continue;
            slot.Age += dt;
            foreach (ParticleSystemComponent system in slot.Systems)
                system.Simulation?.Step(dt, Matrix.Identity, _context);
        }
    }

    /// <summary>Removes every particle (scene change, Stop).</summary>
    public void Clear()
    {
        foreach (Slot slot in _slots)
            if (slot != null)
                foreach (ParticleSystemComponent system in slot.Systems) system.Clear();
        BurstCount = 0;
    }

    /// <summary>The systems with particles to draw.</summary>
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

    // A free slot, else the one that burst longest ago.
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

    private static ParticleSystemComponent CreateSpray() => new()
    {
        Main = new ParticleMainModule
        {
            Looping = false, PlayOnAwake = false, Duration = 1, MaxParticles = 1200,
            GravityModifier = 1, SimulationSpace = ParticleSimulationSpace.World, StartRotation = new ParticleRange(0, 360),
        },
        Emission = new ParticleEmissionModule { Enabled = false, RateOverTime = 0 },
        Shape = new ParticleShapeModule { Shape = ParticleShapeType.Sphere, RadiusThickness = 1f },
        LimitVelocity = new ParticleLimitVelocityModule { Enabled = true, Speed = 100, Drag = 0.6f },
        SizeOverLifetime = new ParticleSizeModule { Enabled = true, Size = new ParticleCurve((0, 1), (1, 0.7f)) },
        Collision = new ParticleCollisionModule
        {
            Enabled = true, Type = ParticleCollisionType.World, Bounce = 0.1f, Dampen = 0.9f, LifetimeLoss = 1, RadiusScale = 0.5f,
        },
        Renderer = new ParticleRendererModule
        {
            RenderMode = ParticleRenderMode.StretchedBillboard, Texture = ParticleTexture.SoftCircle,
            LengthScale = 1.4f, SpeedScale = 0.025f, Lit = true, Brightness = 1f, SoftParticles = 0.03f, CameraFade = 0.3f,
        },
    };

    private static ParticleSystemComponent CreateMist() => new()
    {
        Main = new ParticleMainModule
        {
            Looping = false, PlayOnAwake = false, Duration = 1, MaxParticles = 240,
            StartRotation = new ParticleRange(0, 360), SimulationSpace = ParticleSimulationSpace.World,
        },
        Emission = new ParticleEmissionModule { Enabled = false, RateOverTime = 0 },
        Shape = new ParticleShapeModule { Shape = ParticleShapeType.Sphere, RadiusThickness = 0.6f },
        LimitVelocity = new ParticleLimitVelocityModule { Enabled = true, Speed = 100, Drag = 2.2f },
        ExternalForces = new ParticleExternalForcesModule { Enabled = true, Wind = 0.5f, Response = 1 },
        SizeOverLifetime = new ParticleSizeModule { Enabled = true, Size = new ParticleCurve((0, 0.5f), (0.35f, 1.1f), (1, 1.8f)) },
        ColorOverLifetime = new ParticleColorModule
        {
            Enabled = true,
            Color = new ParticleGradient((0, Color.White), (0.25f, Color.White), (1, new Color(255, 255, 255, 0))),
        },
        Renderer = new ParticleRendererModule
        {
            RenderMode = ParticleRenderMode.Billboard, Texture = ParticleTexture.Smoke,
            Lit = true, Brightness = 1, SoftParticles = 0.4f, CameraFade = 0.5f,
        },
    };

    // Flat blots lying on the floor, fading at the end of their life.
    private static ParticleSystemComponent CreateSplats() => new()
    {
        Main = new ParticleMainModule
        {
            Looping = false, PlayOnAwake = false, Duration = 1, MaxParticles = 96,
            StartLifetime = new ParticleRange(SplatLifetime * 0.8f, SplatLifetime), StartSpeed = 0,
            StartRotation = new ParticleRange(0, 360), SimulationSpace = ParticleSimulationSpace.World,
        },
        Emission = new ParticleEmissionModule { Enabled = false, RateOverTime = 0 },
        Shape = new ParticleShapeModule { Shape = ParticleShapeType.Sphere, Radius = 0.001f, RadiusThickness = 0 },
        SizeOverLifetime = new ParticleSizeModule { Enabled = true, Size = new ParticleCurve((0, 0.4f), (0.02f, 1f), (1, 1.05f)) },
        ColorOverLifetime = new ParticleColorModule
        {
            Enabled = true,
            Color = new ParticleGradient((0, Color.White), (0.85f, Color.White), (1, new Color(255, 255, 255, 0))),
        },
        Renderer = new ParticleRendererModule
        {
            RenderMode = ParticleRenderMode.HorizontalBillboard, Texture = ParticleTexture.SoftCircle,
            Lit = true, Brightness = 0.9f, SoftParticles = 0f, CameraFade = 0.2f,
        },
    };

    private static Color Multiply(Color color, float factor) =>
        new((byte)Math.Clamp(color.R * factor, 0, 255), (byte)Math.Clamp(color.G * factor, 0, 255), (byte)Math.Clamp(color.B * factor, 0, 255), color.A);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
