using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class BreakableGlassComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _crackSize = 0.381;
    [ObservableProperty] private double _holeRadius = 0.06;
    [ObservableProperty] private double _impactSpeed = 4;
    [ObservableProperty] private double _impactMomentum = 2;
    [ObservableProperty] private double _debrisLifetime = 15;
    [ObservableProperty] private double _hitTolerance = 2;
    [ObservableProperty] private bool _dropLooseShards = true;
    [ObservableProperty] private bool _distort = true;
    [ObservableProperty] private bool _solidShards = true;

    public BreakableGlassComponentViewModel(SceneObjectViewModel owner) : base(owner, BreakableGlassComponent.TypeId) { }

    partial void OnCrackSizeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float size = (float)Math.Clamp(value, 0.05, 10);
        Push(c => ((BreakableGlassComponent)c).CrackSize = size);
    }

    partial void OnHoleRadiusChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float radius = (float)Math.Clamp(value, 0.01, 2);
        Push(c => ((BreakableGlassComponent)c).HoleRadius = radius);
    }

    partial void OnImpactSpeedChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float speed = (float)Math.Max(value, 0);
        Push(c => ((BreakableGlassComponent)c).ImpactSpeed = speed);
    }

    partial void OnImpactMomentumChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float momentum = (float)Math.Max(value, 0);
        Push(c => ((BreakableGlassComponent)c).ImpactMomentum = momentum);
    }

    partial void OnDebrisLifetimeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float lifetime = (float)Math.Clamp(value, 0.1, 600);
        Push(c => ((BreakableGlassComponent)c).DebrisLifetime = lifetime);
    }

    partial void OnHitToleranceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float tolerance = (float)Math.Clamp(value, 1, 5);
        Push(c => ((BreakableGlassComponent)c).HitTolerance = tolerance);
    }

    partial void OnDropLooseShardsChanged(bool value) => Push(c => ((BreakableGlassComponent)c).DropLooseShards = value);
    partial void OnDistortChanged(bool value) => Push(c => ((BreakableGlassComponent)c).Distort = value);
    partial void OnSolidShardsChanged(bool value) => Push(c => ((BreakableGlassComponent)c).SolidShards = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var glass = (BreakableGlassComponent)component;
        if (Math.Abs(CrackSize - glass.CrackSize) > 0.00001) CrackSize = glass.CrackSize;
        if (Math.Abs(HoleRadius - glass.HoleRadius) > 0.00001) HoleRadius = glass.HoleRadius;
        if (Math.Abs(ImpactSpeed - glass.ImpactSpeed) > 0.00001) ImpactSpeed = glass.ImpactSpeed;
        if (Math.Abs(ImpactMomentum - glass.ImpactMomentum) > 0.00001) ImpactMomentum = glass.ImpactMomentum;
        if (Math.Abs(DebrisLifetime - glass.DebrisLifetime) > 0.00001) DebrisLifetime = glass.DebrisLifetime;
        if (Math.Abs(HitTolerance - glass.HitTolerance) > 0.00001) HitTolerance = glass.HitTolerance;
        if (DropLooseShards != glass.DropLooseShards) DropLooseShards = glass.DropLooseShards;
        if (Distort != glass.Distort) Distort = glass.Distort;
        if (SolidShards != glass.SolidShards) SolidShards = glass.SolidShards;
    }
}
