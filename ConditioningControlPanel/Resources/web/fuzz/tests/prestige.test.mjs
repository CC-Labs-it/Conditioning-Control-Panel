// Renewal Sky core: sim/prestige.js rules, the life + run saves, the Journey timing per Depth, the engine's prestige mounts.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as P from '../sim/prestige.js';
import * as E from '../sim/economy.js';
import { createJourney, planTotal } from '../sim/journey.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { createEngine } from '../ui/engine.js';
import { LANES, STAR_BY_ID, T_RUN, P_RUN, MAX_DEPTH, CORE, PRESTIGE_CURRENCY } from '../data/prestige.js';
import { allRoutes } from '../sim/route.js';
import { measureJourney } from '../dev/journey-timing.mjs';

const own = (p, lane, n) => { for (let k = 1; k <= n; k++) p.own[`${lane}_${k}`] = 1; return p; };
const ui = () => ({ doc: {}, buttonFor: () => ({}), layer: () => ({}) });

test('createPrestige has the contract shape, and the currency name lives in one constant', () => {
  const p = P.createPrestige();
  assert.deepEqual({ ...p, store: undefined }, { depth: 0, dust: 0, spent: 0, own: {}, lit: {}, core: false, next: { rules: [], words: [], keep: [] }, store: undefined, last: null });
  for (const l of LANES) assert.deepEqual(p.store[l.id], {});
  assert.equal(PRESTIGE_CURRENCY, 'Stardust');
});

test('parsePrestige never throws, drops unknown ids and clamps numbers', () => {
  for (const bad of [null, 5, 'x', [], { depth: 'q', own: 3, next: 7, store: [] }]) assert.equal(P.parsePrestige(bad).depth, 0);
  const p = P.parsePrestige({
    depth: 99, dust: -4, spent: 1.5, own: { pay_1: 1, nope_3: 1 }, lit: { drop: 1, zz: 1 }, core: 1,
    next: { rules: ['silent', 'silent', 'bogus'], words: ['drop', 'banana', 'sink', 'soft', 'yes'], keep: ['wheel', 'start'] },
    store: { ghost: { runs: [1] }, nolane: { a: 1 }, trig: [] }, last: { route: { f1: 'eyes', f9: 'x', f2: 'nah' }, toys: ['toy_box', 'stray'], ms: -3 },
  });
  assert.equal(p.depth, MAX_DEPTH); assert.equal(p.dust, 0); assert.equal(p.spent, 1);
  assert.deepEqual(p.own, { pay_1: 1 }); assert.deepEqual(p.lit, { drop: 1 }); assert.equal(p.core, true);
  assert.deepEqual(p.next, { rules: ['silent'], words: ['drop', 'sink', 'soft'], keep: ['wheel'] });
  assert.deepEqual(p.store.ghost, { runs: [1] }); assert.equal(p.store.nolane, undefined); assert.deepEqual(p.store.trig, {});
  assert.deepEqual(p.last, { route: { f1: 'eyes' }, toys: ['toy_box'], ms: 0 });
});

test('stars: depth, the previous star and dust gate a buy; level counts from the tip', () => {
  const p = P.createPrestige();
  assert.equal(P.canBuy(p, 'pay_1').reason, 'depth');
  p.depth = 2; p.dust = 1e6;
  assert.equal(P.canBuy(p, 'pay_2').reason, 'previous');
  assert.equal(P.canBuy(p, 'zz_1').reason, 'unknown');
  assert.equal(P.buyStar(p, 'pay_1').ok, true);
  assert.equal(P.canBuy(p, 'pay_1').reason, 'owned');
  assert.equal(P.buyStar(p, 'pay_2').ok, true);
  assert.equal(P.canBuy(p, 'pay_3').reason, 'depth');
  assert.equal(P.level(p, 'pay'), 2); assert.equal(P.has(p, 'pay_2'), true);
  assert.equal(p.dust + p.spent, 1e6);
  p.dust = 0;
  p.depth = 3;
  assert.equal(P.canBuy(p, 'pay_3').reason, 'dust');
});

