using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

/// <summary>Inspector of a Planar Reflection component (mirror images on glass panes).</summary>
public partial class PlanarReflectionComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _resolutionScale = 1;
    [ObservableProperty] private double _renderDistance = 100;

    public PlanarReflectionComponentViewModel(SceneObjectViewModel owner) : base(owner, PlanarReflectionComponent.TypeId) { }

    partial void OnResolutionScaleChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0.25, 1);
        Push(c => ((PlanarReflectionComponent)c).ResolutionScale = v);
    }

    partial void OnRenderDistanceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 1, 10000);
        Push(c => ((PlanarReflectionComponent)c).RenderDistance = v);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var planar = (PlanarReflectionComponent)component;
        if (Math.Abs(ResolutionScale - planar.ResolutionScale) > 0.00001) ResolutionScale = planar.ResolutionScale;
        if (Math.Abs(RenderDistance - planar.RenderDistance) > 0.00001) RenderDistance = planar.RenderDistance;
    }
}
