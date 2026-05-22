using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.Models;

public partial class InspectorState : ObservableObject
{
    [ObservableProperty] private string _name = "Cube";
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

    [ObservableProperty] private Color _materialColor = Color.Parse("#3d5afa");
    [ObservableProperty] private double _roughness = 0.5;

    public string MaterialColorHex => $"#{MaterialColor.R:x2}{MaterialColor.G:x2}{MaterialColor.B:x2}";

    partial void OnMaterialColorChanged(Color value)
    {
        OnPropertyChanged(nameof(MaterialColorHex));
    }
}
