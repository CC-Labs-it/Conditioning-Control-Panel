/* render/kartPose.js - the three-free pose law for every kart, so tests run it under node.
 *
 * Per kart and frame: lean into the turn (roll from yaw rate x speed, plus a drift lean), a small nose
 * lift while boosting, squash (the player's comes from FxState, a rival's from its own hop and land
 * events), a hit wobble (a damped sway, gentler and slower under Gentle Visuals, never a flicker), the
 * antenna sway from lateral acceleration and EMI's arms going up while a boost runs.
 * Plus the small helpers the render lane tests: tint mix, name tag fade, spark ladder, mote density.
 * Polish 2 adds the reaction layer at the bottom (squash and stretch, secondary motion, EMI's face).
 */
import { S, BOOST, RESPAWN } from '../sim/state.js';
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

// ---- reactions (polish 2): every kart acts alive. Squash and stretch, a saucer twist, EMI's head on a
// spring, tilts away from hits, a dizzy beat, per-effect moods, place faces, idle breath, finish cheer.
// Edges come from state (observeKart) and from a few bus events (reactionsFor); stepReact writes r.out.

/** Reaction numbers; LOOK.react (render/look.js, integrator) overrides any of them. */
export const REACT_DEFAULTS = {
  gentleShake: 0.5,                                      // Gentle halves every shake-like motion
  hopSquash: 0.1, hopSquashS: 0.07, hopStretch: 0.13, hopStretchS: 0.3,
  hopTwistRad: 0.45, hopTwistS: 0.42, trickAirS: 0.55, trickSpinS: 0.75,
  landSqPerMps: 0.016, landSqMax: 0.24, landSqHz: 3, landSqZeta: 0.3,
  headHz: 3.2, headZeta: 0.3, headBobPerMps: 0.012, headDvMaxMps: 12, headBobMaxM: 0.1, headMaxRad: 0.5,
  turnHeadLean: 0.45, driftCounterLean: 1.5, boostLeanRad: 0.07, boostHeadBackRad: 0.16,
  bumpMinMps: 1, bumpTiltPerMps: 0.05, bumpTiltMaxRad: 0.34, tiltHz: 2.4, tiltZeta: 0.26, bumpHeadKnock: 1.4,
  wallMinMps: 3, sweatMps: 3.5, sweatCount: 4, dizzyMps: 6, dizzyS: 1.5, dizzyHz: 1.2, dizzyRad: 0.18, starCount: 8,
  spiralSpinRadps: 2.4, spiralHeadRad: 0.12, freezeStiff: 0.12, meltDroopRad: 0.32, meltSq: 0.08, meltBobM: -0.06,
  wiggleRad: 0.16, wiggleHz: 5, wiggleS: 1,
  overtakeHopM: 0.14, overtakeHopS: 0.34, cheerHopM: 0.16, cheerHz: 1.7, armsCheerRad: 1.1, sulkHeadRad: 0.3,
  breathHz: 0.32, breathSq: 0.02, breathBobM: 0.012, idleBelowPct: 0.03,
  glanceEveryS: [2.2, 4.6], glanceS: 1, glanceYawRad: 0.38, glancePitchRad: 0.16,
  faceHoldS: 1.3, faceShortS: 0.6, faceMinS: 0.22, blinkEveryS: [2.4, 5.2], blinkS: 0.11,
  // sprays over EMI's head (render/particles.js burst): dizzy stars, sweat drops
  sprayUpM: 1.35, starColours: ['#ffe27a', '#fff4fa', '#ffbf2e'], starSpeedMps: 1.8, starSizeM: 0.17,
  sweatColours: ['#bfefff', '#8fd8ff'], sweatSpeedMps: 1.5, sweatSizeM: 0.09,
};

export const reactNumbers = (L) => ({ ...REACT_DEFAULTS, ...((L && L.react) || {}) });

const TAU = Math.PI * 2;
const spr = () => ({ x: 0, v: 0 });

