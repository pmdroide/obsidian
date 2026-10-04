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
    public bool Loop { get; set; }
    public bool PlayOnStart { get; set; } = true;
    public bool Spatial { get; set; } = true;

    private Channel? _channel;
    private string _playingClipPath;
    private bool _playingSpatial;
    [JsonIgnore] public bool IsPlaying => _channel?.IsPlaying ?? false;

    public static bool IsAudioAsset(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).ToLowerInvariant()
            is ".wav" or ".mp3" or ".ogg" or ".flac";

    public void Play(BasicEntity owner)
    {
        OnStop();
        if (!Enabled || !owner.IsEnabled || !IsAudioAsset(ClipPath)) return;
        float volume = Math.Clamp(Volume, 0f, 1f);
        _channel = Spatial
            ? Audio.Instance?.PlaySound3D(ClipPath, () => owner.Position, volume, Loop, contentRelativePath: true)
            : Audio.Instance?.PlaySound(ClipPath, volume, loop: Loop, contentRelativePath: true);
        _playingClipPath = ClipPath;
        _playingSpatial = Spatial;
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
        if (_playingClipPath != ClipPath || _playingSpatial != Spatial) Play(owner);
        else if (_channel is { } channel)
        {
            channel.Volume = Math.Clamp(Volume, 0f, 1f);
            channel.Looping = Loop;
        }
    }

    public override void OnStop()
    {
        if (_channel.HasValue) Audio.Instance?.StopInstance(_channel.Value);
        _channel = null;
    }
}
