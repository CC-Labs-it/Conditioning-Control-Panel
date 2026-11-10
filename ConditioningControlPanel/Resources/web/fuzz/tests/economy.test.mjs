import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';
import { MOODS, MOOD_IDS, rollMood } from '../sim/moods.js';
import { CONFIG } from '../data/config.js';

/** What a catalog cost sells for: every price follows CONFIG.PRICE_SCALE. */
const P = (cost) => Math.round(cost * CONFIG.PRICE_SCALE);

const rich = (s, n = 1e9) => { E.lump(s, n, 'test'); return s; };

test('a run starts as one button that pays 1 a tap', () => {
  const s = E.createRun();
  assert.equal(s.fuzz, 0);
  assert.ok(E.has(s, 'start'));
  assert.equal(E.tap(s), 1);
  assert.equal(s.fuzz, 1);
});

test('parents gate a row, and a buy spends the price', () => {
  const s = rich(E.createRun(), 100);
  assert.equal(E.buy(s, 'fpsv').reason, 'closed');
  assert.equal(E.buy(s, 'stray').ok, true);
  assert.equal(s.fuzz, 100 - P(3));
  assert.equal(E.buy(s, 'fpsv').ok, true);
  assert.equal(s.fuzz, 100 - P(3) - P(25));
});

test('short of money is refused and costs nothing', () => {
  const s = E.createRun();
  assert.equal(E.buy(s, 'stray').reason, 'short');
  assert.equal(s.fuzz, 0);
});

test('stacking rows get dearer and stop at their max', () => {
  const s = rich(E.createRun());
  const prices = [];
  for (let i = 0; i < 12; i++) { prices.push(E.price(s, 'stray')); assert.ok(E.buy(s, 'stray').ok); }
  assert.deepEqual(prices.slice(0, 4), [0, 1, 2, 3].map((n) => P(3 * 1.5 ** n)));
  assert.equal(E.count(s, 'stray'), 12);
  assert.equal(E.buy(s, 'stray').reason, 'closed');
  assert.equal(E.rates(s).wall, 2, 'a stack pays through bounces, not through its row number');
});

test('bounces and manual Round Corners taps add a share of base passive income', () => {
  const s = E.createRun({ pace: 2 });
  E.grant(s, 'ticker');
  const passive = s.base.fps;
  assert.ok(passive > 0);
  const mods = [{ fps: v => v * 2 }];
  const before = E.rates(s, mods);
  assert.equal(E.tap(s, mods), before.click * 2);
  E.grant(s, 'rounded');
  const rate = E.rates(s, mods);
  assert.equal(E.tap(s, mods), (rate.click + passive * 0.005) * 2);
  assert.equal(E.tap(s, mods, s.clock, true), rate.click * 2);
  assert.equal(E.wallHit(s, mods), (rate.wall + passive * 0.01) * 2);
  assert.equal(E.wallHit(s, mods, s.clock, 'e_bounce'), rate.wall * 2);
  assert.equal(E.tap(s, [{ click: () => 0 }]), 0);
  assert.equal(E.wallHit(s, [{ wall: () => 0 }]), 0);
});

test('a fork costs nothing, needs its price reached, and locks the other lanes', () => {
  const s = rich(E.createRun(), 200);
  E.buy(s, 'stray'); E.buy(s, 'fpsv');
  const before = s.fuzz;
  assert.equal(E.buy(s, 'f1').reason, 'fork');
  assert.equal(E.choose(s, 'f1', 'nope').reason, 'unknown');
  assert.equal(E.choose(s, 'f1', 'eyes').ok, true);
  assert.equal(s.fuzz, before);
  assert.equal(E.pick(s, 'f1'), 'eyes');
  assert.equal(E.choose(s, 'f1', 'ears').reason, 'closed', 'a fork is taken once');
  assert.equal(E.isOpen(s, BY_ID.e_spiral), true);
  assert.equal(E.isOpen(s, BY_ID.a_hum), false);
});

test('a fork below its price is refused', () => {
  const s = rich(E.createRun(), P(3) + P(25) + P(100) - 1);
  E.buy(s, 'stray'); E.buy(s, 'fpsv');
  assert.equal(E.choose(s, 'f1', 'eyes').reason, 'short');
});

