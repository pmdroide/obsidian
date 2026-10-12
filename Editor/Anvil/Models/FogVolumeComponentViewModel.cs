using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class FogVolumeComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _density = 0.15;
    [ObservableProperty] private double _edgeFade = 0.1;

    public FogVolumeComponentViewModel(SceneObjectViewModel owner) : base(owner, FogVolumeComponent.TypeId) { }

    partial void OnDensityChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float density = (float)Math.Clamp(value, 0, 5);
        Push(c => ((FogVolumeComponent)c).Density = density);
    }

    partial void OnEdgeFadeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float fade = (float)Math.Clamp(value, 0, 1);
        Push(c => ((FogVolumeComponent)c).EdgeFade = fade);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var volume = (FogVolumeComponent)component;
        if (Math.Abs(Density - volume.Density) > 0.00001) Density = volume.Density;
        if (Math.Abs(EdgeFade - volume.EdgeFade) > 0.00001) EdgeFade = volume.EdgeFade;
    }
}
