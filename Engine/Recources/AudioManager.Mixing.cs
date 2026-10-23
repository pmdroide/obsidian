using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FmodForFoxes;
using Microsoft.Xna.Framework;

namespace Engine.Recources
{
    /// <summary>What FMOD is doing right now (<see cref="AudioManager.Stats"/>).</summary>
    /// <param name="Channels">Sounds playing, virtual ones included.</param>
    /// <param name="RealChannels">Sounds actually being mixed (FMOD virtualises the quietest above its limit).</param>
    /// <param name="DspCpu">Percent of one CPU core the mixer uses.</param>
    public readonly record struct AudioStats(int Channels, int RealChannels, float DspCpu);

    /// <summary>
    /// Script-facing FMOD controls on top of playback: sample-accurate fades (FMOD fade points), music
    /// crossfades, per-bus pause / pitch / low-pass / fades, bus level and spectrum metering, preloading,
    /// Doppler scale and stats. Everything a script changes on a bus is put back by <see cref="ResetMixing"/>
    /// when Play stops.
    /// </summary>
    public partial class AudioManager
    {
        /// <summary>Highest low-pass cutoff (Hz); at or above it the bus filter is bypassed.</summary>
        public const float LowPassOff = 22000f;

        // Fade state per channel / group handle, so a new fade starts from wherever the last one is now.
        private readonly struct FadeState
        {
            public readonly ulong Start, End;
            public readonly float From, To;
            public FadeState(ulong start, ulong end, float from, float to) { Start = start; End = end; From = from; To = to; }
            public float At(ulong clock) =>
                clock >= End ? To : clock <= Start ? From : MathHelper.Lerp(From, To, (float)(clock - Start) / (End - Start));
        }

        private readonly Dictionary<nint, FadeState> _fades = new();
        private readonly Dictionary<AudioBus, FMOD.DSP> _busLowPass = new();
        private readonly Dictionary<AudioBus, float> _busCutoff = new();
        private readonly Dictionary<AudioBus, float> _busPitch = new();
        private readonly Dictionary<AudioBus, FMOD.DSP> _busFft = new();
        private readonly HashSet<AudioBus> _busMetering = new();
        private float[] _spectrumBuffer = Array.Empty<float>();
        private float _dopplerScale = 1f;
        private int _sampleRate;

