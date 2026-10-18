using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

/// <summary>Inspector of an AO Proxy component (Source 2 look, phase 6.1).</summary>
public partial class AoProxyComponentViewModel : ComponentViewModel
{
    public static AoProxyShape[] Shapes { get; } = Enum.GetValues<AoProxyShape>();

    [ObservableProperty] private AoProxyShape _shape = AoProxyShape.Auto;
    [ObservableProperty] private double _intensity = 1;
    [ObservableProperty] private double _sizeScale = 1;
    [ObservableProperty] private double _falloff = 2;
    [ObservableProperty] private double _cullDistance = 25;

    public AoProxyComponentViewModel(SceneObjectViewModel owner) : base(owner, AoProxyComponent.TypeId) { }

    partial void OnShapeChanged(AoProxyShape value)
    {
        if (!Enum.IsDefined(value)) return;
        Push(c => ((AoProxyComponent)c).Shape = value);
    }

    partial void OnIntensityChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0, 1);
        Push(c => ((AoProxyComponent)c).Intensity = v);
    }

    partial void OnSizeScaleChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0.1, 4);
        Push(c => ((AoProxyComponent)c).SizeScale = v);
    }

    partial void OnFalloffChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0.25, 8);
        Push(c => ((AoProxyComponent)c).Falloff = v);
    }

    partial void OnCullDistanceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 1, 1000);
        Push(c => ((AoProxyComponent)c).CullDistance = v);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var proxy = (AoProxyComponent)component;
        if (Shape != proxy.Shape) Shape = proxy.Shape;
        if (Math.Abs(Intensity - proxy.Intensity) > 0.00001) Intensity = proxy.Intensity;
        if (Math.Abs(SizeScale - proxy.SizeScale) > 0.00001) SizeScale = proxy.SizeScale;
        if (Math.Abs(Falloff - proxy.Falloff) > 0.00001) Falloff = proxy.Falloff;
        if (Math.Abs(CullDistance - proxy.CullDistance) > 0.00001) CullDistance = proxy.CullDistance;
    }
}
