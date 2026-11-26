// T&C Ascension round, lane K: the next-run bag in sim/prestige.js (buyLater, settleLater) and sim/ascension.js ascensionView.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as P from '../sim/prestige.js';
import * as E from '../sim/economy.js';
import { ascensionView, ASCENSION_LABELS } from '../sim/ascension.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { createEngine } from '../ui/engine.js';
import { LANES, STAR_BY_ID, ENDING_STARS, CORE, MAX_DEPTH, STEPS } from '../data/prestige.js';

const ui = () => ({ doc: {}, buttonFor: () => ({}), layer: () => ({}) });
const fullSky = (p) => { for (const l of LANES) for (const s of l.stars) p.own[s.id] = 1; return p; };

test('createPrestige carries an empty next-run bag; old saves without one read as empty', () => {
  assert.deepEqual(P.createPrestige().later, { own: {}, core: false });
  assert.deepEqual(P.parsePrestige({ depth: 3, own: { pay_1: 1 } }).later, { own: {}, core: false });
  for (const bad of [null, 7, 'x', [], { later: 5 }, { later: { own: [1], core: 'y' } }, { later: { own: null } }]) {
    const p = P.parsePrestige(bad);
    assert.deepEqual(Object.keys(p.later.own), []);
  }
});

test('parsePrestige clamps the bag: unknown ids dropped, owned ones dropped, core only when not owned', () => {
  const p = P.parsePrestige({ depth: 4, own: { pay_1: 1 }, later: { own: { pay_1: 1, pay_2: 1, nope_1: 1, idle_1: 0 }, core: 1 } });
  assert.deepEqual(p.later.own, { pay_2: 1 });
  assert.equal(p.later.core, true);
  assert.equal(P.parsePrestige({ core: true, later: { core: true } }).later.core, false);
});

test('buyLater: pays now, goes into the bag, never felt this run; a chain of buys in a row works', () => {
  const p = P.createPrestige();
  p.depth = 3; p.dust = 1e6;
  const cost1 = STAR_BY_ID.pay_1.cost, cost2 = STAR_BY_ID.pay_2.cost;
  const r = P.buyLater(p, 'pay_1');
  assert.deepEqual(r, { ok: true, cost: cost1 });
  assert.equal(p.dust, 1e6 - cost1); assert.equal(p.spent, cost1);
  assert.equal(P.has(p, 'pay_1'), false); assert.equal(P.level(p, 'pay'), 0);
  assert.equal(P.taken(p, 'pay_1'), true); assert.equal(P.isLater(p, 'pay_1'), true); assert.equal(P.takenLevel(p, 'pay'), 1);
  assert.equal(P.canBuy(p, 'pay_1').reason, 'owned');
  assert.equal(P.buyLater(p, 'pay_1').ok, false);
  assert.equal(P.buyStar(p, 'pay_1').ok, false);   // no double buy across the two doors
  assert.equal(P.canBuy(p, 'pay_2').ok, true);     // lane order counts the bag
  assert.equal(P.buyLater(p, 'pay_2').ok, true);
  assert.equal(P.buyLater(p, 'pay_3').ok, true);
  assert.equal(P.canBuy(p, 'pay_4').reason, 'depth');
  assert.deepEqual(p.later.own, { pay_1: 1, pay_2: 1, pay_3: 1 });
  assert.equal(p.dust, 1e6 - cost1 - cost2 - STAR_BY_ID.pay_3.cost);
  assert.equal(P.runStamp(p).pay, 1);              // nothing bought mid-run applies to a stamp before settling
  const poor = P.createPrestige(); poor.depth = 1;
  assert.deepEqual(P.buyLater(poor, 'pay_1'), { ok: false, reason: 'dust' });
  assert.deepEqual(P.buyLater(poor, 'bogus'), { ok: false, reason: 'unknown' });
});

