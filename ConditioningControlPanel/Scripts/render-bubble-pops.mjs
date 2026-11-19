/**
 * Renders the bubble skins' pop sounds: Resources/sounds/bubbles/<skin>_1.wav .. <skin>_3.wav.
 *
 * The recipes are the owner's: each skin's sfx(b, t) in the bubble lab (shop rework 2026-11-17),
 * rebuilt here with plain maths instead of WebAudio, so nothing synthesises at runtime and a rerun
 * gives the same bytes. Helpers mirror the lab's: osc (oscillator through an exponential envelope,
 * pitch on an exponential ramp), nz (looped white noise through a biquad, WebAudio's filter
 * formulas, Q in dB for low and high pass), echo (a delay with feedback) and the lab's master: a
 * 0.5 gain into a compressor at WebAudio's defaults (approximated: soft knee, 3 ms attack, 250 ms
 * release). ONE KEY: every pitched part sits on A major pentatonic, the lab's NOTES.
 *
 * Three variants per skin, each with its own fixed seed and its own note (A4, C#5, E5), so a run of
 * pops is not one stamp repeated. Every clip is levelled to sit UNDER today's Pop.mp3: its peak and
 * its loudest 50 ms stay below POP_REF, which was measured from Pop.mp3 decoded to WAV.
 * Soap is not rendered: soap keeps Pop.mp3, Pop2.mp3 and Pop3.mp3 (CCP.Core/Bubbles/BubbleSkinSounds.cs).
 *
 *     node Scripts/render-bubble-pops.mjs              write the clips and print their numbers
 *     node Scripts/render-bubble-pops.mjs --check      render in memory, exit 1 unless the files match
 *     node Scripts/render-bubble-pops.mjs measure a.wav b.wav   print the same numbers for any 16-bit WAV
 * Keep SKINS in step with BubbleSkinSounds.Skins and Variants (BubbleSkinSoundsTests pins both).
 */
