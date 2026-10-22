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
  assert.deepEqual(rec.first('drift-start').payload, { dir: -1, via: 'hop' });
  assert.equal(rec.first('land').payload.tier, 0, 'a plain hop lands soft: no landing thud on every drift (polish 2)');
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

test('a charged drift that straightens out by itself pays like a release (fixer: every completed drift pays)', () => {
  const feel = makeFeel({ 'drift.yawMul': 0.05 }), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 });
  st[S.driftDir] = 1; st[S.driftCharge] = feel.get('drift.miniMinS') + 0.05;
  run(st, tr, feel, acts({ throttle: 1, hop: true }), 1, rec);
  assert.ok(rec.first('drift-boost'), 'paid');
  assert.ok(!rec.first('drift-cancel'), 'not cancelled');
});

// polish 4: the power slide (brake while steering, throttle held), the mini-turbo and drift-boost
import { skidOf, isPowerSlide } from '../sim/drift.js';

test('power slide: throttle held, a brake press while steering at speed starts a held slide that keeps its speed', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 });
  run(st, tr, feel, acts({ throttle: 1, brake: 1, steer: 1 }), 1, rec);
  assert.deepEqual(rec.first('drift-start').payload, { dir: 1, via: 'brake' });
  assert.ok(rec.first('drift-start').t <= 2 * DT, 'starts on the press');
  assert.equal(st[S.driftDir], 1); assert.ok(isPowerSlide(st)); assert.equal(skidOf(st), 1, 'skid flag');
  assert.ok(!rec.first('drift-cancel'));
  assert.ok(st[S.engineV] > 21, 'the brake does not brake in a power slide: ' + st[S.engineV]);
  // brake with no throttle is a real brake: no slide, the kart slows hard
  const b = place(tr, { speed: 25 }), rb = recorder();
  run(b, tr, feel, acts({ brake: 1, steer: 1 }), 0.5, rb);
  assert.ok(!rb.first('drift-start')); assert.ok(b[S.engineV] < 16, 'braked ' + b[S.engineV]);
  // a brake held first and steered later is a brake too
  const c = place(tr, { speed: 25 }), rc = recorder(), a = acts({ throttle: 1, brake: 1 });
  run(c, tr, feel, a, 0.3, rc); a.steer = 1; run(c, tr, feel, a, 0.3, rc);
  assert.ok(!rc.first('drift-start'), 'a stale brake never slides');
  assert.equal(skidOf(c), 0);
});

test('power slide: tier 1 inside half a second, letting off the brake pays the tier with drift-boost', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 }), a = acts({ throttle: 1, brake: 1, steer: 1 });
  run(st, tr, feel, a, 0.7, rec);
  const t1 = rec.first('drift-tier');
  assert.ok(t1 && t1.t <= 0.5, 'tier 1 at ' + (t1 && t1.t));
  a.brake = 0; run(st, tr, feel, a, DT, rec);
  assert.deepEqual(rec.first('drift-release').payload, { tier: 1 });
  assert.deepEqual(rec.first('drift-boost').payload, { tier: 1, dir: 1 });
  assert.equal(st[S.boostKind], BOOST.DRIFT1); assert.equal(st[S.driftDir], 0); assert.equal(skidOf(st), 0);
});

test('power slide: straightening the stick releases and pays', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 }), a = acts({ throttle: 1, brake: 1, steer: 1 });
  run(st, tr, feel, a, 0.6, rec);
  a.steer = 0; run(st, tr, feel, a, 0.4, rec);
  const r = rec.first('drift-release');
  assert.ok(r, 'released');
  near(r.t - 0.6, feel.get('drift.straightenS'), 2 * DT, 'after straightenS');
  assert.ok(rec.first('drift-boost'), 'paid');
});

test('every completed drift of 0.4 s pays at least the mini-turbo; tier 1 by 0.5 s with the stick neutral', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  assert.ok(feel.get('drift.tierS')[0] <= 0.5);
  const st = place(tr, { speed: 25 });
  st[S.driftDir] = 1;
  run(st, tr, feel, acts({ throttle: 1, hop: true }), 0.45, rec);
  assert.equal(st[S.driftStage], 0, 'no tier yet');
  run(st, tr, feel, acts({ throttle: 1 }), DT, rec);
  assert.deepEqual(rec.first('drift-boost').payload, { tier: 0, dir: 1 });
  const bs = rec.first('boost-start').payload;
  assert.equal(bs.kind, 'drift'); assert.equal(bs.tier, 0);
  near(bs.amount, feel.get('boost.miniAmount'), 1e-9); near(bs.holdS, feel.get('boost.miniHoldS'), 1e-9);
  assert.ok(bs.amount < feel.get('boost.tierAmount')[0], 'smaller than tier 1');
});
