using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Particles;

/// <summary>One live particle. Positions and velocities are world space; colours are sRGB 0..1.</summary>
public struct Particle
{
    public Vector3 Position;
    /// <summary>Own velocity: start speed, gravity, forces, wind, drag and bounces accumulate here.</summary>
    public Vector3 Velocity;
    /// <summary>What it moved with last step (own velocity scaled, plus velocity over lifetime and noise); stretched billboards follow it.</summary>
    public Vector3 DrawVelocity;
    public float Age;
    public float Lifetime;
    public float StartSize;
    public float Size;
    /// <summary>Radians.</summary>
    public float Rotation;
    public Vector4 StartColor;
    public Vector4 Color;
    /// <summary>Texture sheet frame (0 without the module).</summary>
    public float Frame;
    public float StartFrame;
    /// <summary>0..1, fixed per particle: picks its spin, noise pattern and frame.</summary>
    public float Random;
    /// <summary>Fractional particles a Trail sub-emitter still owes.</summary>
    public float TrailCarry;

    /// <summary>Share of its life used, 0..1.</summary>
    public readonly float NormalizedAge => Lifetime > 0 ? Math.Clamp(Age / Lifetime, 0, 1) : 1;
}

/// <summary>A particle bouncing off a collider (<see cref="ParticleSimulation.Collided"/>).</summary>
public struct ParticleCollisionEvent
{
    /// <summary>The gameobject hit; null for the Plane collision type.</summary>
    public BasicEntity GameObject;
    public Vector3 Point;
    public Vector3 Normal;
    /// <summary>The particle's velocity before the bounce.</summary>
    public Vector3 Velocity;
}

/// <summary>Closest collider along a ray, for World collisions.</summary>
public delegate bool ParticleRaycast(Vector3 origin, Vector3 direction, float maxDistance,
    out Vector3 point, out Vector3 normal, out BasicEntity hit);

/// <summary>What a simulation needs from the scene for one step.</summary>
public sealed class ParticleStepContext
{
    public Vector3 Gravity = new(0, 0, -9.81f);
    /// <summary>Scene wind, m/s (X/Y).</summary>
    public Vector2 Wind;
    /// <summary>World collisions; null skips them.</summary>
    public ParticleRaycast Raycast;
    /// <summary>Static colliders only (walls a voxel smoke can't flood through); null floods freely.</summary>
    public ParticleRaycast RaycastStatic;
    /// <summary>Towards the sun (unit), or null without one: voxels a Static collider shades get only sky light.</summary>
    public Vector3? SunDirection;
    /// <summary>Resolves a sub-emitter's gameobject name to its particle system; null skips sub-emitters.</summary>
    public Func<string, ParticleSimulation> FindSubEmitter;
}

/// <summary>
/// CPU simulation of one <see cref="ParticleSystemComponent"/>: emission over a looping cycle, the shape,
/// every lifetime module, collisions and sub-emitters. Settings are read live from the component each step,
/// so inspector edits apply at once. It needs no graphics device.
/// </summary>
public sealed class ParticleSimulation
{
    // Long frames (a hitch, a breakpoint) are cut so nothing tunnels or explodes.
    public const float MaxStep = 0.1f;
    private const float PrewarmStep = 1f / 30f;

    private readonly ParticleSystemComponent _system;
    private ParticleVolume _volume;
    private ParticleLights _lights;
    private Particle[] _particles = Array.Empty<Particle>();
    private int _count;

    private bool _playing, _emitting, _paused;
    private float _time, _delay, _rateCarry, _distanceCarry, _noiseTime;
    private int[] _burstFired = Array.Empty<int>();

    private Matrix _world = Matrix.Identity, _previousWorld = Matrix.Identity;
    private bool _hasWorld;
    private ParticleStepContext _context;
    private uint _rng;

    // Sub-emitter targets resolved for the current step, by trigger.
    private readonly List<(ParticleSubEmitter Settings, ParticleSimulation Target)> _subEmitters = new();
    private bool _hasBirth, _hasTrail, _hasCollision, _hasDeath;

