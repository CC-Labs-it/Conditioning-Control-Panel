/* render/boostRimMath.js - the boost speed rim's numbers and pure math (no three.js; render/boostRim.js builds the nodes).
 *
 * Owner ask 2026-10-30: when a boost (pad, drift, launch, Surge, slipstream, music/clean speed bonus) carries the kart
 * past its normal top speed, a soft rainbow rim of radial speed streaks comes in from the four screen edges and
 * corners (densest in the corners), smeared toward the vanishing point, coloured by a rainbow skewed toward the live
 * course palette. The centre (road, kart, rivals, HUD middle) stays clear.
 *
 * ONE SIGNAL: the rim reads FxState.speedFeel, the settled share the drawn-only boost FOV widen reads
 * (render/camera.js speedFov), divided back by its motion / Gentle multiplier so the share past 1 (speed above top)
 * is the same number under every option, plus the boost ladder envelope FxState.speedLines while a boost is live
 * (mini-turbo 0.5 .. tier 3 / pad 1, fx/cues.js boostLadder). A weak drift boost is a light rim, a pad or Surge full.
 * Screen-space only: it never touches the camera pose (cameraMath.js stays the only pose law).
 * Safety: Motion off = no rim at all (the widen is off too), Motion less = half the flow speed, Gentle = half the
 * level, half the flow, rise at least gentle.riseMs (>= 300 ms) and fall at least gentle.fadeMs, a strict photosafe
 * host halves the level again. The flow's per-pixel temporal rate stays under FLOW_HZ_MAX (no 3 Hz+ flicker).
 * Tunnel Vision: `ride` (BOOST_RIM.ride) is a second level that follows the ride's live speed, so the ride reads as a
 * boost and more: a bigger, denser, more saturated rim. Same safety rules, and it settles the same way.
 */
import { clamp, smoothstep } from '../core/math.js';
import { SPEED_FEEL, speedFeelMul } from './cameraMath.js';

export const BOOST_RIM = Object.freeze({
  // intensity: over = the part of the widen share past top speed (0 at top, 1 at speed.maxPct)
  overW: 0.55,              // weight of speed gained above top speed
  envW: 0.6,                // weight of the boost ladder envelope
  envFrom: 0.4, envTo: 1,   // envelope remap: a mini-turbo (0.5) barely shows, tier 3 / pad (1) full
  riseS: 0.3, fallS: 0.6,   // critically damped settle (95% in riseS up, fallS down): never a pop
  offFallS: 0.18,           // hit cam / finish slow frame: a quick ease out, still no cut
  gentleMul: 0.5, photosafeMul: 0.6, gentleRiseMul: 2,   // Gentle rises twice as slowly (and never under gentle.riseMs)
  // look
  maxAlpha: 0.5,            // the strongest a streak covers the picture (soft, never opaque)
  washAlpha: 0.14,          // the faint coloured corner wash under the streaks
  clearBox: 0.6,            // the centre 60% box (both axes) is never touched
  cornerBias: 0.55,         // 0 = an even frame band, 1 = only the corners
  streaks: Object.freeze({ high: 2, mid: 2, phone: 1 }),   // streak layers per quality tier (one draw, one pass)
  cells: Object.freeze([46, 73]),                          // radial streak cells per layer round the screen
  dashFreq: Object.freeze([1.6, 2.3]),                     // dashes per unit of centre distance, per layer
  dashFreqVar: Object.freeze([0.75, 1.25]),               // each streak runs at this share of its layer rate
  flowUps: 0.85,            // centre-distance units per second the dashes travel inward at full motion
  lessMul: 0.5,             // Motion 'less'
  gentleFlowMul: 0.5,
  hueStops: 6,
  hueSpread: 1,             // rainbow span (turns) before the palette skew pulls it in
  paletteSkew: 0.62,        // 0 = a plain rainbow, 1 = only the two course hues
  satMin: 0.72, lightMix: 0.45, lightMin: 0.5, lightMax: 0.8,
  // Tunnel Vision's ride (owner 2026-11-01, "like the boost but more"): the same rim pushed past anything a boost can
  // reach, by a second level `ride` 0..1 that follows the live speed. ride 0 is the boost's look exactly.
  ride: Object.freeze({
    pctFrom: 1,             // speed.topMps share where a ride sits at `floor` (a ride starts about here)
    pctTop: 3,              // the share where the colour is full (99 m/s, an 800 m gap in the 8 s limit)
    floor: 0.3,             // the least a ride shows, however slow
    curve: 0.8,             // under 1 lifts ordinary rides: today's 1.6 x top reads 0.57, 2.2 x top 0.77
    gentleMul: 0.45,        // Gentle's cap on ride (the base level is halved as well)
    clearBox: 0.42,         // at ride 1 the clear centre box (boost 0.6): road ahead, target kart and player stay clean
    cornerBias: 0.15,       // at ride 1 the whole frame takes colour, not just the corners (boost 0.55)
    washAlpha: 0.72,        // at ride 1 the coloured wash (boost 0.14)
    maxAlpha: 0.92,         // at ride 1 the streaks (boost 0.5)
    saturate: 1.1,          // at ride 1 hue pushed this much further from grey (a pastel course stays saturated)
    flowExtra: 0.18,        // at ride 1 the flow runs this much faster (off under Gentle); stays under FLOW_HZ_MAX
  }),
});

