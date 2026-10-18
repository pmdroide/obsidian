using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>Tonemapping curve of the final image (PostProcessing.fx). Saved by name: never rename a value.</summary>
    public enum TonemapCurve
    {
        /// <summary>Hejl/Burgess-Dawson filmic fit (2015); the engine's original curve.</summary>
        Hejl,
        /// <summary>Narkowicz's fit of the ACES reference rendering transform: contrasty, saturated highlights.</summary>
        Aces,
        /// <summary>John Hable's Uncharted 2 filmic curve: a long, soft shoulder.</summary>
        Hable,
        /// <summary>Extended Reinhard on luminance, white at the White Point: flat and gentle.</summary>
        Reinhard,
        /// <summary>No curve: exposure, clamp and sRGB, like Source 1's HDR.</summary>
        Linear,
    }

    /// <summary>How auto exposure meters the frame. Saved by name.</summary>
    public enum ExposureMetering
    {
        /// <summary>
        /// Source's tonemap controller: the brightest <see cref="PostProcessSettings.BrightPixelPercent"/> % of the frame
        /// is exposed to <see cref="PostProcessSettings.BrightPixelTarget"/>, so highlights hold and dark rooms stay dark.
        /// </summary>
        BrightPixels,
        /// <summary>The (centre-weighted) average log luminance is exposed to the Key value.</summary>
        Average,
    }

    /// <summary>
    /// The scene's post processing (Source 2 look, Docs/Research/Source2Look phases 0.3 and 1.3-1.5): tonemapping,
    /// colour grading, eye adaptation and bloom. Saved in <see cref="EnvironmentSettings.Post"/>; the renderer blends it
    /// with the Post Process Volume components around the camera and copies the result into
    /// <see cref="Recources.GameSettings"/> every frame (Renderer.ApplyScenePost), so scripts change it through
    /// ScriptBehaviour.SceneEnvironment.Post. Every public get/set property is a setting the volumes can override.
    /// </summary>
    public sealed class PostProcessSettings
    {
        // ---- Tonemapping ----
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TonemapCurve Tonemap { get; set; } = TonemapCurve.Hejl;
        /// <summary>Exposure compensation in EV, on top of auto exposure.</summary>
        public float Exposure { get; set; } = 0.75f;
        /// <summary>Scene brightness that becomes white (Hejl, Hable, Reinhard).</summary>
        public float WhitePoint { get; set; } = 1.1f;
        public float SCurveStrength { get; set; } = 0.05f;
        /// <summary>Exact sRGB encoding of the output; off = a plain 2.2 gamma (the engine's original output).</summary>
        public bool ExactSrgb { get; set; } = true;

        // ---- Colour grading ----
        public bool ColorGrading { get; set; } = true;
        /// <summary>Content path of a colour lookup texture (a 256 x 16 strip like Shaders/PostProcessing/lut); empty = the built-in one.</summary>
        public string LutPath { get; set; }
        /// <summary>Blending only: a second LUT faded in by <see cref="LutBlend"/> (two volumes with different LUTs).</summary>
        [JsonIgnore] public string BlendLutPath { get; set; }
        [JsonIgnore] public float LutBlend { get; set; }
        public float Saturation { get; set; } = 1;
        public float Contrast { get; set; } = 1;
        /// <summary>Multiplies the image before tonemapping (sRGB colour).</summary>
        public Color ColorFilter { get; set; } = Color.White;
        public float Vignette { get; set; } = 1;
        public float ChromaticAberration { get; set; }

        // ---- Eye adaptation ----
        public bool AutoExposure { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExposureMetering Metering { get; set; } = ExposureMetering.BrightPixels;
        /// <summary>Average metering: the average luminance is exposed to this value (0.18 = middle grey).</summary>
        public float AutoExposureKey { get; set; } = 0.18f;
        /// <summary>Bright-pixel metering: share of the frame (percent) counted as its highlights.</summary>
        public float BrightPixelPercent { get; set; } = 2;
        /// <summary>Bright-pixel metering: linear brightness (before compensation) the highlights are exposed to.</summary>
        public float BrightPixelTarget { get; set; } = 0.18f;
        /// <summary>Bright-pixel metering: the average never ends up darker than this, so a lamp can't black out a room.</summary>
        public float MinAverageLuminance { get; set; } = 0.03f;
        public float AutoExposureMin { get; set; } = -3;
        public float AutoExposureMax { get; set; } = 3;
        /// <summary>Adaptation per second when the view gets brighter / darker.</summary>
        public float AdaptationSpeedUp { get; set; } = 3;
        public float AdaptationSpeedDown { get; set; } = 1;
        /// <summary>0 = meter the whole frame equally, 1 = favour the centre.</summary>
        public float CenterWeight { get; set; } = 0.5f;

        // ---- Bloom ----
        public bool Bloom { get; set; } = true;
        /// <summary>Exposed brightness where bloom starts (Source's mat_tonemap_bloom_start_value); 1 = white.</summary>
        public float BloomStart { get; set; } = 1.5f;
        /// <summary>Width of the soft transition below Bloom Start.</summary>
        public float BloomKnee { get; set; } = 0.5f;
        /// <summary>Amount of bloom added to the image (mat_tonemap_bloom_scale).</summary>
        public float BloomScale { get; set; } = 0.35f;

        // ---- Area mood ----
        /// <summary>Multiplies the scene's volumetric fog density (Post Process Volumes thicken or clear the air).</summary>
        public float FogDensityScale { get; set; } = 1;
        /// <summary>Strength of the afterimage bright lights leave (Half-Life: Alyx's retina burn); 0 = off.</summary>
        public float RetinaBurn { get; set; }

        public PostProcessSettings Clone() => (PostProcessSettings)MemberwiseClone();

        public void Normalize()
        {
            if (!Enum.IsDefined(Tonemap)) Tonemap = TonemapCurve.Hejl;
            if (!Enum.IsDefined(Metering)) Metering = ExposureMetering.BrightPixels;
            Exposure = Limit(Exposure, -8, 8, 0.75f);
            WhitePoint = Limit(WhitePoint, 0.1f, 32, 1.1f);
            SCurveStrength = Limit(SCurveStrength, -1, 1, 0.05f);
            Saturation = Limit(Saturation, 0, 3, 1);
            Contrast = Limit(Contrast, 0.25f, 3, 1);
            Vignette = Limit(Vignette, 0, 2, 1);
            ChromaticAberration = Limit(ChromaticAberration, 0, 5, 0);
            AutoExposureKey = Limit(AutoExposureKey, 0.001f, 4, 0.18f);
            BrightPixelPercent = Limit(BrightPixelPercent, 0.01f, 50, 2);
            BrightPixelTarget = Limit(BrightPixelTarget, 0.01f, 16, 0.18f);
            MinAverageLuminance = Limit(MinAverageLuminance, 0, 1, 0.03f);
            AutoExposureMin = Limit(AutoExposureMin, -12, 12, -3);
            AutoExposureMax = Limit(AutoExposureMax, AutoExposureMin, 12, Math.Max(3, AutoExposureMin));
            AdaptationSpeedUp = Limit(AdaptationSpeedUp, 0, 50, 3);
            AdaptationSpeedDown = Limit(AdaptationSpeedDown, 0, 50, 1);
            CenterWeight = Limit(CenterWeight, 0, 1, 0.5f);
            BloomStart = Limit(BloomStart, 0, 64, 1.5f);
            BloomKnee = Limit(BloomKnee, 0, 16, 0.5f);
            BloomScale = Limit(BloomScale, 0, 1, 0.35f);
            FogDensityScale = Limit(FogDensityScale, 0, 16, 1);
            RetinaBurn = Limit(RetinaBurn, 0, 4, 0);
        }

        private static float Limit(float value, float min, float max, float fallback) =>
            float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

        // ---- Blending (Post Process Volumes) ----

        /// <summary>Every setting a volume can override, by property name.</summary>
        public static IReadOnlyList<PropertyInfo> Settings { get; } = typeof(PostProcessSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

        public static PropertyInfo Find(string name) =>
            Settings.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Moves the named settings (all when <paramref name="only"/> is null) <paramref name="weight"/> of the way to
        /// <paramref name="other"/>'s values: numbers and colours interpolate, switches, curves and paths flip at half weight.
        /// </summary>
        public void BlendTowards(PostProcessSettings other, float weight, IEnumerable<string> only = null)
        {
            if (other == null || !(weight > 0)) return;
            weight = Math.Min(weight, 1);
            IEnumerable<PropertyInfo> properties = only == null ? Settings : only.Select(Find).Where(p => p != null);
            foreach (PropertyInfo property in properties)
            {
                if (property.Name == nameof(BlendLutPath) || property.Name == nameof(LutBlend)) continue;
                object from = property.GetValue(this);
                object to = property.GetValue(other);
                //LUTs fade into each other (one combined LUT per frame) instead of switching at half weight
                if (property.Name == nameof(LutPath) && !string.Equals(from as string ?? "", to as string ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    if (weight >= 0.999f) { LutPath = to as string; BlendLutPath = null; LutBlend = 0; }
                    else { BlendLutPath = string.IsNullOrEmpty(to as string) ? "" : (string)to; LutBlend = weight; }
                    continue;
                }
                object value = to;
                if (from is float f && to is float t) value = MathHelper.Lerp(f, t, weight);
                else if (from is Color a && to is Color b) value = Color.Lerp(a, b, weight);
                else if (weight < 0.5f) value = from;
                property.SetValue(this, value);
            }
        }
    }
}