test('buyCoreLater: the Core counts lanes the bag fills, and goes into the bag', () => {
  const p = fullSky(P.createPrestige());
  p.depth = MAX_DEPTH; p.dust = CORE.cost * 3;
  delete p.own.ghost_10;
  assert.equal(P.canCore(p).reason, 'lanes');
  assert.equal(P.buyLater(p, 'ghost_10').ok, true);
  assert.equal(P.canCore(p).ok, true);
  assert.deepEqual(P.buyCoreLater(p), { ok: true, cost: CORE.cost });
  assert.equal(p.core, false); assert.equal(p.later.core, true); assert.equal(P.coreTaken(p), true);
  assert.equal(P.canCore(p).reason, 'owned');
  assert.equal(P.buyCore(p).ok, false);
  assert.deepEqual(P.settleLater(p).sort(), ['core', 'ghost_10']);
  assert.equal(p.core, true); assert.equal(P.has(p, 'ghost_10'), true);
});

test('settleLater moves the bag into own once and empties it', () => {
  const p = P.createPrestige();
  p.depth = 2; p.dust = 1e6;
  P.buyLater(p, 'pay_1'); P.buyLater(p, 'idle_1');
  const dust = p.dust;
  assert.deepEqual(P.settleLater(p), ['pay_1', 'idle_1']);
  assert.deepEqual(p.own, { pay_1: 1, idle_1: 1 });
  assert.deepEqual(p.later, { own: {}, core: false });
  assert.equal(p.dust, dust);
  assert.deepEqual(P.settleLater(p), []);
  assert.deepEqual(P.settleLater(null), []);
  const old = { depth: 1, own: {} };              // a raw record with no bag
  assert.deepEqual(P.settleLater(old), []); assert.deepEqual(old.later, { own: {}, core: false });
});

test('renew settles the bag before it counts', () => {
  const life = createLife();
  const p = life.prestige;
  p.depth = 2; p.dust = 1e6;
  P.buyLater(p, 'pay_1');
  const run = E.createRun();
  P.renew(life, run);
  assert.equal(P.has(p, 'pay_1'), true);
  assert.deepEqual(p.later.own, {});
  assert.equal(p.depth, 3);
});

