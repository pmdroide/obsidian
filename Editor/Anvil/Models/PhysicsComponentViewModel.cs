using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;
using Engine.Physics;
using Engine.Recources;

namespace Anvil.Models;

public partial class PhysicsComponentViewModel : ComponentViewModel
{
    // None is "remove the component", so the picker only offers real bodies.
    public static PhysicsBodyType[] BodyTypes { get; } = [PhysicsBodyType.Static, PhysicsBodyType.Dynamic];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsStatic), nameof(IsDynamic))]
    private PhysicsBodyType _bodyType = PhysicsBodyType.Dynamic;
    [ObservableProperty] private double _mass = 1;
    [ObservableProperty] private bool _buoyancy;
    [ObservableProperty] private double _buoyancyStrength = 2;
    [ObservableProperty] private double _waterDrag = 3;
    [ObservableProperty] private bool _isTrigger;
    [ObservableProperty] private bool _freezeRotation;
    // Model units (before Scale); the engine fills them with the model's bounds.
    [ObservableProperty] private double _colliderSizeX;
    [ObservableProperty] private double _colliderSizeY;
    [ObservableProperty] private double _colliderSizeZ;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCustomSound), nameof(SoundSummary))]
    private SoundMaterial _soundMaterial = SoundMaterial.Default;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SoundSummary))] private double _soundBlock = 0.6;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SoundSummary))] private double _soundDullness = 0.5;

    public static SoundMaterial[] SoundMaterials { get; } = Enum.GetValues<SoundMaterial>();
    public bool IsCustomSound => SoundMaterial == SoundMaterial.Custom;

    /// <summary>What the chosen material does, e.g. "Stops 90 % of sound, takes 85 % of the treble".</summary>
    public string SoundSummary
    {
        get
        {
            var p = IsCustomSound ? new SoundMaterialProperties((float)SoundBlock, (float)SoundDullness)
                : AudioOcclusion.PropertiesOf(SoundMaterial, Audio.WallOcclusion);
            return $"Stops {p.Block * 100:0} % of sound passing through, takes {p.Dullness * 100:0} % of its treble.";
        }
    }

    public bool IsStatic => BodyType == PhysicsBodyType.Static;
    public bool IsDynamic => BodyType == PhysicsBodyType.Dynamic;

    public PhysicsComponentViewModel(SceneObjectViewModel owner) : base(owner, PhysicsComponent.TypeId) { }

    partial void OnBodyTypeChanged(PhysicsBodyType value)
    {
        if (value == PhysicsBodyType.None) return;
        Push(c => ((PhysicsComponent)c).BodyType = value);
    }

    partial void OnMassChanged(double value)
    {
        float mass = (float)Math.Max(value, PhysicsComponent.MinMass);
        Push(c => ((PhysicsComponent)c).Mass = mass);
    }

    partial void OnBuoyancyChanged(bool value) => Push(c => ((PhysicsComponent)c).Buoyancy = value);

    partial void OnBuoyancyStrengthChanged(double value)
    {
        float strength = (float)Math.Max(value, 0);
        Push(c => ((PhysicsComponent)c).BuoyancyStrength = strength);
    }

    partial void OnWaterDragChanged(double value)
    {
        float drag = (float)Math.Max(value, 0);
        Push(c => ((PhysicsComponent)c).WaterDrag = drag);
    }

    partial void OnSoundMaterialChanged(SoundMaterial value) => Push(c => ((PhysicsComponent)c).SoundMaterial = value);

    partial void OnSoundBlockChanged(double value)
    {
        if (double.IsFinite(value)) Push(c => ((PhysicsComponent)c).SoundBlock = (float)Math.Clamp(value, 0, 1));
    }

    partial void OnSoundDullnessChanged(double value)
    {
        if (double.IsFinite(value)) Push(c => ((PhysicsComponent)c).SoundDullness = (float)Math.Clamp(value, 0, 1));
    }

    partial void OnIsTriggerChanged(bool value) => Push(c => ((PhysicsComponent)c).IsTrigger = value);
    partial void OnFreezeRotationChanged(bool value) => Push(c => ((PhysicsComponent)c).FreezeRotation = value);

    // Emptied or zero boxes would mean "use the model's bounds": only the Default Size button resets.
    private static bool IsSize(double value) => double.IsFinite(value) && value > 0;

    partial void OnColliderSizeXChanged(double value)
    {
        if (IsSize(value)) Push(c => ((PhysicsComponent)c).ColliderSizeX = (float)value);
    }

    partial void OnColliderSizeYChanged(double value)
    {
        if (IsSize(value)) Push(c => ((PhysicsComponent)c).ColliderSizeY = (float)value);
    }

    partial void OnColliderSizeZChanged(double value)
    {
        if (IsSize(value)) Push(c => ((PhysicsComponent)c).ColliderSizeZ = (float)value);
    }

    /// <summary>Fits the collider to the model's bounds again.</summary>
    [RelayCommand]
    private void ResetColliderSize() => Push(c => ((PhysicsComponent)c).ResetColliderSize());

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var physics = (PhysicsComponent)component;
        BodyType = physics.BodyType;
        if (Math.Abs(Mass - physics.Mass) > 0.0001) Mass = physics.Mass;
        Buoyancy = physics.Buoyancy;
        if (Math.Abs(BuoyancyStrength - physics.BuoyancyStrength) > 0.0001) BuoyancyStrength = physics.BuoyancyStrength;
        if (Math.Abs(WaterDrag - physics.WaterDrag) > 0.0001) WaterDrag = physics.WaterDrag;
        IsTrigger = physics.IsTrigger;
        FreezeRotation = physics.FreezeRotation;
        if (Math.Abs(ColliderSizeX - physics.ColliderSizeX) > 0.0001) ColliderSizeX = physics.ColliderSizeX;
        if (Math.Abs(ColliderSizeY - physics.ColliderSizeY) > 0.0001) ColliderSizeY = physics.ColliderSizeY;
        if (Math.Abs(ColliderSizeZ - physics.ColliderSizeZ) > 0.0001) ColliderSizeZ = physics.ColliderSizeZ;
        SoundMaterial = physics.SoundMaterial;
        if (Math.Abs(SoundBlock - physics.SoundBlock) > 0.0001) SoundBlock = physics.SoundBlock;
        if (Math.Abs(SoundDullness - physics.SoundDullness) > 0.0001) SoundDullness = physics.SoundDullness;
    }
}
