/* render/items/fxmath.js - pure helpers for render/screenfx.js and render/items/visuals.js.
 * No three.js, so tests/render-screenfx.test.mjs and render-items.test.mjs run them under bare node.
 */
import { ITEM_TIERS, SCREEN_LOOK, ITEM_LOOK } from './look.js';

export const SCREEN_KINDS = ['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter'];
const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);

/** Quality tier caps by name; an unknown name gets the high tier. */
export const tierOf = (name) => ITEM_TIERS[name] || ITEM_TIERS.high;

/** A sim vector as an array or {x,y,z}: component i. */
export const comp = (p, i) => (p ? (p[i] !== undefined ? p[i] : p['xyz'[i]]) || 0 : 0);

/**
 * ScreenState (fx/effects.js) -> one amount 0..1 per kind plus the knobs the shader needs.
 * The fading previous kind adds its own share (prevK), so a replace never hard cuts. Gentle halves
 * every amount except the corner Flash and the Spiral dim, which effects.js already made small,
 * and slows motion (timeMul). Never more than 1 per kind.
 */
export function screenAmounts(screen, out = {}) {
  for (const k of SCREEN_KINDS) out[k] = 0;
  const p = (screen && screen.params) || {};
  const gentle = !!p.gentle, st = screen && screen.strength > 0 ? screen.strength : 1;
  // the corner Flash keeps full alpha (it is already small); strength under 1 sizes it instead
  const add = (kind, k) => { if (kind && kind !== 'none' && kind in out && k > 0) out[kind] += k * (kind === 'flash' && p.corner ? 1 : st); };
  if (screen) { add(screen.kind, clamp01(screen.k)); add(screen.prevKind, clamp01(screen.prevK)); }
  const g = SCREEN_LOOK.gentleMul;
  for (const k of SCREEN_KINDS) {
    const keep = (k === 'flash' && p.corner) || (k === 'spiral' && p.dimOnly);
    out[k] = clamp01(gentle && !keep ? out[k] * g : out[k]);
  }
  out.any = SCREEN_KINDS.some((k) => out[k] > 0.001);
  out.gentle = gentle;
  out.corner = !!p.corner;
  out.dimOnly = !!p.dimOnly;
  // the blur radius scales with the (already Gentle-halved) amount; divide that share back out so
  // the radius under Gentle is exactly items.gentleBlurMul of normal, not gentleMul times it
  const bm = p.blurMul > 0 ? p.blurMul : 1;
  out.blurMul = gentle && g > 0 ? bm / g : bm;
  out.cover = p.cover > 0 ? p.cover : 0.4;
  out.cornerScale = p.scale > 0 ? p.scale : st < 1 ? st : 0.35;
  out.seed = Number.isFinite(p.seed) ? p.seed : 0;
  out.timeMul = gentle ? SCREEN_LOOK.gentleTimeMul : 1;
  return out;
}

/** Blackout: the iris radius (fraction of the view height) at amount a. */
export const irisRadius = (a) => SCREEN_LOOK.irisFrom + (SCREEN_LOOK.irisTo - SCREEN_LOOK.irisFrom) * clamp01(a);

/**
 * Flash card rectangle in screen uv (x of width, y of height): area = cover of the view, a 4:3
 * picture, nudged by the seed; corner mode = scaled down and parked at flashCornerAt.
 * @returns {{ cx: number, cy: number, hw: number, hh: number }}
 */
export function flashRect(cover, aspect, { corner = false, scale = 0.35, seed = 0 } = {}) {
  const a = aspect > 0 ? aspect : 16 / 9;
  let h = Math.sqrt(Math.max(0, cover) * 0.75 * a), w = (h * 4) / 3 / a;
  const fit = Math.min(1, 0.9 / w, 0.9 / h);
  w *= fit; h *= fit;
  if (corner) {
    w *= scale; h *= scale;
    const [x, y] = SCREEN_LOOK.flashCornerAt;
    return { cx: Math.max(x, w / 2 + 0.02), cy: y, hw: w / 2, hh: h / 2 };
  }
  const s = Math.sin(seed * 12.9898) * 43758.5453, r = s - Math.floor(s);
  const [ox, oy] = SCREEN_LOOK.flashOffset;
  return { cx: 0.5 + ox * (r * 2 - 1), cy: 0.5 + oy, hw: w / 2, hh: h / 2 };
}