test('the core opens at Depth 10 with every lane full', () => {
  const p = P.createPrestige();
  p.depth = MAX_DEPTH; p.dust = CORE.cost;
  assert.equal(P.canCore(p).reason, 'lanes');
  for (const l of LANES) own(p, l.id, 10);
  assert.equal(P.buyCore(p).ok, true);
  assert.equal(p.core, true); assert.equal(p.dust, 0);
  assert.equal(P.runMods(p).minutes, 45);
});

test('Fine Print: open rules, slots, multipliers, Hush Money and Full Contract', () => {
  const p = P.createPrestige();
  assert.equal(P.fineSlots(p), 0); assert.equal(P.fineMult(['silent'], p), 1);
  own(p, 'fine', 1);
  assert.deepEqual(P.rulesOpen(p), ['silent']); assert.equal(P.fineSlots(p), 1);
  assert.equal(P.fineMult(['silent', 'no_hands'], p), 1.2);
  own(p, 'fine', 4);
  assert.equal(P.fineSlots(p), 2);
  assert.ok(Math.abs(P.fineMult(['silent', 'no_hands', 'blank'], p) - 1.8) < 1e-9, 'two slots: the third rule does not count');
  own(p, 'fine', 6);
  assert.equal(P.fineMult(['silent'], p), 1.5);
  own(p, 'fine', 10);
  assert.equal(P.fineMult(['silent', 'no_hands', 'blank', 'no_toys', 'speedrun', 'one_theme'], p), 5);
});

test('runMods: Depth 0 is exactly today; prices carry the stars and their boost speed, Drop adds pay only', () => {
  const p = P.createPrestige();
  assert.deepEqual(P.runMods(p), { payMult: 1, priceMult: 1, minutes: 90 });
  p.lit.drop = 1;
  assert.deepEqual(P.runMods(p), { payMult: 1.05, priceMult: 1, minutes: 90 }, 'an ending alone never moves prices');
  p.depth = 4; own(p, 'pay', 2); own(p, 'idle', 1);
  const m = P.runMods(p);
  assert.equal(m.minutes, T_RUN[4]);
  assert.ok(Math.abs(m.payMult - 1.15 ** 2 * 1.08 * 1.05) < 1e-9, 'pay is the star multiplier (the pill), the entries pay the rest');
  assert.ok(m.priceMult > P.starMult(p) * P.boostSpeed(p), 'behind the Depth 4 sky: prices run ahead of what is owned');
  assert.ok(m.priceMult <= P.starMult(p) * P.boostSpeed(p) * T_RUN[0] / T_RUN[4] + 1e-9, 'never slower than the first run');
  const q = own(own(own(own(P.createPrestige(), 'pay', 4), 'idle', 4), 'active', 4), 'luck', 4);
  for (const l of ['head', 'trig', 'ghost', 'fine']) own(q, l, 4);
  q.depth = 4;
  assert.ok(Math.abs(P.runMods(q).priceMult - P.refPrice(4)) < 1e-6, 'the reference sky prices at exactly refPrice');
  for (let d = 1; d <= MAX_DEPTH; d++) assert.ok(P.refPrice(d) > P.refPrice(d - 1), `refPrice rises at ${d}`);
});

