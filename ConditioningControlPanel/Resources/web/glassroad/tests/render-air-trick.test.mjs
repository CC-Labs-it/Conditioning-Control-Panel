import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { AIR_TRICK as T, createAirTrick, stepAirTrick, longFlight } from '../render/airTrickMath.js';

const DT = 1 / 60;
/** A flight: takeoff at upMps under hop gravity, airborne for airS, then onGround seconds on the road. */
function fly(s, { upMps = 14, airS = 1, groundS = 2, gentle = false, blockedAt = -1, hop = false } = {}) {
  const frames = [];
  let vy = upMps;
  for (let t = 0; t < airS + groundS; t += DT) {
    const air = t < airS;
    const o = stepAirTrick(s, DT, { air, airTime: air ? t : 0, velY: air ? vy : 0, hopT: hop ? t : 9, gentle, blocked: blockedAt >= 0 && t >= blockedAt });
    if (air) vy -= T.gravityMps2 * DT;
    frames.push({ t, air, ...o });
  }
  return frames;
}

test('air trick: a plain hop never qualifies, a ramp or long fall does', () => {
  assert.equal(longFlight(0.15, 6.8 - 26 * 0.15), false, 'a hop is short');
  assert.equal(longFlight(0.15, 11 - 28 * 0.15), true, 'an 11 m/s ramp launch (0.79 s) is long');
  assert.equal(longFlight(0.05, 14), false, 'never before the lag');
  assert.equal(longFlight(T.lateStartS, -3), true, 'falling off a ledge long enough');
  assert.equal(longFlight(T.lateStartS, -3, T, true), false, 'a hop on a slope needs more');
  for (const airS of [0.52, 0.7, 0.85]) {
    const hop = fly(createAirTrick(1), { upMps: 6.8, airS, hop: true });
    assert.ok(hop.every((f) => !f.active), 'no trick on a hop of ' + airS + ' s');
  }
  assert.ok(fly(createAirTrick(1), { upMps: 6.8, airS: 1.6, hop: true }).some((f) => f.active), 'a hop off a big drop still does');
  const rampHop = fly(createAirTrick(1), { upMps: 14, airS: 1, hop: true }).find((f) => f.active);
  assert.ok(rampHop && rampHop.t < 0.2, 'a hop on a ramp lip tricks on time');
});

test('air trick: EMI leaves a beat late, spins one whole turn through a single arc and lands back in', () => {
  const f = fly(createAirTrick(3), { upMps: 14, airS: 1.0 });
  const first = f.find((x) => x.active);
  assert.ok(first && first.t >= T.lagS - 1e-9, 'starts after the kart left');
  const peak = Math.max(...f.map((x) => x.lift));
  assert.ok(peak > T.liftM * 0.95, 'clearly out of the cup');
  const spins = f.filter((x) => x.active).map((x) => Math.abs(x.spin));
  assert.ok(Math.max(...spins) > Math.PI * 1.9, 'a full 360');
  assert.ok(f.some((x) => x.armK > 0.99), 'arm fully up during the turn');
  const faces = new Set(f.filter((x) => x.face).map((x) => x.face));
  assert.equal(faces.size, 1); assert.ok(T.faces.includes([...faces][0]));
  assert.ok(f.every(x => x.phase !== 'hang'), 'no held mid-air phase');
  const arc = f.filter(x => x.phase === 'launch' || x.phase === 'return');
  assert.ok(Math.abs(arc.length * DT - T.arcS) <= DT * 2, 'one timed arc');
  const settle = f.filter((x) => x.phase === 'settle');
  assert.ok(settle.length > 10 && settle.some((x) => Math.abs(x.wobRoll) > 0.05), 'a wobble on the way back in');
  const last = f[f.length - 1];
  assert.equal(last.active, false); assert.equal(last.lift, 0); assert.equal(last.spin, 0); assert.equal(last.armK, 0);
});

test('air trick: early landings and long flights never add a mid-air wait or a second trick', () => {
  const short = fly(createAirTrick(5), { upMps: 14, airS: 0.6 });
  assert.ok(short.every(x => x.phase !== 'hang'));
  assert.ok(short.some(x => x.phase === 'return' && !x.air), 'she keeps falling after the kart touches down');
  const long = fly(createAirTrick(5), { upMps: 14, airS: 3, groundS: 1 });
  const returnsAt = long.find((x) => x.phase === 'return').t;
  assert.ok(returnsAt < 3, 'back in the cup before the kart lands');
  assert.equal(long.filter((x) => x.air && x.phase === 'launch' && x.t > returnsAt).length, 0, 'one trick per flight');
});

test('air trick: the fall never rises again and flows into a squash and recovery', () => {
  for (const seed of [1, 3, 6]) {
    const f = fly(createAirTrick(seed), { upMps: 14, airS: 1.0 });
    let worst = 0;
    for (let n = 1; n < f.length; n++) worst = Math.max(worst, f[n - 1].lift - f[n].lift);
    assert.ok(worst < T.liftM * 0.1, 'never drops more than a tenth of the lift in one frame (was the whole lift): ' + worst);
    const ret = f.filter((x) => x.phase === 'return');
    for (let i = 1; i < ret.length; i++) assert.ok(ret[i].lift < ret[i - 1].lift, 'strictly falling after the apex');
    assert.ok(Math.min(...ret.map((x) => x.squash)) < -0.08, 'stretched by the fall');
    const settle = f.filter((x) => x.phase === 'settle');
    const low = settle.reduce((a, x) => (x.lift < a.lift ? x : a));
    assert.ok(low.squash > 0.12, 'squashed into the cup at the bottom of the dip');
    assert.ok(Math.abs(settle[settle.length - 1].squash) < 0.01, 'and springs back to rest');
  }
});

