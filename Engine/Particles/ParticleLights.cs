using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Particles;

/// <summary>
/// The point lights of one simulation's Lights module (<see cref="ParticleLightsModule"/>), refreshed after every
/// step. They are engine-owned <see cref="PointLight"/>s that never enter the scene: the renderer gathers them each
/// frame (<see cref="Components.ParticleSystemComponent.CollectLights"/>) next to the scene's own point lights, so
/// they light the G-buffer, forward materials and the volumetric fog but are never saved, picked or listed.
/// </summary>
public sealed class ParticleLights : IDisposable
{
    // Shadow cube faces of a shadowed System light (only allocated while Cast Shadows is on).
    private const int ShadowResolution = 512;
    // A light moves (and redraws its shadow faces) only past this many metres.
    private const float MoveThreshold = 0.01f;

    private readonly List<PointLight> _pool = new();
    private int _active;
    private float _peakGlow, _activity, _flickerTime;
    private Vector3 _centre;
    private bool _hasCentre;
    // Per Particle: indices and weights of the brightest carriers this step.
    private readonly int[] _picked = new int[ParticleLightsModule.MaxLightsLimit];
    private readonly float[] _pickedWeight = new float[ParticleLightsModule.MaxLightsLimit];

    /// <summary>Lights to draw this frame.</summary>
    public int Count => _active;
    public PointLight this[int index] => _pool[index];

    /// <summary>Adds the active lights to <paramref name="into"/>.</summary>
    public void AddTo(List<PointLight> into)
    {
        for (int i = 0; i < _active; i++) into.Add(_pool[i]);
    }

    /// <summary>No lights until the next update (Clear/Stop); frees a shadow map.</summary>
    public void Clear()
    {
        _active = 0;
        _peakGlow = 0;
        _activity = 0;
        _hasCentre = false;
        ReleaseShadows();
    }

    internal void Update(ParticleLightsModule module, ReadOnlySpan<Particle> particles, float dt)
    {
        if (!module.Enabled || particles.Length == 0)
        {
            // Off, or every particle gone: the light goes with them (a stopped fire ends dark).
            if (!module.Enabled) Clear();
            else
            {
                _active = 0;
                _hasCentre = false;
            }
            return;
        }
        float speed = Finite(module.FlickerSpeed, 8, 0, 100);
        _flickerTime = (_flickerTime + dt * speed) % 4096f;
        if (module.Mode == ParticleLightMode.PerParticle) UpdatePerParticle(module, particles);
        else UpdateSystem(module, particles, dt);
    }

    // One light at the glow-weighted centre of the particles.
    private void UpdateSystem(ParticleLightsModule module, ReadOnlySpan<Particle> particles, float dt)
    {
        float total = 0;
        Vector3 centre = Vector3.Zero;
        Vector3 color = Vector3.Zero;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle p = ref particles[i];
            float weight = Glow(in p) * Math.Max(p.Size, 0.01f);
            if (!(weight > 0)) continue;
            total += weight;
            centre += p.Position * weight;
            color += new Vector3(p.Color.X, p.Color.Y, p.Color.Z) * weight;
        }
        if (!(total > 1e-6f))
        {
            _active = 0;
            return;
        }
        centre /= total;
        // The centre of a flickering fire jumps between flames; ease it so the light (and its shadows) glides.
        float follow = _hasCentre ? 1 - (float)Math.Exp(-dt * 10) : 1;
        _centre = Vector3.Lerp(_centre, centre, follow);
        _hasCentre = true;

        // Relative to the recent peak (halving every 1.5 s): steady fire = 1, a dying fire or a cooling blast fades.
        float activity = 1;
        if (module.AlphaAffectsIntensity)
        {
            _peakGlow = Math.Max(total, _peakGlow * (float)Math.Exp(-dt * 0.462f));
            activity = _peakGlow > 0 ? total / _peakGlow : 0;
        }
        _activity = MathHelper.Lerp(_activity, activity, 1 - (float)Math.Exp(-dt * 12));

        float flicker = Finite(module.Flicker, 0, 0, 1);
        float wave = Flicker(_flickerTime, 0);
        float intensity = Finite(module.Intensity, 5, 0, 100000) * _activity * Math.Max(0, 1 + flicker * wave);
        // A flame's light also sways a little.
        Vector3 sway = flicker > 0
            ? new Vector3(Flicker(_flickerTime * 0.7f, 11.3f), Flicker(_flickerTime * 0.7f, 23.9f), Flicker(_flickerTime * 0.5f, 37.1f) * 0.5f) * flicker * 0.06f
            : Vector3.Zero;
        Vector3 offset = IsFinite(module.Offset) ? module.Offset : Vector3.Zero;

