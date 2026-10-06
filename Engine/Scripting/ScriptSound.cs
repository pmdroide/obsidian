using Engine.Recources;
using FmodForFoxes;

namespace Engine.Scripting;

/// <summary>A sound started by a script. It stops automatically when the script stops.</summary>
public sealed class ScriptSound
{
    private Channel _channel;
    private bool _stopped;

    internal ScriptSound(Channel channel) => _channel = channel;

    public bool IsPlaying
    {
        get
        {
            if (_stopped) return false;
            try { return _channel.IsPlaying; }
            catch { return false; } // handle already released by FMOD
        }
    }

    /// <summary>0 = silent, 1 = full volume.</summary>
    public float Volume
    {
        get => _channel.Volume;
        set => _channel.Volume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Playback speed multiplier: 2 is an octave up, 0.5 an octave down.</summary>
    public float Pitch
    {
        get => _channel.Pitch;
        set => _channel.Pitch = value;
    }

    public bool Loop
    {
        get => _channel.Looping;
        set => _channel.Looping = value;
    }

    public bool Paused
    {
        get => _channel.Paused;
        set => _channel.Paused = value;
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        if (Audio.Instance != null) Audio.Instance.StopInstance(_channel);
        else try { _channel.Stop(); } catch { /* invalid handle */ }
    }
}
