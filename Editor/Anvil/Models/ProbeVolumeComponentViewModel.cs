using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

/// <summary>Inspector of a Probe Volume component (detail probe volume, Source 2 look phase 4.1).</summary>
public partial class ProbeVolumeComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private double _probeSpacing = 1;
    [ObservableProperty] private double _priority;
    [ObservableProperty] private double _blendMargin = 1;

    public ProbeVolumeComponentViewModel(SceneObjectViewModel owner) : base(owner, ProbeVolumeComponent.TypeId) { }

    partial void OnProbeSpacingChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0.1, 20);
        Push(c => ((ProbeVolumeComponent)c).ProbeSpacing = v);
    }

    partial void OnPriorityChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int v = (int)Math.Round(value);
        Push(c => ((ProbeVolumeComponent)c).Priority = v);
    }

    partial void OnBlendMarginChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0, 20);
        Push(c => ((ProbeVolumeComponent)c).BlendMargin = v);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var volume = (ProbeVolumeComponent)component;
        if (Math.Abs(ProbeSpacing - volume.ProbeSpacing) > 0.00001) ProbeSpacing = volume.ProbeSpacing;
        if (Math.Abs(Priority - volume.Priority) > 0.5) Priority = volume.Priority;
        if (Math.Abs(BlendMargin - volume.BlendMargin) > 0.00001) BlendMargin = volume.BlendMargin;
    }
}
