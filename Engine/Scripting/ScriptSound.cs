using Engine.Recources;
using FmodForFoxes;
using Microsoft.Xna.Framework;

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

    /// <summary>
    /// Stereo position of the 2D part of the sound: -1 = left speaker, 0 = centre, 1 = right speaker.
    /// Write-only: FMOD can't read a pan back.
    /// </summary>
    public float Pan
    {
        set => _channel.Native.setPan(Math.Clamp(value, -1f, 1f));
    }

    /// <summary>0..1: how much of the sound goes into the Reverb Zones' reverb (1 when it starts).</summary>
    public float ReverbSend
    {
        get { _channel.Native.getReverbProperties(0, out float wet); return wet; }
        set => _channel.Native.setReverbProperties(0, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>3D sounds: pitch shift from motion (Doppler); 0 = none, 1 = real (the default).</summary>
    public float DopplerLevel
    {
        get => _channel.DopplerLevel3D;
        set => _channel.DopplerLevel3D = Math.Clamp(value, 0f, 5f);
    }

    /// <summary>3D sounds: 0..1, how much walls between it and the listener muffle it (1 when it starts).</summary>
    public float WallMuffling
    {
        set => Audio.Instance?.SetWallMuffling(_channel, value);
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

    /// <summary>Silences it without changing Volume (and without stopping it).</summary>
    public bool Mute
    {
        get => _channel.Mute;
        set => _channel.Mute = value;
    }

    /// <summary>Playback position in seconds; set it to skip (seek).</summary>
    public float Time
    {
        get => _channel.Native.getPosition(out uint ms, FMOD.TIMEUNIT.MS) == FMOD.RESULT.OK ? ms / 1000f : 0;
        set => _channel.Native.setPosition((uint)(Math.Max(value, 0) * 1000), FMOD.TIMEUNIT.MS);
    }

    /// <summary>Length of the clip in seconds.</summary>
    public float Length => AudioManager.LengthOf(_channel);

    /// <summary>How loud it really is now (0..1): volume x fades x distance x walls x bus volumes.</summary>
    public float Audibility => _channel.Native.getAudibility(out float a) == FMOD.RESULT.OK ? a : 0;

    /// <summary>Fades to <paramref name="volume"/> (0..1, on top of Volume) over <paramref name="seconds"/>.</summary>
    public void FadeTo(float volume, float seconds) => Audio.Instance?.Fade(_channel, volume, seconds);

    /// <summary>Fades in from silence over <paramref name="seconds"/>.</summary>
    public void FadeIn(float seconds) => Audio.Instance?.Fade(_channel, 1, seconds, from: 0);

    /// <summary>Fades out over <paramref name="seconds"/>, then stops.</summary>
    public void FadeOut(float seconds)
    {
        if (Audio.Instance == null) { Stop(); return; }
        Audio.Instance.Fade(_channel, 0, seconds, stopAtEnd: true);
    }

    /// <summary>3D sounds: where it plays. Setting it pins the sound there (it stops following its gameobject).</summary>
    public Vector3 Position
    {
        get => Audio.Instance != null && Audio.Instance.TryGetEmitter(_channel, out var state) ? state.Position : Vector3.Zero;
        set => Audio.Instance?.SetEmitterPosition(_channel, value);
    }

    /// <summary>3D sounds: full volume within this distance.</summary>
    public float MinDistance
    {
        get => _channel.MinDistance3D;
        set => _channel.MinDistance3D = Math.Max(value, 0.01f);
    }

    /// <summary>3D sounds: no further falloff beyond this distance (silent here for the Linear curves).</summary>
    public float MaxDistance
    {
        get => _channel.MaxDistance3D;
        set => _channel.MaxDistance3D = Math.Max(value, 0.01f);
    }

    /// <summary>3D sounds: the distance falloff curve (write-only).</summary>
    public AudioRolloff Rolloff
    {
        set
        {
            bool loop = Loop;
            AudioManager.SetRolloff(_channel, value);
            Loop = loop;
        }
    }

    /// <summary>3D sounds: 1 = fully positional, 0 = plain 2D.</summary>
    public float SpatialBlend
    {
        get => _channel.Level3D;
        set => _channel.Level3D = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>3D sounds: degrees the speakers spread it over (0 = a point, 360 = all around).</summary>
    public float Spread
    {
        get => _channel.Spread3D;
        set => _channel.Spread3D = Math.Clamp(value, 0f, 360f);
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        if (Audio.Instance != null) Audio.Instance.StopInstance(_channel);
        else try { _channel.Stop(); } catch { /* invalid handle */ }
    }
}
