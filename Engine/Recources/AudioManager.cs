using System;
using System.Collections.Generic;
using Engine.Editor;
using Engine.Entities;
using FmodForFoxes;
using Microsoft.Xna.Framework;

namespace Engine.Recources
{
    /// <summary>
    /// Game audio subsystem built on FMOD via the FmodForFoxes wrapper.
    /// Follows the engine's Load/Initialize/Update/Dispose subsystem convention and is
    /// wired into <see cref="Engine.Logic.ScreenManager"/>. Supports 2D one-shot SFX,
    /// looping/instanced sounds, 3D positional audio (listener driven by the active
    /// camera, emitters by world positions), a master volume, and streamed music.
    ///
    /// FMOD's native libraries (fmod.dll / fmodL.dll) are supplied locally by the dev
    /// from fmod.com and copied next to the executable. If they are missing, Initialize
    /// fails gracefully and every call becomes a no-op so the engine still runs.
    ///
    /// Note: FmodForFoxes' <see cref="Channel"/> is a value type (a thin wrapper around an
    /// FMOD handle), so playback methods return <c>Channel?</c> — null means "not played".
    /// </summary>
    public class AudioManager : IDisposable
    {
        private sealed class Tracked3D
        {
            public Channel Channel;
            public Func<Vector3> PositionSource;
        }

        private INativeFmodLibrary _native;
        private Listener3D _listener;
        private bool _initialized;

        // Separate caches for 2D vs 3D so toggling FMOD's 3D mode on a shared Sound
        // can never make a 2D one-shot suddenly spatialise (or vice versa).
        private readonly Dictionary<string, Sound> _cache2D = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Sound> _cache3D = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Tracked3D> _tracked = new List<Tracked3D>();
        private Channel? _music;
        private float _masterVolume = 1f;

        ////////////////////////////////////////////////////////////////////////////////
        //  LIFECYCLE
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Boots FMOD. <paramref name="contentRoot"/> is combined with the requested
        /// path by FmodForFoxes' FileLoader (TitleContainer, i.e. relative to the app
        /// base directory), so sounds placed under Content/Audio load as "Audio/name".
        /// </summary>
        public void Initialize(string contentRoot = "Content")
        {
            uint expected = (uint)FMOD.VERSION.number;
            EditorBridge.Log($"Audio: FmodForFoxes bindings require FMOD {FormatFmodVersion(expected)} " +
                             $"(0x{expected:X8}). fmod.dll/fmodL.dll must be EXACTLY this version (x64).");
            try
            {
                _native = new DesktopNativeFmodLibrary();
                FmodManager.Init(_native, FmodInitMode.Core, contentRoot);
                _listener = new Listener3D();
                _initialized = true;
                Audio.Instance = this;
                MasterVolume = _masterVolume;

                CoreSystem.Native.getVersion(out uint dllVersion);
                bool match = dllVersion == expected;
                EditorBridge.Log($"Audio: FMOD initialized. Loaded DLL is FMOD {FormatFmodVersion(dllVersion)} " +
                                 $"(0x{dllVersion:X8}) -> {(match ? "MATCH" : "MISMATCH")}.");
            }
            catch (Exception e)
            {
                _initialized = false;
                EditorBridge.Log("Audio: FMOD init FAILED: " + e.Message +
                    " | A header/version mismatch means your fmod.dll is not FMOD " + FormatFmodVersion(expected) +
                    "; a 'unable to load DLL' error means fmod.dll/fmodL.dll (x64) aren't next to the executable.");
            }
        }

        // FMOD packs versions as 16:8:8 — product:minor:patch, each byte read as decimal.
        private static string FormatFmodVersion(uint v)
            => $"{(v >> 16) & 0xFFFF}.{(v >> 8) & 0xFF:D2}.{v & 0xFF}";

        /// <summary>Pumps FMOD. Must run every frame, including during the intro state.</summary>
        public void SystemUpdate()
        {
            if (!_initialized) return;
            FmodManager.Update();
        }