test('a combo needs both parents, so it only exists when two routes meet', () => {
  const s = E.createRun();
  E.grant(s, 'e_pulse');
  assert.equal(E.isOpen(s, BY_ID.x_dream), false);
  E.grant(s, 's_blanket');
  assert.equal(E.pick(s, 'f1'), 'eyes');
  assert.equal(E.pick(s, 'f2'), 'sweet');
  assert.equal(E.isOpen(s, BY_ID.x_dream), true);
  const other = E.createRun();
  E.grant(other, 'a_voice');
  E.grant(other, 's_blanket');
  assert.equal(E.isOpen(other, BY_ID.x_dream), false);
});

test('grant walks the parents and refuses a lane the route locked', () => {
  const s = E.createRun();
  E.grant(s, 'e_speed');
  assert.ok(E.has(s, 'e_spiral') && E.has(s, 'f1') && E.has(s, 'fpsv') && E.has(s, 'stray'));
  assert.deepEqual(E.grant(s, 'a_hum'), []);
  assert.equal(E.has(s, 'a_hum'), false);
});

test('modifiers fold in order, can read state and clock, and a bad value is ignored', () => {
  const s = rich(E.createRun());
  const mods = [
    { fps: (v) => v + 5, click: (v) => v * 3, price: (v, _s, _n, item) => (item.id === 'stray' ? 1 : v) },
    { fps: (v, st, now) => v + (st.taps + now), wall: () => NaN },
  ];
  assert.equal(E.rates(s, mods, 5).fps, 10);
  assert.equal(E.rates(s, mods).click, 3);
  assert.equal(E.rates(s, mods).wall, 1);
  assert.equal(E.price(s, 'stray', mods), 1);
  assert.equal(E.tap(s, mods), 3);
  assert.equal(E.rates(s, mods, 0).fps, 6, 'the tap moved state, the modifier saw it');
});

test('per-second income follows the game clock and pace', () => {
  const s = E.createRun({ pace: 2 });
  E.grant(s, 'ticker');
  const fps = E.rates(s).fps;
  assert.ok(fps > 0);
  E.tick(s, 1000);
  assert.equal(s.fuzz, fps * 2);
  assert.equal(s.clock, 1000);
});

test('no fallback ending: the fate pick holds the ending; Drop is retired; nothing is on offer after an ending and income stops', () => {
  const s = rich(E.createRun());
  E.grant(s, 'taste2');
  assert.equal(E.heldEnding(s), null, 'no seed, no ending');
  E.grant(s, 'tunnel');   // Spiral Overlay needs a seed: the dev door grants the first, A Window (Go Outside)
  assert.equal(E.fateOf(s), 'outside');
  assert.ok(!E.isOpen(s, BY_ID.outside), 'Go Outside waits for its fourth card');
  E.grant(s, 'ft_door');
  assert.equal(E.heldEnding(s), 'outside');
  assert.equal(E.goal(s).id, 'outside', 'the held ending takes the main path\'s last place');
  assert.ok(!E.isOpen(s, BY_ID.drop));
  assert.ok(!E.buy(s, 'drop').ok);
  assert.ok(E.buy(s, 'outside').ok);
  assert.equal(s.ended.id, 'outside');
  assert.equal(E.openItems(s).length, 0);
  assert.equal(E.goal(s), null);
  const f = s.fuzz;
  E.tick(s, 1000);
  assert.equal(s.fuzz, f);
});

