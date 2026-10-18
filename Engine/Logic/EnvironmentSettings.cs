using System;
using System.Text.Json.Serialization;
using Engine.Voxels;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>Scene-owned sky selection, initial time, clouds for the animated sky, weather, volumetric fog, post processing and the voxel world.</summary>
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

        // Volumetric fog (froxels): the haze and light shafts (god rays) of this scene. The renderer copies these into
        // GameSettings.g_Froxel* every frame. Defaults are an outdoor haze that stays thin near the camera; denser
        // pockets (a dusty room) are Fog Volume components.
        /// <summary>Fog density everywhere (per metre, scaled by the distance ramp below).</summary>
        public float FogDensity { get; set; } = 0.03f;
        /// <summary>How much of the light behind the fog it swallows (extinction = density * absorption).</summary>
        public float FogAbsorption { get; set; } = 0.1f;
        /// <summary>Brightness of the sun's light in the fog (light shafts).</summary>
        public float FogSunScatter { get; set; } = 0.1f;
        /// <summary>Brightness of point and spot lights in the fog.</summary>
        public float FogLightScatter { get; set; } = 1.5f;
        /// <summary>Brightness of the baked bounce light (Lighting > probe volume) in the fog; none without a bake.</summary>
        public float FogIndirectScatter { get; set; } = 0.1f;
        /// <summary>Henyey-Greenstein g: 0 scatters evenly, towards 1 the fog glows mostly when looking at the light.</summary>
        public float FogAnisotropy { get; set; } = 0.45f;
        /// <summary>Distance (m) where the density starts ramping up from Near Density.</summary>
        public float FogStartDistance { get; set; } = 30;
        /// <summary>Distance (m) where the density is full.</summary>
        public float FogFullDistance { get; set; } = 400;
        /// <summary>Fraction of the density kept before Start Distance: 0 = clear near the camera, 1 = no ramp.</summary>
        public float FogNearDensity { get; set; }
        /// <summary>How much of the fog beyond Sky Shaft Distance covers the sky: 0 = none, 1 = all.</summary>
        public float FogSkyStrength { get; set; } = 0.1f;
        /// <summary>Fog in front of the sky up to this distance (m) is kept whole, so shafts show against it.</summary>
        public float FogSkyShaftDistance { get; set; } = 200;
        /// <summary>Caps how far auto exposure brightens dark views, in EV (Post Processing's maximum still applies).
        /// Lower it to keep dark interiors dark around bright openings.</summary>
        public float MaxAutoExposure { get; set; } = 3;

        /// <summary>Tonemapping, grading, eye adaptation and bloom of this scene (Source 2 look, phase 0.3).</summary>
        public PostProcessSettings Post { get; set; } = new PostProcessSettings();

        // Voxel world: block terrain generated from these settings, drawn and collidable in both Edit and Play
        // (Engine/Voxels, VoxelSystem). Changing any of them regenerates the world.
        public bool Voxels { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public VoxelTerrain VoxelTerrain { get; set; } = VoxelTerrain.Hills;
        public int VoxelSeed { get; set; } = 1337;
        /// <summary>Blocks along X and Y: 32-256, a multiple of the 32-block chunk.</summary>
        public int VoxelWorldSize { get; set; } = 128;
        /// <summary>Blocks along Z: 16-128, a multiple of 16.</summary>
        public int VoxelWorldHeight { get; set; } = 64;
        /// <summary>Edge length of one block in metres.</summary>
        public float VoxelBlockSize { get; set; } = 1;
        /// <summary>World Z of the voxel world's floor; the world is centred on the origin in X/Y.</summary>
        public float VoxelBaseHeight { get; set; } = -24;
        public bool VoxelCaves { get; set; } = true;
        /// <summary>0 = no trees, 1 = a dense forest on grass.</summary>
        public float VoxelTrees { get; set; } = 0.5f;
        /// <summary>Water surface as a fraction of the world height; 0 = no sea.</summary>
        public float VoxelSeaLevel { get; set; } = 0.3f;
        /// <summary>Static colliders for the chunks (bodies, characters and raycasts hit the blocks).</summary>
        public bool VoxelCollision { get; set; } = true;

        public EnvironmentSettings Clone()
        {
            var clone = (EnvironmentSettings)MemberwiseClone();
            clone.Post = (Post ?? new PostProcessSettings()).Clone();
            return clone;
        }

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
            FogDensity = Limit(FogDensity, 0.001f, 5, 0.03f);
            FogAbsorption = Limit(FogAbsorption, 0, 1, 0.1f);
            FogSunScatter = Limit(FogSunScatter, 0, 4, 0.1f);
            FogLightScatter = Limit(FogLightScatter, 0, 16, 1.5f);
            FogIndirectScatter = Limit(FogIndirectScatter, 0, 4, 0.1f);
            FogAnisotropy = Limit(FogAnisotropy, -0.95f, 0.95f, 0.45f);
            FogStartDistance = Limit(FogStartDistance, 0, 5000, 30);
            FogFullDistance = Limit(FogFullDistance, FogStartDistance + 1, 10000, Math.Max(400, FogStartDistance + 1));
            FogNearDensity = Limit(FogNearDensity, 0, 1, 0);
            FogSkyStrength = Limit(FogSkyStrength, 0, 1, 0.1f);
            FogSkyShaftDistance = Limit(FogSkyShaftDistance, 0, 10000, 200);
            MaxAutoExposure = Limit(MaxAutoExposure, -3, 3, 3);
            Post ??= new PostProcessSettings();
            Post.Normalize();
            if (!Enum.IsDefined(VoxelTerrain)) VoxelTerrain = VoxelTerrain.Hills;
            VoxelWorldSize = VoxelWorldSettings.ClampSize(VoxelWorldSize);
            VoxelWorldHeight = VoxelWorldSettings.ClampHeight(VoxelWorldHeight);
            VoxelBlockSize = VoxelWorldSettings.ClampBlockSize(VoxelBlockSize);
            VoxelBaseHeight = Limit(VoxelBaseHeight, -1000, 1000, -24);
            VoxelTrees = Limit(VoxelTrees, 0, 1, 0.5f);
            VoxelSeaLevel = Limit(VoxelSeaLevel, 0, 0.9f, 0.3f);
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
