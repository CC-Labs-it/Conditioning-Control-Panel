/* Renewal Sky ending stars that are behaviour (sim/prestige-endings.js and the feature hooks that read it). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as PE from '../sim/prestige-endings.js';
import * as P from '../sim/prestige.js';
import * as E from '../sim/economy.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { createEngine } from '../ui/engine.js';
import { ENDING_STARS } from '../data/prestige.js';
import { BY_ID, FORKS } from '../data/items.js';
import { goldStay, peekRate } from '../ui/unlocks/prestige/boosts.js';

const ui = () => ({ doc: {}, buttonFor: () => ({}), layer: () => ({}) });
const view = (...ids) => ({ lit: (id) => ids.includes(id), has: () => false, level: () => 0 });
const raw = (...ids) => ({ lit: Object.fromEntries(ids.map((id) => [id, 1])) });

test('every ending star perk is wired somewhere', () => {
  const plain = ['drop', 'o_out', 'p_life', 'si_end', 'outside'];   // ui/engine.js + sim/prestige.js (Core lane)
  const here = ['go_end', 'lo_end', 'tr_end', 'bi_end', 'dr_end', 'ce_end', 'in_end', 'onemore'];
  assert.deepEqual([...plain, ...here].sort(), ENDING_STARS.map((e) => e.id).sort());
});

test('lit reads a ctx.prestige view and a raw prestige object alike', () => {
  assert.equal(PE.litOf(view('go_end'), 'go_end'), true);
  assert.equal(PE.litOf(raw('go_end'), 'go_end'), true);
  assert.equal(PE.litOf(raw(), 'go_end'), false);
  assert.equal(PE.litOf(null, 'go_end'), false);
});

test('Sunrise: the Golden Button stays longer, stacked with Long Gold', () => {
  assert.equal(PE.sunriseStay(view()), 1);
  assert.equal(PE.sunriseStay(view('go_end')), PE.SUNRISE_STAY);
  const longGold = { has: (id) => id === 'active_9', lit: (id) => id === 'go_end' };
  assert.equal(goldStay(longGold) * PE.sunriseStay(longGold), 1.5 * 1.5);
});

test('Lost Key: the shut Toy Box counts down faster, stacked with Peek', () => {
  assert.equal(PE.lostKeyRate(view()), 1);
  assert.equal(PE.lostKeyRate(view('lo_end')), PE.LOST_KEY_RATE);
  assert.ok(PE.LOST_KEY_RATE > 1);
  const both = { has: (id) => id === 'luck_2', lit: (id) => id === 'lo_end' };
  assert.equal(peekRate(both) * PE.lostKeyRate(both), 1.1 * PE.LOST_KEY_RATE);
});

test('Count Up: every spiral row pays 10% more of its own pay, after the soft cap', () => {
  for (const id of PE.SPIRAL_ROWS) assert.ok(BY_ID[id]?.fps > 0, id);
  const run = E.createRun();
  E.grant(run, 'overlay');
  const mods = [];
  const ctx = (lit) => ({ prestige: view(...lit), item: BY_ID.overlay, mod: (m) => mods.push(m) });
  PE.mountCountUp(ctx([]));
  assert.equal(mods.length, 0, 'not lit: no mod at all');
  PE.mountCountUp(ctx(['tr_end']));
  assert.equal(mods.length, 1);
  assert.equal(mods[0].post, true);
  const before = E.rates(run, []).fps, after = E.rates(run, mods).fps;
  assert.ok(Math.abs(after - before - BY_ID.overlay.fps * 0.1) < 1e-9, `${before} -> ${after}`);
});

test('Bimbodoll: a fresh run owns Bubble Button, a saved one is left alone', () => {
  const life = createLife();
  life.prestige.lit.bi_end = 1;
  const run = E.createRun();
  createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.ok(E.has(run, PE.BUBBLE_ROW));
  const plain = E.createRun();
  createEngine({ run: plain, life: createLife(), ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(E.has(plain, PE.BUBBLE_ROW), false);
});

test('Unit Complete: Scrolling Thoughts arrives at Rank 2 in the Journey', () => {
  const run = { owned: { ticker: 1 }, store: { journey: { ranks: {} } } };
  assert.equal(PE.tickerHeadStart(run, raw()), false);
  assert.equal(PE.tickerHeadStart(run, raw('dr_end')), true);
  assert.equal(run.store.journey.ranks.ticker, 1);
  assert.equal(PE.tickerHeadStart(run, raw('dr_end')), false, 'once');
  assert.equal(PE.tickerHeadStart({ owned: { ticker: 1 }, store: {} }, raw('dr_end')), false, 'no Journey, no ranks');
  assert.equal(PE.tickerHeadStart({ owned: {}, store: { journey: { ranks: {} } } }, raw('dr_end')), false, 'not owned yet');
});

test('Unit Complete: buying the ticker in a Journey run sets the rank', () => {
  const life = createLife();
  life.prestige.lit.dr_end = 1;
  const run = E.createRun();
  const g = createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {}, env: { journey: true } });
  for (let n = 0; n < 60 && !run.owned.ticker; n += 1) {
    const it = g.shop(6).goal;
    if (!it) break;
    run.fuzz += 1e9; run.total += 1e9;
    if (it.kind === 'fork') g.choose(it.id, Object.keys(FORKS[it.id].opts)[0]);
    else g.buy(it.id, it.options?.[0]?.[0]);
  }
  assert.ok(run.owned.ticker, 'the ticker was bought');
  assert.equal(run.store.journey.ranks.ticker, 1);
});

test('Redacted: a tap on a bar pays a scratch, only when lit', () => {
  assert.equal(PE.redactedPay(view(), { fps: 100, click: 5 }), 0);
  assert.equal(PE.redactedPay(view('ce_end'), { fps: 100, click: 5 }), 15);
  assert.equal(PE.redactedPay(view('ce_end'), {}), 1);
});

test('Flatline: Infection spreads a third faster', () => {
  assert.equal(PE.flatlineSpread(view()), 1);
  assert.equal(PE.flatlineSpread(view('in_end')), PE.FLATLINE_SPREAD);
  assert.ok(PE.FLATLINE_SPREAD < 1);
});

test('Endless: Keep playing only when lit, after another ending, before a Renew', () => {
  const life = createLife();
  const run = E.createRun();
  run.owned.go_end = 1; run.order.push('go_end');
  run.ended = { id: 'go_end', at: 5 };
  assert.equal(PE.canKeepPlaying(life, run), false, 'not lit');
  life.prestige.lit.onemore = 1;
  assert.equal(PE.canKeepPlaying(life, run), true);
  assert.equal(PE.canKeepPlaying(life, { ...run, ended: { id: 'onemore', at: 1 } }), false, 'Endless Mode itself never ends a run');
  assert.equal(PE.canKeepPlaying(life, { ...run, prestige: { renewed: true } }), false, 'a renewed run is gone');
  assert.equal(PE.canKeepPlaying(life, { ...run, ended: null }), false, 'nothing ended');
  assert.equal(PE.keepPlaying(life, run), true);
  assert.equal(run.ended, null);
  assert.equal(P.lit(life.prestige, 'go_end'), true, 'the ending it finished on lights now');
  assert.equal(PE.keepPlaying(life, run), false);
  // The saved run loads as a live one: the engine is not stopped and the run keeps paying.
  const back = parse(serialize(run, life, {}));
  assert.equal(back.run.ended, null);
  assert.ok(back.run.owned.go_end);
  const g = createEngine({ run: back.run, life: back.life, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(g.stopped, false);
  const total = back.run.total;
  g.pay(100, 'lump');
  assert.ok(back.run.total > total);
});
