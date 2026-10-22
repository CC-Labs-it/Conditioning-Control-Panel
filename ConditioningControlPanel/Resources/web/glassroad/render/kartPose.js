/* render/kartPose.js - the three-free pose law for every kart, so tests run it under node.
 *
 * Per kart and frame: lean into the turn (roll from yaw rate x speed, plus a drift lean), a small nose
 * lift while boosting, squash (the player's comes from FxState, a rival's from its own hop and land
 * events), a hit wobble (a damped sway, gentler and slower under Gentle Visuals, never a flicker), the
 * antenna sway from lateral acceleration and EMI's arms going up while a boost runs.
 * Plus the small helpers the render lane tests: tint mix, name tag fade, spark ladder, mote density.
 */
import { S, BOOST } from '../sim/state.js';
import { clamp, smoothstep } from '../core/math.js';

export function createPose() {
  return { roll: 0, lean: 0, pitch: 0, yawAdd: 0, sq: 0, sqPeak: 0, sqT: -1, wobT: -1, wobAmp: 0, wobHz: 0, ant: 0, arms: 0 };
}

const ease = (v, to, tc, dt) => v + (to - v) * (1 - Math.exp(-dt / Math.max(tc, 1e-4)));

/** Lean target in radians about the kart's +Z (back) axis: turning right (yawRate +) rolls the right side down. */
export function leanTarget(yawRate, speedPct, driftDir, L) {
  const turn = clamp(-yawRate * clamp(speedPct, 0, 1) * L.leanPerRadps, -L.leanMaxRad, L.leanMaxRad);
  return turn - Math.sign(driftDir || 0) * L.driftLeanRad;
}

/** A hit wobble: amplitude and rate drop under Gentle, so it reads as a sway, never a shake. */
export function kickWobble(pose, L, gentle, mul = 1) {
  pose.wobT = 0;
  pose.wobAmp = L.wobbleRad * mul * (gentle ? L.wobbleGentle : 1);
  pose.wobHz = L.wobbleHz * (gentle ? 0.5 : 1);
}

/** The wobble value at time t; 0 once the decay is under 2%. */
export function wobbleAt(t, amp, hz, tcS) {
  if (t < 0) return 0;
  const k = Math.exp(-t / tcS);
  return k < 0.02 ? 0 : amp * k * Math.sin(2 * Math.PI * hz * t);
}

/** A rival's landing or hop squash: peak at once, gone over goneS. */
export function kickSquash(pose, peak) { pose.sqPeak = Math.max(peak, pose.sqT >= 0 ? pose.sq : 0); pose.sqT = 0; }

/**
 * One frame. st = interpolated KartState, opts.squash = the player's fx.squash (null for a rival),
 * opts.spinRad = fxv.spinRad (Shatter, visual only). Writes roll, pitch, yawAdd, sq, ant, arms.
 */
export function stepPose(pose, st, dt, L, { topMps = 30, gentle = false, squash = null, spinRad = 0 } = {}) {
  pose.lean = ease(pose.lean, leanTarget(st[S.yawRate], st[S.speedPct], st[S.driftDir], L), L.leanTcS, dt);
  const boosting = st[S.boostKind] !== BOOST.NONE;
  pose.pitch = ease(pose.pitch, boosting && st[S.grounded] > 0 ? L.boostPitchRad : 0, L.pitchTcS, dt);
  let wob = 0;
  if (pose.wobT >= 0) {
    pose.wobT += dt;
    wob = wobbleAt(pose.wobT, pose.wobAmp, pose.wobHz, L.wobbleTcS);
    if (wob === 0 && pose.wobT > L.wobbleTcS) pose.wobT = -1;
  }
  pose.roll = pose.lean + wob;
  pose.yawAdd = wob * 0.5 + (spinRad || 0);
  if (squash !== null) pose.sq = squash;
  else if (pose.sqT >= 0) {
    pose.sqT += dt;
    const k = 1 - pose.sqT / L.botSquashGoneS;
    pose.sq = k > 0 ? pose.sqPeak * k * k : 0;
    if (k <= 0) pose.sqT = -1;
  } else pose.sq = 0;
  // antenna: lateral acceleration (yaw rate x speed) sways it outward, a squash pushes it down
  const latAcc = st[S.yawRate] * st[S.speedPct] * topMps;
  const antTo = clamp(latAcc * L.antSwayPerMps2, -L.antSwayMaxRad, L.antSwayMaxRad);
  pose.ant = ease(pose.ant, antTo, L.antTcS, dt);
  pose.arms = ease(pose.arms, boosting ? L.armsUpRad : 0, L.armsTcS, dt);
  return pose;
}

/** Kart tint i as linear-ish 0..1 rgb: white mixed toward LOOK.kartTints[i] by kartTintMix (i 0 = white). */
export function tintRgb(i, L) {
  const hex = L.kartTints[((i % L.kartTints.length) + L.kartTints.length) % L.kartTints.length];
  const n = parseInt(hex.slice(1), 16), m = i === 0 ? 0 : L.kartTintMix;
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => 1 - m + m * c / 255);
}

/** Name tag opacity: full inside tagNearM - tagFadeM, gone past tagNearM. */
export function tagAlpha(distM, L) {
  return 1 - smoothstep(L.tagNearM - L.tagFadeM, L.tagNearM, distM);
}