test('engine: a fresh run settles the bag and feels it; a reload of that run settles nothing more', () => {
  const life = createLife();
  const p = life.prestige;
  p.depth = 2; p.dust = 1e6;
  P.buyLater(p, 'pay_1');
  const run = E.createRun();
  createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(P.has(p, 'pay_1'), true);
  assert.ok(run.prestige.pay > 1);
  // A star bought mid-run on this run, then a reload: the saved run keeps its stamp, the bag waits for the next run.
  P.buyLater(p, 'pay_2');
  const back = parse(serialize(run, life));
  const lifeB = back.life, runB = back.run;
  assert.deepEqual(lifeB.prestige.later.own, { pay_2: 1 });
  createEngine({ run: runB, life: lifeB, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.deepEqual(lifeB.prestige.later.own, { pay_2: 1 });
  assert.equal(P.has(lifeB.prestige, 'pay_2'), false);
  // The next fresh run feels it.
  createEngine({ run: E.createRun(), life: lifeB, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(P.has(lifeB.prestige, 'pay_2'), true);
  assert.deepEqual(lifeB.prestige.later.own, {});
});

test('ascensionView: shapes, sky order, endings, core, next bag', () => {
  const life = createLife();
  const p = life.prestige;
  p.depth = 4; p.dust = 1e6;
  p.own.idle_1 = 1; p.own.pay_1 = 1; p.own.pay_2 = 1; p.own.pay_4 = 1;
  p.lit.drop = 1; p.lit.outside = 1;
  P.buyLater(p, 'pay_3'); P.buyLater(p, 'idle_2');
  const v = ascensionView(life);
  assert.equal(v.depth, 4); assert.equal(v.maxDepth, MAX_DEPTH); assert.equal(v.dust, p.dust); assert.equal(v.core, false);
  assert.deepEqual(v.bonuses.map((b) => b.id), ['pay_1', 'pay_2', 'pay_4', 'idle_1', 'drop', 'outside']);
  assert.deepEqual(v.next.map((b) => b.id), ['pay_3', 'idle_2']);
  assert.deepEqual(v.counts, { stars: 4, endings: 2, next: 2 });
  const keys = ['id', 'kind', 'lane', 'laneName', 'hue', 'icon', 'name', 'line', 'plain', 'next'].sort();
  for (const b of [...v.bonuses, ...v.next]) {
    assert.deepEqual(Object.keys(b).sort(), keys);
    for (const k of ['id', 'lane', 'laneName', 'hue', 'icon', 'name', 'line', 'plain']) assert.equal(typeof b[k], 'string', k);
  }
  const pay4 = v.bonuses[2], pay = LANES.find((l) => l.id === 'pay');
  assert.deepEqual({ ...pay4 }, { id: 'pay_4', kind: 'star', lane: 'pay', laneName: 'Pay', hue: pay.hue, icon: pay.icons[1],
    name: STAR_BY_ID.pay_4.name, line: STAR_BY_ID.pay_4.flav, plain: STAR_BY_ID.pay_4.plain, next: false });
  assert.equal(v.bonuses[0].icon, pay.icons[0]);
  const drop = v.bonuses[4], dropE = ENDING_STARS.find((e) => e.id === 'drop');
  assert.equal(drop.kind, 'ending'); assert.equal(drop.icon, dropE.icon); assert.equal(drop.line, dropE.perk);
  assert.equal(drop.laneName, ASCENSION_LABELS.endingLane);
  assert.ok(v.next.every((b) => b.next === true));
  assert.ok(v.bonuses.every((b) => b.next === false));
  // every star icon is one the Sky uses for that lane; star 10 takes the fourth icon
  for (const l of LANES) for (const s of l.stars) {
    const q = P.createPrestige(); q.own[s.id] = 1;
    assert.equal(ascensionView({ prestige: q }).bonuses[0].icon, l.icons[Math.floor((s.n - 1) / 3)]);
  }
  assert.equal(STEPS, 10);
});

test('ascensionView: the Core, owned or in the bag', () => {
  const p = fullSky(P.createPrestige());
  p.depth = MAX_DEPTH; p.core = true;
  const v = ascensionView({ prestige: p });
  const last = v.bonuses[v.bonuses.length - 1];
  assert.equal(v.core, true);
  assert.deepEqual({ id: last.id, kind: last.kind, icon: last.icon, name: last.name, next: last.next }, { id: 'core', kind: 'core', icon: 'spiral', name: CORE.name, next: false });
  p.core = false; p.later.core = true;
  const w = ascensionView({ prestige: p });
  assert.equal(w.core, false);
  assert.deepEqual(w.next.map((b) => [b.id, b.kind, b.next]), [['core', 'core', true]]);
});

test('ascensionView never throws on a bad save', () => {
  const empty = { depth: 0, maxDepth: MAX_DEPTH, dust: 0, core: false, bonuses: [], next: [], counts: { stars: 0, endings: 0, next: 0 } };
  for (const bad of [undefined, null, 4, 'x', [], {}, { prestige: null }, { prestige: 'q' }, { prestige: { own: 5, lit: [1], later: 'z', depth: 'q' } }]) {
    assert.deepEqual(ascensionView(bad), empty);
  }
  const trap = { get prestige() { throw new Error('boom'); } };
  assert.deepEqual(ascensionView(trap), empty);
  const v = ascensionView({ prestige: { depth: 99, dust: -1, own: { zz_1: 1, pay_1: 1 }, lit: { nope: 1 } } });
  assert.equal(v.depth, MAX_DEPTH); assert.equal(v.dust, 0); assert.deepEqual(v.bonuses.map((b) => b.id), ['pay_1']);
});
