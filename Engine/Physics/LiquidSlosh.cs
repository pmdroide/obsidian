using System;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// The liquid in one container (Liquid component), as Half-Life: Alyx's bottles do it (Docs/Research/BottleLiquid):
    /// no fluid simulation, only a surface plane. The server code (<c>server.dll</c>, <c>prop_liquid_*</c> convars) keeps
    /// <list type="bullet">
    /// <item>an <b>agitation</b> and a <b>foam</b> amount: every 0.1 s the change of linear + angular velocity, raised to
    /// 1 - dampening, adds to a peak that decays linearly to zero over the settle duration (20 s / 30 s);</item>
    /// <item>a <b>down</b> direction: gravity minus the body's velocity (in/s) plus angular velocity (deg/s), eased 20 % per
    /// 0.1 s, so the liquid lags behind and leans when the bottle is swung.</item>
    /// </list>
    /// The shader (<c>vr_simple_liquid</c>) wobbles that direction at ~21 rad/s with the agitation, takes the surface
    /// height for the fill from three orientation tables (<see cref="LiquidVolume"/>), and draws liquid below it.
    /// Here the per-instance part of the shader runs on the CPU (<see cref="UpdateSurface"/>); Forward.fx adds the
    /// per-pixel ripples, meniscus, water line, foam and bubbles. Lives on <see cref="Entities.TransformMatrix.Liquid"/>.
    /// Source units are kept inside so Valve's defaults apply unchanged.
    /// </summary>
    public sealed class LiquidSlosh
    {
        // ---- Valve's defaults (server.dll prop_liquid_* convars, read from the shipped binary) ----
        public const float InchesPerMetre = 39.3701f;
        public const float DegreesPerRadian = 57.29578f;
        /// <summary>sv_gravity (in/s²).</summary>
        public const float SourceGravity = 386f;
        /// <summary>The server thinks every 0.1 s while the liquid moves.</summary>
        public const float ThinkInterval = 0.1f;
        /// <summary>prop_liquid_gravity_interpolation: share of the new down direction taken per think.</summary>
        public const float GravityInterpolation = 0.2f;
        /// <summary>Impulses at or below this don't agitate (the 0.1 compare in the think).</summary>
        public const float ImpulseThreshold = 0.1f;

        // ---- Settings (copied from the component every frame) ----
        public float Fill = 0.6f;
        /// <summary>prop_liquid_settle_duration / _foam_settle_duration (s).</summary>
        public float SettleDuration = 20f, FoamSettleDuration = 30f;
        /// <summary>prop_liquid_velocity_max_agitation / _foam_max_agitation.</summary>
        public float MaxAgitation = 5f, FoamMaxAgitation = 200f;
        /// <summary>prop_liquid_velocity_additive_power / _foam_additive_power.</summary>
        public float AdditivePower = 10f, FoamAdditivePower = 1f;
        /// <summary>prop_liquid_velocity_dampening / _foam_dampening.</summary>
        public float Dampening = 0.4f, FoamDampening = 0.4f;
        /// <summary>prop_liquid_gravity_velocity_scale: how much motion leans the surface.</summary>
        public float VelocityScale = 1f;
        /// <summary>g_flLiquidWobbleSpeed / Scale / Wavelength (material).</summary>
        public float WobbleSpeed = 1f, WobbleScale = 0.02f, WobbleWavelength = 0.1f;
        /// <summary>g_flSurfaceTension: how far the surface climbs the glass at grazing angles.</summary>
        public float SurfaceTension = 1f;
        /// <summary>Per-container phase so neighbours don't wobble in step (Alyx offsets the clock by the render colour).</summary>
        public float Phase;

        // ---- State ----
        /// <summary>0..1: current agitation / max (the render colour's blue byte in Alyx).</summary>
        public float Agitation { get; private set; }
        /// <summary>0..1: current foam / max (green byte).</summary>
        public float Foam { get; private set; }
        /// <summary>Eased down direction (world, unit). Alyx packs it in the render colour's alpha (4 + 4 bits).</summary>
        public Vector3 Down { get; private set; } = -Vector3.UnitZ;

        // ---- Per frame, for the shader ----
        /// <summary>World "up" of the liquid surface after the wobble.</summary>
        public Vector3 SurfaceUp { get; private set; } = Vector3.UnitZ;
        /// <summary>A pixel p is liquid where dot(p, SurfaceUp) &lt;= SurfaceHeight (+ ripples, meniscus).</summary>
        public float SurfaceHeight { get; private set; } = float.MinValue;
        /// <summary>World centre of the container (ripple origin).</summary>
        public Vector3 Centre { get; private set; }
        /// <summary>Container's model +Z in world (unit) and its world height (m), for bubbles and foam.</summary>
        public Vector3 Axis { get; private set; } = Vector3.UnitZ;
        public float Height { get; private set; } = 0.3f;
        /// <summary>Container radius around its axis (m): the liquid top is only drawn inside it.</summary>
        public float Radius { get; private set; } = 0.04f;
        /// <summary>dot(Axis, SurfaceUp): 1 upright, 0 on its side, -1 upside down.</summary>
        public float CosUp { get; private set; } = 1;
        /// <summary>Wobble amplitude (agitation^10 + 0.2 agitation + 0.01) and bubble amount (agitation^9 + 0.25 foam).</summary>
        public float WobbleAmplitude { get; private set; } = 0.01f;
        public float Bubbles { get; private set; }
        /// <summary>The wobble clock (Alyx: 600 x speed rad per s before the wave frequencies), wrapped.</summary>
        public double Clock { get; private set; }
        /// <summary>Ripple phases t·k for the shader's six waves (radians, wrapped to 2π).</summary>
        public Vector4 RipplePhaseA { get; private set; }
        public Vector2 RipplePhaseB { get; private set; }
        /// <summary>True when the container holds nothing (no liquid draws).</summary>
        public bool Empty => Fill <= 0.0005f;

        private float _time, _nextThink;
        private float _peak, _peakTime = float.MinValue, _foamPeak, _foamPeakTime = float.MinValue;
        private Vector3 _thinkVelocity, _thinkAngular;
        private Vector3 _velocity, _angular;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private bool _hasLast;

        /// <summary>
        /// Advances by <paramref name="dt"/> seconds with the container at <paramref name="world"/> (scale × rotation × translation),
        /// in a scene with <paramref name="gravity"/> (m/s²). Velocities come from the change of the transform, so it
        /// sloshes whether physics, a script, a ragdoll hand or the editor gizmo moves it.
        /// </summary>
        public void Update(Matrix world, Vector3 gravity, float dt, LiquidVolume volume)
        {
            world.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 position);
            if (dt > 0)
            {
                if (_hasLast)
                {
                    Vector3 moved = position - _lastPosition;
                    // Teleports (scene loads, snapping) don't slosh.
                    if (moved.Length() > 2f) { _velocity = Vector3.Zero; _angular = Vector3.Zero; _thinkVelocity = Vector3.Zero; _thinkAngular = Vector3.Zero; }
                    else
                    {
                        _velocity = moved / dt;
                        Quaternion delta = rotation * Quaternion.Inverse(_lastRotation);
                        if (delta.W < 0) delta = -delta;
                        float angle = 2f * MathF.Acos(Math.Clamp(delta.W, -1f, 1f));
                        float s = MathF.Sqrt(Math.Max(1 - delta.W * delta.W, 0));
                        _angular = s > 1e-5f ? new Vector3(delta.X, delta.Y, delta.Z) / s * (angle / dt) : Vector3.Zero;
                    }
                }
                _lastPosition = position;
                _lastRotation = rotation;
                _hasLast = true;

                _time += dt;
                if (_time >= _nextThink)
                {
                    Think();
                    _nextThink = _time + ThinkInterval;
                }
                UpdateDown(gravity, dt);
                // Alyx wraps its clock at 200 s; a double with every wave wrapped below keeps it seamless.
                Clock += dt;
                if (Clock > 1e6) Clock -= 1e6;
            }
            UpdateSurface(world, scale, position, volume);
        }

        /// <summary>Shakes the liquid as a velocity change of <paramref name="speed"/> m/s would (scripts: <c>Agitate</c>).</summary>
        public void Agitate(float speed)
        {
            float delta = Math.Max(speed, 0) * InchesPerMetre;
            AddImpulse(MathF.Pow(delta, 1 - Dampening), MathF.Pow(delta, 1 - FoamDampening));
        }

        /// <summary>Settles the liquid at once (scene loads, Stop).</summary>
        public void Settle()
        {
            _peak = _foamPeak = 0;
            _peakTime = _foamPeakTime = float.MinValue;
            Agitation = Foam = 0;
            _hasLast = false;
            _velocity = _angular = _thinkVelocity = _thinkAngular = Vector3.Zero;
        }

        /// <summary>server.dll 0x1809DC010: the 10 Hz think.</summary>
        private void Think()
        {
            Vector3 velocity = _velocity * InchesPerMetre;
            Vector3 angular = _angular * DegreesPerRadian;
            float delta = (velocity - _thinkVelocity).Length() + (angular - _thinkAngular).Length();
            _thinkVelocity = velocity;
            _thinkAngular = angular;
            AddImpulse(MathF.Pow(delta, 1 - Dampening), MathF.Pow(delta, 1 - FoamDampening));
            Agitation = Current(_peak, _peakTime, SettleDuration) / Math.Max(MaxAgitation, 1e-4f);
            Foam = Current(_foamPeak, _foamPeakTime, FoamSettleDuration) / Math.Max(FoamMaxAgitation, 1e-4f);
            Agitation = Math.Clamp(Agitation, 0, 1);
            Foam = Math.Clamp(Foam, 0, 1);
        }

        private void AddImpulse(float impulse, float foamImpulse)
        {
            if (impulse > ImpulseThreshold) Peak(ref _peak, ref _peakTime, impulse, SettleDuration, MaxAgitation, AdditivePower);
            if (foamImpulse > ImpulseThreshold) Peak(ref _foamPeak, ref _foamPeakTime, foamImpulse, FoamSettleDuration, FoamMaxAgitation, FoamAdditivePower);
        }

        /// <summary>
        /// A new impulse while the last peak still decays adds (impulse/max)^power of itself on top of what is left;
        /// never below the impulse, never above max. Then the peak restarts its decay now.
        /// </summary>
        private void Peak(ref float peak, ref float peakTime, float impulse, float settle, float max, float power)
        {
            settle = Math.Max(settle, 1e-3f);
            max = Math.Max(max, 1e-4f);
            float end = peakTime + settle;
            float value;
            if (end > _time)
            {
                float remaining = (end - _time) / settle;
                float ratio = Math.Clamp(impulse / max, 0, 1);
                value = MathF.Pow(ratio, power) * impulse + remaining * peak;
                value = Math.Min(Math.Max(value, impulse), max);
            }
            else value = Math.Min(max, impulse);
            peak = value;
            peakTime = _time;
        }

        private float Current(float peak, float peakTime, float settle)
        {
            if (settle <= 0) return 0;
            return peak * (1 - Math.Clamp((_time - peakTime) / settle, 0, 1));
        }

        /// <summary>server.dll 0x1809D94B0: down = normalize(gravity - scale·(v + ω)), eased towards.</summary>
        private void UpdateDown(Vector3 gravity, float dt)
        {
            Vector3 g = gravity * InchesPerMetre;
            if (g.LengthSquared() < 1e-6f) g = new Vector3(0, 0, -SourceGravity);
            Vector3 target = g - VelocityScale * (_velocity * InchesPerMetre + _angular * DegreesPerRadian);
            target = target.LengthSquared() > 1e-8f ? Vector3.Normalize(target) : -Vector3.UnitZ;
            // 20 % per 0.1 s think, at any frame rate.
            float k = 1 - MathF.Pow(1 - GravityInterpolation, dt / ThinkInterval);
            Vector3 down = Down + (target - Down) * k;
            Down = down.LengthSquared() > 1e-8f ? Vector3.Normalize(down) : target;
        }

        /// <summary>The per-instance part of vr_simple_liquid's pixel shader: the wobbling surface plane.</summary>
        private void UpdateSurface(Matrix world, Vector3 scale, Vector3 position, LiquidVolume volume)
        {
            Vector3 axis = Vector3.TransformNormal(Vector3.UnitZ, world);
            axis = axis.LengthSquared() > 1e-10f ? Vector3.Normalize(axis) : Vector3.UnitZ;
            Axis = axis;
            Vector3 centreLocal = volume?.Centre ?? Vector3.Zero;
            Centre = Vector3.Transform(centreLocal, world);

            float a = Agitation;
            float amp = MathF.Pow(a, 10) + 0.2f * a + 0.01f;
            WobbleAmplitude = amp;
            Bubbles = MathF.Pow(a, 9) + 0.25f * Foam;

            // Alyx: t = 3 x floor(200 x speed) x (time mod 200); the waves below multiply it by 0.001 .. 0.104.
            double t = Clock * 600.0 * Math.Max(WobbleSpeed, 0) + Phase * 1000.0;
            float s1 = (float)Math.Sin(Wrap(t * 0.035));
            float s2 = (float)Math.Sin(Wrap(t * 0.001)), c2 = (float)Math.Cos(Wrap(t * 0.001));
            RipplePhaseA = new Vector4((float)Wrap(t * 0.034), (float)Wrap(t * 0.044), (float)Wrap(t * 0.024), (float)Wrap(t * 0.029));
            RipplePhaseB = new Vector2((float)Wrap(t * 0.104), (float)Wrap(t * -0.104));

            Vector3 g = Down;
            // Bob along the container's axis...
            float k1 = s1 * 0.1f * amp * amp;
            Vector3 bob = g + (-axis - g) * k1;
            // ...and sway around a slowly turning direction.
            float m = amp * s1 * 0.3f;
            Vector2 d = new Vector2(m * s2, m * c2);
            Vector3 sway;
            sway.X = g.X + d.X * (1 - Math.Abs(g.X));
            sway.Y = g.Y + d.Y * (1 - Math.Abs(g.Y));
            sway.Z = g.Z + d.X * (1 - Math.Abs(sway.Y)) + d.Y * (1 - Math.Abs(sway.X));
            float w = (1 - Math.Abs(Vector3.Dot(axis, -sway))) * (1 - amp);
            Vector3 down = sway + (bob - sway) * w;
            down = down.LengthSquared() > 1e-8f ? Vector3.Normalize(down) : g;
            Vector3 up = -down;
            SurfaceUp = up;

            float cosUp = Vector3.Dot(axis, up);
            CosUp = cosUp;
            float axisScale = Math.Abs(scale.Z), sideScale = (Math.Abs(scale.X) + Math.Abs(scale.Y)) * 0.5f;
            Height = (volume != null ? volume.Upwards[LiquidVolume.Samples - 1] - volume.Upwards[0] : 0.3f) * axisScale;
            Radius = (volume?.Radius ?? 0.04f) * sideScale;
            if (volume == null || Empty)
            {
                SurfaceHeight = float.MinValue;
                return;
            }
            // Model-unit height along up, scaled by the scale along the axis that points up.
            float height = volume.Height(cosUp, Fill);
            float c = Math.Abs(cosUp);
            height *= axisScale * c + sideScale * (1 - c);
            SurfaceHeight = Vector3.Dot(Centre, up) + height;
        }

        private static double Wrap(double radians)
        {
            const double TwoPi = Math.PI * 2;
            radians %= TwoPi;
            return radians < 0 ? radians + TwoPi : radians;
        }
    }
}
