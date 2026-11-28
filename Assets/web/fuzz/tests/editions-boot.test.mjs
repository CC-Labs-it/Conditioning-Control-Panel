/* The small edition at boot (lane S2, 2026-11-22): the engine never mounts a sleeping row, a small run has no Renewal
 * (no stamp, no settle, no Sky entries, no renew), and a save that ended on the Expo (an open visit included) loads in
 * small as a plain ended run: nothing of the Expo mounts or resumes, and the save keeps the visit. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import * as P from '../sim/prestige.js';
import * as PE from '../sim/prestige-endings.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { createEngine } from '../ui/engine.js';
import { createJourney, buildPlan } from '../sim/journey.js';
import { allRoutes } from '../sim/route.js';
import { SMALL_CUT_IDS } from '../data/editions.js';

const small = Ed.readEdition({ caps: { edition: 'small' } }, { hostname: 'tc.cclabs.app', hosted: true });
const cut = new Set(SMALL_CUT_IDS);
const ui = (calls = {}) => ({ doc: {}, buttonFor: () => ({}), layer: () => ({}), credits: (id) => { calls.credits = id; }, ending: async () => { calls.fade = true; } });
const clone = (v) => JSON.parse(JSON.stringify(v));

function renewedLife() {
  const life = createLife();
  life.prestige = P.parsePrestige({ depth: 3, dust: 900, spent: 100, core: true, lit: { bi_end: 1 } });
  life.prestige.later.own = {};
  return life;
}

test('a fresh small run takes no Renewal stamp, settles nothing and owns nothing from the Sky', () => {
  const life = renewedLife();
  const before = clone(life.prestige);
  const run = E.createRun({ ed: small });
  const engine = createEngine({ run, life, ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.equal(run.prestige, undefined, 'no stamp');
  assert.equal(E.has(run, PE.BUBBLE_ROW), false, 'Bimbodoll stays asleep');
  assert.equal(engine.renew(), null, 'no renew in small');
  assert.deepEqual(clone(life.prestige), before, 'the life Renewal state is untouched');
  /* The same life in full still stamps: the full edition is unchanged. */
  const full = E.createRun();
  createEngine({ run: full, life: renewedLife(), ui: ui(), registry: {}, prestigeRegistry: {} });
  assert.ok(full.prestige, 'full takes the stamp');
});

test('a small engine never mounts a sleeping row or a Renewal entry', () => {
  const mounted = [];
  const entry = (id) => ({ mount: () => { mounted.push(id); return {}; } });
  const src = E.createRun();
  for (const id of ['swap', 'toy_box', 'quiz']) E.grant(src, id);
  const raw = serialize(src, createLife(), {});
  const back = parse(raw, { ed: small });
  const registry = Object.fromEntries([...back.run.order].map((id) => [id, entry(id)]));
  const prestigeRegistry = { pay: { always: true, mount: () => { mounted.push('prestige_pay'); return {}; } } };
  const engine = createEngine({ run: back.run, life: back.life, ui: ui(), registry, prestigeRegistry });
  engine.mountAll(true);
  for (const id of mounted) assert.equal(cut.has(id), false, `${id} mounted while asleep`);
  assert.equal(mounted.includes('prestige_pay'), false, 'no Renewal entry in small');
  assert.ok(mounted.includes('quiz') || mounted.includes('em_head'), 'kept rows still mount');
  assert.equal(engine.mount('swap'), false);
  assert.equal(engine.grant('swap').length, 0, 'the dev door refuses a cut row');
});

test('a save with an open ll_end visit loads in small as a plain ended run, and keeps the visit', async () => {
  const src = E.createRun();
  src.owned.ll_end = 1; src.order.push('ll_end');
  src.ended = { id: 'll_end', at: 1000 };
  src.store.ll_end = { visitOpen: true, keep: 'me' };
  const life = createLife();
  life.endings.ll_end = 1; life.runs = 1;
  const raw = serialize(src, life, {});
  const back = parse(raw, { ed: small });
  assert.equal(E.sleepingEnd(back.run), true, 'the one rule says: plain ended run');
  const touched = [];
  const registry = { ll_end: { resumeTake: true, showMaxMs: null, mount: () => { touched.push('mount'); return {}; },
    take: async () => { touched.push('take'); }, ending: async () => { touched.push('show'); } } };
  const calls = {};
  const engine = createEngine({ run: back.run, life: back.life, ui: ui(calls), registry, prestigeRegistry: {}, save: () => { touched.push('save'); } });
  await engine.end('ll_end', { restored: true });
  assert.deepEqual(touched, [], 'nothing of the Expo ran, nothing saved over it');
  assert.equal(calls.credits, 'll_end', 'the plain credits card shows');
  assert.equal(engine.stopped, true);
  assert.equal(back.run.store.ll_end.visitOpen, true, 'the visit is kept');
  assert.equal(back.life.endings.ll_end, 1, 'nothing recorded again');
  const full = parse(serialize(back.run, back.life, back.extras));
  assert.deepEqual(full.run.ended, src.ended);
  assert.deepEqual(full.run.store.ll_end, { visitOpen: true, keep: 'me' });
  assert.equal(E.sleepingEnd(full.run), false, 'awake again in full');
});

test('the small Journey plan never names a cut row and ends on its ending', () => {
  const run = E.createRun({ ed: small });
  E.grant(run, 'f1');
  const j = createJourney(run);
  for (const e of j.peek ? j.peek(400) : []) assert.equal(cut.has(e.item?.id), false, `${e.item?.id} planned`);
  const shop = j.shop(8);
  for (const it of [shop.goal, ...(shop.rows || [])].filter(Boolean)) assert.equal(cut.has(it.id), false, `${it.id} on the shelf`);
  for (const picks of allRoutes().filter((_, i) => i % 32 === 0)) {
    const plan = buildPlan(picks, undefined, 1, undefined, small);
    for (const e of plan) assert.equal(cut.has(e.item.id), false, `${e.item.id} planned`);
    assert.equal(plan.at(-1).item.kind, 'end', `${Object.values(picks).join('/')} ends on an ending`);
  }
});