/** The spark ladder per drift tier 1..3: rate per second, size, petal share; mul scales the rate (rivals, LOD). */
export function sparkProfile(tier, L, mul = 1) {
  const i = clamp(tier | 0, 1, 3) - 1;
  return { rate: L.sparkRateByTier[i] * mul, size: L.sparkSizeByTier[i], petal: L.sparkPetalByTier[i] };
}

/** Ambient mote share 0..1 from the deep palette weight on screen; Gentle thins them. */
export function moteDensity(deepW, L, gentle) {
  return (L.moteBase + (L.moteDeep - L.moteBase) * clamp(deepW, 0, 1)) * (gentle ? L.moteGentle : 1);
}

/** Bump spark count from the closing speed dv (m/s), between bumpSparks[0] and [1]. */
export function bumpCount(dv, L) {
  return Math.round(L.bumpSparks[0] + (L.bumpSparks[1] - L.bumpSparks[0]) * clamp(dv / 8, 0, 1));
}

// ---- rivals (wave 2 art): colourways from art/palettes.json `karts`, pushed apart, one accessory each ----

/** Rival look numbers; LOOK.rival (render/look.js, integrator) overrides any of them. */
export const RIVAL_DEFAULTS = {
  // ART.md "Tint binding": the porcelain the base atlas was painted with, linear
  porcelainRef: [0.95597335, 0.83076988, 0.8796224],
  cupSatMul: 1.8, cupSatMin: 0.5, cupLight: [0.5, 0.72],
  saucerSatMul: 1.5, saucerSatMin: 0.35, saucerLight: [0.55, 0.8],
  // a near-grey colourway (pearl) stays pale instead of turning into a weak pink
  paleBelow: 0.3, paleSatMul: 0.6, paleLight: 0.86,
  rimGain: 0.85, rimPow: 2.2,
  rimHex: { petal: '#ff5fae', mint: '#3fe8c4', honey: '#ffbf2e', periwinkle: '#7f86ff', apricot: '#ff7d45', tide: '#3fb4ff', pearl: '#f4ecff' },
  accessories: ['head-bow', 'head-cat-ears', 'head-halo', 'head-spiral-crown'],
};

export const rivalNumbers = (L) => ({ ...RIVAL_DEFAULTS, ...((L && L.rival) || {}) });

/** '#rrggbb' -> sRGB 0..1. */
export function hexRgb(hex) {
  const n = parseInt(String(hex).replace('#', ''), 16) || 0;
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => c / 255);
}

export const srgbToLinear = (c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);

function toHsl([r, g, b]) {
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2, d = mx - mn;
  const s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
  let h = 0;
  if (d) h = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [((h * 60) + 360) % 360, s, l];
}

function fromHsl([h, s, l]) {
  const c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs(((h / 60) % 2) - 1)), m = l - c / 2;
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return [r + m, g + m, b + m];
}

/** Push an sRGB colour apart for speed: more saturation inside a lightness band; a near-grey one stays pale. */
export function pushColour(rgb, satMul, satMin, [lo, hi], R = RIVAL_DEFAULTS) {
  const [h, s, l] = toHsl(rgb);
  if (s < R.paleBelow) return fromHsl([h, s * R.paleSatMul, Math.max(l, R.paleLight)]);
  return fromHsl([h, clamp(Math.max(satMin, s * satMul), 0, 1), clamp(l, lo, hi)]);
}

/** The accessory id for racer i (null for the player): the colourway's own, else spread over the four. */
export function accessoryFor(i, table, L) {
  if (i <= 0) return null;
  const R = rivalNumbers(L), list = table ? Object.values(table) : [];
  const e = list.length ? list[(i - 1) % list.length] : null;
  return (e && e.accessory) || R.accessories[(i - 1) % R.accessories.length];
}

/**
 * Racer i's look (null for the player, who keeps EMI's colours). table = palettes.json `karts` (or null:
 * then LOOK.kartTints). Returns sRGB cup / saucer / rim, the linear multipliers the kart shader applies
 * (tintLinear / porcelainRef, ART.md), the name and the accessory id.
 */
export function rivalLook(i, table, L) {
  if (i <= 0) return null;
  const R = rivalNumbers(L), list = table ? Object.entries(table) : [];
  let name, cupHex, saucerHex, accessory;
  if (list.length) {
    const [n, e] = list[(i - 1) % list.length];
    name = n; cupHex = e.cup; saucerHex = e.saucer || e.cup; accessory = e.accessory;
  } else {
    const t = (L && L.kartTints) || ['#ffffff'];
    name = 'tint' + i; cupHex = saucerHex = t[i % t.length];
  }
  const cup = pushColour(hexRgb(cupHex), R.cupSatMul, R.cupSatMin, R.cupLight, R);
  const saucer = pushColour(hexRgb(saucerHex), R.saucerSatMul, R.saucerSatMin, R.saucerLight, R);
  const rim = R.rimHex[name] ? hexRgb(R.rimHex[name]) : pushColour(cup, 2, 0.8, [0.55, 0.65], R);
  const mul = (c) => c.map((v, k) => srgbToLinear(v) / R.porcelainRef[k]);
  return {
    name, cup, saucer, rim, cupMul: mul(cup), saucerMul: mul(saucer), rimLin: rim.map(srgbToLinear),
    accessory: accessory || accessoryFor(i, null, L),
    hex: '#' + rim.map((v) => Math.round(clamp(v, 0, 1) * 255).toString(16).padStart(2, '0')).join(''),
  };
}
