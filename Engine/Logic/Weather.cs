using System;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>Scene weather drawn by WeatherRenderModule. Saved by name: never rename a value.</summary>
    public enum WeatherType
    {
        None,
        Rain,
        Sandstorm,
        Snow,
    }

    /// <summary>
    /// The fixed look of one weather type. The scene's <see cref="EnvironmentSettings"/> scale it with
    /// intensity, haze and wind; everything else lives here so the renderer, the sky and tests agree.
    /// </summary>
    public sealed class WeatherProfile
    {
        /// <summary>Particles drawn at intensity 1.</summary>
        public int MaxParticles;
        /// <summary>Size of the box of particles that follows the camera, metres (Z is up).</summary>
        public Vector3 Box;
        /// <summary>Falling speed in m/s, before wind.</summary>
        public float FallSpeed;
        /// <summary>How much of the wind the particles take on.</summary>
        public float WindFactor;
        /// <summary>The weather always blows at least this fast along the wind direction, m/s.</summary>
        public float MinWind;
        /// <summary>Streak length is the particle's speed times this; 0 draws a round flake.</summary>
        public float StreakSeconds;
        /// <summary>Particle width (streaks) or diameter (flakes) in metres.</summary>
        public float Size;
        /// <summary>Side-to-side sway amplitude in metres (snow flutter, sand gusts).</summary>
        public float Sway;
        /// <summary>sRGB particle colour.</summary>
        public Color Tint;
        /// <summary>Particle opacity at full coverage.</summary>
        public float Opacity;
        /// <summary>How strongly direct light (sun/moon) lights a particle, relative to the sky ambient.</summary>
        public float LightResponse;
        /// <summary>sRGB colour of the haze the weather adds.</summary>
        public Color HazeColor;
        /// <summary>
        /// Distance at which the haze reaches ~63% of its maximum, metres, at the reference haze strength
        /// (intensity x haze = <see cref="ReferenceHazeStrength"/>, about the defaults). Stronger settings shorten it.
        /// </summary>
        public float HazeDistance;
        /// <summary>Most of the view the haze can cover (0..1), reached from the reference haze strength up.</summary>
        public float HazeMax;
        /// <summary>Cloud cover the day/night sky moves towards at full intensity.</summary>
        public float CloudCoverage;
        /// <summary>Fraction of the day/night sun the weather takes away at full intensity.</summary>
        public float SunDim;
        /// <summary>sRGB colour the day/night sun is tinted towards at full intensity (white = no tint).</summary>
        public Color SunTint = Color.White;

        /// <summary>Intensity x haze at which <see cref="HazeDistance"/> and <see cref="HazeMax"/> apply.</summary>
        public const float ReferenceHazeStrength = 0.4f;

        public static readonly WeatherProfile Rain = new WeatherProfile
        {
            MaxParticles = 24000,
            Box = new Vector3(36, 36, 22),
            FallSpeed = 9,
            WindFactor = 1,
            StreakSeconds = 0.05f,
            Size = 0.006f,
            Tint = new Color(200, 212, 230),
            Opacity = 0.32f,
            LightResponse = 0.04f,
            HazeColor = new Color(150, 160, 172),
            HazeDistance = 160,
            HazeMax = 0.6f,
            CloudCoverage = 0.95f,
            SunDim = 0.6f,
        };

        public static readonly WeatherProfile Sandstorm = new WeatherProfile
        {
            MaxParticles = 20000,
            Box = new Vector3(30, 30, 14),
            FallSpeed = 0.6f,
            WindFactor = 1,
            MinWind = 7,
            StreakSeconds = 0.025f,
            Size = 0.01f,
            Sway = 0.35f,
            Tint = new Color(196, 150, 96),
            Opacity = 0.7f,
            LightResponse = 0.12f,
            HazeColor = new Color(190, 140, 88),
            HazeDistance = 35,
            HazeMax = 0.95f,
            CloudCoverage = 0.5f,
            SunDim = 0.65f,
            SunTint = new Color(255, 178, 112),
        };

        public static readonly WeatherProfile Snow = new WeatherProfile
        {
            MaxParticles = 16000,
            Box = new Vector3(28, 28, 18),
            FallSpeed = 1.1f,
            WindFactor = 0.6f,
            StreakSeconds = 0,
            Size = 0.045f,
            Sway = 0.3f,
            Tint = new Color(245, 248, 255),
            Opacity = 0.9f,
            LightResponse = 0.15f,
            HazeColor = new Color(212, 220, 232),
            HazeDistance = 100,
            HazeMax = 0.7f,
            CloudCoverage = 0.9f,
            SunDim = 0.5f,
        };

        public static WeatherProfile For(WeatherType type) => type switch
        {
            WeatherType.Rain => Rain,
            WeatherType.Sandstorm => Sandstorm,
            WeatherType.Snow => Snow,
            _ => null,
        };

        /// <summary>Is any weather drawn for these settings?</summary>
        public static bool IsActive(EnvironmentSettings settings) =>
            settings != null && For(settings.Weather) != null && Intensity(settings) > 0;

        public static float Intensity(EnvironmentSettings settings) =>
            settings == null || !float.IsFinite(settings.WeatherIntensity) ? 0 : Math.Clamp(settings.WeatherIntensity, 0, 1);

        /// <summary>Horizontal wind in m/s; the wind direction is the compass angle it blows towards (0 = +X, 90 = +Y).</summary>
        public static Vector2 Wind(EnvironmentSettings settings)
        {
            if (settings == null) return Vector2.Zero;
            float speed = float.IsFinite(settings.WindSpeed) ? Math.Clamp(settings.WindSpeed, 0, 40) : 0;
            speed = Math.Max(speed, For(settings.Weather)?.MinWind ?? 0);
            float angle = MathHelper.ToRadians(float.IsFinite(settings.WindDirection) ? settings.WindDirection : 0);
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
        }

        /// <summary>Velocity of the average particle, m/s.</summary>
        public Vector3 Velocity(EnvironmentSettings settings)
        {
            Vector2 wind = Wind(settings) * WindFactor;
            return new Vector3(wind.X, wind.Y, -FallSpeed);
        }

        /// <summary>Cloud cover the day/night sky draws: the weather thickens the scene's clouds.</summary>
        public static float SkyCloudCoverage(EnvironmentSettings settings)
        {
            float clouds = settings?.CloudCoverage ?? 0;
            WeatherProfile profile = For(settings?.Weather ?? WeatherType.None);
            if (profile == null) return clouds;
            return Math.Max(clouds, MathHelper.Lerp(clouds, profile.CloudCoverage, Intensity(settings)));
        }
    }
}