        /// <summary>
        /// Refreshes the 3D listener from the active camera and the tracked emitters from
        /// their position sources, then reaps any finished channels. Called once per frame
        /// after scene logic has advanced positions.
        /// </summary>
        public void UpdateListener(Camera cam)
        {
            if (!_initialized || _listener == null || cam == null) return;

            Vector3 forward = cam.Forward;
            if (forward != Vector3.Zero) forward.Normalize();
            Vector3 up = cam.Up;
            if (up != Vector3.Zero) up.Normalize();

            // Velocity left zero for now; supply (pos - lastPos)/dt later for Doppler.
            _listener.SetAttributes(ToFmod(cam.Position), Vector3.Zero, ToFmod(forward), ToFmod(up));

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Tracked3D t = _tracked[i];
                if (!t.Channel.IsPlaying)
                {
                    _tracked.RemoveAt(i);
                    continue;
                }
                t.Channel.Position3D = ToFmod(t.PositionSource());
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  2D
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>One-shot or looping 2D (non-positional) sound.</summary>
        public Channel? PlaySound(string name, float volume = 1f, float pitch = 1f, bool loop = false)
        {
            if (!_initialized) return null;
            Sound sound = GetOrLoad(name, streamed: false, is3d: false);
            if (sound == null) return null;
            if (!TryPlay(sound, paused: false, out Channel channel)) return null;

            channel.Is3D = false;
            channel.Volume = volume;
            channel.Pitch = pitch;
            channel.Looping = loop;
            return channel;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  3D POSITIONAL
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>Plays a sound at a fixed world position.</summary>
        public Channel? PlaySound3D(string name, Vector3 worldPosition, float volume = 1f, bool loop = false,
                                    float minDist = 1f, float maxDist = 10000f)
        {
            if (!_initialized) return null;
            Sound sound = GetOrLoad(name, streamed: false, is3d: true);
            if (sound == null) return null;

            // Play paused so the first audible frame is already positioned/attenuated.
            if (!TryPlay(sound, paused: true, out Channel channel)) return null;
            channel.Is3D = true;
            channel.MinDistance3D = minDist;
            channel.MaxDistance3D = maxDist;
            channel.Position3D = ToFmod(worldPosition);
            channel.Velocity3D = Vector3.Zero;
            channel.Volume = volume;
            channel.Looping = loop;
            channel.Paused = false;
            return channel;
        }

        /// <summary>
        /// Plays a sound whose emitter follows a live position (e.g. an entity), updated
        /// every frame in <see cref="UpdateListener"/> until the channel stops.
        /// </summary>
        public Channel? PlaySound3D(string name, Func<Vector3> positionSource, float volume = 1f, bool loop = false,
                                    float minDist = 1f, float maxDist = 10000f)
        {
            if (!_initialized || positionSource == null) return null;
            Channel? channel = PlaySound3D(name, positionSource(), volume, loop, minDist, maxDist);
            if (channel.HasValue)
                _tracked.Add(new Tracked3D { Channel = channel.Value, PositionSource = positionSource });
            return channel;
        }

        public void StopInstance(Channel channel)
        {
            try { channel.Stop(); } catch { /* handle may already be invalid */ }
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i].Channel.Equals(channel)) _tracked.RemoveAt(i);
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  MUSIC (streamed)
        ////////////////////////////////////////////////////////////////////////////////

        public Channel? PlayMusic(string name, float volume = 1f, bool loop = true)
        {
            if (!_initialized) return null;
            StopMusic();
            Sound sound = GetOrLoad(name, streamed: true, is3d: false);
            if (sound == null) return null;
            if (!TryPlay(sound, paused: false, out Channel channel)) return null;

            channel.Is3D = false;
            channel.Volume = volume;
            channel.Looping = loop;
            _music = channel;
            return channel;
        }

        public void StopMusic()
        {
            if (_music == null) return;
            try { _music.Value.Stop(); } catch { /* invalid handle */ }
            _music = null;
        }

        public void PauseMusic() { if (_music.HasValue) { try { Channel c = _music.Value; c.Paused = true; } catch { } } }
        public void ResumeMusic() { if (_music.HasValue) { try { Channel c = _music.Value; c.Paused = false; } catch { } } }

        ////////////////////////////////////////////////////////////////////////////////
        //  GLOBAL
        ////////////////////////////////////////////////////////////////////////////////

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = MathHelper.Clamp(value, 0f, 1f);
                if (!_initialized) return;
                if (CoreSystem.Native.getMasterChannelGroup(out var masterGroup) == FMOD.RESULT.OK)
                    masterGroup.setVolume(_masterVolume);
            }
        }

