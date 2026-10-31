/* render/airTrickMath.js - EMI's air trick on a long or high jump (owner, 2026-10-30), three-free so tests run it.
 *
 * A beat after the kart leaves the ground on a jump that will last (a ramp, a real gap, a drop), EMI springs
 * out of the cup, turns one full 360 about the kart's up axis, holds in the air with one arm up and one of
 * four faces for about a second, then drops back into the cup and settles with a damped wobble.
 * Timeline: launch (rise, spin starts) > hang (float, arm up; ends at hangMaxS or a beat after the kart lands,
 * so she lands after it) > return (a small rise and gather, then a stretched fall that eases into the cup)
 * > settle (squash into the cup, a dip and a damped wobble). A plain hop never qualifies.
 * Landing follow-through (owner, 2026-10-30: "the landing is abrupt"): anticipation, overlap, squash.
 * Visual only: never touches the sim or the camera. Gentle halves the height and slows the spin and wobble;
 * motion off or a blocked frame (rescue, hat glide, phrase ride) ends it at once.
 */
export const AIR_TRICK = Object.freeze({
  lagS: 0.12,          // EMI leaves a beat after the kart (the slight async)
  minAirS: 0.75,       // predicted flight needed to start at lagS
  lateStartS: 0.5,     // still airborne this long (a drop off a ledge): go anyway
  hopLateS: 0.9,       // a flight that began with a hop (0.52 s on the flat) needs this much air
  rampVyMps: 9,        // rising faster than this on the first frame: a ramp or kicker, never a plain hop
  gravityMps2: 28,     // feel.json hop.gravityMps2, for the flight estimate
  launchS: 0.42, hangMaxS: 0.95, hangMinS: 0.3, landHoldS: 0.18, returnS: 0.5, settleS: 1.0,
  liftM: 1.05,         // model units over the cup rest (EMI is about 0.75 tall; 0.8 m in the world)
  floatM: 0.05, floatHz: 1.1,
  antS: 0.14, antUp: 0.1,   // return: rise this much (of the lift) and gather before the drop
  stretch: 0.1, squash: 0.22,   // fall stretch, landing squash at the bottom of the dip (fraction of height)
  spinStartS: 0.05, spinS: 0.78,
  armAtS: 0.14, armInS: 0.2, armOutS: 0.3,
  faceAtS: 0.12, faceKeepS: 0.5,
  wobRad: 0.2, wobHz: 2.2, wobTcS: 0.3, dipM: 0.12, dipS: 0.12,
  gentle: { lift: 0.5, spin: 1.6, wob: 0.5, wobHz: 0.5 },
  faces: ['cheer', 'wink', 'sparkle', 'heart'],
});

const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const smooth = (x) => { x = clamp01(x); return x * x * (3 - 2 * x); };
const smoother = x => { x = clamp01(x); return x * x * x * (x * (x * 6 - 15) + 10); };
const outBack = (x) => { x = clamp01(x); const c = 1.4, y = x - 1; return 1 + (c + 1) * y * y * y + c * y * y; };
const hermite = (a, b, va, vb, duration, u) => {
  u = clamp01(u); const u2 = u * u, u3 = u2 * u;
  return (2 * u3 - 3 * u2 + 1) * a + (u3 - 2 * u2 + u) * va * duration
    + (-2 * u3 + 3 * u2) * b + (u3 - u2) * vb * duration;
};
const TAU = Math.PI * 2;

export function createAirTrick(seed = 0) {
  return {
    phase: 'idle', t: 0, age: 0, used: false, hop: null, landed: -1, rng: (seed * 2654435761 + 12345) >>> 0,
    face: null, lastFace: null, arm: 1, spinSign: 1, from: 0, fromV: 0, gentle: false,
    out: { active: false, phase: 'idle', lift: 0, spin: 0, armK: 0, arm: 1, face: null, wobRoll: 0, wobPitch: 0, squash: 0 },
  };
}

function rand(s) { s.rng = (Math.imul(s.rng, 1664525) + 1013904223) >>> 0; return s.rng; }

export function resetAirTrick(s) {
  s.phase = 'idle'; s.t = 0; s.age = 0; s.landed = -1; s.face = null;
  const o = s.out; o.active = false; o.phase = 'idle'; o.lift = o.spin = o.armK = o.wobRoll = o.wobPitch = o.squash = 0; o.face = null;
}

/**
 * Will this flight last? airTime and velY now. A rising kart lands back at its takeoff height after
 * 2 v0 / g = 2 t + 2 v / g in all. hop = the flight began with a hop (a drift hop never tricks).
 */
export function longFlight(airTime, velY, T = AIR_TRICK, hop = false) {
  if (airTime >= (hop ? T.hopLateS : T.lateStartS)) return true;
  return !hop && airTime >= T.lagS && 2 * airTime + 2 * Math.max(0, velY) / T.gravityMps2 >= T.minAirS;
}

function start(s, gentle, T) {
  s.phase = 'launch'; s.t = 0; s.age = 0; s.landed = -1; s.used = true; s.gentle = !!gentle;
  let f = T.faces[rand(s) % T.faces.length];
  if (f === s.lastFace) f = T.faces[(T.faces.indexOf(f) + 1 + (rand(s) % (T.faces.length - 1))) % T.faces.length];
  s.face = s.lastFace = f;
  s.arm = rand(s) & 1 ? 1 : -1;
  s.spinSign = rand(s) & 2 ? 1 : -1;
}

/**
 * One frame. sig = { air, airTime, velY, hopT (s since the last hop), gentle, motionOff, blocked }. Returns s.out:
 * { active, phase, lift (model units along the kart's up), spin (rad about up), armK 0..1, arm (+1 right, -1 left),
 *   face (id or null), wobRoll, wobPitch (rad), squash (fraction of height: + squashed, - stretched) }.
 */
