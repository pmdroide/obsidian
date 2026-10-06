using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Engine.Components;

namespace Anvil.Models;

public partial class AnimatorComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private string _clipSource = "";
    [ObservableProperty] private string _clipName = "";
    [ObservableProperty] private double _speed = 1;
    [ObservableProperty] private bool _loop = true;
    [ObservableProperty] private bool _inPlace;

    public AnimatorComponentViewModel(SceneObjectViewModel owner) : base(owner, AnimatorComponent.TypeId) { }

    partial void OnClipSourceChanged(string value)
    {
        string source = value?.Trim() ?? "";
        Push(c => ((AnimatorComponent)c).ClipSource = source);
    }

    partial void OnClipNameChanged(string value)
    {
        string name = value?.Trim() ?? "";
        Push(c => ((AnimatorComponent)c).ClipName = name);
    }

    partial void OnSpeedChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float speed = (float)value;
        Push(c => ((AnimatorComponent)c).Speed = speed);
    }

    partial void OnLoopChanged(bool value) => Push(c => ((AnimatorComponent)c).Loop = value);
    partial void OnInPlaceChanged(bool value) => Push(c => ((AnimatorComponent)c).InPlace = value);

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var animator = (AnimatorComponent)component;
        ClipSource = animator.ClipSource ?? "";
        ClipName = animator.ClipName ?? "";
        if (Math.Abs(Speed - animator.Speed) > 0.0001) Speed = animator.Speed;
        Loop = animator.Loop;
        InPlace = animator.InPlace;
    }
}
