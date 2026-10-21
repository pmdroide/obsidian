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
        /// <summary>
        /// How wet Wet Surface materials get at full intensity (0..1). Rain soaks them and fills puddles;
        /// the renderer eases towards it (<see cref="SurfaceWetness"/>) so ground wets and dries over time.
        /// </summary>
        public float SurfaceWetness;
        /// <summary>Raindrop ripples in puddles at full intensity (0..1).</summary>
        public float PuddleRipples;
        /// <summary>
        /// Density (per metre) at the ground of the mist the weather raises at full intensity and Ground Fog 1.
        /// It thins out with height (<see cref="EnvironmentSettings.GroundFogHeight"/>), rises over
        /// <see cref="SurfaceWetness.MistRiseSeconds"/> and lingers after the weather.
        /// </summary>
        public float GroundFog;
        /// <summary>sRGB colour of the ground mist.</summary>
        public Color GroundFogColor = Color.White;
        /// <summary>Raindrops beading on and running down materials with Rain Drops on, at full intensity (0..1).</summary>
        public float SurfaceDrops;

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
            SurfaceWetness = 1,
            PuddleRipples = 1,
            GroundFog = 0.12f,
            GroundFogColor = new Color(168, 176, 186),
            SurfaceDrops = 1,
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
            // Dust dragged along the ground
            GroundFog = 0.12f,
            GroundFogColor = new Color(186, 138, 90),
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
            SurfaceWetness = 0.3f,
            GroundFog = 0.05f,
            GroundFogColor = new Color(214, 222, 234),
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

        /// <summary>Wetness Wet Surface materials move towards under these settings (0 = dry).</summary>
        public static float TargetWetness(EnvironmentSettings settings) =>
            (For(settings?.Weather ?? WeatherType.None)?.SurfaceWetness ?? 0) * Intensity(settings);

        /// <summary>Strength of the raindrop ripples in puddles under these settings.</summary>
        public static float TargetRipples(EnvironmentSettings settings) =>
            (For(settings?.Weather ?? WeatherType.None)?.PuddleRipples ?? 0) * Intensity(settings);

        /// <summary>Ground mist density at the base (per metre) the scene moves towards under these settings.</summary>
        public static float TargetGroundFog(EnvironmentSettings settings) =>
            (For(settings?.Weather ?? WeatherType.None)?.GroundFog ?? 0) * Intensity(settings) *
            (settings == null || !float.IsFinite(settings.GroundFog) ? 0 : Math.Clamp(settings.GroundFog, 0, 1));

        /// <summary>Strength of the raindrops on surfaces under these settings.</summary>
        public static float TargetSurfaceDrops(EnvironmentSettings settings) =>
            (For(settings?.Weather ?? WeatherType.None)?.SurfaceDrops ?? 0) * Intensity(settings) *
            (settings == null || !float.IsFinite(settings.SurfaceDrops) ? 0 : Math.Clamp(settings.SurfaceDrops, 0, 1));

        /// <summary>Cloud cover the day/night sky draws: the weather thickens the scene's clouds.</summary>
        public static float SkyCloudCoverage(EnvironmentSettings settings)
        {
            float clouds = settings?.CloudCoverage ?? 0;
            WeatherProfile profile = For(settings?.Weather ?? WeatherType.None);
            if (profile == null) return clouds;
            return Math.Max(clouds, MathHelper.Lerp(clouds, profile.CloudCoverage, Intensity(settings)));
        }
    }

    /// <summary>
    /// Scene-wide wetness of Wet Surface materials. It follows the weather over time: rain soaks the
    /// ground in about <see cref="WetSeconds"/> and it dries in about <see cref="DrySeconds"/>.
    /// The ground mist rises and clears the same way (<see cref="MistRiseSeconds"/>/<see cref="MistClearSeconds"/>),
    /// and so do the raindrops on surfaces (<see cref="DropsSeconds"/>/<see cref="DropsDrySeconds"/>).
    /// A newly shown scene starts at its weather's state instead of drying out or soaking in.
    /// </summary>
    public sealed class SurfaceWetness
    {
        public const float WetSeconds = 8;
        public const float DrySeconds = 40;
        public const float RippleSeconds = 1.5f;
        public const float MistRiseSeconds = 12;
        public const float MistClearSeconds = 30;
        public const float DropsSeconds = 4;
        public const float DropsDrySeconds = 25;

        private object _scene;

        /// <summary>0 dry .. 1 soaked with full puddles.</summary>
        public float Wetness { get; private set; }
        /// <summary>0 still puddles .. 1 heavy raindrop ripples.</summary>
        public float Ripples { get; private set; }
        /// <summary>Ground mist density at its base, per metre (0 = none).</summary>
        public float GroundFog { get; private set; }
        /// <summary>Linear colour of the ground mist; keeps the last weather's colour while it clears.</summary>
        public Vector3 GroundFogColor { get; private set; } = Vector3.One;
        /// <summary>0 dry .. 1 surfaces covered in raindrops.</summary>
        public float Drops { get; private set; }

        /// <param name="scene">Identity of the shown scene; a change snaps to the new weather.</param>
        /// <param name="seconds">Elapsed time; 0 (paused) holds the current state.</param>
        public void Update(object scene, EnvironmentSettings settings, double seconds)
        {
            float wetness = WeatherProfile.TargetWetness(settings);
            float ripples = WeatherProfile.TargetRipples(settings);
            float mist = WeatherProfile.TargetGroundFog(settings);
            float drops = WeatherProfile.TargetSurfaceDrops(settings);
            WeatherProfile profile = WeatherProfile.For(settings?.Weather ?? WeatherType.None);
            Vector3 mistColor = profile != null && mist > 0 ? Linear(profile.GroundFogColor) : GroundFogColor;
            if (!ReferenceEquals(scene, _scene))
            {
                _scene = scene;
                Wetness = wetness;
                Ripples = ripples;
                GroundFog = mist;
                GroundFogColor = mistColor;
                Drops = drops;
                return;
            }
            float dt = double.IsFinite(seconds) ? (float)Math.Max(seconds, 0) : 0;
            Wetness = Approach(Wetness, wetness, dt / (wetness > Wetness ? WetSeconds : DrySeconds));
            Ripples = Approach(Ripples, ripples, dt / RippleSeconds);
            // Density is per metre: step by the larger density so the timing is the same for every weather
            float mistScale = Math.Max(Math.Max(mist, GroundFog), 1e-4f);
            GroundFog = Approach(GroundFog, mist, mistScale * dt / (mist > GroundFog ? MistRiseSeconds : MistClearSeconds));
            // A new weather's mist takes on its colour over a few seconds
            GroundFogColor = Vector3.Lerp(GroundFogColor, mistColor, Math.Min(1, dt / 3));
            Drops = Approach(Drops, drops, dt / (drops > Drops ? DropsSeconds : DropsDrySeconds));
        }

        private static Vector3 Linear(Color color)
        {
            Vector3 c = color.ToVector3();
            return new Vector3((float)Math.Pow(c.X, 2.2), (float)Math.Pow(c.Y, 2.2), (float)Math.Pow(c.Z, 2.2));
        }

        private static float Approach(float value, float target, float step) =>
            value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    }
}