/* Pacing pinned loosely: dev/prestige-measure.mjs is the tool, these are its corners (route 0, the staying player). */
test('pacing: Depth 0 stays today, a full sky stays near 45 and never under 40, the curve roughly follows T_RUN', async () => {
  const { measureRun, buyCheapest } = await import('../dev/prestige-measure.mjs');
  const r0 = allRoutes()[0];
  assert.equal(measureRun(r0, P.createPrestige()).minutes, measureJourney(r0).minutes, 'the first run is byte for byte journey-timing');
  const full = P.createPrestige(); full.depth = MAX_DEPTH; full.core = true; full.lit.drop = 1;
  for (const l of LANES) own(full, l.id, 10);
  const fm = measureRun(r0, full).minutes;
  assert.ok(fm >= 40 && fm <= 50, `full sky ${fm.toFixed(1)} min`);
  const p = P.createPrestige();
  for (let d = 0; d < 6; d++) {
    const res = measureRun(r0, p);
    if (d) assert.ok(Math.abs(res.minutes / T_RUN[d] - 1) < 0.15, `depth ${d}: ${res.minutes.toFixed(1)} vs ${T_RUN[d]}`);
    res.run.ended = { id: 'drop', at: res.run.clock };
    const { dust } = P.renew({ prestige: p }, res.run);
    assert.ok(Math.abs(dust / P_RUN[d] - 1) < 0.2, `dust at ${d}: ${dust} vs ${P_RUN[d]}`);
    buyCheapest(p);
  }
  const bare = P.createPrestige(); bare.depth = MAX_DEPTH;
  assert.ok(P.runMods(bare).priceMult <= T_RUN[0] / T_RUN[MAX_DEPTH - 1] + 1e-9, 'no stars at all: never slower than the first run');
});

test('Head Start stamp: money, free buys, skip, pre-picks, keepsakes from last run only', () => {
  const p = P.createPrestige();
  p.depth = 10;
  p.last = { route: { f1: 'ears', f5: 'goon' }, toys: ['wheel', 'toy_box'], ms: 1 };
  p.next.keep = ['wheel', 'toy_box'];
  assert.deepEqual([P.runStamp(p).start, P.runStamp(p).free, P.runStamp(p).keep, P.runStamp(p).prepick], [0, 0, [], {}]);
  own(p, 'head', 4);
  let st = P.runStamp(p);
  assert.deepEqual([st.start, st.free, st.prepick, st.keep], [10000, 3, { f1: 'ears' }, []]);
  own(p, 'head', 8);
  st = P.runStamp(p);
  assert.deepEqual([st.start, st.free, st.keep, st.skip], [100000, 6, ['wheel', 'toy_box'], false]);
  own(p, 'head', 10);
  st = P.runStamp(p);
  assert.deepEqual([st.skip, st.prepick, st.freeChange], [true, { f1: 'ears', f5: 'goon' }, true]);
});

test('saves: a life without prestige loads as Depth 0; prestige and the run stamp round-trip; pace is not multiplied twice', () => {
  const old = parse(JSON.stringify({ v: 2, run: { pace: 3, clock: 5 }, life: { runs: 2 } }));
  assert.equal(old.life.prestige.depth, 0); assert.equal(old.run.prestige, undefined); assert.equal(old.run.pace, 3);
  const life = createLife();
  life.prestige.depth = 3; life.prestige.dust = 50; own(life.prestige, 'trig', 2); life.prestige.store.trig.grow = 7;
  const st = P.runStamp({ ...life.prestige, depth: 3 });
  st.pay = 2.5; st.free = 2;
  const run = E.createRun({ pace: 4, prestige: st });
  assert.equal(run.pace, 10);
  const back = parse(serialize(run, life, {}));
  assert.equal(back.run.pace, 10); assert.equal(back.run.prestige.free, 2); assert.equal(back.run.prestige.depth, 3);
  assert.equal(back.life.prestige.depth, 3); assert.equal(back.life.prestige.store.trig.grow, 7); assert.equal(P.level(back.life.prestige, 'trig'), 2);
});

test('createRun without a stamp is unchanged', () => {
  assert.equal('prestige' in E.createRun(), false);
  assert.equal(E.createRun({ pace: 7 }).pace, 7);
});

test('Journey at Depth 0 with a stamp prices exactly like today', () => {
  const a = E.createRun(), b = E.createRun({ prestige: P.runStamp(P.createPrestige()) });
  const ja = createJourney(a), jb = createJourney(b);
  const rows = (j) => { const s = j.shop(8); return [s.goal, ...s.rows].map((it) => [it.id, j.price(it.id)]); };
  assert.deepEqual(rows(jb), rows(ja));
  assert.equal(planTotal(90), planTotal());
});

