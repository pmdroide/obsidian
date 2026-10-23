using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class ReverbZoneComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private ReverbPreset _preset = ReverbPreset.Room;
    [ObservableProperty] private bool _infinite;
    [ObservableProperty] private double _blendDistance = 1;
    [ObservableProperty] private double _weight = 1;
    [ObservableProperty] private double _priority;
    [ObservableProperty] private double _decayScale = 1;
    [ObservableProperty] private double _wetLevel;

    public static ReverbPreset[] Presets { get; } = Enum.GetValues<ReverbPreset>();

    public ReverbZoneComponentViewModel(SceneObjectViewModel owner) : base(owner, ReverbZoneComponent.TypeId) { }

    partial void OnPresetChanged(ReverbPreset value) => Push(c => ((ReverbZoneComponent)c).Preset = value);
    partial void OnInfiniteChanged(bool value) => Push(c => ((ReverbZoneComponent)c).Infinite = value);
    partial void OnBlendDistanceChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Max(value, 0);
        Push(c => ((ReverbZoneComponent)c).BlendDistance = v);
    }
    partial void OnWeightChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0, 1);
        Push(c => ((ReverbZoneComponent)c).Weight = v);
    }
    partial void OnPriorityChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        int v = (int)Math.Round(value);
        Push(c => ((ReverbZoneComponent)c).Priority = v);
    }
    partial void OnDecayScaleChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, 0.1, 10);
        Push(c => ((ReverbZoneComponent)c).DecayScale = v);
    }
    partial void OnWetLevelChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float v = (float)Math.Clamp(value, -40, 20);
        Push(c => ((ReverbZoneComponent)c).WetLevel = v);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var zone = (ReverbZoneComponent)component;
        Preset = zone.Preset;
        Infinite = zone.Infinite;
        if (Math.Abs(BlendDistance - zone.BlendDistance) > 0.0001) BlendDistance = zone.BlendDistance;
        if (Math.Abs(Weight - zone.Weight) > 0.0001) Weight = zone.Weight;
        if (Math.Abs(Priority - zone.Priority) > 0.0001) Priority = zone.Priority;
        if (Math.Abs(DecayScale - zone.DecayScale) > 0.0001) DecayScale = zone.DecayScale;
        if (Math.Abs(WetLevel - zone.WetLevel) > 0.0001) WetLevel = zone.WetLevel;
    }
}
