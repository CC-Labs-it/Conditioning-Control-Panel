/**
 * Renders the Sparkles flight's sounds into Resources/sounds/sparkles/:
 *   bell_00.wav .. bell_07.wav  one landing star each, A major pentatonic from A5 (the bubble lab's key)
 *   chord.wav                   the settle chord on the last star
 *   air_up.wav / air_down.wav   the breath as the stars lift off (a gain) or leave the wallet (a spend)
 *
 * The recipe is the owner's prize counter mockup v4 (functions bell, air, tick, chord): a struck bar
 * with two quiet upper partials that die sooner, through a short soft echo (210 ms, feedback 0.3,
 * wet 0.2). Every file shares ONE gain, so the mockup's balance between a tick, the chord and the
 * air is kept, and the loudest file (the chord) peaks at 0.9. SparkleSfx plays them all at one scale.
 * Offline on purpose, like render-launcher-notes.mjs: no runtime synth, no speech.
 *     node Scripts/render-sparkle-bells.mjs
 */
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', '..', 'Assets', 'sounds', 'sparkles');   // port: shared Assets tree
const SR = 32000;
const NOTES = [880, 987.77, 1108.73, 1318.51, 1479.98, 1760, 1975.53, 2217.46];   // keep in step with SparkleFlightPlan.Bells
const TAIL = 0.5;

/** Exponential up over 8 ms, exponential down to the end: the Web Audio ramps the mockup uses. */
function env(t, peak, end) {
  const lo = 0.0001, a = 0.008;
  if (t < 0 || t >= end) return 0;
  if (t <= a) return lo * (peak / lo) ** (t / a);
  return peak * (lo / peak) ** ((t - a) / (end - a));
}

function bell(buf, freq, vol, len, when = 0) {
  for (const [mul, gain, life] of [[1, 1, 1], [2.76, 0.2, 0.45], [5.4, 0.06, 0.22]]) {
    const hz = freq * mul, end = len * life;
    if (hz >= SR / 2) continue;
    const i0 = Math.round(when * SR);
    for (let i = 0; i0 + i < buf.length && i < end * SR; i++)
      buf[i0 + i] += Math.sin((2 * Math.PI * hz * i) / SR) * env(i / SR, vol * gain, end);
  }
}

/** Filtered noise sliding up (700 -> 3200 Hz) or down (2600 -> 600 Hz), Q 1.4. */
function air(buf, len, vol, up) {
  let s = 12345, x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  const f0 = up ? 700 : 2600, f1 = up ? 3200 : 600, n = Math.floor(len * SR);
  for (let i = 0; i < n && i < buf.length; i++) {
    s = (s * 1664525 + 1013904223) >>> 0;
    const x = (s / 0x100000000) * 2 - 1, t = i / SR;
    const hz = f0 * (f1 / f0) ** (t / len), w = (2 * Math.PI * hz) / SR, alpha = Math.sin(w) / (2 * 1.4), a0 = 1 + alpha;
    const y = (alpha / a0) * x - (alpha / a0) * x2 - ((-2 * Math.cos(w)) / a0) * y1 - ((1 - alpha) / a0) * y2;
    x2 = x1; x1 = x; y2 = y1; y1 = y;
    const g = t < len * 0.35 ? 0.0001 * (vol / 0.0001) ** (t / (len * 0.35)) : vol * (0.0001 / vol) ** ((t - len * 0.35) / (len * 0.65));
    buf[i] += y * g;
  }
}

/** The mockup's room: the dry signal plus a 210 ms echo line, feedback 0.3, at 0.2. */
function echo(buf) {
  const d = Math.round(0.21 * SR), line = new Float64Array(d), out = new Float64Array(buf.length);
  for (let i = 0, j = 0; i < buf.length; i++, j = (j + 1) % d) {
    const tap = line[j];
    out[i] = buf[i] + tap * 0.2;
    line[j] = buf[i] + tap * 0.3;
  }
  return out;
}

const clips = {};
NOTES.forEach((hz, k) => { const b = new Float64Array(Math.ceil((0.5 + TAIL) * SR)); bell(b, hz, 0.11, 0.5); clips['bell_' + String(k).padStart(2, '0')] = b; });
{ const b = new Float64Array(Math.ceil((1.7 + TAIL) * SR)); [0, 2, 3, 5].forEach((n, i) => bell(b, NOTES[n], 0.12, 1.5, i * 0.05)); bell(b, 440, 0.1, 1.7); clips.chord = b; }
{ const b = new Float64Array(Math.ceil((0.55 + TAIL) * SR)); air(b, 0.55, 0.05, true); clips.air_up = b; }
{ const b = new Float64Array(Math.ceil((0.5 + TAIL) * SR)); air(b, 0.5, 0.045, false); clips.air_down = b; }

for (const k of Object.keys(clips)) clips[k] = echo(clips[k]);
const peak = Math.max(...Object.values(clips).map(b => b.reduce((p, v) => Math.max(p, Math.abs(v)), 0)));
const gain = 0.9 / peak;

mkdirSync(OUT, { recursive: true });
for (const [name, b] of Object.entries(clips)) {
  const fade = Math.round(0.02 * SR), data = Buffer.alloc(b.length * 2);
  for (let i = 0; i < b.length; i++) {
    const left = b.length - i, v = b[i] * gain * (left < fade ? left / fade : 1);   // never end on a step
    data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, v)) * 32767), i * 2);
  }
  const head = Buffer.alloc(44);
  head.write('RIFF', 0); head.writeUInt32LE(36 + data.length, 4); head.write('WAVE', 8);
  head.write('fmt ', 12); head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(1, 22);
  head.writeUInt32LE(SR, 24); head.writeUInt32LE(SR * 2, 28); head.writeUInt16LE(2, 32); head.writeUInt16LE(16, 34);
  head.write('data', 36); head.writeUInt32LE(data.length, 40);
  writeFileSync(join(OUT, name + '.wav'), Buffer.concat([head, data]));
  console.log(name.padEnd(9), (b.length / SR).toFixed(2) + ' s', 'peak', (b.reduce((p, v) => Math.max(p, Math.abs(v)), 0) * gain).toFixed(3));
}
