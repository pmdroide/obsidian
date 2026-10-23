using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Components;
using Engine.Recources;

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
    [ObservableProperty] private AudioBus _output = AudioBus.Effects;
    [ObservableProperty] private double _minDistance = 10;
    [ObservableProperty] private double _maxDistance = 1000;
    [ObservableProperty] private double _pitch = 1;
    [ObservableProperty] private double _pan;
    [ObservableProperty] private double _spatialBlend = 1;
    [ObservableProperty] private double _spread;
    [ObservableProperty] private AudioRolloff _rolloff = AudioRolloff.Inverse;
    [ObservableProperty] private double _reverbSend = 1;
    [ObservableProperty] private double _wallMuffling = 1;
    [ObservableProperty] private double _dopplerLevel = 1;

    public static AudioBus[] Outputs { get; } = [AudioBus.Effects, AudioBus.Music];
    public static AudioRolloff[] Rolloffs { get; } = Enum.GetValues<AudioRolloff>();

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
    partial void OnOutputChanged(AudioBus value) => Push(c => ((AudioComponent)c).Output = value);
    partial void OnMinDistanceChanged(double value) => Push(c => ((AudioComponent)c).MinDistance = (float)Math.Max(value, 0.01));
    partial void OnMaxDistanceChanged(double value) => Push(c => ((AudioComponent)c).MaxDistance = (float)Math.Max(value, 0.01));
    partial void OnPitchChanged(double value) => Push(c => ((AudioComponent)c).Pitch = (float)Math.Clamp(value, 0.1, 4));
    partial void OnPanChanged(double value) => Push(c => ((AudioComponent)c).Pan = (float)Math.Clamp(value, -1, 1));
    partial void OnSpatialBlendChanged(double value) => Push(c => ((AudioComponent)c).SpatialBlend = (float)Math.Clamp(value, 0, 1));
    partial void OnSpreadChanged(double value) => Push(c => ((AudioComponent)c).Spread = (float)Math.Clamp(value, 0, 360));
    partial void OnRolloffChanged(AudioRolloff value) => Push(c => ((AudioComponent)c).Rolloff = value);
    partial void OnReverbSendChanged(double value) => Push(c => ((AudioComponent)c).ReverbSend = (float)Math.Clamp(value, 0, 1));
    partial void OnWallMufflingChanged(double value) => Push(c => ((AudioComponent)c).WallMuffling = (float)Math.Clamp(value, 0, 1));
    partial void OnDopplerLevelChanged(double value) => Push(c => ((AudioComponent)c).DopplerLevel = (float)Math.Clamp(value, 0, 5));

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
        Output = audio.Output;
        if (Math.Abs(MinDistance - audio.MinDistance) > 0.0001) MinDistance = audio.MinDistance;
        if (Math.Abs(MaxDistance - audio.MaxDistance) > 0.0001) MaxDistance = audio.MaxDistance;
        if (Math.Abs(Pitch - audio.Pitch) > 0.0001) Pitch = audio.Pitch;
        if (Math.Abs(Pan - audio.Pan) > 0.0001) Pan = audio.Pan;
        if (Math.Abs(SpatialBlend - audio.SpatialBlend) > 0.0001) SpatialBlend = audio.SpatialBlend;
        if (Math.Abs(Spread - audio.Spread) > 0.0001) Spread = audio.Spread;
        Rolloff = audio.Rolloff;
        if (Math.Abs(ReverbSend - audio.ReverbSend) > 0.0001) ReverbSend = audio.ReverbSend;
        if (Math.Abs(WallMuffling - audio.WallMuffling) > 0.0001) WallMuffling = audio.WallMuffling;
        if (Math.Abs(DopplerLevel - audio.DopplerLevel) > 0.0001) DopplerLevel = audio.DopplerLevel;
    }
}