import { writeFileSync, readFileSync, existsSync } from 'node:fs';
import { dirname, join, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', 'Resources', 'sounds', 'bubbles');

const SR = 44100;
const LEN = Math.ceil(2.4 * SR);           // render room; each clip is trimmed to its own tail
const T0 = 0.004;                          // first event, so nothing starts on sample 0
const NOTES = [440, 493.88, 554.37, 659.25, 739.99];
const VARIANT_NOTES = [NOTES[0], NOTES[2], NOTES[3]];
const VARIANTS = 3;

/** Pop.mp3 decoded to WAV (ffmpeg, 44.1 kHz stereo), read with `measure`: peak, loudest 50 ms and loudest 400 ms, dBFS. */
const POP_REF = { peakDb: -3.33, st50Db: -12.73, m400Db: -21.75 };
/** Headroom under POP_REF: these clips run longer than a pop, so they sit a little lower. */
const UNDER_PEAK_DB = 1, UNDER_DB = 1;

function mulberry32(a) {
  return () => {
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** The lab's env(): exponential from 0.0001 to peak over `at`, then down to 0.0001 over `dec`. */
function envAt(u, at, peak, dec) {
  const p = Math.max(peak, 0.0002), lo = 0.0001;
  if (u < 0) return 0;
  if (u <= at) return lo * (p / lo) ** (u / at);
  if (u <= at + dec) return p * (lo / p) ** ((u - at) / dec);
  return lo;
}

/** WebAudio BiquadFilterNode coefficients (normalised by a0). */
function coeffs(type, hz, q) {
  const w0 = (2 * Math.PI * Math.min(hz, SR * 0.49)) / SR, cos = Math.cos(w0), sin = Math.sin(w0);
  const alpha = type === 'bandpass' ? sin / (2 * q) : sin / (2 * 10 ** (q / 20));
  let b0, b1, b2;
  if (type === 'lowpass') { b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; }
  else if (type === 'highpass') { b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; }
  else { b0 = alpha; b1 = 0; b2 = -alpha; }
  const a0 = 1 + alpha;
  return [b0 / a0, b1 / a0, b2 / a0, (-2 * cos) / a0, (1 - alpha) / a0];
}

function biquadInPlace(buf, type, hz, q) {
  const [b0, b1, b2, a1, a2] = coeffs(type, hz, q);
  let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  for (let i = 0; i < buf.length; i++) {
    const x = buf[i], y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
    x2 = x1; x1 = x; y2 = y1; y1 = y; buf[i] = y;
  }
}

function blep(t, dt) {
  if (t < dt) { t /= dt; return t + t - t * t - 1; }
  if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
  return 0;
}

/** WebAudio's waves are sine-phase; square and saw are band-limited with PolyBLEP. */
function wave(type, ph, dt) {
  switch (type) {
    case 'sine': return Math.sin(2 * Math.PI * ph);
    case 'triangle': return ph < 0.25 ? 4 * ph : ph < 0.75 ? 2 - 4 * ph : 4 * ph - 4;
    case 'square': return (ph < 0.5 ? 1 : -1) + blep(ph, dt) - blep((ph + 0.5) % 1, dt);
    default: { const p = (ph + 0.5) % 1; return 2 * p - 1 - blep(p, dt); }   // sawtooth
  }
}

/** One render: the lab's helpers, writing into buses that the master sums at the end. */
function studio(seed) {
  const rnd = mulberry32(seed);
  const noise = new Float64Array(SR);
  for (let i = 0; i < SR; i++) noise[i] = rnd() * 2 - 1;
  const out = new Float64Array(LEN);
  const post = [];
  const span = (t, dur) => [Math.max(0, Math.round(t * SR)), Math.min(LEN, Math.ceil((t + dur + 0.06) * SR))];

  return {
    rnd,
    out,
    osc(type, f0, f1, t, dur, peak, dest = out, fm = null) {
      const [s0, s1] = span(t, dur);
      let ph = 0;
      for (let n = s0; n < s1; n++) {
        const u = n / SR - t;
        let f = f1 === f0 ? f0 : u < dur ? f0 * (f1 / f0) ** (Math.max(0, u) / dur) : f1;
        if (fm) f += fm(n / SR);
        const dt = Math.abs(f) / SR;
        dest[n] += wave(type, ph, dt) * envAt(u, 0.004, peak, dur);
        ph += f / SR; ph -= Math.floor(ph);
      }
    },
    nz(t, dur, peak, type, f0, f1, q, dest = out) {
      const [s0, s1] = span(t, dur);
      const ramp = f1 && f1 !== f0;
      let pos = Math.floor(rnd() * 0.5 * SR), c = coeffs(type, f0, q || 0.7);
      let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
      for (let n = s0; n < s1; n++) {
        const u = n / SR - t;
        if (ramp) c = coeffs(type, u < dur ? f0 * (f1 / f0) ** (Math.max(0, u) / dur) : f1, q || 0.7);
        const x = noise[pos++ % SR];
        const y = c[0] * x + c[1] * x1 + c[2] * x2 - c[3] * y1 - c[4] * y2;
        x2 = x1; x1 = x; y2 = y1; y1 = y;
        dest[n] += y * envAt(u, 0.003, peak, dur);
      }
    },
    /** The lab's echo(): the input goes out dry and into a delay that feeds itself back at `fb`. */
    echo(time, fb) {
      const inp = new Float64Array(LEN);
      post.push(() => {
        const d = Math.round(time * SR), loop = new Float64Array(LEN);
        for (let n = 0; n < LEN; n++) {
          const delayed = n >= d ? loop[n - d] : 0;
          loop[n] = inp[n] + fb * delayed;
          out[n] += inp[n] + delayed;
        }
      });
      return inp;
    },
    lowpass(hz) {
      const inp = new Float64Array(LEN);
      post.push(() => { biquadInPlace(inp, 'lowpass', hz, 1); for (let n = 0; n < LEN; n++) out[n] += inp[n]; });
      return inp;
    },
    mixdown() { for (const p of post) p(); return out; },
  };
}

/** The skins, each the lab's sfx(b, t) line for line, with Math.random as the seeded rnd. */
const SKINS = {
  champagne(a, n, t) {
    const r = a.rnd;
    a.osc('sine', 190, 70, t, 0.09, 0.55); a.nz(t, 0.035, 0.3, 'bandpass', 1300, 0, 1.2);
    a.nz(t + 0.03, 0.85, 0.07, 'highpass', 7000);
    for (let i = 0; i < 34; i++) { const d = Math.pow(r(), 1.6) * 0.8; a.nz(t + 0.04 + d, 0.012, 0.11 * (1 - d), 'highpass', 5200 + r() * 3000); }
    for (let i = 0; i < 6; i++) { const d = 0.05 + i * 0.06 + r() * 0.03, f = n * (2 + i * 0.5); a.osc('sine', f, f * 1.5, t + d, 0.03, 0.09); }
  },
  heart(a, n, t) {
    for (const [d, gn] of [[0, 0.8], [0.15, 0.6]]) { a.osc('sine', 82, 44, t + d, 0.12, gn); a.osc('triangle', 164, 92, t + d, 0.08, gn * 0.28); }
    a.osc('sine', n * 2, n * 2, t + 0.3, 0.55, 0.13); a.osc('sine', n * 2.5, n * 2.5, t + 0.38, 0.6, 0.1); a.osc('sine', n * 4, n * 4, t + 0.3, 0.2, 0.04);
  },
  spiral(a, n, t) {
    const lfo = s => (s >= t && s < t + 0.5 ? 30 * Math.sin(2 * Math.PI * 19 * (s - t)) : 0);
    a.osc('sine', n * 0.5, n * 4, t, 0.44, 0.16, a.out, lfo);
    a.nz(t, 0.44, 0.1, 'bandpass', 400, 5200, 3);
    const e = a.echo(0.115, 0.46), t2 = t + 0.46;
    a.osc('sine', n * 4, n * 4, t2, 0.25, 0.22, e); a.osc('triangle', n * 2, n * 2, t2, 0.18, 0.1, e); a.nz(t2, 0.03, 0.12, 'highpass', 4000);
  },
  marble(a, n, t) {
    const r = a.rnd;
    a.nz(t, 0.01, 0.25, 'highpass', 3500); a.osc('square', 240, 150, t, 0.04, 0.1); a.nz(t + 0.09, 0.008, 0.14, 'highpass', 4200);
    const t2 = t + 0.2; a.nz(t2, 0.28, 0.32, 'highpass', 2600);
    for (let i = 0; i < 7; i++) { const f = 2100 + r() * 5200; a.osc('sine', f, f * 0.985, t2, 0.06 + r() * 0.25, 0.09); }
    for (let i = 0; i < 12; i++) { const f = 3000 + r() * 5500; a.osc('sine', f, f, t2 + 0.12 + r() * 0.6, 0.05, 0.045); }
  },
  disco(a, n, t) {
    for (let i = 0; i < 3; i++) a.nz(t + i * 0.012, 0.02, 0.3, 'bandpass', 1500, 0, 1.4);
    a.nz(t + 0.036, 0.16, 0.22, 'bandpass', 1300, 0, 0.9);
    const f = a.lowpass(3200);
    [1, 1.25, 1.5, 2].forEach((m, i) => a.osc('sawtooth', n * m, n * m, t + 0.04 + i * 0.055, 0.09, 0.1, f));
    a.nz(t + 0.05, 0.4, 0.05, 'highpass', 8000);
  },
  frost(a, n, t) {
    const r = a.rnd;
    for (let i = 0; i < 16; i++) a.nz(t + r() * 0.22, 0.008, 0.2, 'bandpass', 3200 + r() * 3500, 0, 4);
    a.osc('sine', n * 4, n * 4, t + 0.1, 0.5, 0.06); a.osc('sine', n * 6.02, n * 6.02, t + 0.1, 0.3, 0.03);
    a.nz(t + 0.1, 0.55, 0.12, 'lowpass', 1500, 300);
  },
};

/** The lab's master: 0.5 gain, then a compressor at WebAudio's defaults (-24 dB, knee 30, ratio 12). */
function master(buf) {
  const T = -24, W = 30, R = 12;
  const curve = x => (2 * (x - T) < -W ? x : 2 * Math.abs(x - T) <= W ? x + ((1 / R - 1) * (x - T + W / 2) ** 2) / (2 * W) : T + (x - T) / R);
  const aA = Math.exp(-1 / (0.003 * SR)), aR = Math.exp(-1 / (0.25 * SR));
  let gr = 0;
  const y = new Float64Array(buf.length);
  for (let n = 0; n < buf.length; n++) {
    const x = buf[n] * 0.5, xd = 20 * Math.log10(Math.max(Math.abs(x), 1e-9)), want = curve(xd) - xd;
    gr = want < gr ? aA * gr + (1 - aA) * want : aR * gr + (1 - aR) * want;
    y[n] = x * 10 ** (gr / 20);
  }
  return y;
}

/** Loudest RMS over any window of `sec` seconds (a clip shorter than the window counts as padded with silence). */
function loudest(samples, sec) {
  const win = Math.round(sec * SR), hop = Math.round(0.005 * SR);
  let best = 0;
  for (let s = 0; s === 0 || s + win <= samples.length; s += hop) {
    let e = 0; for (let i = s; i < Math.min(samples.length, s + win); i++) e += samples[i] * samples[i];
    best = Math.max(best, Math.sqrt(e / win));
  }
  return best;
}

/** Length, peak, and loudness as the loudest 50 ms (the hit) and the loudest 400 ms (the whole sound). */
function stats(samples) {
  let peak = 0;
  for (const v of samples) peak = Math.max(peak, Math.abs(v));
  const db = v => 20 * Math.log10(Math.max(v, 1e-9));
  return { seconds: samples.length / SR, peakDb: db(peak), st50Db: db(loudest(samples, 0.05)), m400Db: db(loudest(samples, 0.4)) };
}

/** Trim to the tail (-55 dB under the clip's own peak, plus 15 ms), fade the last 15 ms, level under POP_REF. */
function finish(y) {
  let peak = 0; for (const v of y) peak = Math.max(peak, Math.abs(v));
  const floor = peak * 10 ** (-55 / 20);
  let end = y.length - 1; while (end > 0 && Math.abs(y[end]) < floor) end--;
  const fade = Math.round(0.015 * SR), len = Math.min(y.length, end + fade);
  const clip = y.slice(0, len);
  for (let i = 0; i < fade; i++) clip[len - 1 - i] *= i / fade;
  const s = stats(clip);
  const gainDb = Math.min(POP_REF.peakDb - UNDER_PEAK_DB - s.peakDb, POP_REF.st50Db - UNDER_DB - s.st50Db,
    POP_REF.m400Db - UNDER_DB - s.m400Db);
  const g = 10 ** (gainDb / 20);
  for (let i = 0; i < len; i++) clip[i] *= g;
  return clip;
}

function wav(clip) {
  const data = Buffer.alloc(clip.length * 2);
  for (let i = 0; i < clip.length; i++) data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, clip[i])) * 32767), i * 2);
  const head = Buffer.alloc(44);
  head.write('RIFF', 0); head.writeUInt32LE(36 + data.length, 4); head.write('WAVE', 8);
  head.write('fmt ', 12); head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(1, 22);
  head.writeUInt32LE(SR, 24); head.writeUInt32LE(SR * 2, 28); head.writeUInt16LE(2, 32); head.writeUInt16LE(16, 34);
  head.write('data', 36); head.writeUInt32LE(data.length, 40);
  return Buffer.concat([head, data]);
}

