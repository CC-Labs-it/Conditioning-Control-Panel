import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import * as V from '../sim/events.js';

test('the golden bubble comes 60 to 120 s apart and the three effects keep their weights', () => {
  assert.equal(V.nextGoldenAt(1000, () => 0), 61_000);
  assert.equal(V.nextGoldenAt(1000, () => 0.999), 1000 + 60_000 + 0.999 * 60_000);
  assert.equal(V.rollGolden(() => 0), 'rush');
  assert.equal(V.rollGolden(() => 0.5), 'change');
  assert.equal(V.rollGolden(() => 0.95), 'behaviour');
});

test('Keep the Change pays the lesser of 15% of the bank and 15 minutes of income, plus 13', () => {
  const s = E.createState();
  s.fuzz = 10_000;
  s.owned.spiral = 1; // 3/s -> 15 min = 2700
  const r = V.applyGolden(s, 'change', 0);
  assert.equal(r.amount, 1500 + 13);
  assert.equal(s.fuzz, 11_513);
  s.fuzz = 100;
  assert.equal(V.applyGolden(s, 'change', 0).amount, 15 + 13);
});

test('Pink Rush multiplies the layers for 30 s and then stops', () => {
  const s = E.createState();
  s.owned.spiral = 1;
  V.applyGolden(s, 'rush', 1000);
  assert.equal(E.fps(s, 2000), 21);
  assert.equal(E.fps(s, 1000 + 30_000), 3);
});

test('Triggers: FOCUS makes every click a crit-sized click, BLANK only sets its window', () => {
  const s = E.createState();
  V.applyTrigger(s, 'focus', 0);
  assert.equal(E.click(s, 100, () => 0.999).gain, 8);
  assert.equal(E.click(s, 16_000, () => 0.999).gain, 1);
  const before = JSON.stringify({ ...s, effects: null });
  V.applyTrigger(s, 'blank', 0);
  assert.equal(s.effects.blankUntil, 6000);
  assert.equal(JSON.stringify({ ...s, effects: null }), before, 'BLANK pays nothing');
  V.applyTrigger(s, 'sparkle', 0);
  assert.equal(s.sparkles, 1);
});

test('a box pays 7 to 32 percent of a minute of income and drops a Sparkle only with the item', () => {
  const s = E.createState();
  s.owned.spiral = 1; // 180 a minute
  assert.deepEqual(V.openBox(s, () => 0), { amount: Math.round(180 * 0.07), sparkle: false });
  s.owned.sparkles = 1;
  assert.equal(V.openBox(s, () => 0.1).sparkle, true);
  assert.equal(V.openBox(s, () => 0.9).sparkle, false);
});

test('levels need 2000 x 1.25^n and Collect pays once per level', () => {
  const s = E.createState();
  s.total = 2000 + 2500 + 10;
  const p = V.levelProgress(s);
  assert.equal(p.level, 2);
  assert.equal(p.into, 10);
  assert.equal(p.need, 3125);
  s.owned.spiral = 1;
  let r = V.collectLevel(s, 0);
  assert.equal(r.ok, true); assert.equal(r.collected, 1); assert.equal(r.amount, 180);
  r = V.collectLevel(s, 1); assert.equal(r.ok, true);
  r = V.collectLevel(s, 2); assert.equal(r.ok, false);
});
