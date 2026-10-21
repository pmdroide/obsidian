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
        /// <summary>
        /// Half-Life: Alyx's filmic curve (post_process.vfx, Docs/Research/Source2Look R1): the eight-parameter Hable curve
        /// H on 2.8 x the exposed colour, divided by H(2^White Point stops); parameters are the Filmic* settings (.vpost).
        /// </summary>
        Source2,
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
        /// <summary>
        /// Half-Life: Alyx (CTonemapSystem log-luminance mode, Docs/Research/Source2Look R2): the frame's median luminance
        /// is exposed to the Key value with Source 2's exponent, exposure = Key / 2^ln(median), i.e. it follows L^-0.69.
        /// </summary>
        Source2,
    }

    /// <summary>How bloom picks the bright parts of the frame. Saved by name: never rename a value.</summary>
    public enum BloomThresholdShape
    {
        /// <summary>Brightest channel above Bloom Start, with a quadratic knee of Bloom Knee below it.</summary>
        SoftKnee,
        /// <summary>
        /// Half-Life: Alyx (gaussian_bloom_blur.vfx, Docs/Research/Source2Look R3): the whole colour weighted by
        /// saturate((luma x exposure - Bloom Start) / Bloom Knee), luma = 0.3 R + 0.59 G + 0.11 B.
        /// </summary>
        Source2,
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
        public TonemapCurve Tonemap { get; set; } = TonemapCurve.Source2;
        /// <summary>Exposure compensation in EV, on top of auto exposure (Source 2's m_flExposureBias, applied as 2^EV).</summary>
        public float Exposure { get; set; } = 0;
        /// <summary>Scene brightness that becomes white (Hejl, Hable, Reinhard).</summary>
        public float WhitePoint { get; set; } = 1.1f;
        public float SCurveStrength { get; set; } = 0.05f;
        // Source 2 filmic curve (TonemapParameters_t of the .vpost files). Defaults: Hable's published constants, which
        // HLA's neutral profiles use; lighting/postprocessing/basepostprocess.vpost has 0.319/0.5047/0.1619/0.4667/0/0.7475/4.
        public float FilmicShoulder { get; set; } = 0.15f;
        public float FilmicLinearStrength { get; set; } = 0.5f;
        public float FilmicLinearAngle { get; set; } = 0.1f;
        public float FilmicToeStrength { get; set; } = 0.2f;
        public float FilmicToeNumerator { get; set; } = 0.02f;
        public float FilmicToeDenominator { get; set; } = 0.3f;
        /// <summary>Source 2 curve: white point in stops (m_flWhitePoint); the curve is normalised by H(2^stops).</summary>
        public float FilmicWhitePoint { get; set; } = 4;
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
        public ExposureMetering Metering { get; set; } = ExposureMetering.Source2;
        /// <summary>Average / Source 2 metering: the average (median) luminance is exposed to this value (0.18 = middle grey).</summary>
        public float AutoExposureKey { get; set; } = 0.18f;
        /// <summary>Bright-pixel metering: share of the frame (percent) counted as its highlights.</summary>
        public float BrightPixelPercent { get; set; } = 2;
        /// <summary>Bright-pixel metering: linear brightness (before compensation) the highlights are exposed to.</summary>
        public float BrightPixelTarget { get; set; } = 0.18f;
        /// <summary>Bright-pixel metering: the average never ends up darker than this, so a lamp can't black out a room.</summary>
        public float MinAverageLuminance { get; set; } = 0.03f;
        /// <summary>Clamp of auto exposure in EV. HLA: 0.25 to 8 (-2 to +3 EV), env_tonemap_controller / post_processing_volume.</summary>
        public float AutoExposureMin { get; set; } = -2;
        public float AutoExposureMax { get; set; } = 3;
        /// <summary>
        /// Adaptation per second when the view gets brighter / darker. HLA (TonemappingVRForward): rate 0.3, sped up to
        /// at most 4 x (1.2) while exposure has to come down (mat_tonemap_force_accelerate_exposure_down).
        /// </summary>
        public float AdaptationSpeedUp { get; set; } = 1.2f;
        public float AdaptationSpeedDown { get; set; } = 0.3f;
        /// <summary>0 = meter the whole frame equally (HLA), 1 = favour the centre.</summary>
        public float CenterWeight { get; set; } = 0;

        // ---- Bloom ----
        public bool Bloom { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BloomThresholdShape BloomShape { get; set; } = BloomThresholdShape.Source2;
        /// <summary>Exposed brightness where bloom starts (m_flBloomThreshold; HLA basepostprocess 1.976); 1 = white.</summary>
        public float BloomStart { get; set; } = 1.976f;
        /// <summary>Soft Knee: transition below Bloom Start. Source 2: width of the ramp above it (m_flBloomThresholdWidth, 2.3655).</summary>
        public float BloomKnee { get; set; } = 2.3655f;
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
            if (!Enum.IsDefined(Tonemap)) Tonemap = TonemapCurve.Source2;
            if (!Enum.IsDefined(Metering)) Metering = ExposureMetering.Source2;
            if (!Enum.IsDefined(BloomShape)) BloomShape = BloomThresholdShape.Source2;
            Exposure = Limit(Exposure, -8, 8, 0);
            FilmicShoulder = Limit(FilmicShoulder, 0.001f, 4, 0.15f);
            FilmicLinearStrength = Limit(FilmicLinearStrength, 0, 4, 0.5f);
            FilmicLinearAngle = Limit(FilmicLinearAngle, 0, 4, 0.1f);
            FilmicToeStrength = Limit(FilmicToeStrength, 0, 4, 0.2f);
            FilmicToeNumerator = Limit(FilmicToeNumerator, 0, 1, 0.02f);
            FilmicToeDenominator = Limit(FilmicToeDenominator, 0.001f, 4, 0.3f);
            FilmicWhitePoint = Limit(FilmicWhitePoint, 0, 16, 4);
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
            AutoExposureMin = Limit(AutoExposureMin, -12, 12, -2);
            AutoExposureMax = Limit(AutoExposureMax, AutoExposureMin, 12, Math.Max(3, AutoExposureMin));
            AdaptationSpeedUp = Limit(AdaptationSpeedUp, 0, 50, 1.2f);
            AdaptationSpeedDown = Limit(AdaptationSpeedDown, 0, 50, 0.3f);
            CenterWeight = Limit(CenterWeight, 0, 1, 0);
            BloomStart = Limit(BloomStart, 0, 64, 1.976f);
            BloomKnee = Limit(BloomKnee, 0, 16, 2.3655f);
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