/** A 16-bit PCM WAV as floats, loudest channel per frame. */
function readWav(path) {
  const b = readFileSync(path);
  let p = 12, ch = 1, bits = 16, data = null;
  while (p + 8 <= b.length) {
    const id = b.toString('ascii', p, p + 4), size = b.readUInt32LE(p + 4);
    if (id === 'fmt ') { ch = b.readUInt16LE(p + 10); bits = b.readUInt16LE(p + 22); }
    if (id === 'data') data = b.subarray(p + 8, p + 8 + size);
    p += 8 + size + (size & 1);
  }
  if (!data || bits !== 16) throw new Error(path + ': not a 16-bit PCM WAV');
  const frames = Math.floor(data.length / (2 * ch)), out = new Float64Array(frames);
  for (let f = 0; f < frames; f++) {
    let v = 0;
    for (let c = 0; c < ch; c++) { const s = data.readInt16LE((f * ch + c) * 2) / 32768; if (Math.abs(s) > Math.abs(v)) v = s; }
    out[f] = v;
  }
  return out;
}

const row = (name, s) => name.padEnd(16) + s.seconds.toFixed(3).padStart(7) + ' s   peak ' + s.peakDb.toFixed(2).padStart(7) +
  ' dBFS   loudest 50 ms ' + s.st50Db.toFixed(2).padStart(7) + ' dBFS   loudest 400 ms ' + s.m400Db.toFixed(2).padStart(7) + ' dBFS';

const args = process.argv.slice(2);
if (args[0] === 'measure') {
  for (const f of args.slice(1)) console.log(row(basename(f), stats(readWav(f))));
  process.exit(0);
}

const check = args.includes('--check');
let bad = 0;
Object.keys(SKINS).forEach((skin, si) => {
  for (let v = 0; v < VARIANTS; v++) {
    const a = studio(1009 * (si + 1) + 97 * v + 13);
    SKINS[skin](a, VARIANT_NOTES[v], T0);
    const clip = finish(master(a.mixdown()));
    const bytes = wav(clip), file = join(OUT, skin + '_' + (v + 1) + '.wav');
    if (check) {
      const same = existsSync(file) && Buffer.compare(readFileSync(file), bytes) === 0;
      if (!same) { bad++; console.log('differs: ' + file); }
    } else {
      writeFileSync(file, bytes);
      console.log(row(skin + '_' + (v + 1), stats(clip)) + '   ' + Math.round(bytes.length / 1024) + ' KB');
    }
  }
});
if (check) { console.log(bad ? bad + ' clip(s) differ: rerun without --check' : 'all clips match'); process.exit(bad ? 1 : 0); }
