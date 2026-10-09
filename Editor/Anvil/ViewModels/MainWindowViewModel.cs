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
    // Mirrors the game's window name (Game Settings) in the title bar.
    [ObservableProperty] private string _projectName = Engine.Recources.GameInfo.Read().WindowTitle;
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
    [NotifyPropertyChangedFor(nameof(AudioIcon), nameof(AudioToggleTip))]
    private bool _isAudioEnabled = true;

    public string AudioIcon => IsAudioEnabled ? "Volume2" : "VolumeX";
    public string AudioToggleTip => IsAudioEnabled ? "Mute game audio" : "Enable game audio";
    partial void OnIsAudioEnabledChanged(bool value) => _bridge?.EnqueueSetAudioEnabled(value);

    [RelayCommand]
    private void ToggleAudio() => IsAudioEnabled = !IsAudioEnabled;

    // Engine debug stats (DebugScreen) in the viewport's top-left corner.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsToggleTip))]
    private bool _isStatsVisible = true;

    public string StatsToggleTip => IsStatsVisible ? "Hide debug stats" : "Show debug stats";
    partial void OnIsStatsVisibleChanged(bool value) => PushStatsVisible();

    [RelayCommand]
    private void ToggleStats() => IsStatsVisible = !IsStatsVisible;

    // 3 = DebugScreen's full detail level, 0 = off.
    private void PushStatsVisible()
    {
        bool visible = IsStatsVisible;
        _bridge?.EnqueueGameThreadAction(() => Engine.Recources.GameSettings.u_showdisplayinfo = visible ? 3 : 0);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredConsole))]
    private string _consoleFilter = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredConsole))]
    private string _consoleSearch = string.Empty;

    [ObservableProperty] private int? _selectedLogId = 1;

    [ObservableProperty] private string _assetsSearch = string.Empty;

    partial void OnAssetsSearchChanged(string value) => ApplyAssetFilter();

    public ObservableCollection<SceneObjectViewModel> SceneObjects { get; } = new();
    public ObservableCollection<AssetNode> AssetTree { get; } = new();
    public ObservableCollection<ConsoleEntry> ConsoleEntries { get; } = new();
    public ObservableCollection<string> AvailableModels { get; } = new();

    // Data-driven catalog for the "+" add-object menu. Point Light only for now (per Docs/TODO.md);
    // re-enabling another type is a single AddableObjects.Add(...) line in AttachBridge — no XAML.
    public ObservableCollection<AddableObjectType> AddableObjects { get; } = new();

    /// <summary>
    /// View model for the global post-processing / render settings panel.
    /// Shown inside the Inspector when <see cref="InspectorView"/> ==
    /// "PostProcessing". Replaces the legacy HelperSuite right-side panel.
    /// </summary>
    public PostProcessingViewModel PostProcessing { get; } = new();

    /// <summary>
    /// Baked lighting (probe volume) settings + bake controls for the active scene. Shown inside
    /// the Inspector when <see cref="InspectorView"/> == "Lighting".
    /// </summary>
    public LightingViewModel Lighting { get; } = new();
    public EnvironmentViewModel Environment { get; } = new();
    public SteamViewModel Steam { get; } = new();
    public InputDevicesViewModel InputDevices { get; } = new();

    /// <summary>
    /// Inspector content switch: "Selection" (default) shows the selected
    /// object; "PostProcessing" shows <see cref="PostProcessing"/>. Driven by
    /// the segmented control in the inspector header and by
    /// Window &gt; Post Processing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorSelectionView),
        nameof(IsInspectorPostProcessingView), nameof(IsInspectorLightingView), nameof(IsInspectorEnvironmentView),
        nameof(IsInspectorSteamView), nameof(IsInspectorInputView))]
    private string _inspectorView = "Selection";

    public bool IsInspectorSelectionView => InspectorView == "Selection";
    public bool IsInspectorPostProcessingView => InspectorView == "PostProcessing";
    public bool IsInspectorLightingView => InspectorView == "Lighting";
    public bool IsInspectorEnvironmentView => InspectorView == "Environment";
    public bool IsInspectorSteamView => InspectorView == "Steam";
    public bool IsInspectorInputView => InspectorView == "Input";

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
        BuildConsole();
    }

    // -------- Engine bridge wiring --------

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        bridge.EnqueueSetAudioEnabled(IsAudioEnabled);
        PushStatsVisible();
        bridge.SnapshotUpdated += OnBridgeSnapshot;
        bridge.SelectionChanged += OnBridgeSelectionChanged;
        bridge.SceneChanged += OnBridgeSceneChanged;
        bridge.ModeChanged += OnBridgeModeChanged;
        bridge.ModelRegistryChanged += OnBridgeModelRegistryChanged;

        // Refresh model picker now (may already be populated after first frame).
        RefreshAvailableModels();
        RefreshAssetTree();
        StartContentWatcher();

        // Build the "+" add-object catalog. It runs the same commands as the title bar's
        // GameObject menu, so both menus create identical objects.
        AddableObjects.Add(new AddableObjectType("Cube", _ => AddEntity("Cube"), _bridge));
        AddableObjects.Add(new AddableObjectType("Sphere", _ => AddEntity("IsoSphere"), _bridge));
        AddableObjects.Add(new AddableObjectType("Directional Light", _ => AddDirectionalLight(), _bridge));
        AddableObjects.Add(new AddableObjectType("Point Light", _ => AddPointLight(), _bridge));
        AddableObjects.Add(new AddableObjectType("Spot Light", _ => AddSpotLight(), _bridge));

        // Hand the bridge to the post-processing VM so its setters can
        // marshal shader-parameter writes onto the game thread.
        PostProcessing.AttachBridge(bridge);

        // Baked lighting tab: per-scene settings + bake progress; bake results go to the console.
        Lighting.LogRequested += (level, message) => AddConsoleEntry(level, message, "Anvil:Lighting");
        Lighting.AttachBridge(bridge);
        Environment.AttachBridge(bridge);
        Steam.AttachBridge(bridge);
        InputDevices.AttachBridge(bridge);

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
            Steam.Refresh();
            InputDevices.Refresh();
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
            // The asset tree is normally rebuilt via ModelRegistryChanged, but built-in
            // models don't fire that event — if the tree was built before the bridge
            // reported its models, rebuild once they're available so mesh files map to
            // their model keys and become draggable.
            if (_assetTreeModelCount != _bridge.AvailableModelKeys.Count)
                RefreshAssetTree();

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

    public void SetAssetDragActive(bool active) => _bridge?.SetHostDragDropActive(active);

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
    private void AddSpotLight()
    {
        if (_bridge == null) return;
        // Points straight down (engine convention: -Z is down); rotate it with the gizmo or inspector.
        _bridge.EnqueueAddSpotLight(_bridge.SpawnPoint, new XnaVector3(0, 0, -1), radius: 25f, color: XnaColor.White, intensity: 40f, spotAngle: 60f);
    }

    [RelayCommand]
    private void AddDirectionalLight()
    {
        if (_bridge == null) return;
        // Default sun-ish direction pointing into the ground (engine convention: -Z is down).
        // Intensity matches the starter scene's sun; 1 was too dim to notice.
        var dir = new XnaVector3(0.3f, 0.2f, -1f);
        _bridge.EnqueueAddDirectionalLight(dir, XnaColor.White, intensity: 100f);
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

    [RelayCommand]
    private void DeleteSceneObject(SceneObjectViewModel? obj)
    {
        if (_bridge == null || obj?.EngineId is not int id) return;
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

    // -------- Game settings (standalone window title + icon) --------

    [RelayCommand]
    private async Task OpenGameSettingsAsync(Window? owner)
    {
        if (owner == null) return;
        var vm = new GameSettingsViewModel();
        var dialog = new Views.GameSettingsWindow { DataContext = vm };
        if (!await dialog.ShowDialog<bool>(owner)) return;

        try
        {
            ProjectName = vm.Apply();
            AddConsoleEntry(ConsoleLevel.Log,
                $"Game settings saved to {Engine.Recources.GameInfo.FileName} (window name \"{ProjectName}\"), " +
                $"{Engine.Recources.IntroVideoList.FileName} ({vm.IntroVideos.Count} intro videos) " +
                $"and {Engine.Recources.SceneList.FileName} ({vm.Scenes.Count} scenes)",
                "Anvil:GameSettings");
        }
        catch (Exception ex)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Couldn't save game settings: " + ex.Message, "Anvil:GameSettings");
        }
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

    // -------- Assets panel (mirrors Engine/Content on disk) --------

    // Top-level Content entries that aren't user assets: mgcb build output, the mgcb
    // manifest (+ its backup), the legacy GUI folder and the engine's System folder. *.mgcontent build-state files
    // are hidden at any depth.
    private static readonly HashSet<string> HiddenContentEntries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "Graphical User Interface", "Content.mgcb", "Content.mgcb.org",
            // Engine-managed files (editor gizmo meshes/icons, GameInfo.json, GameIcon.ico).
            "System",
        };

    private static bool IsHiddenContentFile(string name) =>
        name.EndsWith(".mgcontent", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, AssetKind> AssetKindsByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".fbx"] = AssetKind.Mesh, [".obj"] = AssetKind.Mesh, [".x"] = AssetKind.Mesh,
            [".dae"] = AssetKind.Mesh, [".gltf"] = AssetKind.Mesh, [".glb"] = AssetKind.Mesh,
            [".png"] = AssetKind.Texture, [".jpg"] = AssetKind.Texture, [".jpeg"] = AssetKind.Texture,
            [".tga"] = AssetKind.Texture, [".dds"] = AssetKind.Texture, [".bmp"] = AssetKind.Texture,
            [".hdr"] = AssetKind.Texture,
            [".wav"] = AssetKind.Audio, [".mp3"] = AssetKind.Audio, [".ogg"] = AssetKind.Audio,
            [".flac"] = AssetKind.Audio, [".bank"] = AssetKind.Audio,
            [".fx"] = AssetKind.Shader, [".fxh"] = AssetKind.Shader, [".hlsl"] = AssetKind.Shader,
            [".spritefont"] = AssetKind.Font, [".ttf"] = AssetKind.Font, [".otf"] = AssetKind.Font,
            [".mp4"] = AssetKind.Video, [".wmv"] = AssetKind.Video, [".avi"] = AssetKind.Video,
            [".cs"] = AssetKind.Script, [".xml"] = AssetKind.Script, [".css"] = AssetKind.Script,
            [".obsc"] = AssetKind.Scene,
        };

    // Folders the user has expanded, by relative path, so rebuilds keep the tree open.
    private readonly HashSet<string> _expandedAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _assetTreeModelCount = -1;
    // Unfiltered tree; AssetTree shows it as-is or filtered by AssetsSearch.
    private List<AssetNode> _assetRoots = new();
    private System.IO.FileSystemWatcher? _contentWatcher;
    private DispatcherTimer? _assetRefreshTimer;

    private void RefreshAssetTree()
    {
        if (_bridge == null) return;
        string root = _bridge.ContentSourceRoot;
        if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root)) return;

        // Extensionless content path -> model key, so e.g. GameObjects/Test/cube.fbx
        // becomes a draggable "Cube" mesh.
        var modelKeysByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = _bridge.AvailableModelKeys.ToArray();
        foreach (var key in keys)
        {
            string? path = _bridge.GetModelAssetPath(key);
            if (!string.IsNullOrEmpty(path)) modelKeysByPath.TryAdd(path.Replace('\\', '/'), key);
        }
        _assetTreeModelCount = keys.Length;

        _assetRoots = BuildAssetNodes(root, root, modelKeysByPath);
        ApplyAssetFilter();
    }

    private void ApplyAssetFilter()
    {
        AssetTree.Clear();
        string query = AssetsSearch?.Trim() ?? string.Empty;
        foreach (var node in _assetRoots)
        {
            var shown = query.Length == 0 ? node : FilterAssetNode(node, query);
            if (shown != null) AssetTree.Add(shown);
        }
    }

    // A node whose name matches is kept whole; a folder that only contains matches is
    // shown as an expanded copy holding just those matches, so the user's own
    // expansion state is left alone.
    private static AssetNode? FilterAssetNode(AssetNode node, string query)
    {
        if (node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return node;
        if (node.Kind != AssetKind.Folder) return null;

        AssetNode? copy = null;
        foreach (var child in node.Children)
        {
            var match = FilterAssetNode(child, query);
            if (match == null) continue;
            copy ??= new AssetNode
            {
                Id = node.Id,
                Name = node.Name,
                Kind = node.Kind,
                RelativePath = node.RelativePath,
                ModelKey = node.ModelKey,
                OwnerModelKey = node.OwnerModelKey,
                IsExpanded = true,
            };
            copy.Children.Add(match);
        }
        return copy;
    }

    private List<AssetNode> BuildAssetNodes(string dir, string root, Dictionary<string, string> modelKeysByPath)
    {
        var nodes = new List<AssetNode>();
        bool isRoot = string.Equals(dir, root, StringComparison.OrdinalIgnoreCase);
        string[] subDirs, files;
        try
        {
            subDirs = System.IO.Directory.GetDirectories(dir);
            files = System.IO.Directory.GetFiles(dir);
        }
        catch { return nodes; }
        Array.Sort(subDirs, StringComparer.OrdinalIgnoreCase);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string sub in subDirs)
        {
            string name = System.IO.Path.GetFileName(sub);
            if (isRoot && HiddenContentEntries.Contains(name)) continue;
            string rel = RelativeContentPath(root, sub);
            var node = new AssetNode
            {
                Id = "dir:" + rel,
                Name = name,
                Kind = AssetKind.Folder,
                RelativePath = rel,
                OwnerModelKey = ImportedModelKeyFor(rel),
                IsExpanded = _expandedAssetPaths.Contains(rel),
            };
            foreach (var child in BuildAssetNodes(sub, root, modelKeysByPath))
                node.Children.Add(child);
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AssetNode.IsExpanded)) return;
                if (node.IsExpanded) _expandedAssetPaths.Add(node.RelativePath);
                else _expandedAssetPaths.Remove(node.RelativePath);
            };
            nodes.Add(node);
        }

        foreach (string file in files)
        {
            string name = System.IO.Path.GetFileName(file);
            if ((isRoot && HiddenContentEntries.Contains(name)) || IsHiddenContentFile(name)) continue;
            string rel = RelativeContentPath(root, file);
            string ext = System.IO.Path.GetExtension(name);
            var kind = AssetKindsByExtension.TryGetValue(ext, out var k) ? k : AssetKind.File;
            string? modelKey = null;
            if (kind == AssetKind.Mesh)
                modelKeysByPath.TryGetValue(rel.Substring(0, rel.Length - ext.Length), out modelKey);
            nodes.Add(new AssetNode
            {
                Id = "file:" + rel,
                Name = name,
                Kind = kind,
                RelativePath = rel,
                ModelKey = modelKey,
                OwnerModelKey = ImportedModelKeyFor(rel),
                IsExpanded = false,
            });
        }
        return nodes;
    }

    private static string RelativeContentPath(string root, string path) =>
        System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

    // Anything under GameObjects/Models/{key}/ belongs to that runtime-imported model
    // (the AssetImporter layout), making it a texture-drop / delete target.
    private string? ImportedModelKeyFor(string relPath)
    {
        var parts = relPath.Split('/');
        if (parts.Length < 3) return null;
        if (!parts[0].Equals("GameObjects", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("Models", StringComparison.OrdinalIgnoreCase)) return null;
        return _bridge != null && _bridge.IsDeletableModel(parts[2]) ? parts[2] : null;
    }

    // Keeps the tree in sync with edits made outside the editor (Explorer, git, model
    // imports). Events are debounced; bin/obj churn from content builds is ignored.
    private void StartContentWatcher()
    {
        if (_contentWatcher != null || _bridge == null) return;
        string root = _bridge.ContentSourceRoot;
        if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root)) return;

        _assetRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _assetRefreshTimer.Tick += (_, _) =>
        {
            _assetRefreshTimer.Stop();
            ApplyPendingManifestChanges(root);
            RefreshAssetTree();
        };

        try
        {
            _contentWatcher = new System.IO.FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName,
            };
            _contentWatcher.Created += (_, e) => OnContentChanged(root, e.FullPath, null, deleted: false);
            _contentWatcher.Deleted += (_, e) => OnContentChanged(root, e.FullPath, null, deleted: true);
            _contentWatcher.Renamed += (_, e) => OnContentChanged(root, e.FullPath, e.OldFullPath, deleted: false);
            _contentWatcher.EnableRaisingEvents = true;
        }
        catch
        {
            // Watching is a convenience; the tree still refreshes on model imports.
            _contentWatcher = null;
        }
    }

    // Deletes / renames seen by the watcher, applied to Content.mgcb on the next
    // debounce tick. UI thread only.
    private readonly List<string> _pendingManifestRemovals = new();
    private readonly List<(string OldRel, string NewRel)> _pendingManifestRenames = new();

    // Fires on a thread-pool thread.
    private void OnContentChanged(string root, string fullPath, string? oldFullPath, bool deleted)
    {
        string rel = RelativeContentPath(root, fullPath);
        string? oldRel = oldFullPath != null ? RelativeContentPath(root, oldFullPath) : null;
        bool visible = !HiddenContentEntries.Contains(rel.Split('/')[0]) && !IsHiddenContentFile(fullPath);
        bool oldVisible = oldRel != null && !HiddenContentEntries.Contains(oldRel.Split('/')[0]) && !IsHiddenContentFile(oldRel);
        if (!visible && !oldVisible) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (deleted) _pendingManifestRemovals.Add(rel);
            else if (oldRel != null) _pendingManifestRenames.Add((oldRel, rel));
            _assetRefreshTimer?.Stop();
            _assetRefreshTimer?.Start();
        });
    }

    // Keeps Content.mgcb in step with files deleted or renamed on disk (in Explorer or
    // the editor). Paths are re-checked first: editors that save by delete+rename, and
    // git checkouts, briefly remove files that come straight back.
    private void ApplyPendingManifestChanges(string root)
    {
        bool Exists(string rel)
        {
            string full = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            return System.IO.File.Exists(full) || System.IO.Directory.Exists(full);
        }

        try
        {
            foreach (var (oldRel, newRel) in _pendingManifestRenames)
            {
                if (Exists(oldRel) || !Exists(newRel)) continue;
                int n = Engine.Recources.ContentManifest.Rename(oldRel, newRel);
                if (n > 0) AddConsoleEntry(ConsoleLevel.Log, $"Content.mgcb: renamed {n} entr{(n == 1 ? "y" : "ies")} {oldRel} -> {newRel}", "Anvil:Assets");
            }
            foreach (string rel in _pendingManifestRemovals)
            {
                if (Exists(rel)) continue;
                var removed = Engine.Recources.ContentManifest.Unregister(rel);
                if (removed.Count > 0) AddConsoleEntry(ConsoleLevel.Log, $"Content.mgcb: removed {string.Join(", ", removed)}", "Anvil:Assets");
            }
        }
        catch (Exception ex)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Couldn't update Content.mgcb: " + ex.Message, "Anvil:Assets");
        }
        finally
        {
            _pendingManifestRenames.Clear();
            _pendingManifestRemovals.Clear();
        }
    }

    private void OnBridgeModelRegistryChanged()
    {
        // Fires on the engine thread; ObservableCollection mutations require the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            RefreshAvailableModels();
            RefreshAssetTree();
        });
    }

    /// <summary>
    /// Forward a disk-path .fbx (or .obj) to the engine importer. The registry-changed
    /// event handler will refresh the Assets panel + model picker once import completes.
    /// </summary>
    public void ImportFbxFromDisk(string path)
    {
        if (_bridge == null || string.IsNullOrEmpty(path)) return;
        _bridge.EnqueueImportModel(path, _ => { /* refresh happens via ModelRegistryChanged */ });
    }

    /// <summary>
    /// Copy dropped files / folders into Engine/Content: into the folder they were dropped
    /// on, the folder containing the file they were dropped on, or the Content root. Name
    /// clashes get a "_2", "_3", … suffix. Runs off the UI thread; the content watcher
    /// refreshes the tree once the copies land. Files are copied as-is (no Content.mgcb
    /// entry) — meshes go through <see cref="ImportFbxFromDisk"/> instead.
    /// </summary>
    public void CopyFilesIntoContent(AssetNode? target, IEnumerable<string> paths)
    {
        if (_bridge == null) return;
        string root = _bridge.ContentSourceRoot;
        if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root)) return;

        string relDir = target == null ? string.Empty
            : target.Kind == AssetKind.Folder ? target.RelativePath
            : (System.IO.Path.GetDirectoryName(target.RelativePath) ?? string.Empty);
        string destDir = System.IO.Path.Combine(root, relDir.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var sources = paths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (sources.Length == 0) return;

        Task.Run(() =>
        {
            var copied = new List<string>();
            foreach (string src in sources)
            {
                try
                {
                    if (System.IO.Directory.Exists(src))
                    {
                        string dest = UniqueContentPath(destDir, System.IO.Path.GetFileName(src.TrimEnd('\\', '/')));
                        // Refuse to copy a folder into itself (e.g. dragging Content's own subfolder in).
                        string full = System.IO.Path.GetFullPath(src).TrimEnd('\\') + "\\";
                        if (System.IO.Path.GetFullPath(dest).StartsWith(full, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("can't copy a folder into itself");
                        CopyDirectory(src, dest);
                        copied.AddRange(System.IO.Directory.GetFiles(dest, "*", System.IO.SearchOption.AllDirectories));
                    }
                    else if (System.IO.File.Exists(src))
                    {
                        System.IO.Directory.CreateDirectory(destDir);
                        string dest = UniqueContentPath(destDir, System.IO.Path.GetFileName(src));
                        System.IO.File.Copy(src, dest);
                        copied.Add(dest);
                    }
                }
                catch (Exception ex)
                {
                    string msg = $"Couldn't copy \"{src}\" into Content: {ex.Message}";
                    Dispatcher.UIThread.Post(() => AddConsoleEntry(ConsoleLevel.Error, msg, "Anvil:Assets"));
                }
            }

            // Pipeline assets (models, textures, effects, fonts) get a Content.mgcb entry
            // so the next content build picks them up; loose files are left as-is.
            try
            {
                var added = Engine.Recources.ContentManifest.Register(copied.Select(c => RelativeContentPath(root, c)));
                if (added.Count > 0)
                    Dispatcher.UIThread.Post(() => AddConsoleEntry(ConsoleLevel.Log,
                        $"Content.mgcb: added {string.Join(", ", added)}", "Anvil:Assets"));
            }
            catch (Exception ex)
            {
                string msg = "Couldn't update Content.mgcb: " + ex.Message;
                Dispatcher.UIThread.Post(() => AddConsoleEntry(ConsoleLevel.Error, msg, "Anvil:Assets"));
            }
        });
    }

    /// <summary>
    /// Imported model a Delete on this node should remove as a whole (with the engine
    /// unregistering it): its mesh file or its GameObjects/Models/{key} folder. Other
    /// nodes — including files inside that folder — are deleted as plain files.
    /// </summary>
    public string? ModelKeyToDeleteFor(AssetNode? node)
    {
        string? key = DeletableModelKeyFor(node);
        if (key == null || node == null) return null;
        bool isModelFolder = node.Kind == AssetKind.Folder &&
            node.RelativePath.Equals("GameObjects/Models/" + key, StringComparison.OrdinalIgnoreCase);
        bool isModelMesh = string.Equals(node.ModelKey, key, StringComparison.OrdinalIgnoreCase);
        return isModelFolder || isModelMesh ? key : null;
    }

    /// <summary>
    /// Moves a Content file or folder to the Recycle Bin and removes its Content.mgcb
    /// entries (everything under it, for a folder).
    /// </summary>
    public void DeleteAssetFromDisk(AssetNode? node)
    {
        if (_bridge == null || node == null || string.IsNullOrEmpty(node.RelativePath)) return;
        string full = System.IO.Path.Combine(_bridge.ContentSourceRoot,
            node.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        try
        {
            if (System.IO.Directory.Exists(full))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(full,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else if (System.IO.File.Exists(full))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

            var removed = Engine.Recources.ContentManifest.Unregister(node.RelativePath);
            if (removed.Count > 0)
                AddConsoleEntry(ConsoleLevel.Log, $"Content.mgcb: removed {string.Join(", ", removed)}", "Anvil:Assets");
        }
        catch (Exception ex)
        {
            AddConsoleEntry(ConsoleLevel.Error, $"Couldn't delete \"{node.RelativePath}\": {ex.Message}", "Anvil:Assets");
        }
    }

    private static string UniqueContentPath(string dir, string name)
    {
        string path = System.IO.Path.Combine(dir, name);
        string stem = System.IO.Path.GetFileNameWithoutExtension(name);
        string ext = System.IO.Path.GetExtension(name);
        for (int i = 2; System.IO.File.Exists(path) || System.IO.Directory.Exists(path); i++)
            path = System.IO.Path.Combine(dir, $"{stem}_{i}{ext}");
        return path;
    }

    private static void CopyDirectory(string src, string dest)
    {
        System.IO.Directory.CreateDirectory(dest);
        foreach (string file in System.IO.Directory.GetFiles(src))
            System.IO.File.Copy(file, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(file)));
        foreach (string sub in System.IO.Directory.GetDirectories(src))
            CopyDirectory(sub, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(sub)));
    }

    /// <summary>
    /// Spawn a BasicEntity in the scene from a registered model key (drag-drop target
    /// for the Hierarchy panel).
    /// </summary>
    public void AddEntityFromAsset(string modelKey)
    {
        if (_bridge == null || string.IsNullOrEmpty(modelKey)) return;
        _bridge.EnqueueAddBasicEntity(modelKey, _bridge.SpawnPoint);
    }

    /// <summary>
    /// Forward dropped texture files to the engine to be bound (by filename convention)
    /// to <paramref name="modelKey"/>. Refresh happens via ModelRegistryChanged.
    /// </summary>
    public void ImportTexturesFromDisk(string modelKey, IEnumerable<string> paths)
    {
        if (_bridge == null || string.IsNullOrEmpty(modelKey)) return;
        var arr = paths?.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (arr == null || arr.Length == 0) return;
        _bridge.EnqueueImportTextures(modelKey, arr, () => { /* refresh via ModelRegistryChanged */ });
    }

    /// <summary>
    /// Permanently delete an imported model (and its disk content). Called after the
    /// view confirms. No-op for built-ins. Refresh happens via ModelRegistryChanged.
    /// </summary>
    public void DeleteModelAsset(string modelKey)
    {
        if (_bridge == null || string.IsNullOrEmpty(modelKey)) return;
        if (!_bridge.IsDeletableModel(modelKey)) return;
        _bridge.EnqueueDeleteModelAsset(modelKey, () => { /* refresh via ModelRegistryChanged */ });
    }

    /// <summary>
    /// Resolves an Assets-tree node to a deletable imported-model key, or null: the mesh
    /// itself, or anything inside its GameObjects/Models/{key} folder.
    /// </summary>
    public string? DeletableModelKeyFor(AssetNode? node)
    {
        if (_bridge == null || node == null) return null;
        string? key = node.OwnerModelKey ?? node.ModelKey;
        return !string.IsNullOrEmpty(key) && _bridge.IsDeletableModel(key) ? key : null;
    }

    // Assets opened in an external text editor on double-click.
    private static readonly HashSet<string> TextAssetExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".fx", ".fxh", ".hlsl", ".xml", ".css", ".cs", ".spritefont",
            ".mgcb", ".json", ".txt", ".md",
        };

    public static bool IsTextAsset(AssetNode? node) =>
        node != null && node.Kind != AssetKind.Folder &&
        TextAssetExtensions.Contains(System.IO.Path.GetExtension(node.Name));

    /// <summary>
    /// Opens a text asset (shader, UI XML/CSS, ...) from Engine/Content in VS Code,
    /// or Notepad if VS Code isn't installed. Returns false if the node isn't a text asset.
    /// </summary>
    /// <summary>Opens a .obsc from the Assets panel as the active scene (e.g. Scenes/AutoExposureTest.obsc).</summary>
    public bool OpenSceneAsset(AssetNode? node)
    {
        if (_bridge == null || node?.Kind != AssetKind.Scene) return false;
        string fullPath = System.IO.Path.Combine(_bridge.ContentSourceRoot,
            node.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        _lastSceneFolder = System.IO.Path.GetDirectoryName(fullPath);
        _bridge.EnqueueLoadScene(fullPath);
        return true;
    }

    public bool OpenInTextEditor(AssetNode? node)
    {
        if (_bridge == null || !IsTextAsset(node)) return false;
        string fullPath = System.IO.Path.Combine(_bridge.ContentSourceRoot,
            node!.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!ExternalTextEditor.TryOpen(fullPath, out string error))
            AddConsoleEntry(ConsoleLevel.Error, error, "Anvil:Assets");
        return true;
    }

    private void AddConsoleEntry(ConsoleLevel level, string message, string source)
    {
        int id = ConsoleEntries.Count == 0 ? 1 : ConsoleEntries.Max(e => e.Id) + 1;
        ConsoleEntries.Add(new ConsoleEntry
        {
            Id = id, Level = level, Message = message, Source = source,
            Time = DateTime.Now.ToString("HH:mm:ss"),
        });
        OnPropertyChanged(nameof(FilteredConsole));
        OnPropertyChanged(nameof(ConsoleLogCount));
        OnPropertyChanged(nameof(ConsoleWarnCount));
        OnPropertyChanged(nameof(ConsoleErrorCount));
    }

    private void BuildConsole()
    {
        ConsoleEntries.Add(new ConsoleEntry { Id = 1, Level = ConsoleLevel.Log, Message = "Anvil ready — waiting for engine bridge", Source = "Anvil:Boot", Time = "00:00:01" });
    }
}
