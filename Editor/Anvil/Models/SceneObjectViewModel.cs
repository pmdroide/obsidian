using System.Collections.ObjectModel;
using Anvil.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Editor;
using Engine.Entities;
using Color = Avalonia.Media.Color;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

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
    [ObservableProperty] private double _emissiveStrength;
    [ObservableProperty] private bool _isTransparent;
    [ObservableProperty] private int _materialType;

    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    // Parent back-pointer so setters can find the EngineId + bridge to enqueue against.
    private SceneObjectViewModel? _parent;
    internal void AttachToParent(SceneObjectViewModel parent) => _parent = parent;

    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(ColorHex));
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
        {
            var v = BridgeReconciler.ToVector3(value);
            bridge.EnqueueMutateMaterial(id, mat => mat.DiffuseColor = v);
        }
    }

    partial void OnRoughnessChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
            bridge.EnqueueMutateMaterial(id, mat => mat.Roughness = (float)value);
    }

    partial void OnMetallicChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
            bridge.EnqueueMutateMaterial(id, mat => mat.Metallic = (float)value);
    }

    partial void OnEmissiveStrengthChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
            bridge.EnqueueMutateMaterial(id, mat => mat.EmissiveStrength = (float)value);
    }

    partial void OnIsTransparentChanged(bool value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
            bridge.EnqueueMutateMaterial(id, mat => mat.IsTransparent = value);
    }

    partial void OnMaterialTypeChanged(int value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
            bridge.EnqueueMutateMaterial(id, mat => mat.Type = (Engine.Recources.MaterialEffect.MaterialTypes)value);
    }
}

public partial class LightInfo : ObservableObject
{
    [ObservableProperty] private LightType _type = LightType.Directional;
    [ObservableProperty] private Color _color = Color.Parse("#FFF7D6");
    [ObservableProperty] private double _intensity = 1.0;
    [ObservableProperty] private double _radius = 10.0;
    [ObservableProperty] private bool _castShadows;

    private SceneObjectViewModel? _parent;
    internal void AttachToParent(SceneObjectViewModel parent) => _parent = parent;

    partial void OnColorChanged(Color value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
        {
            XnaColor c = BridgeReconciler.ToXnaColor(value);
            bridge.EnqueueMutate(id, obj =>
            {
                if (obj is PointLight pl) pl.Color = c;
                else if (obj is DirectionalLight dl) dl.Color = c;
            });
        }
    }

    partial void OnIntensityChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
        {
            float f = (float)value;
            bridge.EnqueueMutate(id, obj =>
            {
                if (obj is PointLight pl) pl.Intensity = f;
                else if (obj is DirectionalLight dl) dl.Intensity = f;
            });
        }
    }

    partial void OnRadiusChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
        {
            float f = (float)value;
            bridge.EnqueueMutate(id, obj =>
            {
                if (obj is PointLight pl) pl.Radius = f;
            });
        }
    }

    partial void OnCastShadowsChanged(bool value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
        {
            bridge.EnqueueMutate(id, obj =>
            {
                // DirectionalLight.CastShadows is readonly; only PointLight is settable at runtime.
                if (obj is PointLight pl) pl.CastShadows = value;
            });
        }
    }
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

    // -------- Engine binding state --------

    public int? EngineId { get; private set; }
    public EditorObjectKind Kind { get; private set; }
    public IEditorBridge? Bridge { get; private set; }

    /// <summary>
    /// When true, property setters skip dispatching back to the engine. The reconciler
    /// flips this around snapshot writes so engine→UI sync doesn't loop back to UI→engine.
    /// </summary>
    public bool SuppressPush { get; private set; }

    internal void AttachBridge(IEditorBridge bridge, int engineId, EditorObjectKind kind)
    {
        Bridge = bridge;
        EngineId = engineId;
        Kind = kind;
    }

    public void BeginSuppressPush() => SuppressPush = true;
    public void EndSuppressPush() => SuppressPush = false;

    // -------- Transform partials enqueueing into the bridge --------

    partial void OnPositionXChanged(double value) => PushPosition();
    partial void OnPositionYChanged(double value) => PushPosition();
    partial void OnPositionZChanged(double value) => PushPosition();

    partial void OnRotationXChanged(double value) => PushRotation();
    partial void OnRotationYChanged(double value) => PushRotation();
    partial void OnRotationZChanged(double value) => PushRotation();

    partial void OnScaleXChanged(double value) => PushScale();
    partial void OnScaleYChanged(double value) => PushScale();
    partial void OnScaleZChanged(double value) => PushScale();

    partial void OnNameChanged(string value)
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id) return;
        bridge.EnqueueMutate(id, obj => obj.Name = value);
    }

    private void PushPosition()
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id) return;
        var v = new XnaVector3((float)PositionX, (float)PositionY, (float)PositionZ);
        bridge.EnqueueMutate(id, obj => obj.Position = v);
    }

    private void PushRotation()
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id) return;
        var mat = RotationConversion.EulerToMatrix(RotationX, RotationY, RotationZ);
        bridge.EnqueueMutate(id, obj => obj.RotationMatrix = mat);
    }

    private void PushScale()
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id) return;
        var v = new XnaVector3((float)ScaleX, (float)ScaleY, (float)ScaleZ);
        bridge.EnqueueMutate(id, obj => obj.Scale = v);
    }
}
