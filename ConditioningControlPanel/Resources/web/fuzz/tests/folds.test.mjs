import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, ITEMS } from '../data/items.js';
import { FOLD, LEAD_OF, buildFolds } from '../data/folds.js';
import * as E from '../sim/economy.js';
import { overLimit, defaultWindowWidth, railWidth } from '../ui/dock.js';

const rich = (s, n = 1e9) => { E.lump(s, n, 'test'); return s; };

test('bundles fold plain lane stretches and never touch forks, endings, skins or toys', () => {
  assert.ok(Object.keys(FOLD).length >= 40, 'a run has about half the purchases');
  for (const [lead, riders] of Object.entries(FOLD)) {
    const group = [lead, ...riders].map((id) => BY_ID[id]);
    assert.ok(group.every((it) => it.kind === 'item' && !it.grow && it.lane === group[0].lane), lead);
    riders.forEach((id, i) => assert.equal(BY_ID[id].req[0], i ? riders[i - 1] : lead, `${id} follows its bundle`));
  }
  for (const id of ['rounded', 'heartbtn', 'toy_box', 'mini_slots', 'f1', 'drop', 'stray']) assert.equal(!!(FOLD[id] || LEAD_OF[id]), false, id);
  assert.deepEqual(buildFolds(ITEMS, 1).FOLD, {});
});

test('a bundle costs its rows together, is one purchase, and owns every row it brings', () => {
  const s = rich(E.createRun());
  E.grant(s, 'e_bounce');
  const lead = 'e_words', riders = FOLD[lead];
  assert.ok(riders.length >= 2);
  const sum = [lead, ...riders].reduce((t, id) => t + BY_ID[id].cost, 0);
  const p = E.price(s, lead);
  assert.ok(Math.abs(p - Math.round(sum * 1.5)) <= riders.length + 1, `${p} for ${sum}`);
  const buys = s.buys;
  const res = E.buy(s, lead);
  assert.equal(res.ok, true);
  assert.deepEqual(res.riders, riders);
  assert.equal(s.buys, buys + 1);
  assert.ok(riders.every((id) => E.has(s, id)));
  assert.ok(riders.every((id) => !E.isOpen(s, BY_ID[id])));
});

test('an old save that owns a lead without its riders sells each rider on its own', () => {
  const s = rich(E.createRun());
  E.grant(s, 'e_words');
  const [first] = FOLD.e_words;
  assert.equal(E.has(s, first), false);
  assert.equal(E.isOpen(s, BY_ID[first]), true);
  assert.equal(E.buy(s, first).ok, true);
});

test('twelve windows stay open; the one used longest ago goes first, pinned ones last', () => {
  const w = (z, more = {}) => ({ z, min: false, door: false, ...more });
  const slots = Array.from({ length: 12 }, (_, i) => w(i + 1));
  assert.deepEqual(overLimit(slots, 12), []);
  const fresh = w(50);
  const pinned = slots[0]; pinned.pin = true;
  assert.deepEqual(overLimit([...slots, fresh], 12, fresh), [slots[1]]);
  assert.deepEqual(overLimit([...slots, w(60, { min: true }), w(61, { door: true })], 12), []);
  assert.ok(defaultWindowWidth(1920) < railWidth(1920) - 12, 'fresh windows are smaller than their rail');
  assert.ok(defaultWindowWidth(1100) >= 180);
});
