import test from 'node:test';
import assert from 'node:assert/strict';
import { slotState, slotStep, beginSpin, finishSpin, COIN_MS, SPIN_MS } from '../sim/arcade.js';
import { restoreSlot, slotStepRanked, beginSpinHeld, toggleHold, slotRank, coinCap, coinMs, BIG_COIN_MS } from '../ui/unlocks/arcade/slot-upgrades.js';

const lcg = (seed) => () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646;

test('no rank plays exactly the old machine', () => {
  assert.equal(slotRank({ store: {} }), 0);
  assert.equal(slotRank({ store: { journey: { ranks: { mini_slots: 3 } } } }), 3);
  const a = slotState(), b = restoreSlot(undefined), ra = lcg(5), rb = lcg(5);
  for (let i = 0; i < 40; i++) {
    slotStep(a, 7000); slotStepRanked(b, 7000, 0);
    assert.equal(beginSpin(a, ra), beginSpinHeld(b, rb, 0));
    slotStep(a, SPIN_MS); slotStepRanked(b, SPIN_MS, 0);
    assert.deepEqual(finishSpin(a), finishSpin(b));
    assert.deepEqual(b.last, a.last); assert.equal(b.coins, a.coins);
  }
  b.hold = 1;
  assert.equal(toggleHold(b, 0, 0), false, 'Hold needs the rank');
});

test('Fourth Coin: the tray holds four and a coin comes every 35 s', () => {
  assert.equal(coinCap(1), 3); assert.equal(coinCap(2), 4);
  assert.equal(coinMs(1), COIN_MS); assert.equal(coinMs(2), BIG_COIN_MS);
  const s = restoreSlot();
  assert.equal(s.coins, 1);
  slotStepRanked(s, BIG_COIN_MS - 1, 2); assert.equal(s.coins, 1);
  slotStepRanked(s, 1, 2); assert.equal(s.coins, 2);
  slotStepRanked(s, BIG_COIN_MS * 10, 2); assert.equal(s.coins, 4); assert.equal(s.elapsed, 0);
  const saved = restoreSlot(JSON.parse(JSON.stringify(s)));
  assert.equal(saved.coins, 4, 'a reload keeps the fourth coin');
  slotStepRanked(saved, COIN_MS * 3, 0);
  assert.equal(saved.coins, 4, 'coins above the cap are never taken away');
  assert.equal(restoreSlot({ coins: 9 }).coins, 4);
});

test('Hold: the held reel keeps its symbol, a win lands on it, and two wins in six still hold', () => {
  const rng = lcg(17);
  let s = restoreSlot(), wins = 0, held = 0;
  for (let i = 0; i < 120; i++) {
    slotStepRanked(s, COIN_MS, 1);
    const reel = i % 3, shown = s.last[reel];
    assert.equal(toggleHold(s, reel, 1), true);
    assert.equal(s.hold, reel);
    assert.equal(beginSpinHeld(s, rng, 1), true);
    assert.equal(s.hold, null, 'one hold per coin');
    assert.equal(toggleHold(s, reel, 1), false, 'no hold while the reels turn');
    assert.equal(s.pending.reels[reel], shown, 'the held reel keeps its symbol');
    s = restoreSlot(JSON.parse(JSON.stringify(s)));
    slotStepRanked(s, SPIN_MS, 1);
    const result = finishSpin(s);
    assert.equal(result.win, new Set(s.last).size === 1, 'honest reels');
    if (result.win) { assert.deepEqual(s.last, [shown, shown, shown]); assert.equal(result.big, shown === 0); }
    wins += Number(result.win); held++;
    if (held % 6 === 0) { assert.equal(wins, 2 * held / 6); }
  }
  // Tapping the held reel again lets go, tapping another moves the hold.
  const t = restoreSlot();
  toggleHold(t, 2, 1); toggleHold(t, 2, 1); assert.equal(t.hold, null);
  toggleHold(t, 0, 1); toggleHold(t, 1, 1); assert.equal(t.hold, 1);
  assert.equal(restoreSlot({ hold: 1 }).hold, 1);
  assert.equal(restoreSlot({ hold: 5 }).hold, null);
});
