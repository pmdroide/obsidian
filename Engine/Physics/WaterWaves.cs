using System;
using Engine.Components;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>The water material settings that shape the moving surface.</summary>
    public readonly struct WaterWaveSettings
    {
        public readonly float WaveScale;
        public readonly float WaveSpeed;
        public readonly float WaveHeight;

        public WaterWaveSettings(float waveScale, float waveSpeed, float waveHeight)
        {
            WaveScale = waveScale;
            WaveSpeed = waveSpeed;
            WaveHeight = waveHeight;
        }

        public static WaterWaveSettings From(MaterialEffect material) =>
            new WaterWaveSettings(material.WaveScale, material.WaveSpeed, material.WaveHeight);

        /// <summary>Clamped the same way <see cref="MaterialComponent.ApplyTo"/> clamps the rendered material.</summary>
        public static WaterWaveSettings From(MaterialComponent material) =>
            new WaterWaveSettings(MaterialComponent.Limit(material.WaveScale, 0.001f, 10),
                MaterialComponent.Limit(material.WaveSpeed, 0, 10),
                MaterialComponent.Limit(material.WaveHeight, 0, MaterialComponent.MaxWaveHeight));
    }

    /// <summary>
    /// CPU copy of the swell waves that move the water mesh (Swell() in Shaders/Forward/Water.fx).
    /// Keep the constants and formulas identical, or floating objects bob out of step with the surface.
    /// </summary>
    public static class WaterWaves
    {
        public const int SwellCount = 4;
        private const float Gravity = 9.81f;
        // Caps the sideways Gerstner motion so crests sharpen without folding over.
        private const float Choppiness = 0.8f;

        /// <summary>Longest wavelength in metres: 20 m at the default Wave Scale of 0.3.</summary>
        public static float BaseWavelength(float waveScale) => 6f / Math.Max(waveScale, 0.001f);

        /// <summary>How far the surface point that rests at <paramref name="restXY"/> has moved at <paramref name="time"/>.</summary>
        public static Vector3 Displacement(Vector2 restXY, float time, in WaterWaveSettings settings)
        {
            if (!(settings.WaveHeight > 0)) return Vector3.Zero;

            float totalWeight = 0, weight = 1;
            for (int i = 0; i < SwellCount; i++) { totalWeight += weight; weight *= 0.78f; }

            Vector3 offset = Vector3.Zero;
            float wavelength = BaseWavelength(settings.WaveScale);
            weight = 1;
            for (int i = 0; i < SwellCount; i++)
            {
                float angle = 0.6f + i * 0.839986f + (i % 2) * 0.9f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float k = MathHelper.TwoPi / wavelength;
                float omega = MathF.Sqrt(Gravity * k) * settings.WaveSpeed;
                float phase = Vector2.Dot(direction, restXY) * k - omega * time + i * 1.7f;
                float amplitude = settings.WaveHeight * 0.5f * weight / totalWeight;
                float sideways = Math.Min(amplitude, Choppiness / (k * SwellCount));
                float c = MathF.Cos(phase);
                offset.X += direction.X * sideways * c;
                offset.Y += direction.Y * sideways * c;
                offset.Z += amplitude * MathF.Sin(phase);
                wavelength *= 0.68f;
                weight *= 0.78f;
            }
            return offset;
        }

        /// <summary>Height of the moving surface above its rest plane at world <paramref name="xy"/>.</summary>
        public static float HeightAt(Vector2 xy, float time, in WaterWaveSettings settings)
        {
            if (!(settings.WaveHeight > 0)) return 0;
            // Gerstner waves also move points sideways: find the rest point that ends up above xy.
            Vector2 rest = xy;
            Vector3 offset = Vector3.Zero;
            for (int i = 0; i < 4; i++)
            {
                offset = Displacement(rest, time, settings);
                rest = xy - new Vector2(offset.X, offset.Y);
            }
            return offset.Z;
        }
    }
}
