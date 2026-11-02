// Bounded secondary acting. The authored clips keep their contacts and major actions.
const clamp = (x, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, Number.isFinite(x) ? x : 0));
const smooth = (x) => { x = clamp(x); return x * x * (3 - 2 * x); };
export const LIFE_LIMITS = Object.freeze({ antenna: .24, body: .065, shoulder: .014, foot: .055 });

/** Exact underdamped spring for a target held over dt, including velocity. */
export function spring(s, target, dt, frequency = 9, damping = .7) {
  if (!(dt > 0)) return s;
  const w = frequency, z = damping, wd = w * Math.sqrt(1 - z * z);
  const x = s.x - target, c = Math.cos(wd * dt), sn = Math.sin(wd * dt), e = Math.exp(-z * w * dt);
  const b = (s.v + z * w * x) / wd;
  s.x = target + e * (x * c + b * sn);
  s.v = e * ((-z * w * x + b * wd) * c + (-z * w * b - x * wd) * sn);
  return s;
}

/** A deliberate anticipation, lift, held look, and unhurried recovery. */
export function gestureAt(t) {
  if (t < 0 || t > 3.2) return { anticipate: 0, hold: 0, phase: 'rest' };
  const anticipate = t < .3 ? Math.sin(Math.PI * t / .3) : 0;
  const hold = t < .3 ? 0 : t < .85 ? smooth((t - .3) / .55) : t < 1.65 ? 1 : 1 - smooth((t - 1.65) / 1.55);
  return { anticipate, hold, phase: t < .3 ? 'anticipate' : t < .85 ? 'greet' : t < 1.65 ? 'hold' : 'recover' };
}

export function createLifeState(seed = 0) {
  const salt = typeof seed === 'number' ? seed : [...String(seed)].reduce((a, c) => (a * 31 + c.charCodeAt(0)) >>> 0, 0);
  const channels = Object.fromEntries(['antPitch', 'antYaw', 'tipPitch', 'tipYaw', 'bodyPitch', 'bodyYaw', 'bodyRoll', 'shoulder', 'foot'].map(k => [k, { x: 0, v: 0 }]));
  return { time: 0, age: 99, selected: false, seed: Math.abs(salt % 997) / 997, channels, phase: 'rest', authority: 1 };
}

export function resetLife(s) {
  for (const q of Object.values(s.channels)) { q.x = 0; q.v = 0; }
  s.age = 99; s.selected = false; s.phase = 'rest'; s.authority = 0;
}

/** No translation, scale or procedural face changes. Foot and shoulder output is planar yaw only. */
export function stepLife(s, dt, signals = {}) {
  dt = clamp(dt, 0, .25);
  if (signals.motionOff) { resetLife(s); return lifeOffsets(s); }
  const near = signals.near !== false, menu = !!signals.menu, selected = !!signals.selected;
  if (menu && selected && !s.selected) s.age = 0;
  s.selected = menu && selected; s.time += dt; s.age += dt;
  const clip = String(signals.clip || 'idle_bob');
  const free = /^(idle_bob|idle|rest)$/.test(clip);
  const scale = (signals.gentle ? .42 : 1) * (near ? 1 : 0) * (1 - clamp(signals.transition));
  const authority = free ? 1 : 0; s.authority = authority;
  const g = gestureAt(s.age), cycle = (s.time + s.seed * 4) % 16;
  // Long rests separate the held attention shifts. This is not a continuous wave loop.
  const look = cycle < 6 ? 0 : cycle < 7 ? smooth(cycle - 6) : cycle < 8.6 ? 1 : cycle < 10.4 ? 1 - smooth((cycle - 8.6) / 1.8) : 0;
  const direction = s.seed < .5 ? -1 : 1;
  const drive = clamp(signals.speed), steer = clamp(signals.steer, -1, 1), boost = clamp(signals.boost);
  const air = clamp(signals.air), land = clamp(signals.land), hit = clamp(signals.hit);
  const active = menu && selected ? 1 : 0;
  const breath = active * Math.sin(s.time * 1.65 + s.seed * Math.PI * 2) * .003;
  const targets = {
    bodyYaw: active ? direction * (.045 * look + .018 * g.hold) : -.006 * steer * drive,
    bodyPitch: active ? -.035 * g.hold + .016 * g.anticipate + breath : -.006 * boost + .004 * air,
    bodyRoll: active ? direction * .009 * g.hold : -.004 * steer * drive,
    antPitch: active ? .11 * g.hold - .04 * g.anticipate + breath * 4 : -.07 * boost + .065 * land - .035 * air + .04 * hit,
    antYaw: active ? direction * .10 * look : -.10 * steer * drive,
    shoulder: active ? .010 * g.hold : -.003 * boost,
    // Planted feet swivel around the cup's up axis; they never lift off their floor.
    foot: active ? .045 * g.hold * Math.sin(Math.PI * Math.min(1, Math.max(0, (s.age - .3) / 2.9))) : .012 * air,
  };
  for (const key of ['bodyPitch', 'bodyYaw', 'bodyRoll', 'shoulder', 'foot']) {
    spring(s.channels[key], targets[key] * scale * authority, dt, key === 'foot' ? 7 : 6, .86);
    // Ownership changes release body/limbs immediately; authored takeoffs cannot inherit our last look.
    if (!free) { s.channels[key].x = 0; s.channels[key].v = 0; }
  }
  const antennaWeight = scale * (free ? 1 : .08);
  spring(s.channels.antPitch, targets.antPitch * antennaWeight, dt, 9, .62);
  spring(s.channels.antYaw, targets.antYaw * antennaWeight, dt, 8, .66);
  spring(s.channels.tipPitch, s.channels.antPitch.x, dt, 7, .60);
  spring(s.channels.tipYaw, s.channels.antYaw.x, dt, 7, .60);
  s.phase = active && free ? g.phase === 'rest' && look > 0 ? 'attend' : g.phase : 'rest';
  return lifeOffsets(s);
}

export function lifeOffsets(s) {
  const o = {};
  for (const [key, value] of Object.entries(s.channels)) {
    const cap = key.startsWith('ant') || key.startsWith('tip') ? LIFE_LIMITS.antenna : key === 'foot' ? LIFE_LIMITS.foot : key === 'shoulder' ? LIFE_LIMITS.shoulder : LIFE_LIMITS.body;
    o[key] = clamp(value.x, -cap, cap);
  }
  return o;
}
