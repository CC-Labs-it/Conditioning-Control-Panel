import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { serialize, parse, load, store, forget, SAVE_KEY } from '../sim/save.js';

function fakeStorage() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k), size: () => m.size };
}

test('a save round-trips the economy and the extras', () => {
  const s = E.createState({ now: 5, pace: 8 });
  s.fuzz = 1234; s.total = 9999; s.owned = { bubble: 3, spiral: 1 }; s.bought = ['bubble', 'spiral', 'bubble', 'bubble'];
  s.achievements = ['first-pop']; s.sparkles = 2;
  const back = parse(serialize(s, { muted: true, safe: false }), { now: 50 });
  assert.deepEqual(back.state.owned, s.owned);
  assert.equal(back.state.fuzz, 1234);
  assert.equal(back.state.pace, 8);
  assert.equal(back.state.startedAt, 5);
  assert.deepEqual(back.extras, { muted: true, safe: false });
});

test('garbage and old versions read as no save, never a throw', () => {
  assert.equal(parse('not json'), null);
  assert.equal(parse(JSON.stringify({ v: 0, s: {} })), null);
  assert.equal(parse(JSON.stringify({ v: 1 })), null);
  assert.equal(parse(42), null);
});

test('a tampered save is clamped: unknown items dropped, counts capped, ended only with Drop owned', () => {
  const raw = JSON.stringify({ v: 1, s: { fuzz: -5, total: 'x', owned: { bubble: 99, ghost: 1, pink: 7 }, ended: true, pace: 1e9 } });
  const { state } = parse(raw);
  assert.equal(state.fuzz, 0);
  assert.deepEqual(state.owned, { bubble: 12, pink: 1 });
  assert.equal(state.ended, false);
  assert.equal(state.pace, E.MAX_PACE);
});

test('storage helpers swallow errors and forget on demand', () => {
  const st = fakeStorage();
  const s = E.createState();
  assert.equal(store(st, s), true);
  assert.ok(load(st).state);
  assert.equal(forget(st), true);
  assert.equal(load(st), null);
  const broken = { getItem() { throw new Error('private'); }, setItem() { throw new Error('quota'); } };
  assert.equal(load(broken), null);
  assert.equal(store(broken, s), false);
  assert.equal(SAVE_KEY, 'fuzz.save.v1');
});
