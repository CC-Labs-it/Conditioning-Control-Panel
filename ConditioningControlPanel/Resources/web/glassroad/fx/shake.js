/* fx/shake.js - the camera shake law (plan 6).
 *
 * One kick is a run of parabolas f(t) = a*t - b*t^2 over shake.periodS, so each lobe
 * starts and ends at rest and peaks at its amplitude (a = 4*amp/T, b = a/T). Each lobe
 * flips sign and is shake.repeatMul of the last; the run stops once a lobe would be under
 * shake.stopBelowM. Hits shake the vertical axis, boosts the forward axis (-Z, camera
 * local). Kicks add. Under Gentle nothing shakes.
 * Polish 5 (J1): a kick may carry its own amplitude and repeat share (the boost ladder in fx/cues.js:
 * tier 2 shakes smaller, boosts die faster at shake.boostRepeatMul); shakeRun() is the budget of one kick.
 * The standalone analysis channel can model beat dips when explicitly supplied a music voice.
 * Race camera shake uses physical events only; music animates the road and world.
 * Once an analysis song reaches its rhythm part the channel dips on the beat
 * (audio/songShape.js bakes which beats and how much; the clock is the song, music.songS). Each dip rises over
 * beatShake.inS and falls over beatShake.outS. It ducks under any kick (over beatShake.duckInS, back over
 * duckOutS), so a boost or hit always wins. Gentle and OS reduced motion zero it. update(dt) is unchanged.
 */
import { shapeParams, shapeFromChart, shapeFromAnalysis, beatShakeAt, liveMusic } from '../audio/songShape.js';

// side (polish 6, for JUICE's bump): camera-local right; with no amplitude given it shakes at shake.hitAmpM
const AXIS = { hit: [0, 1, 0], boost: [0, 0, -1], side: [1, 0, 0] };

/** One lobe value at t in [0, T] for peak amp. */
export function lobe(amp, t, T) {
  const a = 4 * amp / T, b = a / T;
  return a * t - b * t * t;
}

/**
 * The budget of one kick: lobes until one would fall under stopBelowM, its peak and how long it runs.
 * @returns {{peakM: number, lobes: number, seconds: number}}
 */
export function shakeRun(ampM, repeatMul, stopBelowM, periodS) {
  if (!(stopBelowM > 0) || !(ampM >= stopBelowM)) return { peakM: 0, lobes: 0, seconds: 0 };
  const mul = Math.min(Math.max(repeatMul, 0), 0.999);
  let lobes = 0;
  for (let a = ampM; a >= stopBelowM; a *= mul) lobes++;
  return { peakM: ampM, lobes, seconds: lobes * periodS };
}

/** OS reduced motion, read live (false without matchMedia). */
function reducedMotionQuery() {
  try {
    const q = globalThis.matchMedia ? globalThis.matchMedia('(prefers-reduced-motion: reduce)') : null;
    return q ? () => !!q.matches : () => false;
  } catch (e) { return () => false; }
}

/**
 * The beat channel: song (music voice: songS, song, analysis, playing) -> a vertical dip in metres (<= 0).
 * duck 0..1 is how much a kick holds it down. Pure apart from its own cache.
 */
export function createBeatChannel(feel, { music = liveMusic, reduced = reducedMotionQuery() } = {}) {
  let P = shapeParams(feel), key = null, shape = null, duck = 1, last = 0;
  if (feel && typeof feel.subscribe === 'function') {
    feel.subscribe((path) => { if (path === '*' || String(path).startsWith('beatShake.')) { P = shapeParams(feel); key = null; } });
  }
  return {
    /** kicking: any kick run alive now. @returns {number} vertical offset, metres */
    update(dt, kicking) {
      duck += ((kicking ? 0 : 1) - duck) * Math.min(1, dt / Math.max(kicking ? P.duckInS : P.duckOutS, 1e-3));
      const m = typeof music === 'function' ? music() : music;
      const src = m && m.playing ? (m.song || m.analysis || null) : null;
      if (src !== key) {
        key = src;
        shape = !src ? null : m.song ? shapeFromChart(m.song, P) : shapeFromAnalysis(m.analysis, P);
      }
      let v = 0;
      if (shape && !(feel && feel.get('gentle.on')) && !reduced()) v = -beatShakeAt(shape, m.songS, P) * duck;
      // no song (stopped, Gentle switched on mid-dip): the last dip eases out instead of cutting
      if (v === 0 && last !== 0) { v = last * Math.exp(-dt / Math.max(P.outS / 3, 1e-3)); if (Math.abs(v) < 1e-4) v = 0; }
      last = v;
      return v;
    },
    /** The baked plan of the playing song (dev checks), or null. */
    get shape() { return shape; },
    reset() { duck = 1; last = 0; },
  };
}

/** @returns {{kick(kind: 'hit'|'boost'|'side', ampM?: number, repeatMul?: number): void, update(dt: number, otherKick?: boolean): number[], reset(): void, beat: object}} */
export function createShake(feel, opts = {}) {
  const runs = [];      // { axis, amp, mul, sign, t }
  const out = [0, 0, 0];
  // Music animates the world. Repeated camera dips read as bumps on smooth road.
  const beat = createBeatChannel(feel, { music: null, ...opts });

  /** ampM and repeatMul default to the kind's shake.* numbers. */
  function kick(kind, ampM = 0, repeatMul = 0) {
    if (feel.get('gentle.on')) return;
    const axis = AXIS[kind];
    if (!axis) return;
    const amp = ampM > 0 ? ampM : feel.get(kind === 'boost' ? 'shake.boostAmpM' : 'shake.hitAmpM');
    const mul = Math.min(repeatMul > 0 ? repeatMul : feel.get('shake.repeatMul'), 0.999);
    if (amp >= feel.get('shake.stopBelowM')) runs.push({ axis, amp, mul, sign: 1, t: 0 });
  }

  /** otherKick: a shake run outside this channel (the bump side shake, fx/reactions.js) is alive, so the beat ducks too. */
  function update(dt, otherKick = false) {
    out[0] = out[1] = out[2] = 0;
    if (feel.get('gentle.on')) { runs.length = 0; beat.reset(); return out; }
    const T = feel.get('shake.periodS'), stop = feel.get('shake.stopBelowM');
    for (let i = runs.length - 1; i >= 0; i--) {
      const r = runs[i];
      r.t += dt;
      while (r.t >= T) {
        r.t -= T; r.amp *= r.mul; r.sign = -r.sign;
        if (r.amp < stop) break;
      }
      if (r.amp < stop) { runs.splice(i, 1); continue; }
      const v = r.sign * lobe(r.amp, r.t, T);
      out[0] += r.axis[0] * v; out[1] += r.axis[1] * v; out[2] += r.axis[2] * v;
    }
    out[1] += beat.update(dt, runs.length > 0 || otherKick);
    return out;
  }

  return { kick, update, beat, reset() { runs.length = 0; beat.reset(); out[0] = out[1] = out[2] = 0; } };
}
