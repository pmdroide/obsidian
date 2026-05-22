using System.Collections.ObjectModel;
using Anvil.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private string _projectName = "MyGame";
    [ObservableProperty] private string _sceneName = "SampleScene";
    [ObservableProperty] private string _platform = "PC, Mac & Linux";
    [ObservableProperty] private string _graphicsApi = "DX12";
    [ObservableProperty] private int _fps = 60;

    [ObservableProperty] private string _viewMode = "Perspective";
    [ObservableProperty] private string _shadingMode = "Solid";

    [ObservableProperty] private string _playStateLabel = "paused";

    public ObservableCollection<HierarchyNode> Hierarchy { get; } = new();
    public ObservableCollection<ConsoleEntry> ConsoleEntries { get; } = new();
    public ObservableCollection<AssetNode> Assets { get; } = new();

    [ObservableProperty] private InspectorState _inspector = new();

    [ObservableProperty] private int _consoleInfoCount;
    [ObservableProperty] private int _consoleWarningCount;
    [ObservableProperty] private int _consoleErrorCount;

    [ObservableProperty] private string _consoleFilterText = string.Empty;
    [ObservableProperty] private string _assetSearchText = string.Empty;

    public MainWindowViewModel()
    {
        BuildHierarchy();
        BuildAssets();
        BuildConsole();
    }

    private void BuildHierarchy()
    {
        var scene = new HierarchyNode { Name = "Scene", Icon = HierarchyIcon.Folder, Depth = 0 };
        scene.Children.Add(new HierarchyNode { Name = "Main Camera", Icon = HierarchyIcon.Camera, Depth = 1 });
        scene.Children.Add(new HierarchyNode { Name = "Directional Light", Icon = HierarchyIcon.Light, Depth = 1 });

        var sceneObjects = new HierarchyNode { Name = "Scene Objects", Icon = HierarchyIcon.Folder, Depth = 1 };
        sceneObjects.Children.Add(new HierarchyNode { Name = "Cube", Icon = HierarchyIcon.Mesh, Depth = 2, IsSelected = true });
        sceneObjects.Children.Add(new HierarchyNode { Name = "Sphere", Icon = HierarchyIcon.Mesh, Depth = 2 });
        scene.Children.Add(sceneObjects);

        var props = new HierarchyNode { Name = "Props", Icon = HierarchyIcon.Folder, Depth = 1 };
        props.Children.Add(new HierarchyNode { Name = "Pillar A", Icon = HierarchyIcon.Mesh, Depth = 2 });
        props.Children.Add(new HierarchyNode { Name = "Box B", Icon = HierarchyIcon.Mesh, Depth = 2 });
        scene.Children.Add(props);

        Hierarchy.Add(scene);
    }

    private void BuildAssets()
    {
        var scenes = new AssetNode { Name = "Scenes", Kind = AssetKind.Folder, Depth = 0 };
        scenes.Children.Add(new AssetNode { Name = "SampleScene", Kind = AssetKind.Scene, Depth = 1 });

        var meshes = new AssetNode { Name = "Meshes", Kind = AssetKind.Folder, Depth = 0 };
        meshes.Children.Add(new AssetNode { Name = "Cube.fbx", Kind = AssetKind.Mesh, Depth = 1 });
        meshes.Children.Add(new AssetNode { Name = "Sphere.fbx", Kind = AssetKind.Mesh, Depth = 1 });
        meshes.Children.Add(new AssetNode { Name = "Pillar.fbx", Kind = AssetKind.Mesh, Depth = 1 });

        var textures = new AssetNode { Name = "Textures", Kind = AssetKind.Folder, Depth = 0, IsExpanded = false };

        Assets.Add(scenes);
        Assets.Add(meshes);
        Assets.Add(textures);
    }

    private void BuildConsole()
    {
        ConsoleEntries.Add(new ConsoleEntry { Level = ConsoleLevel.Info, Message = "Scene loaded successfully", Timestamp = "00:00:01" });
        ConsoleEntries.Add(new ConsoleEntry { Level = ConsoleLevel.Info, Message = "Shader compiled: PBR/Standard", Timestamp = "00:00:01" });
        ConsoleEntries.Add(new ConsoleEntry { Level = ConsoleLevel.Warning, Message = "Mesh 'Pillar A' has no collider component", Timestamp = "00:00:02" });
        ConsoleEntries.Add(new ConsoleEntry { Level = ConsoleLevel.Warning, Message = "Directional Light intensity clamped to [0, 8]", Timestamp = "00:00:02" });
        ConsoleEntries.Add(new ConsoleEntry { Level = ConsoleLevel.Error, Message = "NullReferenceException: Object reference not set", Source = "PlayerController.cs:201" });

        foreach (var entry in ConsoleEntries)
        {
            switch (entry.Level)
            {
                case ConsoleLevel.Info: ConsoleInfoCount++; break;
                case ConsoleLevel.Warning: ConsoleWarningCount++; break;
                case ConsoleLevel.Error: ConsoleErrorCount++; break;
            }
        }
    }
}
