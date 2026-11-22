/* The small edition in the sim (lane S, 2026-11-22; CONTRACT.md section 6, data/editions.js): a cut row is never
 * offered, bought or granted, requirements go through reqFor, fold riders through keptIds, and SLEEP: a save that owns
 * cut rows or Renewal state loads in small with those asleep and is written back unchanged. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ITEMS, BY_ID } from '../data/items.js';
import { SMALL_CUT_IDS } from '../data/editions.js';
import { CONFIG } from '../data/config.js';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { parsePrestige, parseStamp } from '../sim/prestige.js';
import { allRoutes, playRoute } from '../sim/route.js';

const small = Ed.readEdition({ caps: { edition: 'small' } }, { hostname: 'tc.cclabs.app', hosted: true });
const full = Ed.readEdition({ caps: { edition: 'full', platform: 'desktop' } }, { hostname: 'ccp.game', hosted: true });
const cut = new Set(SMALL_CUT_IDS);
const runRows = ITEMS.filter((it) => it.group === 'run' && it.id !== 'start');
const cutOwned = (s) => Object.keys(s.owned).filter((id) => cut.has(id));

test('a full reading or none leaves the run as it always was', () => {
  assert.equal(E.runEdition(E.createRun()), null);
  assert.equal(E.runEdition(E.createRun({ ed: full })), null);
  assert.equal(E.runEdition(E.createRun({ ed: small })), small);
  const s = E.createRun({ ed: small });
  assert.equal(JSON.stringify(s), JSON.stringify(E.createRun()), 'the reading never reaches the save');
  assert.equal(E.runEdition({ ...s }), small, 'a spread copy (awayRate) keeps it');
});

test('in small no cut row is ever granted, opened or bought, and every kept row can be granted', () => {
  for (const it of runRows) {
    const s = E.createRun({ ed: small });
    const added = E.grant(s, it.id);
    assert.deepEqual(cutOwned(s), [], `granting ${it.id} brought a cut row`);
    if (cut.has(it.id)) {
      assert.deepEqual(added, [], `${it.id} is cut and was granted`);
      continue;
    }
    for (const o of E.openItems(s)) assert.equal(cut.has(o.id), false, `${o.id} offered after ${it.id}`);
  }
  const s = E.createRun({ ed: small });
  E.lump(s, 1e30);
  for (const id of ['swap', 'toy_box', 'buzz']) assert.equal(E.buy(s, id).ok, false, `${id} bought in small`);
});

test('in small requirements read through reqFor and riders never bring a cut row', () => {
  for (const it of runRows) {
    if (cut.has(it.id)) continue;
    const want = Ed.reqFor(it, small);
    for (const q of [...want.req, ...(want.any || [])]) assert.equal(cut.has(q), false, `${it.id} needs cut ${q}`);
    const s = E.createRun({ ed: small });
    for (const r of E.ridersFor(s, it.id)) assert.equal(cut.has(r), false, `${it.id} would bring cut rider ${r}`);
  }
  /* The plan's three re-links: a kept row whose parent is cut opens on that parent's own parents. */
  for (const id of ['nag', 'onlyyes', 'em_nap']) {
    const s = E.createRun({ ed: small });
    E.grant(s, id);
    assert.ok(E.has(s, id), `${id} can be had in small`);
  }
});

test('the full rules are unchanged: a cut row is still offered and granted in full', () => {
  for (const id of ['swap', 'toy_box', 'em_jeal']) {
    const s = E.createRun({ ed: full });
    assert.ok(E.grant(s, id).includes(id), `${id} granted in full`);
    assert.ok(E.has(s, id));
  }
});

function fullSave() {
  const stamp = parseStamp({ depth: 2, pay: 1.5, price: 1.2, minutes: 40 });
  const s = E.createRun({ prestige: stamp, seed: 7 });
  for (const id of ['toy_box', 'swap', 'em_jeal', 'inbox', 'buzz', 'x_dream', 'dispenser', 'mini_breakout']) E.grant(s, id);
  E.lump(s, 12345);
  E.tick(s, 30000);
  s.store.toy_box = { kept: 1 };
  s.off.dispenser = 1;
  const life = createLife();
  life.prestige = parsePrestige({ depth: 2, dust: 500, spent: 40, core: true });
  life.seen.swap = 1;
  life.endings.ll_end = 1;
  return { s, life, raw: serialize(s, life, { sky: null, keep: 'me' }) };
}

