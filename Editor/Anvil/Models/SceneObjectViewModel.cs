using System;
using System.Collections.ObjectModel;
using System.Linq;
using Anvil.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Components;
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

public partial class MaterialInfo : ComponentViewModel
{
    [ObservableProperty] private Color _color = Color.Parse("#3D5AFA");
    [ObservableProperty] private double _roughness = 0.4;
    [ObservableProperty] private double _metallic = 0.1;
    [ObservableProperty] private double _opacity = 1.0;
    [ObservableProperty] private double _emissiveStrength;
    [ObservableProperty] private bool _isTransparent;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWater))] private int _materialType;
    [ObservableProperty] private bool _castShadows = true;
    [ObservableProperty] private double _waveScale = 0.3;
    [ObservableProperty] private double _waveSpeed = 1;
    [ObservableProperty] private double _waveStrength = 0.2;
    [ObservableProperty] private double _waveHeight = 0.5;
    [ObservableProperty] private double _clarity = 4;
    [ObservableProperty] private double _foam = 0.5;

    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
    public bool IsWater => MaterialType == (int)Engine.Recources.MaterialEffect.MaterialTypes.Water;

    public MaterialTextureSlotViewModel[] TextureSlots { get; }

    public MaterialInfo(SceneObjectViewModel owner) : base(owner, MaterialComponent.TypeId)
    {
        TextureSlots =
        [
            new("Base Color", c => c.BaseColorTexture, path => Push(c => ((MaterialComponent)c).BaseColorTexture = path)),
            new("Normal", c => c.NormalTexture, path => Push(c => ((MaterialComponent)c).NormalTexture = path)),
            new("Roughness", c => c.RoughnessTexture, path => Push(c => ((MaterialComponent)c).RoughnessTexture = path)),
            new("Metallic", c => c.MetallicTexture, path => Push(c => ((MaterialComponent)c).MetallicTexture = path)),
            new("Mask", c => c.MaskTexture, path => Push(c => ((MaterialComponent)c).MaskTexture = path)),
            new("Displacement", c => c.DisplacementTexture, path => Push(c => ((MaterialComponent)c).DisplacementTexture = path)),
        ];
    }

    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(ColorHex));
        var v = BridgeReconciler.ToVector3(value);
        Push(c =>
        {
            var material = (MaterialComponent)c;
            material.Red = v.X; material.Green = v.Y; material.Blue = v.Z;
        });
    }

    partial void OnRoughnessChanged(double value)
    {
        Push(c => ((MaterialComponent)c).Roughness = (float)value);
    }

    partial void OnMetallicChanged(double value)
    {
        Push(c => ((MaterialComponent)c).Metallic = (float)value);
    }

    partial void OnEmissiveStrengthChanged(double value)
    {
        Push(c => ((MaterialComponent)c).EmissiveStrength = (float)value);
    }

    partial void OnIsTransparentChanged(bool value)
    {
        Push(c => ((MaterialComponent)c).IsTransparent = value);
    }

    partial void OnMaterialTypeChanged(int value)
    {
        if (value < 0) return; // ComboBox clears SelectedIndex transiently while its template swaps
        Push(c => ((MaterialComponent)c).MaterialType = (Engine.Recources.MaterialEffect.MaterialTypes)value);
    }

    partial void OnOpacityChanged(double value) => Push(c => ((MaterialComponent)c).Opacity = (float)value);
    partial void OnCastShadowsChanged(bool value) => Push(c => ((MaterialComponent)c).CastShadows = value);
    partial void OnWaveScaleChanged(double value) => Push(c => ((MaterialComponent)c).WaveScale = (float)value);
    partial void OnWaveSpeedChanged(double value) => Push(c => ((MaterialComponent)c).WaveSpeed = (float)value);
    partial void OnWaveStrengthChanged(double value) => Push(c => ((MaterialComponent)c).WaveStrength = (float)value);
    partial void OnWaveHeightChanged(double value) => Push(c => ((MaterialComponent)c).WaveHeight = (float)value);
    partial void OnClarityChanged(double value) => Push(c => ((MaterialComponent)c).Clarity = (float)value);
    partial void OnFoamChanged(double value) => Push(c => ((MaterialComponent)c).Foam = (float)value);

    [RelayCommand]
    private void WaterExample()
    {
        MaterialType = (int)Engine.Recources.MaterialEffect.MaterialTypes.Water;
        Color = Color.FromRgb(13, 89, 115);
        Roughness = 0.08; Opacity = 0.65;
        WaveScale = 0.3; WaveSpeed = 1; WaveStrength = 0.2; WaveHeight = 0.5;
        Clarity = 4; Foam = 0.5;
        // The example is a whole water body: floating objects need the Water role too.
        Owner.Role = GameObjectRole.Water;
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var material = (MaterialComponent)component;
        Color = BridgeReconciler.FromVector3(new XnaVector3(material.Red, material.Green, material.Blue));
        Roughness = material.Roughness; Metallic = material.Metallic;
        EmissiveStrength = material.EmissiveStrength; IsTransparent = material.IsTransparent;
        MaterialType = (int)material.MaterialType; CastShadows = material.CastShadows;
        Opacity = material.Opacity; WaveScale = material.WaveScale;
        WaveSpeed = material.WaveSpeed; WaveStrength = material.WaveStrength;
        WaveHeight = material.WaveHeight;
        Clarity = material.Clarity; Foam = material.Foam;
        foreach (var slot in TextureSlots) slot.Apply(material);
    }
}

