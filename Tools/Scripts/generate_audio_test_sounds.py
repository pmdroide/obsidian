"""Generates the AudioTest scene's sounds into Engine/Content/Audio/Test (numpy only, deterministic).

    python Tools/Scripts/generate_audio_test_sounds.py

clap.wav          mono, a sharp hand-clap-like noise burst: dry enough that a room's reverb tail is obvious.
pulse_loop.wav    mono, 2 s seamless loop: a ticking broadband pulse over a low chord. Broadband and
                  rhythmic so direction (stereo / 3D pan) and distance are easy to hear.
engine_loop.wav   mono, 2 s seamless loop: a buzzing engine-like tone (110 Hz with harmonics). A steady
                  pitch, so the Doppler shift of a passing sound is easy to hear.
stereo_check.wav  stereo, 4 s loop: three low pips in the LEFT channel only, then three high pips in the
                  RIGHT channel only. Checks the speakers / headphones are not swapped.
"""
import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Engine", "Content", "Audio", "Test")
rng = np.random.default_rng(7)


def write(name, data):
    """data: float array (n,) mono or (n, 2) stereo, -1..1."""
    data = np.clip(data, -1, 1)
    channels = 1 if data.ndim == 1 else data.shape[1]
    pcm = (data * 32767).astype("<i2")
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print("wrote", os.path.normpath(os.path.join(OUT, name)), f"{len(data) / RATE:.2f} s")


def t(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def bandpass_noise(n, low, high):
    spectrum = np.fft.rfft(rng.standard_normal(n))
    freqs = np.fft.rfftfreq(n, 1 / RATE)
    spectrum[(freqs < low) | (freqs > high)] = 0
    noise = np.fft.irfft(spectrum, n)
    return noise / np.max(np.abs(noise))


def clap():
    # A few micro-bursts 8-12 ms apart (several hands), then a short noise decay.
    n = int(0.25 * RATE)
    out = np.zeros(n)
    noise = bandpass_noise(n, 600, 9000)
    for start_ms, gain in ((0, 0.7), (9, 0.85), (20, 1.0)):
        s = int(start_ms / 1000 * RATE)
        k = np.arange(n - s) / RATE
        out[s:] += gain * noise[: n - s] * np.exp(-k / 0.012)
    tail = np.exp(-t(0.25) / 0.035)
    out += 0.35 * noise * tail
    fade = np.ones(n)
    fade[-200:] = np.linspace(1, 0, 200)
    return 0.9 * out / np.max(np.abs(out)) * fade


def pulse_loop():
    # 2 s, every component completes whole cycles so the loop is seamless.
    time = t(2.0)
    chord = sum(np.sin(2 * np.pi * f * time) for f in (110, 165, 220)) / 3
    hum = 0.22 * chord * (0.75 + 0.25 * np.sin(2 * np.pi * 0.5 * time))
    # 8 ticks per loop: noise bursts with a bright click, every 0.25 s.
    n = len(time)
    noise = bandpass_noise(n, 1500, 12000)
    period = int(0.25 * RATE)
    env = np.exp(-(np.arange(n) % period) / RATE / 0.03)
    ticks = 0.55 * noise * env
    # Accent on the first tick so you can hear where the loop starts.
    ticks[:period] *= 1.4
    return 0.85 * (hum + ticks) / np.max(np.abs(hum + ticks))


def engine_loop():
    # Band-limited sawtooth at 110 Hz (whole cycles in 2 s) with an 8 Hz throb and a little rumble.
    time = t(2.0)
    saw = sum(np.sin(2 * np.pi * 110 * n * time) / n for n in range(1, 25))
    throb = 0.8 + 0.2 * np.sin(2 * np.pi * 8 * time)
    n = len(time)
    rumble = bandpass_noise(n, 60, 400)
    out = saw * throb + 0.25 * rumble
    return 0.8 * out / np.max(np.abs(out))


def stereo_check():
    n = int(4.0 * RATE)
    left = np.zeros(n)
    right = np.zeros(n)
    for i in range(3):
        for buf, freq, start in ((left, 440.0, 0.1), (right, 880.0, 2.1)):
            s = int((start + i * 0.35) * RATE)
            k = t(0.22)
            pip = np.sin(2 * np.pi * freq * k) * np.minimum(1, k / 0.005) * np.exp(-k / 0.09)
            buf[s:s + len(pip)] += 0.8 * pip
    return np.stack([left, right], axis=1)


if __name__ == "__main__":
    write("clap.wav", clap())
    write("pulse_loop.wav", pulse_loop())
    write("engine_loop.wav", engine_loop())
    write("stereo_check.wav", stereo_check())
