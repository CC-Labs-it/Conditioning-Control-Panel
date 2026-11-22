// The terms card: the stage ladder over a run, the bundle cap, which rows skip it, and the copy it reads.
import test from 'node:test';
import assert from 'node:assert/strict';
import { clauseStage, clauseLines, skipsCard, BUNDLE_CAP, HEADS } from '../ui/clause.js';
import { CLAUSES, CLAUSE_EXTRAS } from '../data/content/clauses.js';
import { ITEMS } from '../data/items.js';
import { ridersOf } from '../data/folds.js';

test('the card shortens over a run: ticks, then one button, then one clause', () => {
  assert.equal(clauseStage(1), 1);
  assert.equal(clauseStage(8), 1);
  assert.equal(clauseStage(9), 2);
  assert.equal(clauseStage(25), 2);
  assert.equal(clauseStage(26), 3);
  assert.equal(clauseStage(140), 3);
  assert.equal(clauseStage(1, true), 2, 'a later run starts at the middle stage');
  assert.equal(HEADS[1], 'Please kindly accept the following terms to unlock:');
  assert.equal(HEADS[2], 'Accept the terms to unlock');
});

test('a bundle shows clause 1 of the lead and each rider, at most three; stage 3 shows one clause', () => {
  const map = { a: ['a1', 'a2'], b: ['b1', 'b2'], c: ['c1'], d: ['d1'] };
  assert.deepEqual(clauseLines('a', ['b', 'c', 'd'], 1, map), ['a1', 'b1', 'c1']);
  assert.equal(clauseLines('a', ['b', 'c', 'd'], 2, map).length, BUNDLE_CAP);
  assert.deepEqual(clauseLines('a', ['b'], 3, map), ['a1']);
  assert.deepEqual(clauseLines('a', [], 1, map), ['a1', 'a2']);
  assert.deepEqual(clauseLines('a', [], 3, map), ['a1']);
  assert.deepEqual(clauseLines('zz', [], 1, map), [], 'no copy means no card');
  for (const it of ITEMS) {
    const lines = clauseLines(it.id, ridersOf(it.id), 1);
    assert.ok(lines.length <= 3, it.id);
  }
});

test('forks, endings, ranks and specialties never get a card', () => {
  assert.ok(skipsCard({ id: 'f1', kind: 'fork' }));
  assert.ok(skipsCard({ id: 'drop', kind: 'end' }));
  assert.ok(skipsCard({ id: 'rank_stray_2', kind: 'item' }));
  assert.ok(skipsCard({ id: 'spec_mix', kind: 'item', options: [['a', 'A', '']] }));
  assert.ok(!skipsCard({ id: 'rounded', kind: 'item' }));
});

test('every plain unlock in the catalog has 1 to 3 clauses in the house voice', () => {
  for (const it of ITEMS.filter((i) => i.group === 'run' && i.kind === 'item')) {
    const c = CLAUSES[it.id];
    assert.ok(c && c.length >= 1 && c.length <= 3, it.id);
  }
  for (const t of [...Object.values(CLAUSES).flat(), ...Object.values(CLAUSE_EXTRAS).flat()]) {
    assert.ok(!/[–—!]/.test(t), t);
  }
  for (const t of CLAUSE_EXTRAS.decline) assert.ok(!/unlocked|not applied/i.test(t), `Decline never buys: ${t}`);
  for (const t of CLAUSE_EXTRAS.accept) assert.equal(t, t.toUpperCase());
});