        private int SampleRate
        {
            get
            {
                if (_sampleRate <= 0 && _initialized) CoreSystem.Native.getSoftwareFormat(out _sampleRate, out _, out _);
                return _sampleRate > 0 ? _sampleRate : 48000;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  FADES (per sound)
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Fades a playing sound's volume (on top of its Volume) to <paramref name="to"/> (0..1) over
        /// <paramref name="seconds"/>, from where its last fade is now (or <paramref name="from"/>);
        /// <paramref name="stopAtEnd"/> stops it when the fade ends. Sample-accurate (FMOD fade points), and
        /// keeps going while the game is paused.
        /// </summary>
        public void Fade(Channel channel, float to, float seconds, bool stopAtEnd = false, float? from = null)
        {
            if (!_initialized) return;
            FMOD.Channel native = channel.Native;
            if (native.getDSPClock(out _, out ulong now) != FMOD.RESULT.OK) return;
            ulong end = Schedule(native.handle, now, to, seconds, from, out float start);
            native.removeFadePoints(now, ulong.MaxValue);
            native.addFadePoint(now, start);
            native.addFadePoint(end, MathHelper.Clamp(to, 0, 1));
            if (stopAtEnd) native.setDelay(0, end, true);
        }

        /// <summary>The fade level (0..1) a sound is at now: 1 if it was never faded.</summary>
        public float FadeLevel(Channel channel)
        {
            if (!_initialized || !_fades.TryGetValue(channel.Native.handle, out FadeState fade)) return 1;
            return channel.Native.getDSPClock(out _, out ulong now) == FMOD.RESULT.OK ? fade.At(now) : fade.To;
        }

        private ulong Schedule(nint handle, ulong now, float to, float seconds, float? from, out float start)
        {
            start = from ?? (_fades.TryGetValue(handle, out FadeState last) ? last.At(now) : 1f);
            start = MathHelper.Clamp(start, 0, 1);
            ulong samples = (ulong)Math.Max(1, (float.IsFinite(seconds) ? Math.Max(seconds, 0) : 0) * SampleRate);
            if (_fades.Count > 256) PruneFades();
            _fades[handle] = new FadeState(now, now + samples, start, MathHelper.Clamp(to, 0, 1));
            return now + samples;
        }

        // Fade records of channels that have stopped (bus records stay: groups live as long as FMOD).
        private void PruneFades()
        {
            var stale = new List<nint>();
            foreach (nint handle in _fades.Keys)
            {
                if (IsBusHandle(handle)) continue;
                var channel = new FMOD.Channel { handle = handle };
                if (channel.isPlaying(out bool playing) != FMOD.RESULT.OK || !playing) stale.Add(handle);
            }
            foreach (nint handle in stale) _fades.Remove(handle);
        }

        private bool IsBusHandle(nint handle) =>
            handle == _musicGroup.handle || handle == _effectsGroup.handle ||
            (TryGetGroup(AudioBus.Master, out FMOD.ChannelGroup master) && handle == master.handle);

        ////////////////////////////////////////////////////////////////////////////////
        //  BUSES
        ////////////////////////////////////////////////////////////////////////////////

        private bool TryGetGroup(AudioBus bus, out FMOD.ChannelGroup group)
        {
            group = default;
            if (!_initialized) return false;
            if (bus == AudioBus.Master) return CoreSystem.Native.getMasterChannelGroup(out group) == FMOD.RESULT.OK;
            group = GroupFor(bus);
            return group.hasHandle();
        }

        /// <summary>
        /// Pauses or resumes every sound on a bus (Master = everything). A pause menu pauses Effects and keeps
        /// Music; its own clicks then need the Master bus (<c>PlaySound(..., bus: AudioBus.Master)</c>).
        /// </summary>
        public void SetBusPaused(AudioBus bus, bool paused)
        {
            if (TryGetGroup(bus, out FMOD.ChannelGroup group)) group.setPaused(paused);
        }

        public bool IsBusPaused(AudioBus bus) =>
            TryGetGroup(bus, out FMOD.ChannelGroup group) && group.getPaused(out bool paused) == FMOD.RESULT.OK && paused;

        /// <summary>Speed/pitch of a whole bus (0.1..4): 0.5 = slow motion an octave down. 1 = normal.</summary>
        public void SetBusPitch(AudioBus bus, float pitch)
        {
            pitch = float.IsFinite(pitch) ? MathHelper.Clamp(pitch, 0.1f, 4f) : 1f;
            _busPitch[bus] = pitch;
            if (TryGetGroup(bus, out FMOD.ChannelGroup group)) group.setPitch(pitch);
        }

        public float GetBusPitch(AudioBus bus) => _busPitch.TryGetValue(bus, out float pitch) ? pitch : 1f;

        /// <summary>
        /// Muffles a whole bus with a low-pass filter: sound above <paramref name="cutoffHz"/> is cut (24 dB/oct).
        /// ~500 Hz = underwater / through a wall, ~2000 Hz = a pause menu, <see cref="LowPassOff"/> = off.
        /// </summary>
        public void SetBusLowPass(AudioBus bus, float cutoffHz)
        {
            cutoffHz = float.IsFinite(cutoffHz) ? MathHelper.Clamp(cutoffHz, 10f, LowPassOff) : LowPassOff;
            _busCutoff[bus] = cutoffHz;
            if (!TryGetGroup(bus, out FMOD.ChannelGroup group)) return;
            if (!_busLowPass.TryGetValue(bus, out FMOD.DSP dsp))
            {
                if (cutoffHz >= LowPassOff) return;
                if (CoreSystem.Native.createDSPByType(FMOD.DSP_TYPE.MULTIBAND_EQ, out dsp) != FMOD.RESULT.OK) return;
                dsp.setParameterInt((int)FMOD.DSP_MULTIBAND_EQ.A_FILTER, (int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.LOWPASS_24DB);
                // Tail = the input side: filtered before the bus fader, which stays the head GetBusLevel meters.
                group.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, dsp);
                _busLowPass[bus] = dsp;
            }
            dsp.setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.A_FREQUENCY, cutoffHz);
            dsp.setBypass(cutoffHz >= LowPassOff);
        }

        public float GetBusLowPass(AudioBus bus) => _busCutoff.TryGetValue(bus, out float cutoff) ? cutoff : LowPassOff;

        /// <summary>
        /// Fades a whole bus to <paramref name="level"/> (0..1, on top of its volume setting) over
        /// <paramref name="seconds"/>: ducking Effects under dialogue, fading everything out before a scene change.
        /// </summary>
        public void FadeBus(AudioBus bus, float level, float seconds)
        {
            if (!TryGetGroup(bus, out FMOD.ChannelGroup group)) return;
            if (group.getDSPClock(out ulong own, out ulong parent) != FMOD.RESULT.OK) return;
            ulong now = bus == AudioBus.Master || parent == 0 ? own : parent;
            ulong end = Schedule(group.handle, now, level, seconds, null, out float start);
            group.removeFadePoints(now, ulong.MaxValue);
            group.addFadePoint(now, start);
            group.addFadePoint(end, MathHelper.Clamp(level, 0, 1));
        }

        /// <summary>The fade level (0..1) a bus is at now; 1 unless <see cref="FadeBus"/> changed it.</summary>
        public float GetBusFade(AudioBus bus)
        {
            if (!TryGetGroup(bus, out FMOD.ChannelGroup group) || !_fades.TryGetValue(group.handle, out FadeState fade)) return 1;
            if (group.getDSPClock(out ulong own, out ulong parent) != FMOD.RESULT.OK) return fade.To;
            return fade.At(bus == AudioBus.Master || parent == 0 ? own : parent);
        }

        /// <summary>
        /// Loudness of a bus right now: the RMS level (0..1) of its loudest output channel. The first call
        /// switches metering on and returns 0.
        /// </summary>
        public float GetBusLevel(AudioBus bus)
        {
            if (!TryGetGroup(bus, out FMOD.ChannelGroup group) ||
                group.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, out FMOD.DSP head) != FMOD.RESULT.OK) return 0;
            if (_busMetering.Add(bus))
            {
                head.setMeteringEnabled(false, true);
                return 0;
            }
            if (head.getMeteringInfo(IntPtr.Zero, out FMOD.DSP_METERING_INFO info) != FMOD.RESULT.OK || info.rmslevel == null) return 0;
            float level = 0;
            for (int i = 0; i < Math.Min(info.numchannels, info.rmslevel.Length); i++) level = Math.Max(level, info.rmslevel[i]);
            return MathHelper.Clamp(level, 0, 1);
        }

