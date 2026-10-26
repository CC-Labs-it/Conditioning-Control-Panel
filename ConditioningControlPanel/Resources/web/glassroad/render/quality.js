/* render/quality.js - quality tiers (plan section 2 table) and the frame-time governor.
 *
 * Lifted from dtrh/shared/quality.js: one shared knob block `Q` that every render module reads,
 * set once before the scene is built and again whenever the governor steps. No three.js here,
 * so tests/render-quality.test.mjs runs it under bare node.
 */

/** Per tier: DPR cap, bloom on and its resolution scale, particle budget, speed line count, ambient motes,
 * the distance past which a rival kart drops its bone pose, sparks and name tag (kartLodM), boost ribbon points,
 * shadows ('map' a real shadow map of shadowMapPx, 'blob' soft blobs and ellipses, no map) per CONTRACT polish 2,
 * the art textures' anisotropic filtering (read ONCE at load from the start tier: a change re-uploads), and
 * sceneReflections: whether the water keeps its live scene probe (read ONCE when the course builds; PHONE WATER,
 * owner 2026-10-26: the phone tier drops the probe warm-up and its in-race cube captures for a stand-in sheen). */
export const TIERS = Object.freeze({
  high: Object.freeze({ name: 'high', dprMax: 2, antialias: true, bloom: true, bloomScale: 0.5, particles: 4000, speedLines: 160, scatter: 1, motes: 1400, kartLodM: 90, trailPoints: 28, shadows: 'map', shadowMapPx: 2048, anisotropy: 8, sceneReflections: true }),
  mid: Object.freeze({ name: 'mid', dprMax: 1.5, antialias: true, bloom: true, bloomScale: 0.25, particles: 1500, speedLines: 100, scatter: 0.7, motes: 800, kartLodM: 60, trailPoints: 20, shadows: 'map', shadowMapPx: 1024, anisotropy: 4, sceneReflections: true }),
  phone: Object.freeze({ name: 'phone', dprMax: 1.25, antialias: false, bloom: true, bloomScale: 0.25, particles: 700, speedLines: 60, scatter: 0.45, motes: 400, kartLodM: 40, trailPoints: 14, shadows: 'blob', shadowMapPx: 0, anisotropy: 2, sceneReflections: false }),
});
/** Lowest first; the governor moves one step along this list. */
export const ORDER = Object.freeze(['phone', 'mid', 'high']);

/** Plan section 2: down when p95 > downMs for downS, up after upS under upMs. */
export const GOVERNOR = Object.freeze({ downMs: 20, downS: 2, upMs: 12, upS: 10, windowS: 1, everyS: 0.25, gapMs: 250 });

/** The live knob block. Read fields each frame, never cache them at module load. */
export const Q = { ...TIERS.high };

/** The shadow mode WORLD builds: the player's Shadows switch off wins, else the tier's. */
export function shadowMode(opts = null, q = Q) {
  return opts && opts.shadows === false ? 'off' : q.shadows || 'blob';
}

export function setTier(name) {
  Object.assign(Q, TIERS[name] || TIERS.high);
  return Q;
}

/** A short adapter name from WebGPU adapter info or a WebGL renderer string (ANGLE wraps it). */
export function shortGpu(s) {
  s = String(s || '').trim();
  const m = s.indexOf('ANGLE (');
  if (m >= 0) { const parts = s.slice(m + 7).split(', '); s = parts.length > 1 ? parts[1] : parts[0]; }
  const hex = s.indexOf(' (0x');
  if (hex > 0) s = s.slice(0, hex);
  for (const tail of [' Direct3D', ' vs_', ' OpenGL']) { const i = s.indexOf(tail); if (i > 0) s = s.slice(0, i); }
  return s.trim().slice(0, 48);
}

/** Starting tier: `?quality=` wins, then coarse pointer = phone, a WebGL2 fallback = mid, else high. */
export function pickTier({ override = null, coarse = false, webgl2 = false, gpu = '' } = {}) {
  if (override && TIERS[override]) return override;
  if (coarse) return 'phone';
  // an Intel integrated GPU (HD / UHD / Iris) starts on mid: on high the owner's HD Graphics fell to phone
  // mid-race, and every tier step relinks shaders (owner 2026-10-22, Firefox)
  if (/intel/i.test(gpu) && /\b(hd|uhd|iris)\b/i.test(gpu)) return 'mid';
  return webgl2 ? 'mid' : 'high';
}

/** 95th percentile of a numeric array (copies, does not reorder the input). */
export function p95(values) {
  if (!values.length) return 0;
  const a = Array.from(values).sort((x, y) => x - y);
  return a[Math.min(a.length - 1, Math.ceil(a.length * 0.95) - 1)];
}

