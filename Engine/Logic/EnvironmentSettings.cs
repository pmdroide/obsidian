using System;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>Scene-owned sky selection, initial time, clouds for the animated sky, and weather.</summary>
    public sealed class EnvironmentSettings
    {
        public bool DayNightCycle { get; set; }
        public string SkyboxPath { get; set; }
        public float TimeOfDay { get; set; } = 12;
        public float CycleDurationMinutes { get; set; } = 10;
        /// <summary>0 = clear sky, 1 = overcast. Only drawn by the day/night sky.</summary>
        public float CloudCoverage { get; set; } = 0.45f;
        /// <summary>Wind speed multiplier for cloud drift; 0 freezes the clouds.</summary>
        public float CloudSpeed { get; set; } = 1;

        // Day/night sky look. Colours are sRGB; the renderer converts them to linear.
        public Color DaySkyColor { get; set; } = new Color(81, 148, 231);
        public Color DayHorizonColor { get; set; } = new Color(210, 228, 249);
        public Color SunsetColor { get; set; } = new Color(243, 136, 81);
        public Color NightSkyColor { get; set; } = new Color(15, 21, 38);
        public Color NightHorizonColor { get; set; } = new Color(34, 41, 59);
        /// <summary>Multiplies the sun light and disc.</summary>
        public float SunBrightness { get; set; } = 1;
        public float SunSize { get; set; } = 1;
        /// <summary>Multiplies the moonlight and disc.</summary>
        public float MoonBrightness { get; set; } = 1;
        public float MoonSize { get; set; } = 1;
        public float StarBrightness { get; set; } = 1;
        /// <summary>Exposure offsets in EV (stops) blended by daylight; negative values darken.</summary>
        public float DayExposure { get; set; } = -1f;
        public float NightExposure { get; set; } = -2.5f;

        // Weather (rain, sandstorm, snow), drawn in both Edit and Play. See WeatherProfile for each type's look.
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WeatherType Weather { get; set; } = WeatherType.None;
        /// <summary>0 = no particles or haze, 1 = the heaviest the type gets.</summary>
        public float WeatherIntensity { get; set; } = 0.7f;
        /// <summary>Scales the weather's haze; 0 leaves only the particles.</summary>
        public float WeatherHaze { get; set; } = 0.6f;
        /// <summary>Horizontal wind in m/s that blows rain, snow and sand.</summary>
        public float WindSpeed { get; set; } = 4;
        /// <summary>Compass angle in degrees the wind blows towards: 0 = +X, 90 = +Y.</summary>
        public float WindDirection { get; set; } = 45;

        public EnvironmentSettings Clone() => (EnvironmentSettings)MemberwiseClone();

        public void Normalize()
        {
            TimeOfDay = float.IsFinite(TimeOfDay) ? Math.Clamp(TimeOfDay, 0, 24) % 24 : 12;
            CycleDurationMinutes = float.IsFinite(CycleDurationMinutes)
                ? Math.Clamp(CycleDurationMinutes, 0.1f, 1440) : 10;
            CloudCoverage = float.IsFinite(CloudCoverage) ? Math.Clamp(CloudCoverage, 0, 1) : 0.45f;
            CloudSpeed = float.IsFinite(CloudSpeed) ? Math.Clamp(CloudSpeed, 0, 10) : 1;
            SunBrightness = Limit(SunBrightness, 0, 5, 1);
            SunSize = Limit(SunSize, 0.25f, 4, 1);
            MoonBrightness = Limit(MoonBrightness, 0, 5, 1);
            MoonSize = Limit(MoonSize, 0.25f, 4, 1);
            StarBrightness = Limit(StarBrightness, 0, 3, 1);
            DayExposure = Limit(DayExposure, -4, 4, -1f);
            NightExposure = Limit(NightExposure, -4, 4, -2.5f);
            if (!Enum.IsDefined(Weather)) Weather = WeatherType.None;
            WeatherIntensity = Limit(WeatherIntensity, 0, 1, 0.7f);
            WeatherHaze = Limit(WeatherHaze, 0, 1, 0.6f);
            WindSpeed = Limit(WindSpeed, 0, 40, 4);
            WindDirection = float.IsFinite(WindDirection) ? ((WindDirection % 360) + 360) % 360 : 45;
        }

        private static float Limit(float value, float min, float max, float fallback) =>
            float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

        public static float AdvanceHour(float initialHour, double elapsedSeconds, float durationMinutes) =>
            (float)((initialHour + elapsedSeconds * 24 / (Math.Max(0.1f, durationMinutes) * 60)) % 24);

        public static Vector3 SunDirection(float hour)
        {
            float angle = (hour - 6) / 24 * MathHelper.TwoPi;
            // Z is up. A slight Y component avoids the light's look-at pole at noon.
            return Vector3.Normalize(new Vector3((float)Math.Cos(angle), 0.15f, (float)Math.Sin(angle)));
        }

        /// <summary>The moon sits opposite the sun, so it rises as the sun sets.</summary>
        public static Vector3 MoonDirection(float hour)
        {
            Vector3 sun = SunDirection(hour);
            return Vector3.Normalize(new Vector3(-sun.X, sun.Y, -sun.Z));
        }
    }
}