export function createReact(i = 0) {
  return {
    i, t: 0, rs: (i * 7919 + 17) % 2147483647 || 1,
    was: { init: false, air: false, boost: 0, place: 0, effect: null, shield: null, vyMin: 0 },
    hopT: -1, sq: spr(), twT: -1, twS: 1, twRad: 0, twFull: false, twSign: i & 1 ? -1 : 1, trick: true,
    spYaw: 0, tr: spr(), tp: spr(), hr: spr(), hp: spr(), hy: spr(), hb: spr(), vyPrev: null,
    dizT: -1, wigT: -1, liftT: -1, liftM: 0, liftS: 1, glT: -1, glYaw: 0, glUp: 0, glAt: 1.5 + i * 0.7,
    effect: null, finish: 0, melt: 0, stiffK: 0, boostLean: 0, idleT: 0, armsK: 0,
    timed: 'neutral', timedUntil: 0, shown: 'neutral', shownT: -1, blinkNext: 1 + i * 0.37,
    sprays: [],
    out: { sq: 0, yaw: 0, roll: 0, pitch: 0, lift: 0, hRoll: 0, hPitch: 0, hYaw: 0, hBob: 0, stiff: 1, arms: 0, armsK: 0, face: 'neutral', blink: false },
  };
}

const rnd = (r) => { r.rs = (r.rs * 16807) % 2147483647; return r.rs / 2147483647; };

/** A damped spring toward `to` (semi-implicit Euler at 120 Hz substeps). s = { x, v }; returns s.x. */
export function springStep(s, to, dt, hz, zeta) {
  if (!(dt > 0)) return s.x;
  const w = TAU * hz, n = Math.ceil(dt * 120), h = dt / n;
  for (let k = 0; k < n; k++) { s.v += (w * w * (to - s.x) - 2 * zeta * w * s.v) * h; s.x += s.v * h; }
  return s.x;
}

/** Hop squash t s after the hop: an anticipation squash (+), a stretch (-) on the rise, then 0. */
export function hopSquashAt(t, R) {
  if (t < 0) return 0;
  if (t < R.hopSquashS) return R.hopSquash * Math.sin(Math.PI * t / R.hopSquashS);
  const u = (t - R.hopSquashS) / R.hopStretchS;
  return u < 1 ? -R.hopStretch * Math.sin(Math.PI * u) : 0;
}

/** Landing squash from the fall speed (m/s, positive down), capped. */
export const landSquash = (fallMps, R) => clamp(fallMps * R.landSqPerMps, 0, R.landSqMax);

/** The saucer twist at u 0..1 of its run: a hop twist goes out and back, a trick eases one full turn. */
export function twistAt(u, rad, full) {
  if (u <= 0 || u >= 1) return 0;
  return full ? rad * u * u * (3 - 2 * u) : rad * Math.sin(Math.PI * u);
}

/** Tilt away from a hit at kart-local (x right, z back): roll (+ = right side up) and pitch (+ = lean back). */
export function tiltAway(lx, lz, dv, R) {
  const len = Math.hypot(lx, lz) || 1, amp = clamp(dv * R.bumpTiltPerMps, 0, R.bumpTiltMaxRad);
  return { roll: (lx / len) * amp, pitch: (-lz / len) * amp };
}

// Bus events state cannot show. racer -1 = every kart. hop, land, drift, boost, place and effects come from state.
const BUS = {
  bump: (p) => [[p.a, 'bump', { dv: p.dv || 0, p: p.p || null }], [p.b, 'bump', { dv: p.dv || 0, p: p.p || null }]],
  wall: (p, i = 0) => [[i, 'wall', { dv: p.dv || 0, side: p.side || 0 }]],
  countdown: (p) => [[-1, 'countdown', { n: p.n || 0 }]],
  go: () => [[-1, 'go', {}]],
  finish: (p, i = 0) => [[i, 'finish', {}]],
  'item-hit': (p) => (p.verdict && p.verdict !== 'hit' ? [[p.victim, 'saved', { verdict: p.verdict }]] : []),
  'race-result': (p) => (p.rows || []).map((row) => [row.i, 'finish', { place: row.place, count: p.rows.length }]),
};
const FROM_BOTS = new Set(['wall', 'finish']);

