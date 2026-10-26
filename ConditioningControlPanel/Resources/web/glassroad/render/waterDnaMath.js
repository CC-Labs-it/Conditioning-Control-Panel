// The liquid's paired strands read the same heard song and smoothed bands as the floor.
import { beatOf, barOf } from '../audio/chart.js';

const unit = v => Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
export function createWaterDnaFrame() {
  return { phase: 0, radius: 2.1, hue: .5, pulse: 0,
    beat: { index: 0, phase: 0, lenS: .5 }, bar: { index: 0, phase: 0, lenS: 2 } };
}

/** All transport comes from heard seconds, never elapsed render time. Silence holds the pose. */
export function waterDnaFrame(out, bands, signal, song, { paused = false, gentle = false } = {}) {
  if (paused || signal?.frozen || !signal?.active || !Number.isFinite(signal.songS)) return out;
  const bass = unit(bands.bass), mid = unit(bands.mid), high = unit(bands.treble);
  const t = Math.max(0, signal.songS);
  // Picked recordings without a chart still use their actual playing position.
  const beat = song ? beatOf(song, t, out.beat) : null;
  const bar = song ? barOf(song, t, out.bar) : null;
  const steps = beat ? beat.index + beat.phase : t;
  const bars = bar ? bar.index + bar.phase : t / 4;
  const slow = gentle ? .35 : 1;
  const breath = .5 - .5 * Math.cos(steps * Math.PI * (gentle ? .5 : 1));
  const melodic = unit(bands.note1) + unit(bands.note4) - unit(bands.note0) - unit(bands.note6);
  out.phase = steps * .9 * slow;
  out.radius = 2.1 + slow * (bass * (.3 + breath * .7) + mid * .45);
  out.hue = .5 + .5 * Math.sin(bars * .9 * slow + melodic * 1.2 + high * .7 - bass * .45);
  out.pulse = (bass * .6 + mid * .25 + high * .15) * (.65 + breath * .35) * slow;
  return out;
}