/** How coloured a ride is at this speed share, 0..1: the floor at pctFrom, full at pctTop. */
export function rideHeat(pct, L = BOOST_RIM) {
  const R = L.ride, t = clamp(((+pct || 0) - R.pctFrom) / (R.pctTop - R.pctFrom), 0, 1);
  return R.floor + (1 - R.floor) * Math.pow(t, R.curve);
}

/** The ride level's target before the settle: the fx tunnel envelope (0 outside a ride) times the speed's heat. */
export function rideTarget(v = {}, L = BOOST_RIM) {
  if ((v.motion || 'full') === 'off') return 0;
  const k = clamp(+v.tunnel || 0, 0, 1);
  return k > 0 ? k * rideHeat(v.speedPct, L) : 0;
}

/** Highest per-pixel temporal frequency (Hz) of the dash flow: the fastest streak (dashFreqVar[1] of the faster layer) passing a fixed pixel. */
export const FLOW_HZ_MAX = 3;
export function flowHz(flowMul = 1, L = BOOST_RIM) {
  return Math.max(...L.dashFreq) * L.dashFreqVar[1] * L.flowUps * flowMul;
}

/** The share of the widen past top speed, 0..1, from the settled FxState.speedFeel (motion / Gentle undone). */
export function overTop(speedFeel, motion = 'full', gentle = false, F = SPEED_FEEL) {
  const mul = speedFeelMul(motion, gentle, F);
  if (!(mul > 0)) return 0;
  const raw = (+speedFeel || 0) / mul, cap = F.boostDeg / F.topDeg;
  return cap > 1 ? clamp((raw - 1) / (cap - 1), 0, 1) : 0;
}

/**
 * The rim's target level 0..1 before the settle.
 * v = { speedFeel, speedLines, boostKind, motion, gentle }. The ladder envelope only counts while a boost is live or
 * the kart is still over top speed (an item throw's speed-line flick never lights the rim).
 */
export function rimTarget(v = {}, L = BOOST_RIM, F = SPEED_FEEL) {
  const motion = v.motion || 'full';
  if (motion === 'off') return 0;
  const over = overTop(v.speedFeel, motion, !!v.gentle, F);
  const live = (v.boostKind | 0) > 0 || over > 0.02;
  const env = live ? smoothstep(L.envFrom, L.envTo, clamp(+v.speedLines || 0, 0, 1)) : 0;
  // force: the ?boostRim= dev look; tunnel: a ride holds the rim from its first frame (it starts below top speed)
  return clamp(Math.max(over * L.overW + env * L.envW, +v.force || 0, +v.tunnel || 0), 0, 1);
}

/** Flow speed multiplier: Motion less and Gentle each slow it, off stops it. */
export function rimFlowMul(motion = 'full', gentle = false, L = BOOST_RIM) {
  if (motion === 'off') return 0;
  return (motion === 'less' ? L.lessMul : 1) * (gentle ? L.gentleFlowMul : 1);
}

/** The settle times: Gentle never rises faster than riseMs nor fades faster than fadeMs; `off` eases out quickly. */
export function rimTimes({ gentle = false, riseMs = 300, fadeMs = 600, off = false } = {}, L = BOOST_RIM) {
  const rise = gentle ? Math.max(L.riseS * L.gentleRiseMul, riseMs / 1000) : L.riseS;
  const fall = off ? (gentle ? Math.max(L.offFallS, fadeMs / 1000) : L.offFallS) : gentle ? Math.max(L.fallS, fadeMs / 1000) : L.fallS;
  return { rise, fall };
}

/**
 * The per-frame state: settle(view, dt) -> level 0..1 (already Gentle / photosafe scaled) and the flow clock.
 * view = { enabled, off, speedFeel, speedLines, boostKind, motion, gentle, photosafe, riseMs, fadeMs }.
 * dt <= 0 (pause) freezes everything. enabled false (menu, garage, intro, results) clears at once: the rim is only
 * ever built up in a race, so a cut there never shows. off (hit cam, finish slow frame) eases out.
 */
