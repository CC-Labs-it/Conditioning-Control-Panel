import test from 'node:test';
import assert from 'node:assert/strict';
import { createRun, earn, quoteSideLump, sideLump } from '../sim/economy.js';
import { SECTORS, SPIN_MS, restoreWheelSpin, rollWheel, wheelAngle, wheelBase, wheelRequest, wheelTravel } from '../sim/prize-wheel.js';

test('wedge weights, actual frequency and pointer agree across the entire wheel', () => {
  const hits = Object.fromEntries(SECTORS.map(s => [s.id, 0]));
  for (let i = 0; i < 10000; i++) {
    const spin = rollWheel((i + 0.5) / 10000, (i * 17) % 360);
    const landed = SECTORS.find(s => wheelAngle(-spin.end) < s.end);
    assert.equal(landed.id, spin.id);
    assert.ok(spin.end >= spin.from + 1440);
    hits[spin.id]++;
  }
  assert.deepEqual(hits, { small: 3000, large: 800, nothing: 2000, regular: 2100, jackpot: 400, double: 1700 });
  assert.equal(SECTORS.reduce((sum,s) => sum+s.weight*s.mult, 0), 150, 'same nominal mean as the original wheel');
  for (const s of SECTORS) assert.ok(Math.abs(s.end - s.start - s.weight * 3.6) < 1e-8);
});

test('a funded spin pays its displayed multiples exactly through the side budget even if funding changes while spinning', () => {
  for (const pace of [1, 3, 17]) for (const balance of [0, 100, 10000, 500000]) {
    const initial = createRun({pace}), rates = {fps: 50, click: 7};
    earn(initial, balance, 'base');
    const before = structuredClone(initial.side);
    const base = wheelBase(initial, rates);
    assert.deepEqual(initial.side, before, 'reading the face must not spend its budget');
    assert.ok(base >= 2 && base % 2 === 0);
    for (const sector of SECTORS) for (const sideSpend of [0, 80000]) {
      const run = structuredClone(initial);
      if (sideSpend) sideLump(run, sideSpend, 'quiz');
      const target = base * sector.mult;
      const request = wheelRequest(run, target) * pace;
      const quote = quoteSideLump(run, request, 'wheel');
      const got = sideLump(run, request, 'wheel');
      assert.equal(quote, got);
      assert.ok(Math.abs(got - target) < 1e-7, JSON.stringify({pace, balance, id:sector.id, target, got}));
    }
  }
});

test('a saved spin restores its original winner, base and phase; invalid data cannot issue a payout', () => {
  const spin = { ...rollWheel(.794, 141), elapsed: 1300, duration: SPIN_MS, base: 400 };
  assert.deepEqual(restoreWheelSpin(spin), spin);
  assert.equal(restoreWheelSpin(undefined), null);
  for (const bad of [{base:-2},{elapsed:'foo'},{duration:999999},{from:Infinity},{id:'invalid'},{id:'nothing'}])
    assert.equal(restoreWheelSpin({...spin,...bad}), null);
});

test('manual releases coast in either direction to the weighted result and restore mid-flight', () => {
  for (const speed of [-.03,-.006,.006,.03]) for (const sector of SECTORS) {
    const sample = (sector.start + sector.end)/720;
    const from = 352, plan = rollWheel(sample,from,speed);
    assert.equal(plan.id,sector.id);
    assert.equal(Math.sign(plan.end-from),Math.sign(speed));
    assert.ok(Math.abs(plan.end-from) >= 360 - 1e-7);
    assert.ok(plan.duration >= 3200 && plan.duration <= 5600);
    assert.equal(SECTORS.find(s => wheelAngle(-plan.end) < s.end).id,sector.id);
    const saved = {...plan, base:100, elapsed:600};
    assert.deepEqual(restoreWheelSpin(saved),saved);
    assert.equal(wheelTravel(saved,0).degrees,0 * speed);
    assert.equal(wheelTravel(saved,1).degrees,plan.end-from);
    assert.ok(Math.abs(wheelTravel(saved,0).velocity) > Math.abs(wheelTravel(saved,.25).velocity));
    assert.equal(Math.abs(wheelTravel(saved,1).velocity),0);
  }
  const slow = rollWheel(.5,25,.006), fast = rollWheel(.5,25,.03);
  assert.ok(fast.end - slow.end >= 720);
  assert.equal(restoreWheelSpin({...fast,motion:'fling',elapsed:10,base:100,end:fast.end+3600}),null);
});