/** A bus event -> [[racer, kind, args]] reactions (bots' Phase A events arrive as racer-event). */
export function reactionsFor(name, p) {
  p = p || {};
  if (name === 'racer-event') return FROM_BOTS.has(p.name) ? BUS[p.name](p.p || {}, p.racer) : [];
  return BUS[name] ? BUS[name](p) : [];
}

const inRescue = (st) => { const ph = st[S.respawnPhase]; return ph === RESPAWN.PULL || ph === RESPAWN.HOVER || ph === RESPAWN.RELEASE; };

/** State edges since the last call -> [[kind, args]] (hop, airborne, land with its fall, boost, places, effects). */
export function observeKart(r, st, fxv) {
  const w = r.was, out = [], rescue = inRescue(st);
  const air = !rescue && st[S.airborne] > 0.5, boost = st[S.boostKind] | 0;
  const place = (fxv && fxv.place) | 0, effect = (fxv && fxv.effect) || null, shield = (fxv && fxv.shield) || null;
  if (w.init) {
    if (air && !w.air) out.push([st[S.hopT] < 0.2 ? 'hop' : 'airborne', {}]);
    if (!air && w.air && !rescue) out.push(['land', { fall: Math.max(0, -w.vyMin) }]);
    if (boost !== w.boost && boost !== BOOST.NONE) out.push(['boost', { kind: boost }]);
    if (place && w.place && place !== w.place) out.push([place < w.place ? 'overtake' : 'overtaken', { place }]);
    if (effect !== w.effect) out.push(['effect', { kind: effect }]);
    if (shield && shield !== w.shield) out.push(['shield', { kind: shield }]);
  }
  w.vyMin = air ? Math.min(w.air ? w.vyMin : 0, st[S.velY]) : 0;
  w.init = true; w.air = air; w.boost = boost; w.place = place; w.effect = effect; w.shield = shield;
  return out;
}

const timed = (r, id, holdS) => { r.timed = id; r.timedUntil = r.t + holdS; };
const knock = (r, roll, pitch, R, g) => {
  r.tr.v += roll * g * TAU * R.tiltHz; r.tp.v += pitch * g * TAU * R.tiltHz;
  r.hr.v -= roll * g * R.bumpHeadKnock * TAU * R.headHz; r.hp.v -= pitch * g * R.bumpHeadKnock * TAU * R.headHz;
};
const SELF_FACE = { golden: 'heart', surge: 'determined', glass: 'smug', echo: 'wink' };

