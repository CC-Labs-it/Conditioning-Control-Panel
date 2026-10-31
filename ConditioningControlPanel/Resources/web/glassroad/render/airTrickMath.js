/* render/airTrickMath.js - EMI's air trick on a long or high jump (owner, 2026-10-30), three-free so tests run it.
 *
 * A beat after the kart leaves the ground on a jump that will last (a ramp, a real gap, a drop), EMI springs
 * out of the cup and turns one full 360 with an arm up and one of four faces.
 * One continuous arc rises, passes its apex and falls straight into a damped landing compression.
 * There is no held pose or second rise at the top (owner, 2026-10-31). A plain hop never qualifies.
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
  arcS: 1.0, settleS: 1.0,
  liftM: 1.05,         // model units over the cup rest (EMI is about 0.75 tall; 0.8 m in the world)
  stretch: 0.1, squash: 0.22,   // fall stretch, landing squash at the bottom of the dip (fraction of height)
  spinStartS: 0.05, spinS: 0.78,
  armAtS: 0.14, armInS: 0.2, armOutS: 0.3,
  faceAtS: 0.12, faceKeepS: 0.5,
  wobRad: 0.2, wobHz: 2.2, wobTcS: 0.3, dipM: 0.12,
  gentle: { lift: 0.5, spin: 1.6, wob: 0.5, wobHz: 0.5 },
  faces: ['cheer', 'wink', 'sparkle', 'heart'],
});

const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const smooth = (x) => { x = clamp01(x); return x * x * (3 - 2 * x); };
const smoother = x => { x = clamp01(x); return x * x * x * (x * (x * 6 - 15) + 10); };
const TAU = Math.PI * 2;

export function createAirTrick(seed = 0) {
  return {
    phase: 'idle', t: 0, age: 0, used: false, hop: null, rng: (seed * 2654435761 + 12345) >>> 0,
    face: null, lastFace: null, arm: 1, spinSign: 1, gentle: false,
    out: { active: false, phase: 'idle', lift: 0, spin: 0, armK: 0, arm: 1, face: null, wobRoll: 0, wobPitch: 0, squash: 0 },
  };
}

function rand(s) { s.rng = (Math.imul(s.rng, 1664525) + 1013904223) >>> 0; return s.rng; }

export function resetAirTrick(s) {
  s.phase = 'idle'; s.t = 0; s.age = 0; s.face = null;
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
  s.phase = 'launch'; s.t = 0; s.age = 0; s.used = true; s.gentle = !!gentle;
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
  if (s.phase === 'launch' || s.phase === 'return') {
    if (s.age >= T.arcS) { s.phase = 'settle'; s.t = s.age - T.arcS; }
    else { s.phase = s.age < T.arcS / 2 ? 'launch' : 'return'; s.t = s.age; }
  }
  if (s.phase === 'settle' && s.t >= T.settleS) {
    resetAirTrick(s); s.used = air;   // still flying after the settle: wait for the next takeoff
    return s.out;
  }
  const g = s.gentle ? T.gentle : null, o = s.out;
  o.active = true; o.phase = s.phase; o.arm = s.arm;
  o.lift = liftAt(s, T) * T.liftM * (g ? g.lift : 1);
  const spinS = Math.min(T.spinS * (g ? g.spin : 1), T.arcS - T.spinStartS);
  o.spin = s.spinSign * TAU * smoother((s.age - T.spinStartS) / spinS);
  if (o.spin && Math.abs(Math.abs(o.spin) - TAU) < 1e-9) o.spin = 0;   // a whole turn is the rest pose
  const armIn = smooth((s.age - T.armAtS) / T.armInS);
  o.armK = s.phase === 'settle' ? 0 : armIn * (1 - smooth((s.age - T.arcS + T.armOutS) / T.armOutS));
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
  if (s.phase === 'launch' || s.phase === 'return') {
    const u = clamp01(s.age / T.arcS);
    return 4 * u * (1 - u);
  }
  if (s.phase === 'settle') {
    // Match the arc's contact speed; this decay reaches the same authored dip depth.
    const velocity = 4 / T.arcS, tc = T.dipM * Math.E / (T.liftM * velocity);
    return -velocity * s.t * Math.exp(-s.t / tc) * settleTail(s.t, T);
  }
  return 0;
}

/** Stretch through the fall, compress on contact, then recover with the same landing curve. */
function squashAt(s, T) {
  if (s.phase === 'return') {
    return -T.stretch * smooth((s.age / T.arcS - .5) * 2);
  }
  if (s.phase === 'settle') {
    // squashed while the dip is low, stretched on the rebound: the same spring as the dip
    const spring = -T.squash * liftAt(s, T) * T.liftM / T.dipM;
    return -T.stretch + (spring + T.stretch) * smooth(s.t / 0.05);
  }
  return 0;
}