        public void StopAll()
        {
            StopMusic();
            for (int i = 0; i < _tracked.Count; i++)
            {
                try { _tracked[i].Channel.Stop(); } catch { }
            }
            _tracked.Clear();
        }

        public void Dispose()
        {
            if (!_initialized) return;
            try { StopAll(); } catch { }

            foreach (Sound s in _cache2D.Values) { try { s.Dispose(); } catch { } }
            foreach (Sound s in _cache3D.Values) { try { s.Dispose(); } catch { } }
            _cache2D.Clear();
            _cache3D.Clear();

            try { _listener?.Destroy(); } catch { }
            _listener = null;

            try { FmodManager.Unload(); } catch { }
            _initialized = false;
            if (ReferenceEquals(Audio.Instance, this)) Audio.Instance = null;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  HELPERS
        ////////////////////////////////////////////////////////////////////////////////

        // Plays a sound through the raw FMOD API and wraps the result with the SAFE
        // single-arg Channel ctor. This deliberately avoids FmodForFoxes' 2-arg
        // Channel(Sound, FMOD.Channel) ctor, which reads its computed `Sound` property
        // (getCurrentSound -> getUserData -> linker) during construction and throws a
        // NullReferenceException whenever playSound returns a bad channel (e.g. an FMOD
        // native/bindings version mismatch). Here we check the result first and degrade
        // to a logged no-op instead of crashing.
        private static bool TryPlay(Sound sound, bool paused, out Channel channel)
        {
            channel = default;
            if (sound == null) return false;

            FMOD.RESULT result = CoreSystem.Native.playSound(
                sound.Native, default, paused, out FMOD.Channel native);

            if (result != FMOD.RESULT.OK)
            {
                EditorBridge.Log("Audio: playSound failed (" + result +
                                 ") — often a wrong fmod.dll version; FmodForFoxes 3.2.0 needs FMOD 2.02.x.");
                return false;
            }

            channel = new Channel(native);
            return true;
        }

        private Sound GetOrLoad(string name, bool streamed, bool is3d)
        {
            Dictionary<string, Sound> cache = is3d ? _cache3D : _cache2D;
            if (cache.TryGetValue(name, out Sound cached)) return cached;

            string rel = "Audio/" + (name.Contains('.') ? name : name + ".wav");
            Sound sound;
            try
            {
                sound = streamed ? CoreSystem.LoadStreamedSound(rel) : CoreSystem.LoadSound(rel);
            }
            catch (Exception e)
            {
                EditorBridge.Log("Audio: failed to load '" + rel + "': " + e.Message);
                return null;
            }

            sound.Is3D = is3d;
            cache[name] = sound;
            return sound;
        }

        // The engine world is Z-up, right-handed; FMOD core defaults to a left-handed
        // frame. Identity preserves distances (so attenuation is correct) but L/R panning
        // may be mirrored. If the 3D test pans the wrong way, switch to the remap below.
        private static Vector3 ToFmod(Vector3 v) => v;
        // private static Vector3 ToFmod(Vector3 v) => new Vector3(v.X, v.Z, v.Y);
    }

    /// <summary>
    /// Static facade over the live <see cref="AudioManager"/> (mirrors the
    /// <see cref="Globals"/> convention) so gameplay code can call e.g.
    /// <c>Audio.PlaySound("blip")</c> without threading a reference through.
    /// All calls are null-safe before the manager has initialized.
    /// </summary>
    public static class Audio
    {
        internal static AudioManager Instance;

        public static Channel? PlaySound(string name, float volume = 1f, float pitch = 1f, bool loop = false)
            => Instance?.PlaySound(name, volume, pitch, loop);

        public static Channel? PlaySound3D(string name, Vector3 position, float volume = 1f, bool loop = false)
            => Instance?.PlaySound3D(name, position, volume, loop);

        public static Channel? PlaySound3D(string name, Func<Vector3> positionSource, float volume = 1f, bool loop = false)
            => Instance?.PlaySound3D(name, positionSource, volume, loop);

        public static Channel? PlayMusic(string name, float volume = 1f, bool loop = true)
            => Instance?.PlayMusic(name, volume, loop);

        public static void StopMusic() => Instance?.StopMusic();

        public static void StopAll() => Instance?.StopAll();

        public static float MasterVolume
        {
            get => Instance?.MasterVolume ?? 0f;
            set { if (Instance != null) Instance.MasterVolume = value; }
        }
    }
}
