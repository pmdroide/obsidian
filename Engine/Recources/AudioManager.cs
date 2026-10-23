using System;
using System.Collections.Generic;
using Engine.Components;
using Engine.Physics;
using Engine.Editor;
using Engine.Entities;
using FmodForFoxes;
using Microsoft.Xna.Framework;

namespace Engine.Recources
{
    /// <summary>
    /// The mixer bus a sound plays on. Music and Effects each have their own volume and both go through
    /// <see cref="Master"/>, so a sound's loudness is its own volume x its bus volume x the master volume.
    /// Saved by name (Audio component Output), so never rename a value.
    /// </summary>
    public enum AudioBus
    {
        /// <summary>Sound effects: one-shots, loops and 3D sounds. The default for everything but music.</summary>
        Effects,
        /// <summary>Music tracks (<see cref="AudioManager.PlayMusic"/>, Audio components set to Music).</summary>
        Music,
        /// <summary>The master bus itself: only the master volume applies to a sound played here.</summary>
        Master,
    }

    /// <summary>
    /// How a 3D sound gets quieter between its min and max distance. Saved by name (Audio component Rolloff),
    /// so never rename a value.
    /// </summary>
    public enum AudioRolloff
    {
        /// <summary>Real-world falloff: full within Min, half at 2x Min, a quarter at 4x...; stops getting quieter at Max (never silent).</summary>
        Inverse,
        /// <summary>Straight fade from full at Min to silent at Max.</summary>
        Linear,
        /// <summary>Like Linear but holds volume longer near Min and drops faster near Max.</summary>
        LinearSquare,
        /// <summary>Inverse near the source, tapering to silent at Max.</summary>
        InverseTapered,
    }

    /// <summary>
    /// Game audio subsystem built on FMOD via the FmodForFoxes wrapper.
    /// Follows the engine's Load/Initialize/Update/Dispose subsystem convention and is
    /// wired into <see cref="Engine.Logic.ScreenManager"/>. Supports 2D one-shot SFX,
    /// looping/instanced sounds, 3D positional audio (listener driven by the active
    /// camera, emitters by world positions), streamed music, and master/music/effects volumes
    /// (FMOD channel groups: Music and Effects are children of the master group, see <see cref="AudioBus"/>).
    ///
    /// FMOD's native libraries (fmod.dll / fmodL.dll) are supplied locally by the dev
    /// from fmod.com and copied next to the executable. If they are missing, Initialize
    /// fails gracefully and every call becomes a no-op so the engine still runs.
    ///
    /// Note: FmodForFoxes' <see cref="Channel"/> is a value type (a thin wrapper around an
    /// FMOD handle), so playback methods return <c>Channel?</c> — null means "not played".
    /// </summary>
    public partial class AudioManager : IDisposable
    {
        private sealed class Tracked3D
        {
            public Channel Channel;
            /// <summary>Null for a sound at a fixed position.</summary>
            public Func<Vector3> PositionSource;
            public Vector3 Position, Velocity;
            /// <summary>Its own colliders don't muffle it (a radio on a static table is fine, but not one inside a static crate).</summary>
            public BasicEntity Owner;
            /// <summary>0..1, how much walls muffle this sound (Audio component Wall Muffling).</summary>
            public float WallMuffling = 1;
            public float Walls, Occlusion, Dullness;
            public bool Moved;
        }

        /// <summary>What <see cref="AudioManager"/> works out for a 3D sound each frame.</summary>
        /// <param name="Occlusion">0..1 of the sound the walls stop (quieter).</param>
        /// <param name="Dullness">0..1 of extra treble the walls' materials take away (muffled).</param>
        public readonly record struct EmitterState(Vector3 Position, Vector3 Velocity, float Walls, float Occlusion, float Dullness);

        private INativeFmodLibrary _native;
        private Listener3D _listener;
        private bool _initialized;

