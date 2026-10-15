using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

/// <summary>Inspector of a Reflection Probe component (Source 2 look, phase 3.2).</summary>
public partial class ReflectionProbeComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _blendDistance = 1;
    [ObservableProperty] private bool _boxProjection = true;
    [ObservableProperty] private double _priority;
    [ObservableProperty] private double _resolution = 128;

    public ReflectionProbeComponentViewModel(SceneObjectViewModel owner) : base(owner, ReflectionProbeComponent.TypeId) { }

    partial void OnBlendDistanceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0, 100);
        Push(c => ((ReflectionProbeComponent)c).BlendDistance = v);
    }

    partial void OnBoxProjectionChanged(bool value) => Push(c => ((ReflectionProbeComponent)c).BoxProjection = value);

    partial void OnPriorityChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int v = (int)Math.Round(value);
        Push(c => ((ReflectionProbeComponent)c).Priority = v);
    }

    partial void OnResolutionChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int v = (int)Math.Clamp(value, 64, 512);
        Push(c => ((ReflectionProbeComponent)c).Resolution = v);
    }

    /// <summary>Captures the probe again (after the room changed).</summary>
    [RelayCommand]
    private void Capture() => Push(c => ((ReflectionProbeComponent)c).RequestCapture());

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var probe = (ReflectionProbeComponent)component;
        if (Math.Abs(BlendDistance - probe.BlendDistance) > 0.00001) BlendDistance = probe.BlendDistance;
        if (BoxProjection != probe.BoxProjection) BoxProjection = probe.BoxProjection;
        if (Math.Abs(Priority - probe.Priority) > 0.5) Priority = probe.Priority;
        if (Math.Abs(Resolution - probe.Resolution) > 0.5) Resolution = probe.Resolution;
    }
}
