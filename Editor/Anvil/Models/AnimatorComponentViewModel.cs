using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

public partial class AnimatorComponentViewModel : ComponentViewModel
{
    [ObservableProperty] private string _clipSource = "";
    [ObservableProperty] private string _clipName = "";
    [ObservableProperty] private double _speed = 1;
    [ObservableProperty] private bool _loop = true;
    [ObservableProperty] private bool _inPlace;
    [ObservableProperty] private string _startAction = "";
    [ObservableProperty] private double _fadeTime = 0.2;
    public ObservableCollection<AnimationActionViewModel> Actions { get; } = new();
    public IRelayCommand AddActionCommand { get; }

    // Set while Apply rebuilds or refreshes the rows, whose setters would otherwise push them back.
    private bool _applying;

    public AnimatorComponentViewModel(SceneObjectViewModel owner) : base(owner, AnimatorComponent.TypeId)
    {
        AddActionCommand = new RelayCommand(() =>
        {
            Actions.Add(new AnimationActionViewModel(this, new AnimationAction { Name = UniqueName() }));
            PushActions();
        });
    }

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

    partial void OnStartActionChanged(string value)
    {
        string name = value?.Trim() ?? "";
        Push(c => ((AnimatorComponent)c).StartAction = name);
    }

    partial void OnFadeTimeChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        float fade = (float)Math.Max(value, 0);
        Push(c => ((AnimatorComponent)c).FadeTime = fade);
    }

    internal void RemoveAction(AnimationActionViewModel action)
    {
        if (Actions.Remove(action)) PushActions();
    }

    /// <summary>Sends the whole action list.</summary>
    internal void PushActions()
    {
        if (_applying) return;
        var actions = Actions.Select(a => a.ToAction()).ToList();
        Push(c => ((AnimatorComponent)c).Actions = actions);
    }

    private string UniqueName()
    {
        var names = new HashSet<string>(Actions.Select(a => a.Name?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase);
        string name = "Action";
        for (int i = 2; names.Contains(name); i++) name = "Action " + i;
        return name;
    }

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
        StartAction = animator.StartAction ?? "";
        if (Math.Abs(FadeTime - animator.FadeTime) > 0.0001) FadeTime = animator.FadeTime;

        var actions = animator.Actions ?? new List<AnimationAction>();
        _applying = true;
        try
        {
            if (actions.Count != Actions.Count)
            {
                Actions.Clear();
                foreach (var action in actions) Actions.Add(new AnimationActionViewModel(this, action ?? new AnimationAction()));
                return;
            }
            // Same rows: update in place so a focused field keeps its control.
            for (int i = 0; i < actions.Count; i++) Actions[i].Apply(actions[i] ?? new AnimationAction());
        }
        finally
        {
            _applying = false;
        }
    }
}

/// <summary>One row of the Animator's Actions list: a name scripts play it by, and its clip.</summary>
public partial class AnimationActionViewModel : ObservableObject
{
    private readonly AnimatorComponentViewModel _parent;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _clipSource = "";
    [ObservableProperty] private string _clipName = "";
    [ObservableProperty] private double _speed = 1;
    [ObservableProperty] private bool _loop = true;
    public IRelayCommand RemoveCommand { get; }

    // Last valid speed: what the engine gets for this row while another field is edited.
    private double _committedSpeed = 1;
    private bool _applying;

    public AnimationActionViewModel(AnimatorComponentViewModel parent, AnimationAction action)
    {
        _parent = parent;
        RemoveCommand = new RelayCommand(() => _parent.RemoveAction(this));
        Apply(action);
    }

    internal AnimationAction ToAction() => new()
    {
        Name = Name?.Trim() ?? "",
        ClipSource = ClipSource?.Trim() ?? "",
        ClipName = ClipName?.Trim() ?? "",
        Speed = (float)_committedSpeed,
        Loop = Loop,
    };

    internal void Apply(AnimationAction action)
    {
        _applying = true;
        try
        {
            Name = action.Name ?? "";
            ClipSource = action.ClipSource ?? "";
            ClipName = action.ClipName ?? "";
            _committedSpeed = float.IsFinite(action.Speed) ? action.Speed : 1;
            if (Math.Abs(Speed - _committedSpeed) > 0.0001) Speed = _committedSpeed;
            Loop = action.Loop;
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnNameChanged(string value) => Changed();
    partial void OnClipSourceChanged(string value) => Changed();
    partial void OnClipNameChanged(string value) => Changed();
    partial void OnLoopChanged(bool value) => Changed();

    partial void OnSpeedChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        _committedSpeed = value;
        Changed();
    }

    private void Changed()
    {
        if (!_applying) _parent.PushActions();
    }
}
