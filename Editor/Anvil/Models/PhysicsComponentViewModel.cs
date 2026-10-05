using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Physics;

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
    }
}
