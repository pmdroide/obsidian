using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;

namespace Anvil.Models;

public partial class AudioComponentViewModel : ComponentViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipLabel), nameof(HasClip))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ClearClipCommand))]
    private string _clipPath = string.Empty;
    [ObservableProperty] private double _volume = 1;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private bool _playOnStart = true;
    [ObservableProperty] private bool _spatial = true;
    [ObservableProperty] private double _minDistance = 10;
    [ObservableProperty] private double _maxDistance = 1000;

    public string ClipLabel => HasClip ? ClipPath : "Drop an audio asset here";
    public bool HasClip => AudioComponent.IsAudioAsset(ClipPath);

    public AudioComponentViewModel(SceneObjectViewModel owner) : base(owner, AudioComponent.TypeId) { }

    public void AssignClip(string path)
    {
        if (AudioComponent.IsAudioAsset(path)) ClipPath = path;
    }

    partial void OnClipPathChanged(string value) => Push(c => ((AudioComponent)c).ClipPath = value);
    partial void OnVolumeChanged(double value) => Push(c => ((AudioComponent)c).Volume = (float)Math.Clamp(value, 0, 1));
    partial void OnLoopChanged(bool value) => Push(c => ((AudioComponent)c).Loop = value);
    partial void OnPlayOnStartChanged(bool value) => Push(c => ((AudioComponent)c).PlayOnStart = value);
    partial void OnSpatialChanged(bool value) => Push(c => ((AudioComponent)c).Spatial = value);
    partial void OnMinDistanceChanged(double value) => Push(c => ((AudioComponent)c).MinDistance = (float)Math.Max(value, 0.01));
    partial void OnMaxDistanceChanged(double value) => Push(c => ((AudioComponent)c).MaxDistance = (float)Math.Max(value, 0.01));

    [RelayCommand(CanExecute = nameof(HasClip))]
    private void ClearClip() => ClipPath = string.Empty;

    [RelayCommand(CanExecute = nameof(HasClip))]
    private void Play()
    {
        if (Owner.EngineId is int id) Owner.Bridge?.EnqueuePlayAudio(id, true);
    }

    [RelayCommand]
    private void Stop()
    {
        if (Owner.EngineId is int id) Owner.Bridge?.EnqueuePlayAudio(id, false);
    }

    public override void Apply(GameComponent component, bool freezeFields)
    {
        base.Apply(component, freezeFields);
        if (freezeFields) return;
        var audio = (AudioComponent)component;
        ClipPath = audio.ClipPath;
        if (Math.Abs(Volume - audio.Volume) > 0.0001) Volume = audio.Volume;
        Loop = audio.Loop;
        PlayOnStart = audio.PlayOnStart;
        Spatial = audio.Spatial;
        if (Math.Abs(MinDistance - audio.MinDistance) > 0.0001) MinDistance = audio.MinDistance;
        if (Math.Abs(MaxDistance - audio.MaxDistance) > 0.0001) MaxDistance = audio.MaxDistance;
    }
}
