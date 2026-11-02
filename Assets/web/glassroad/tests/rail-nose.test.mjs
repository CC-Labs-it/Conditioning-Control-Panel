// Playtest #6 (2026-10-28): nothing on the course turns a moving kart round or mirrors its wheel.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S } from '../sim/state.js';
import { step, railNose, RAIL_TRAVEL_MPS } from '../sim/kart.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);
const T = [0, 0, 1], B = [1, 0, 0];

test('railNose: a square nose-in hit with forward travel is laid up the road, never down it', () => {
  const F = [1, 0, 0];                                   // nose straight into the right rail
  railNose(F, [0.5, 0, 20], B, T);
  assert.deepEqual(F.map((v) => Math.round(v * 1e6) / 1e6), [0, 0, 1]);
  const G = [1, 0, 0];                                   // no travel to read: the road's forward
  railNose(G, [0, 0, 0], B, T);
  near(G[2], 1, 1e-9);
});

test('railNose: a nose spun past square while travelling up the road keeps facing up the road', () => {
  const F = [Math.sin(1.9), 0, Math.cos(1.9)];          // 109 deg off the road, into the right rail
  railNose(F, [0, 0, 25], B, T);
  near(F[2], 1, 1e-9, 'forward');
  near(F[0], 0, 1e-9, 'along the rail');
});

test('railNose: reverse, slow travel and a nose already up the road keep the plain projection', () => {
  const spun = () => [Math.sin(1.9), 0, Math.cos(1.9)];
  const a = spun(); railNose(a, [0, 0, 25], B, T, false);   // reversing: the nose is not turned round
  near(a[2], -1, 1e-9);
  const b = spun(); railNose(b, [0, 0, RAIL_TRAVEL_MPS * 0.5], B, T);
  near(b[2], -1, 1e-9);
  const c = [Math.sin(0.4), 0, Math.cos(0.4)]; railNose(c, [0, 0, 25], B, T);
  near(c[2], 1, 1e-9);
  const d = spun(); railNose(d, [0, 0, -25], B, T);         // genuinely travelling down the road: unchanged
  near(d[2], -1, 1e-9);
});

test('a kart driven square into a rail goes on up the road instead of bouncing rail to rail', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder();
  // nose and travel square into the right rail, 0.9 m from it: no travel along the road to read
  const st = place(tr, { s: 200, lateral: 5, headingDeg: 90, speed: 30, engine: 30 });
  const s0 = st[S.s];
  run(st, tr, feel, acts({ throttle: 1 }), 2, rec);
  assert.ok(st[S.s] - s0 > 20, 'went on up the road: ' + (st[S.s] - s0));
  assert.ok(rec.all('wall').length <= 3, 'no rail to rail bouncing: ' + rec.all('wall').length);
});

test('the wheel mirrors only in a deliberate reverse, never on a backwards knock with the throttle held', () => {
  const feel = makeFeel(), tr = fakeTrack();
  const knocked = place(tr, { s: 200, headingDeg: 0, speed: -6, engine: 10 });
  step(knocked, acts({ throttle: 1, steer: 1 }), tr, feel, DT, recorder());
  assert.ok(knocked[S.yawRate] > 0, 'throttle held, rolling back: stick right turns the nose right ' + knocked[S.yawRate]);
  const reversing = place(tr, { s: 200, headingDeg: 0, speed: -6, engine: -6 });
  step(reversing, acts({ brake: 1, steer: 1 }), tr, feel, DT, recorder());
  assert.ok(reversing[S.yawRate] < 0, 'in reverse the yaw mirrors as before ' + reversing[S.yawRate]);
});
