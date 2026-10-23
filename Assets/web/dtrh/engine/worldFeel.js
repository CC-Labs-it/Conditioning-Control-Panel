/* ============================================================================
 * worldFeel.js - the tube reacts to play (feel pass, wave 2, world lane).
 *
 * Three things, all driven from the window 'dtrh:feel' seam and one clock:
 *   warmth - one smoothed 0..1 variable from the streak. Ribbons, sparkles, fog
 *            and line hue lean warmer and denser as it rises. A detonation
 *            drains it over WARMTH_DRAIN_SEC (failure subtracts).
 *   breath - a very slow swell of line glow on one shared clock.
 *   ring   - a one-shot band of light that runs down the tube away from the
 *            camera (rabbit-caught, combo-big) or a wider, slower sweep
 *            (act-changed). It has an in and an out and never moves the camera.
 *
 * This module holds no three.js and never touches the scene: fx.js reads the
 * frame values from update() and writes them to its own materials. Everything
 * scales with the page motion level (shared/motion.js): reduced = half
 * amplitude, off = no travel and no swell, fades only.
 * ==========================================================================*/

import { motionScale, motionOff } from '../shared/motion.js';

// ---- warmth ------------------------------------------------------------------
export const WARMTH_STREAK_FULL = 40;   // streak at which the tube is fully warm
export const WARMTH_CURVE = 0.7;        // under 1 = the first pops warm it soonest
export const WARMTH_RISE_TAU = 1.6;     // seconds, how gently warmth follows a rising streak
export const WARMTH_FALL_TAU = 3.5;     // seconds, how gently it follows a falling one
export const WARMTH_IDLE_TAU = 16;      // seconds, the target cools on its own with no pops
export const WARMTH_DRAIN_SEC = 2.6;    // a detonation empties the warmth over this long
// what warmth buys (read by fx.js; all at warmth = 1)
export const WARM_LINE_COLOR = 0xff9a6b;  // peach-amber the line work leans toward
export const WARM_FOG_COLOR = 0x3a1420;   // dark warm rose the haze leans toward (never bright)
export const WARM_HUE_PULL = 0.22;        // how far line and spiral lean to WARM_LINE_COLOR
export const WARM_FOG_PULL = 0.3;         // how far the haze leans to WARM_FOG_COLOR
export const WARM_DENSITY = 0.12;         // extra fog density (fraction)
export const WARM_RIBBON = 0.45;          // extra ribbon opacity (fraction)
export const WARM_SPARKLE = 0.5;          // extra sparkle opacity (fraction)
export const WARM_GLOW = 0.1;             // extra line glow (fraction)

// ---- breath ------------------------------------------------------------------
export const BREATH_PERIOD_SEC = 7;     // one full swell in and out
export const BREATH_GLOW = 0.06;        // line glow swing at full motion (plus and minus)
export const BREATH_RIBBON = 0.08;      // ribbon and sparkle opacity swing at full motion

// ---- light ring --------------------------------------------------------------
// near / far are view distances in world units; width is the band's soft radius.
export const RING = { dur: 1.9, near: 3, far: 70, width: 5, amp: 0.5 };       // rabbit-caught, combo-big
export const SWEEP = { dur: 3.8, near: 4, far: 78, width: 13, amp: 0.3 };     // act-changed
export const RING_IN = 0.2;             // fraction of the run spent fading in
export const RING_OUT_FROM = 0.45;      // fraction of the run where the fade out begins
export const RING_STILL_AMP = 0.4;      // motion off: a still glow at this share of the amplitude
export const RING_EVENTS = { 'rabbit-caught': RING, 'combo-big': RING, 'act-changed': SWEEP };

// ---- pure helpers ------------------------------------------------------------
// ---- thread layers (tunnel.js uTh*) ---------------------------------------------
// What every tube wears on its threads, whatever the biome: a soft halo, a few glints,
// a light sway. A biome can ask for more of these or add its own (game/biomes.js knobs).
export const THREAD_BASE = { thHalo: 0.55, thSparkle: 0.5, thWeave: 0.3 };
// what a full streak adds on top (at warmth = 1)
export const THREAD_WARM = { thSparkle: 0.45, thSheen: 0.35 };
// layers that are motion: they shrink with the motion level and are gone at off
export const THREAD_MOVING = { thWeave: true, thStatic: true };

/** Where a knob should settle. Only thread keys change; every other knob passes through. */
export function threadTarget(key, biome, warmth, ms) {
  if (typeof key !== 'string' || key.slice(0, 2) !== 'th') return biome;
  let v = Math.max(biome || 0, THREAD_BASE[key] || 0) + (THREAD_WARM[key] || 0) * clamp01(warmth || 0);
  if (THREAD_MOVING[key]) v *= ms;
  return clamp01(v);
}

