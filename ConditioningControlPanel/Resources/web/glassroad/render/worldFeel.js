/* render/worldFeel.js - feel wave 2026-10-26, lane C (render world): the pure numbers behind permanence,
 * the final-lap wake, speed sense, cascading list entrances and rival face glances. Three-free so node
 * tests reach every rule; the render modules (skids, particles, speedlines, audioReactive, faces) and the
 * hud lists read these. Every tunable lives in feel.json under permanence.*, speedSense.*, cascade.*,
 * faces.* (FEEL.md "Feel wave 2026-10-26 / Lane C"); a missing key falls back to the default here.
 * Motion: 'full' | 'less' | 'off' (hud.options.motion). Less halves, off removes every new effect.
 */

export const WORLD_FEEL = Object.freeze({
  permanence: Object.freeze({ skidCapMul: 3, skidLifeS: 600, scorchLenM: 1.4, scorchWidthM: 0.9, scorchColour: '#3a1430',
    debrisCount: 10, debrisSettleS: 20, debrisFadeS: 1.5, debrisSizeM: 0.16 }),
  speedSense: Object.freeze({ finalLapTweenS: 1.5, finalLapParticleMul: 1.5, finalLapFieldShift: 0.12, finalLapReactiveMul: 1.35,
    topFromPct: 0.85, topLenMul: 0.6, topDensity: 0.25 }),
  cascade: Object.freeze({ staggerMs: Object.freeze([40, 90]), durMs: 320, riseY: 14, overshoot: 1.7, lessMul: 0.5 }),
  faces: Object.freeze({ holdS: 0.8, cooldownS: 2, passer: 'grin', passed: 'determined' }),
});

/** A feel number with a default (core/feel.js get() throws on a missing key; tests pass stubs or null). */
export function feelVal(feel, path, def) {
  if (!feel || typeof feel.get !== 'function') return def;
  try { const v = feel.get(path); return v === undefined || v === null ? def : v; } catch { return def; }
}

const G = (feel, group, key) => feelVal(feel, group + '.' + key, WORLD_FEEL[group][key]);

/** Motion level to a share: full 1, less lessMul (0.5), off 0; unknown reads as full. */
export function motionMul(motion = 'full', less = 0.5) { return motion === 'off' ? 0 : motion === 'less' ? less : 1; }

/** Skid ring size and life for a quality tier: desktop tiers grow by skidCapMul and keep marks race-long; phone unchanged. */
export function skidBudget(P, tier, feel = null) {
  const base = (P.skidCap && (P.skidCap[tier] || P.skidCap.high)) || 1200;
  if (tier === 'phone') return { cap: base, lifeS: P.skidLifeS, holdS: P.skidHoldS };
  const life = Math.max(P.skidLifeS, +G(feel, 'permanence', 'skidLifeS') || 0);
  return { cap: Math.round(base * Math.max(1, +G(feel, 'permanence', 'skidCapMul') || 1)), lifeS: life, holdS: Math.max(P.skidHoldS, life - (P.skidLifeS - P.skidHoldS)) };
}

/** Debris alpha 0..1 at age s: born like any particle, settled for settleS, then a fadeS ease out (no snap). */
export function debrisAlpha(age, settleS = 20, fadeS = 1.5) {
  if (!(age >= 0)) return 0;
  if (age <= settleS) return 1;
  const t = Math.min(1, (age - settleS) / Math.max(1e-3, fadeS));
  return (1 - t) * (1 - t);
}

/** Settle a falling point on the ground: returns the clamped y and whether it rests now. */
export function settleY(y, vy, groundY) { return y <= groundY && vy <= 0 ? { y: groundY, rest: true } : { y, rest: false }; }

const smooth = (t) => t * t * (3 - 2 * t);

/**
 * The final-lap wake: ONE variable 0..1, tweened in over finalLapTweenS on lap {final:true} and out on reset.
 * step(dt, paused) only moves while the race runs (pause freezes it). value already wears the motion share.
 */
export function createFinalLap(feel = null) {
  let x = 0, target = 0, motion = 'full';
  const tween = () => Math.max(0.05, +G(feel, 'speedSense', 'finalLapTweenS') || 1.5);
  return {
    lap(p = {}) { if (p && p.final) target = 1; },
    reset() { target = 0; x = 0; },
    setMotion(m) { motion = m || 'full'; },
    step(dt, paused = false) {
      if (paused || !(dt > 0)) return this.value;
      const d = dt / tween();
      x = target > x ? Math.min(target, x + d) : Math.max(target, x - d);
      return this.value;
    },
    get raw() { return x; },
    get value() { return smooth(x) * motionMul(motion); },
  };
}