export function createRimState(L = BOOST_RIM, F = SPEED_FEEL) {
  const s = { x: 0, v: 0 }, r = { x: 0, v: 0 }, out = { level: 0, raw: 0, ride: 0, flowS: 0, flowMul: 1 };
  const settle = (e, target, t, dt) => {
    const w = 4.744 / Math.max(0.02, target > e.x ? t.rise : t.fall), n = Math.min(24, Math.ceil(dt * 120)), h = Math.min(dt, 0.2) / n;
    for (let k = 0; k < n; k++) { e.v += (w * w * (target - e.x) - 2 * w * e.v) * h; e.x += e.v * h; }
    if (e.x < 0) { e.x = 0; e.v = Math.max(0, e.v); }
    if (e.x > 1) { e.x = 1; e.v = Math.min(0, e.v); }
    if (Math.abs(e.x - target) < 1e-4 && Math.abs(e.v) < 1e-3) { e.x = target; e.v = 0; }
  };
  const clear = () => { s.x = 0; s.v = 0; r.x = 0; r.v = 0; out.level = 0; out.raw = 0; out.ride = 0; };
  function update(view = {}, dt = 0) {
    if (view.enabled === false) { clear(); return out; }
    if (!(dt > 0)) return out;
    const gentle = !!view.gentle, target = view.off ? 0 : rimTarget(view, L, F);
    const t = rimTimes({ gentle, riseMs: view.riseMs, fadeMs: view.fadeMs, off: !!view.off }, L);
    settle(s, target, t, dt);
    settle(r, view.off ? 0 : rideTarget(view, L), t, dt);
    out.raw = s.x;
    const safe = view.photosafe ? L.photosafeMul : 1;
    out.level = s.x * (gentle ? L.gentleMul : 1) * safe;
    out.ride = r.x * (gentle ? L.ride.gentleMul : 1) * safe;
    out.flowMul = rimFlowMul(view.motion || 'full', gentle, L) * (gentle ? 1 : 1 + L.ride.flowExtra * out.ride);
    // the flow clock restarts whenever the rim is fully out, so its float phase never grows large or wraps mid-boost
    out.flowS = s.x > 0 ? (out.flowS + Math.min(dt, 0.2) * out.flowMul) % 1000 : 0;
    return out;
  }
  return { update, frame: out, reset() { clear(); out.flowS = 0; } };
}

// ---- colour: a rainbow skewed toward the course palette ----
const hex6 = (h) => (/^#?[a-f\d]{6}$/i.test(h || '') ? h.replace('#', '') : null);
export function hexToHsl(hex) {
  const m = hex6(hex);
  if (!m) return [0, 0, 1];
  const r = parseInt(m.slice(0, 2), 16) / 255, g = parseInt(m.slice(2, 4), 16) / 255, b = parseInt(m.slice(4, 6), 16) / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, d = max - min;
  if (d < 1e-6) return [0, 0, l];
  const s = d / (1 - Math.abs(2 * l - 1));
  const h = max === r ? ((g - b) / d + (g < b ? 6 : 0)) / 6 : max === g ? ((b - r) / d + 2) / 6 : ((r - g) / d + 4) / 6;
  return [h, s, l];
}
function hslToRgb(h, s, l, out, o) {
  const k = (n) => (n + h * 12) % 12, a = s * Math.min(l, 1 - l);
  for (let i = 0; i < 3; i++) { const n = [0, 8, 4][i], kk = k(n); out[o + i] = l - a * Math.max(-1, Math.min(kk - 3, 9 - kk, 1)); }
}
/** The signed shortest turn from hue a to hue b, in (-0.5, 0.5]. */
export function hueDelta(a, b) { let d = ((b - a) % 1 + 1.5) % 1 - 0.5; if (d === -0.5) d = 0.5; return d; }

/**
 * Fill `out` (Float32Array(stops * 3), sRGB 0..1) with the rim's hue stops: a rainbow that starts at the primary hue,
 * each stop pulled paletteSkew of the way toward its palette anchor (even stops the primary, odd the accent), with
 * the palette's own lightness mixed in so a pastel course (Threshold) reads as a whitish rainbow. Returns out.
 * Runs on a palette change only (no per-frame allocation).
 */
export function rimHues(primaryHex, accentHex, out = new Float32Array(BOOST_RIM.hueStops * 3), L = BOOST_RIM) {
  const P = hexToHsl(primaryHex), A = hexToHsl(accentHex || primaryHex), n = L.hueStops;
  for (let i = 0; i < n; i++) {
    const anchor = i % 2 ? A : P;
    const rainbow = P[0] + (i / n) * L.hueSpread;
    const h = ((rainbow + hueDelta(rainbow, anchor[0]) * L.paletteSkew) % 1 + 1) % 1;
    const s = Math.max(L.satMin, anchor[1]);
    const l = clamp(0.6 + (anchor[2] - 0.6) * L.lightMix, L.lightMin, L.lightMax);
    hslToRgb(h, s, l, out, i * 3);
  }
  return out;
}

/** The rim mask at a screen point (uv 0..1, top-left origin) for the JS mirror of the shader (tests). */
export function rimMask(u, v, L = BOOST_RIM, ride = 0) {
  const x = Math.abs(u - 0.5) * 2, y = Math.abs(v - 0.5) * 2;
  const box = Math.max(x, y), corner = Math.min(1, Math.hypot(x, y) / Math.SQRT2);
  const clearBox = L.clearBox + (L.ride.clearBox - L.clearBox) * ride, bias = L.cornerBias + (L.ride.cornerBias - L.cornerBias) * ride;
  const d = box + (corner - box) * bias;
  if (box <= clearBox) return 0;
  return smoothstep(clearBox, 1, d) * smoothstep(clearBox, clearBox + 0.08, box);
}