        /// <summary>
        /// Fills <paramref name="bands"/> with the bus's frequency spectrum (each 0..1), bands spaced
        /// logarithmically from 40 Hz to 16 kHz like a music visualiser's bars. Returns how many bands were
        /// filled: 0 on the first call (the analyser starts) or without audio.
        /// </summary>
        public int GetBusSpectrum(AudioBus bus, float[] bands)
        {
            if (bands == null || bands.Length == 0) return 0;
            Array.Clear(bands);
            if (!TryGetGroup(bus, out FMOD.ChannelGroup group)) return 0;
            if (!_busFft.TryGetValue(bus, out FMOD.DSP fft))
            {
                if (CoreSystem.Native.createDSPByType(FMOD.DSP_TYPE.FFT, out fft) != FMOD.RESULT.OK) return 0;
                fft.setParameterInt((int)FMOD.DSP_FFT.WINDOWSIZE, 1024);
                // Tail: the spectrum is measured before the bus volume, so a quiet bus still shows its shape.
                group.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, fft);
                _busFft[bus] = fft;
                return 0;
            }
            if (fft.getParameterData((int)FMOD.DSP_FFT.SPECTRUMDATA, out nint data, out _) != FMOD.RESULT.OK || data == 0) return 0;
            var spectrum = Marshal.PtrToStructure<FMOD.DSP_PARAMETER_FFT>(data);
            if (spectrum.length <= 0 || spectrum.numchannels <= 0) return 0;
            if (_spectrumBuffer.Length < spectrum.length) _spectrumBuffer = new float[spectrum.length];