/** Shatter: a brief tilt (radians), settling to 0. t = seconds into the effect, a = amount. */
export function shatterTilt(t, a) {
  return SCREEN_LOOK.shatterTiltRad * clamp01(a) * Math.sin(Math.PI * 2 * SCREEN_LOOK.shatterTiltHz * t) * Math.exp(-2 * t);
}

/** Deterministic 0..1 noise from an integer seed (the DtRH bubbleLife rnd). */
export const rnd = (i) => { const x = Math.sin(i * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); };

/**
 * Mote i of n round a bubble of radius R at time t (DtRH chaosField material motes):
 * a slow ring, drifting outward and a little up over its life, then it starts over.
 * Writes [dx, dy, dz] into out and returns { alpha, size }.
 */
export function moteAt(i, n, t, R, out, seed = 0) {
  const L = ITEM_LOOK, [l0, l1] = L.moteLifeS, life = l0 + rnd(i * 3.1 + seed) * (l1 - l0);
  const q = ((t / life) + rnd(i * 5.7 + seed)) % 1;
  const a = (i / n) * Math.PI * 2 + rnd(i + seed) * 0.9 + t * L.moteTurnRadps * (i % 2 ? 1 : -0.7);
  const r = R * (L.moteRingMul[0] + q * (L.moteRingMul[1] - L.moteRingMul[0]));
  out[0] = Math.cos(a) * r; out[1] = (q - 0.3) * L.moteRiseM * 2 + Math.sin(t * 0.9 + i) * 0.05; out[2] = Math.sin(a) * r;
  return { alpha: Math.sin(q * Math.PI) ** 1.5 * (0.55 + 0.35 * rnd(i * 9.3 + seed)), size: L.moteSizeM * (0.7 + 0.6 * rnd(i * 2.3 + seed)) };
}

/** Pop progress 0..1 from its age; past showS it is done (1). */
export const popPhase = (t, showS) => clamp01(showS > 0 ? t / showS : 1);

/** Pop mote k of n: offset [dx, dy, dz] at age t (a burst on a sphere, falling a little). */
export function popMote(k, n, t, out, seed = 0) {
  const L = ITEM_LOOK, y = 1 - ((k + 0.5) / n) * 2, r = Math.sqrt(1 - y * y), a = k * 2.399963 + seed;
  const v = L.popSpeedMps * (0.6 + 0.4 * rnd(k + seed)), d = v * t * Math.exp(-t * 1.5);
  out[0] = Math.cos(a) * r * d; out[1] = y * d * 0.6 + d * 0.3 - 0.5 * L.popGravityMps2 * t * t; out[2] = Math.sin(a) * r * d;
  return out;
}

/** The icon a rolling slot shows: cycles the ids every swapS, never faster. */
export function rollIconAt(rollT, ids, swapS) {
  if (!ids.length) return null;
  return ids[Math.floor(Math.max(0, rollT) / Math.max(0.05, swapS)) % ids.length];
}

/** Melt puddle edge radius (1 = the trap radius) at angle a, time t: wobble plus dripping arms. */
export function meltEdge(a, t, { arms = ITEM_LOOK.meltArms, amp = ITEM_LOOK.meltDripAmp, wobble = ITEM_LOOK.meltWobble, hz = ITEM_LOOK.meltHz } = {}) {
  const w = Math.sin(a * 3 + t * hz * 2.1) * 0.5 + Math.sin(a * 5 - t * hz * 1.3) * 0.5;
  const arm = Math.max(0, Math.sin(a * arms + t * hz)) ** 3 * (0.6 + 0.4 * Math.sin(t * hz * 0.7 + a * 2));
  return 1 + w * wobble * 0.5 + arm * amp;
}

/**
 * A per-frame pool over a fixed cap: begin(), then take() per thing drawn (null when the cap is met),
 * end() hides every object not taken this frame. make(i) builds one; hide(obj) parks it.
 */
export function createPool(cap, make, hide) {
  const items = [];
  let used = 0;
  return {
    get used() { return used; }, get size() { return items.length; }, cap,
    begin() { used = 0; },
    take() {
      if (used >= cap) return null;
      if (used === items.length) items.push(make(used));
      return items[used++];
    },
    end() { for (let i = used; i < items.length; i++) hide(items[i]); },
    each(fn) { items.forEach(fn); },
  };
}
