using Engine.Entities;
using Engine.Recources;
using FmodForFoxes;
using Microsoft.Xna.Framework;
using System.Text.Json.Serialization;

namespace Engine.Components;

public sealed class AudioComponent : GameComponent
{
    public const string TypeId = "audio";
    public string ClipPath { get; set; } = "";
    public float Volume { get; set; } = 1f;
    /// <summary>Playback speed: 2 is an octave up, 0.5 an octave down.</summary>
    public float Pitch { get; set; } = 1f;
    public bool Loop { get; set; }
    public bool PlayOnStart { get; set; } = true;
    public bool Spatial { get; set; } = true;
    /// <summary>The mixer bus (Effects or Music), so the menus' Music/Effects volume settings apply to it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AudioBus Output { get; set; } = AudioBus.Effects;
    /// <summary>Stereo position of the 2D part of the sound: -1 = left speaker only, 0 = centre, 1 = right only.</summary>
    public float Pan { get; set; }
    /// <summary>3D only: 1 = fully positional, 0 = plain 2D (no pan or falloff), in between mixes both.</summary>
    public float SpatialBlend { get; set; } = 1f;
    /// <summary>3D only: degrees the speakers spread the sound over; 0 = a point, 360 = all around (big or close sources).</summary>
    public float Spread { get; set; }
    /// <summary>3D only: full volume within this many units, then quieter along <see cref="Rolloff"/>.</summary>
    public float MinDistance { get; set; } = 10f;
    /// <summary>3D only: no further attenuation beyond this distance (silent here for the Linear curves).</summary>
    public float MaxDistance { get; set; } = 1000f;
    /// <summary>3D only: the falloff curve between <see cref="MinDistance"/> and <see cref="MaxDistance"/>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AudioRolloff Rolloff { get; set; } = AudioRolloff.Inverse;
    /// <summary>0..1: how much of the sound goes into the Reverb Zones' reverb around the listener.</summary>
    public float ReverbSend { get; set; } = 1f;
    /// <summary>3D only: 0..1, how much walls (static colliders) between it and the listener muffle it.</summary>
    public float WallMuffling { get; set; } = 1f;
    /// <summary>3D only: pitch shift from the sound's and the listener's motion; 0 = none, 1 = real, up to 5 = exaggerated.</summary>
    public float DopplerLevel { get; set; } = 1f;

    private Channel? _channel;
    private string _playingClipPath;
    private bool _playingSpatial;
    private AudioBus _playingOutput;
    private AudioRolloff _playingRolloff;
    [JsonIgnore] public bool IsPlaying => _channel?.IsPlaying ?? false;

    /// <summary>While a 3D sound plays: its velocity, the walls between it and the listener and how muffled it is.</summary>
    [JsonIgnore] public AudioManager.EmitterState? Emitter =>
        _channel is { } c && Audio.Instance != null && Audio.Instance.TryGetEmitter(c, out var state) ? state : null;

    public static bool IsAudioAsset(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).ToLowerInvariant()
            is ".wav" or ".mp3" or ".ogg" or ".flac";

    public void Play(BasicEntity owner)
    {
        OnStop();
        if (!Enabled || !owner.IsEnabled || !IsAudioAsset(ClipPath)) return;
        float volume = Math.Clamp(Volume, 0f, 1f);
        _channel = Spatial
            ? Audio.Instance?.PlaySound3D(ClipPath, () => owner.Position, volume, Loop,
                ClampedMinDistance, ClampedMaxDistance, contentRelativePath: true, Output, Configure)
            : Audio.Instance?.PlaySound(ClipPath, volume, ClampedPitch, Loop, contentRelativePath: true, Output, Configure);
        _playingClipPath = ClipPath;
        _playingSpatial = Spatial;
        _playingOutput = Output;
        _playingRolloff = Rolloff;
        if (Spatial && _channel is { } playing) Audio.Instance?.SetEmitter(playing, owner, Clamp(WallMuffling, 0f, 1f, 1f));
    }

    /// <summary>Plays from silence, fading in over <paramref name="seconds"/>.</summary>
    public void FadeIn(BasicEntity owner, float seconds)
    {
        Play(owner);
        if (_channel is { } channel) Audio.Instance?.Fade(channel, 1, seconds, from: 0);
    }

    /// <summary>Fades out over <paramref name="seconds"/>, then stops.</summary>
    public void FadeOut(float seconds)
    {
        if (_channel is { } channel && Audio.Instance != null) Audio.Instance.Fade(channel, 0, seconds, stopAtEnd: true);
        else OnStop();
    }

    /// <summary>Playback position in seconds while playing; set it to skip (seek).</summary>
    [JsonIgnore]
    public float Time
    {
        get => _channel is { } c && c.Native.getPosition(out uint ms, FMOD.TIMEUNIT.MS) == FMOD.RESULT.OK ? ms / 1000f : 0;
        set { if (_channel is { } c) c.Native.setPosition((uint)(Math.Max(value, 0) * 1000), FMOD.TIMEUNIT.MS); }
    }

    public override void OnStart(BasicEntity owner)
    {
        if (PlayOnStart) Play(owner);
    }

    public override void OnUpdate(BasicEntity owner, GameTime time) => OnChanged(owner);

    public override void OnChanged(BasicEntity owner)
    {
        if (!Enabled || !owner.IsEnabled || !IsAudioAsset(ClipPath))
        {
            OnStop();
            return;
        }
        if (!IsPlaying) return;
        if (_playingClipPath != ClipPath || _playingSpatial != Spatial || _playingOutput != Output) Play(owner);
        else if (_channel is { } channel)
        {
            channel.Volume = Math.Clamp(Volume, 0f, 1f);
            if (Spatial)
            {
                channel.MinDistance3D = ClampedMinDistance;
                channel.MaxDistance3D = ClampedMaxDistance;
                if (_playingRolloff != Rolloff)
                {
                    AudioManager.SetRolloff(channel, Rolloff);
                    _playingRolloff = Rolloff;
                }
            }
            Configure(channel);
            channel.Looping = Loop;
            if (Spatial) Audio.Instance?.SetEmitter(channel, owner, Clamp(WallMuffling, 0f, 1f, 1f));
        }
    }

    // Everything but volume, distances and looping, which the play calls set themselves.
    private void Configure(Channel channel)
    {
        channel.Pitch = ClampedPitch;
        channel.Native.setPan(Clamp(Pan, -1f, 1f, 0f));
        channel.Native.setReverbProperties(0, Clamp(ReverbSend, 0f, 1f, 1f));
        if (!Spatial) return;
        // Starting (no channel yet); a playing channel only changes its rolloff in OnChanged.
        if (_channel == null) AudioManager.SetRolloff(channel, Rolloff);
        channel.Level3D = Clamp(SpatialBlend, 0f, 1f, 1f);
        channel.Spread3D = Clamp(Spread, 0f, 360f, 0f);
        channel.DopplerLevel3D = Clamp(DopplerLevel, 0f, 5f, 1f);
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private float ClampedPitch => Clamp(Pitch, 0.1f, 4f, 1f);
    private float ClampedMinDistance => float.IsFinite(MinDistance) ? Math.Max(MinDistance, 0.01f) : 10f;
    private float ClampedMaxDistance => float.IsFinite(MaxDistance) ? Math.Max(MaxDistance, ClampedMinDistance) : 1000f;

    public override void OnStop()
    {
        if (_channel.HasValue) Audio.Instance?.StopInstance(_channel.Value);
        _channel = null;
    }
}