const REACT = {
  hop(r, a, R) { r.hopT = 0; r.twT = 0; r.twS = R.hopTwistS; r.twFull = false; r.twRad = R.hopTwistRad * (r.twSign = -r.twSign); r.trick = false; },
  airborne(r) { r.trick = false; },
  land(r, a, R) { r.hopT = -1; r.sq.x = Math.max(r.sq.x, landSquash(a.fall || 0, R)); r.sq.v = 0; if ((a.fall || 0) > 9) timed(r, 'grin', R.faceShortS); },
  // a.lx / a.lz: the hit point in kart-local metres (karts.js fills them from the bump point)
  bump(r, a, R, g) {
    const dv = a.dv || 0;
    if (dv < R.bumpMinMps) return;
    const t = tiltAway(a.lx || 0, a.lz || 0, dv, R);
    knock(r, t.roll, t.pitch, R, g);
    if (dv >= R.dizzyMps) { r.dizT = 0; r.sprays.push(['stars', R.starCount]); }
    else { timed(r, 'x-eyes', R.faceShortS); if (dv >= R.sweatMps) r.sprays.push(['sweat', R.sweatCount]); }
  },
  wall(r, a, R, g) { if ((a.dv || 0) >= R.wallMinMps) REACT.bump(r, { dv: a.dv, lx: a.side || 1, lz: 0 }, R, g); },
  effect(r, a, R, g) {
    r.effect = a.kind || null;
    if (!a.kind) return;
    if (SELF_FACE[a.kind]) timed(r, SELF_FACE[a.kind], R.faceHoldS);
    if (a.kind === 'golden') r.wigT = 0;
    else if (a.kind === 'shatter') { knock(r, 0.18, -0.1, R, g); r.sprays.push(['stars', R.starCount >> 1]); }
    else if (!SELF_FACE[a.kind]) knock(r, 0.08, 0.12, R, g);
  },
  shield(r, a, R) { if (a.kind === 'golden') { r.wigT = 0; timed(r, 'heart', R.faceHoldS); } else timed(r, 'grin', R.faceShortS); },
  saved(r, a, R) { if (a.verdict !== 'mercy') timed(r, a.verdict === 'decoy' ? 'wink' : 'smug', R.faceHoldS); },
  boost(r, a, R) {
    if (a.kind >= BOOST.DRIFT1 && a.kind <= BOOST.DRIFT3) timed(r, 'sparkle', R.faceShortS + 0.25 * a.kind);
    else timed(r, a.kind === BOOST.LAUNCH ? 'cheer' : 'grin', a.kind === BOOST.LAUNCH ? R.faceHoldS : R.faceShortS);
  },
  overtake(r, a, R) { timed(r, 'smug', R.faceHoldS); r.liftT = 0; r.liftM = R.overtakeHopM; r.liftS = R.overtakeHopS; },
  overtaken(r, a, R) { timed(r, 'sad', R.faceHoldS); },
  // a countdown opens a new race: last race's cheer or sulk, daze and hops never carry over
  countdown(r, a, R) { r.finish = 0; r.dizT = -1; r.wigT = -1; r.liftT = -1; r.glT = 0; r.glYaw = 0; r.glUp = 1; timed(r, a.n === 1 ? 'determined' : 'neutral', R.glanceS); },
  go(r, a, R) { timed(r, 'grin', R.faceHoldS); r.glT = -1; },
  finish(r, a) {
    if (!a.place || !a.count) return;
    r.finish = a.place <= 3 ? 1 : a.place >= a.count ? -1 : 0;
    if (!r.finish) timed(r, 'happy', 3);
  },
};

/** Apply one reaction (kinds above); Gentle halves shake-like motion. */
export function reactTo(r, kind, a, R, gentle = false) {
  const f = REACT[kind];
  if (f) f(r, a || {}, R, gentle ? R.gentleShake : 1);
}

const STRONG = { spiral: 'dizzy', freeze: 'frozen', melt: 'sleepy', shatter: 'x-eyes', flash: 'x-eyes', blackout: 'blank', brainDrain: 'sleepy', pink: 'heart' };
const WEAK = { golden: 'happy', surge: 'determined', glass: 'smug', echo: 'wink' };
const NO_BLINK = new Set(['dizzy', 'sleepy', 'frozen', 'heart', 'x-eyes', 'blank', 'happy', 'cheer', 'wink', 'sparkle']);

/** The mood face from state: [id, strong]. A strong mood beats any timed face. */
export function moodFace(r, st) {
  if (r.effect && STRONG[r.effect]) return [STRONG[r.effect], true];
  if (r.dizT >= 0) return ['dizzy', true];
  if (r.finish) return [r.finish > 0 ? 'cheer' : 'sad', true];
  if (r.effect && WEAK[r.effect]) return [WEAK[r.effect], false];
  if (st[S.driftDir]) return ['drift-focus', false];
  if (st[S.airborne] > 0.5 && st[S.airTime] > 0.4) return ['grin', false];
  return ['neutral', false];
}

/** The face shown now: strong mood, else a timed face, else the mood; any change holds faceMinS. */
export function pickFace(r, st, R) {
  const [mood, strong] = moodFace(r, st);
  const want = strong || r.t >= r.timedUntil ? mood : r.timed;
  if (want !== r.shown && (strong || r.shownT < 0 || r.t - r.shownT >= R.faceMinS)) { r.shown = want; r.shownT = r.t; }
  return r.shown;
}

