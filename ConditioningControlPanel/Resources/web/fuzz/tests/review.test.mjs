// Defects the review lane found: each test holds one fix.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cardUp } from '../ui/unlocks/extras/kit.js';
import { bankOf, collect, levelSeconds, xpNeed } from '../sim/levels.js';

test('a closed Guide or Control Panel sheet is not a card up', () => {
  const was = globalThis.document;
  const host = { children: [{ hidden: true }] };
  globalThis.document = { getElementById: () => host };
  try {
    assert.equal(cardUp(), false, 'only a hidden sheet');
    host.children = [{ hidden: true }, { hidden: false }];
    assert.equal(cardUp(), true, 'a real card over it');
    host.children = [];
    assert.equal(cardUp(), false, 'an empty host');
  } finally { globalThis.document = was; }
});

test('Levels: a huge bank answers at once and the sums match the slow way', () => {
  for (const xp of [1e308, 1e18, Number.MAX_SAFE_INTEGER]) {
    const b = bankOf(1, xp);
    assert.ok(Number.isFinite(b.banked) && b.left >= 0 && b.left < b.need, `bank of ${xp}`);
    assert.ok(Number.isFinite(collect(1, xp).seconds));
  }
  for (const [lvl, xp] of [[1, 0], [1, 34.9], [1, 35], [1, 500], [3, 2000], [12, 181], [40, 12345.5]]) {
    let banked = 0, left = xp, seconds = 0;
    while (left >= xpNeed(lvl + banked)) { left -= xpNeed(lvl + banked); seconds += levelSeconds(lvl + banked); banked += 1; }
    const now = bankOf(lvl, xp), got = collect(lvl, xp);
    assert.equal(now.banked, banked, `banked at ${lvl}, ${xp}`);
    assert.ok(Math.abs(now.left - left) < 1e-6, `left at ${lvl}, ${xp}`);
    assert.equal(got.seconds, seconds, `seconds at ${lvl}, ${xp}`);
    assert.equal(got.lvl, lvl + banked);
  }
});
