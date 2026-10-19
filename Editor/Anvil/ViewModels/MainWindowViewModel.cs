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
using Engine.Components;
using Engine.Editor;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Anvil.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<WorkspaceTabViewModel> WorkspaceTabs { get; } =
        new() { new WorkspaceTabViewModel("Engine", "Engine") };

    public IReadOnlyList<WorkspaceTabViewModel> AvailableWorkspacePanels { get; } = new[]
    {
        new WorkspaceTabViewModel("Engine", "Engine"),
        new WorkspaceTabViewModel("Environment", "Environment"),
        new WorkspaceTabViewModel("World", "World"),
        new WorkspaceTabViewModel("RenderPipeline", "Pipeline"),
        new WorkspaceTabViewModel("PostProcessing", "Post Processing"),
        new WorkspaceTabViewModel("Lighting", "Lighting"),
        new WorkspaceTabViewModel("Input", "Input Devices"),
        new WorkspaceTabViewModel("Steam", "Steam"),
        new WorkspaceTabViewModel("Mcp", "MCP"),
        new WorkspaceTabViewModel("Classifications", "Layers, Tags & Roles"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineWorkspaceSelected), nameof(IsWorkspaceEnvironmentView),
        nameof(IsWorkspaceWorldView), nameof(IsWorkspaceRenderPipelineView), nameof(IsWorkspacePostProcessingView),
        nameof(IsWorkspaceLightingView), nameof(IsWorkspaceInputView), nameof(IsWorkspaceSteamView), nameof(IsWorkspaceClassificationsView), nameof(IsWorkspaceMcpView))]
    private WorkspaceTabViewModel? _selectedWorkspaceTab;

    public bool IsEngineWorkspaceSelected => SelectedWorkspaceTab?.IsEngine == true;
    public bool IsWorkspaceEnvironmentView => SelectedWorkspaceTab?.Kind == "Environment";
    public bool IsWorkspaceWorldView => SelectedWorkspaceTab?.Kind == "World";
    public bool IsWorkspaceRenderPipelineView => SelectedWorkspaceTab?.Kind == "RenderPipeline";
    public bool IsWorkspacePostProcessingView => SelectedWorkspaceTab?.Kind == "PostProcessing";
    public bool IsWorkspaceLightingView => SelectedWorkspaceTab?.Kind == "Lighting";
    public bool IsWorkspaceInputView => SelectedWorkspaceTab?.Kind == "Input";
    public bool IsWorkspaceSteamView => SelectedWorkspaceTab?.Kind == "Steam";
    public bool IsWorkspaceMcpView => SelectedWorkspaceTab?.Kind == "Mcp";
    public McpSettingsViewModel Mcp { get; } = new();
    public bool IsWorkspaceClassificationsView => SelectedWorkspaceTab?.Kind == "Classifications";
    public ClassificationsViewModel Classifications { get; } = new();

    partial void OnSelectedWorkspaceTabChanged(WorkspaceTabViewModel? value)
    {
        if (value?.Kind == "RenderPipeline") RenderPipeline.Refresh();
    }

    [RelayCommand]
    private void OpenWorkspaceTab(string kind)
    {
        var tab = WorkspaceTabs.FirstOrDefault(t => t.Kind == kind);
        if (tab == null)
        {
            tab = AvailableWorkspacePanels.FirstOrDefault(t => t.Kind == kind);
            if (tab == null) return;
            WorkspaceTabs.Add(tab);
        }
        if (ReferenceEquals(SelectedWorkspaceTab, tab) && kind == "RenderPipeline") RenderPipeline.Refresh();
        SelectedWorkspaceTab = tab;
    }

    [RelayCommand]
    private void CloseWorkspaceTab(WorkspaceTabViewModel? tab)
    {
        if (tab == null) return;
        int index = WorkspaceTabs.IndexOf(tab);
        if (index < 0) return;

        // Pick a neighbor before removal so the TabStrip keeps the intended selection.
        if (ReferenceEquals(SelectedWorkspaceTab, tab))
            SelectedWorkspaceTab = index + 1 < WorkspaceTabs.Count ? WorkspaceTabs[index + 1]
                : index > 0 ? WorkspaceTabs[index - 1] : null;
        WorkspaceTabs.RemoveAt(index);
    }

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

    // Engine ground grid (GridRenderModule) at Z = 0, drawn in edit mode only.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridToggleTip))]
    private bool _isGridVisible = true;

    public string GridToggleTip => IsGridVisible ? "Hide ground grid" : "Show ground grid";
    partial void OnIsGridVisibleChanged(bool value) => PushGridVisible();

    [RelayCommand]
    private void ToggleGrid() => IsGridVisible = !IsGridVisible;

    private void PushGridVisible()
    {
        bool visible = IsGridVisible;
        _bridge?.EnqueueGameThreadAction(() => Engine.Recources.GameSettings.e_showgrid = visible);
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
    /// View model for the Post Processing workspace panel.
    /// </summary>
    public PostProcessingViewModel PostProcessing { get; } = new();

    /// <summary>
    /// Baked lighting settings and bake controls for the Lighting workspace panel.
    /// </summary>
    public LightingViewModel Lighting { get; } = new();
    public EnvironmentViewModel Environment { get; } = new();
    public SteamViewModel Steam { get; } = new();
    public InputDevicesViewModel InputDevices { get; } = new();

    /// <summary>
    /// The engine's passes and user render features, shown in the Pipeline workspace panel.
    /// </summary>
    public RenderPipelineViewModel RenderPipeline { get; } = new();

    /// <summary>
    /// Right-hand Inspector content: object selection or a selected material asset.
    /// Settings commands are routed to workspace tabs by SetInspectorView.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorSelectionView), nameof(IsInspectorMaterialAssetView))]
    private string _inspectorView = "Selection";

    public bool IsInspectorSelectionView => InspectorView == "Selection";
    /// <summary>A material asset (.mat) clicked in Assets; see <see cref="MaterialAsset"/>.</summary>
    public bool IsInspectorMaterialAssetView => InspectorView == "MaterialAsset" && MaterialAsset != null;

    [RelayCommand]
    private void SetInspectorView(string view)
    {
        if (view != "MaterialAsset") MaterialAsset?.Flush();
        if (AvailableWorkspacePanels.Any(panel => panel.Kind == view))
        {
            OpenWorkspaceTab(view);
            return;
        }
        InspectorView = view;
    }

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
        SelectedWorkspaceTab = WorkspaceTabs[0];
        BuildConsole();
        RefreshConsoleLogs();
    }

    private DispatcherTimer? _logTimer;
    private long _lastLogSequence;

    public void RefreshConsoleLogs()
    {
        foreach (var entry in EditorBridge.GetLogEntries(_lastLogSequence))
        {
            AddConsoleEntry(ConsoleLevel.Log, entry.Message,
                entry.Message.StartsWith("MCP:", StringComparison.Ordinal) ? "Anvil:MCP" : "Engine",
                entry.Timestamp);
            _lastLogSequence = entry.Sequence;
        }
    }

    public void StopConsoleLogging()
    {
        _logTimer?.Stop();
        RefreshConsoleLogs();
    }

    // -------- Engine bridge wiring --------

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_bridge != null) return;
        _bridge = bridge;
        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _logTimer.Tick += (_, _) => RefreshConsoleLogs();
        _logTimer.Start();
        bridge.EnqueueSetAudioEnabled(IsAudioEnabled);
        PushStatsVisible();
        PushGridVisible();
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
        AddableObjects.Add(new AddableObjectType("Particle System", _ => AddParticleSystem(), _bridge));
        AddableObjects.Add(new AddableObjectType("Decal", _ => AddDecal(), _bridge));
        AddableObjects.Add(new AddableObjectType("Liquid Bottle", _ => AddLiquidBottle(), _bridge));
        AddableObjects.Add(new AddableObjectType("Tree", _ => AddTree(), _bridge));

        // Hand the bridge to the post-processing VM so its setters can
        // marshal shader-parameter writes onto the game thread.
        PostProcessing.AttachBridge(bridge);

        // Baked lighting tab: per-scene settings + bake progress; bake results go to the console.
        Lighting.LogRequested += (level, message) => AddConsoleEntry(level, message, "Anvil:Lighting");
        Lighting.AttachBridge(bridge);
        Environment.AttachBridge(bridge);
        Steam.AttachBridge(bridge);
        Mcp.AttachBridge(bridge);
        InputDevices.AttachBridge(bridge);
        Classifications.AttachBridge(bridge);
        RenderPipeline.LogRequested += (level, message) => AddConsoleEntry(level, message, "Anvil:RenderPipeline");
        RenderPipeline.AttachBridge(bridge, MoveToRecycleBin);

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
            Classifications.Refresh();
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
            // Picking a gameobject (viewport or Hierarchy) leaves a material asset's Inspector.
            if (id != null && InspectorView == "MaterialAsset") SetInspectorView("Selection");
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
        // Clear pending history too, so the next timer tick cannot restore old messages.
        var pending = EditorBridge.GetLogEntries(_lastLogSequence);
        if (pending.Length > 0) _lastLogSequence = pending[^1].Sequence;
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

    /// <summary>
    /// A small sphere carrying a Particle System: the sphere marks and selects the emitter while editing and is
    /// hidden in Play (Particle System > Hide Mesh).
    /// </summary>
    [RelayCommand]
    private void AddParticleSystem()
    {
        if (_bridge == null) return;
        _bridge.EnqueueAddBasicEntity("IsoSphere", _bridge.SpawnPoint, "Particle System", new XnaVector3(0.15f),
            new[] { ParticleSystemComponent.TypeId });
    }

    /// <summary>
    /// A 2 x 2 x 2 m decal box projecting the default scorch mark down -Z onto whatever it overlaps; move it onto
    /// a surface and scale/rotate it with the gizmo (local Z is the projection depth).
    /// </summary>
    [RelayCommand]
    private void AddDecal()
    {
        if (_bridge == null) return;
        _bridge.EnqueueAddDecal(_bridge.SpawnPoint, XnaVector3.One);
    }

    /// <summary>The built-in Tree (own bark and leaf materials) swaying in the scene wind (Foliage Wind).</summary>
    [RelayCommand]
    private void AddTree()
    {
        if (_bridge == null) return;
        _bridge.EnqueueAddBasicEntity("Tree", _bridge.SpawnPoint, "Tree", componentTypes: new[] { FoliageWindComponent.TypeId });
    }

    /// <summary>The built-in glass Bottle filled by a Liquid component (sloshes when moved; Inspector &gt; Liquid).</summary>
    [RelayCommand]
    private void AddLiquidBottle()
    {
        if (_bridge == null) return;
        _bridge.EnqueueAddBasicEntity("Bottle", _bridge.SpawnPoint, "Bottle", componentTypes: new[] { LiquidComponent.TypeId });
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
        // Bones belong to their gameobject's model.
        if (_bridge == null || SelectedObject is null || SelectedObject.EngineId is not int id || SelectedObject.IsBone) return;
        _bridge.EnqueueDelete(id);
    }

    [RelayCommand]
    private void DeleteSceneObject(SceneObjectViewModel? obj)
    {
        if (_bridge == null || obj?.EngineId is not int id) return;
        _bridge.EnqueueDelete(id);
    }

    // -------- Prefabs (Content/Prefabs/*.prefab, Engine.Logic.PrefabUtility) --------

    /// <summary>
    /// Saves a gameobject (the selection when null) as a new prefab in a Content folder ("Prefabs" when null) and
    /// links it to the prefab. Hierarchy/Inspector/GameObject menu, or a Hierarchy drag onto the Assets panel.
    /// </summary>
    [RelayCommand]
    private void CreatePrefab(SceneObjectViewModel? obj) => CreatePrefabIn(obj ?? SelectedObject, null);

    public void CreatePrefabIn(SceneObjectViewModel? obj, string? folder)
    {
        if (_bridge == null) return;
        if (obj?.EngineId is not int id || obj.Kind != EditorObjectKind.BasicEntity)
        {
            AddConsoleEntry(ConsoleLevel.Warn, "Select a gameobject to save it as a prefab (lights, decals and the camera can't be prefabs).", "Anvil:Prefabs");
            return;
        }
        string name = obj.Name;
        _bridge.EnqueueCreatePrefab(id, folder, (path, error) => Dispatcher.UIThread.Post(() =>
        {
            ScriptPrefabFieldViewModel.InvalidateFiles();
            if (path != null)
                AddConsoleEntry(ConsoleLevel.Log, $"Saved \"{name}\" as prefab {path}. Drag it from Assets into the Hierarchy (or double-click it) to place copies.", "Anvil:Prefabs");
            else
                AddConsoleEntry(ConsoleLevel.Error, $"Couldn't save \"{name}\" as a prefab: {error}", "Anvil:Prefabs");
        }));
    }

    /// <summary>Writes a prefab instance's changes into its prefab; the other instances follow (their own overrides stay).</summary>
    [RelayCommand]
    private void ApplyPrefab(SceneObjectViewModel? obj)
    {
        obj ??= SelectedObject;
        if (_bridge == null || obj?.EngineId is not int id || !obj.IsPrefabInstance) return;
        string prefab = obj.PrefabPath!;
        _bridge.EnqueueApplyPrefab(id, (changed, error) => Dispatcher.UIThread.Post(() =>
        {
            if (changed >= 0)
                AddConsoleEntry(ConsoleLevel.Log, $"Applied to {prefab}" + (changed > 0 ? $" ({changed} other instance{(changed == 1 ? "" : "s")} in this scene updated)." : "."), "Anvil:Prefabs");
            else
                AddConsoleEntry(ConsoleLevel.Error, $"Couldn't apply to {prefab}: {error}", "Anvil:Prefabs");
        }));
    }

    /// <summary>Drops a prefab instance's overrides: it takes the prefab's values again.</summary>
    [RelayCommand]
    private void RevertPrefab(SceneObjectViewModel? obj)
    {
        obj ??= SelectedObject;
        if (_bridge == null || obj?.EngineId is not int id || !obj.IsPrefabInstance) return;
        _bridge.EnqueueRevertPrefab(id, error =>
        {
            if (error != null) Dispatcher.UIThread.Post(() => AddConsoleEntry(ConsoleLevel.Error, "Couldn't revert: " + error, "Anvil:Prefabs"));
        });
    }

    /// <summary>Unlinks a prefab instance: it becomes a plain gameobject that no longer follows the prefab.</summary>
    [RelayCommand]
    private void UnpackPrefab(SceneObjectViewModel? obj)
    {
        obj ??= SelectedObject;
        if (_bridge == null || obj?.EngineId is not int id || !obj.IsPrefabInstance) return;
        _bridge.EnqueueUnpackPrefab(id);
    }

    /// <summary>Places a prefab (Content-relative .prefab path) in front of the editor camera and selects it.</summary>
    public void InstantiatePrefabAsset(string? prefabPath)
    {
        if (_bridge == null || string.IsNullOrEmpty(prefabPath)) return;
        _bridge.EnqueueInstantiatePrefab(prefabPath, _bridge.SpawnPoint, id =>
        {
            if (id == null) Dispatcher.UIThread.Post(() => AddConsoleEntry(ConsoleLevel.Error,
                $"Couldn't place {prefabPath}: the file is missing or uses an unknown model.", "Anvil:Prefabs"));
        });
    }

    // -------- Material assets (Content/Materials/*.mat, Engine.Recources.MaterialAsset) --------

    /// <summary>The material asset the Inspector shows (Assets click), or null.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsInspectorMaterialAssetView))]
    private MaterialAssetViewModel? _materialAsset;

    /// <summary>The engine's source Content folder ("" before the engine is up).</summary>
    public string ContentSourceRoot => _bridge?.ContentSourceRoot ?? string.Empty;

    /// <summary>Shows a material asset (Content-relative .mat path) in the Inspector, where its settings can be edited.</summary>
    public void OpenMaterialAsset(string? path)
    {
        if (_bridge == null || !Engine.Recources.MaterialAsset.IsMaterialAsset(path)) return;
        if (MaterialAsset != null && Engine.Recources.MaterialAsset.SamePath(MaterialAsset.Path, path))
        {
            InspectorView = "MaterialAsset";
            return;
        }
        MaterialAsset?.Flush();
        var asset = new MaterialAssetViewModel(path!);
        asset.SaveFailed += message => AddConsoleEntry(ConsoleLevel.Error, message, "Anvil:Materials");
        MaterialAsset = asset;
        InspectorView = "MaterialAsset";
    }

    /// <summary>Material asset Inspector > Assign to Selected GameObject.</summary>
    [RelayCommand]
    private void AssignOpenMaterialAsset()
    {
        if (MaterialAsset == null) return;
        MaterialAsset.Flush();
        AssignMaterialAsset(null, MaterialAsset.Path);
    }

    private void CloseMaterialAsset()
    {
        MaterialAsset = null;
        if (InspectorView == "MaterialAsset") InspectorView = "Selection";
    }

    /// <summary>Assets context menu > Create Material: a new material in the folder under the cursor (Materials when null).</summary>
    public void CreateMaterialAsset(AssetNode? node)
    {
        if (_bridge == null) return;
        try
        {
            var asset = Engine.Recources.MaterialAsset.Create(FolderFor(node), "New Material");
            AddConsoleEntry(ConsoleLevel.Log, $"Created material {asset.Path}. Edit it in the Inspector, then drag it onto a gameobject in the Hierarchy (or double-click it with the gameobject selected).", "Anvil:Materials");
            RefreshAssetTree();
            OpenMaterialAsset(asset.Path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Couldn't create a material: " + ex.Message, "Anvil:Materials");
        }
    }

    /// <summary>
    /// Makes a gameobject (the selection when null) use a material asset; adds a Material component when it has none.
    /// Its Material component then follows the asset's edits.
    /// </summary>
    public void AssignMaterialAsset(SceneObjectViewModel? obj, string? path)
    {
        obj ??= SelectedObject;
        if (_bridge == null || !Engine.Recources.MaterialAsset.IsMaterialAsset(path)) return;
        if (obj?.EngineId is not int id || obj.Kind != EditorObjectKind.BasicEntity)
        {
            AddConsoleEntry(ConsoleLevel.Warn, "Select a gameobject (or drop the material on one in the Hierarchy) to assign it.", "Anvil:Materials");
            return;
        }
        // Add then link: both are queued in order and mark the scene changed.
        if (!obj.HasMaterialComponent) _bridge.EnqueueAddComponent(id, Engine.Components.MaterialComponent.TypeId);
        _bridge.EnqueueMutateComponent(id, Engine.Components.MaterialComponent.TypeId,
            c => ((Engine.Components.MaterialComponent)c).Link(path));
        AddConsoleEntry(ConsoleLevel.Log, $"\"{obj.Name}\" uses material {path}.", "Anvil:Materials");
    }

    /// <summary>
    /// Saves the surface a gameobject (the selection when null) shows, with the textures it draws, as a new material
    /// asset in Content/Materials and links the gameobject to it.
    /// </summary>
    [RelayCommand]
    private void CreateMaterialFromObject(SceneObjectViewModel? obj)
    {
        obj ??= SelectedObject;
        if (_bridge == null) return;
        if (obj?.EngineId is not int id || obj.Kind != EditorObjectKind.BasicEntity)
        {
            AddConsoleEntry(ConsoleLevel.Warn, "Select a gameobject to save its material as an asset.", "Anvil:Materials");
            return;
        }
        string name = obj.Name;
        // Captured on the game thread (the textures its model draws), then linked like any assignment.
        _bridge.EnqueueMutate(id, o =>
        {
            if (o is not Engine.Entities.BasicEntity entity) return;
            string? path = null, error = null;
            try { path = Engine.Recources.MaterialAsset.Create(null, entity.Name, Engine.Recources.MaterialAsset.Capture(entity))?.Path; }
            catch (Exception ex) { error = ex.Message; }
            Dispatcher.UIThread.Post(() =>
            {
                if (path == null)
                {
                    AddConsoleEntry(ConsoleLevel.Error, $"Couldn't save \"{name}\"'s material: {error ?? "unknown error"}", "Anvil:Materials");
                    return;
                }
                AssignMaterialAsset(obj, path);
                RefreshAssetTree();
                AddConsoleEntry(ConsoleLevel.Log, $"Saved \"{name}\"'s material as {path}. Drag it onto other gameobjects to share it.", "Anvil:Materials");
            });
        });
    }

    /// <summary>Content folder a drop on <paramref name="node"/> goes to: the folder itself, a file's folder, or null (Prefabs).</summary>
    public static string? FolderFor(AssetNode? node) =>
        node == null ? null
        : node.Kind == AssetKind.Folder ? node.RelativePath
        : System.IO.Path.GetDirectoryName(node.RelativePath)?.Replace('\\', '/') is { Length: > 0 } dir ? dir : null;

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

    // -------- Input actions (Window > Input Actions, Content/System/InputActions.json) --------

    [RelayCommand]
    private async Task OpenInputActionsAsync(Window? owner)
    {
        if (owner == null) return;
        var vm = new InputActionsViewModel();
        var dialog = new Views.InputActionsWindow { DataContext = vm };
        if (!await dialog.ShowDialog<bool>(owner)) return;

        try
        {
            vm.Apply();
            AddConsoleEntry(ConsoleLevel.Log,
                $"Input actions saved to {Engine.Logic.InputActionList.FileName} ({vm.Actions.Count} actions)", "Anvil:Input");
        }
        catch (Exception ex)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Couldn't save input actions: " + ex.Message, "Anvil:Input");
        }
    }

    // -------- Project settings (Edit > Project Settings, Delete Project) --------

    [RelayCommand]
    private async Task OpenProjectSettingsAsync(Window? owner)
    {
        if (owner == null) return;
        var vm = new ProjectSettingsViewModel(IsPlaying);
        var dialog = new Views.ProjectSettingsWindow { DataContext = vm };
        if (await dialog.ShowDialog<bool>(owner)) ResetProject();
    }

    /// <summary>
    /// Turns the project into a new, empty one (<see cref="Engine.Recources.ProjectReset"/>): the
    /// bridge swaps in an empty scene, the project's content goes to the Recycle Bin and the
    /// settings go back to their defaults.
    /// </summary>
    private void ResetProject()
    {
        if (_bridge == null) return;
        if (IsPlaying)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Stop Play before deleting the project.", "Anvil:Project");
            return;
        }
        AddConsoleEntry(ConsoleLevel.Log, "Deleting the project...", "Anvil:Project");
        _bridge.EnqueueResetProject(MoveToRecycleBin, result => Dispatcher.UIThread.Post(() =>
        {
            ProjectName = Engine.Recources.GameInfo.Read().WindowTitle;
            RefreshAssetTree();
            foreach (string error in result.Errors)
                AddConsoleEntry(ConsoleLevel.Error, "Delete Project: " + error, "Anvil:Project");
            AddConsoleEntry(result.Succeeded ? ConsoleLevel.Log : ConsoleLevel.Warn,
                $"Project deleted: {result.Removed.Count} item(s) moved to the Recycle Bin, " +
                $"{result.ManifestEntriesRemoved.Count} Content.mgcb entr(ies) removed, settings reset. " +
                $"New project scene: {Engine.Recources.ProjectReset.FirstScene}.",
                "Anvil:Project");
        }));
    }

    /// <summary>Moves a file or folder to the Recycle Bin (Delete in the Assets panel, Delete Project).</summary>
    private static void MoveToRecycleBin(string full)
    {
        if (System.IO.Directory.Exists(full))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(full,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else if (System.IO.File.Exists(full))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    // -------- Build (File > Build / Build and Run) --------

    private System.Threading.CancellationTokenSource? _buildCancel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(BuildAndRunCommand), nameof(CancelBuildCommand))]
    private bool _isBuilding;

    private bool CanBuild => !IsBuilding;

    /// <summary>Asks for the output folder (starting at the last one), then builds.</summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync(Window? window)
    {
        window ??= (Avalonia.Application.Current?.ApplicationLifetime as
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.StorageProvider is not { } sp) return;

        string current = GameBuilder.ResolveOutputFolder();
        IStorageFolder? start = null;
        try
        {
            // The default folder may not exist yet: start at the closest existing parent.
            string? dir = current;
            while (dir != null && !System.IO.Directory.Exists(dir)) dir = System.IO.Path.GetDirectoryName(dir);
            if (dir != null) start = await sp.TryGetFolderFromPathAsync(new Uri(dir));
        }
        catch { /* no suggestion */ }

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Build Game To",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count == 0) return;
        string folder = folders[0].Path.LocalPath;
        try { GameBuilder.SaveOutputFolder(folder); }
        catch (Exception ex) { AddConsoleEntry(ConsoleLevel.Warn, "Couldn't remember the build folder: " + ex.Message, "Anvil:Build"); }
        await RunBuildAsync(folder, run: false);
    }

    /// <summary>Builds to the last output folder (Builds/&lt;window name&gt; at first) and starts the game.</summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAndRunAsync() => RunBuildAsync(GameBuilder.ResolveOutputFolder(), run: true);

    [RelayCommand(CanExecute = nameof(IsBuilding))]
    private void CancelBuild() => _buildCancel?.Cancel();

    private async Task RunBuildAsync(string folder, bool run)
    {
        if (IsBuilding) return;
        if (_bridge?.IsSceneDirty == true)
            AddConsoleEntry(ConsoleLevel.Warn,
                "The open scene has unsaved changes: the build uses the saved scene files. Save first to include them.",
                "Anvil:Build");

        IsBuilding = true;
        _buildCancel = new System.Threading.CancellationTokenSource();
        try
        {
            string? exe = await Task.Run(() => GameBuilder.BuildAsync(folder,
                (level, message) => Dispatcher.UIThread.Post(() => AddConsoleEntry(level, message, "Anvil:Build")),
                _buildCancel.Token));
            if (exe == null || !run) return;
            if (GameBuilder.TryRun(exe, out string error))
                AddConsoleEntry(ConsoleLevel.Log, "Started " + System.IO.Path.GetFileName(exe), "Anvil:Build");
            else
                AddConsoleEntry(ConsoleLevel.Error, $"Couldn't start {exe}: {error}", "Anvil:Build");
        }
        catch (Exception ex)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Build failed: " + ex.Message, "Anvil:Build");
        }
        finally
        {
            _buildCancel.Dispose();
            _buildCancel = null;
            IsBuilding = false;
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
            [Engine.Logic.Prefab.Extension] = AssetKind.Prefab,
            [Engine.Recources.MaterialAsset.Extension] = AssetKind.Material,
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
        // Prefab fields in the Inspector offer the prefabs on disk.
        ScriptPrefabFieldViewModel.InvalidateFiles();
        // The open material asset follows outside edits (a linked gameobject's Inspector writes the file too).
        if (MaterialAsset != null && !MaterialAsset.ReloadIfChanged()) CloseMaterialAsset();
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
            MoveToRecycleBin(full);

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

    private void AddConsoleEntry(ConsoleLevel level, string message, string source, DateTime? timestamp = null)
    {
        int id = ConsoleEntries.Count == 0 ? 1 : ConsoleEntries.Max(e => e.Id) + 1;
        if (ConsoleEntries.Count >= EditorBridge.LogCapacity) ConsoleEntries.RemoveAt(0);
        ConsoleEntries.Add(new ConsoleEntry
        {
            Id = id, Level = level, Message = message, Source = source,
            Time = (timestamp ?? DateTime.Now).ToString("HH:mm:ss"),
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
