using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;
using Engine.Particles;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Anvil.Models;

public partial class BreakableComponentViewModel : ComponentViewModel
{
    public static BreakEffect[] Effects { get; } = Enum.GetValues<BreakEffect>();

    [ObservableProperty] private int _pieces = 12;
    [ObservableProperty] private double _impactSpeed = 4;
    [ObservableProperty] private double _impactMomentum = 2;
    [ObservableProperty] private double _breakForce = 2.5;
    [ObservableProperty] private double _debrisLifetime = 10;
    [ObservableProperty] private double _density = 1500;
    [ObservableProperty] private Color _interiorColor = Color.FromRgb(196, 168, 140);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEffect))]
    private BreakEffect _effect = BreakEffect.Dust;
    [ObservableProperty] private Color _effectColor = ToAvalonia(BreakableComponent.DustColor);
    [ObservableProperty] private double _effectAmount = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFollowing))]
    private string _followObject = "";
    [ObservableProperty] private string _followBone = "";
    [ObservableProperty] private bool _collapseBone = true;
    [ObservableProperty] private bool _ragdollOnBreak = true;

    public bool HasEffect => Effect != BreakEffect.None;
    public bool IsFollowing => !string.IsNullOrWhiteSpace(FollowObject);

    public BreakableComponentViewModel(SceneObjectViewModel owner) : base(owner, BreakableComponent.TypeId) { }

    partial void OnPiecesChanged(int value)
    {
        int pieces = Math.Clamp(value, 2, 64);
        Push(c => ((BreakableComponent)c).Pieces = pieces);
    }

    partial void OnImpactSpeedChanged(double value) => PushNumber(value, 0, 1000, (b, v) => b.ImpactSpeed = v);
    partial void OnImpactMomentumChanged(double value) => PushNumber(value, 0, 100000, (b, v) => b.ImpactMomentum = v);
    partial void OnBreakForceChanged(double value) => PushNumber(value, 0, 40, (b, v) => b.BreakForce = v);
    partial void OnDebrisLifetimeChanged(double value) => PushNumber(value, 0.1, 600, (b, v) => b.DebrisLifetime = v);
    partial void OnDensityChanged(double value) => PushNumber(value, 1, 20000, (b, v) => b.Density = v);
    partial void OnEffectAmountChanged(double value) => PushNumber(value, 0, 5, (b, v) => b.EffectAmount = v);

    partial void OnInteriorColorChanged(Color value)
    {
        var color = new XnaColor(value.R, value.G, value.B, (byte)255);
        Push(c => ((BreakableComponent)c).InteriorColor = color);
    }

    partial void OnEffectChanged(BreakEffect oldValue, BreakEffect newValue)
    {
        Push(c => ((BreakableComponent)c).Effect = newValue);
        // Switching between dust and blood swaps an untouched default colour for the other's.
        if (!Owner.SuppressPush && EffectColor == DefaultColor(oldValue) && DefaultColor(newValue) is { } color) EffectColor = color;
    }

    partial void OnEffectColorChanged(Color value)
    {
        var color = new XnaColor(value.R, value.G, value.B, (byte)255);
        Push(c => ((BreakableComponent)c).EffectColor = color);
    }

    partial void OnFollowObjectChanged(string value)
    {
        string name = value?.Trim() ?? "";
        Push(c => ((BreakableComponent)c).FollowObject = name);
    }

    partial void OnFollowBoneChanged(string value)
    {
        string bone = value?.Trim() ?? "";
        Push(c => ((BreakableComponent)c).FollowBone = bone);
    }

    partial void OnCollapseBoneChanged(bool value) => Push(c => ((BreakableComponent)c).CollapseBone = value);
    partial void OnRagdollOnBreakChanged(bool value) => Push(c => ((BreakableComponent)c).RagdollOnBreak = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var breakable = (BreakableComponent)component;
        static bool Differs(double shown, float value) => Math.Abs(shown - value) > 0.00001;
        if (Pieces != breakable.Pieces) Pieces = breakable.Pieces;
        if (Differs(ImpactSpeed, breakable.ImpactSpeed)) ImpactSpeed = breakable.ImpactSpeed;
        if (Differs(ImpactMomentum, breakable.ImpactMomentum)) ImpactMomentum = breakable.ImpactMomentum;
        if (Differs(BreakForce, breakable.BreakForce)) BreakForce = breakable.BreakForce;
        if (Differs(DebrisLifetime, breakable.DebrisLifetime)) DebrisLifetime = breakable.DebrisLifetime;
        if (Differs(Density, breakable.Density)) Density = breakable.Density;
        if (Differs(EffectAmount, breakable.EffectAmount)) EffectAmount = breakable.EffectAmount;
        Color interior = ToAvalonia(breakable.InteriorColor);
        if (InteriorColor != interior) InteriorColor = interior;
        if (Effect != breakable.Effect) Effect = breakable.Effect;
        Color effectColor = ToAvalonia(breakable.EffectColor);
        if (EffectColor != effectColor) EffectColor = effectColor;
        if (FollowObject != (breakable.FollowObject ?? "")) FollowObject = breakable.FollowObject ?? "";
        if (FollowBone != (breakable.FollowBone ?? "")) FollowBone = breakable.FollowBone ?? "";
        if (CollapseBone != breakable.CollapseBone) CollapseBone = breakable.CollapseBone;
        if (RagdollOnBreak != breakable.RagdollOnBreak) RagdollOnBreak = breakable.RagdollOnBreak;
    }

    private void PushNumber(double value, double min, double max, Action<BreakableComponent, float> set)
    {
        if (!double.IsFinite(value)) return;
        float clamped = (float)Math.Clamp(value, min, max);
        Push(c => set((BreakableComponent)c, clamped));
    }

    private static Color? DefaultColor(BreakEffect effect) => effect switch
    {
        BreakEffect.Dust => ToAvalonia(BreakableComponent.DustColor),
        BreakEffect.Blood => ToAvalonia(BreakableComponent.BloodColor),
        _ => null,
    };

    private static Color ToAvalonia(XnaColor color) => Color.FromRgb(color.R, color.G, color.B);
}
