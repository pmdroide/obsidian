using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Physics;

namespace Anvil.Models;

public partial class ClothComponentViewModel : ComponentViewModel
{
    public static ClothPinMode[] PinModes { get; } = Enum.GetValues<ClothPinMode>();

    [ObservableProperty] private ClothPinMode _pin = ClothPinMode.TopEdge;
    [ObservableProperty] private double _mass = 1;
    [ObservableProperty] private double _stiffness = 0.95;
    [ObservableProperty] private double _bend = 0.3;
    [ObservableProperty] private double _damping = 0.1;
    [ObservableProperty] private double _friction = 0.5;
    [ObservableProperty] private double _thickness = 0.03;
    [ObservableProperty] private double _wind = 1;
    [ObservableProperty] private bool _collisions = true;

    public ClothComponentViewModel(SceneObjectViewModel owner) : base(owner, ClothComponent.TypeId) { }

    partial void OnPinChanged(ClothPinMode value)
    {
        if (!Enum.IsDefined(value)) return;
        Push(c => ((ClothComponent)c).Pin = value);
    }

    partial void OnMassChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float mass = (float)Math.Max(value, ClothComponent.MinMass);
        Push(c => ((ClothComponent)c).Mass = mass);
    }

    partial void OnStiffnessChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float stiffness = (float)Math.Clamp(value, 0, 1);
        Push(c => ((ClothComponent)c).Stiffness = stiffness);
    }

    partial void OnBendChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float bend = (float)Math.Clamp(value, 0, 1);
        Push(c => ((ClothComponent)c).Bend = bend);
    }

    partial void OnDampingChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float damping = (float)Math.Clamp(value, 0, 1);
        Push(c => ((ClothComponent)c).Damping = damping);
    }

    partial void OnFrictionChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float friction = (float)Math.Clamp(value, 0, 2);
        Push(c => ((ClothComponent)c).Friction = friction);
    }

    partial void OnThicknessChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float thickness = (float)Math.Clamp(value, 0, 0.5);
        Push(c => ((ClothComponent)c).Thickness = thickness);
    }

    partial void OnWindChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float wind = (float)Math.Clamp(value, 0, 10);
        Push(c => ((ClothComponent)c).Wind = wind);
    }

    partial void OnCollisionsChanged(bool value) => Push(c => ((ClothComponent)c).Collisions = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var cloth = (ClothComponent)component;
        Pin = cloth.Pin;
        if (Math.Abs(Mass - cloth.Mass) > 0.0001) Mass = cloth.Mass;
        if (Math.Abs(Stiffness - cloth.Stiffness) > 0.0001) Stiffness = cloth.Stiffness;
        if (Math.Abs(Bend - cloth.Bend) > 0.0001) Bend = cloth.Bend;
        if (Math.Abs(Damping - cloth.Damping) > 0.0001) Damping = cloth.Damping;
        if (Math.Abs(Friction - cloth.Friction) > 0.0001) Friction = cloth.Friction;
        if (Math.Abs(Thickness - cloth.Thickness) > 0.0001) Thickness = cloth.Thickness;
        if (Math.Abs(Wind - cloth.Wind) > 0.0001) Wind = cloth.Wind;
        Collisions = cloth.Collisions;
    }
}
