/* render/items/fxmath.js - pure helpers for render/screenfx.js and render/items/visuals.js.
 * No three.js, so tests/render-screenfx.test.mjs and render-items.test.mjs run them under bare node.
 */
import { ITEM_TIERS, SCREEN_LOOK, ITEM_LOOK, SLOT_LOOK } from './look.js';

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
  // Gentle Flash: fx/effects.js sends params.scale (items.gentleFlashScale, a LINEAR scale of the card)
  out.cornerScale = p.scale > 0 ? p.scale : st < 1 ? st : SCREEN_LOOK.flashCornerScale;
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
export function flashRect(cover, aspect, { corner = false, scale = SCREEN_LOOK.flashCornerScale, seed = 0 } = {}) {
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
 * The item card's edge glow at time t (wave 2 item-card, item_edge): a slow breath round cardEdgeGlow,
 * never faster than cardEdgeHz; Gentle holds it steady at cardEdgeGentle of the glow (no breath).
 */
export function cardEdgeGlow(t, gentle) {
  const L = ITEM_LOOK;
  if (gentle) return L.cardEdgeGlow * L.cardEdgeGentle;
  return L.cardEdgeGlow * (1 + L.cardEdgeBreath * Math.sin(2 * Math.PI * L.cardEdgeHz * t));
}

/**
 * How the effect-bubble GLB maps onto a bubble of radius R (manifest row, ART.md "item card and effect
 * bubble"): the shell's centre height above its base, its radius, and the icon carrier's share of the
 * shell diameter (the living icon sprite is drawn at 2R x carrier). Falls back to the art assumptions.
 */
export function shellFit(row) {
  const size = row && row.sizeM ? row.sizeM[0] : 1.1;
  const radius = size / 2, centreY = row && row.billboardCentreM ? row.billboardCentreM[1] : radius;
  const carrier = row && row.billboardSizeM ? row.billboardSizeM[0] / size : 0.7 / 1.1;
  return { radius, centreY, carrier };
}

/**
 * The largest radius (m) that keeps a bubble at distance d under frac of the view's height: a sphere of
 * radius R spans 2 asin(R / d), and that angle fills tan(half) / tan(vFov / 2) of the frame.
 * Returns R unchanged without a distance or a field of view.
 */
export function nearCapR(R, d, vFovDeg, frac = ITEM_LOOK.nearMaxFrac) {
  if (!(d > 0) || !(vFovDeg > 0) || !(frac > 0)) return R;
  const half = Math.atan(frac * Math.tan((vFovDeg * Math.PI) / 360));
  return Math.min(R, d * Math.sin(half));
}

/**
 * True when an impact at (dx, dy, dz) from the camera is near (under maxM) and in front, inside the
 * horizontal field of view plus a small margin; (fx, fy, fz) is the unit view direction. Only these
 * glows ask photosafe, so unseen ones never spend the budget the player's own juice needs.
 */
export function impactSeen(dx, dy, dz, fx, fy, fz, { maxM = ITEM_LOOK.impactAskM, vFovDeg = 0, aspect = 16 / 9 } = {}) {
  const d = Math.hypot(dx, dy, dz);
  if (!(d <= maxM)) return false;
  if (d < 1e-6) return true;
  const along = (dx * fx + dy * fy + dz * fz) / d;
  if (!(vFovDeg > 0)) return along > 0;
  const half = Math.min(Math.PI / 2, Math.atan(Math.tan((vFovDeg * Math.PI) / 360) * aspect) + 0.17);
  return along >= Math.cos(half);
}

/**
 * A per-frame pool over a fixed cap: begin(), then take() per thing drawn (null when the cap is met),
 * end() hides every object not taken this frame. make(i) builds one; hide(obj) parks it. prime(n) builds the first n
 * up front and parks them, so the boot warm links their programs (Firefox links serially: a first make() mid-race froze it).
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
    prime(n = 1) { while (items.length < Math.min(n, cap)) { const e = make(items.length); items.push(e); hide(e); } return this; },
  };
}

// ---- polish 2 ----------------------------------------------------------------------------------
const TAU = Math.PI * 2;
const ease01 = (x) => { const q = clamp01(x); return q * q * (3 - 2 * q); };

/** The living icon's breath: a scale round 1, slow; Gentle keeps a third of the move. */
export function breathe(t, seed = 0, gentle = false) {
  const L = ITEM_LOOK;
  return 1 + L.breathAmp * (gentle ? 1 / 3 : 1) * Math.sin(TAU * L.breathHz * t + seed * 1.7);
}

/**
 * A trail history { pts: Float32Array(n * 3), n }: adds (x, y, z) once it is stepM from the newest
 * point, drops the oldest past n, starts over after a jump. Returns the count.
 */
export function trailPush(h, x, y, z, n, stepM, jumpM = 20) {
  const p = h.pts;
  if (h.n) {
    const j = (h.n - 1) * 3, d = Math.hypot(p[j] - x, p[j + 1] - y, p[j + 2] - z);
    if (d > jumpM) h.n = 0;
    else if (d < stepM) return h.n;
  }
  if (h.n >= n) { p.copyWithin(0, 3); h.n = n - 1; }
  const j = h.n * 3;
  p[j] = x; p[j + 1] = y; p[j + 2] = z;
  return ++h.n;
}

/** Ribbon point i of n (0 = oldest, n - 1 = the head): size share and alpha, tapered to the tail, a slow twinkle (none under Gentle). */
export function trailLook(i, n, t, seed = 0, gentle = false) {
  const q = n > 1 ? i / (n - 1) : 1;
  const tw = gentle ? 1 : 0.75 + 0.25 * Math.sin(TAU * ITEM_LOOK.trailTwinkleHz * t + i * 2.3 + seed);
  return { size: 0.35 + 0.65 * q, alpha: q ** 1.3 * tw };
}

/** A trap's warning, 0..1, from the nearest kart's distance d (m): wakes at trapWarnM[0], full at trapWarnM[1]. */
export function trapWarn(d) {
  const [far, near] = ITEM_LOOK.trapWarnM;
  return Number.isFinite(d) ? ease01((far - d) / (far - near)) : 0;
}

/** The lob's landing mark at flight share k (0..1): the shadow tightens and darkens as it falls; the ring pulses gently (steady under Gentle). */
export function lobMark(k, t, gentle = false) {
  const L = ITEM_LOOK, q = clamp01(k), [r0, r1] = L.lobShadowR;
  const pulse = gentle ? 0.6 : 0.7 + 0.3 * Math.sin(TAU * L.markPulseHz * t);
  return { r: r1 + (r0 - r1) * q * q, alpha: L.lobShadowAlpha * (0.25 + 0.75 * q), ring: L.markRingM * (1 - 0.35 * q), ringAlpha: L.markAlpha * (0.4 + 0.6 * q) * pulse };
}

/**
 * An impact at age t (s): the shockwave ring's radius and alpha, and the soft glow's alpha (0 once over,
 * or when photosafe refused it). Gentle: half as bright, twice as long, every rise at least 300 ms.
 */
export function impactPhase(t, { glow = true, gentle = false } = {}) {
  const L = ITEM_LOOK, life = L.impactRingS * (gentle ? 2 : 1), q = clamp01(t / life), [r0, r1] = L.impactRingM;
  const rise = gentle ? 0.3 : 0.05, k = gentle ? 0.5 : 1;
  const ringA = q >= 1 ? 0 : L.impactRingAlpha * k * Math.min(1, t / rise) * (1 - q) ** 1.5;
  const gS = Math.max(L.impactGlowS, rise + 0.1) * (gentle ? 2 : 1);
  const glowA = !glow || t >= gS ? 0 : L.impactGlowAlpha * k * Math.min(1, t / rise) * (1 - t / gS) ** 2;
  return { ring: r0 + (r1 - r0) * (1 - (1 - q) ** 3), ringA, glowA };
}

/** Confetti card k at age t from a taken box: writes the offset [dx, dy, dz] (m, y up) into out; returns its turn and alpha. */
export function confettiAt(k, t, out, seed = 0) {
  const L = ITEM_LOOK, [v0, v1] = L.confettiSpeedMps;
  const a = k * 2.399963 + seed, e = 0.5 + 0.9 * rnd(k * 3.3 + seed), v = v0 + (v1 - v0) * rnd(k + seed);
  const d = (1 - Math.exp(-t * 1.8)) / 1.8, side = Math.sin(t * 5 + k) * 0.15 * t;
  out[0] = Math.cos(a) * Math.cos(e) * v * d + Math.cos(a + 1.57) * side;
  out[1] = Math.sin(e) * v * d - 0.5 * L.confettiGravityMps2 * 0.6 * t * t;
  out[2] = Math.sin(a) * Math.cos(e) * v * d + Math.sin(a + 1.57) * side;
  const f = t / L.confettiS;
  return { rx: t * (3 + 4 * rnd(k * 7.1 + seed)), ry: t * (2 + 3 * rnd(k * 1.9 + seed)), alpha: f >= 1 ? 0 : 1 - ease01((f - 0.55) / 0.45) };
}

/** A box card's glint: where it is along the card's diagonal (-1..1) and how bright (0..1); one sweep each glintEveryS, boxes out of step. */
export function boxGlint(t, i = 0, gentle = false) {
  const L = ITEM_LOOK, p = (((t + i * 0.37 * L.glintEveryS) % L.glintEveryS) + L.glintEveryS) % L.glintEveryS;
  if (p >= L.glintS) return { u: 0, alpha: 0 };
  const q = p / L.glintS;
  return { u: q * 2 - 1, alpha: Math.sin(q * Math.PI) * (gentle ? 0.4 : 1) };
}

/** Freeze: the edge value the frost front has reached at amount a (1 = nothing frozen, frostFrontAt at full). */
export const frostThreshold = (a) => 1 - clamp01(a) * (1 - SCREEN_LOOK.frostFrontAt);

/** Shatter at t seconds in with amount a: how far the cracks have grown (0..1) and how far the pieces slid (0..1); the OUT slides them home. */
export function shatterPhase(t, a) {
  const L = SCREEN_LOOK, k = clamp01(a);
  return { crack: ease01(t / L.shatterCrackS) * k, slide: ease01((t - L.shatterCrackS) / L.shatterSlideS) * k };
}

/** Spiral: the radius (view heights) the spiral has opened to at amount a. */
export const spiralOpen = (a) => clamp01(a) * SCREEN_LOOK.spiralOpenR;

/** Pink: the heart vignette's size at amount a (big and off screen at 0, pinkHeartR[1] at full). */
export const pinkHeart = (a) => SCREEN_LOOK.pinkHeartR[0] + (SCREEN_LOOK.pinkHeartR[1] - SCREEN_LOOK.pinkHeartR[0]) * clamp01(a);

/** Signed distance to a heart of size 1 (point up at y = 0, lobes near y = 1; the shader's same formula). */
export function heartSd(x, y) {
  x = Math.abs(x);
  if (x + y > 1) return Math.hypot(x - 0.25, y - 0.75) - Math.SQRT2 / 4;
  const m = 0.5 * Math.max(x + y, 0);
  return Math.sqrt(Math.min(Math.hypot(x, y - 1) ** 2, Math.hypot(x - m, y - m) ** 2)) * Math.sign(x - y);
}

/** HUD slot card shuffle s seconds after a roll tick: the new card slides in from the right, tilted and small, and settles. */
export function shuffleAt(s, gentle = false) {
  const L = SLOT_LOOK, m = gentle ? L.gentleMul : 1, q = 1 - ease01(s / L.shuffleS);
  return { dx: L.shuffleDx * q * m, rotDeg: L.shuffleRotDeg * q * m, scale: 1 - (1 - L.shuffleScale) * q * m };
}

/** HUD slot orbit mote i of n at time t: x, y in slot radii (a tilted ring), size px, alpha, and whether it passes in front. */
export function slotOrbit(i, n, t, gentle = false) {
  const L = SLOT_LOOK, a = TAU * (i / n + L.orbitHz * t * (gentle ? 0.5 : 1)) + rnd(i) * 0.4;
  const s = Math.sin(a), [s0, s1] = L.orbitSizePx, wob = 1 + 0.06 * Math.sin(t * 0.9 + i * 1.3);
  return { x: Math.cos(a) * L.orbitR * wob, y: s * L.orbitR * L.orbitTilt, size: s0 + (s1 - s0) * (0.5 + 0.5 * s), alpha: L.orbitAlpha * (0.45 + 0.55 * (0.5 + 0.5 * s)), front: s > 0 };
}

// ---- polish 4: items as events -----------------------------------------------------------------
const METER = { hitK: 0, hitHex: '#ffffff', meterK: 0, meterHex: '#ffffff', remain: 0, badgeS: 1 };

/**
 * ScreenState -> the countdown meter, badge and hit tint (one shared object, rewritten each call): meterK
 * fades with the effect (Gentle a touch softer), remain 1..0 over the hold, badgeS pops past 1 as it lands
 * (no pop under Gentle); hitK is the hue tint fx/effects.js already shaped (Gentle halved there).
 */
export function meterLook(screen) {
  const o = METER, L = SCREEN_LOOK;
  o.hitK = 0; o.meterK = 0; o.remain = 0; o.badgeS = 1;
  if (!screen) return o;
  const gentle = !!(screen.params && screen.params.gentle), h = screen.hit;
  if (h && h.k > 0.001) { o.hitK = clamp01(h.k); o.hitHex = L.meterColours[h.kind] || '#ffffff'; }
  if (screen.kind && screen.kind !== 'none' && screen.durS > 0) {
    o.meterK = clamp01(screen.k) * (gentle ? L.meterGentle : 1);
    o.meterHex = L.meterColours[screen.kind] || '#ffffff';
    o.remain = clamp01(Number.isFinite(screen.remain) ? screen.remain : 1 - (screen.t || 0) / screen.durS);
    const q = clamp01((screen.t || 0) / L.badgePopS);
    o.badgeS = (0.6 + 0.4 * clamp01(screen.k)) * (1 + (gentle ? 0 : L.badgePop * Math.sin(Math.PI * q)));
  }
  return o;
}

/** A thrown bubble at age t: it leaves big and charged and settles to its size by launchS (Gentle: half the pop). */
export function launchPop(t, gentle = false) {
  const L = ITEM_LOOK, q = clamp01(t / L.launchS);
  return 1 + L.launchPop * (gentle ? 0.5 : 1) * (1 - q) * (1 - q);
}

/** The launch flare at age t: a glow that swells and goes, a ring that opens on the road; alpha 0 once over. */
export function launchFlare(t, gentle = false) {
  const L = ITEM_LOOK, life = L.launchS * (gentle ? 2 : 1), q = clamp01(t / life), [r0, r1] = L.launchRingM;
  const rise = gentle ? 0.3 : 0.04, k = gentle ? 0.5 : 1, up = Math.min(1, t / rise);
  return { glowR: L.launchGlowM * (0.5 + 0.5 * q), glowA: q >= 1 ? 0 : L.launchGlowAlpha * k * up * (1 - q) ** 2,
    ring: r0 + (r1 - r0) * (1 - (1 - q) ** 2), ringA: q >= 1 ? 0 : L.markAlpha * k * up * (1 - q) };
}

/** Impact fountain spark k of n at age t: writes [dx, dy, dz] (m, y up) into out; returns its alpha (glitter rising and spreading). */
export function impactFountain(k, n, t, out, seed = 0) {
  const L = ITEM_LOOK, [v0, v1] = L.fountainSpeedMps, a = k * 2.399963 + seed, v = v0 + (v1 - v0) * rnd(k * 1.7 + seed);
  const spread = 0.35 + (1 - Math.exp(-t * 2.2)) * (1.2 + 1.6 * rnd(k * 5.3 + seed));
  out[0] = Math.cos(a) * spread; out[2] = Math.sin(a) * spread;
  out[1] = v * t - 0.5 * L.fountainGravityMps2 * t * t;
  return clamp01(1 - t / L.fountainS) * (out[1] > -0.2 ? 1 : 0);
}
