using System;
using System.Collections.Generic;
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
    // A skinned gameobject's Armature node and its bones (Engine.Entities.BoneObject).
    Armature,
    Bone,
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWater)), NotifyPropertyChangedFor(nameof(IsWetSurface))] private int _materialType;
    [ObservableProperty] private bool _castShadows = true;
    [ObservableProperty] private double _waveScale = 0.3;
    [ObservableProperty] private double _waveSpeed = 1;
    [ObservableProperty] private double _waveStrength = 0.2;
    [ObservableProperty] private double _waveHeight = 0.5;
    [ObservableProperty] private double _clarity = 4;
    [ObservableProperty] private double _foam = 0.5;
    [ObservableProperty] private double _wetness;
    [ObservableProperty] private double _puddleAmount = 0.5;
    [ObservableProperty] private double _puddleSize = 3;
    [ObservableProperty] private bool _weatherWetness = true;
    [ObservableProperty] private double _tilingU = 1;
    [ObservableProperty] private double _tilingV = 1;

    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
    public bool IsWater => MaterialType == (int)Engine.Recources.MaterialEffect.MaterialTypes.Water;
    public bool IsWetSurface => MaterialType == (int)Engine.Recources.MaterialEffect.MaterialTypes.WetSurface;

    public MaterialTextureSlotViewModel[] TextureSlots { get; }

    // -------- Material asset link (MaterialComponent.MaterialPath, Engine.Recources.MaterialAsset) --------

    // Content-relative .mat this component shows ("Materials/Brick.mat"), or null for its own settings.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLinked), nameof(IsNotLinked), nameof(MaterialAssetLabel),
        nameof(IsMaterialAssetMissing), nameof(MaterialAssetTip))]
    private string? _materialPath;

    public bool IsLinked => !string.IsNullOrEmpty(MaterialPath);
    public bool IsNotLinked => !IsLinked;
    public string MaterialAssetLabel => IsLinked ? System.IO.Path.GetFileNameWithoutExtension(MaterialPath)! : "None (own settings)";
    public bool IsMaterialAssetMissing => IsLinked && !Engine.Recources.MaterialAsset.Exists(MaterialPath);
    public string MaterialAssetTip => !IsLinked
        ? "This gameobject's own surface. Pick or drop a material asset (.mat) from Assets to share one, or Save as Asset."
        : IsMaterialAssetMissing ? $"{MaterialPath} doesn't exist any more. The gameobject keeps its last values; Unlink or pick another material."
        : $"{MaterialPath}: the settings below edit this asset, and every gameobject using it follows.";

    /// <summary>Links this gameobject to a material asset (Content-relative .mat path); adds nothing else.</summary>
    public void AssignAsset(string? path)
    {
        if (!Engine.Recources.MaterialAsset.IsMaterialAsset(path)) return;
        // Not through the asset-saving Push: the link takes the asset's values, it doesn't overwrite them.
        base.Push(c => ((MaterialComponent)c).Link(path));
    }

    /// <summary>Keeps the current values but stops following the asset.</summary>
    [RelayCommand]
    private void Unlink() => base.Push(c => ((MaterialComponent)c).MaterialPath = null);

    // A linked component's edits are the asset's: write them into its file so every gameobject using it follows.
    protected override void Push(Action<GameComponent> mutate)
    {
        if (!IsLinked) { base.Push(mutate); return; }
        base.Push(c =>
        {
            mutate(c);
            Engine.Recources.MaterialAsset.SaveFrom((MaterialComponent)c);
        });
    }

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
            new("Ambient Occlusion", c => c.AmbientOcclusionTexture, path => Push(c => ((MaterialComponent)c).AmbientOcclusionTexture = path)),
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
    partial void OnWetnessChanged(double value) => Push(c => ((MaterialComponent)c).Wetness = (float)value);
    partial void OnPuddleAmountChanged(double value) => Push(c => ((MaterialComponent)c).PuddleAmount = (float)value);
    partial void OnPuddleSizeChanged(double value) => Push(c => ((MaterialComponent)c).PuddleSize = (float)value);
    partial void OnWeatherWetnessChanged(bool value) => Push(c => ((MaterialComponent)c).WeatherWetness = value);
    partial void OnTilingUChanged(double value) => Push(c => ((MaterialComponent)c).TilingU = (float)value);
    partial void OnTilingVChanged(double value) => Push(c => ((MaterialComponent)c).TilingV = (float)value);

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

    [RelayCommand]
    private void WetFloorExample()
    {
        MaterialType = (int)Engine.Recources.MaterialEffect.MaterialTypes.WetSurface;
        Wetness = 1; PuddleAmount = 0.5; PuddleSize = 3; WeatherWetness = true;
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        var material = (MaterialComponent)component;
        if (MaterialPath != material.MaterialPath) MaterialPath = material.MaterialPath;
        if (freezeFields) return;
        Color = BridgeReconciler.FromVector3(new XnaVector3(material.Red, material.Green, material.Blue));
        Roughness = material.Roughness; Metallic = material.Metallic;
        EmissiveStrength = material.EmissiveStrength; IsTransparent = material.IsTransparent;
        MaterialType = (int)material.MaterialType; CastShadows = material.CastShadows;
        Opacity = material.Opacity; WaveScale = material.WaveScale;
        WaveSpeed = material.WaveSpeed; WaveStrength = material.WaveStrength;
        WaveHeight = material.WaveHeight;
        Clarity = material.Clarity; Foam = material.Foam;
        Wetness = material.Wetness; PuddleAmount = material.PuddleAmount;
        PuddleSize = material.PuddleSize; WeatherWetness = material.WeatherWetness;
        TilingU = material.TilingU; TilingV = material.TilingV;
        foreach (var slot in TextureSlots) slot.Apply(material);
    }
}

