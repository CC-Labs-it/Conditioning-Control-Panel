import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { createJourney } from '../sim/journey.js';
import { setupJourney, takeActivity, journeyChapter } from '../sim/journey-effects.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { measureJourney } from '../dev/journey-timing.mjs';
test('the new-player shelf keeps original first prices and blocks late purchases through the buy API', () => {
  const s = E.createRun(),
    store = createJourney(s);
  assert.equal(store.shop(3).goal.id, 'stray');
  assert.equal(store.price('stray'), 5);
  assert.equal(store.price('rounded'), 8);
  E.lump(s, 1e7);
  assert.equal(store.buy('drop').ok, false);
  assert.equal(store.buy('rank_stray_1').ok, false);
  assert.equal(store.buy('stray').ok, true);
});
test('activity ranks cap two active minutes and collect once', () => {
  const s = E.createRun();
  E.grant(s, 'ticker');
  E.grant(s, 'levels');
  const j = setupJourney(s);
  j.ranks.levels = 2;
  j.banks.levels = 0;
  E.tick(s, 190000);
  assert.equal(takeActivity(s, 'levels'), s.base.fps * 2 * .01 * 120);
  assert.equal(takeActivity(s, 'levels'), 0);
});
test('a shop coupon is shown and charged at the same price, and free forks do not advance its chapter', () => {
  const s = E.createRun(),
    shop = createJourney(s),
    discount = [{
      price: () => 0
    }];
  assert.equal(shop.price('stray', discount), 0);
  assert.equal(shop.buy('stray', null, discount).cost, 0);
  assert.equal(s.fuzz, 0);
  s.buys = 15;
  s.picks = {
    f1: 'eyes',
    f5: 'trance'
  };
  assert.equal(journeyChapter(s), 0);
  s.buys++;
  assert.equal(journeyChapter(s), 1);
});
test('a different specialization changes the right hit; a missing option never consumes money', () => {
  const s = E.createRun(),
    shop = createJourney(s);
  E.lump(s, 1e7);
  for (let i = 0; i < 50; i++) {
    const {
      goal,
      rows
    } = shop.shop(6);
    if ([goal, ...rows].some(it => it?.id === 'spec_rebounds')) break;
    if (goal.kind === 'fork') {
      shop.choose(goal.id, { f1:'eyes', f5:'trance', f3:'scroll' }[goal.id]);
    } else {
      shop.buy(goal.id);
    }
  }
  const before = s.fuzz;
  assert.equal(shop.buy('spec_rebounds', 'invalid').reason, 'choice');
  assert.equal(s.fuzz, before);
  assert.ok(shop.buy('spec_rebounds', 'corner').ok);
  const normal = E.wallHit(s);
  assert.equal(E.wallHit(s, null, s.clock, 'stray', true), normal + s.base.fps * .2);
  assert.equal(shop.buy('spec_rebounds', 'steady').ok, false);
});
test('different first runs retain their ranks and choices after a save and reach Drop with regular paid upgrades', () => {
  for (const [taps, f1, f2, f3, f4, f5, option] of [[4, 'eyes', 'sweet', 'scroll', 'sell', 'trance', 0], [2, 'ears', 'strict', 'blind', 'premium', 'drone', 1]]) {
    const r = measureJourney({
      f1,
      f2,
      f3,
      f4,
      f5
    }, {
      taps,
      option,
      restore: s => parse(serialize(s, createLife())).run
    });
    assert.equal(Object.keys(r.s.store.journey.specs).length, 4);
    assert.deepEqual(r.s.picks, {
      f1,
      f2,
      f3,
      f4,
      f5
    });
    assert.ok(r.buys >= 140 && r.max < 270, JSON.stringify([r.buys, r.max]));
    assert.ok(r.minutes > 70 && r.minutes < 150, `${r.minutes} min: owner 2026-11-09, about 90 staying (JOURNEY_STRETCH 1.47)`);
  }
});
