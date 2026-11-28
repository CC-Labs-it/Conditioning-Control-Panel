import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, FORKS, ITEMS } from '../data/items.js';
import { repeatKind, offerBadge, standingBadges } from '../ui/repeat-badge.js';
import { createJourney } from '../sim/journey.js';
import * as E from '../sim/economy.js';

const run = (owned = {}, ranks = {}) => ({ owned, store: { journey: { ranks } } });

test('a second Bouncing Button reads as one more of the same thing, the first has no sticker', () => {
  const stray = BY_ID.stray;
  assert.equal(repeatKind(stray), 'plus');
  assert.equal(offerBadge(stray, run()), null);
  const b = offerBadge(stray, run({ stray: 2 }));
  assert.deepEqual([b.kind, b.n, b.text], ['plus', 3, '+']);
  assert.match(b.label, /One more Bouncing Button\. You have 2\./);
});

test('a Journey rank row reads as the level it buys, matching the Rank number in its name', () => {
  const row = { id: 'rank_stray_1', name: 'Bouncing Button · Rank 2', iconId: 'stray', rank: 1 };
  const b = offerBadge(row, run({ stray: 3 }));
  assert.deepEqual([b.kind, b.n, b.text], ['level', 2, '2']);
  assert.equal(b.label, 'Level 2 of Bouncing Button');
  assert.equal(offerBadge({ ...row, rank: 4 }, run()).text, '5');
});

test('a specialty reads as a pick for its parent, plain catalog rows get nothing', () => {
  const spec = { id: 'spec_rebounds', iconId: 'stray', options: [['a', 'A', 'a'], ['b', 'B', 'b']] };
  assert.equal(offerBadge(spec, run()).kind, 'pick');
  const loose = ITEMS.filter((it) => it.id !== 'stray').map((it) => offerBadge(it, run({ [it.id]: 3 }))).filter(Boolean);
  assert.deepEqual(loose, []);
});

test('the map shows the level and the copies the run already has', () => {
  assert.deepEqual(standingBadges(BY_ID.stray, run({ stray: 1 })), []);
  const both = standingBadges(BY_ID.stray, run({ stray: 4 }, { stray: 2 }));
  assert.deepEqual(both.map((b) => [b.kind, b.text]), [['level', '3'], ['plus', 'x4']]);
  assert.deepEqual(standingBadges(BY_ID.levels, run({ levels: 1 }, { levels: 1 })).map((b) => b.text), ['2']);
  assert.deepEqual(standingBadges(BY_ID.levels, { owned: { levels: 1 }, store: {} }), []);
});

test('every Journey offer that shares its parent icon gets a sticker, and the numbers never repeat side by side', () => {
  const s = E.createRun();
  s.store ??= {};
  const j = createJourney(s);
  s.fuzz = 1e15; s.total = 1e15;
  const seen = [];
  for (let i = 0; i < 160 && !s.ended; i++) {
    const win = j.shop(6);
    const offers = [win.goal, ...win.rows].filter(Boolean);
    const same = new Map();
    for (const it of offers) {
      const icon = it.iconId || it.id;
      const b = offerBadge(it, s);
      if (it.iconId) assert.ok(b, `${it.id} shares ${icon} with no sticker`);
      const key = icon + ':' + (b ? b.kind + b.text : '');
      assert.ok(!same.has(key), `two tiles read the same: ${key}`);
      same.set(key, it.id);
      if (b) seen.push(b.kind);
    }
    const next = win.goal;
    if (!next) break;
    if (next.kind === 'fork') { const lane = Object.keys(FORKS[next.id]?.opts || {})[0]; if (!lane || !j.choose(next.id, lane).ok) break; continue; }
    const r = j.buy(next.id, next.options?.[0]?.[0]);
    if (!r.ok) break;
  }
  assert.ok(seen.includes('level'), 'a run offers rank rows');
});