public partial class LightInfo : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRadius)), NotifyPropertyChangedFor(nameof(IsSpot))]
    private LightType _type = LightType.Directional;
    [ObservableProperty] private Color _color = Color.Parse("#FFF7D6");
    [ObservableProperty] private double _intensity = 1.0;
    [ObservableProperty] private double _radius = 10.0;
    [ObservableProperty] private bool _castShadows;
    // Spot lights only: full cone angles in degrees.
    [ObservableProperty] private double _spotAngle = 60.0;
    [ObservableProperty] private double _innerSpotAngle = 40.0;

    public bool HasRadius => Type != LightType.Directional;
    public bool IsSpot => Type == LightType.Spot;

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

    partial void OnSpotAngleChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge && !double.IsNaN(value))
        {
            float f = (float)value;
            bridge.EnqueueMutate(id, obj =>
            {
                if (obj is SpotLight sl) sl.SpotAngle = f;
            });
        }
    }

    partial void OnInnerSpotAngleChanged(double value)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge && !double.IsNaN(value))
        {
            float f = (float)value;
            bridge.EnqueueMutate(id, obj =>
            {
                if (obj is SpotLight sl) sl.InnerSpotAngle = f;
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

    public static GameObjectRole[] Roles { get; } = Enum.GetValues<GameObjectRole>();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWaterRole))]
    private GameObjectRole _role = GameObjectRole.Default;
    public bool IsWaterRole => Role == GameObjectRole.Water;

    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private double _positionZ;

    [ObservableProperty] private double _rotationX;
    [ObservableProperty] private double _rotationY;
    [ObservableProperty] private double _rotationZ;

    [ObservableProperty] private double _scaleX = 1;
    [ObservableProperty] private double _scaleY = 1;
    [ObservableProperty] private double _scaleZ = 1;

    // Shortcut to the Material component editor (null when the object has none).
    public MaterialInfo? Material => Components.OfType<MaterialInfo>().FirstOrDefault();
    [ObservableProperty] private LightInfo? _light;
    [ObservableProperty] private CameraInfo? _camera;

    public ObservableCollection<SceneObjectViewModel> Children { get; } = new();
    public ObservableCollection<ComponentViewModel> Components { get; } = new();
    public ObservableCollection<AddableComponentType> AddableComponents { get; } = new();
    // The Main Camera accepts Script Behaviours only (Engine.Entities.Camera.SupportsComponent).
    public bool CanAddComponents => EngineId.HasValue &&
        (Kind == EditorObjectKind.BasicEntity || Kind == EditorObjectKind.Camera);
    public bool HasRole => EngineId.HasValue && Kind == EditorObjectKind.BasicEntity;
    public bool HasMaterialComponent => Components.Any(c => c.TypeId == MaterialComponent.TypeId);

    public SceneObjectViewModel()
    {
        Components.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMaterialComponent));
            OnPropertyChanged(nameof(Material));
            foreach (var type in AddableComponents) type.AddCommand.NotifyCanExecuteChanged();
        };
    }

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
        foreach (var definition in ComponentRegistry.All)
            if (ComponentEditorRegistry.Supports(definition.Id) &&
                (kind != EditorObjectKind.Camera || Engine.Entities.Camera.SupportsComponent(definition.Id)))
                AddableComponents.Add(new AddableComponentType(definition, this));
        OnPropertyChanged(nameof(CanAddComponents));
        OnPropertyChanged(nameof(HasRole));
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

    partial void OnRoleChanged(GameObjectRole value)
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id || !Enum.IsDefined(value)) return;
        bridge.EnqueueSetRole(id, value);
    }

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
