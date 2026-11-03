import test from 'node:test';
import assert from 'node:assert/strict';
import { slotState, slotStep, beginSpin, finishSpin, dispenserState, dispenserStep, dispense, catchPrize, COIN_MS, SPIN_MS, DROP_MS, CATCH_MS } from '../sim/arcade.js';
import { createRun, sideLump } from '../sim/economy.js';

test('a scarce slot win pays its advertised prize even when other side games drained their shared budget', () => {
  const s = createRun();
  sideLump(s, 100000, 'noise');
  const before = { ...s.side };
  assert.equal(sideLump(s, 5000, 'mini_slots'), 5000);
  assert.deepEqual(s.side, before);
  assert.equal(s.sources.mini_slots, 5000);
});

test('free coins use active elapsed time, cap at three and do not bank hidden overflow', () => {
  const s = slotState(); assert.equal(s.coins, 1);
  slotStep(s, COIN_MS - 1); assert.equal(s.coins, 1);
  slotStep(s, 1); assert.equal(s.coins, 2);
  slotStep(s, COIN_MS * 8); assert.equal(s.coins, 3); assert.equal(s.elapsed, 0);
  beginSpin(s, () => .4); slotStep(s, 1); assert.equal(s.coins, 2);
});

test('every six resolved spins contain exactly two wins with honest reel symbols', () => {
  let seed = 17;
  const rng = () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646;
  let s = slotState();
  for (let batch = 0; batch < 20; batch++) {
    let wins = 0;
    for (let i = 0; i < 6; i++) {
      slotStep(s, COIN_MS); assert.equal(beginSpin(s, rng), true);
      const expected = JSON.stringify(s.pending.reels);
      s = slotState(JSON.parse(JSON.stringify(s)));
      assert.equal(JSON.stringify(s.pending.reels), expected);
      assert.equal(beginSpin(s, rng), false);
      assert.equal(finishSpin(s), null);
      slotStep(s, SPIN_MS); const result = finishSpin(s);
      assert.equal(result.win, new Set(s.last).size === 1);
      wins += Number(result.win); assert.equal(finishSpin(s), null);
    }
    assert.equal(wins, 2);
  }
});

test('dispenser needs a press, catch starts at landing, and pays at most once', () => {
  const s = dispenserState(); dispenserStep(s, 600000); assert.equal(s.phase, 'ready');
  assert.equal(dispense(s), true); assert.equal(dispense(s), false); assert.equal(catchPrize(s), false);
  dispenserStep(s, DROP_MS); assert.equal(s.left, CATCH_MS);
  const restored = dispenserState(JSON.parse(JSON.stringify(s)));
  dispenserStep(restored, CATCH_MS - 1); assert.equal(catchPrize(restored), true);
  assert.equal(catchPrize(restored), false); assert.equal(restored.caught, 1);
});

test('miss dissolves, exact deadline refuses a catch, cooldown never auto-dispenses', () => {
  const s = dispenserState(); dispense(s, 15000);
  dispenserStep(s, DROP_MS + CATCH_MS); assert.equal(s.phase, 'dissolve'); assert.equal(catchPrize(s), false);
  assert.equal(s.missed, 1); dispenserStep(s, 15000); assert.equal(s.phase, 'ready');
  dispenserStep(s, 60000); assert.equal(s.phase, 'ready'); assert.equal(s.missed, 1);
});
