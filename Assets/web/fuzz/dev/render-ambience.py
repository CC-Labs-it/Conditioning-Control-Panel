"""Original nonverbal ambience, rendered offline. No voices or external samples.
Mono tone files loop at exact cycles; rain and radio wrap through a one-second crossfade.
Run with Python's standard library. One process, a few MB of working memory.
"""
from array import array
import json
import math
from pathlib import Path
import random
import wave

RATE = 32000
DEST = Path(__file__).resolve().parents[1] / 'assets' / 'audio' / 'ambient'
DEST.mkdir(parents=True, exist_ok=True)


def noise(seconds, seed, rain):
    rng = random.Random(seed)
    n, lap = int(seconds * RATE), RATE
    raw = array('f')
    low = mid = drops = 0.0
    for i in range(n + lap):
        white = rng.uniform(-1, 1)
        low += .018 * (white - low)
        mid += .18 * (white - mid)
        if rain:
            if rng.random() < 65 / RATE:
                drops += rng.uniform(.2, .9)
            drops *= .994
            raw.append(.65 * mid + .06 * (white - mid) + .1 * (white - mid) * min(2, drops))
        else:
            raw.append(mid - low)
    result = raw[:n]
    for i in range(lap):
        mix = .5 - .5 * math.cos(math.pi * i / (lap - 1))
        result[i] = raw[n + i] * (1 - mix) + raw[i] * mix
    return result


def write(name, channels, peak):
    maximum = max(abs(v) for channel in channels for v in channel) or 1
    scale = peak / maximum
    pcm = array('h')
    for samples in zip(*channels):
        pcm.extend(round(max(-1, min(1, v * scale)) * 32767) for v in samples)
    with wave.open(str(DEST / (name + '.wav')), 'wb') as target:
        target.setnchannels(len(channels))
        target.setsampwidth(2)
        target.setframerate(RATE)
        target.writeframes(pcm.tobytes())
    rms = math.sqrt(sum(v * v for c in channels for v in c) / sum(map(len, channels))) * scale
    return {'file': name + '.wav', 'seconds': len(channels[0]) / RATE, 'channels': len(channels), 'peak': peak, 'rms': round(rms, 5)}


def tone(hz, harmonic=False):
    return array('f', (math.sin(2 * math.pi * hz * i / RATE) + (.12 * math.sin(2 * math.pi * hz * 2 * i / RATE) if harmonic else 0) for i in range(4 * RATE)))


manifest = [write('hum', [tone(220, True)], .18),
            write('hum-left', [tone(220)], .22), write('hum-right', [tone(226)], .22),
            write('rain', [noise(24, 7081, True), noise(24, 7082, True)], .45),
            write('radio', [noise(8, 7083, False)], .24)]
(DEST / 'manifest.json').write_text(json.dumps({'origin': 'Original offline nonverbal synthesis, no speech or external samples', 'sampleRate': RATE, 'binaural': {'leftHz': 220, 'rightHz': 226, 'differenceHz': 6}, 'files': manifest}, indent=2) + '\n', encoding='utf-8')
print(json.dumps(manifest, indent=2))