/** What the wake buys at finalLap v (0..1): particle count share, speed-field threshold drop, reactive pulse gain. */
export function finalLapMods(v, feel = null) {
  const k = Math.max(0, Math.min(1, v || 0));
  return {
    particleMul: 1 + (G(feel, 'speedSense', 'finalLapParticleMul') - 1) * k,
    fieldShift: G(feel, 'speedSense', 'finalLapFieldShift') * k,
    reactiveMul: 1 + (G(feel, 'speedSense', 'finalLapReactiveMul') - 1) * k,
  };
}

/** Top-speed streaks: 0 below topFromPct, eased to 1 at full speed, times the motion share. */
export function topSpeedShare(pct, feel = null, motion = 'full') {
  const from = G(feel, 'speedSense', 'topFromPct'), t = Math.max(0, Math.min(1, ((pct || 0) - from) / Math.max(1e-3, 1 - from)));
  return smooth(t) * motionMul(motion);
}

/** easeOutBack: 0 -> 1 with an overshoot s (1.70158 is the classic). */
export function easeOutBack(t, s = 1.70158) {
  const u = Math.max(0, Math.min(1, t)) - 1;
  return 1 + (s + 1) * u * u * u + s * u * u;
}

/**
 * Cascade timing for n list items: each item waits a random 40-90 ms after the one before (rnd: a 0..1
 * source, deterministic in tests). less: half the stagger and the rise, no overshoot; off: everything 0.
 * Returns { delays: ms[], durMs, riseY, overshoot }.
 */
export function cascadePlan(n, motion = 'full', rnd = Math.random, feel = null) {
  const m = motionMul(motion, G(feel, 'cascade', 'lessMul')), [lo, hi] = G(feel, 'cascade', 'staggerMs');
  const delays = []; let at = 0;
  for (let i = 0; i < n; i++) { delays.push(Math.round(at)); at += (lo + (hi - lo) * rnd()) * m; }
  if (m === 0) return { delays: delays.map(() => 0), durMs: 0, riseY: 0, overshoot: 0 };
  return { delays, durMs: Math.round(G(feel, 'cascade', 'durMs') * (motion === 'less' ? 0.75 : 1)), riseY: G(feel, 'cascade', 'riseY') * m,
    overshoot: motion === 'less' ? 0 : G(feel, 'cascade', 'overshoot') };
}

/** easeOutBack sampled as Web Animations keyframes (WAAPI has no back easing); opacity rises over the first 40%. */
export function cascadeFrames(plan, steps = 8) {
  const out = [];
  for (let k = 0; k <= steps; k++) {
    const t = k / steps, e = plan.overshoot > 0 ? easeOutBack(t, plan.overshoot) : smooth(t);
    out.push({ offset: t, opacity: Math.min(1, t / 0.4), transform: `translateY(${((1 - e) * plan.riseY).toFixed(2)}px)` });
  }
  return out;
}

/**
 * Tween a DOM list in (garage, menu, result). Animations carry no fill after they end, so the list's own
 * hover / selected CSS (transforms included) takes over again. motion off: nothing runs. Returns the count.
 */
export function cascadeIn(elements, motion = 'full', { rnd = Math.random, feel = null } = {}) {
  const list = Array.from(elements || []).filter((el) => el && typeof el.animate === 'function');
  const plan = cascadePlan(list.length, motion, rnd, feel);
  if (!plan.durMs) return 0;
  const frames = cascadeFrames(plan);
  list.forEach((el, i) => el.animate(frames, { duration: plan.durMs, delay: plan.delays[i], fill: 'backwards', easing: 'linear' }));
  return list.length;
}

/**
 * Rival face glances on overtake {racer, passed}: the passer grins, the passed racer glances (determined),
 * each for holdS; never more than one change per racer per cooldownS. now = race render seconds.
 * face(i, now) -> a FACE_IDS id while it holds, else null (the pose's own face stands).
 */
export function createFaceGlance(feel = null) {
  const until = new Map(), last = new Map(), face = new Map();
  function put(i, id, now) {
    if (i === null || i === undefined || i < 0) return false;
    if (last.has(i) && now - last.get(i) < G(feel, 'faces', 'cooldownS')) return false;
    last.set(i, now); until.set(i, now + G(feel, 'faces', 'holdS')); face.set(i, id); return true;
  }
  return {
    overtake(p = {}, now = 0) {
      const a = put(p.racer, G(feel, 'faces', 'passer'), now), b = put(p.passed, G(feel, 'faces', 'passed'), now);
      return (a ? 1 : 0) + (b ? 1 : 0);
    },
    face(i, now = 0) { return until.has(i) && now < until.get(i) ? face.get(i) : null; },
    reset() { until.clear(); last.clear(); face.clear(); },
  };
}