test('one ending a run: the fate set picked, offered only once the main path is done', () => {
  for (const id of ['go_mus', 'o_out']) {
    const s = rich(E.createRun());
    E.grant(s, BY_ID[id].req[0]);
    assert.equal(E.heldEnding(s), id);
    assert.ok(!E.isOpen(s, BY_ID[id]), `${id} waits for the end of the main path`);
    assert.ok(!E.buy(s, id).ok);
    E.grant(s, 'tunnel');
    assert.ok(E.isOpen(s, BY_ID[id]), id);
    assert.ok(!E.isOpen(s, BY_ID.drop), 'Drop steps aside for the held ending');
    assert.ok(!E.isOpen(s, BY_ID.outside), 'Go Outside is not offered beside it');
    assert.equal(E.openItems(s).filter((i) => i.kind === 'end').length, 1);
    assert.ok(E.buy(s, id).ok, id);
    assert.equal(s.ended.id, id);
  }
  /* the first seed holds the set: another set's cards never open, and the dev door cannot grant them */
  const s = rich(E.createRun());
  E.grant(s, BY_ID.o_out.req[0]);
  assert.deepEqual(E.grant(s, 'ft_window'), []);
  E.grant(s, 'tunnel');
  assert.equal(E.heldEnding(s), 'o_out');
  assert.deepEqual(E.openItems(s).filter((i) => i.kind === 'end').map((i) => i.id), ['o_out']);
  /* the retired theme and Premium endings are never offered, even with their old chain owned */
  const r = rich(E.createRun());
  E.grant(r, 'tr_deep'); E.grant(r, 'go_tx'); E.grant(r, 'tunnel');
  assert.ok(!E.isOpen(r, BY_ID.tr_end));
  assert.ok(!E.buy(r, 'tr_end').ok);
  /* Endless Mode is off the shelf (owner 2026-11-10) */
  const t = rich(E.createRun());
  E.grant(t, 'f5');
  assert.ok(!E.isOpen(t, BY_ID.onemore));
  assert.ok(!E.buy(t, 'onemore').ok);
});

test('the shop is a rolling window: the goal plus the next few by price, never a wall', () => {
  const s = rich(E.createRun(), 1e7);
  E.grant(s, 'f5');
  const open = E.openItems(s).length;
  const win = E.shopWindow(s, null, 5);
  assert.equal(win.rows.length, 5);
  assert.equal(win.goal.lane, 'trunk');
  assert.ok(!win.rows.includes(win.goal));
  assert.equal(win.more + win.rows.length + 1, open);
  const prices = win.rows.map((r) => E.price(s, r.id));
  assert.deepEqual(prices, [...prices].sort((a, b) => a - b));
});

test('a row far out of reach stays out of the window; the goal never does', () => {
  const s = E.createRun();
  assert.equal(E.shopWindow(s).goal.id, 'stray');
  assert.deepEqual(E.shopWindow(s).rows.map((r) => r.id), ['rounded', 'title'].filter((id) => E.price(s, id) <= 8));
});

test('moods change prices and pay without touching the base rows', () => {
  const pink = E.createRun({ mood: 'm_pink' });
  assert.equal(E.price(pink, 'colour'), 0);
  assert.equal(E.price(pink, 'rounded'), Math.round(BY_ID.rounded.cost * CONFIG.PRICE_SCALE * 0.5));
  assert.equal(E.price(pink, 'f1'), Math.round(BY_ID.f1.cost * CONFIG.PRICE_SCALE * 1.1));
  const rush = E.createRun({ mood: 'm_rush' });
  assert.equal(E.price(rush, 'drop'), Math.round(BY_ID.drop.cost * CONFIG.PRICE_SCALE * 0.75));
  E.lump(rush, 1e7);
  E.grant(rush, 'f5');
  assert.equal(E.shopWindow(rush, null, 6).rows.length, 3);
  const broke = E.createRun({ mood: 'm_broke' });
  E.grant(broke, 'banner');
  assert.equal(broke.owned.banner, 1);
  assert.ok(E.rates(broke).fps > E.rates((() => { const n = E.createRun(); E.grant(n, 'banner'); return n; })()).fps);
  assert.equal(E.rates(E.createRun({ mood: 'm_static' })).walls, 0.7);
});

test('every mood row in the design has its numbers; the first run has none', () => {
  const designed = ITEMS.filter((i) => i.lane === 'meta' && i.id !== 'm_mood').map((i) => i.id).sort();
  assert.deepEqual([...MOOD_IDS].sort(), designed);
  assert.equal(rollMood(0, () => 0), null);
  assert.equal(rollMood(1, () => 0), MOOD_IDS[0]);
  for (const id of MOOD_IDS) assert.ok(MOODS[id]);
});