test('air trick: faces vary between jumps, Gentle is half the height, blocked or motion off ends it at once', () => {
  const s = createAirTrick(7), seen = [];
  for (let n = 0; n < 6; n++) seen.push(fly(s, { airS: 1 }).find((x) => x.face).face);
  for (let n = 1; n < seen.length; n++) assert.notEqual(seen[n], seen[n - 1], 'never the same face twice in a row');
  const gentle = fly(createAirTrick(7), { airS: 1, gentle: true });
  assert.ok(Math.max(...gentle.map((x) => x.lift)) < T.liftM * T.gentle.lift * 1.2);
  const cut = fly(createAirTrick(7), { airS: 1.5, blockedAt: 0.5 });
  assert.ok(cut.filter((x) => x.t >= 0.5).every((x) => !x.active && x.lift === 0), 'a rescue ends it');
  const s2 = createAirTrick(2);
  for (let t = 0; t < 1; t += DT) assert.equal(stepAirTrick(s2, DT, { air: true, airTime: t, velY: 14, motionOff: true }).active, false);
});

test('air trick: karts.js restores the rider before every pose and passes the trick face', () => {
  const src = readFileSync(new URL('../render/karts.js', import.meta.url), 'utf8');
  const top = src.indexOf('k.trickPieces?.beforePose();'), phrase = src.indexOf('k.phrasePieces?.beforePose();'), life = src.indexOf('k.life?.beforePose();');
  assert.ok(top > 0 && top < phrase && phrase < life, 'restored in reverse order of application');
  assert.match(src, /face \|\| trick\.face \|\| o\.face/);
  assert.ok(src.indexOf('k.trickPieces?.update(trick, near)') > src.indexOf('k.phrasePieces?.update('), 'applied last');
});

test('air trick: descent meets the landing compression at the same position and velocity', () => {
  const epsilon = .00001;
  const sample = (phase, t) => {
    const s = createAirTrick(2); Object.assign(s, { phase, t, age: phase === 'return' ? t : T.arcS + t, used: true });
    return { ...stepAirTrick(s, 0, { air: false }) };
  };
  const left = sample('return', T.arcS - epsilon), join = sample('settle', 0), right = sample('settle', epsilon);
  assert.ok(Math.abs(join.lift) < 1e-12);
  const inV = (join.lift - left.lift) / epsilon, outV = (right.lift - join.lift) / epsilon;
  assert.ok(Math.abs(inV - outV) < .002, 'no landing velocity kick: ' + inV + ' / ' + outV);
  assert.ok(Math.abs(right.wobRoll) < 1e-8, 'settle rotation eases in from zero velocity');
  const finish = sample('settle', T.settleS - epsilon);
  for (const key of ['lift', 'wobRoll', 'wobPitch', 'squash']) assert.ok(Math.abs(finish[key]) < 1e-8, key + ' returns smoothly to rest');
});

test('air trick: phase changes retain fractional frame time at low frame rates', () => {
  const s = createAirTrick(1);
  Object.assign(s, { phase: 'launch', t: T.arcS / 2 - .01, age: T.arcS / 2 - .01, used: true });
  stepAirTrick(s, .05, { air: true, airTime: .8, velY: 0 });
  assert.equal(s.phase, 'return'); assert.ok(Math.abs(s.age - (T.arcS / 2 + .04)) < 1e-12);
  Object.assign(s, { phase: 'return', t: T.arcS - .02, age: T.arcS - .02 });
  stepAirTrick(s, .05, { air: false });
  assert.equal(s.phase, 'settle'); assert.ok(Math.abs(s.t - .03) < 1e-12);
});

test('air trick: the apex has no plateau at 20, 30, 60 or 120 fps, including Gentle', () => {
  for (const hz of [20, 30, 60, 120]) for (const gentle of [false, true]) {
    const s = createAirTrick(3), arc = [];
    for (let i = 0; i < hz * 3; i++) {
      const t = i / hz, o = stepAirTrick(s, 1 / hz, { air: t < 1, airTime: t < 1 ? t : 1, velY: 14 - 28 * t, hopT: 99, gentle });
      if (o.phase === 'launch' || o.phase === 'return') arc.push({ ...o });
      assert.notEqual(o.phase, 'hang');
    }
    const top = Math.max(...arc.map(x => x.lift)), atTop = arc.findIndex(x => x.lift === top);
    for (let i = atTop + 1; i < arc.length; i++) assert.ok(arc[i].lift < arc[i - 1].lift - 1e-7, 'no pause or second rise at ' + hz + ' fps');
    assert.ok(arc.filter(x => x.lift >= top * .999).length / hz <= .075, 'only the natural instant around the apex');
  }
});

test('air trick: one full turn has a bounded angular speed and acceleration', () => {
  const frames = fly(createAirTrick(3)); let previous = 0, velocity = 0, peak = 0, acceleration = 0;
  for (const frame of frames.filter(f => f.active)) {
    const angle = frame.spin === 0 && previous > Math.PI ? Math.PI * 2 : Math.abs(frame.spin);
    const nextV = (angle - previous) / DT;
    peak = Math.max(peak, nextV); acceleration = Math.max(acceleration, Math.abs(nextV - velocity) / DT);
    previous = angle; velocity = nextV;
  }
  assert.ok(peak < 16, 'the turn never snaps through its middle: ' + peak);
  assert.ok(acceleration < 70, 'angular acceleration stays continuous and bounded: ' + acceleration);
  assert.ok(previous > Math.PI * 1.99, 'the accepted full turn remains');
});