/* A trimmed copy of dev/journey-timing.mjs's staying player (4 taps a second), on a stamped run. */
function minutesFor(stamp, picks) {
  const s = E.createRun({ prestige: stamp }), shop = createJourney(s);
  const mods = [{ fps: (v, s) => (s.owned.depth ? v * 1.1 : v), click: (v, s) => (s.owned.f1 ? v * 1.5 : v) }];
  while (!s.ended && s.clock < 8 * 3600e3) {
    const sh = shop.shop(8);
    const items = [sh.goal, ...sh.rows].filter((it) => it && (it.kind !== 'end' || it.id === 'drop'));
    const ready = items.filter((it) => (it.kind === 'fork' ? s.total : s.fuzz) + 1e-8 >= shop.price(it.id))
      .sort((a, b) => Number(b.kind === 'fork') - Number(a.kind === 'fork') || shop.price(a.id) - shop.price(b.id));
    if (ready[0]) {
      const it = ready[0];
      const res = it.kind === 'fork' ? shop.choose(it.id, picks[it.id], mods) : shop.buy(it.id, it.options?.[0]?.[0], mods);
      if (res.ok) continue;
    }
    const r = E.rates(s, mods);
    const active = (r.manualClick * 4 + E.count(s, 'stray') * r.walls * r.bounce) * s.pace;
    const side = s.owned.gold ? (active + r.fps * s.pace) * 0.125 : 0;
    const rate = active + r.fps * s.pace + side;
    const missing = Math.min(...items.map((it) => shop.price(it.id) - (it.kind === 'fork' ? s.total : s.fuzz)));
    const dt = Math.max(0.01, Math.min(60, (missing + 0.001) / Math.max(rate, 0.01)));
    E.tick(s, dt * 1000, mods);
    E.earn(s, (active + side) * dt, 'active');
  }
  return { minutes: s.clock / 60000, s };
}

test('run length follows T_RUN[depth], and the run multiplier never makes a run moot', () => {
  const picks = allRoutes()[0];
  const base = minutesFor(P.runStamp(P.createPrestige()), picks).minutes;
  const p = P.createPrestige();
  p.depth = 5; own(p, 'pay', 5); own(p, 'active', 3);
  const st = P.runStamp(p);
  assert.ok(st.pay > 2);
  // The stamp alone (no lane entries mounted): prices carry the boost speed the entries pay, so the run is that much longer.
  const r = minutesFor(st, picks);
  const want = base * T_RUN[5] / T_RUN[0] * st.price / st.pay;
  assert.ok(Math.abs(r.minutes - want) / want < 0.12, `${r.minutes.toFixed(1)} min vs ${want.toFixed(1)}`);
  assert.equal(r.s.ended?.id, 'drop');
});

test('dustFor: a normal finished run earns about P_RUN[depth], times the Fine Print factor', () => {
  const r = measureJourney(allRoutes()[0], { taps: 4, share: 0.125 });
  const p = P.createPrestige();
  const got = P.dustFor(r.s, p);
  assert.ok(Math.abs(got - P_RUN[0]) / P_RUN[0] < 0.1, `${got} vs ${P_RUN[0]}`);
  own(p, 'fine', 1);
  r.s.prestige = { ...P.runStamp(p), rules: ['silent'] };
  assert.equal(P.dustFor(r.s, p), Math.round(got * 1.2) || got);
  r.s.prestige.gift = r.s.total;
  assert.equal(P.dustFor(r.s, p), 0, 'money the run was handed is not score');
  for (let d = 1; d < 10; d++) {
    const q = P.createPrestige(); q.depth = d;
    const stamp = P.runStamp(q);
    const ref = { total: planTotal(stamp.minutes) * stamp.price, prestige: stamp };
    const want = P_RUN[d] * Math.max(0.6, Math.sqrt(1 / P.expectMult(d)));
    assert.ok(Math.abs(P.dustFor(ref, q) - want) / want < 0.02, `depth ${d}`);
  }
});