    public ParticleSimulation(ParticleSystemComponent system, uint seed = 0)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _rng = seed != 0 ? seed : (uint)Environment.TickCount ^ (uint)system.InstanceId.GetHashCode() | 1u;
    }

    /// <summary>Raised for each particle that bounces off a collider.</summary>
    public event Action<ParticleCollisionEvent> Collided;

    public ParticleSystemComponent System => _system;
    public ReadOnlySpan<Particle> Particles => new(_particles, 0, _count);
    public int ParticleCount => _count;
    /// <summary>Playing and not paused (also while the last particles of a finished cycle fade out).</summary>
    public bool IsPlaying => _playing && !_paused;
    /// <summary>Still creating particles (false once a non-looping cycle ends, or after Stop).</summary>
    public bool IsEmitting => _playing && _emitting;
    public bool IsPaused => _paused;
    /// <summary>Has particles (or a volume) or will make more.</summary>
    public bool IsAlive => _count > 0 || IsEmitting || _volume?.HasDensity == true;
    /// <summary>Seconds into the current cycle.</summary>
    public float Time => _time;
    /// <summary>Bounces since the system was created or cleared.</summary>
    public int CollisionCount { get; private set; }
    /// <summary>Particles emitted since the system was created or cleared.</summary>
    public long EmittedCount { get; private set; }
    /// <summary>The gameobject's pose at the last step (rotation and translation).</summary>
    public Matrix World => _world;
    /// <summary>The density grid of a Volumetric system (null until the module first runs).</summary>
    public ParticleVolume Volume => _volume;
    /// <summary>The point lights of the Lights module (null until the module first runs).</summary>
    public ParticleLights Lights => _lights;
    /// <summary>Emitting right now (past the Start Delay): a voxel smoke floods while this holds.</summary>
    internal bool IsVolumeEmitting => _playing && _emitting && !_paused && _delay <= 0;
    /// <summary>Where a voxel smoke starts: the shape's position on the gameobject.</summary>
    internal Vector3 VolumeSeed => Vector3.Transform(_system.Shape.Enabled ? _system.Shape.Position : Vector3.Zero, _world);

    /// <param name="context">Scene gravity/wind/colliders for Prewarm; defaults to the last step's.</param>
    public void Play(ParticleStepContext context = null)
    {
        // Resuming after Pause carries on where it stopped.
        if (_paused)
        {
            _paused = false;
            if (_playing || _count > 0) return;
        }
        // A finished cycle whose last particles (or smoke) still fade out starts emitting again.
        if (_playing && _emitting) return;
        _playing = true;
        _emitting = true;
        _paused = false;
        RestartCycle();
        _delay = Math.Max(0, _system.Main.StartDelay.Lerp(NextFloat()));
        if (_system.Main.Prewarm && _system.Main.Looping)
        {
            float delay = _delay;
            _delay = 0;
            Prewarm(_system.Main.ClampedDuration, context);
            _delay = delay;
        }
    }

    /// <summary>Places the gameobject without moving Local particles (before the first step, or a teleport).</summary>
    public void SetPose(Matrix emitterWorld)
    {
        _world = _previousWorld = emitterWorld;
        _hasWorld = true;
    }

    /// <summary>Stops emitting; existing particles live out their lives unless <paramref name="clear"/>.</summary>
    public void Stop(bool clear = false)
    {
        _playing = false;
        _emitting = false;
        _paused = false;
        if (clear) Clear();
    }

    public void Pause()
    {
        if (_playing || _count > 0) _paused = true;
    }

    /// <summary>Removes every particle (no Death sub-emitters).</summary>
    public void Clear()
    {
        _volume?.Clear();
        _lights?.Clear();
        _count = 0;
        CollisionCount = 0;
        EmittedCount = 0;
    }

    /// <summary>Stops, clears and forgets the pose (the next step starts from the gameobject again).</summary>
    public void Reset()
    {
        Stop(clear: true);
        _hasWorld = false;
        _time = 0;
        _rateCarry = _distanceCarry = 0;
    }

    /// <summary>Emits <paramref name="count"/> particles from the shape now, playing or not.</summary>
    public void Emit(int count)
    {
        for (int i = 0; i < count; i++)
            if (!Spawn(0, 0)) break;
    }

    /// <summary>
    /// Emits <paramref name="count"/> particles from this system's shape centred on a world position (sub-emitters, scripts).
    /// <paramref name="velocity"/> is added to theirs; <paramref name="tint"/> (sRGB 0..1) multiplies their start colour.
    /// </summary>
    public void EmitAt(Vector3 position, Vector3 velocity, Vector4 tint, int count)
    {
        for (int i = 0; i < count; i++)
            if (!Spawn(0, 0, position, velocity, tint)) break;
    }

    /// <summary>Advances <paramref name="seconds"/> in fixed steps at the current pose (prewarm, scripts fast-forwarding).</summary>
    public void Prewarm(float seconds, ParticleStepContext context = null)
    {
        context ??= _context ?? new ParticleStepContext();
        Matrix world = _world;
        int steps = (int)Math.Min(Math.Ceiling(seconds / PrewarmStep), 36000);
        for (int i = 0; i < steps; i++) Step(PrewarmStep, world, context);
    }

    /// <summary>
    /// Advances the system by <paramref name="deltaSeconds"/> with the gameobject at <paramref name="emitterWorld"/>
    /// (rotation and translation, no scale).
    /// </summary>
    public void Step(float deltaSeconds, Matrix emitterWorld, ParticleStepContext context)
    {
        context ??= new ParticleStepContext();
        _context = context;
        if (!_hasWorld)
        {
            _world = _previousWorld = emitterWorld;
            _hasWorld = true;
        }
        else
        {
            _previousWorld = _world;
            _world = emitterWorld;
        }
        if (_paused) return;

        ParticleMainModule main = _system.Main;
        // Local particles ride along with the gameobject.
        if (main.SimulationSpace == ParticleSimulationSpace.Local && _previousWorld != _world) MoveWithEmitter();

        float dt = Math.Clamp(float.IsFinite(deltaSeconds) ? deltaSeconds : 0, 0, MaxStep) * main.ClampedSimulationSpeed;
        if (dt <= 0) return;
        _noiseTime = (_noiseTime + dt) % 4096f;

        ResolveSubEmitters(context);
        UpdateParticles(dt, context);
        if (_playing) EmitOverTime(dt);
        if (_system.Volumetric.Enabled || _volume != null) (_volume ??= new ParticleVolume()).Update(this, dt, context);
        if (_system.Lights.Enabled || _lights != null) (_lights ??= new ParticleLights()).Update(_system.Lights, Particles, dt);
        // A voxel smoke keeps the system playing until it has dissipated.
        if (_playing && !_emitting && _count == 0 && _volume?.HasDensity != true) _playing = false;
    }

    // -------------------------------------------------------------------------------------------------
    // Emission
    // -------------------------------------------------------------------------------------------------

    private void RestartCycle()
    {
        _time = 0;
        _rateCarry = 0;
        _distanceCarry = 0;
        int bursts = _system.Emission.Bursts?.Count ?? 0;
        if (_burstFired.Length < bursts) _burstFired = new int[bursts];
        Array.Clear(_burstFired);
    }

    private void EmitOverTime(float dt)
    {
        ParticleMainModule main = _system.Main;
        ParticleEmissionModule emission = _system.Emission;
        float remaining = dt;
        if (_delay > 0)
        {
            float used = Math.Min(_delay, remaining);
            _delay -= used;
            remaining -= used;
            if (_delay > 0) return;
        }

        // Rate over distance: spread along the gameobject's path this frame.
        if (_emitting && emission.Enabled && emission.RateOverDistance > 0 && float.IsFinite(emission.RateOverDistance))
        {
            float moved = Vector3.Distance(_previousWorld.Translation, _world.Translation);
            if (moved < 100) // a teleport isn't a path
            {
                _distanceCarry += moved * emission.RateOverDistance;
                int n = (int)_distanceCarry;
                _distanceCarry -= n;
                for (int i = 0; i < n; i++)
                    if (!Spawn(NextFloat() * remaining, dt)) break;
            }
        }

        int guard = 0;
        while (remaining > 1e-6f && _emitting && guard++ < 64)
        {
            float duration = main.ClampedDuration;
            float step = Math.Min(remaining, duration - _time);
            remaining -= step;
            if (emission.Enabled) EmitInterval(_time, _time + step, remaining, dt);
            _time += step;
            if (_time < duration - 1e-5f) continue;
            if (main.Looping) RestartCycle();
            else
            {
                _time = duration;
                _emitting = false;
            }
        }
    }

    /// <param name="after">Frame time left after this interval, so particles born earlier are older.</param>
    private void EmitInterval(float t0, float t1, float after, float frame)
    {
        ParticleEmissionModule emission = _system.Emission;
        float span = t1 - t0;
        if (emission.RateOverTime > 0 && float.IsFinite(emission.RateOverTime))
        {
            _rateCarry += emission.RateOverTime * span;
            int n = (int)_rateCarry;
            _rateCarry -= n;
            for (int i = 0; i < n; i++)
                if (!Spawn(NextFloat() * span + after, frame)) break;
        }

        var bursts = emission.Bursts;
        if (bursts == null) return;
        if (_burstFired.Length < bursts.Count) Array.Resize(ref _burstFired, bursts.Count);
        for (int b = 0; b < bursts.Count; b++)
        {
            ParticleBurst burst = bursts[b];
            if (burst == null || !float.IsFinite(burst.Time)) continue;
            float interval = float.IsFinite(burst.Interval) ? Math.Max(burst.Interval, 0.01f) : 0.1f;
            // Repeats already behind this interval (a burst added mid-cycle) are skipped, not fired late.
            if (burst.Time + _burstFired[b] * interval < t0)
                _burstFired[b] = Math.Max(_burstFired[b], (int)Math.Ceiling((t0 - burst.Time) / interval - 1e-4f));
            while (burst.Cycles <= 0 || _burstFired[b] < burst.Cycles)
            {
                float at = burst.Time + _burstFired[b] * interval;
                if (at >= t1) break;
                _burstFired[b]++;
                if (at < t0) continue;
                if (burst.Probability < 1 && NextFloat() >= burst.Probability) continue;
                int count = (int)Math.Round(Math.Max(0, burst.Count.Lerp(NextFloat())));
                float age = t1 - at + after;
                for (int i = 0; i < count; i++)
                    if (!Spawn(age, frame)) break;
            }
        }
    }

    /// <summary>A new particle from the shape at the gameobject. <paramref name="age"/> seconds of it already passed this frame.</summary>
    private bool Spawn(float age, float frame) => Spawn(age, frame, null, Vector3.Zero, Vector4.One);

    private bool Spawn(float age, float frame, Vector3? at, Vector3 addedVelocity, Vector4 tint)
    {
        ParticleMainModule main = _system.Main;
        int max = main.ClampedMaxParticles;
        if (_count >= max) return false;
        if (_particles.Length <= _count)
            Array.Resize(ref _particles, Math.Min(max, Math.Max(16, _particles.Length * 2)));

        float lifetime = main.StartLifetime.Lerp(NextFloat());
        if (!(lifetime > 0.001f)) return true; // nothing to show, but keep emitting the rest
        if (age >= lifetime) return true;

        SampleShape(out Vector3 localPosition, out Vector3 localDirection);
        Vector3 position, direction;
        if (at.HasValue)
        {
            position = at.Value + Vector3.TransformNormal(localPosition, _world);
            direction = Vector3.TransformNormal(localDirection, _world);
        }
        else
        {
            position = Vector3.Transform(localPosition, _world);
            direction = Vector3.TransformNormal(localDirection, _world);
            // A moving gameobject leaves particles born earlier in the frame behind on its path.
            if (frame > 0 && age > 0)
                position += (_previousWorld.Translation - _world.Translation) * Math.Min(1, age / frame);
        }

        Vector4 color = Vector4.Lerp(main.StartColor.ToVector4(), main.StartColor2.ToVector4(), NextFloat()) * tint;
        float size = Math.Max(0, main.StartSize.Lerp(NextFloat()));
        var particle = new Particle
        {
            Velocity = direction * main.StartSpeed.Lerp(NextFloat()) + addedVelocity,
            Lifetime = lifetime,
            StartSize = size,
            Size = size,
            Rotation = MathHelper.ToRadians(main.StartRotation.Lerp(NextFloat())),
            StartColor = color,
            Color = color,
            Random = NextFloat(),
        };
        ParticleTextureSheetModule sheet = _system.TextureSheetAnimation;
        if (sheet.Enabled && sheet.RandomStartFrame)
            particle.StartFrame = (float)Math.Floor(NextFloat() * sheet.ClampedTilesX * sheet.ClampedTilesY);
        // Born mid-frame: catch up on the part of the frame it already lived.
        particle.Age = age;
        particle.Position = position + particle.Velocity * age;
        particle.DrawVelocity = particle.Velocity;
        ApplyLifetimeLook(ref particle);

        _particles[_count++] = particle;
        EmittedCount++;
        if (_hasBirth) FireSubEmitters(ParticleSubEmitterTrigger.Birth, in particle, particle.Position, 1);
        return true;
    }

    private void SampleShape(out Vector3 position, out Vector3 direction)
    {
        ParticleShapeModule shape = _system.Shape;
        if (!shape.Enabled)
        {
            position = Vector3.Zero;
            direction = Vector3.UnitZ;
            return;
        }
        float radius = Finite(shape.Radius, 1, 0, 10000);
        float thickness = Finite(shape.RadiusThickness, 1, 0, 1);
        float arc = MathHelper.ToRadians(Finite(shape.Arc, 360, 0, 360));
        float phi = NextFloat() * arc;
        float cos = (float)Math.Cos(phi), sin = (float)Math.Sin(phi);
        switch (shape.Shape)
        {
            case ParticleShapeType.Sphere:
            case ParticleShapeType.Hemisphere:
            {
                float z = shape.Shape == ParticleShapeType.Sphere ? NextFloat() * 2 - 1 : NextFloat();
                float ring = (float)Math.Sqrt(Math.Max(0, 1 - z * z));
                direction = new Vector3(ring * cos, ring * sin, z);
                float inner = 1 - thickness;
                float f = (float)Math.Cbrt(MathHelper.Lerp(inner * inner * inner, 1, NextFloat()));
                position = direction * radius * f;
                break;
            }
            case ParticleShapeType.Box:
            {
                Vector3 size = shape.BoxSize;
                position = new Vector3((NextFloat() - 0.5f) * Finite(size.X, 1, 0, 10000),
                    (NextFloat() - 0.5f) * Finite(size.Y, 1, 0, 10000), (NextFloat() - 0.5f) * Finite(size.Z, 1, 0, 10000));
                direction = Vector3.UnitZ;
                break;
            }
            case ParticleShapeType.Circle:
            {
                float f = AnnulusFraction(thickness);
                position = new Vector3(cos, sin, 0) * radius * f;
                direction = new Vector3(cos, sin, 0);
                break;
            }
            case ParticleShapeType.Edge:
                position = new Vector3((NextFloat() * 2 - 1) * radius, 0, 0);
                direction = Vector3.UnitZ;
                break;
            default: // Cone
            {
                float f = AnnulusFraction(thickness);
                position = new Vector3(cos, sin, 0) * radius * f;
                // Particles further out lean further, up to the cone angle at the rim.
                float tilt = MathHelper.ToRadians(Finite(shape.Angle, 25, 0, 90)) * f;
                float s = (float)Math.Sin(tilt);
                direction = new Vector3(s * cos, s * sin, (float)Math.Cos(tilt));
                break;
            }
        }

        float randomize = Finite(shape.RandomizeDirection, 0, 0, 1);
        if (randomize > 0)
        {
            Vector3 blended = Vector3.Lerp(direction, RandomUnit(), randomize);
            direction = blended.LengthSquared() > 1e-8f ? Vector3.Normalize(blended) : RandomUnit();
        }
        if (shape.Rotation != Vector3.Zero || shape.Position != Vector3.Zero)
        {
            Vector3 r = shape.Rotation;
            Matrix rotation = Matrix.CreateRotationX(MathHelper.ToRadians(r.X)) *
                Matrix.CreateRotationY(MathHelper.ToRadians(r.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(r.Z));
            position = Vector3.Transform(position, rotation) + shape.Position;
            direction = Vector3.TransformNormal(direction, rotation);
        }
    }

    // Area-uniform radius fraction within the outer `thickness` of a disc.
    private float AnnulusFraction(float thickness)
    {
        float inner = 1 - thickness;
        return (float)Math.Sqrt(MathHelper.Lerp(inner * inner, 1, NextFloat()));
    }

    // -------------------------------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------------------------------

    private void MoveWithEmitter()
    {
        Matrix delta = Matrix.Invert(_previousWorld) * _world;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            p.Position = Vector3.Transform(p.Position, delta);
            p.Velocity = Vector3.TransformNormal(p.Velocity, delta);
        }
    }

    private void UpdateParticles(float dt, ParticleStepContext context)
    {
        ParticleMainModule main = _system.Main;
        ParticleVelocityModule velocityModule = _system.VelocityOverLifetime;
        ParticleLimitVelocityModule limit = _system.LimitVelocity;
        ParticleForceModule forceModule = _system.ForceOverLifetime;
        ParticleExternalForcesModule external = _system.ExternalForces;
        ParticleNoiseModule noise = _system.Noise;
        ParticleCollisionModule collision = _system.Collision;

        Vector3 acceleration = context.Gravity * Finite(main.GravityModifier, 0, -100, 100);
        if (forceModule.Enabled && IsFinite(forceModule.Force))
            acceleration += forceModule.Space == ParticleSimulationSpace.Local
                ? Vector3.TransformNormal(forceModule.Force, _world) : forceModule.Force;

        bool hasVelocity = velocityModule.Enabled;
        Vector3 linear = Vector3.Zero, orbital = Vector3.Zero;
        float radial = 0;
        if (hasVelocity)
        {
            if (IsFinite(velocityModule.Linear))
                linear = velocityModule.Space == ParticleSimulationSpace.Local
                    ? Vector3.TransformNormal(velocityModule.Linear, _world) : velocityModule.Linear;
            if (IsFinite(velocityModule.Orbital))
                orbital = Vector3.TransformNormal(velocityModule.Orbital * (MathHelper.Pi / 180f), _world);
            radial = Finite(velocityModule.Radial, 0, -1000, 1000);
        }
        Vector3 center = _world.Translation;

        float windBlend = 0;
        Vector2 wind = Vector2.Zero;
        if (external.Enabled)
        {
            wind = context.Wind * Finite(external.Wind, 1, -100, 100);
            windBlend = 1 - (float)Math.Exp(-Finite(external.Response, 1.5f, 0, 100) * dt);
        }

        float drag = 0, limitSpeed = 0, limitKeep = 1;
        if (limit.Enabled)
        {
            drag = Finite(limit.Drag, 0, 0, 1000);
            limitSpeed = Finite(limit.Speed, 0, 0, 100000);
            limitKeep = (float)Math.Pow(1 - Finite(limit.Dampen, 0.5f, 0, 1), dt * 30);
        }

        float noiseStrength = 0, noiseFrequency = 0, noiseScroll = 0;
        if (noise.Enabled)
        {
            noiseStrength = Finite(noise.Strength, 1, -1000, 1000);
            noiseFrequency = Finite(noise.Frequency, 0.5f, 0, 100);
            noiseScroll = Finite(noise.ScrollSpeed, 0.5f, -100, 100) * _noiseTime;
        }

        bool collide = collision.Enabled && (collision.Type == ParticleCollisionType.Plane || context.Raycast != null);

        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime)
            {
                Kill(i--, dying: true);
                continue;
            }
            float t = p.Age / p.Lifetime;

            p.Velocity += acceleration * dt;
            if (windBlend > 0)
            {
                p.Velocity.X += (wind.X - p.Velocity.X) * windBlend;
                p.Velocity.Y += (wind.Y - p.Velocity.Y) * windBlend;
            }
            if (drag > 0) p.Velocity *= (float)Math.Exp(-drag * dt);
            if (limitSpeed > 0)
            {
                float speed = p.Velocity.Length();
                if (speed > limitSpeed) p.Velocity *= (limitSpeed + (speed - limitSpeed) * limitKeep) / speed;
            }

            Vector3 moving = p.Velocity;
            if (hasVelocity)
            {
                moving *= velocityModule.SpeedModifier?.Evaluate(t) ?? 1;
                moving += linear;
                if (orbital != Vector3.Zero) moving += Vector3.Cross(orbital, p.Position - center);
                if (radial != 0)
                {
                    Vector3 away = p.Position - center;
                    if (away.LengthSquared() > 1e-8f) moving += Vector3.Normalize(away) * radial;
                }
            }
            if (noiseStrength != 0)
                moving += Turbulence(p.Position * noiseFrequency, noiseScroll + p.Random * 17.3f) * noiseStrength;

            Vector3 from = p.Position;
            p.Position += moving * dt;
            p.DrawVelocity = moving;

            if (collide && Collide(ref p, from, collision, context))
            {
                if (p.Age >= p.Lifetime || p.Velocity.Length() < Finite(collision.MinKillSpeed, 0, 0, 100000))
                {
                    Kill(i--, dying: true);
                    continue;
                }
                t = p.Age / p.Lifetime;
            }

            ParticleRotationModule rotation = _system.RotationOverLifetime;
            if (rotation.Enabled)
                p.Rotation = (p.Rotation + MathHelper.ToRadians(rotation.AngularVelocity.Lerp(p.Random)) * dt) % MathHelper.TwoPi;
            ApplyLifetimeLook(ref p, t);

            if (_hasTrail) Trail(ref p, from, dt);
        }
    }

    private void ApplyLifetimeLook(ref Particle p) => ApplyLifetimeLook(ref p, p.NormalizedAge);

    private void ApplyLifetimeLook(ref Particle p, float t)
    {
        ParticleSizeModule size = _system.SizeOverLifetime;
        p.Size = size.Enabled ? Math.Max(0, p.StartSize * (size.Size?.Evaluate(t) ?? 1)) : p.StartSize;
        ParticleColorModule color = _system.ColorOverLifetime;
        p.Color = color.Enabled ? p.StartColor * (color.Color?.Evaluate(t) ?? Vector4.One) : p.StartColor;
        ParticleTextureSheetModule sheet = _system.TextureSheetAnimation;
        if (sheet.Enabled)
        {
            int frames = sheet.ClampedTilesX * sheet.ClampedTilesY;
            float frame = p.StartFrame + t * Finite(sheet.Cycles, 1, 0, 1000) * frames;
            p.Frame = (float)Math.Floor(frame) % frames;
            if (p.Frame < 0) p.Frame += frames;
        }
        else p.Frame = 0;
    }

    /// <returns>True when the particle bounced.</returns>
    private bool Collide(ref Particle p, Vector3 from, ParticleCollisionModule collision, ParticleStepContext context)
    {
        float radius = p.Size * 0.5f * Finite(collision.RadiusScale, 1, 0, 100);
        Vector3 point, normal;
        BasicEntity hitObject = null;
        if (collision.Type == ParticleCollisionType.Plane)
        {
            float floor = Finite(collision.PlaneHeight, 0, -1e6f, 1e6f) + radius;
            if (p.Position.Z >= floor || p.Velocity.Z + p.DrawVelocity.Z > 0) return false;
            normal = Vector3.UnitZ;
            point = new Vector3(p.Position.X, p.Position.Y, floor - radius);
            p.Position.Z = floor;
        }
        else
        {
            Vector3 move = p.Position - from;
            float length = move.Length();
            if (length < 1e-5f) return false;
            Vector3 direction = move / length;
            if (!context.Raycast(from, direction, length + radius, out point, out normal, out hitObject)) return false;
            if (Vector3.Dot(normal, direction) >= 0) return false; // leaving a surface (started inside it)
            // Stop the radius short of the surface.
            float distance = Vector3.Distance(from, point);
            p.Position = from + direction * Math.Max(0, distance - radius) + normal * 0.001f;
        }

        Vector3 velocity = p.Velocity;
        float into = Vector3.Dot(velocity, normal);
        if (into < 0)
        {
            Vector3 along = velocity - into * normal;
            p.Velocity = along * (1 - Finite(collision.Dampen, 0.1f, 0, 1)) - into * normal * Finite(collision.Bounce, 0.5f, 0, 2);
        }
        p.Age += p.Lifetime * Finite(collision.LifetimeLoss, 0, 0, 1);
        CollisionCount++;
        var hit = new ParticleCollisionEvent { GameObject = hitObject, Point = point, Normal = normal, Velocity = velocity };
        Collided?.Invoke(hit);
        if (_hasCollision) FireSubEmitters(ParticleSubEmitterTrigger.Collision, in p, point, 1);
        return true;
    }

    private void Kill(int index, bool dying)
    {
        if (dying && _hasDeath) FireSubEmitters(ParticleSubEmitterTrigger.Death, in _particles[index], _particles[index].Position, 1);
        // Order isn't kept: sorting happens when drawing.
        _particles[index] = _particles[--_count];
    }

    // -------------------------------------------------------------------------------------------------
    // Sub-emitters
    // -------------------------------------------------------------------------------------------------

    private void ResolveSubEmitters(ParticleStepContext context)
    {
        _subEmitters.Clear();
        _hasBirth = _hasTrail = _hasCollision = _hasDeath = false;
        ParticleSubEmittersModule module = _system.SubEmitters;
        if (!module.Enabled || module.Emitters == null || context.FindSubEmitter == null) return;
        foreach (ParticleSubEmitter emitter in module.Emitters)
        {
            if (emitter == null || string.IsNullOrWhiteSpace(emitter.Target)) continue;
            ParticleSimulation target = context.FindSubEmitter(emitter.Target.Trim());
            if (target == null || target == this) continue; // a system feeding itself would never stop
            _subEmitters.Add((emitter, target));
            switch (emitter.Trigger)
            {
                case ParticleSubEmitterTrigger.Birth: _hasBirth = true; break;
                case ParticleSubEmitterTrigger.Trail: _hasTrail = true; break;
                case ParticleSubEmitterTrigger.Collision: _hasCollision = true; break;
                default: _hasDeath = true; break;
            }
        }
    }

    private void FireSubEmitters(ParticleSubEmitterTrigger trigger, in Particle parent, Vector3 position, float scale)
    {
        foreach (var (settings, target) in _subEmitters)
        {
            if (settings.Trigger != trigger) continue;
            int count = (int)Math.Round(Math.Max(0, settings.Count.Lerp(NextFloat())) * scale);
            if (count == 0) continue;
            target.EmitAt(position, parent.Velocity * Finite(settings.InheritVelocity, 0, -10, 10),
                settings.InheritColor ? parent.Color : Vector4.One, count);
        }
    }

    private void Trail(ref Particle p, Vector3 from, float dt)
    {
        foreach (var (settings, target) in _subEmitters)
        {
            if (settings.Trigger != ParticleSubEmitterTrigger.Trail) continue;
            p.TrailCarry += Math.Max(0, settings.Count.Lerp(p.Random)) * dt;
            int n = (int)p.TrailCarry;
            p.TrailCarry -= n;
            Vector3 inherited = p.Velocity * Finite(settings.InheritVelocity, 0, -10, 10);
            Vector4 tint = settings.InheritColor ? p.Color : Vector4.One;
            // Spread along the path so fast particles leave an even line.
            for (int k = 0; k < n; k++)
                target.EmitAt(Vector3.Lerp(from, p.Position, (k + NextFloat()) / n), inherited, tint, 1);
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------

    private float NextFloat()
    {
        // xorshift32
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng >> 8) * (1f / 16777216f);
    }

    private Vector3 RandomUnit()
    {
        float z = NextFloat() * 2 - 1;
        float phi = NextFloat() * MathHelper.TwoPi;
        float ring = (float)Math.Sqrt(Math.Max(0, 1 - z * z));
        return new Vector3(ring * (float)Math.Cos(phi), ring * (float)Math.Sin(phi), z);
    }

    private static float Finite(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Smooth noise velocity in -1..1 per axis around a point (three offset value-noise fields).</summary>
    public static Vector3 Turbulence(Vector3 p, float time) => new(
        ValueNoise(p.X + time, p.Y, p.Z - time * 0.5f),
        ValueNoise(p.Y + 31.7f, p.Z + time, p.X + 11.3f),
        ValueNoise(p.Z - 47.1f, p.X - time * 0.7f, p.Y + time + 5.9f));

    private static float ValueNoise(float x, float y, float z)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
        float x00 = MathHelper.Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), u);
        float x10 = MathHelper.Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), u);
        float x01 = MathHelper.Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), u);
        float x11 = MathHelper.Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), u);
        return MathHelper.Lerp(MathHelper.Lerp(x00, x10, v), MathHelper.Lerp(x01, x11, v), w) * 2 - 1;
    }

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) * (1f / 16777215f);
        }
    }
}
