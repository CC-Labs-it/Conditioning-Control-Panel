/* render/quality.js - quality tiers (plan section 2 table) and the frame-time governor.
 *
 * Lifted from dtrh/shared/quality.js: one shared knob block `Q` that every render module reads,
 * set once before the scene is built and again whenever the governor steps. No three.js here,
 * so tests/render-quality.test.mjs runs it under bare node.
 */

/** Per tier: DPR cap, bloom on and its resolution scale, particle budget, speed line count, ambient motes,
 * the distance past which a rival kart drops its bone pose, sparks and name tag (kartLodM), boost ribbon points. */
export const TIERS = Object.freeze({
  high: Object.freeze({ name: 'high', dprMax: 2, antialias: true, bloom: true, bloomScale: 0.5, particles: 4000, speedLines: 160, scatter: 1, motes: 1400, kartLodM: 90, trailPoints: 28 }),
  mid: Object.freeze({ name: 'mid', dprMax: 1.5, antialias: true, bloom: true, bloomScale: 0.25, particles: 1500, speedLines: 100, scatter: 0.7, motes: 800, kartLodM: 60, trailPoints: 20 }),
  phone: Object.freeze({ name: 'phone', dprMax: 1.25, antialias: false, bloom: true, bloomScale: 0.25, particles: 700, speedLines: 60, scatter: 0.45, motes: 400, kartLodM: 40, trailPoints: 14 }),
});
/** Lowest first; the governor moves one step along this list. */
export const ORDER = Object.freeze(['phone', 'mid', 'high']);

/** Plan section 2: down when p95 > downMs for downS, up after upS under upMs. */
export const GOVERNOR = Object.freeze({ downMs: 20, downS: 2, upMs: 12, upS: 10, windowS: 1, everyS: 0.25, gapMs: 250 });

/** The live knob block. Read fields each frame, never cache them at module load. */
export const Q = { ...TIERS.high };

export function setTier(name) {
  Object.assign(Q, TIERS[name] || TIERS.high);
  return Q;
}

/** Starting tier: `?quality=` wins, then coarse pointer = phone, a WebGL2 fallback = mid, else high. */
export function pickTier({ override = null, coarse = false, webgl2 = false } = {}) {
  if (override && TIERS[override]) return override;
  if (coarse) return 'phone';
  return webgl2 ? 'mid' : 'high';
}

/** 95th percentile of a numeric array (copies, does not reorder the input). */
export function p95(values) {
  if (!values.length) return 0;
  const a = Array.from(values).sort((x, y) => x - y);
  return a[Math.min(a.length - 1, Math.ceil(a.length * 0.95) - 1)];
}

/**
 * @param {{ tier?: string, ceiling?: string, rules?: object }} opts ceiling = highest tier allowed (default: the start tier)
 * @returns {{ tier: string, sample(frameMs: number): string|null, reset(): void }} sample returns the new tier on a step
 */
export function createGovernor({ tier = 'high', ceiling = tier, rules = GOVERNOR } = {}) {
  const g = { tier, sample, reset };
  let buf = [], bufT = 0, sinceEval = 0, overS = 0, underS = 0;
  function reset() { buf = []; bufT = 0; sinceEval = 0; overS = 0; underS = 0; }
  function sample(frameMs) {
    if (!(frameMs > 0) || frameMs > rules.gapMs) return null; // a hidden tab or a pause is not a slow frame
    const dt = frameMs / 1000;
    buf.push(frameMs); bufT += dt; sinceEval += dt;
    while (bufT > rules.windowS && buf.length > 1) bufT -= buf.shift() / 1000;
    if (sinceEval < rules.everyS) return null;
    const step = sinceEval; sinceEval = 0;
    const p = p95(buf);
    overS = p > rules.downMs ? overS + step : 0;
    underS = p < rules.upMs ? underS + step : 0;
    const i = ORDER.indexOf(g.tier);
    let next = null;
    if (overS >= rules.downS && i > 0) next = ORDER[i - 1];
    else if (underS >= rules.upS && i < ORDER.indexOf(ceiling)) next = ORDER[i + 1];
    if (next) { g.tier = next; reset(); }
    return next;
  }
  return g;
}