        // Separate caches for 2D vs 3D so toggling FMOD's 3D mode on a shared Sound
        // can never make a 2D one-shot suddenly spatialise (or vice versa).
        private readonly Dictionary<string, Sound> _cache2D = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Sound> _cache3D = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Tracked3D> _tracked = new List<Tracked3D>();
        private Channel? _music;
        private FMOD.ChannelGroup _musicGroup, _effectsGroup;
        private float _masterVolume = 1f, _musicVolume = 1f, _effectsVolume = 1f;
        private bool _isMuted;
        private FMOD.REVERB_PROPERTIES _reverb = FMOD.PRESET.OFF();
        private bool _reverbSet;
        private Vector3 _listenerPosition, _listenerVelocity;
        private bool _listenerMoved;

        /// <summary>Speed of sound in m/s (FMOD's default, Doppler).</summary>
        public const float SpeedOfSound = 340f;
        /// <summary>Faster than this (m/s) is a teleport, not motion: no Doppler jump when a camera or emitter is moved far.</summary>
        public const float MaxDopplerSpeed = 120f;
        /// <summary>0..1 of the direct sound a Default-material wall between the listener and a 3D sound blocks (volume + low-pass).</summary>
        public float WallOcclusion { get; set; } = 0.6f;
        /// <summary>0..1 of a wall's occlusion that also applies to the sound's reverb (the room on its side still rings).</summary>
        public float WallReverbOcclusion { get; set; } = 0.5f;
        /// <summary>The colliders walls are read from; null = <see cref="ScenePhysics.Current"/>.</summary>
        public ScenePhysics Physics { get; set; }
        /// <summary>The listener's velocity (m/s, smoothed) as given to FMOD for Doppler.</summary>
        public Vector3 ListenerVelocity => _listenerVelocity;
        /// <summary>False when FMOD failed to start (fmod.dll missing or the wrong version): every call is a no-op.</summary>
        public bool IsAvailable => _initialized;
        /// <summary>Source content root for live editor assets; null in standalone games.</summary>
        public string EditorContentRoot { get; set; }

