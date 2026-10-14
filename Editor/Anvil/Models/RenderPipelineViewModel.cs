using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Anvil.Services;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Renderer.Features;

namespace Anvil.Models;

/// <summary>
/// Inspector "Render Pipeline" view (viewport toolbar > Pipeline): the engine's passes in draw order with the
/// project's render features at their injection points, and the selected feature's settings. Features are
/// project-wide (Content/System/RenderFeatures.json); every edit goes through the bridge and is saved by the engine.
/// </summary>
public partial class RenderPipelineViewModel : ObservableObject
{
    public static readonly string[] InjectionPointNames = { "After Opaques", "After Transparents", "After Bloom", "After Post Processing" };
    public static readonly string[] BlendNames = { "Replace", "Alpha Blend", "Additive", "Multiply" };

    private IEditorBridge? _bridge;
    private Action<string>? _deleteFile;
    private bool _suppress;
    private Guid? _pendingSelection;
    private readonly Dictionary<Guid, RenderFeatureStatus> _lastStatus = new();

    /// <summary>Compile results for the console (level, message).</summary>
    public event Action<ConsoleLevel, string>? LogRequested;

    public ObservableCollection<RenderPipelineRowViewModel> Rows { get; } = new();
    public IReadOnlyList<string> InjectionPoints => InjectionPointNames;
    public IReadOnlyList<string> BlendModes => BlendNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFeature), nameof(HasSelectedStage), nameof(SelectedStageText))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(EditShaderCommand),
        nameof(RecompileCommand), nameof(DeleteCommand))]
    private RenderPipelineRowViewModel? _selectedRow;

    /// <summary>Where + New puts the feature.</summary>
    [ObservableProperty] private int _newFeatureEventIndex = (int)RenderFeatureEvent.AfterTransparents;
    [ObservableProperty] private string _summary = string.Empty;

    // --------- Selected feature ---------
    [ObservableProperty] private string _featureName = string.Empty;
    [ObservableProperty] private bool _featureEnabled;
    [ObservableProperty] private int _eventIndex;
    [ObservableProperty] private int _blendIndex;
    [ObservableProperty] private bool _showInEditMode;
    [ObservableProperty] private double _intensity;
    [ObservableProperty] private Color _featureColor = Colors.White;
    [ObservableProperty] private double _param0;
    [ObservableProperty] private double _param1;
    [ObservableProperty] private double _param2;
    [ObservableProperty] private double _param3;
    [ObservableProperty] private string _technique = string.Empty;
    [ObservableProperty] private string _shaderPath = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    public bool HasSelectedFeature => SelectedRow?.IsFeature == true;
    public bool HasSelectedStage => SelectedRow != null && !SelectedRow.IsFeature;
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public string SelectedStageText => SelectedRow == null || SelectedRow.IsFeature ? string.Empty
        : SelectedRow.IsInjectionPoint
            ? $"{SelectedRow.Name}: {SelectedRow.Detail}. Render features placed here run in the listed order. Add one with + New, or move one here with Up / Down."
            : $"{SelectedRow.Name}: {SelectedRow.Detail}. A built-in pass of the engine ({(SelectedRow.Active ? "on" : "switched off in the render settings")}).";

    private Guid? SelectedId => SelectedRow?.FeatureId;

    public void AttachBridge(IEditorBridge bridge, Action<string> deleteFile)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        _deleteFile = deleteFile;
        bridge.RenderPipelineChanged += () => Dispatcher.UIThread.Post(Reload);
        Reload();
    }

    /// <summary>Re-reads the pipeline (the built-in passes' on/off state comes from the render settings).</summary>
    public void Refresh() => _bridge?.EnqueueRefreshRenderPipeline();

    private void Reload()
    {
        if (_bridge == null) return;
        IReadOnlyList<RenderPipelineLayer> layers = _bridge.GetRenderPipeline();

        // Keep the row objects (and so the ListBox selection) when only values changed.
        bool sameShape = Rows.Count == layers.Count && Rows.Zip(layers).All(p => p.First.Key == RenderPipelineRowViewModel.KeyOf(p.Second));
        if (sameShape)
        {
            for (int i = 0; i < layers.Count; i++) Rows[i].Update(layers[i]);
        }
        else
        {
            string? selectedKey = SelectedRow?.Key;
            Rows.Clear();
            foreach (RenderPipelineLayer layer in layers)
                Rows.Add(new RenderPipelineRowViewModel(layer, SetFeatureEnabled));
            string? wanted = _pendingSelection is Guid id ? "F:" + id : selectedKey;
            SelectedRow = Rows.FirstOrDefault(r => r.Key == wanted);
        }
        if (_pendingSelection is Guid pending && Rows.FirstOrDefault(r => r.FeatureId == pending) is { } created)
        {
            SelectedRow = created;
            _pendingSelection = null;
        }

        int features = Rows.Count(r => r.IsFeature);
        Summary = features == 0 ? "No render features yet." : features == 1 ? "1 render feature" : $"{features} render features";

        ReportStatusChanges(layers);
        ApplyServerFields();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // Compile results go to the console once per change.
    private void ReportStatusChanges(IReadOnlyList<RenderPipelineLayer> layers)
    {
        foreach (RenderPipelineLayer layer in layers)
        {
            RenderFeature? feature = layer.Feature;
            if (feature == null) continue;
            bool known = _lastStatus.TryGetValue(feature.Id, out RenderFeatureStatus previous);
            _lastStatus[feature.Id] = feature.Status;
            if (known && previous == feature.Status) continue;
            if (feature.Status == RenderFeatureStatus.Error)
                LogRequested?.Invoke(ConsoleLevel.Error, $"Render feature '{feature.Name}': {feature.Error}");
            else if (feature.Status == RenderFeatureStatus.Ready && previous == RenderFeatureStatus.Compiling)
                LogRequested?.Invoke(ConsoleLevel.Log, $"Render feature '{feature.Name}' compiled ({feature.Shader}).");
        }
    }

    partial void OnSelectedRowChanged(RenderPipelineRowViewModel? value)
    {
        RenderFeature? feature = value?.Feature;
        if (feature == null) return;
        _suppress = true;
        try
        {
            FeatureName = feature.Name;
            FeatureEnabled = feature.Enabled;
            BlendIndex = (int)feature.Blend;
            ShowInEditMode = feature.ShowInEditMode;
            Intensity = feature.Intensity;
            float[] c = feature.Color;
            FeatureColor = Color.FromArgb(ToByte(c[3]), ToByte(c[0]), ToByte(c[1]), ToByte(c[2]));
            Param0 = feature.Params[0];
            Param1 = feature.Params[1];
            Param2 = feature.Params[2];
            Param3 = feature.Params[3];
            Technique = feature.Technique ?? string.Empty;
        }
        finally { _suppress = false; }
        ApplyServerFields();
    }

    // Values the engine changes on its own (compile status, Move): refreshed on every pipeline change.
    // Fields the user types are only read on selection, so a late echo can't undo a newer edit.
    private void ApplyServerFields()
    {
        RenderFeature? feature = SelectedRow?.Feature;
        if (feature == null) return;
        _suppress = true;
        try
        {
            EventIndex = (int)feature.Event;
            FeatureEnabled = feature.Enabled;
            ShaderPath = feature.Shader ?? "(none)";
            StatusText = feature.Status switch
            {
                RenderFeatureStatus.Ready => "Compiled",
                RenderFeatureStatus.Compiling => "Compiling...",
                RenderFeatureStatus.Error => "Error",
                _ => "Not compiled",
            };
            ErrorText = feature.Error;
        }
        finally { _suppress = false; }
    }

    private void Push(Action<RenderFeature> mutate)
    {
        if (_suppress || _bridge == null || SelectedId is not Guid id) return;
        _bridge.EnqueueMutateRenderFeature(id, mutate);
    }

    private void SetFeatureEnabled(Guid id, bool enabled) =>
        _bridge?.EnqueueMutateRenderFeature(id, f => f.Enabled = enabled);

    partial void OnFeatureNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string name = value.Trim();
        Push(f => f.Name = name);
    }

    partial void OnFeatureEnabledChanged(bool value) => Push(f => f.Enabled = value);

    partial void OnEventIndexChanged(int value)
    {
        // ComboBox reports -1 while its items are swapped.
        if (value < 0 || value >= InjectionPointNames.Length) return;
        Push(f => f.Event = (RenderFeatureEvent)value);
    }

    partial void OnBlendIndexChanged(int value)
    {
        if (value < 0 || value >= BlendNames.Length) return;
        Push(f => f.Blend = (RenderFeatureBlend)value);
    }

    partial void OnShowInEditModeChanged(bool value) => Push(f => f.ShowInEditMode = value);
    partial void OnIntensityChanged(double value) => Push(f => f.Intensity = (float)value);

    partial void OnFeatureColorChanged(Color value)
    {
        float[] color = { value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f };
        Push(f => f.Color = color);
    }

    partial void OnParam0Changed(double value) => PushParam(0, value);
    partial void OnParam1Changed(double value) => PushParam(1, value);
    partial void OnParam2Changed(double value) => PushParam(2, value);
    partial void OnParam3Changed(double value) => PushParam(3, value);

    private void PushParam(int index, double value) => Push(f =>
    {
        float[] p = (float[])f.Params.Clone();
        p[index] = (float)value;
        f.Params = p;
    });

    partial void OnTechniqueChanged(string value)
    {
        string technique = value?.Trim() ?? string.Empty;
        Push(f => f.Technique = technique);
    }

    // --------- Commands ---------

    [RelayCommand]
    private void NewFeature()
    {
        if (_bridge == null) return;
        int index = Math.Clamp(NewFeatureEventIndex, 0, InjectionPointNames.Length - 1);
        _bridge.EnqueueCreateRenderFeature("New Render Feature", (RenderFeatureEvent)index, id =>
            Dispatcher.UIThread.Post(() =>
            {
                if (id == Guid.Empty)
                {
                    LogRequested?.Invoke(ConsoleLevel.Error, "Couldn't create the render feature (see anvil-bridge.log).");
                    return;
                }
                _pendingSelection = id;
                if (Rows.FirstOrDefault(r => r.FeatureId == id) is { } row)
                {
                    SelectedRow = row;
                    _pendingSelection = null;
                }
                LogRequested?.Invoke(ConsoleLevel.Log, "Render feature created; its shader is compiling. Edit Shader opens it.");
            }));
    }

    private bool CanEditFeature() => SelectedRow?.IsFeature == true;

    private bool CanMoveUp() => SelectedRow?.IsFeature == true && Rows.IndexOf(SelectedRow) > FirstMovableIndex();
    private bool CanMoveDown() => SelectedRow?.IsFeature == true && Rows.IndexOf(SelectedRow) < Rows.Count - 1 && !IsLastFeatureOfLastPoint();

    // Rows before the first injection point can't take a feature.
    private int FirstMovableIndex()
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].IsInjectionPoint) return i + 1;
        return Rows.Count;
    }

    private bool IsLastFeatureOfLastPoint()
    {
        if (SelectedRow?.Feature is not { } feature || feature.Event != RenderFeatureEvent.AfterPostProcessing) return false;
        int index = Rows.IndexOf(SelectedRow);
        return index + 1 >= Rows.Count || !Rows[index + 1].IsFeature;
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedId is Guid id) _bridge?.EnqueueMoveRenderFeature(id, -1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedId is Guid id) _bridge?.EnqueueMoveRenderFeature(id, 1);
    }

    [RelayCommand(CanExecute = nameof(CanEditFeature))]
    private void EditShader()
    {
        if (_bridge == null || SelectedRow?.Feature?.Shader is not { } shader) return;
        string path = Path.Combine(_bridge.ContentSourceRoot, shader.Replace('/', Path.DirectorySeparatorChar));
        if (!ExternalTextEditor.TryOpen(path, out string error))
            LogRequested?.Invoke(ConsoleLevel.Error, error);
    }

    [RelayCommand(CanExecute = nameof(CanEditFeature))]
    private void Recompile()
    {
        if (SelectedId is Guid id) _bridge?.EnqueueRecompileRenderFeature(id);
    }

    [RelayCommand(CanExecute = nameof(CanEditFeature))]
    private void Delete()
    {
        if (SelectedId is not Guid id || _bridge == null) return;
        string name = SelectedRow?.Name ?? "render feature";
        _bridge.EnqueueDeleteRenderFeature(id, deleteShader: true, _deleteFile!);
        LogRequested?.Invoke(ConsoleLevel.Log, $"Deleted render feature '{name}' (its shader went to the Recycle Bin).");
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}

