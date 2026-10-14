using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class FoliageWindComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _height;
    [ObservableProperty] private double _trunkBend = 1;
    [ObservableProperty] private double _branchSway = 1;
    [ObservableProperty] private double _leafFlutter = 1;
    [ObservableProperty] private double _windScale = 1;
    [ObservableProperty] private double _minWind = 1;
    [ObservableProperty] private double _speed = 1;

    public FoliageWindComponentViewModel(SceneObjectViewModel owner) : base(owner, FoliageWindComponent.TypeId) { }

    partial void OnHeightChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float height = (float)Math.Clamp(value, 0, 1000);
        Push(c => ((FoliageWindComponent)c).Height = height);
    }

    partial void OnTrunkBendChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float bend = (float)Math.Clamp(value, 0, 3);
        Push(c => ((FoliageWindComponent)c).TrunkBend = bend);
    }

    partial void OnBranchSwayChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float sway = (float)Math.Clamp(value, 0, 3);
        Push(c => ((FoliageWindComponent)c).BranchSway = sway);
    }

    partial void OnLeafFlutterChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float flutter = (float)Math.Clamp(value, 0, 3);
        Push(c => ((FoliageWindComponent)c).LeafFlutter = flutter);
    }

    partial void OnWindScaleChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float scale = (float)Math.Clamp(value, 0, 5);
        Push(c => ((FoliageWindComponent)c).WindScale = scale);
    }

    partial void OnMinWindChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float wind = (float)Math.Clamp(value, 0, 40);
        Push(c => ((FoliageWindComponent)c).MinWind = wind);
    }

    partial void OnSpeedChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float speed = (float)Math.Clamp(value, 0.05, 5);
        Push(c => ((FoliageWindComponent)c).Speed = speed);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var wind = (FoliageWindComponent)component;
        if (Math.Abs(Height - wind.Height) > 0.00001) Height = wind.Height;
        if (Math.Abs(TrunkBend - wind.TrunkBend) > 0.00001) TrunkBend = wind.TrunkBend;
        if (Math.Abs(BranchSway - wind.BranchSway) > 0.00001) BranchSway = wind.BranchSway;
        if (Math.Abs(LeafFlutter - wind.LeafFlutter) > 0.00001) LeafFlutter = wind.LeafFlutter;
        if (Math.Abs(WindScale - wind.WindScale) > 0.00001) WindScale = wind.WindScale;
        if (Math.Abs(MinWind - wind.MinWind) > 0.00001) MinWind = wind.MinWind;
        if (Math.Abs(Speed - wind.Speed) > 0.00001) Speed = wind.Speed;
    }
}
