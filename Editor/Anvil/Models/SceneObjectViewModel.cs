using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.Models;

public enum SceneObjectType
{
    Empty,
    Mesh,
    Light,
    Camera,
    Group,
}

public enum LightType
{
    Directional,
    Point,
    Spot,
}

public partial class MaterialInfo : ObservableObject
{
    [ObservableProperty] private Color _color = Color.Parse("#3D5AFA");
    [ObservableProperty] private double _roughness = 0.4;
    [ObservableProperty] private double _metallic = 0.1;
    [ObservableProperty] private double _opacity = 1.0;

    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    partial void OnColorChanged(Color value) => OnPropertyChanged(nameof(ColorHex));
}

public partial class LightInfo : ObservableObject
{
    [ObservableProperty] private LightType _type = LightType.Directional;
    [ObservableProperty] private Color _color = Color.Parse("#FFF7D6");
    [ObservableProperty] private double _intensity = 1.0;
}

public partial class CameraInfo : ObservableObject
{
    [ObservableProperty] private double _fov = 60;
    [ObservableProperty] private double _near = 0.1;
    [ObservableProperty] private double _far = 1000;
}

public partial class SceneObjectViewModel : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private SceneObjectType _type = SceneObjectType.Mesh;
    [ObservableProperty] private bool _visible = true;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private string _tag = "Untagged";
    [ObservableProperty] private string _layer = "Default";

    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private double _positionZ;

    [ObservableProperty] private double _rotationX;
    [ObservableProperty] private double _rotationY;
    [ObservableProperty] private double _rotationZ;

    [ObservableProperty] private double _scaleX = 1;
    [ObservableProperty] private double _scaleY = 1;
    [ObservableProperty] private double _scaleZ = 1;

    [ObservableProperty] private MaterialInfo? _material;
    [ObservableProperty] private LightInfo? _light;
    [ObservableProperty] private CameraInfo? _camera;

    public ObservableCollection<SceneObjectViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;
}
