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

test('air trick: EMI leaves a beat late, spins one whole turn, holds an arm up with a face, and lands back in', () => {
  const f = fly(createAirTrick(3), { upMps: 14, airS: 1.0 });
  const first = f.find((x) => x.active);
  assert.ok(first && first.t >= T.lagS - 1e-9, 'starts after the kart left');
  const peak = Math.max(...f.map((x) => x.lift));
  assert.ok(peak > T.liftM * 0.95, 'clearly out of the cup');
  const spins = f.filter((x) => x.active).map((x) => Math.abs(x.spin));
  assert.ok(Math.max(...spins) > Math.PI * 1.9, 'a full 360');
  assert.ok(f.some((x) => x.phase === 'hang' && x.armK > 0.99), 'arm fully up in the hang');
  const faces = new Set(f.filter((x) => x.face).map((x) => x.face));
  assert.equal(faces.size, 1); assert.ok(T.faces.includes([...faces][0]));
  const hang = f.filter((x) => x.phase === 'hang');
  const airborne = hang.length * DT;
  assert.ok(airborne > 0.3 && airborne <= T.hangMaxS + DT, 'about a second up there, at most hangMaxS');
  const settle = f.filter((x) => x.phase === 'settle');
  assert.ok(settle.length > 10 && settle.some((x) => Math.abs(x.wobRoll) > 0.05), 'a wobble on the way back in');
  const last = f[f.length - 1];
  assert.equal(last.active, false); assert.equal(last.lift, 0); assert.equal(last.spin, 0); assert.equal(last.armK, 0);
});

test('air trick: an early landing cuts the hang short, a long glide sends EMI back while still flying', () => {
  const short = fly(createAirTrick(5), { upMps: 14, airS: 0.6 });
  const hangS = short.filter((x) => x.phase === 'hang').length * DT;
  assert.ok(hangS < T.hangMinS + T.landHoldS + 0.1, 'returns soon after the kart lands');
  const long = fly(createAirTrick(5), { upMps: 14, airS: 3, groundS: 1 });
  const returnsAt = long.find((x) => x.phase === 'return').t;
  assert.ok(returnsAt < 3, 'back in the cup before the kart lands');
  assert.equal(long.filter((x) => x.air && x.phase === 'launch' && x.t > returnsAt).length, 0, 'one trick per flight');
});

test('air trick: the landing follows through (owner: "abrupt") - EMI lands after the kart, rises, eases in, squashes', () => {
  for (const seed of [1, 3, 6]) {
    const f = fly(createAirTrick(seed), { upMps: 14, airS: 1.0 });
    const touch = f.find((x) => !x.air);
    assert.ok(f.find((x) => x.t >= touch.t + 0.1).lift > T.liftM * 0.8, 'still up a beat after the kart lands');
    let worst = 0;
    for (let n = 1; n < f.length; n++) worst = Math.max(worst, f[n - 1].lift - f[n].lift);
    assert.ok(worst < T.liftM * 0.1, 'never drops more than a tenth of the lift in one frame (was the whole lift): ' + worst);
    const ret = f.filter((x) => x.phase === 'return');
    assert.ok(Math.max(...ret.map((x) => x.lift)) > ret[0].lift + 0.05, 'a small rise before the drop');
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
    const s = createAirTrick(2); Object.assign(s, { phase, t, age: 2, used: true, from: 1, fromV: 0, landed: .4 });
    return { ...stepAirTrick(s, 0, { air: false }) };
  };
  const left = sample('return', T.returnS - epsilon), join = sample('settle', 0), right = sample('settle', epsilon);
  assert.ok(Math.abs(join.lift) < 1e-12);
  const inV = (join.lift - left.lift) / epsilon, outV = (right.lift - join.lift) / epsilon;
  assert.ok(Math.abs(inV - outV) < .002, 'no landing velocity kick: ' + inV + ' / ' + outV);
  assert.ok(Math.abs(right.wobRoll) < 1e-8, 'settle rotation eases in from zero velocity');
  const finish = sample('settle', T.settleS - epsilon);
  for (const key of ['lift', 'wobRoll', 'wobPitch', 'squash']) assert.ok(Math.abs(finish[key]) < 1e-8, key + ' returns smoothly to rest');
});

test('air trick: phase changes retain fractional frame time at low frame rates', () => {
  const s = createAirTrick(1);
  Object.assign(s, { phase: 'launch', t: T.launchS - .01, age: .4, used: true });
  stepAirTrick(s, .05, { air: true, airTime: .8, velY: 0 });
  assert.equal(s.phase, 'hang'); assert.ok(Math.abs(s.t - .04) < 1e-12);
  Object.assign(s, { phase: 'return', t: T.returnS - .02, from: 1, fromV: 0 });
  stepAirTrick(s, .05, { air: false });
  assert.equal(s.phase, 'settle'); assert.ok(Math.abs(s.t - .03) < 1e-12);
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
