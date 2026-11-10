import test from 'node:test';
import assert from 'node:assert/strict';
import { createRun, earn, quoteSideLump, sideLump } from '../sim/economy.js';
import { BIG_SECTORS, SECTORS, SPIN_MS, earnsRespin, restoreWheelSpin, rollWheel, sectorsFor, wheelAngle, wheelBase, wheelRank, wheelRequest, wheelTravel } from '../sim/prize-wheel.js';
import { skipNothing, wheelDraw, wheelMultAt } from '../ui/unlocks/prestige/boosts.js';

test('Big Wedge: the 10x wedge doubles, its odds with it, taken from the other prizes; Nothing stays 20%', () => {
  const by = (list) => Object.fromEntries(list.map((s) => [s.id, s]));
  const plain = by(SECTORS), big = by(BIG_SECTORS);
  assert.equal(big.jackpot.weight, 2 * plain.jackpot.weight);
  assert.equal(big.nothing.weight, 20);
  assert.equal(BIG_SECTORS.reduce((sum, s) => sum + s.weight, 0), 100);
  assert.deepEqual(BIG_SECTORS.map((s) => s.id), SECTORS.map((s) => s.id), 'same order, only the dividers move');
  for (const s of BIG_SECTORS) {
    assert.ok(Math.abs(s.end - s.start - s.weight * 3.6) < 1e-8, 'wedge area matches odds');
    if (!['jackpot', 'nothing'].includes(s.id)) assert.ok(s.weight <= plain[s.id].weight, `${s.id} gives width, never takes it`);
  }
  const hits = Object.fromEntries(BIG_SECTORS.map((s) => [s.id, 0]));
  for (let i = 0; i < 10000; i++) {
    const spin = rollWheel((i + 0.5) / 10000, (i * 17) % 360, undefined, BIG_SECTORS);
    assert.equal(spin.big, true);
    assert.equal(BIG_SECTORS.find((s) => wheelAngle(-spin.end) < s.end).id, spin.id);
    hits[spin.id]++;
  }
  assert.equal(hits.jackpot, 800); assert.equal(hits.nothing, 2000);
  // A big-face spin restores on the big face; the plain face cannot claim it.
  const saved = { ...rollWheel(.9, 40, undefined, BIG_SECTORS), elapsed: 100, duration: SPIN_MS, base: 10 };
  assert.deepEqual(restoreWheelSpin(saved), saved);
  assert.equal(rollWheel(.9, 40).big, undefined);
  // Prestige draws follow the face shown: No Nothing skips the big face's Nothing wedge too.
  const nothing = BIG_SECTORS.find((s) => s.id === 'nothing');
  for (let i = 0; i < 1000; i++) {
    const deg = skipNothing(i / 1000, BIG_SECTORS) * 360;
    assert.ok(!(deg >= nothing.start && deg < nothing.end));
  }
  assert.equal(wheelMultAt(wheelDraw({ has: (id) => id === 'luck_10' }, () => .5, new Map(), BIG_SECTORS), BIG_SECTORS), 10);
});

test('Respin: Nothing hands back one free spin at once, only with the rank, never twice in a row', () => {
  const ranked = (n) => ({ store: { journey: { ranks: { wheel: n } } } });
  assert.equal(wheelRank({ store: {} }), 0);
  assert.equal(wheelRank(ranked(1)), 1);
  assert.equal(sectorsFor(wheelRank(ranked(1)) >= 2), SECTORS);
  assert.equal(sectorsFor(wheelRank(ranked(2)) >= 2), BIG_SECTORS);
  const blank = { ...rollWheel(.45, 0), elapsed: 0, duration: SPIN_MS, base: 10 };
  assert.equal(blank.id, 'nothing');
  assert.equal(earnsRespin(blank, 0), false, 'off outside Journey');
  assert.equal(earnsRespin(blank, 1), true);
  assert.equal(earnsRespin({ ...blank, respin: true }, 4), false, 'a respin never earns another');
  assert.equal(earnsRespin({ ...rollWheel(.95, 0), base: 1 }, 1), false, 'a prize is a prize');
  assert.equal(earnsRespin(restoreWheelSpin({ ...blank, respin: true }), 1), false, 'a reload cannot turn a respin into a fresh one');
  const prize = { ...rollWheel(.1, 0), elapsed: 0, duration: SPIN_MS, base: 10, respin: true };
  assert.deepEqual(restoreWheelSpin(prize), prize, 'a respin in flight survives a reload as a respin');
});

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