test('SLEEP: a full save loads in small with cut rows and Renewal asleep', () => {
  const { s, raw } = fullSave();
  const owned = cutOwned(s);
  assert.ok(owned.length >= 4, `the fixture owns cut rows (${owned})`);
  const p = parse(raw, { ed: small });
  for (const id of owned) {
    assert.equal(p.run.owned[id], s.owned[id], `${id} kept in owned`);
    assert.equal(E.has(p.run, id), false, `${id} asleep: not owned to the sim`);
    assert.equal(E.count(p.run, id), 0);
  }
  assert.equal(p.run.prestige, undefined, 'the Renewal stamp sleeps');
  assert.ok(Math.abs(p.run.pace - s.pace / 1.5) < 1e-9, 'pace plays without the stamp pay');
  const awake = owned.reduce((a, id) => a + (BY_ID[id].fps || 0), 0);
  if (awake > 0) assert.ok(p.run.base.fps < s.base.fps, 'sleeping rows pay nothing');
  for (const o of E.openItems(p.run)) assert.equal(cut.has(o.id), false, `${o.id} offered from a sleeping save`);
});

test('SLEEP round trip: full save, played in small, saved, loaded in full = every cut row and Renewal field intact', () => {
  const { s, life, raw } = fullSave();
  const before = parse(raw);
  const p = parse(raw, { ed: small });
  /* Play a little in small: time, income, a purchase. */
  E.tick(p.run, 60000);
  E.lump(p.run, 1e12);
  const pick = E.openItems(p.run).find((it) => it.kind !== 'fork' && it.kind !== 'end');
  assert.ok(pick, 'something is on offer');
  assert.equal(E.buy(p.run, pick.id).ok, true);
  const back = parse(serialize(p.run, p.life, p.extras));
  for (const id of cutOwned(s)) assert.equal(back.run.owned[id], s.owned[id], `${id} survived`);
  for (const id of cutOwned(s)) assert.ok(back.run.order.includes(id), `${id} kept its place in order`);
  assert.deepEqual(back.run.prestige, before.run.prestige, 'the run stamp came back');
  assert.equal(back.run.pace, before.run.pace, 'the saved pace came back exactly');
  assert.deepEqual(back.run.store.toy_box, { kept: 1 });
  assert.equal(back.run.off.dispenser, 1);
  const sleepingToys = Object.keys(before.run.toys).filter((id) => cut.has(id));
  assert.ok(sleepingToys.length > 0, 'the fixture has a sleeping toy budget');
  for (const id of sleepingToys) assert.deepEqual(back.run.toys[id], before.run.toys[id], `${id} toy budget kept asleep`);
  assert.deepEqual(back.life.prestige, before.life.prestige, 'life Renewal state untouched');
  assert.deepEqual(back.life.prestige, parsePrestige(life.prestige));
  assert.equal(back.life.endings.ll_end, 1);
  assert.equal(back.life.seen.swap, 1);
  assert.deepEqual(back.extras, { sky: null, keep: 'me' });
  assert.ok(back.run.owned[pick.id] > 0, 'what was bought in small is kept');
  assert.ok(E.has(back.run, 'swap'), 'awake again in full');
});

test('the small route never owns a cut row and still reaches its ending', () => {
  const real = { side: true, mods: [{ fps: (v) => v * CONFIG.MOD_FPS_KNEE, click: (v) => v * CONFIG.MOD_CLICK_KNEE }] };
  for (const p of allRoutes().filter((_, i) => i % 16 === 0)) {
    const r = playRoute(p, { ...real, ed: small });
    assert.deepEqual(cutOwned(r.state), [], Object.values(p).join('/'));
    assert.equal(r.stuck, false, `${Object.values(p).join('/')} got stuck at ${(r.end / 60).toFixed(1)} min`);
  }
});
