using System;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Renderer.Lighting;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Anvil.Models;

/// <summary>
/// Inspector "Lighting" tab: the active scene's baked lighting (probe volume) settings and the
/// bake controls. Settings are per scene, so they are re-read whenever the scene changes; edits
/// are marshalled to the game thread through <see cref="IEditorBridge.EnqueueMutateLighting"/>.
/// </summary>
public partial class LightingViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private bool _suppress;

    /// <summary>Bake outcomes for the console (level, message).</summary>
    public event Action<ConsoleLevel, string>? LogRequested;

    // --------- Runtime ---------
    [ObservableProperty] private bool _probeVolumeEnabled;
    [ObservableProperty] private double _intensity;
    [ObservableProperty] private bool _showProbes;

    // --------- Volume ---------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualBounds))]
    private bool _autoBounds;
    [ObservableProperty] private double _boundsPadding;
    [ObservableProperty] private double _boundsMinX;
    [ObservableProperty] private double _boundsMinY;
    [ObservableProperty] private double _boundsMinZ;
    [ObservableProperty] private double _boundsMaxX;
    [ObservableProperty] private double _boundsMaxY;
    [ObservableProperty] private double _boundsMaxZ;
    [ObservableProperty] private double _probeSpacing;
    [ObservableProperty] private int _maxProbesPerAxis;

    public bool IsManualBounds => !AutoBounds;

    // --------- Quality ---------
    [ObservableProperty] private int _samplesPerProbe;
    [ObservableProperty] private int _bounces;
    [ObservableProperty] private double _validityThreshold;

    // --------- Sky ---------
    [ObservableProperty] private Color _skyColor;
    [ObservableProperty] private double _skyIntensity;

    // --------- Bake status ---------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(BakeCommand), nameof(CancelBakeCommand), nameof(ClearBakeCommand))]
    private bool _isBaking;
    [ObservableProperty] private double _bakeProgress;
    [ObservableProperty] private string _bakeStage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBakedData))]
    [NotifyCanExecuteChangedFor(nameof(ClearBakeCommand))]
    private string? _bakedSummary;

    public bool IsIdle => !IsBaking;
    public bool HasBakedData => !string.IsNullOrEmpty(BakedSummary);

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        bridge.LightingStatusChanged += s => Dispatcher.UIThread.Post(() => ApplyStatus(s, fromEvent: true));
        // Settings are per scene: re-read after New/Open
        bridge.SceneChanged += () => Dispatcher.UIThread.Post(Reload);
        Reload();
        ApplyStatus(bridge.LightingStatus, fromEvent: false);
    }

    private void Reload()
    {
        if (_bridge == null) return;
        LightingSettings s = _bridge.GetLightingSettings();
        _suppress = true;
        try
        {
            ProbeVolumeEnabled = s.ProbeVolumeEnabled;
            Intensity = s.Intensity;
            ShowProbes = s.ShowProbes;

            AutoBounds = s.AutoBounds;
            BoundsPadding = s.BoundsPadding;
            BoundsMinX = s.BoundsMin.X; BoundsMinY = s.BoundsMin.Y; BoundsMinZ = s.BoundsMin.Z;
            BoundsMaxX = s.BoundsMax.X; BoundsMaxY = s.BoundsMax.Y; BoundsMaxZ = s.BoundsMax.Z;
            ProbeSpacing = s.ProbeSpacing;
            MaxProbesPerAxis = s.MaxProbesPerAxis;

            SamplesPerProbe = s.SamplesPerProbe;
            Bounces = s.Bounces;
            ValidityThreshold = s.ValidityThreshold;

            SkyColor = Color.FromRgb(ToByte(s.SkyColor.X), ToByte(s.SkyColor.Y), ToByte(s.SkyColor.Z));
            SkyIntensity = s.SkyIntensity;
        }
        finally { _suppress = false; }

        BakedSummary = _bridge.GetBakedLightingSummary();
    }

    private void ApplyStatus(LightingBakeStatus status, bool fromEvent)
    {
        IsBaking = status.IsBaking;
        BakeProgress = status.Progress * 100;
        BakeStage = status.Stage;
        if (!string.IsNullOrEmpty(status.Message)) StatusMessage = status.Message;
        BakedSummary = _bridge?.GetBakedLightingSummary();

        if (!fromEvent || string.IsNullOrEmpty(status.Message)) return;
        ConsoleLevel level = status.State switch
        {
            LightingBakeState.Failed => ConsoleLevel.Error,
            LightingBakeState.Cancelled => ConsoleLevel.Warn,
            _ => ConsoleLevel.Log,
        };
        LogRequested?.Invoke(level, status.Message);
    }

    // --------- Commands ---------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Bake()
    {
        if (_bridge == null) return;
        StatusMessage = string.Empty;
        _bridge.EnqueueBakeLighting();
    }

    [RelayCommand(CanExecute = nameof(IsBaking))]
    private void CancelBake() => _bridge?.CancelLightingBake();

    private bool CanClearBake() => IsIdle && HasBakedData;

    [RelayCommand(CanExecute = nameof(CanClearBake))]
    private void ClearBake() => _bridge?.EnqueueClearBakedLighting();

    // --------- Marshalled setters ---------

    private void Push(Action<LightingSettings> mutate)
    {
        if (_suppress || _bridge == null) return;
        _bridge.EnqueueMutateLighting(mutate);
    }

    partial void OnProbeVolumeEnabledChanged(bool value) => Push(s => s.ProbeVolumeEnabled = value);
    partial void OnIntensityChanged(double value)         => Push(s => s.Intensity = (float)value);
    partial void OnShowProbesChanged(bool value)          => Push(s => s.ShowProbes = value);

    partial void OnAutoBoundsChanged(bool value)          => Push(s => s.AutoBounds = value);
    partial void OnBoundsPaddingChanged(double value)     => Push(s => s.BoundsPadding = (float)value);
    partial void OnBoundsMinXChanged(double value)        => PushBounds();
    partial void OnBoundsMinYChanged(double value)        => PushBounds();
    partial void OnBoundsMinZChanged(double value)        => PushBounds();
    partial void OnBoundsMaxXChanged(double value)        => PushBounds();
    partial void OnBoundsMaxYChanged(double value)        => PushBounds();
    partial void OnBoundsMaxZChanged(double value)        => PushBounds();
    partial void OnProbeSpacingChanged(double value)      => Push(s => s.ProbeSpacing = (float)value);
    partial void OnMaxProbesPerAxisChanged(int value)     => Push(s => s.MaxProbesPerAxis = value);

    partial void OnSamplesPerProbeChanged(int value)      => Push(s => s.SamplesPerProbe = value);
    partial void OnBouncesChanged(int value)              => Push(s => s.Bounces = value);
    partial void OnValidityThresholdChanged(double value) => Push(s => s.ValidityThreshold = (float)value);

    partial void OnSkyColorChanged(Color value)
    {
        var c = new XnaVector3(value.R / 255f, value.G / 255f, value.B / 255f);
        Push(s => s.SkyColor = c);
    }
    partial void OnSkyIntensityChanged(double value)      => Push(s => s.SkyIntensity = (float)value);

    private void PushBounds()
    {
        var min = new XnaVector3((float)BoundsMinX, (float)BoundsMinY, (float)BoundsMinZ);
        var max = new XnaVector3((float)BoundsMaxX, (float)BoundsMaxY, (float)BoundsMaxZ);
        Push(s => { s.BoundsMin = min; s.BoundsMax = max; });
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);
}