            float nyquist = SampleRate / 2f;
            float low = 40f, high = Math.Min(16000f, nyquist);
            for (int channel = 0; channel < spectrum.numchannels; channel++)
            {
                spectrum.getSpectrum(channel, ref _spectrumBuffer);
                for (int b = 0; b < bands.Length; b++)
                {
                    float f0 = low * MathF.Pow(high / low, (float)b / bands.Length);
                    float f1 = low * MathF.Pow(high / low, (float)(b + 1) / bands.Length);
                    int i0 = Math.Clamp((int)(f0 / nyquist * spectrum.length), 0, spectrum.length - 1);
                    int i1 = Math.Clamp((int)MathF.Ceiling(f1 / nyquist * spectrum.length), i0 + 1, spectrum.length);
                    float peak = 0;
                    for (int i = i0; i < i1; i++) peak = Math.Max(peak, _spectrumBuffer[i]);
                    bands[b] = Math.Max(bands[b], MathHelper.Clamp(peak, 0, 1));
                }
            }
            return bands.Length;
        }

        /// <summary>
        /// Puts every bus back: unpaused, pitch 1, no low-pass, no fade, Doppler scale 1. Called when Play stops so a script's
        /// slow motion or pause muffle doesn't stay on in the editor.
        /// </summary>
        public void ResetMixing()
        {
            foreach (AudioBus bus in new[] { AudioBus.Master, AudioBus.Music, AudioBus.Effects })
            {
                SetBusPaused(bus, false);
                if (_busPitch.ContainsKey(bus)) SetBusPitch(bus, 1);
                if (_busCutoff.ContainsKey(bus)) SetBusLowPass(bus, LowPassOff);
                if (TryGetGroup(bus, out FMOD.ChannelGroup group) && _fades.Remove(group.handle))
                    group.removeFadePoints(0, ulong.MaxValue);
            }
            _busPitch.Clear();
            _busCutoff.Clear();
            if (_dopplerScale != 1f) DopplerScale = 1f;
        }

        // Effects the buses own; released before the groups in Dispose.
        private void ReleaseBusEffects()
        {
            foreach (var (bus, dsp) in _busLowPass) ReleaseDsp(bus, dsp);
            foreach (var (bus, dsp) in _busFft) ReleaseDsp(bus, dsp);
            _busLowPass.Clear();
            _busFft.Clear();
            _busMetering.Clear();
            _fades.Clear();

            void ReleaseDsp(AudioBus bus, FMOD.DSP dsp)
            {
                try
                {
                    if (TryGetGroup(bus, out FMOD.ChannelGroup group)) group.removeDSP(dsp);
                    dsp.release();
                }
                catch { /* FMOD already gone */ }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  MISC
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>Doppler strength for every sound (FMOD's doppler scale): 0 = off, 1 = real, 2 = twice as strong.</summary>
        public float DopplerScale
        {
            get => _dopplerScale;
            set
            {
                _dopplerScale = float.IsFinite(value) ? MathHelper.Clamp(value, 0, 10) : 1;
                if (_initialized) CoreSystem.Native.set3DSettings(_dopplerScale, 1f, 1f);
            }
        }

        /// <summary>
        /// Loads a sound now so its first play doesn't hitch (decoding a long WAV can take a frame).
        /// <paramref name="spatial"/>: load it for 3D playback (PlaySound3D / PlaySoundAt / 3D Audio components).
        /// </summary>
        public bool Preload(string clipPath, bool spatial = false) =>
            _initialized && GetOrLoad(clipPath, streamed: false, is3d: spatial, contentRelativePath: true) != null;

        public AudioStats Stats
        {
            get
            {
                if (!_initialized) return default;
                CoreSystem.Native.getChannelsPlaying(out int channels, out int real);
                CoreSystem.Native.getCPUUsage(out FMOD.CPU_USAGE usage);
                return new AudioStats(channels, real, usage.dsp);
            }
        }

        /// <summary>Moves a playing 3D sound to a fixed <paramref name="position"/> (it stops following its gameobject).</summary>
        public void SetEmitterPosition(Channel channel, Vector3 position)
        {
            if (Find(channel) is not { } t) return;
            t.PositionSource = null;
            t.Position = position;
        }

        /// <summary>Length of the sound a channel plays, in seconds (0 if unknown).</summary>
        public static float LengthOf(Channel channel)
        {
            if (channel.Native.getCurrentSound(out FMOD.Sound sound) != FMOD.RESULT.OK) return 0;
            return sound.getLength(out uint ms, FMOD.TIMEUNIT.MS) == FMOD.RESULT.OK ? ms / 1000f : 0;
        }
    }
}