/** The verdict on one window for one display. A vsynced frame never shows faster than the display interval, so
 *  on a 60 Hz screen "p95 under 12 ms" can never be met and a tier lost to a loading hitch never came back
 *  (polish 4, the owner sat on phone at 59 Hz); and some browsers (Firefox WebGPU) pace rAF unevenly, so a p95
 *  over 1.5 intervals shows up at a steady 60 fps. With a display interval the governor judges the MEAN frame
 *  (down below about 83 percent of the display rate, or a p95 of 3 intervals: real hitches) and steps up when the
 *  mean holds the display rate. Without one (tests, an unknown display) the plan's p95 rule stands. */
export function governorVerdict(buf, rules, displayMs = 0) {
  const p = p95(buf);
  if (!(displayMs > 0)) return { slow: p > rules.downMs, fast: p < rules.upMs };
  let sum = 0;
  for (const v of buf) sum += v;
  const mean = buf.length ? sum / buf.length : 0;
  return {
    slow: mean > Math.max(rules.downMs, displayMs * 1.2) || p > displayMs * 3,
    fast: mean <= Math.max(rules.upMs, displayMs * 1.05) && p <= displayMs * 2,
  };
}

/**
 * @param {{ tier?: string, ceiling?: string, rules?: object }} opts ceiling = highest tier allowed (default: the start tier).
 *   rules.graceS (optional): no step down in the first seconds of samples (boot, art decode and shader warm-up hitches).
 * @returns {{ tier: string, sample(frameMs: number, displayMs?: number): string|null, reset(): void }} sample returns the new
 *   tier on a step; displayMs (the learnt display interval) switches to the mean rule (governorVerdict). Each step down makes the
 *   next step up wait one more upS, so a tier that cannot hold vsync does not bounce.
 */
export function createGovernor({ tier = 'high', ceiling = tier, rules = GOVERNOR } = {}) {
  const g = { tier, sample, reset };
  let buf = [], bufT = 0, sinceEval = 0, overS = 0, underS = 0, aliveS = 0, downs = 0;
  function reset() { buf = []; bufT = 0; sinceEval = 0; overS = 0; underS = 0; }
  function sample(frameMs, displayMs = 0) {
    if (!(frameMs > 0) || frameMs > rules.gapMs) return null; // a hidden tab or a pause is not a slow frame
    const dt = frameMs / 1000;
    aliveS += dt;
    buf.push(frameMs); bufT += dt; sinceEval += dt;
    while (bufT > rules.windowS && buf.length > 1) bufT -= buf.shift() / 1000;
    if (sinceEval < rules.everyS) return null;
    const step = sinceEval; sinceEval = 0;
    const v = governorVerdict(buf, rules, displayMs);
    overS = v.slow && aliveS >= (rules.graceS || 0) ? overS + step : 0;
    underS = v.fast ? underS + step : 0;
    const i = ORDER.indexOf(g.tier);
    let next = null;
    if (overS >= rules.downS && i > 0) { next = ORDER[i - 1]; downs++; }
    else if (underS >= rules.upS * (1 + Math.min(downs, 5)) && i < ORDER.indexOf(ceiling)) next = ORDER[i + 1];
    if (next) { g.tier = next; reset(); }
    return next;
  }
  return g;
}

/**
 * Dynamic resolution (renderScale 'auto'): holds the target frame interval by scaling the drawing buffer
 * between min and max. Every windowS it judges the frames: more than downShare of them slower than
 * slowMul x target steps down (by the cost ratio, at least one step); a clean upS steps up one step,
 * never past the ceiling. A step down sets the ceiling just under the scale that failed, and the
 * ceiling climbs back one step per holdS (doubling per failure, up to holdMaxS), and a step down never
 * goes under the last scale that ran clean, so a probe costs one step up and one back: no oscillation. A step down that did not make frames faster (not fill bound) is given back and the
 * controller stops stepping down for holdS. Pure: feed sample(frameMs, targetMs, slowMs?) every rendered frame
 * (slowMs, when given, replaces slowMul x target as the slow line).
 */
export const DYNRES = Object.freeze({ min: 0.6, max: 1, step: 0.05, windowS: 1, settleS: 0.5, slowMul: 1.2,
  downShare: 0.2, maxDrop: 0.15, upShare: 0.03, upS: 3, holdS: 20, holdMaxS: 120, futile: 0.02, gapMs: 250,
  displayMaxMs: 17.5, slowShare: 0.75 });