public partial class LightInfo : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRadius)), NotifyPropertyChangedFor(nameof(IsSpot)), NotifyPropertyChangedFor(nameof(IsDirectional))]
    private LightType _type = LightType.Directional;
    [ObservableProperty] private Color _color = Color.Parse("#FFF7D6");
    [ObservableProperty] private double _intensity = 1.0;
    [ObservableProperty] private double _radius = 10.0;
    [ObservableProperty] private bool _castShadows;
    [ObservableProperty] private double _shadowResolution = 512;
    [ObservableProperty] private bool _staticShadows;
    // Point and spot lights: the glowing sphere / tube the light comes from (Source 2 light source size), metres.
    [ObservableProperty] private double _sourceRadius;
    [ObservableProperty] private double _sourceLength;
    // Spot lights: cookie (Content texture path), empty = none.
    [ObservableProperty] private string _cookiePath = "";
    // Directional lights: cascaded shadows.
    [ObservableProperty] private double _cascadeCount = 4;
    [ObservableProperty] private double _shadowDistance = 150;
    [ObservableProperty] private double _splitLambda = 0.75;
    public bool IsDirectional => Type == LightType.Directional;
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
        Mutate(obj =>
        {
            if (obj is PointLight pl) pl.CastShadows = value;
            else if (obj is DirectionalLight dl) dl.CastShadows = value;
        });
    }

    private void Mutate(System.Action<Engine.Entities.TransformableObject> change)
    {
        if (_parent is { SuppressPush: false } p && p.EngineId is int id && p.Bridge is { } bridge)
            bridge.EnqueueMutate(id, change);
    }

    partial void OnShadowResolutionChanged(double value)
    {
        int v = (int)value;
        Mutate(obj =>
        {
            if (obj is PointLight pl) pl.ShadowResolution = v;
            else if (obj is DirectionalLight dl) dl.ShadowResolution = v;
        });
    }
    partial void OnStaticShadowsChanged(bool value) => Mutate(obj => { if (obj is PointLight pl) { pl.StaticShadows = value; pl.HasChanged = true; } });
    partial void OnSourceRadiusChanged(double value) => Mutate(obj => { if (obj is PointLight pl) pl.SourceRadius = (float)value; });
    partial void OnSourceLengthChanged(double value) => Mutate(obj => { if (obj is PointLight pl) pl.SourceLength = (float)value; });
    partial void OnCookiePathChanged(string value)
    {
        string path = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Mutate(obj => { if (obj is SpotLight sl) sl.CookiePath = path; });
    }
    partial void OnCascadeCountChanged(double value) => Mutate(obj => { if (obj is DirectionalLight dl) dl.CascadeCount = (int)value; });
    partial void OnShadowDistanceChanged(double value) => Mutate(obj => { if (obj is DirectionalLight dl) dl.ShadowDistance = (float)value; });
    partial void OnSplitLambdaChanged(double value) => Mutate(obj => { if (obj is DirectionalLight dl) dl.SplitLambda = (float)value; });
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
    [ObservableProperty] private string _roleName = "Default";
    public ObservableCollection<string> AvailableTags { get; } = new();
    public ObservableCollection<string> AvailableLayers { get; } = new();
    public ObservableCollection<string> AvailableRoles { get; } = new();
    private IReadOnlyList<Engine.Recources.ClassificationDefinition>? _classificationDefinitions;

    public void ApplyClassifications(IReadOnlyList<Engine.Recources.ClassificationDefinition> definitions)
    {
        if (ReferenceEquals(_classificationDefinitions, definitions)) return;
        _classificationDefinitions = definitions;
        Replace(AvailableTags, Engine.Recources.ClassificationKind.Tag);
        Replace(AvailableLayers, Engine.Recources.ClassificationKind.Layer);
        Replace(AvailableRoles, Engine.Recources.ClassificationKind.Role);
        void Replace(ObservableCollection<string> names, Engine.Recources.ClassificationKind kind)
        {
            var next = definitions.Where(e => e.Kind == kind).Select(e => e.Name).ToArray();
            if (names.SequenceEqual(next)) return;
            names.Clear();
            foreach (string name in next) names.Add(name);
        }
    }

    public static GameObjectRole[] Roles { get; } = Enum.GetValues<GameObjectRole>();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWaterRole))]
    private GameObjectRole _role = GameObjectRole.Default;
    public bool IsWaterRole => Role == GameObjectRole.Water;
    // Kept across scene loads during Play (BasicEntity.Persistent).
    [ObservableProperty] private bool _persistent;

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
    public bool IsDecal => EngineId.HasValue && Kind == EditorObjectKind.Decal;
    // A bone or the Armature of a skinned gameobject: only rotates (position follows the skeleton), can't be
    // renamed, hidden or deleted.
    public bool IsBone => EngineId.HasValue && Kind == EditorObjectKind.Bone;
    public bool IsNotBone => !IsBone;
    // Bone only: rotated away from its rest pose.
    [ObservableProperty] private bool _isPosed;

    // Decal only: Content-relative texture path; null projects the built-in scorch mark (Assets.DefaultDecal).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DecalTextureLabel))]
    private string? _decalTexture;
    public string DecalTextureLabel => string.IsNullOrEmpty(DecalTexture) ? "Default (scorch mark)" : DecalTexture;

    public bool HasMaterialComponent => Components.Any(c => c.TypeId == MaterialComponent.TypeId);

    // -------- Prefab instance (BasicEntity.PrefabPath, Engine.Logic.PrefabUtility) --------

    // Content-relative .prefab this gameobject is an instance of; null for a plain gameobject.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPrefabInstance), nameof(IsNotPrefabInstance), nameof(PrefabName), nameof(PrefabStatus))]
    private string? _prefabPath;
    // What the instance overrides (engine override paths); only sent for the selected gameobject, null otherwise.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PrefabStatus), nameof(HasPrefabOverrides), nameof(PrefabOverridesTip))]
    private IReadOnlyList<string>? _prefabOverrides;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PrefabStatus), nameof(PrefabOverridesTip))]
    private bool _prefabMissing;

    public bool IsPrefabInstance => HasRole && !string.IsNullOrEmpty(PrefabPath);
    public bool IsNotPrefabInstance => HasRole && string.IsNullOrEmpty(PrefabPath);
    public string PrefabName => string.IsNullOrEmpty(PrefabPath) ? "" : System.IO.Path.GetFileNameWithoutExtension(PrefabPath);
    public bool HasPrefabOverrides => PrefabOverrides is { Count: > 0 };
    public string PrefabStatus => PrefabMissing ? "Prefab file missing"
        : PrefabOverrides == null ? ""
        : PrefabOverrides.Count == 0 ? "No overrides"
        : PrefabOverrides.Count == 1 ? "1 override" : $"{PrefabOverrides.Count} overrides";
    public string PrefabOverridesTip => PrefabMissing
        ? $"{PrefabPath} doesn't exist any more. The instance keeps its values; Unpack it or create a new prefab."
        : PrefabOverrides is { Count: > 0 } list
            ? "Differs from the prefab:\n" + string.Join("\n", list.Select(p => "• " + Engine.Logic.PrefabUtility.Describe(p)))
            : "Matches the prefab.";

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
        OnPropertyChanged(nameof(IsDecal));
        OnPropertyChanged(nameof(IsBone));
        OnPropertyChanged(nameof(IsNotBone));
        OnPropertyChanged(nameof(IsPrefabInstance));
        OnPropertyChanged(nameof(IsNotPrefabInstance));
    }

    /// <summary>Use a Content texture (Inspector Browse or a drop from Assets) on this decal.</summary>
    public void AssignDecalTexture(string path)
    {
        if (MaterialComponent.IsTextureAsset(path)) DecalTexture = path.Replace('\\', '/');
    }

    [RelayCommand] private void ResetDecalTexture() => DecalTexture = null;

    /// <summary>Bone: back to its rest pose.</summary>
    [RelayCommand]
    private void ResetBone()
    {
        if (Bridge is { } bridge && EngineId is int id && IsBone)
            bridge.EnqueueMutate(id, obj => (obj as BoneObject)?.ResetBone());
    }

    /// <summary>Bone: every bone of its gameobject back to the rest pose.</summary>
    [RelayCommand]
    private void ResetPose()
    {
        if (Bridge is { } bridge && EngineId is int id && IsBone)
            bridge.EnqueueMutate(id, obj => (obj as BoneObject)?.ResetPose());
    }

    partial void OnDecalTextureChanged(string? value)
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id || Kind != EditorObjectKind.Decal) return;
        bridge.EnqueueSetDecalTexture(id, value);
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
        _roleName = value.ToString();
        OnPropertyChanged(nameof(RoleName));
        bridge.EnqueueSetRole(id, value);
    }

    partial void OnTagChanged(string value) => PushClassification(Engine.Recources.ClassificationKind.Tag, value);
    partial void OnLayerChanged(string value) => PushClassification(Engine.Recources.ClassificationKind.Layer, value);
    partial void OnRoleNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        bool previous = SuppressPush;
        SuppressPush = true;
        try { Role = Enum.TryParse<GameObjectRole>(value, out var role) && role.ToString() == value ? role : GameObjectRole.Default; }
        finally { SuppressPush = previous; }
        PushClassification(Engine.Recources.ClassificationKind.Role, value);
    }

    private void PushClassification(Engine.Recources.ClassificationKind kind, string name)
    {
        if (SuppressPush || !HasRole || Bridge is not { } bridge || EngineId is not int id || string.IsNullOrWhiteSpace(name)) return;
        var definition = bridge.Classifications.FirstOrDefault(e => e.Kind == kind && e.Name == name);
        if (definition != null) bridge.EnqueueSetClassification(id, kind, definition.Id);
    }

    partial void OnPersistentChanged(bool value)
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id) return;
        bridge.EnqueueSetPersistent(id, value);
    }

    partial void OnNameChanged(string value)
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id || IsBone) return;
        bridge.EnqueueMutate(id, obj => obj.Name = value);
    }

    private void PushPosition()
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id || IsBone) return;
        var v = new XnaVector3((float)PositionX, (float)PositionY, (float)PositionZ);
        bridge.EnqueueMutate(id, obj => obj.Position = v);
    }

    private void PushRotation()
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id) return;
        var mat = RotationConversion.EulerToMatrix(RotationX, RotationY, RotationZ);
        // A bone's Rotation is its turn away from the rest pose, not its world rotation.
        if (IsBone) bridge.EnqueueMutate(id, obj => { if (obj is BoneObject bone) bone.LocalRotation = mat; });
        else bridge.EnqueueMutate(id, obj => obj.RotationMatrix = mat);
    }

    private void PushScale()
    {
        if (SuppressPush || Bridge is not { } bridge || EngineId is not int id || IsBone) return;
        var v = new XnaVector3((float)ScaleX, (float)ScaleY, (float)ScaleZ);
        bridge.EnqueueMutate(id, obj => obj.Scale = v);
    }
}
