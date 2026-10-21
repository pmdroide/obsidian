using System;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Editor;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer;
using Anvil.Services;

namespace Anvil.Models;

/// <summary>
/// Inspector > Post Processing. Two kinds of settings:
/// <list type="bullet">
/// <item>The scene's post processing (EnvironmentSettings.Post: tonemapping, grading, eye adaptation, bloom; Source 2
/// look phase 0.3). Saved with the scene, reloaded when the scene changes, edited through
/// <see cref="IEditorBridge.EnqueueMutatePost"/>.</item>
/// <item>Project-wide renderer settings (<see cref="GameSettings"/>): TAA, SSR, SSAO, shadows, reflections, culling,
/// debug views. Setters marshal to the game thread because most of these touch shader parameters.</item>
/// </list>
/// </summary>
public partial class PostProcessingViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private bool _suppress;

    // Same order as the engine enums.
    public string[] TonemapCurves { get; } = { "Hejl 2015 (filmic)", "ACES", "Hable (Uncharted 2)", "Reinhard", "Linear (clamp)", "Source 2 (Half-Life: Alyx)" };
    public string[] MeteringModes { get; } = { "Bright Pixels (Source)", "Average", "Source 2 (median)" };
    public string[] BloomShapes { get; } = { "Soft Knee", "Source 2 (ramp)" };
    public string[] DebugViews { get; } = Enum.GetNames<Renderer.RenderModes>();

    // --------- Scene: tonemapping & grading ---------
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSource2Curve))] private int _tonemapCurve;
    public bool IsSource2Curve => TonemapCurve == (int)Engine.Logic.TonemapCurve.Source2;
    [ObservableProperty] private double _filmicShoulder;
    [ObservableProperty] private double _filmicLinearStrength;
    [ObservableProperty] private double _filmicLinearAngle;
    [ObservableProperty] private double _filmicToeStrength;
    [ObservableProperty] private double _filmicToeNumerator;
    [ObservableProperty] private double _filmicToeDenominator;
    [ObservableProperty] private double _filmicWhitePoint;
    [ObservableProperty] private double _whitePoint;
    [ObservableProperty] private double _exposure;
    [ObservableProperty] private double _sCurveStrength;
    [ObservableProperty] private bool _exactSrgb;
    [ObservableProperty] private bool _colorGrading;
    [ObservableProperty] private string _lutPath = "";
    [ObservableProperty] private double _saturation = 1;
    [ObservableProperty] private double _contrast = 1;
    [ObservableProperty] private Color _colorFilter = Colors.White;
    [ObservableProperty] private double _vignette = 1;
    [ObservableProperty] private double _chromaticAberration;
    [ObservableProperty] private double _retinaBurn;
    [ObservableProperty] private double _fogDensityScale = 1;

    // --------- Scene: eye adaptation ---------
    [ObservableProperty] private bool _autoExposure;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBrightPixelMetering))] private int _metering;
    [ObservableProperty] private double _autoExposureKey;
    [ObservableProperty] private double _brightPixelPercent;
    [ObservableProperty] private double _brightPixelTarget;
    [ObservableProperty] private double _minAverageLuminance;
    [ObservableProperty] private double _autoExposureMin;
    [ObservableProperty] private double _autoExposureMax;
    [ObservableProperty] private double _autoExposureSpeedDarkToLight;
    [ObservableProperty] private double _autoExposureSpeedLightToDark;
    [ObservableProperty] private double _autoExposureCenterWeight;
    public bool IsBrightPixelMetering => Metering == 0;

    // --------- Scene: bloom ---------
    [ObservableProperty] private bool _bloomEnabled;
    [ObservableProperty] private int _bloomShape;
    [ObservableProperty] private double _bloomStart;
    [ObservableProperty] private double _bloomKnee;
    [ObservableProperty] private double _bloomScale;

    // --------- Project: anti-aliasing ---------
    [ObservableProperty] private bool _taa;
    [ObservableProperty] private bool _tonemapTaa;
    [ObservableProperty] private bool _taaVelocity;
    [ObservableProperty] private double _taaSharpness;
    [ObservableProperty] private bool _fxaa;

    // --------- Project: bloom shape ---------
    [ObservableProperty] private double _bloomRadius1;
    [ObservableProperty] private double _bloomStrength1;
    [ObservableProperty] private double _bloomRadius2;
    [ObservableProperty] private double _bloomStrength2;
    [ObservableProperty] private double _bloomRadius3;
    [ObservableProperty] private double _bloomStrength3;
    [ObservableProperty] private double _bloomRadius4;
    [ObservableProperty] private double _bloomStrength4;
    [ObservableProperty] private double _bloomRadius5;
    [ObservableProperty] private double _bloomStrength5;

    // --------- Project: Screen-Space Reflections ---------
    [ObservableProperty] private bool _ssrEnabled;
    [ObservableProperty] private bool _ssrStochastic;
    [ObservableProperty] private bool _ssrTemporalNoise;
    [ObservableProperty] private bool _ssrFireflyReduction;
    [ObservableProperty] private double _ssrFireflyThreshold;
    [ObservableProperty] private int _ssrSamples;
    [ObservableProperty] private int _ssrRefinementSamples;

    // --------- Project: Ambient Occlusion ---------
    [ObservableProperty] private bool _ssaoEnabled;
    [ObservableProperty] private bool _ssaoBlur;
    [ObservableProperty] private int _ssaoSamples;
    [ObservableProperty] private double _ssaoRadius;
    [ObservableProperty] private double _ssaoStrength;

    // --------- Project: Volumetric Fog (the per-scene values are in EnvironmentViewModel) ---------
    [ObservableProperty] private bool _fogEnabled;
    [ObservableProperty] private double _fogHistoryBlend;

    // --------- Project: Shadows & Reflections (Source 2 look phases 2-3) ---------
    [ObservableProperty] private int _shadowAtlasSize;
    [ObservableProperty] private int _shadowUpdatesPerFrame;
    [ObservableProperty] private double _shadowFadeStart;
    [ObservableProperty] private double _shadowFadeEnd;
    [ObservableProperty] private double _shadowSizeCull;
    [ObservableProperty] private int _reflectionProbeSize;
    [ObservableProperty] private double _cubemapNormalizationMax;
    [ObservableProperty] private double _cubemapNormalizationRoughnessBegin;

    // --------- Project: Culling (phase 7) ---------
    [ObservableProperty] private double _sizeCullThreshold;
    [ObservableProperty] private bool _occlusionCulling;

    // --------- General view options ---------
    [ObservableProperty] private int _debugView;
    [ObservableProperty] private bool _highlightMeshes;
    [ObservableProperty] private bool _drawSdf;
    [ObservableProperty] private bool _drawSdfVolume;

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        LoadFromEngine();
        // The scene's post block: reload with the scene (and on Stop, which restores what scripts changed)
        bridge.SceneChanged += () => Dispatcher.UIThread.Post(LoadScenePost);
        bridge.ModeChanged += _ => Dispatcher.UIThread.Post(LoadScenePost);
    }

    /// <summary>The scene's post processing block into the view model.</summary>
    private void LoadScenePost()
    {
        if (_bridge == null) return;
        PostProcessSettings post = _bridge.GetEnvironmentSettings().Post ?? new PostProcessSettings();
        _suppress = true;
        try
        {
            TonemapCurve = (int)post.Tonemap;
            Exposure = post.Exposure;
            WhitePoint = post.WhitePoint;
            FilmicShoulder = post.FilmicShoulder;
            FilmicLinearStrength = post.FilmicLinearStrength;
            FilmicLinearAngle = post.FilmicLinearAngle;
            FilmicToeStrength = post.FilmicToeStrength;
            FilmicToeNumerator = post.FilmicToeNumerator;
            FilmicToeDenominator = post.FilmicToeDenominator;
            FilmicWhitePoint = post.FilmicWhitePoint;
            SCurveStrength = post.SCurveStrength;
            ExactSrgb = post.ExactSrgb;
            ColorGrading = post.ColorGrading;
            LutPath = post.LutPath ?? "";
            Saturation = post.Saturation;
            Contrast = post.Contrast;
            ColorFilter = Color.FromRgb(post.ColorFilter.R, post.ColorFilter.G, post.ColorFilter.B);
            Vignette = post.Vignette;
            ChromaticAberration = post.ChromaticAberration;
            RetinaBurn = post.RetinaBurn;
            FogDensityScale = post.FogDensityScale;
            AutoExposure = post.AutoExposure;
            Metering = (int)post.Metering;
            AutoExposureKey = post.AutoExposureKey;
            BrightPixelPercent = post.BrightPixelPercent;
            BrightPixelTarget = post.BrightPixelTarget;
            MinAverageLuminance = post.MinAverageLuminance;
            AutoExposureMin = post.AutoExposureMin;
            AutoExposureMax = post.AutoExposureMax;
            AutoExposureSpeedDarkToLight = post.AdaptationSpeedUp;
            AutoExposureSpeedLightToDark = post.AdaptationSpeedDown;
            AutoExposureCenterWeight = post.CenterWeight;
            BloomEnabled = post.Bloom;
            BloomShape = (int)post.BloomShape;
            BloomStart = post.BloomStart;
            BloomKnee = post.BloomKnee;
            BloomScale = post.BloomScale;
        }
        finally { _suppress = false; }
    }

    /// <summary>
    /// One-shot copy of the engine-side defaults into the VM. Called once on attach; reading static fields from the
    /// UI thread is safe (no shader touches), only writes need to be marshalled.
    /// </summary>
    private void LoadFromEngine()
    {
        _suppress = true;
        try
        {
            Taa                  = GameSettings.g_taa;
            TonemapTaa           = GameSettings.g_taa_tonemapped;
            TaaVelocity          = GameSettings.g_taa_velocity;
            TaaSharpness         = GameSettings.g_taa_sharpness;
            Fxaa                 = GameSettings.g_fxaa;

            SsrEnabled           = GameSettings.g_SSReflection;
            SsrStochastic        = GameSettings.g_SSReflectionTaa;
            SsrTemporalNoise     = GameSettings.g_SSReflectionNoise;
            SsrFireflyReduction  = GameSettings.g_SSReflection_FireflyReduction;
            SsrFireflyThreshold  = GameSettings.g_SSReflection_FireflyThreshold;
            SsrSamples           = GameSettings.g_SSReflections_Samples;
            SsrRefinementSamples = GameSettings.g_SSReflections_RefinementSamples;

            SsaoEnabled          = GameSettings.g_ssao_draw;
            SsaoBlur             = GameSettings.g_ssao_blur;
            SsaoSamples          = GameSettings.g_ssao_samples;
            SsaoRadius           = GameSettings.g_ssao_radius;
            SsaoStrength         = GameSettings.g_ssao_strength;

            BloomRadius1         = GameSettings.g_BloomRadius1;
            BloomStrength1       = GameSettings.g_BloomStrength1;
            BloomRadius2         = GameSettings.g_BloomRadius2;
            BloomStrength2       = GameSettings.g_BloomStrength2;
            BloomRadius3         = GameSettings.g_BloomRadius3;
            BloomStrength3       = GameSettings.g_BloomStrength3;
            BloomRadius4         = GameSettings.g_BloomRadius4;
            BloomStrength4       = GameSettings.g_BloomStrength4;
            BloomRadius5         = GameSettings.g_BloomRadius5;
            BloomStrength5       = GameSettings.g_BloomStrength5;

            FogEnabled           = GameSettings.g_FroxelsEnabled && GameSettings.g_FroxelFogEnabled;
            FogHistoryBlend      = GameSettings.g_FroxelHistoryBlend;

            ShadowAtlasSize      = GameSettings.g_shadowatlassize;
            ShadowUpdatesPerFrame = GameSettings.g_shadowupdatesperframe;
            ShadowFadeStart      = GameSettings.g_shadowfadestart;
            ShadowFadeEnd        = GameSettings.g_shadowfadeend;
            ShadowSizeCull       = GameSettings.g_shadowsizecull;
            ReflectionProbeSize  = GameSettings.g_reflectionprobesize;
            CubemapNormalizationMax = GameSettings.g_cubemapnormalizationmax;
            CubemapNormalizationRoughnessBegin = GameSettings.g_cubemapnormalizationroughnessbegin;
            SizeCullThreshold    = GameSettings.g_sizecullthreshold;
            OcclusionCulling     = GameSettings.g_occlusionculling;

            DebugView            = (int)GameSettings.g_rendermode;
            HighlightMeshes      = GameSettings.e_drawoutlines;
            DrawSdf              = GameSettings.sdf_drawdistance;
            DrawSdfVolume        = GameSettings.sdf_drawvolume;
        }
        finally { _suppress = false; }
        LoadScenePost();
    }

    // ----- Marshalled setters -----

    private void Push(System.Action assign)
    {
        if (_suppress || _bridge == null) return;
        _bridge.EnqueueGameThreadAction(assign);
    }

    private void PushPost(Action<PostProcessSettings> change)
    {
        if (_suppress || _bridge == null) return;
        _bridge.EnqueueMutatePost(change);
    }

    // Scene: tonemapping & grading
    partial void OnTonemapCurveChanged(int value)
    {
        if (value >= 0 && value < TonemapCurves.Length) PushPost(p => p.Tonemap = (Engine.Logic.TonemapCurve)value);
    }
    partial void OnWhitePointChanged(double value)         => PushPost(p => p.WhitePoint = (float)value);
    partial void OnFilmicShoulderChanged(double value)     => PushPost(p => p.FilmicShoulder = (float)value);
    partial void OnFilmicLinearStrengthChanged(double value) => PushPost(p => p.FilmicLinearStrength = (float)value);
    partial void OnFilmicLinearAngleChanged(double value)  => PushPost(p => p.FilmicLinearAngle = (float)value);
    partial void OnFilmicToeStrengthChanged(double value)  => PushPost(p => p.FilmicToeStrength = (float)value);
    partial void OnFilmicToeNumeratorChanged(double value) => PushPost(p => p.FilmicToeNumerator = (float)value);
    partial void OnFilmicToeDenominatorChanged(double value) => PushPost(p => p.FilmicToeDenominator = (float)value);
    partial void OnFilmicWhitePointChanged(double value)   => PushPost(p => p.FilmicWhitePoint = (float)value);
    partial void OnExposureChanged(double value)           => PushPost(p => p.Exposure = (float)value);
    partial void OnSCurveStrengthChanged(double value)     => PushPost(p => p.SCurveStrength = (float)value);
    partial void OnExactSrgbChanged(bool value)            => PushPost(p => p.ExactSrgb = value);
    partial void OnColorGradingChanged(bool value)         => PushPost(p => p.ColorGrading = value);
    partial void OnLutPathChanged(string value)            => PushPost(p => p.LutPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    partial void OnSaturationChanged(double value)         => PushPost(p => p.Saturation = (float)value);
    partial void OnContrastChanged(double value)           => PushPost(p => p.Contrast = (float)value);
    partial void OnColorFilterChanged(Color value)
    {
        var c = BridgeReconciler.ToXnaColor(value);
        PushPost(p => p.ColorFilter = c);
    }
    partial void OnVignetteChanged(double value)           => PushPost(p => p.Vignette = (float)value);
    partial void OnChromaticAberrationChanged(double value)=> PushPost(p => p.ChromaticAberration = (float)value);
    partial void OnRetinaBurnChanged(double value)         => PushPost(p => p.RetinaBurn = (float)value);
    partial void OnFogDensityScaleChanged(double value)    => PushPost(p => p.FogDensityScale = (float)value);

    // Scene: eye adaptation
    partial void OnAutoExposureChanged(bool value)         => PushPost(p => p.AutoExposure = value);
    partial void OnMeteringChanged(int value)
    {
        if (value >= 0 && value < MeteringModes.Length) PushPost(p => p.Metering = (ExposureMetering)value);
    }
    partial void OnAutoExposureKeyChanged(double value)    => PushPost(p => p.AutoExposureKey = (float)value);
    partial void OnBrightPixelPercentChanged(double value) => PushPost(p => p.BrightPixelPercent = (float)value);
    partial void OnBrightPixelTargetChanged(double value)  => PushPost(p => p.BrightPixelTarget = (float)value);
    partial void OnMinAverageLuminanceChanged(double value)=> PushPost(p => p.MinAverageLuminance = (float)value);
    partial void OnAutoExposureMinChanged(double value)    => PushPost(p => p.AutoExposureMin = (float)value);
    partial void OnAutoExposureMaxChanged(double value)    => PushPost(p => p.AutoExposureMax = (float)value);
    partial void OnAutoExposureSpeedDarkToLightChanged(double value) => PushPost(p => p.AdaptationSpeedUp = (float)value);
    partial void OnAutoExposureSpeedLightToDarkChanged(double value) => PushPost(p => p.AdaptationSpeedDown = (float)value);
    partial void OnAutoExposureCenterWeightChanged(double value)     => PushPost(p => p.CenterWeight = (float)value);

    // Scene: bloom
    partial void OnBloomEnabledChanged(bool value)         => PushPost(p => p.Bloom = value);
    partial void OnBloomShapeChanged(int value)
    {
        if (value >= 0 && value < BloomShapes.Length) PushPost(p => p.BloomShape = (BloomThresholdShape)value);
    }
    partial void OnBloomStartChanged(double value)         => PushPost(p => p.BloomStart = (float)value);
    partial void OnBloomKneeChanged(double value)          => PushPost(p => p.BloomKnee = (float)value);
    partial void OnBloomScaleChanged(double value)         => PushPost(p => p.BloomScale = (float)value);

    // Project
    partial void OnTaaChanged(bool value)                  => Push(() => GameSettings.g_taa = value);
    partial void OnTonemapTaaChanged(bool value)           => Push(() => GameSettings.g_taa_tonemapped = value);
    partial void OnTaaVelocityChanged(bool value)          => Push(() => GameSettings.g_taa_velocity = value);
    partial void OnTaaSharpnessChanged(double value)       => Push(() => GameSettings.g_taa_sharpness = (float)value);
    partial void OnFxaaChanged(bool value)                 => Push(() => GameSettings.g_fxaa = value);

    partial void OnSsrEnabledChanged(bool value)           => Push(() => GameSettings.g_SSReflection = value);
    partial void OnSsrStochasticChanged(bool value)        => Push(() => GameSettings.g_SSReflectionTaa = value);
    partial void OnSsrTemporalNoiseChanged(bool value)     => Push(() => GameSettings.g_SSReflectionNoise = value);
    partial void OnSsrFireflyReductionChanged(bool value)  => Push(() => GameSettings.g_SSReflection_FireflyReduction = value);
    partial void OnSsrFireflyThresholdChanged(double value)=> Push(() => GameSettings.g_SSReflection_FireflyThreshold = (float)value);
    partial void OnSsrSamplesChanged(int value)            => Push(() => GameSettings.g_SSReflections_Samples = value);
    partial void OnSsrRefinementSamplesChanged(int value)  => Push(() => GameSettings.g_SSReflections_RefinementSamples = value);

    partial void OnSsaoEnabledChanged(bool value)          => Push(() => GameSettings.g_ssao_draw = value);
    partial void OnSsaoBlurChanged(bool value)             => Push(() => GameSettings.g_ssao_blur = value);
    partial void OnSsaoSamplesChanged(int value)           => Push(() => GameSettings.g_ssao_samples = value);
    partial void OnSsaoRadiusChanged(double value)         => Push(() => GameSettings.g_ssao_radius = (float)value);
    partial void OnSsaoStrengthChanged(double value)       => Push(() => GameSettings.g_ssao_strength = (float)value);

    partial void OnBloomRadius1Changed(double value)       => Push(() => GameSettings.g_BloomRadius1 = (float)value);
    partial void OnBloomStrength1Changed(double value)     => Push(() => GameSettings.g_BloomStrength1 = (float)value);
    partial void OnBloomRadius2Changed(double value)       => Push(() => GameSettings.g_BloomRadius2 = (float)value);
    partial void OnBloomStrength2Changed(double value)     => Push(() => GameSettings.g_BloomStrength2 = (float)value);
    partial void OnBloomRadius3Changed(double value)       => Push(() => GameSettings.g_BloomRadius3 = (float)value);
    partial void OnBloomStrength3Changed(double value)     => Push(() => GameSettings.g_BloomStrength3 = (float)value);
    partial void OnBloomRadius4Changed(double value)       => Push(() => GameSettings.g_BloomRadius4 = (float)value);
    partial void OnBloomStrength4Changed(double value)     => Push(() => GameSettings.g_BloomStrength4 = (float)value);
    partial void OnBloomRadius5Changed(double value)       => Push(() => GameSettings.g_BloomRadius5 = (float)value);
    partial void OnBloomStrength5Changed(double value)     => Push(() => GameSettings.g_BloomStrength5 = (float)value);

    // g_FroxelsEnabled gates the froxel passes, g_FroxelFogEnabled the compose; toggle both so
    // turning fog off also stops paying for it.
    partial void OnFogEnabledChanged(bool value)           => Push(() => { GameSettings.g_FroxelsEnabled = value; GameSettings.g_FroxelFogEnabled = value; });
    partial void OnFogHistoryBlendChanged(double value)    => Push(() => GameSettings.g_FroxelHistoryBlend = (float)value);

    partial void OnShadowAtlasSizeChanged(int value)       => Push(() => GameSettings.g_shadowatlassize = value);
    partial void OnShadowUpdatesPerFrameChanged(int value) => Push(() => GameSettings.g_shadowupdatesperframe = value);
    partial void OnShadowFadeStartChanged(double value)    => Push(() => GameSettings.g_shadowfadestart = (float)value);
    partial void OnShadowFadeEndChanged(double value)      => Push(() => GameSettings.g_shadowfadeend = (float)value);
    partial void OnShadowSizeCullChanged(double value)     => Push(() => GameSettings.g_shadowsizecull = (float)value);
    partial void OnReflectionProbeSizeChanged(int value)   => Push(() => GameSettings.g_reflectionprobesize = value);
    partial void OnCubemapNormalizationMaxChanged(double value) => Push(() => GameSettings.g_cubemapnormalizationmax = (float)value);
    partial void OnCubemapNormalizationRoughnessBeginChanged(double value) => Push(() => GameSettings.g_cubemapnormalizationroughnessbegin = (float)value);
    partial void OnSizeCullThresholdChanged(double value)  => Push(() => GameSettings.g_sizecullthreshold = (float)value);
    partial void OnOcclusionCullingChanged(bool value)     => Push(() => GameSettings.g_occlusionculling = value);

    partial void OnDebugViewChanged(int value)
    {
        if (value >= 0 && value < DebugViews.Length) Push(() => GameSettings.g_rendermode = (Renderer.RenderModes)value);
    }
    partial void OnHighlightMeshesChanged(bool value)      => Push(() => GameSettings.e_drawoutlines = value);
    partial void OnDrawSdfChanged(bool value)              => Push(() => GameSettings.sdf_drawdistance = value);
    partial void OnDrawSdfVolumeChanged(bool value)        => Push(() => GameSettings.sdf_drawvolume = value);
}