test('renew: dust, one Depth (max 10), the ending lit, last run remembered, never twice', () => {
  const life = createLife();
  const run = E.createRun();
  run.total = planTotal(90); run.clock = 5400000; run.picks = { f1: 'ears' }; run.owned.wheel = 1; run.ended = { id: 'drop', at: 1 };
  life.prestige.next.keep = ['toy_box'];
  const r = P.renew(life, run);
  assert.equal(r.depth, 1); assert.ok(r.dust > 60);
  assert.equal(life.prestige.dust, r.dust); assert.equal(life.prestige.lit.drop, 1);
  assert.deepEqual(life.prestige.last, { route: { f1: 'ears' }, toys: ['wheel'], ms: 5400000 });
  assert.deepEqual(life.prestige.next.keep, []);
  assert.deepEqual(P.renew(life, run), { dust: 0, depth: 1 });
  life.prestige.depth = MAX_DEPTH;
  assert.equal(P.renew(life, E.createRun()).depth, MAX_DEPTH);
});

test('engine: prestige entries mount with ctx.prestige and ctx.pstore when the lane has a star, or always', () => {
  const life = createLife();
  life.prestige.depth = 2; own(life.prestige, 'pay', 1);
  const seen = {};
  const reg = {
    pay: { mount(ctx) { seen.pay = { depth: ctx.prestige.depth, lvl: ctx.prestige.level('pay'), has: ctx.prestige.has('pay_1') }; ctx.pstore.n = (ctx.pstore.n || 0) + 1; } },
    idle: { mount() { seen.idle = true; } },
    ghost: { always: true, mount(ctx) { seen.ghost = ctx.pstore; } },
  };
  const g = createEngine({ run: E.createRun(), life, ui: ui(), registry: {}, prestigeRegistry: reg });
  g.mountAll();
  assert.deepEqual(seen.pay, { depth: 2, lvl: 1, has: true });
  assert.equal(seen.idle, undefined);
  assert.equal(seen.ghost, life.prestige.store.ghost);
  assert.equal(life.prestige.store.pay.n, 1);
  assert.equal(g.run.prestige.depth, 2);
});

test('engine: start money, free first buys, keepsakes and the Lifetime Plan wheel on a fresh run; a saved run keeps its stamp', () => {
  const life = createLife();
  const p = life.prestige;
  p.depth = 8; own(p, 'head', 8); p.lit.p_life = 1;
  p.last = { route: {}, toys: ['toy_box'], ms: 1 };
  p.next.keep = ['toy_box'];
  const run = E.createRun();
  const g = createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(run.fuzz, 100000); assert.equal(run.prestige.gift, 100000);
  assert.ok(run.owned.toy_box && run.owned.wheel);
  assert.equal(g.freeBuys, 6);
  const goal = g.shop(6).goal;
  assert.equal(g.price(goal.id), 0);
  const before = run.fuzz;
  assert.equal(g.buy(goal.id).ok, true);
  assert.equal(run.fuzz, before);
  assert.equal(g.freeBuys, 5);
  // A run from a save never takes a new stamp.
  const again = createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(run.fuzz, before); assert.equal(again.freeBuys, 5);
});

test('engine: Skip Ahead buys chapter 1 in the Journey', () => {
  const life = createLife();
  life.prestige.depth = 9; own(life.prestige, 'head', 9);
  life.prestige.last = { route: { f1: 'ears', f5: 'goon' }, toys: [], ms: 1 };
  const run = E.createRun();
  createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {}, env: { journey: true } });
  assert.ok(run.buys >= 14, `buys ${run.buys}`);
  assert.equal(run.picks.f1, 'ears');
  assert.equal(run.picks.f5, undefined, 'Pick a Theme stays with the player (its reveal is a show)');
  assert.ok(run.prestige.gift >= 100000);
});

test('star data stays in step with the rules', () => {
  for (const l of LANES) for (const s of l.stars) assert.equal(STAR_BY_ID[s.id].depth, s.n);
});