        PointLight light = Light(0);
        Place(light, _centre + offset + sway, Finite(module.Range, 8, 0.1f, 1000));
        light.Color = module.UseParticleColor ? Hue(color / total, module.Color) : module.Color;
        light.Intensity = intensity;
        bool shadows = module.CastShadows;
        if (light.CastShadows != shadows)
        {
            light.CastShadows = shadows;
            light.HasChanged = true;
        }
        if (!shadows) ReleaseShadows();
        _active = intensity > 1e-4f ? 1 : 0;
    }

    // Lights on the brightest particles that carry one (Random < Ratio).
    private void UpdatePerParticle(ParticleLightsModule module, ReadOnlySpan<Particle> particles)
    {
        ReleaseShadows();
        int max = module.ClampedMaxLights;
        float ratio = Finite(module.Ratio, 0.1f, 0, 1);
        int picked = 0;
        for (int i = 0; i < particles.Length && max > 0; i++)
        {
            ref readonly Particle p = ref particles[i];
            if (p.Random >= ratio) continue;
            float weight = module.AlphaAffectsIntensity ? Glow(in p) : MaxChannel(p.Color);
            if (!(weight > 1e-4f)) continue;
            // Keep the list sorted brightest first; a dimmer one past the end is dropped.
            int slot = picked < max ? picked++ : max;
            if (slot == max && weight <= _pickedWeight[max - 1]) continue;
            if (slot == max) slot = max - 1;
            while (slot > 0 && _pickedWeight[slot - 1] < weight)
            {
                _picked[slot] = _picked[slot - 1];
                _pickedWeight[slot] = _pickedWeight[slot - 1];
                slot--;
            }
            _picked[slot] = i;
            _pickedWeight[slot] = weight;
        }

        float intensity = Finite(module.Intensity, 5, 0, 100000);
        float range = Finite(module.Range, 8, 0.1f, 1000);
        float flicker = Finite(module.Flicker, 0, 0, 1);
        for (int k = 0; k < picked; k++)
        {
            ref readonly Particle p = ref particles[_picked[k]];
            PointLight light = Light(k);
            light.CastShadows = false;
            float scale = module.SizeAffectsRange && p.StartSize > 1e-4f ? Math.Clamp(p.Size / p.StartSize, 0.05f, 20f) : 1;
            Place(light, p.Position, range * scale);
            light.Color = module.UseParticleColor ? Hue(new Vector3(p.Color.X, p.Color.Y, p.Color.Z), module.Color) : module.Color;
            float alpha = module.AlphaAffectsIntensity ? Math.Clamp(p.Color.W, 0, 1) : 1;
            light.Intensity = intensity * alpha * Math.Max(0, 1 + flicker * Flicker(_flickerTime, p.Random * 97.3f));
        }
        _active = picked;
    }

    // Shadows are toggled on the light (CastShadows); the shadow map itself is made by the shadow pass.
    private PointLight Light(int index)
    {
        while (_pool.Count <= index)
            _pool.Add(new PointLight(Vector3.Zero, 1, Color.White, 0, false, false, ShadowResolution, 0, false)
            {
                Name = "Particle Light",
            });
        return _pool[index];
    }

    private static void Place(PointLight light, Vector3 position, float radius)
    {
        if (Math.Abs(light.Radius - radius) > 1e-3f) light.Radius = radius;
        if (Vector3.DistanceSquared(light.Position, position) > MoveThreshold * MoveThreshold) light.Position = position;
    }

    private void ReleaseShadows()
    {
        if (_pool.Count == 0 || _pool[0].ShadowMap == null) return;
        _pool[0].ShadowMap.Dispose();
        _pool[0].ShadowMap = null;
        _pool[0].HasChanged = true;
    }

    public void Dispose()
    {
        Clear();
        _pool.Clear();
    }

    /// <summary>How brightly a particle glows: its opacity times its brightest colour channel.</summary>
    private static float Glow(in Particle p) => Math.Clamp(p.Color.W, 0, 1) * MaxChannel(p.Color);

    private static float MaxChannel(Vector4 c) => Math.Max(c.X, Math.Max(c.Y, c.Z));

    // The hue of a particle colour at full brightness (the light's brightness is its intensity).
    private static Color Hue(Vector3 rgb, Color fallback)
    {
        float max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        return max > 1e-4f ? new Color(rgb / max) : fallback;
    }

    /// <summary>Smooth flame flicker in -1..1: two octaves of value noise, plus the odd quick dip.</summary>
    private static float Flicker(float t, float seed)
    {
        float slow = Noise(t + seed);
        float fast = Noise(t * 2.7f + seed * 1.7f + 41.2f);
        return Math.Clamp(slow * 0.65f + fast * 0.45f, -1, 1);
    }

    private static float Noise(float x)
    {
        int i = (int)Math.Floor(x);
        float f = x - i;
        float u = f * f * (3 - 2 * f);
        return MathHelper.Lerp(Hash(i), Hash(i + 1), u) * 2 - 1;
    }

    private static float Hash(int x)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) * (1f / 16777215f);
        }
    }

    private static float Finite(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