/** True while a blink runs (faces with open eyes only); schedules the next one. */
export function blinkNow(r, R) {
  if (r.t >= r.blinkNext + R.blinkS) r.blinkNext = r.t + R.blinkEveryS[0] + rnd(r) * (R.blinkEveryS[1] - R.blinkEveryS[0]);
  return r.t >= r.blinkNext && !NO_BLINK.has(r.shown);
}

/**
 * One frame after the reactions: writes r.out (sq, yaw, roll, pitch in radians on the body; lift in m;
 * hRoll, hPitch, hYaw, hBob for EMI; stiff 0..1 scales the lean; arms + armsK blend over the pose arms;
 * face + blink). lean = the pose's lean (EMI counter-leans it in a drift).
 */
export function stepReact(r, st, dt, R, { gentle = false, lean = 0 } = {}) {
  const o = r.out, g = gentle ? R.gentleShake : 1, ease = (v, to, tc) => v + (to - v) * (1 - Math.exp(-dt / tc));
  r.t += dt;
  const air = st[S.airborne] > 0.5 && !inRescue(st), drifting = !!st[S.driftDir], boosting = st[S.boostKind] !== BOOST.NONE;
  // squash: the hop curve, the landing spring (overshoots into a stretch), the melt sag, the idle breath
  let sq = springStep(r.sq, 0, dt, R.landSqHz, R.landSqZeta);
  if (r.hopT >= 0) { r.hopT += dt; sq += hopSquashAt(r.hopT, R); if (r.hopT > R.hopSquashS + R.hopStretchS) r.hopT = -1; }
  r.melt = ease(r.melt, r.effect === 'melt' ? 1 : 0, 0.25);
  r.stiffK = ease(r.stiffK, r.effect === 'freeze' ? 1 : 0, 0.1);
  const idle = !air && st[S.speedPct] < R.idleBelowPct;
  r.idleT = idle ? r.idleT + dt : 0;
  const bk = Math.min(1, r.idleT), breath = Math.sin(TAU * R.breathHz * r.t + r.i) * bk;
  sq += r.melt * R.meltSq + breath * R.breathSq;
  // yaw: hop twist, a full saucer turn on long air, the Spiral's slow spin (it finishes the turn it is on)
  if (air && !r.trick && st[S.airTime] > R.trickAirS) { r.trick = true; r.twT = 0; r.twS = R.trickSpinS; r.twFull = true; r.twRad = TAU * r.twSign; }
  let yaw = 0;
  if (r.twT >= 0) { r.twT += dt; const u = r.twT / r.twS; yaw += twistAt(u, r.twRad, r.twFull); if (u >= 1) r.twT = -1; }
  if (r.effect === 'spiral' || r.spYaw) {
    const before = r.spYaw;
    r.spYaw += R.spiralSpinRadps * dt;
    if (r.effect !== 'spiral' && Math.floor(r.spYaw / TAU) > Math.floor(before / TAU)) r.spYaw = 0;
  }
  yaw += r.spYaw;
  // body tilt: the hit springs, the golden wiggle, a lean back on a boost; lift: overtake hop, cheer bounce
  let roll = springStep(r.tr, 0, dt, R.tiltHz, R.tiltZeta), pitch = springStep(r.tp, 0, dt, R.tiltHz, R.tiltZeta);
  if (r.wigT >= 0) {
    r.wigT += dt;
    const u = r.wigT / R.wiggleS;
    if (u >= 1) r.wigT = -1; else roll += R.wiggleRad * g * Math.sin(TAU * R.wiggleHz * r.wigT) * (1 - u);
  }
  r.boostLean = ease(r.boostLean, boosting && !air ? R.boostLeanRad : 0, 0.15);
  pitch += r.boostLean;
  let lift = 0;
  if (r.liftT >= 0) { r.liftT += dt; const u = r.liftT / r.liftS; if (u >= 1) r.liftT = -1; else lift += 4 * r.liftM * u * (1 - u); }
  if (r.finish > 0) lift += R.cheerHopM * Math.abs(Math.sin(Math.PI * R.cheerHz * r.t));
  // EMI's head: springs toward targets, pushed by vertical speed changes (lags on a hop, bobs on a landing)
  const vy = st[S.velY];
  if (r.vyPrev !== null && dt > 0 && !inRescue(st)) r.hb.v -= clamp(vy - r.vyPrev, -R.headDvMaxMps, R.headDvMaxMps) * R.headBobPerMps * TAU * R.headHz;
  r.vyPrev = vy;
  let hYawTo = 0, hPitchTo = (boosting && !air ? R.boostHeadBackRad : 0) - r.melt * R.meltDroopRad - (r.finish < 0 ? R.sulkHeadRad : 0);
  const hRollTo = -lean * (drifting ? R.driftCounterLean : R.turnHeadLean), hBobTo = r.melt * R.meltBobM + breath * R.breathBobM;
  if (idle && r.glT < 0 && r.t >= r.glAt) {
    r.glT = 0; r.glYaw = (rnd(r) < 0.5 ? -1 : 1) * R.glanceYawRad; r.glUp = 0.4;
    r.glAt = r.t + R.glanceEveryS[0] + rnd(r) * (R.glanceEveryS[1] - R.glanceEveryS[0]);
  }
  if (r.glT >= 0) {
    r.glT += dt;
    const u = r.glT / R.glanceS, k = smoothstep(0, 0.25, u) * (1 - smoothstep(0.75, 1, u));
    hYawTo += r.glYaw * k; hPitchTo += R.glancePitchRad * r.glUp * k;
    if (u >= 1) r.glT = -1;
  }
  let hRoll = springStep(r.hr, hRollTo, dt, R.headHz, R.headZeta), hPitch = springStep(r.hp, hPitchTo, dt, R.headHz, R.headZeta);
  const hYaw = springStep(r.hy, hYawTo, dt, R.headHz, R.headZeta);
  r.hb.x = clamp(r.hb.x, -R.headBobMaxM, R.headBobMaxM);
  const hBob = springStep(r.hb, hBobTo, dt, R.headHz, R.headZeta);
  // dizzy beat and the Spiral: the head circles (halved under Gentle)
  let circ = 0;
  if (r.dizT >= 0) { r.dizT += dt; const u = r.dizT / R.dizzyS; if (u >= 1) r.dizT = -1; else circ = R.dizzyRad * (1 - u * u); }
  if (r.effect === 'spiral') circ = Math.max(circ, R.spiralHeadRad);
  if (circ) { const ph = TAU * R.dizzyHz * r.t; hRoll += circ * g * Math.sin(ph); hPitch += circ * g * 0.6 * Math.cos(ph); }
  const stiff = 1 - r.stiffK * (1 - R.freezeStiff), m = R.headMaxRad;
  o.sq = sq; o.yaw = yaw; o.roll = roll; o.pitch = pitch; o.lift = lift; o.stiff = stiff;
  o.hRoll = clamp(hRoll * stiff, -m, m); o.hPitch = clamp(hPitch * stiff, -m, m); o.hYaw = clamp(hYaw, -m, m); o.hBob = hBob * stiff;
  // arms: up and waving for a cheer or a golden wiggle, down for a sulk or a melt; armsK blends over the pose
  const armsTo = r.finish > 0 || r.wigT >= 0 ? R.armsCheerRad * (0.8 + 0.2 * Math.sin(TAU * R.cheerHz * r.t)) : 0;
  const own = r.finish !== 0 || r.wigT >= 0 || r.melt > 0.5;
  r.armsK = ease(r.armsK, own ? 1 : 0, 0.15);
  o.arms = armsTo; o.armsK = r.armsK;
  o.face = pickFace(r, st, R); o.blink = blinkNow(r, R);
  return o;
}
