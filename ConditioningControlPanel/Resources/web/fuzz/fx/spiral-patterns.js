/* ============================================================================
 * fx/spiral-patterns.js - what the Loom is asked to draw, and how one shape
 * turns into the next. Pure: no DOM, no clock, no dice of its own. The drawing
 * itself is the Loom's own field shader (arcademy/engine/loom/loomField.js),
 * never a fork; fx/spiral-bus.js hands it the frames built here.
 *
 * THE MORPH. Arm counts and styles are whole numbers in the shader, so they
 * cannot be tweened. Bands can: a band's width (duty) may run down to nothing.
 * So the old shape's bands thin out to threads while the new shape's threads
 * swell into bands, and both melt on the way (the twist and the wobble rise and
 * fall together). The two meet as thin wet threads, and nothing ever fades to
 * grey in the middle.
 * ==========================================================================*/

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
export const smooth = (a, b, x) => { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };

/* ---- colours ---- */
const HEX = /^#[0-9a-f]{6}$/i;
export const isHex = (c) => typeof c === 'string' && HEX.test(c);
const rgbOf = (h) => { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
const hexOf = (r, g, b) => `#${((1 << 24) | (Math.round(r) << 16) | (Math.round(g) << 8) | Math.round(b)).toString(16).slice(1)}`;

export function mixHex(a, b, t) {
  if (t <= 0) return a;
  if (t >= 1) return b;
  const p = rgbOf(a), q = rgbOf(b);
  return hexOf(p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t, p[2] + (q[2] - p[2]) * t);
}

/** The same colour, turned round the wheel. */
export function hueShift(hex, deg) {
  const [r, g, b] = rgbOf(hex).map((v) => v / 255);
  const hi = Math.max(r, g, b), lo = Math.min(r, g, b), d = hi - lo, l = (hi + lo) / 2;
  if (d < 1e-4) return hex;
  const s = d / (1 - Math.abs(2 * l - 1));
  const h0 = hi === r ? ((g - b) / d + 6) % 6 : hi === g ? (b - r) / d + 2 : (r - g) / d + 4;
  const h = (h0 * 60 + deg + 360) % 360;
  const c = s * (1 - Math.abs(2 * l - 1)), x = c * (1 - Math.abs(((h / 60) % 2) - 1)), m = l - c / 2;
  const [p, q, w] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return hexOf((p + m) * 255, (q + m) * 255, (w + m) * 255);
}

/** The dark ground under a pair of threads: its own colour, nearly black. */
export const groundOf = (a) => mixHex(a, '#08060c', 0.9);

export const DRIFT_PERIOD_S = 48;
/** Spiral Colours: both threads walk the same closed loop, half a lap apart: the palette and the hues next to it. Starts exactly on the palette. */
export function driftPair([a, b], sec) {
  const keys = [a, hueShift(a, 32), b, hueShift(b, -32)];
  const at = (p) => {
    const q = (((p % 1) + 1) % 1) * keys.length, i = Math.floor(q);
    return mixHex(keys[i], keys[(i + 1) % keys.length], q - i);
  };
  const p = sec / DRIFT_PERIOD_S;
  return [at(p), at(p + 0.5)];
}

/* ---- the patterns ---- */
/* A layer: style, arms, turns, duty, cols. Thread names in cols: a, b, the two palette threads; m, their mix; c, d, each turned a little. */
const L = (style, arms, turns, duty, cols, more) => ({ style, arms, turns, duty, cols, ...more });

export const PATTERNS = Object.freeze([
  /* 0 is the plain spiral everyone already has. Every still paint matches it. */
  { id: 'spiral', l1: L('log', 4, 2, 0.5, 'ab') },
  { id: 'nautilus', l1: L('golden', 3, 1.25, 0.55, 'abm') },
  { id: 'rings', l1: L('tunnel', 8, 0.5, 0.5, 'ab', { spd: 4 }) },
  /* Rings that will not stay round: the same rings, rippling on their way out. */
  { id: 'ripple', l1: L('tunnel', 6, 0.5, 0.42, 'abm', { spd: 3 }), wob: { amp: 0.35, freq: 5, cycles: 2 } },
  { id: 'rose', l1: L('petal', 6, 0.5, 0.45, 'ab'), glow: 0.35 },
  { id: 'pinwheel', l1: L('petal', 5, 3.5, 0.55, 'amb') },
  /* Two spirals, nearly the same, turning against each other: the moire crawls. */
  { id: 'moire', l1: L('log', 2, 3, 0.5, 'a'), l2: L('log', 2, 3.25, 0.5, 'b', { dir: -1 }) },
  { id: 'liquid', l1: L('ribbon', 3, 1.5, 0.5, 'abm'), wob: { amp: 0.3, freq: 4, cycles: 2 } },
  { id: 'sunburst', l1: L('arch', 12, 0.5, 0.5, 'ab'), pulse: { amp: 0.08, cycles: 1 } },
  /* Petals round a ring: an eye. */
  { id: 'iris', l1: L('petal', 12, 0.5, 0.5, 'am'), l2: L('tunnel', 3, 0.5, 0.25, 'b', { spd: 2 }) },
  /* Thin rings on thin spokes. */
  { id: 'web', l1: L('tunnel', 6, 0.5, 0.2, 'a', { spd: 2 }), l2: L('arch', 8, 0.5, 0.2, 'b', { dir: -1 }) },
  /* Soft edges, no bands: it stops being stripes at all. */
  { id: 'smoke', l1: L('log', 3, 1.5, 0.8, 'abm', { band: 'gradient' }), glow: 0.7, wob: { amp: 0.12, freq: 2, cycles: 1 } },
  /* The same spiral both ways round: diamonds, the head of a sunflower. */
  { id: 'lattice', l1: L('log', 4, 2, 0.5, 'a'), l2: L('log', 4, -2, 0.5, 'b', { dir: -1 }) },
]);

const OFF_LAYER = Object.freeze({ arms: 4, turns: 2, duty: 0.35, style: 'log', direction: -1, colors: Object.freeze(['#000000']), bandMode: 'hard', speedMul: 1, enabled: false });
const NO_CENTRE = Object.freeze({ kind: 'none', color: '#ffffff', sizeFrac: 0.16, text: '', flashCycles: 1 });

const threadOf = (name, [a, b]) => (name === 'a' ? a : name === 'b' ? b : name === 'm' ? mixHex(a, b, 0.5) : name === 'c' ? hueShift(a, 24) : hueShift(b, -24));

const TWIST = 1.25, MELT_WOBBLE = 0.2, GONE_BELOW = 0.18;

function poseLayer(l, s, melt, sign, pair, ground, gentle) {
  const fade = s >= GONE_BELOW ? 1 : s / GONE_BELOW;
  return {
    arms: l.arms,
    /* A tunnel snaps its helix to whole cycles, so a twist would jump. It melts through the wobble alone. */
    turns: l.style === 'tunnel' ? l.turns : l.turns + sign * TWIST * melt * (gentle ? 0.4 : 1),
    duty: l.duty * s,
    style: l.style,
    direction: l.dir === -1 ? -1 : 1,
    colors: [...l.cols].map((n) => { const c = threadOf(n, pair); return fade < 1 ? mixHex(ground, c, fade) : c; }),
    bandMode: l.band === 'gradient' ? 'gradient' : 'hard',
    speedMul: l.spd || 1,
  };
}

/** One side of a morph as params for the Loom's field. s: how much of it is there. Numbers can sit past the sidecar limits: only render() reads these. */
function pose(p, s, melt, sign, pair, ground, gentle) {
  const w = p.wob, pu = p.pulse;
  return {
    schema: 2,
    format: 'square',
    speed: 3,
    layer: poseLayer(p.l1, s, melt, sign, pair, ground, gentle),
    layer2: p.l2 ? { ...poseLayer(p.l2, s, melt, sign, pair, ground, gentle), enabled: true } : OFF_LAYER,
    bg: { kind: 'solid', color: ground, outer: ground },
    glow: (p.glow || 0) * s,
    pulse: { amp: gentle || !pu ? 0 : pu.amp, cycles: pu ? pu.cycles : 1 },
    wobble: { amp: gentle ? 0 : (w ? w.amp : 0) + MELT_WOBBLE * melt, freq: w ? w.freq : 3, cycles: w ? w.cycles : 1 },
    hueCycles: 0,
    centerpiece: NO_CENTRE,
  };
}

/**
 * The frame at morph position t (0 = all `from`, 1 = all `to`). `b`, when set, goes on top of `a`:
 * brightest wins on a dark ground, darkest on a light one, so the ground stays the ground.
 * `cross` is the plain crossfade amount for a renderer that cannot do that.
 */
export function morphFrame(from, to, t, pair, ground, { gentle = false } = {}) {
  const A = PATTERNS[from] || PATTERNS[0];
  if (to == null || t <= 0) return { a: pose(A, 1, 0, 1, pair, ground, gentle), b: null, cross: 0 };
  const B = PATTERNS[to] || PATTERNS[0];
  const sA = 1 - smooth(0, 0.62, t), sB = smooth(0.38, 1, t);
  const melt = Math.sin(Math.PI * t) ** 2;
  const a = sA > 0 ? pose(A, sA, melt, 1, pair, ground, gentle) : null;
  const b = sB > 0 ? pose(B, sB, melt, -1, pair, ground, gentle) : null;
  return { a: a || b, b: a ? b : null, cross: smooth(0.3, 0.7, t) };
}

/* ---- the clock ---- */
export const MORPH_S = 3.8, HOLD_S = 5.5, GENTLE_MORPH_S = 18, GENTLE_HOLD_S = 16;

/**
 * Which pattern, and how far into the next. Deals every pattern once before it repeats one.
 * Switched off, it finishes the morph it is on and melts back to the plain spiral, then goes idle.
 * Under OS reduced motion it holds where it is.
 */
export function createMorphClock({ rng = Math.random, start = 0, count = PATTERNS.length, wait = 0, morphS = MORPH_S, holdS = HOLD_S, onSettle = null } = {}) {
  let from = start >= 0 && start < count ? start : 0, to = null, t = 0, left = wait, on = false;
  let bag = [];
  const deal = () => {
    if (!bag.length) {
      bag = Array.from({ length: count }, (_, i) => i);
      for (let i = bag.length - 1; i > 0; i--) {
        const j = Math.min(i, Math.floor(rng() * (i + 1)));
        [bag[i], bag[j]] = [bag[j], bag[i]];
      }
    }
    let i = bag.length - 1;
    if (bag[i] === from && bag.length > 1) i -= 1;
    const [next] = bag.splice(i, 1);
    return next === from ? (from + 1) % count : next;
  };
  return {
    get from() { return from; },
    get to() { return to; },
    get t() { return t; },
    get on() { return on; },
    get morphing() { return to !== null; },
    /** Anything left to draw: switched on, or still on its way home. */
    get active() { return on || to !== null || from !== 0; },
    setOn(v) { on = !!v; },
    step(dt, { gentle = false, reduced = false } = {}) {
      if (reduced) return;
      if (to === null) {
        if (!on) {
          if (from === 0) return;
          to = 0; t = 0;
        } else {
          left -= dt;
          if (left > 0) return;
          to = deal(); t = 0;
        }
      }
      t += dt / (gentle ? GENTLE_MORPH_S : morphS);
      if (t >= 1) {
        from = to; to = null; t = 0;
        left = gentle ? GENTLE_HOLD_S : holdS;
        onSettle?.(from);
      }
    },
  };
}
