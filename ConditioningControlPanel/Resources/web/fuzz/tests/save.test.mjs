import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { createLife, forget, load, noteSeen, parse, recordEnding, SAVE_KEY, serialize, store } from '../sim/save.js';

function fakeStorage() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k) };
}

test('a save round-trips the run and the lifetime', () => {
  const s = E.createRun({ pace: 8, mood: 'm_rush' });
  E.lump(s, 500);
  E.grant(s, 'e_spiral');
  s.store.e_spiral = { speed: 2 };
  const life = createLife();
  life.age = true;
  const back = parse(serialize(s, life, { muted: true }));
  assert.deepEqual(back.run.owned, s.owned);
  assert.deepEqual(back.run.order, s.order);
  assert.deepEqual(back.run.picks, { f1: 'eyes' });
  assert.equal(back.run.pace, 8);
  assert.equal(back.run.mood, 'm_rush');
  assert.deepEqual(back.run.store, { e_spiral: { speed: 2 } });
  assert.deepEqual(back.run.base, s.base);
  assert.equal(back.life.age, true);
  assert.deepEqual(back.extras, { muted: true });
});

test('garbage and the old v1 ladder read as no save, never a throw', () => {
  assert.equal(parse('not json'), null);
  assert.equal(parse(JSON.stringify({ v: 1, s: { fuzz: 5 } })), null);
  assert.equal(parse(42), null);
});

test('a tampered save is clamped: unknown rows dropped, stacks capped, an ending needs its row', () => {
  const raw = JSON.stringify({ v: 2, run: { fuzz: -5, total: 'x', owned: { stray: 99, ghost: 1, rounded: 7, z_map: 1 }, picks: { f1: 'eyes', f9: 'x' }, ended: { id: 'drop', at: 3 }, pace: 1e9, mood: 'm_nope' }, life: { endings: { drop: 2, nope: 1 }, seen: { stray: 1, ghost: 1 }, perk: 'e_spiral', runs: -3 } });
  const { run, life } = parse(raw);
  assert.equal(run.fuzz, 0);
  assert.deepEqual(run.owned, { start: 1, stray: 12, rounded: 1 });
  assert.deepEqual(run.picks, {}, 'a pick needs its fork owned');
  assert.equal(run.ended, null);
  assert.equal(run.mood, null);
  assert.deepEqual(life.endings, { drop: 2 });
  assert.deepEqual(life.seen, { stray: 1 });
  assert.equal(life.perk, null);
  assert.equal(life.runs, 0);
});

test('a save with no run keeps the lifetime: that is what New run writes', () => {
  const life = createLife();
  life.runs = 2;
  const back = parse(serialize(null, life));
  assert.equal(back.run, null);
  assert.equal(back.life.runs, 2);
});

test('an ending is kept across runs, with the route, the time and everything seen', () => {
  const s = E.createRun();
  E.grant(s, 'tunnel');
  s.clock = 1000;
  s.ended = { id: 'drop', at: 1000 };
  const life = recordEnding(createLife(), s, 'drop');
  assert.equal(life.runs, 1);
  assert.deepEqual(life.endings, { drop: 1 });
  assert.equal(life.seen.tunnel, 1);
  assert.equal(life.routes[0].ending, 'drop');
  assert.equal(life.routes[0].ms, 1000);
  assert.equal(life.routes[0].route.length, 5);
  assert.equal(life.bestMs, 1000);
  recordEnding(life, s, 'drop');
  assert.equal(life.endings.drop, 2);
  assert.equal(noteSeen(life, s), life);
});

test('the perk gives the next run one Look it already unlocked', () => {
  const s = E.createRun({ perk: 'rounded' });
  assert.ok(E.has(s, 'rounded'));
  assert.ok(!E.has(E.createRun({ perk: 'e_spiral' }), 'e_spiral'), 'only Looks');
});

test('storage helpers swallow errors and forget on demand', () => {
  const st = fakeStorage();
  const s = E.createRun();
  assert.equal(store(st, s, createLife()), true);
  assert.ok(load(st).run);
  assert.equal(forget(st), true);
  assert.equal(load(st), null);
  const broken = { getItem() { throw new Error('private'); }, setItem() { throw new Error('quota'); } };
  assert.equal(load(broken), null);
  assert.equal(store(broken, s, createLife()), false);
  assert.equal(SAVE_KEY, 'fuzz.save.v2');
});
