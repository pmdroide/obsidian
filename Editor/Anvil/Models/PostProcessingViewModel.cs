using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Editor;
using Engine.Recources;

namespace Anvil.Models;

/// <summary>
/// Inspector-side view model for the engine's global post-processing /
/// rendering toggles. Mirrors the controls that used to live in HelperSuite's
/// right-side options panel. Setters marshal to the game thread through the
/// bridge because most of these touch shader parameters
/// (<see cref="Shaders"/>) that aren't safe to write from the UI thread.
/// </summary>
public partial class PostProcessingViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private bool _suppress;

    // --------- Post Processing ---------
    [ObservableProperty] private bool _taa;
    [ObservableProperty] private bool _tonemapTaa;
    [ObservableProperty] private double _whitePoint;
    [ObservableProperty] private double _exposure;
    [ObservableProperty] private double _sCurveStrength;
    [ObservableProperty] private double _chromaticAberration;
    [ObservableProperty] private bool _colorGrading;

    // --------- Screen-Space Reflections ---------
    [ObservableProperty] private bool _ssrEnabled;
    [ObservableProperty] private bool _ssrStochastic;
    [ObservableProperty] private bool _ssrTemporalNoise;
    [ObservableProperty] private bool _ssrFireflyReduction;
    [ObservableProperty] private double _ssrFireflyThreshold;
    [ObservableProperty] private int _ssrSamples;
    [ObservableProperty] private int _ssrRefinementSamples;

    // --------- Ambient Occlusion ---------
    [ObservableProperty] private bool _ssaoEnabled;
    [ObservableProperty] private bool _ssaoBlur;
    [ObservableProperty] private int _ssaoSamples;
    [ObservableProperty] private double _ssaoRadius;
    [ObservableProperty] private double _ssaoStrength;

    // --------- Bloom ---------
    [ObservableProperty] private bool _bloomEnabled;
    [ObservableProperty] private double _bloomThreshold;
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

    // --------- General view options ---------
    [ObservableProperty] private bool _highlightMeshes;
    [ObservableProperty] private bool _drawSdf;
    [ObservableProperty] private bool _drawSdfVolume;

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        LoadFromEngine();
    }

    /// <summary>
    /// One-shot copy of the engine-side defaults into the VM. Called once on
    /// attach; reading static fields from the UI thread is safe (no shader
    /// touches), only writes need to be marshalled.
    /// </summary>
    private void LoadFromEngine()
    {
        _suppress = true;
        try
        {
            Taa                  = GameSettings.g_taa;
            TonemapTaa           = GameSettings.g_taa_tonemapped;
            WhitePoint           = GameSettings.WhitePoint;
            Exposure             = GameSettings.Exposure;
            SCurveStrength       = GameSettings.SCurveStrength;
            ChromaticAberration  = GameSettings.ChromaticAbberationStrength;
            ColorGrading         = GameSettings.g_ColorGrading;

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

            BloomEnabled         = GameSettings.g_BloomEnable;
            BloomThreshold       = GameSettings.g_BloomThreshold;
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

            HighlightMeshes      = GameSettings.e_drawoutlines;
            DrawSdf              = GameSettings.sdf_drawdistance;
            DrawSdfVolume        = GameSettings.sdf_drawvolume;
        }
        finally { _suppress = false; }
    }

    // ----- Marshalled setters: every On*Changed pushes to the game thread -----
    // Pattern: capture value in a local, enqueue the assignment. We intentionally
    // don't read GameSettings back to update the VM — the engine getter returns
    // exactly what we just wrote, so re-reading would just be churn.

    private void Push(System.Action assign)
    {
        if (_suppress || _bridge == null) return;
        _bridge.EnqueueGameThreadAction(assign);
    }

    partial void OnTaaChanged(bool value)                  => Push(() => GameSettings.g_taa = value);
    partial void OnTonemapTaaChanged(bool value)           => Push(() => GameSettings.g_taa_tonemapped = value);
    partial void OnWhitePointChanged(double value)         => Push(() => GameSettings.WhitePoint = (float)value);
    partial void OnExposureChanged(double value)           => Push(() => GameSettings.Exposure = (float)value);
    partial void OnSCurveStrengthChanged(double value)     => Push(() => GameSettings.SCurveStrength = (float)value);
    partial void OnChromaticAberrationChanged(double value)=> Push(() => GameSettings.ChromaticAbberationStrength = (float)value);
    partial void OnColorGradingChanged(bool value)         => Push(() => GameSettings.g_ColorGrading = value);

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

    partial void OnBloomEnabledChanged(bool value)         => Push(() => GameSettings.g_BloomEnable = value);
    partial void OnBloomThresholdChanged(double value)     => Push(() => GameSettings.g_BloomThreshold = (float)value);
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

    partial void OnHighlightMeshesChanged(bool value)      => Push(() => GameSettings.e_drawoutlines = value);
    partial void OnDrawSdfChanged(bool value)              => Push(() => GameSettings.sdf_drawdistance = value);
    partial void OnDrawSdfVolumeChanged(bool value)        => Push(() => GameSettings.sdf_drawvolume = value);
}
