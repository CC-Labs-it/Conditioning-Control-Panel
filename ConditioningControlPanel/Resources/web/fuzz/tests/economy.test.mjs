import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';

const never = () => 0.999;   // rng that never crits
const always = () => 0;      // rng that always crits

test('a fresh run: one click is one Fuzz, nothing ticks', () => {
  const s = E.createState({ now: 0 });
  assert.equal(E.tick(s, 1000, 1000), 0);
  assert.deepEqual(E.click(s, 0, never), { gain: 1, crit: false });
  assert.equal(s.fuzz, 1);
  assert.equal(s.total, 1);
});

test('the first bubble costs 3 and the shop shows five tiles with the bubble pinned first', () => {
  const s = E.createState();
  const win = E.shopWindow(s);
  assert.equal(win.length, 5);
  assert.equal(win[0].id, 'bubble');
  assert.equal(E.price(s, 'bubble'), 3);
  s.fuzz = 3;
  assert.equal(E.buy(s, 'bubble', 0).ok, true);
  assert.equal(s.fuzz, 0);
  assert.equal(E.price(s, 'bubble'), 5, 'x1.5 rounds 4.5 to 5');
  assert.equal(E.buy(s, 'bubble', 0).reason, 'short');
});

test('prerequisites hide chains until their parent is owned', () => {
  const s = E.createState();
  s.fuzz = 1e9;
  assert.equal(E.buy(s, 'bouncepop', 0).reason, 'locked');
  assert.equal(E.buy(s, 'bubble', 0).ok, true);
  assert.equal(E.buy(s, 'bouncepop', 0).ok, true);
  assert.equal(E.buy(s, 'bouncepop', 0).reason, 'locked', 'a one-shot item sells once');
});

test('layers pay by real elapsed time and Deeper Still multiplies them', () => {
  const s = E.createState();
  s.fuzz = 1e9;
  E.buy(s, 'spiral', 0);       // +3/s
  E.buy(s, 'ticker', 0);       // +4/s
  assert.equal(E.baseFps(s), 7);
  const before = s.fuzz;
  E.tick(s, 500, 500);
  assert.equal(s.fuzz - before, 3.5);
  s.owned.deep1 = 1;
  assert.equal(E.baseFps(s), 7 * 1.1);
});

test('crits need Trigger Words, pay x8, and the frenzy stacks on top', () => {
  const s = E.createState();
  assert.equal(E.click(s, 0, always).crit, false, 'no crit before the item');
  s.owned.trig = 1;
  const r = E.click(s, 0, always);
  assert.equal(r.crit, true);
  assert.equal(r.gain, 8);
  s.effects.clickFrenzyUntil = 10_000; s.effects.clickFrenzyMul = 77;
  assert.equal(E.click(s, 5000, always).gain, 8 * 77);
  assert.equal(E.click(s, 20_000, always).gain, 8, 'the frenzy ends');
});

test('bounces pay more with Bounce Pop and Pink Pulse, corners x10 only after Corner Pop', () => {
  const s = E.createState();
  assert.equal(E.bounce(s), 1);
  assert.equal(E.bounce(s, true), 1);
  s.owned.bouncepop = 1; s.owned.pinkpulse = 1;
  assert.equal(E.bounce(s), 16);
  s.owned.corner = 1;
  assert.equal(E.bounce(s, true), 160);
  assert.equal(s.corners, 1);
});

test('pace scales clicks and layers alike and is capped', () => {
  const s = E.createState({ pace: 999 });
  assert.equal(s.pace, E.MAX_PACE);
  const p = E.createState({ pace: 8 });
  assert.equal(E.click(p, 0, never).gain, 8);
  p.owned.spiral = 1;
  assert.equal(E.fps(p, 0), 24);
});

test('Drop ends the run: nothing pays and nothing sells after it', () => {
  const s = E.createState({ now: 0 });
  s.fuzz = 2_000_000;
  assert.equal(E.buy(s, 'drop', 60_000).ok, true);
  assert.equal(s.ended, true);
  assert.equal(E.tick(s, 1000, 61_000), 0);
  assert.equal(E.click(s, 61_000).gain, 0);
  assert.equal(E.buy(s, 'pink', 61_000).reason, 'ended');
  assert.equal(E.minutesPlayed(s, 999_999), 1);
});

test('a full ladder is affordable in order: every item unlocks once its parents and achievements are met', () => {
  const s = E.createState();
  s.fuzz = 1e12;
  s.achievements = Array.from({ length: 30 }, (_, i) => `a${i}`);
  for (const it of ITEMS) {
    const r = E.buy(s, it.id, 0);
    assert.equal(r.ok, true, `${it.id}: ${r.reason}`);
  }
  assert.equal(s.bought.length, ITEMS.length);
});
