import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, FORKS, ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';
import { buildRoad, CHAPTER_ENDS, chapterEnds, laneHead, POCKETS, purchasesOf, sealsOf, statusOf } from '../ui/guide-road-model.js';

const stampsOf = (ch) => [...ch.road.flatMap((r) => r.ids || []), ...Object.values(ch.pockets).flat()];

/** Play a run the cheap way: buy the cheapest open row, take the first (or a chosen) option at each fork. */
function play(n, choose = {}) {
  const s = E.createRun();
  s.fuzz = 1e15;
  for (let i = 0; i < n; i++) {
    const o = E.openItems(s).find((it) => it.kind !== 'end');
    if (!o) break;
    if (o.kind === 'fork') E.choose(s, o.id, choose[o.id] || Object.keys(FORKS[o.id].opts)[0], null, 0);
    else E.buy(s, o.id);
  }
  return s;
}

test('a fresh run: seven chapters, the first open, the rest folded to a count, the main ending sealed last', () => {
  const r = buildRoad(E.createRun());
  assert.equal(r.chapters.length, CHAPTER_ENDS.length);
  assert.equal(r.open, 1);
  assert.deepEqual(r.chapters.map((c) => c.state), ['open', 'future', 'future', 'future', 'future', 'future', 'future']);
  assert.ok(r.chapters.slice(1).every((c) => c.count > 0), 'every later chapter has something to count');
  assert.ok(r.chapters.at(-1).seals.includes('drop'));
  assert.equal(r.next, 'stray', 'the first thing on offer carries the next tag');
  assert.ok(r.total > 40);
});

test('every projected stamp is a real row, never a start, a mood, an After row or an ending', () => {
  const r = buildRoad(play(30));
  for (const ch of r.chapters) for (const id of stampsOf(ch)) {
    const it = BY_ID[id];
    assert.ok(it, id);
    assert.equal(it.group, 'run', id);
    assert.notEqual(it.kind, 'end', id);
    assert.notEqual(id, 'start');
  }
});

test('the picked route is the road: no stamp from a lane not picked, and its signpost names the pick', () => {
  const s = play(60, { f1: 'ears' });
  assert.equal(s.picks.f1, 'ears');
  const r = buildRoad(s);
  const all = r.chapters.flatMap(stampsOf);
  assert.ok(!all.some((id) => BY_ID[id].lane === 'eyes'), 'Watch rows stay off a Listen road');
  assert.ok(all.some((id) => BY_ID[id].lane === 'ears'), 'Listen rows are on it');
  const sign = r.chapters.flatMap((c) => c.road).find((x) => x.fork === 'f1');
  assert.equal(sign.pick, 'ears');
});

test('side lanes ride as pockets, never on the road', () => {
  const r = buildRoad(play(50));
  for (const ch of r.chapters) {
    for (const row of ch.road) for (const id of row.ids || []) assert.ok(!POCKETS[BY_ID[id].lane], `${id} belongs in a pocket`);
    for (const [lane, ids] of Object.entries(ch.pockets)) for (const id of ids) assert.equal(BY_ID[id].lane, lane);
  }
});

test('what is owned sits in a past or the open chapter, the open chapter follows the purchases made', () => {
  const s = play(45);
  const r = buildRoad(s);
  const made = purchasesOf(s);
  const ends = chapterEnds(s, r.total);
  assert.equal(r.open, ends.findIndex((e) => made < e) + 1);
  for (const ch of r.chapters) {
    const owned = stampsOf(ch).filter((id) => E.has(s, id));
    if (ch.state === 'future') assert.equal(owned.length, 0, `chapter ${ch.n} is folded`);
  }
  for (const ch of r.chapters.filter((c) => c.state === 'past')) assert.equal(ch.made, ch.count, `past chapter ${ch.n} is all bought`);
});

test('a stamp shows at most once on the whole road, and riders ride inside their lead', () => {
  const r = buildRoad(play(70));
  const all = r.chapters.flatMap(stampsOf);
  assert.equal(new Set(all).size, all.length);
  for (const ch of r.chapters) for (const [lead, riders] of Object.entries(ch.riders)) for (const id of riders) assert.ok(!all.includes(id), `${id} rides in ${lead}`);
});

test('rows: the main path walks alone, at most two side by side, never two of one lane', () => {
  const r = buildRoad(play(40));
  for (const ch of r.chapters) for (const row of ch.road) {
    if (!row.ids) continue;
    assert.ok(row.ids.length <= 2);
    if (row.ids.length === 2) {
      assert.notEqual(BY_ID[row.ids[0]].lane, BY_ID[row.ids[1]].lane);
      assert.ok(row.ids.every((id) => BY_ID[id].lane !== 'trunk'));
    }
  }
});

test('the Journey keeps its own chapter numbers; a standard run stretches them to its length', () => {
  const s = E.createRun();
  assert.deepEqual(chapterEnds({ ...s, store: { journey: {} } }, 90), CHAPTER_ENDS);
  const ends = chapterEnds(s, 90);
  assert.equal(ends.at(-1), 90);
  for (let i = 1; i < ends.length; i++) assert.ok(ends[i] >= ends[i - 1]);
});

test('endings: only the ones this route can reach; a picked theme brings its own', () => {
  const s = play(20, { f5: 'bimbo' });
  const seals = sealsOf(s);
  assert.ok(seals.includes('drop'));
  if (s.picks.f5 === 'bimbo') assert.ok(seals.includes('bi_end'));
  assert.ok(!seals.includes('dr_end'));
});

test('every fork lane has a head row for its signpost panel, and status reads owned, offer and later', () => {
  for (const f of Object.values(FORKS)) for (const lane of Object.keys(f.opts)) assert.ok(laneHead(lane), lane);
  const s = E.createRun();
  assert.equal(statusOf(s, 'start'), 'owned');
  assert.equal(statusOf(s, 'stray'), 'offer');
  assert.equal(statusOf(s, 'drop'), 'later');
});

test('a read-only view of the run never throws', () => {
  const s = play(25);
  const view = new Proxy(s, { set: () => false });
  assert.doesNotThrow(() => buildRoad(view));
  assert.ok(ITEMS.length > 0);
});
