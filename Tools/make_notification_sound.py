#!/usr/bin/env python3
"""Synthesizes the incoming-message notification pop (Profile -> Notifications).

Two quick soft sine tones (E6 -> A6, ~0.18s total) with exponential decay,
44.1kHz mono 16-bit WAV, peak ~-6dB. Output: Assets/Audio/notification_pop.wav
(the project's first bundled AudioClip; default Unity import settings are fine).
"""
import math
import os
import struct
import wave

SAMPLE_RATE = 44100
PEAK = 0.5  # ~-6 dBFS


def tone(freq_hz, duration_s, attack_s=0.004, decay_rate=18.0):
    samples = []
    n = int(SAMPLE_RATE * duration_s)
    for i in range(n):
        t = i / SAMPLE_RATE
        envelope = min(1.0, t / attack_s) * math.exp(-decay_rate * t)
        # A touch of the 2nd harmonic keeps it from sounding like a bare beep.
        value = 0.85 * math.sin(2 * math.pi * freq_hz * t) \
              + 0.15 * math.sin(2 * math.pi * freq_hz * 2 * t)
        samples.append(value * envelope)
    return samples


def main():
    e6, a6 = 1318.51, 1760.0
    data = tone(e6, 0.085) + tone(a6, 0.115)

    out_path = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Audio', 'notification_pop.wav')
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    with wave.open(out_path, 'w') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(SAMPLE_RATE)
        frames = b''.join(
            struct.pack('<h', int(max(-1.0, min(1.0, s * PEAK)) * 32767)) for s in data)
        wav.writeframes(frames)

    print('wrote', os.path.normpath(out_path), f'({len(data) / SAMPLE_RATE:.3f}s)')


if __name__ == '__main__':
    main()
