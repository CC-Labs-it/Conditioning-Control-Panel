import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, ITEMS } from '../data/items.js';
import { FOLD, LEAD_OF, buildFolds } from '../data/folds.js';
import * as E from '../sim/economy.js';

const rich = (s, n = 1e9) => { E.lump(s, n, 'test'); return s; };

test('Auto-Scroll is an optional purchase after Swipe Feed and preserves its existing bundle', () => {
  const s = rich(E.createRun());
  const upgrade = BY_ID.d_autoscroll;
  assert.equal(E.isOpen(s, upgrade), false);
  assert.deepEqual(FOLD.d_scroll, ['d_like', 'd_clips']);
  E.grant(s, 'd_scroll');
  assert.equal(E.has(s, 'd_autoscroll'), false);
  assert.equal(E.isOpen(s, upgrade), true);
  assert.equal(E.price(s, 'd_autoscroll'), 6000);
  assert.equal(E.buy(s, 'd_autoscroll').ok, true);
  assert.equal(E.isOpen(s, upgrade), false);
});

test('bundles fold plain lane stretches and never touch forks, endings, skins or toys', () => {
  assert.ok(Object.keys(FOLD).length >= 40, 'a run has about half the purchases');
  for (const [lead, riders] of Object.entries(FOLD)) {
    const group = [lead, ...riders].map((id) => BY_ID[id]);
    assert.ok(group.every((it) => it.kind === 'item' && !it.grow && it.lane === group[0].lane), lead);
    riders.forEach((id, i) => assert.equal(BY_ID[id].req[0], i ? riders[i - 1] : lead, `${id} follows its bundle`));
  }
  for (const id of ['rounded', 'bigbtn', 'toy_box', 'mini_slots', 'f1', 'drop', 'stray']) assert.equal(!!(FOLD[id] || LEAD_OF[id]), false, id);
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

test('Starburst and Word Comet each require their own shop purchase', () => {
  const { createRun, grant, buy, isOpen } = E;
  const run = createRun();
  run.fuzz = run.total = 100000;
  grant(run, 'trail');
  assert.equal(isOpen(run, BY_ID.starburst), true);
  assert.equal(isOpen(run, BY_ID.word_comet), false);
  assert.equal(buy(run, 'starburst').ok, true);
  assert.equal(isOpen(run, BY_ID.word_comet), true);
  assert.equal(buy(run, 'word_comet').ok, true);
});
