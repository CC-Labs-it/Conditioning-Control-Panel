import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, BOOST } from '../sim/state.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);

function tierTimes(steer) {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 });
  st[S.driftDir] = 1;                     // already drifting right, on the ground
  run(st, tr, feel, acts({ throttle: 1, hop: true, steer }), 4, rec);
  return { feel, tr, rec, st, times: rec.all('drift-tier').map((e) => e.t) };
}

test('drift tiers: hold time with the soft-drift modifier', () => {
  const tiers = makeFeel().get('drift.tierS');
  for (const [steer, key] of [[1, 'drift.softInMul'], [0, null], [-1, 'drift.softOutMul']]) {
    const { feel, rec, times } = tierTimes(steer);
    const mul = key ? feel.get(key) : 1;
    assert.ok(!rec.first('drift-cancel'), 'no cancel with steer ' + steer);
    const want = tiers.map((t) => t / mul).filter((t) => t < 4);
    assert.equal(times.length, want.length, 'tiers with steer ' + steer);
    times.forEach((t, i) => near(t, want[i], 1.5 * DT, `tier ${i + 1} steer ${steer}`));
    assert.deepEqual(rec.all('drift-tier').map((e) => e.payload), want.map((_, i) => ({ tier: i + 1, dir: 1 })));
  }
});

test('drift release pays the tier; release with no tier pays nothing', () => {
  const { feel, tr: t1, rec, st } = tierTimes(1);
  const a = acts({ throttle: 1, hop: false });
  run(st, t1, feel, a, DT, rec);
  assert.deepEqual(rec.first('drift-release').payload, { tier: 3 });
  assert.equal(st[S.boostKind], BOOST.DRIFT3);
  const bs = rec.first('boost-start').payload;
  assert.equal(bs.kind, 'drift'); assert.equal(bs.tier, 3); near(bs.amount, feel.get('boost.tierAmount')[2], 1e-9);

  const tr = fakeTrack({ w: 400 }), r2 = recorder(), s2 = place(tr, { speed: 25 });
  s2[S.driftDir] = -1;
  run(s2, tr, feel, acts({ throttle: 1, hop: true, steer: -1 }), 0.2, r2);
  run(s2, tr, feel, acts({ throttle: 1 }), DT, r2);
  assert.deepEqual(r2.first('drift-release').payload, { tier: 0 });
  assert.ok(!r2.first('boost-start'));
});

test('hop starts a drift into the turn above min speed, curves into it, cancels when slow', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 20 });
  const a = acts({ throttle: 1, hop: true, hopPressed: true, steer: -0.5 });
  run(st, tr, feel, a, 1, rec);
  assert.deepEqual(rec.first('hop').payload.speed > 19, true);
  assert.deepEqual(rec.first('drift-start').payload, { dir: -1 });
  assert.equal(rec.first('land').payload.tier, 1, 'a hop lands light');
  assert.ok(st[S.yawRate] < 0, 'curving left');
  assert.equal(st[S.driftDir], -1, 'drift kept through the landing');
  // under min speed: no drift from a hop, and a running drift cancels as slow
  const slow = place(tr, { speed: 5 }), r2 = recorder();
  run(slow, tr, feel, acts({ throttle: 0, hop: true, hopPressed: true, steer: 1 }), 0.6, r2);
  assert.ok(!r2.first('drift-start'));
  const s3 = place(tr, { speed: 10 }), r3 = recorder();
  s3[S.driftDir] = 1;
  run(s3, tr, feel, acts({ brake: 1, hop: true, steer: 1 }), 1, r3);
  assert.equal(r3.first('drift-cancel').payload.reason, 'slow');
});

test('drift cancels on low slip', () => {
  const feel = makeFeel({ 'drift.yawMul': 0.05 }), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 });
  st[S.driftDir] = 1;
  run(st, tr, feel, acts({ throttle: 1, hop: true }), 1, rec);
  const c = rec.first('drift-cancel');
  assert.equal(c.payload.reason, 'slip');
  near(c.t, feel.get('drift.cancelSlipS'), 1.5 * DT);
});