/// <summary>One row of the Render Pipeline list: a built-in pass, an injection point or a render feature.</summary>
public partial class RenderPipelineRowViewModel : ObservableObject
{
    private readonly Action<Guid, bool> _setEnabled;
    private bool _updating;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity), nameof(ActiveBrush))]
    private bool _active;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush), nameof(StatusTip))]
    private RenderFeatureStatus _status;

    public RenderPipelineLayerKind Kind { get; }
    public string Key { get; }
    public Guid? FeatureId { get; }
    public RenderFeature? Feature { get; private set; }

    public bool IsStage => Kind == RenderPipelineLayerKind.Stage;
    public bool IsInjectionPoint => Kind == RenderPipelineLayerKind.InjectionPoint;
    public bool IsFeature => Kind == RenderPipelineLayerKind.Feature;
    public double Opacity => Active ? 1.0 : 0.45;
    public string InjectionLabel => Name.ToUpperInvariant();

    public IBrush StatusBrush => Status switch
    {
        RenderFeatureStatus.Ready => StageOnBrush,
        RenderFeatureStatus.Compiling => CompilingBrush,
        RenderFeatureStatus.Error => ErrorBrush,
        _ => StageOffBrush,
    };

    public string StatusTip => Status switch
    {
        RenderFeatureStatus.Ready => "Compiled",
        RenderFeatureStatus.Compiling => "Compiling...",
        RenderFeatureStatus.Error => "Error: " + Feature?.Error,
        _ => "Not compiled",
    };

    /// <summary>Built-in passes: on (green) or switched off in the render settings (grey).</summary>
    public IBrush ActiveBrush => Active ? StageOnBrush : StageOffBrush;

    private static readonly IBrush StageOnBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0xB8, 0x78));
    private static readonly IBrush StageOffBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
    private static readonly IBrush CompilingBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x4A));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x5A));

    public RenderPipelineRowViewModel(RenderPipelineLayer layer, Action<Guid, bool> setEnabled)
    {
        _setEnabled = setEnabled;
        Kind = layer.Kind;
        Key = KeyOf(layer);
        FeatureId = layer.Feature?.Id;
        Update(layer);
    }

    public static string KeyOf(RenderPipelineLayer layer) => layer.Kind switch
    {
        RenderPipelineLayerKind.Feature => "F:" + layer.Feature?.Id,
        RenderPipelineLayerKind.InjectionPoint => "P:" + layer.Event,
        _ => "S:" + layer.Name,
    };

    public void Update(RenderPipelineLayer layer)
    {
        _updating = true;
        try
        {
            Feature = layer.Feature;
            Name = layer.Name ?? string.Empty;
            Detail = layer.Detail ?? string.Empty;
            Active = layer.Active;
            Status = layer.Feature?.Status ?? RenderFeatureStatus.NotCompiled;
            OnPropertyChanged(nameof(Feature));
            OnPropertyChanged(nameof(InjectionLabel));
            OnPropertyChanged(nameof(StatusTip));
        }
        finally { _updating = false; }
    }

    // The row's checkbox (features only) toggles Enabled.
    partial void OnActiveChanged(bool value)
    {
        if (_updating || FeatureId is not Guid id) return;
        _setEnabled(id, value);
    }
}
