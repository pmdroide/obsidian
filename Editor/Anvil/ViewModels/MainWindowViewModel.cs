using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Anvil.Models;
using Anvil.Services;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Anvil.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private string _projectName = "MyGame";
    [ObservableProperty] private string _sceneName = "SampleScene";
    [ObservableProperty] private string _platform = "PC, Mac & Linux";
    [ObservableProperty] private string _graphicsApi = "DX11";
    [ObservableProperty] private int _fps = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToolSelect), nameof(IsToolMove), nameof(IsToolRotate),
        nameof(IsToolScale), nameof(IsToolPan), nameof(IsToolSnap))]
    private string _activeTool = "Select";

    partial void OnActiveToolChanged(string value)
    {
        // Forward to the engine's gizmo state. Null = Select (no gizmo, just picking).
        if (_bridge == null) return;
        Engine.Logic.EditorLogic.GizmoModes? mode = value switch
        {
            "Move" => Engine.Logic.EditorLogic.GizmoModes.Translation,
            "Rotate" => Engine.Logic.EditorLogic.GizmoModes.Rotation,
            "Scale" => Engine.Logic.EditorLogic.GizmoModes.Scale,
            _ => null,
        };
        _bridge.RequestGizmoMode(mode);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayStateLabel))]
    private bool _isPlaying;

    [ObservableProperty] private string _viewMode = "Perspective";
    [ObservableProperty] private string _shadingMode = "Solid";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredConsole))]
    private string _consoleFilter = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredConsole))]
    private string _consoleSearch = string.Empty;

    [ObservableProperty] private int? _selectedLogId = 1;

    [ObservableProperty] private string _assetsSearch = string.Empty;

    public ObservableCollection<SceneObjectViewModel> SceneObjects { get; } = new();
    public ObservableCollection<AssetNode> AssetTree { get; } = new();
    public ObservableCollection<ConsoleEntry> ConsoleEntries { get; } = new();
    public ObservableCollection<string> AvailableModels { get; } = new();

    /// <summary>
    /// View model for the global post-processing / render settings panel.
    /// Shown inside the Inspector when <see cref="InspectorView"/> ==
    /// "PostProcessing". Replaces the legacy HelperSuite right-side panel.
    /// </summary>
    public PostProcessingViewModel PostProcessing { get; } = new();

    /// <summary>
    /// Inspector content switch: "Selection" (default) shows the selected
    /// object; "PostProcessing" shows <see cref="PostProcessing"/>. Driven by
    /// the segmented control in the inspector header and by
    /// Window &gt; Post Processing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorSelectionView),
        nameof(IsInspectorPostProcessingView))]
    private string _inspectorView = "Selection";

    public bool IsInspectorSelectionView => InspectorView == "Selection";
    public bool IsInspectorPostProcessingView => InspectorView == "PostProcessing";

    [RelayCommand]
    private void SetInspectorView(string view) => InspectorView = view;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedObject), nameof(SelectedObjectName),
        nameof(HasSelectedObject), nameof(HasNoSelectedObject),
        nameof(SelectedSceneObject))]
    private string? _selectedObjectId;

    private IEditorBridge? _bridge;
    private bool _selectionRoundTripGuard;
    private SceneObjectViewModel? _lastNotifiedSelected;

    /// <summary>
    /// True while the snapshot reconciler is touching <see cref="SceneObjects"/>.
    /// Avalonia's TreeView reacts to collection changes by re-computing its
    /// SelectedItem (sometimes resetting to null or the first child), which
    /// would otherwise call our setter and clobber the engine-side selection.
    /// Setter checks this flag and bails.
    /// </summary>
    public bool ReconcilerActive { get; set; }

    public SceneObjectViewModel? SelectedObject => FindObject(SceneObjects, SelectedObjectId);
    public string? SelectedObjectName => SelectedObject?.Name;
    public bool HasSelectedObject => SelectedObject != null;
    public bool HasNoSelectedObject => SelectedObject == null;

    /// <summary>
    /// Two-way alias used by the Hierarchy TreeView so its selection highlight
    /// tracks the engine-side selection. Setting it from a TreeView click
    /// routes through SelectSceneObjectCommand, which also notifies the engine.
    /// </summary>
    public SceneObjectViewModel? SelectedSceneObject
    {
        get => SelectedObject;
        set
        {
            // Reject anything that came from the framework recomputing its
            // selection during reconciliation — only the explicit user click
            // should reach this setter while ReconcilerActive == false.
            if (ReconcilerActive) return;
            // TreeView fires this with null during ItemsSource reconciliation
            // (when an item is replaced or removed). Ignore those — we don't
            // want a transient deselect to clobber the engine-side selection.
            if (value == null) return;
            if (value.Id == SelectedObjectId) return;
            SelectSceneObject(value.Id);
        }
    }

    public string PlayStateLabel => IsPlaying ? "Playing" : "Paused";

    public bool IsToolSelect => ActiveTool == "Select";
    public bool IsToolMove => ActiveTool == "Move";
    public bool IsToolRotate => ActiveTool == "Rotate";
    public bool IsToolScale => ActiveTool == "Scale";
    public bool IsToolPan => ActiveTool == "Pan";
    public bool IsToolSnap => ActiveTool == "Snap";

    public int ConsoleLogCount => ConsoleEntries.Count(e => e.Level == ConsoleLevel.Log);
    public int ConsoleWarnCount => ConsoleEntries.Count(e => e.Level == ConsoleLevel.Warn);
    public int ConsoleErrorCount => ConsoleEntries.Count(e => e.Level == ConsoleLevel.Error);

    public System.Collections.Generic.IEnumerable<ConsoleEntry> FilteredConsole
    {
        get
        {
            var search = ConsoleSearch?.Trim() ?? string.Empty;
            return ConsoleEntries.Where(e =>
            {
                var matchesFilter = ConsoleFilter switch
                {
                    "Log" => e.Level == ConsoleLevel.Log,
                    "Warn" => e.Level == ConsoleLevel.Warn,
                    "Error" => e.Level == ConsoleLevel.Error,
                    _ => true,
                };
                if (!matchesFilter) return false;
                if (search.Length == 0) return true;
                return e.Message.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || e.Source.Contains(search, System.StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    public MainWindowViewModel()
    {
        BuildAssets();
        BuildConsole();
    }

    // -------- Engine bridge wiring --------

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        bridge.SnapshotUpdated += OnBridgeSnapshot;
        bridge.SelectionChanged += OnBridgeSelectionChanged;
        bridge.SceneChanged += OnBridgeSceneChanged;
        bridge.ModeChanged += OnBridgeModeChanged;

        // Refresh model picker now (may already be populated after first frame).
        RefreshAvailableModels();

        // Hand the bridge to the post-processing VM so its setters can
        // marshal shader-parameter writes onto the game thread.
        PostProcessing.AttachBridge(bridge);

        // Push the current tool selection into the engine so the gizmo matches the UI
        // from the first frame (otherwise the engine boots in Translation mode regardless).
        OnActiveToolChanged(ActiveTool);
    }

    private void OnBridgeSceneChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_bridge == null) return;
            SceneName = _bridge.CurrentSceneName ?? "Untitled";
            // Hierarchy will be repopulated by the next snapshot tick; nothing else
            // to do here, since the reconciler removes stale VMs as their IDs leave
            // the snapshot.
        });
    }

    private void OnBridgeModeChanged(Engine.Logic.GameMode mode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Keep the UI's Play indicator in sync if engine-side code (script, hotkey,
            // future automation) changes the mode without going through TogglePlay.
            bool nowPlaying = mode == Engine.Logic.GameMode.Play;
            if (IsPlaying != nowPlaying) IsPlaying = nowPlaying;
        });
    }

    private void OnBridgeSnapshot(IReadOnlyList<EditorObjectSnapshot> snapshot)
    {
        // Marshal: bridge fires on the game thread; ObservableCollection only likes
        // mutations from the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (_bridge == null) return;
            ReconcilerActive = true;
            BridgeReconciler.SelectedEngineId = SelectedObject?.EngineId;
            try
            {
                BridgeReconciler.Apply(snapshot, SceneObjects, _bridge);
            }
            catch
            {
                // Don't let a reconciler error tear down the snapshot pipeline.
            }
            finally
            {
                ReconcilerActive = false;
            }
            if (AvailableModels.Count == 0) RefreshAvailableModels();

            // Only fire SelectedObject change when its identity actually flipped.
            // Re-firing every 3 frames re-evaluates the inspector's DataContext,
            // which loses focus on whichever NumericUpDown / ColorPicker is being edited.
            var nowSelected = SelectedObject;
            if (!ReferenceEquals(nowSelected, _lastNotifiedSelected))
            {
                _lastNotifiedSelected = nowSelected;
                OnPropertyChanged(nameof(SelectedObject));
                OnPropertyChanged(nameof(SelectedObjectName));
                OnPropertyChanged(nameof(HasSelectedObject));
                OnPropertyChanged(nameof(HasNoSelectedObject));
                OnPropertyChanged(nameof(SelectedSceneObject));
            }
        });
    }

    private void OnBridgeSelectionChanged(int? id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _selectionRoundTripGuard = true;
            try { SelectedObjectId = id == null ? null : ("engine-" + id.Value); }
            finally { _selectionRoundTripGuard = false; }
        });
    }

    private void RefreshAvailableModels()
    {
        if (_bridge == null) return;
        AvailableModels.Clear();
        foreach (var k in _bridge.AvailableModelKeys) AvailableModels.Add(k);
    }

    // -------- Commands --------

    [RelayCommand]
    private void SetActiveTool(string tool) => ActiveTool = tool;

    [RelayCommand]
    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
        if (_bridge == null) return;
        if (IsPlaying) _bridge.RequestPlay(); else _bridge.RequestStop();
    }

    [RelayCommand]
    private void SelectSceneObject(string id)
    {
        SelectedObjectId = id;
        if (_selectionRoundTripGuard) return;
        if (_bridge == null) return;
        // string id is "engine-{N}"; reverse to int for the bridge.
        if (id != null && id.StartsWith("engine-") && int.TryParse(id.Substring(7), out int parsed))
            _bridge.RequestSelect(parsed);
    }

    [RelayCommand]
    private void ToggleSceneObjectVisible(SceneObjectViewModel obj)
    {
        if (obj == null) return;
        obj.Visible = !obj.Visible;
        if (_bridge != null && obj.EngineId is int id)
        {
            bool v = obj.Visible;
            _bridge.EnqueueMutate(id, target => target.IsEnabled = v);
        }
    }

    [RelayCommand]
    private void ToggleSceneObjectLocked(SceneObjectViewModel obj)
    {
        if (obj != null) obj.Locked = !obj.Locked;
    }

    [RelayCommand]
    private void ToggleSceneObjectExpanded(SceneObjectViewModel obj)
    {
        if (obj != null) obj.IsExpanded = !obj.IsExpanded;
    }

    [RelayCommand]
    private void SetViewMode(string mode) => ViewMode = mode;

    [RelayCommand]
    private void SetShadingMode(string mode) => ShadingMode = mode;

    [RelayCommand]
    private void SetConsoleFilter(string filter) => ConsoleFilter = filter;

    [RelayCommand]
    private void SelectLog(int id) => SelectedLogId = id;

    [RelayCommand]
    private void ToggleAssetExpanded(AssetNode node) => node.IsExpanded = !node.IsExpanded;

    [RelayCommand]
    private void ClearConsole()
    {
        ConsoleEntries.Clear();
        OnPropertyChanged(nameof(FilteredConsole));
        OnPropertyChanged(nameof(ConsoleLogCount));
        OnPropertyChanged(nameof(ConsoleWarnCount));
        OnPropertyChanged(nameof(ConsoleErrorCount));
    }

    [RelayCommand]
    private void AddPointLight()
    {
        if (_bridge == null) return;
        XnaVector3 pos = _bridge.SpawnPoint;
        _bridge.EnqueueAddPointLight(pos, radius: 25f, color: XnaColor.White, intensity: 20f);
    }

    [RelayCommand]
    private void AddDirectionalLight()
    {
        if (_bridge == null) return;
        // Default sun-ish direction pointing into the ground (engine convention: -Z is down)
        var dir = new XnaVector3(0.3f, 0.2f, -1f);
        _bridge.EnqueueAddDirectionalLight(dir, XnaColor.White, intensity: 1f);
    }

    [RelayCommand]
    private void AddEntity(string? modelKey)
    {
        if (_bridge == null) return;
        if (string.IsNullOrEmpty(modelKey)) modelKey = "Cube";
        _bridge.EnqueueAddBasicEntity(modelKey, _bridge.SpawnPoint);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (_bridge == null || SelectedObject is null || SelectedObject.EngineId is not int id) return;
        _bridge.EnqueueDelete(id);
    }

    // -------- Scene file commands --------

    private const string SceneFileExtension = "obsc";
    private string? _lastSceneFolder;

    [RelayCommand]
    private void NewScene()
    {
        if (_bridge == null) return;
        _bridge.EnqueueNewScene();
    }

    [RelayCommand]
    private async Task OpenSceneAsync(Window? window)
    {
        if (_bridge == null || window?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Scene",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Obsidian Scene") { Patterns = new[] { "*." + SceneFileExtension } }
            },
            SuggestedStartLocation = await GetLastFolderAsync(sp),
        });
        if (files.Count == 0) return;
        string path = files[0].Path.LocalPath;
        _lastSceneFolder = System.IO.Path.GetDirectoryName(path);
        _bridge.EnqueueLoadScene(path);
    }

    [RelayCommand]
    private async Task SaveSceneAsync(Window? window)
    {
        if (_bridge == null) return;
        string? current = _bridge.CurrentScenePath;
        if (string.IsNullOrEmpty(current)) { await SaveSceneAsAsync(window); return; }
        _bridge.EnqueueSaveScene(current);
    }

    [RelayCommand]
    private async Task SaveSceneAsAsync(Window? window)
    {
        if (_bridge == null || window?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Scene As",
            DefaultExtension = SceneFileExtension,
            SuggestedFileName = _bridge.CurrentSceneName ?? "Untitled",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Obsidian Scene") { Patterns = new[] { "*." + SceneFileExtension } }
            },
            SuggestedStartLocation = await GetLastFolderAsync(sp),
        });
        if (file == null) return;
        string path = file.Path.LocalPath;
        _lastSceneFolder = System.IO.Path.GetDirectoryName(path);
        _bridge.EnqueueSaveScene(path);
    }

    private async Task<IStorageFolder?> GetLastFolderAsync(IStorageProvider sp)
    {
        if (string.IsNullOrEmpty(_lastSceneFolder)) return null;
        try { return await sp.TryGetFolderFromPathAsync(new Uri(_lastSceneFolder)); }
        catch { return null; }
    }

    // -------- Helpers --------

    private static SceneObjectViewModel? FindObject(IEnumerable<SceneObjectViewModel> list, string? id)
    {
        if (id == null) return null;
        foreach (var item in list)
        {
            if (item.Id == id) return item;
            var nested = FindObject(item.Children, id);
            if (nested != null) return nested;
        }
        return null;
    }

    private void BuildAssets()
    {
        var meshes = new AssetNode { Id = "f-meshes", Name = "Meshes", Kind = AssetKind.Folder, IsExpanded = true };
        meshes.Children.Add(new AssetNode { Id = "a-cube", Name = "Cube.fbx", Kind = AssetKind.Mesh });
        meshes.Children.Add(new AssetNode { Id = "a-sphere", Name = "Sphere.fbx", Kind = AssetKind.Mesh });
        meshes.Children.Add(new AssetNode { Id = "a-pillar", Name = "Pillar.fbx", Kind = AssetKind.Mesh });

        var textures = new AssetNode { Id = "f-textures", Name = "Textures", Kind = AssetKind.Folder };
        textures.Children.Add(new AssetNode { Id = "a-tex-stone", Name = "Stone_Albedo.png", Kind = AssetKind.Texture });
        textures.Children.Add(new AssetNode { Id = "a-tex-metal", Name = "Metal_Albedo.png", Kind = AssetKind.Texture });

        var scripts = new AssetNode { Id = "f-scripts", Name = "Scripts", Kind = AssetKind.Folder };
        scripts.Children.Add(new AssetNode { Id = "a-script-player", Name = "PlayerController.cs", Kind = AssetKind.Script });
        scripts.Children.Add(new AssetNode { Id = "a-script-scene", Name = "SceneManager.cs", Kind = AssetKind.Script });

        var audio = new AssetNode { Id = "f-audio", Name = "Audio", Kind = AssetKind.Folder, IsExpanded = false };
        audio.Children.Add(new AssetNode { Id = "a-audio-step", Name = "footstep.wav", Kind = AssetKind.Audio });

        var materials = new AssetNode { Id = "f-materials", Name = "Materials", Kind = AssetKind.Folder, IsExpanded = false };
        materials.Children.Add(new AssetNode { Id = "a-mat-default", Name = "Default.mat", Kind = AssetKind.Material });

        AssetTree.Add(meshes);
        AssetTree.Add(textures);
        AssetTree.Add(scripts);
        AssetTree.Add(audio);
        AssetTree.Add(materials);
    }

    private void BuildConsole()
    {
        ConsoleEntries.Add(new ConsoleEntry { Id = 1, Level = ConsoleLevel.Log, Message = "Anvil ready — waiting for engine bridge", Source = "Anvil:Boot", Time = "00:00:01" });
    }
}