/**
 * The dynres target and slow line from the frame gate. The gate learns the display interval from rAF
 * gaps, so a GPU that never holds the display rate teaches it its own slow frame time (an iGPU at
 * 1440p learns 33 ms and never sees a slow frame). No real display is slower than about 57 Hz, so the
 * learnt interval is clamped to displayMaxMs here; a frame cap (30) still sets a slower target.
 * @returns {{ target: number, slow: number }}
 */
export function dynLines(displayMs, capMs = 0, R = DYNRES) {
  const disp = displayMs > 0 ? Math.min(displayMs, R.displayMaxMs) : 0;
  const target = capMs > 0 ? Math.max(capMs, disp || capMs) : disp || 1000 / 60;
  return { target, slow: disp > 0 ? target + disp * R.slowShare : 0 };
}

export function createDynRes({ rules = DYNRES, scale = rules.max } = {}) {
  const R = rules;
  const snap = (x) => Math.round(Math.min(R.max, Math.max(R.min, x)) / R.step) * R.step;
  const fix = (x) => Math.round(snap(x) * 1000) / 1000;
  const d = { scale: fix(scale), ceiling: R.max, sample, reset };
  let win = [], winT = 0, settle = 0, clean = 0, hold = 0, holdS = R.holdS, lock = 0, check = null, good = 0;
  function reset(to = d.scale) { d.scale = fix(to); d.ceiling = R.max; win = []; winT = 0; settle = 0; clean = 0; hold = 0; holdS = R.holdS; lock = 0; check = null; good = 0; }
  function set(next) {
    next = fix(next);
    if (next === d.scale) return null;
    d.scale = next; settle = R.settleS; win = []; winT = 0; clean = 0;
    return next;
  }
  function sample(frameMs, targetMs, slowMs = 0) {
    if (!(frameMs > 0) || frameMs > R.gapMs || !(targetMs > 0)) return null;
    const dt = frameMs / 1000;
    if (lock > 0) lock -= dt;
    if (hold > 0 && (hold -= dt) <= 0 && d.ceiling < R.max) {
      d.ceiling = fix(d.ceiling + R.step);
      if (d.ceiling < R.max) hold = holdS;
    }
    if (settle > 0) { settle -= dt; return null; }
    win.push(frameMs); winT += dt;
    if (winT < R.windowS) return null;
    let slow = 0;
    const lim = slowMs > 0 ? slowMs : targetMs * R.slowMul;
    for (const x of win) if (x > lim) slow++;
    const share = slow / win.length, mean = winT * 1000 / win.length, span = winT;
    win = []; winT = 0;
    if (check) {
      const was = check; check = null;
      // fewer pixels should make frames faster in proportion to the area; under a third of that = not fill bound
      const want = 1 - (d.scale / was.from) ** 2;
      if (mean > was.mean * (1 - Math.max(R.futile, want / 3))) {   // smaller did not help: give the pixels back
        lock = holdS; d.ceiling = Math.max(d.ceiling, was.from);
        return set(was.from);
      }
    }
    if (share > R.downShare && d.scale > R.min && lock <= 0) {
      const from = d.scale;
      // by the cost ratio, at most maxDrop, and never under the last scale that ran clean when that is lower
      let next = Math.max(from - R.maxDrop, Math.min(from - R.step, from * Math.sqrt(Math.min(1, targetMs / mean))));
      if (good && good < from) next = Math.max(next, good);
      good = 0;
      d.ceiling = fix(from - R.step); hold = holdS; holdS = Math.min(R.holdMaxS, holdS * 2);
      check = { from, mean };
      return set(next);
    }
    if (share <= R.upShare) {
      clean += span;
      if (clean >= R.upS) good = Math.max(good, d.scale);
      if (clean >= R.upS && d.scale < d.ceiling) return set(Math.min(d.ceiling, d.scale + R.step));
    } else clean = 0;
    return null;
  }
  return d;
}

/** Where a Quality option change lands. A tier step relinks every program (on a phone, seconds of a frozen
 *  page: 3.6 s of main thread plus a 5.8 s frame gap measured for Auto to High under the pause card, 4x CPU
 *  throttle), so while held (a race, its countdown, its pause card) the new tier waits like a governor verdict
 *  and lands on the next menu or results card, or at the next course load. Choosing the tier already in use
 *  clears anything that waits.
 *  @returns {{ apply: string|null, pending: string|null }} */
export function qualityLanding({ want, current, hold = false }) {
  if (!want || want === current) return { apply: null, pending: null };
  return hold ? { apply: null, pending: want } : { apply: want, pending: null };
}
