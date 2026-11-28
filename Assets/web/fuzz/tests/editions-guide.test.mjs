/* The Guide map in the small edition (lane S3, 2026-11-22): the road, the world map and the seals never draw or list a cut
 * row, a sleeping row a save owns included; an island whose every row is cut is gone; the full edition is unchanged. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { SMALL_CUT_IDS } from '../data/editions.js';
import { BY_ID } from '../data/items.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { buildRoad, sealsOf } from '../ui/guide-road-model.js';
import { islandGone, layoutWorld, SHOP_BY_ID, worldStates } from '../ui/guide-world-model.js';

const small = Ed.readEdition({ caps: { edition: 'small' } }, { hostname: 'tc.cclabs.app', hosted: true });
const cut = new Set(SMALL_CUT_IDS);

/** A small run from a full save that owns cut rows (they sleep), with a few kept rows bought. */
function sleepingRun() {
  const s = E.createRun({ herd: 12 });
  for (const id of ['toy_box', 'swap', 'em_jeal', 'quiz', 'mini_slots']) E.grant(s, id);
  return parse(serialize(s, createLife(), {}), { ed: small }).run;
}

function roadIds(road) {
  const ids = [];
  for (const ch of road.chapters) {
    for (const r of ch.road) if (r.ids) ids.push(...r.ids);
    for (const list of Object.values(ch.pockets)) ids.push(...list);
    for (const [lead, riders] of Object.entries(ch.riders)) ids.push(lead, ...riders);
    ids.push(...ch.seals);
  }
  return ids;
}

test('the small road never draws or lists a cut row, owned or not', () => {
  for (const run of [E.createRun({ ed: small, herd: 12 }), sleepingRun()]) {
    const ids = roadIds(buildRoad(run));
    assert.ok(ids.length > 20, 'the road has rows');
    for (const id of ids) assert.equal(cut.has(id), false, `${id} on the small road`);
    assert.equal(sealsOf(run).includes('ll_end'), false, 'the Expo ending is not a goal in small');
  }
  const full = roadIds(buildRoad(E.createRun({ herd: 12 })));
  assert.ok(full.some((id) => cut.has(id)), 'the full road still shows them');
});

test('the small world map marks every cut row gone and hides the crossover island', () => {
  const world = layoutWorld();
  const run = sleepingRun();
  const view = worldStates(run, world);
  for (const n of world.nodes) {
    const item = n.shop ? SHOP_BY_ID[n.id]?.item : n.id;
    assert.equal(view.states[n.id] === 'gone', cut.has(item), `${n.id} gone only when cut`);
  }
  assert.equal(view.counts.gone, undefined, 'gone rows are not counted');
  assert.equal(cut.has(view.here), false);
  assert.equal(cut.has(view.next), false);
  const combo = world.islands.find((i) => i.id === 'combo');
  assert.ok(combo, 'the crossover island exists in the layout');
  assert.equal(islandGone(view, world, combo), true);
  for (const i of world.islands) if (i.id !== 'combo') assert.equal(islandGone(view, world, i), world.nodes.filter((n) => n.lane === i.id).every((n) => cut.has(n.id)), i.id);
  /* Full: nothing is gone. */
  const fullView = worldStates(E.createRun(), world);
  assert.equal(Object.values(fullView.states).includes('gone'), false);
  for (const i of world.islands) assert.equal(islandGone(fullView, world, i), false, i.id);
});

test('needs read through the edition: a kept row whose parent is cut lists the parent it really waits for', () => {
  for (const id of ['nag', 'onlyyes', 'em_nap']) {
    const run = E.createRun({ ed: small });
    const n = E.needsOf(run, BY_ID[id]);
    assert.deepEqual(n, Ed.reqFor(id, small));
    for (const q of [...n.req, ...(n.any || [])]) assert.equal(cut.has(q), false, `${id} lists cut ${q}`);
    assert.ok(BY_ID[id].req.some((q) => cut.has(q)), `${id} has a cut parent in the catalog`);
    assert.equal(E.needsOf(E.createRun(), BY_ID[id]), BY_ID[id], 'full reads the row itself');
  }
});
