using System.Collections.ObjectModel;
using System.Linq;
using Anvil.Models;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anvil.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private string _projectName = "MyGame";
    [ObservableProperty] private string _sceneName = "SampleScene";
    [ObservableProperty] private string _platform = "PC, Mac & Linux";
    [ObservableProperty] private string _graphicsApi = "DX12";
    [ObservableProperty] private int _fps = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToolSelect), nameof(IsToolMove), nameof(IsToolRotate),
        nameof(IsToolScale), nameof(IsToolPan), nameof(IsToolSnap))]
    private string _activeTool = "Select";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedObject), nameof(SelectedObjectName),
        nameof(HasSelectedObject), nameof(HasNoSelectedObject))]
    private string? _selectedObjectId = "cube-1";

    public SceneObjectViewModel? SelectedObject => FindObject(SceneObjects, SelectedObjectId);
    public string? SelectedObjectName => SelectedObject?.Name;
    public bool HasSelectedObject => SelectedObject != null;
    public bool HasNoSelectedObject => SelectedObject == null;

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
        BuildScene();
        BuildAssets();
        BuildConsole();
    }

    [RelayCommand]
    private void SetActiveTool(string tool) => ActiveTool = tool;

    [RelayCommand]
    private void TogglePlay() => IsPlaying = !IsPlaying;

    [RelayCommand]
    private void SelectSceneObject(string id) => SelectedObjectId = id;

    [RelayCommand]
    private void ToggleSceneObjectVisible(SceneObjectViewModel obj) => obj.Visible = !obj.Visible;

    [RelayCommand]
    private void ToggleSceneObjectLocked(SceneObjectViewModel obj) => obj.Locked = !obj.Locked;

    [RelayCommand]
    private void ToggleSceneObjectExpanded(SceneObjectViewModel obj) => obj.IsExpanded = !obj.IsExpanded;

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

    private static SceneObjectViewModel? FindObject(System.Collections.Generic.IEnumerable<SceneObjectViewModel> list, string? id)
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

    private void BuildScene()
    {
        var camera = new SceneObjectViewModel
        {
            Id = "camera-1",
            Name = "Main Camera",
            Type = SceneObjectType.Camera,
            PositionY = 1.5,
            PositionZ = -6,
            RotationX = 15,
            Camera = new CameraInfo { Fov = 60, Near = 0.1, Far = 1000 },
        };

        var light = new SceneObjectViewModel
        {
            Id = "light-1",
            Name = "Directional Light",
            Type = SceneObjectType.Light,
            PositionY = 5,
            RotationX = 50,
            RotationY = -30,
            Light = new LightInfo { Type = LightType.Directional, Color = Color.Parse("#FFF7D6"), Intensity = 1.0 },
        };

        var sceneGroup = new SceneObjectViewModel
        {
            Id = "group-scene",
            Name = "Scene Objects",
            Type = SceneObjectType.Group,
        };

        var cube = new SceneObjectViewModel
        {
            Id = "cube-1",
            Name = "Cube",
            Type = SceneObjectType.Mesh,
            PositionY = 0.5,
            Material = new MaterialInfo { Color = Color.Parse("#3D5AFA"), Roughness = 0.4, Metallic = 0.1, Opacity = 1.0 },
        };
        var sphere = new SceneObjectViewModel
        {
            Id = "sphere-1",
            Name = "Sphere",
            Type = SceneObjectType.Mesh,
            PositionX = 2,
            PositionY = 0.5,
            Material = new MaterialInfo { Color = Color.Parse("#FA5A6E"), Roughness = 0.7, Metallic = 0.0, Opacity = 1.0 },
        };
        sceneGroup.Children.Add(cube);
        sceneGroup.Children.Add(sphere);

        var props = new SceneObjectViewModel
        {
            Id = "group-props",
            Name = "Props",
            Type = SceneObjectType.Group,
            IsExpanded = false,
        };
        props.Children.Add(new SceneObjectViewModel
        {
            Id = "pillar-a",
            Name = "Pillar A",
            Type = SceneObjectType.Mesh,
            PositionX = -3,
            Material = new MaterialInfo { Color = Color.Parse("#BFC4D6") },
        });
        props.Children.Add(new SceneObjectViewModel
        {
            Id = "box-b",
            Name = "Box B",
            Type = SceneObjectType.Mesh,
            PositionX = 4,
            Material = new MaterialInfo { Color = Color.Parse("#7F5AFA") },
        });

        SceneObjects.Add(camera);
        SceneObjects.Add(light);
        SceneObjects.Add(sceneGroup);
        SceneObjects.Add(props);
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
        ConsoleEntries.Add(new ConsoleEntry { Id = 1, Level = ConsoleLevel.Log, Message = "Scene loaded successfully", Source = "SceneManager.cs:42", Time = "00:00:01" });
        ConsoleEntries.Add(new ConsoleEntry { Id = 2, Level = ConsoleLevel.Log, Message = "Shader compiled: PBR/Standard", Source = "ShaderCache.cs:128", Time = "00:00:01" });
        ConsoleEntries.Add(new ConsoleEntry { Id = 3, Level = ConsoleLevel.Warn, Message = "Mesh 'Pillar A' has no collider component", Source = "Physics.cs:88", Time = "00:00:02" });
        ConsoleEntries.Add(new ConsoleEntry { Id = 4, Level = ConsoleLevel.Warn, Message = "Directional Light intensity clamped to [0, 8]", Source = "Lighting.cs:204", Time = "00:00:02" });
        ConsoleEntries.Add(new ConsoleEntry { Id = 5, Level = ConsoleLevel.Error, Message = "NullReferenceException: Object reference not set", Source = "PlayerController.cs:201", Time = "00:00:03" });
    }
}