export function clamp01(v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
function smooth(a, b, x) { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

/** Streak -> the warmth the tube should settle at (0..1). */
export function warmthTarget(streak) {
  const s = Number(streak);
  if (!(s > 0)) return 0;
  return clamp01(Math.pow(s / WARMTH_STREAK_FULL, WARMTH_CURVE));
}

/** Frame-rate independent ease of cur toward target with time constant tau (seconds). */
export function approach(cur, target, dt, tau) {
  if (!(dt > 0)) return cur;
  if (!(tau > 0)) return target;
  return cur + (target - cur) * (1 - Math.exp(-dt / tau));
}

/** Advance a warmth state {value, target, drainLeft} by dt. Mutates and returns it. */
export function stepWarmth(s, dt) {
  if (!(dt > 0)) return s;
  s.target = approach(s.target, 0, dt, WARMTH_IDLE_TAU);
  if (s.drainLeft > 0) {
    // three time constants inside the drain window = about 5% left at its end
    s.value = approach(s.value, 0, dt, WARMTH_DRAIN_SEC / 3);
    s.drainLeft = Math.max(0, s.drainLeft - dt);
  } else {
    s.value = approach(s.value, s.target, dt, s.target > s.value ? WARMTH_RISE_TAU : WARMTH_FALL_TAU);
  }
  s.value = clamp01(s.value);
  return s;
}

/** The breath clock: -1..1 at time t (seconds). */
export function breathAt(t, period = BREATH_PERIOD_SEC) {
  return Math.sin((t / period) * Math.PI * 2);
}

/**
 * Where a ring is at `age` seconds: writes {dist, amp, width} into out and
 * returns true, or returns false once it has finished. scale = motion scale
 * (1, 0.5, 0); still = motion off (no travel, a quiet glow that fades).
 */
export function ringAt(age, spec, scale, still, out) {
  if (!spec || !(age >= 0) || age >= spec.dur) return false;
  const k = age / spec.dur;
  const env = smooth(0, RING_IN, k) * (1 - smooth(RING_OUT_FROM, 1, k));
  const travel = still ? 0.35 : (1 - Math.cos(k * Math.PI)) / 2;   // ease in and out
  out.dist = spec.near + (spec.far - spec.near) * travel;
  out.width = still ? spec.width * 2 : spec.width;
  out.amp = spec.amp * env * (still ? RING_STILL_AMP : scale);
  return true;
}

// ---- the live instance (one per fx) ------------------------------------------
export function createWorldFeel(target = (typeof window !== 'undefined' ? window : null)) {
  const warm = { value: 0, target: 0, drainLeft: 0 };
  let clock = 0;
  let ringSpec = null, ringAge = 0;
  // one reused frame object: no per-frame allocation
  const out = { warmth: 0, breath: 0, glow: 1, ringDist: 0, ringAmp: 0, ringWidth: 1 };
  const ringOut = { dist: 0, amp: 0, width: 1 };

  function startRing(spec) {
    // one ring at a time: a new one waits out the first half of a live one
    if (ringSpec && ringAge < ringSpec.dur * 0.5) return;
    ringSpec = spec; ringAge = 0;
  }

  function onFeel(e) {
    const d = e && e.detail;
    if (!d) return;
    if (d.kind === 'pop') {
      warm.target = Math.max(warm.target * 0.6, warmthTarget(d.streak));
    } else if (d.kind === 'detonate') {
      warm.target = 0; warm.drainLeft = WARMTH_DRAIN_SEC;
    } else if (d.kind === 'moment') {
      const spec = RING_EVENTS[d.event];
      if (spec) startRing(spec);
    } else if (d.kind === 'phase') {
      if (d.state === 'recap' || d.state === 'warren') warm.target = 0;
    }
  }
  if (target && target.addEventListener) target.addEventListener('dtrh:feel', onFeel);

  /** Advance by dt; returns the frame values (the same object every call). */
  function update(dt) {
    dt = dt > 0 ? Math.min(dt, 0.1) : 0;
    clock += dt;
    stepWarmth(warm, dt);
    const ms = motionScale();
    out.warmth = warm.value;
    out.breath = breathAt(clock) * ms;
    out.glow = 1 + out.breath * BREATH_GLOW + warm.value * WARM_GLOW;
    out.ringAmp = 0;
    if (ringSpec) {
      ringAge += dt;
      if (ringAt(ringAge, ringSpec, ms, motionOff(), ringOut)) {
        out.ringDist = ringOut.dist; out.ringAmp = ringOut.amp; out.ringWidth = ringOut.width;
      } else {
        ringSpec = null;
      }
    }
    return out;
  }

  function dispose() {
    if (target && target.removeEventListener) target.removeEventListener('dtrh:feel', onFeel);
    ringSpec = null;
  }

  return { update, dispose, _feel: onFeel, _state: warm };
}
