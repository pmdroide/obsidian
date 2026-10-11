using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class RagdollComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _mass = 70;
    [ObservableProperty] private bool _activeOnStart;
    [ObservableProperty] private double _impactThreshold;
    [ObservableProperty] private double _jointFriction = 0.2;

    public RagdollComponentViewModel(SceneObjectViewModel owner) : base(owner, RagdollComponent.TypeId) { }

    partial void OnMassChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float mass = (float)Math.Max(value, RagdollComponent.MinMass);
        Push(c => ((RagdollComponent)c).Mass = mass);
    }

    partial void OnActiveOnStartChanged(bool value) => Push(c => ((RagdollComponent)c).ActiveOnStart = value);

    partial void OnImpactThresholdChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float threshold = (float)Math.Max(value, 0);
        Push(c => ((RagdollComponent)c).ImpactThreshold = threshold);
    }

    partial void OnJointFrictionChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float friction = (float)Math.Clamp(value, 0, 1);
        Push(c => ((RagdollComponent)c).JointFriction = friction);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var ragdoll = (RagdollComponent)component;
        if (Math.Abs(Mass - ragdoll.Mass) > 0.0001) Mass = ragdoll.Mass;
        ActiveOnStart = ragdoll.ActiveOnStart;
        if (Math.Abs(ImpactThreshold - ragdoll.ImpactThreshold) > 0.0001) ImpactThreshold = ragdoll.ImpactThreshold;
        if (Math.Abs(JointFriction - ragdoll.JointFriction) > 0.0001) JointFriction = ragdoll.JointFriction;
    }
}