export function stepAirTrick(s, dt, sig = {}, T = AIR_TRICK) {
  dt = Math.min(Math.max(dt || 0, 0), 0.1);
  const air = !!sig.air;
  if (sig.motionOff || sig.blocked) { if (s.phase !== 'idle') resetAirTrick(s); s.used = s.used || air; return s.out; }
  if (s.phase === 'idle') {
    if (!air) { s.used = false; s.hop = null; }
    else {
      // the flight's first frame says whether a hop started it (render/kartPose.js observeKart does the same)
      // a hop pressed on a ramp lip still flies like a ramp (track/ramps.js upMps 11-16; a hop is 6.8 m/s)
      if (s.hop == null) s.hop = (sig.hopT ?? 99) < 0.2 + (sig.airTime || 0) && (sig.velY || 0) < T.rampVyMps;
      if (!s.used && longFlight(sig.airTime || 0, sig.velY || 0, T, s.hop)) start(s, sig.gentle, T);
    }
    if (s.phase === 'idle') return s.out;
  }
  s.t += dt; s.age += dt;
  if (!air && s.landed < 0 && (s.phase === 'launch' || s.phase === 'hang')) s.landed = 0;
  else if (s.landed >= 0) s.landed += dt;
  if (s.phase === 'launch' && s.t >= T.launchS) { s.phase = 'hang'; s.t -= T.launchS; }
  if (s.phase === 'hang' && (s.t >= T.hangMaxS || (s.landed >= T.landHoldS && s.t >= T.hangMinS))) {
    const over = s.t >= T.hangMaxS ? s.t - T.hangMaxS : Math.min(s.t - T.hangMinS, s.landed - T.landHoldS);
    s.t -= over; s.from = liftAt(s, T);
    s.fromV = T.floatM * TAU * T.floatHz * Math.cos(TAU * T.floatHz * s.t);
    s.phase = 'return'; s.t = over;
  }
  if (s.phase === 'return' && s.t >= T.returnS) { s.phase = 'settle'; s.t -= T.returnS; }
  if (s.phase === 'settle' && s.t >= T.settleS) {
    resetAirTrick(s); s.used = air;   // still flying after the settle: wait for the next takeoff
    return s.out;
  }
  const g = s.gentle ? T.gentle : null, o = s.out;
  o.active = true; o.phase = s.phase; o.arm = s.arm;
  o.lift = liftAt(s, T) * T.liftM * (g ? g.lift : 1);
  const spinS = T.spinS * (g ? g.spin : 1);
  o.spin = s.spinSign * TAU * smoother((s.age - T.spinStartS) / spinS);
  if (o.spin && Math.abs(Math.abs(o.spin) - TAU) < 1e-9) o.spin = 0;   // a whole turn is the rest pose
  const armIn = smooth((s.age - T.armAtS) / T.armInS);
  o.armK = s.phase === 'return' ? armIn * (1 - smooth(s.t / T.armOutS)) : s.phase === 'settle' ? 0 : armIn;
  o.face = s.phase !== 'settle' && s.age >= T.faceAtS ? s.face : s.phase === 'settle' && s.t < T.faceKeepS ? s.face : null;
  o.squash = squashAt(s, T) * (g ? g.wob : 1);
  if (s.phase === 'settle') {
    const hz = T.wobHz * (g ? g.wobHz : 1), k = Math.exp(-s.t / (T.wobTcS * (g ? 1 / g.wobHz : 1))), w = TAU * hz * s.t;
    const amp = T.wobRad * (g ? g.wob : 1) * k * smooth(s.t / .12) * settleTail(s.t, T);
    o.wobRoll = amp * Math.sin(w);
    o.wobPitch = 0.55 * amp * Math.sin(w + 1.3);
  } else o.wobRoll = o.wobPitch = 0;
  return o;
}

/** Normalised height 0..1 (the settle dip goes a little below 0). */
function settleTail(t, T) { return 1 - smooth((t - T.settleS * .65) / (T.settleS * .35)); }
function liftAt(s, T) {
  if (s.phase === 'launch') return outBack(s.t / T.launchS);
  if (s.phase === 'hang') return 1 + T.floatM * Math.sin(TAU * T.floatHz * s.t) * smooth(s.t / 0.2);
  if (s.phase === 'return') {
    // Match both sides' velocities: float into the gather, then fall into one damped compression.
    const top = s.from + T.antUp;
    if (s.t < T.antS) return hermite(s.from, top, s.fromV, 0, T.antS, s.t / T.antS);
    const p = clamp01((s.t - T.antS) / (T.returnS - T.antS));
    return hermite(top, 0, 0, -(T.dipM / T.liftM) * Math.E / T.dipS, T.returnS - T.antS, p);
  }
  if (s.phase === 'settle') { const u = s.t / T.dipS; return -(T.dipM / T.liftM) * u * Math.exp(1 - u) * settleTail(s.t, T); }
  return 0;
}

/** Squash and stretch: a gather before the drop, stretched by the fall, squashed into the cup, springing back. */
function squashAt(s, T) {
  if (s.phase === 'return') {
    if (s.t < T.antS) return 0.4 * T.stretch * Math.sin(Math.PI * s.t / T.antS);
    return -T.stretch * smooth((s.t - T.antS) / (T.returnS - T.antS));
  }
  if (s.phase === 'settle') {
    // squashed while the dip is low, stretched on the rebound: the same spring as the dip
    const spring = -T.squash * liftAt(s, T) * T.liftM / T.dipM;
    return -T.stretch + (spring + T.stretch) * smooth(s.t / 0.05);
  }
  return 0;
}