        ////////////////////////////////////////////////////////////////////////////////
        //  LIFECYCLE
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Boots FMOD. <paramref name="contentRoot"/> is combined with the requested
        /// path by FmodForFoxes' FileLoader (TitleContainer, i.e. relative to the app
        /// base directory), so sounds placed under Content/Audio load as "Audio/name".
        /// </summary>
        /// <param name="silent">Mix without a sound device (FMOD's no-sound output, real time): checks and headless runs.</param>
        public void Initialize(string contentRoot = "Content", bool silent = false)
        {
            // Set even if FMOD fails below: every call is then a no-op, but the volume settings still hold their values.
            Audio.Instance = this;
            uint expected = (uint)FMOD.VERSION.number;
            EditorBridge.Log($"Audio: FmodForFoxes bindings require FMOD {FormatFmodVersion(expected)} " +
                             $"(0x{expected:X8}). fmod.dll/fmodL.dll must be EXACTLY this version (x64).");
            try
            {
                _native = new DesktopNativeFmodLibrary();
                // The world is right-handed (Z up), FMOD's default is left-handed: without this flag left and right swap.
                FmodManager.Init(_native, FmodInitMode.Core, contentRoot,
                    coreInitFlags: FMOD.INITFLAGS.CHANNEL_LOWPASS | FMOD.INITFLAGS.CHANNEL_DISTANCEFILTER | FMOD.INITFLAGS._3D_RIGHTHANDED,
                    preInitAction: silent ? () => CoreSystem.Native.setOutput(FMOD.OUTPUTTYPE.NOSOUND) : null);
                _listener = new Listener3D();
                // New channel groups start as children of the master group.
                CoreSystem.Native.createChannelGroup("Music", out _musicGroup);
                CoreSystem.Native.createChannelGroup("Effects", out _effectsGroup);
                _initialized = true;
                MasterVolume = _masterVolume;
                MusicVolume = _musicVolume;
                EffectsVolume = _effectsVolume;
                IsMuted = _isMuted;
                if (_dopplerScale != 1f) DopplerScale = _dopplerScale;

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
        /// their position sources, then reaps any finished channels, and sets the reverb of the
        /// Reverb Zones of <paramref name="entities"/> around the listener. Called once per frame
        /// after scene logic has advanced positions.
        /// </summary>
        /// <param name="dt">Seconds since the last call: listener and emitter velocities (Doppler) and
        /// the easing of wall muffling. 0 keeps the velocities.</param>
        public void UpdateListener(Camera cam, IReadOnlyList<BasicEntity> entities = null, float dt = 0)
        {
            if (!_initialized || _listener == null || cam == null) return;

            Vector3 forward = cam.Forward;
            if (forward != Vector3.Zero) forward.Normalize();
            Vector3 up = cam.Up;
            if (up != Vector3.Zero) up.Normalize();

            Vector3 listener = cam.Position;
            _listenerVelocity = TrackVelocity(_listenerPosition, listener, _listenerVelocity, _listenerMoved, dt);
            _listenerPosition = listener;
            _listenerMoved = true;
            _listener.SetAttributes(ToFmod(listener), ToFmod(_listenerVelocity), ToFmod(forward), ToFmod(up));

            ScenePhysics physics = Physics ?? ScenePhysics.Current;
            // Muffling eases in/out over ~0.15 s so walking past a door edge doesn't click.
            float ease = dt > 0 ? 1 - MathF.Exp(-dt * 15f) : 1;
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Tracked3D t = _tracked[i];
                if (!t.Channel.IsPlaying)
                {
                    _tracked.RemoveAt(i);
                    continue;
                }
                Vector3 position = t.PositionSource?.Invoke() ?? t.Position;
                t.Velocity = TrackVelocity(t.Position, position, t.Velocity, t.Moved, dt);
                t.Position = position;
                t.Moved = true;
                t.Channel.Position3D = ToFmod(position);
                t.Channel.Velocity3D = ToFmod(t.Velocity);

                // Each wall's Sound Material sets how much it stops (occlusion: quieter) and how much treble it
                // takes on top (FMOD's low-pass gain: duller); Wall Muffling scales both.
                AcousticPath path = t.WallMuffling > 0 ? AudioOcclusion.Trace(physics, listener, position, t.Owner, WallOcclusion) : AcousticPath.Open;
                float muffling = MathHelper.Clamp(t.WallMuffling, 0, 1);
                t.Walls = path.Walls;
                float occlusionTarget = path.Occlusion * muffling, dullTarget = path.Dullness * muffling;
                float occlusion = Ease(t.Occlusion, occlusionTarget, ease), dullness = Ease(t.Dullness, dullTarget, ease);
                if (occlusion != t.Occlusion)
                {
                    t.Occlusion = occlusion;
                    t.Channel.Native.set3DOcclusion(occlusion, occlusion * MathHelper.Clamp(WallReverbOcclusion, 0, 1));
                }
                if (dullness != t.Dullness)
                {
                    t.Dullness = dullness;
                    t.Channel.Native.setLowPassGain(1 - dullness);
                }
            }

            SetReverb(ReverbZoneComponent.Evaluate(entities, listener));
        }

        // Moves towards the target, landing on it exactly once close (so FMOD isn't called forever).
        private static float Ease(float current, float target, float t)
        {
            float next = MathHelper.Lerp(current, target, t);
            return MathF.Abs(next - target) < 1e-3f ? target : next;
        }

        // Velocity from the position change, smoothed (frame times jitter); a jump faster than
        // MaxDopplerSpeed is a teleport and stops instead of sending a huge pitch spike.
        private static Vector3 TrackVelocity(Vector3 last, Vector3 now, Vector3 velocity, bool hasLast, float dt)
        {
            if (!hasLast) return Vector3.Zero;
            if (dt <= 0) return velocity;
            Vector3 raw = (now - last) / dt;
            if (!float.IsFinite(raw.X + raw.Y + raw.Z) || raw.LengthSquared() > MaxDopplerSpeed * MaxDopplerSpeed) return Vector3.Zero;
            return Vector3.Lerp(velocity, raw, 1 - MathF.Exp(-dt * 12f));
        }

        /// <summary>
        /// Sets what only the engine knows about a playing 3D sound: the gameobject it belongs to (whose own
        /// colliders never muffle it) and how much walls muffle it (0..1).
        /// </summary>
        public void SetEmitter(Channel channel, BasicEntity owner, float wallMuffling)
        {
            Tracked3D t = Find(channel);
            if (t == null) return;
            t.Owner = owner;
            SetWallMuffling(channel, wallMuffling);
        }

        /// <summary>0..1, how much walls muffle a playing 3D sound.</summary>
        public void SetWallMuffling(Channel channel, float wallMuffling)
        {
            if (Find(channel) is { } t) t.WallMuffling = float.IsFinite(wallMuffling) ? MathHelper.Clamp(wallMuffling, 0, 1) : 1;
        }

        /// <summary>Position, velocity, walls and muffling of a playing 3D sound; false for 2D or stopped ones.</summary>
        public bool TryGetEmitter(Channel channel, out EmitterState state)
        {
            Tracked3D t = Find(channel);
            state = t == null ? default : new EmitterState(t.Position, t.Velocity, t.Walls, t.Occlusion, t.Dullness);
            return t != null;
        }

        private Tracked3D Find(Channel channel)
        {
            for (int i = 0; i < _tracked.Count; i++)
                if (_tracked[i].Channel.Equals(channel)) return _tracked[i];
            return null;
        }

        /// <summary>The reverb every sound sends to (FMOD reverb instance 0), as last set by the Reverb Zones.</summary>
        public FMOD.REVERB_PROPERTIES Reverb => _reverb;

        private void SetReverb(FMOD.REVERB_PROPERTIES reverb)
        {
            if (_reverbSet && reverb.Equals(_reverb)) return;
            _reverb = reverb;
            _reverbSet = CoreSystem.Native.setReverbProperties(0, ref reverb) == FMOD.RESULT.OK;
        }

        /// <summary>Sets a 3D channel's distance falloff curve.</summary>
        public static void SetRolloff(Channel channel, AudioRolloff rolloff)
        {
            FMOD.MODE mode = rolloff switch
            {
                AudioRolloff.Linear => FMOD.MODE._3D_LINEARROLLOFF,
                AudioRolloff.LinearSquare => FMOD.MODE._3D_LINEARSQUAREROLLOFF,
                AudioRolloff.InverseTapered => FMOD.MODE._3D_INVERSETAPEREDROLLOFF,
                _ => FMOD.MODE._3D_INVERSEROLLOFF,
            };
            channel.Native.setMode(FMOD.MODE._3D | mode);
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  2D
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// One-shot or looping 2D (non-positional) sound. <paramref name="configure"/> sets more of the channel
        /// (pan, reverb send...) before the first audible frame.
        /// </summary>
        public Channel? PlaySound(string name, float volume = 1f, float pitch = 1f, bool loop = false, bool contentRelativePath = false,
                                  AudioBus bus = AudioBus.Effects, Action<Channel> configure = null)
        {
            if (!_initialized) return null;
            Sound sound = GetOrLoad(name, streamed: false, is3d: false, contentRelativePath);
            if (sound == null) return null;
            if (!TryPlay(sound, GroupFor(bus), paused: true, out Channel channel)) return null;

            channel.Is3D = false;
            channel.Volume = volume;
            channel.Pitch = pitch;
            configure?.Invoke(channel);
            channel.Looping = loop;
            channel.Paused = false;
            return channel;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  3D POSITIONAL
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Plays a sound at a fixed world position. <paramref name="configure"/> sets more of the channel
        /// (rolloff, spread, reverb send...) before the first audible frame.
        /// </summary>
        public Channel? PlaySound3D(string name, Vector3 worldPosition, float volume = 1f, bool loop = false,
                                    float minDist = 1f, float maxDist = 10000f, bool contentRelativePath = false,
                                    AudioBus bus = AudioBus.Effects, Action<Channel> configure = null)
        {
            if (!_initialized) return null;
            Sound sound = GetOrLoad(name, streamed: false, is3d: true, contentRelativePath);
            if (sound == null) return null;

            // Play paused so the first audible frame is already positioned/attenuated.
            if (!TryPlay(sound, GroupFor(bus), paused: true, out Channel channel)) return null;
            channel.Is3D = true;
            channel.MinDistance3D = minDist;
            channel.MaxDistance3D = maxDist;
            channel.Position3D = ToFmod(worldPosition);
            channel.Velocity3D = Vector3.Zero;
            channel.Volume = volume;
            configure?.Invoke(channel);
            channel.Looping = loop;
            // Tracked for wall muffling (and Doppler when it moves) until it stops.
            _tracked.Add(new Tracked3D { Channel = channel, Position = worldPosition });
            channel.Paused = false;
            return channel;
        }

        /// <summary>
        /// Plays a sound whose emitter follows a live position (e.g. an entity), updated
        /// every frame in <see cref="UpdateListener"/> until the channel stops.
        /// </summary>
        public Channel? PlaySound3D(string name, Func<Vector3> positionSource, float volume = 1f, bool loop = false,
                                    float minDist = 1f, float maxDist = 10000f, bool contentRelativePath = false,
                                    AudioBus bus = AudioBus.Effects, Action<Channel> configure = null)
        {
            if (!_initialized || positionSource == null) return null;
            Channel? channel = PlaySound3D(name, positionSource(), volume, loop, minDist, maxDist, contentRelativePath, bus, configure);
            if (channel.HasValue && Find(channel.Value) is { } tracked) tracked.PositionSource = positionSource;
            return channel;
        }

        public void StopInstance(Channel channel)
        {
            try { channel.Stop(); } catch { /* handle may already be invalid */ }
            _fades.Remove(channel.Native.handle);
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i].Channel.Equals(channel)) _tracked.RemoveAt(i);
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  MUSIC (streamed)
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Streams a track on the <see cref="AudioBus.Music"/> bus, replacing the current one (one at a time).
        /// <paramref name="crossfade"/> seconds: the old track fades out while the new one fades in.
        /// </summary>
        public Channel? PlayMusic(string name, float volume = 1f, bool loop = true, bool contentRelativePath = false,
                                  float crossfade = 0f)
        {
            if (!_initialized) return null;
            if (crossfade > 0 && _music is { } old && IsMusicPlaying)
            {
                Fade(old, 0, crossfade, stopAtEnd: true);
                _music = null;
            }
            else StopMusic();
            Sound sound = GetOrLoad(name, streamed: true, is3d: false, contentRelativePath);
            if (sound == null) return null;
            if (!TryPlay(sound, _musicGroup, paused: true, out Channel channel)) return null;

            channel.Is3D = false;
            channel.Volume = volume;
            // Music isn't in the room: keep it out of the Reverb Zones' reverb.
            channel.Native.setReverbProperties(0, 0f);
            channel.Looping = loop;
            if (crossfade > 0) Fade(channel, 1, crossfade, from: 0);
            channel.Paused = false;
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

        /// <summary>True while the track started by <see cref="PlayMusic"/> is playing.</summary>
        public bool IsMusicPlaying
        {
            get
            {
                if (!_music.HasValue) return false;
                try { return _music.Value.IsPlaying; } catch { return false; }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  GLOBAL
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>0..1, scales every sound and music track (FMOD's master channel group).</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = ClampVolume(value);
                if (!_initialized) return;
                if (CoreSystem.Native.getMasterChannelGroup(out var masterGroup) == FMOD.RESULT.OK)
                    masterGroup.setVolume(_masterVolume);
            }
        }

        /// <summary>0..1, scales the <see cref="AudioBus.Music"/> bus, on top of the master volume.</summary>
        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = ClampVolume(value);
                if (_initialized && _musicGroup.hasHandle()) _musicGroup.setVolume(_musicVolume);
            }
        }

        /// <summary>0..1, scales the <see cref="AudioBus.Effects"/> bus, on top of the master volume.</summary>
        public float EffectsVolume
        {
            get => _effectsVolume;
            set
            {
                _effectsVolume = ClampVolume(value);
                if (_initialized && _effectsGroup.hasHandle()) _effectsGroup.setVolume(_effectsVolume);
            }
        }

        /// <summary>The volume of one bus (0..1); <see cref="AudioBus.Master"/> is <see cref="MasterVolume"/>.</summary>
        public float GetVolume(AudioBus bus) => bus switch
        {
            AudioBus.Music => MusicVolume,
            AudioBus.Effects => EffectsVolume,
            _ => MasterVolume,
        };

        /// <summary>Sets the volume of one bus, clamped to 0..1. Sounds already playing follow at once.</summary>
        public void SetVolume(AudioBus bus, float volume)
        {
            switch (bus)
            {
                case AudioBus.Music: MusicVolume = volume; break;
                case AudioBus.Effects: EffectsVolume = volume; break;
                default: MasterVolume = volume; break;
            }
        }

        private static float ClampVolume(float value) => float.IsFinite(value) ? MathHelper.Clamp(value, 0f, 1f) : 1f;

        /// <summary>Mute output without changing master or per-component volumes.</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                if (!_initialized) return;
                if (CoreSystem.Native.getMasterChannelGroup(out var masterGroup) == FMOD.RESULT.OK)
                    masterGroup.setMute(value);
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
            if (ReferenceEquals(Audio.Instance, this)) Audio.Instance = null;
            if (!_initialized) return;
            try { StopAll(); } catch { }

            foreach (Sound s in _cache2D.Values) { try { s.Dispose(); } catch { } }
            foreach (Sound s in _cache3D.Values) { try { s.Dispose(); } catch { } }
            _cache2D.Clear();
            _cache3D.Clear();

            try { ReleaseBusEffects(); } catch { }
            try { _listener?.Destroy(); } catch { }
            _listener = null;

            try { if (_musicGroup.hasHandle()) _musicGroup.release(); } catch { }
            try { if (_effectsGroup.hasHandle()) _effectsGroup.release(); } catch { }
            _musicGroup = default;
            _effectsGroup = default;
            _reverbSet = false;

            try { FmodManager.Unload(); } catch { }
            _initialized = false;
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
        private static bool TryPlay(Sound sound, FMOD.ChannelGroup group, bool paused, out Channel channel)
        {
            channel = default;
            if (sound == null) return false;

            // A default (null) group plays on the master group.
            FMOD.RESULT result = CoreSystem.Native.playSound(
                sound.Native, group, paused, out FMOD.Channel native);

            if (result != FMOD.RESULT.OK)
            {
                EditorBridge.Log("Audio: playSound failed (" + result +
                                 ") — often a wrong fmod.dll version; FmodForFoxes 3.2.0 needs FMOD 2.02.x.");
                return false;
            }

            channel = new Channel(native);
            return true;
        }

        private FMOD.ChannelGroup GroupFor(AudioBus bus) => bus switch
        {
            AudioBus.Music => _musicGroup,
            AudioBus.Effects => _effectsGroup,
            _ => default,
        };

        private Sound GetOrLoad(string name, bool streamed, bool is3d, bool contentRelativePath = false)
        {
            Dictionary<string, Sound> cache = is3d ? _cache3D : _cache2D;
            string rel = contentRelativePath ? name.Replace('\\', '/')
                : "Audio/" + (name.Contains('.') ? name : name + ".wav");
            if (Path.IsPathRooted(rel) || rel.Split('/').Any(p => p is ".." or "."))
            {
                EditorBridge.Log("Audio: invalid content path '" + rel + "'");
                return null;
            }
            if (cache.TryGetValue(rel, out Sound cached)) return cached;
            Sound sound;
            try
            {
                // Assets imported while Anvil is running have not gone through a build yet.
                if (EditorContentRoot != null)
                {
                    string source = Path.Combine(EditorContentRoot, rel);
                    string target = Path.Combine(AppContext.BaseDirectory, "Content", rel);
                    if (File.Exists(source) && !source.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                        (!File.Exists(target) || new FileInfo(source).Length != new FileInfo(target).Length ||
                         File.GetLastWriteTimeUtc(source) != File.GetLastWriteTimeUtc(target)))
                    {
                        // A sound cached in the other spatial mode can still hold the
                        // target open. Reuse an up-to-date runtime copy in that case.
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(source, target, overwrite: true);
                    }
                }
                sound = streamed ? CoreSystem.LoadStreamedSound(rel) : CoreSystem.LoadSound(rel);
            }
            catch (Exception e)
            {
                EditorBridge.Log("Audio: failed to load '" + rel + "': " + e.Message);
                return null;
            }

            sound.Is3D = is3d;
            cache[rel] = sound;
            return sound;
        }

        // The engine world is Z-up, right-handed (metres; FMOD's distance factor 1 = metres, so Doppler
        // uses 340 m/s). FMOD takes the up axis from the listener's
        // up vector, and Initialize sets FMOD_INIT_3D_RIGHTHANDED, so positions pass through
        // unchanged and left/right pan correctly (Scenes/AudioTest.obsc checks it).
        private static Vector3 ToFmod(Vector3 v) => v;
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

        public static Channel? PlaySound(string name, float volume = 1f, float pitch = 1f, bool loop = false,
                                         AudioBus bus = AudioBus.Effects)
            => Instance?.PlaySound(name, volume, pitch, loop, bus: bus);

        public static Channel? PlaySound3D(string name, Vector3 position, float volume = 1f, bool loop = false,
                                           AudioBus bus = AudioBus.Effects)
            => Instance?.PlaySound3D(name, position, volume, loop, bus: bus);

        public static Channel? PlaySound3D(string name, Func<Vector3> positionSource, float volume = 1f, bool loop = false,
                                           AudioBus bus = AudioBus.Effects)
            => Instance?.PlaySound3D(name, positionSource, volume, loop, bus: bus);

        public static Channel? PlayMusic(string name, float volume = 1f, bool loop = true, float crossfade = 0f)
            => Instance?.PlayMusic(name, volume, loop, crossfade: crossfade);

        public static void StopMusic() => Instance?.StopMusic();
        public static void PauseMusic() => Instance?.PauseMusic();
        public static void ResumeMusic() => Instance?.ResumeMusic();

        /// <summary>Pauses or resumes every sound on a bus (Master = everything).</summary>
        public static void SetPaused(AudioBus bus, bool paused) => Instance?.SetBusPaused(bus, paused);
        public static bool IsPaused(AudioBus bus) => Instance?.IsBusPaused(bus) ?? false;

        /// <summary>Speed/pitch of a whole bus, 0.1..4 (0.5 = slow motion). Reset when Play stops.</summary>
        public static void SetPitch(AudioBus bus, float pitch) => Instance?.SetBusPitch(bus, pitch);
        public static float GetPitch(AudioBus bus) => Instance?.GetBusPitch(bus) ?? 1f;

        /// <summary>Low-pass a whole bus: cut above <paramref name="cutoffHz"/> (500 = underwater, 22000 = off). Reset when Play stops.</summary>
        public static void SetLowPass(AudioBus bus, float cutoffHz) => Instance?.SetBusLowPass(bus, cutoffHz);
        public static float GetLowPass(AudioBus bus) => Instance?.GetBusLowPass(bus) ?? AudioManager.LowPassOff;

        /// <summary>Fades a whole bus to <paramref name="level"/> (0..1) over <paramref name="seconds"/> (ducking, fade to black). Reset when Play stops.</summary>
        public static void FadeBus(AudioBus bus, float level, float seconds) => Instance?.FadeBus(bus, level, seconds);
        public static float GetBusFade(AudioBus bus) => Instance?.GetBusFade(bus) ?? 1f;

        /// <summary>RMS loudness (0..1) of a bus now; 0 on the first call (metering starts).</summary>
        public static float GetLevel(AudioBus bus) => Instance?.GetBusLevel(bus) ?? 0f;

        /// <summary>Fills <paramref name="bands"/> with the bus's spectrum (0..1, log-spaced 40 Hz..16 kHz); returns how many were filled.</summary>
        public static int GetSpectrum(AudioBus bus, float[] bands) => Instance?.GetBusSpectrum(bus, bands) ?? 0;

        /// <summary>Loads a sound now so its first play doesn't hitch; <paramref name="spatial"/> for 3D playback.</summary>
        public static bool Preload(string clipPath, bool spatial = false) => Instance?.Preload(clipPath, spatial) ?? false;

        /// <summary>Doppler strength for every sound: 0 = off, 1 = real.</summary>
        public static float DopplerScale
        {
            get => Instance?.DopplerScale ?? 1f;
            set { if (Instance != null) Instance.DopplerScale = value; }
        }

        /// <summary>Channels playing and the mixer's CPU use.</summary>
        public static AudioStats Stats => Instance?.Stats ?? default;

        public static void StopAll() => Instance?.StopAll();

        public static bool IsMusicPlaying => Instance?.IsMusicPlaying ?? false;

        /// <summary>False until FMOD has started (or when fmod.dll is missing): sounds then play silently.</summary>
        public static bool IsAvailable => Instance?.IsAvailable ?? false;

        /// <summary>The listener's (camera's) velocity in m/s, as used for Doppler.</summary>
        public static Vector3 ListenerVelocity => Instance?.ListenerVelocity ?? Vector3.Zero;

        /// <summary>0..1 of the direct sound each wall between the listener and a 3D sound blocks (default 0.6).</summary>
        public static float WallOcclusion
        {
            get => Instance?.WallOcclusion ?? 0.6f;
            set { if (Instance != null) Instance.WallOcclusion = MathHelper.Clamp(value, 0, 1); }
        }

        /// <summary>The reverb the Reverb Zones around the listener set this frame (FMOD.PRESET.OFF outside them).</summary>
        public static FMOD.REVERB_PROPERTIES Reverb => Instance?.Reverb ?? FMOD.PRESET.OFF();

        /// <summary>0..1, every sound and music track.</summary>
        public static float MasterVolume
        {
            get => GetVolume(AudioBus.Master);
            set => SetVolume(AudioBus.Master, value);
        }

        /// <summary>0..1, the music bus (multiplied by <see cref="MasterVolume"/>).</summary>
        public static float MusicVolume
        {
            get => GetVolume(AudioBus.Music);
            set => SetVolume(AudioBus.Music, value);
        }

        /// <summary>0..1, the sound effects bus (multiplied by <see cref="MasterVolume"/>).</summary>
        public static float EffectsVolume
        {
            get => GetVolume(AudioBus.Effects);
            set => SetVolume(AudioBus.Effects, value);
        }

        /// <summary>The volume of a bus, 0..1 (1 before the audio manager exists).</summary>
        public static float GetVolume(AudioBus bus) => Instance?.GetVolume(bus) ?? 1f;

        /// <summary>Sets the volume of a bus, clamped to 0..1. Sounds already playing follow at once.</summary>
        public static void SetVolume(AudioBus bus, float volume) => Instance?.SetVolume(bus, volume);

        public static bool IsMuted
        {
            get => Instance?.IsMuted ?? false;
            set { if (Instance != null) Instance.IsMuted = value; }
        }
    }
}
