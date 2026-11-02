import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';
import { parse, serialize } from '../sim/save.js';
import { createLife } from '../sim/save.js';
import { bankOf, collect, levelSeconds, xpNeed } from '../sim/levels.js';
import { cleanOff, familyOf, liveOn, panelGroups, parentOf, switchable } from '../sim/switches.js';
import { edgesOf, layoutGuide, marksOf, NODE_H, NODE_W, reachable } from '../ui/guide-map.js';

/* ---- levels ---- */
test('xp past a full bar banks whole levels and the bar keeps filling', () => {
  assert.equal(bankOf(1, 0).banked, 0);
  assert.equal(bankOf(1, xpNeed(1)).banked, 1);
  const b = bankOf(1, xpNeed(1) + xpNeed(2) + xpNeed(3) + 5);
  assert.equal(b.banked, 3);
  assert.ok(Math.abs(b.left - 5) < 1e-9);
  assert.equal(b.need, xpNeed(4));
  assert.ok(b.fill > 0 && b.fill < 1);
});

test('one press pays every banked level and keeps the part-filled bar', () => {
  const xp = xpNeed(2) + xpNeed(3) + 7;
  const got = collect(2, xp);
  assert.equal(got.levels, 2);
  assert.equal(got.lvl, 4);
  assert.ok(Math.abs(got.xp - 7) < 1e-9);
  assert.equal(got.seconds, levelSeconds(2) + levelSeconds(3));
  assert.deepEqual(collect(got.lvl, got.xp).levels, 0);
});

test('collecting late pays exactly what collecting each one on time pays', () => {
  let lvl = 1, xp = 0, seconds = 0;
  for (let i = 0; i < 9; i++) { xp = xpNeed(lvl) + 1e-6; const g = collect(lvl, xp); lvl = g.lvl; xp = g.xp; seconds += g.seconds; }
  const all = collect(1, Array.from({ length: 9 }, (_, i) => xpNeed(i + 1)).reduce((a, b) => a + b, 0) + 1e-6);
  assert.equal(all.levels, 9);
  assert.equal(all.lvl, lvl);
  assert.equal(all.seconds, seconds);
});

test('an old save reads as it stands: a junk or a missing number is nothing, a full bar is one level waiting', () => {
  assert.equal(bankOf(undefined, undefined).banked, 0);
  assert.equal(bankOf(1, NaN).banked, 0);
  assert.equal(bankOf(-4, 3).lvl, 1);
  assert.equal(bankOf(5, xpNeed(5) + 1).banked, 1);
});

/* ---- switches ---- */
test('the panel never lists a fork, an ending, a mood, start or itself', () => {
  for (const id of ['start', 'panel', 'f1', 'f5', 'drop', 'onemore']) assert.equal(switchable(BY_ID[id]), false, id);
  for (const it of ITEMS) if (it.group !== 'run' || it.kind !== 'item') assert.equal(switchable(it), false, it.id);
  assert.equal(switchable(BY_ID.stray), true);
  assert.equal(switchable(undefined), false);
});

test('the list is owned rows grouped by lane, skipping rows with nothing to switch', () => {
  const s = E.createRun();
  for (const id of ['screen', 'levels', 'overlay', 'panel']) E.grant(s, id);
  const groups = panelGroups(s, (id) => id !== 'levels');
  const ids = groups.flatMap((g) => g.rows.map((r) => r.id));
  assert.ok(ids.includes('screen') && ids.includes('stray'));
  for (const no of ['levels', 'panel', 'start', 'f1']) assert.ok(!ids.includes(no), no);
  for (const g of groups) for (const r of g.rows) assert.equal(r.lane, g.lane);
});

test('the off list survives a save, drops what it cannot be, and an old save has none', () => {
  const s = E.createRun();
  E.grant(s, 'screen');
  s.off = { stray: 1 };
  const raw = JSON.parse(serialize(s, createLife()));
  raw.run.off.nope = 1; raw.run.off.f1 = 1; raw.run.off.levels = 1;
  const back = parse(JSON.stringify(raw)).run;
  assert.deepEqual(back.off, { stray: 1 });
  delete raw.run.off;
  assert.deepEqual(parse(JSON.stringify(raw)).run.off, {});
  assert.deepEqual(cleanOff([1, 2], { stray: 1 }), {});
});

test('a row switched off keeps its own pay in the sim', () => {
  const s = E.createRun();
  const row = ITEMS.find((it) => it.fps && it.kind === 'item' && it.group === 'run' && it.lane === 'side');
  E.grant(s, row.id);
  assert.ok(row, 'a paying row');
  const before = E.rates(s).fps;
  s.off[row.id] = 1;
  E.recalc(s);
  assert.equal(E.rates(s).fps, before);
});

test('a family hangs off its parent: children wait while it is off, and keep their own switch', () => {
  assert.equal(parentOf('em_hat'), 'em_head');
  assert.equal(parentOf('em_head'), null);
  assert.equal(parentOf('d_like'), 'd_scroll');
  assert.equal(parentOf('stray'), null);
  assert.ok(familyOf('em_hat').includes('em_boss'));
  for (const id of familyOf('a_hum')) assert.ok(BY_ID[id], id);
  assert.equal(liveOn({}, 'em_hat'), true);
  assert.equal(liveOn({ em_head: 1 }, 'em_hat'), false);
  assert.equal(liveOn({ em_hat: 1 }, 'em_hat'), false);
  assert.equal(liveOn({ em_hat: 1 }, 'em_head'), true);
  assert.equal(liveOn({ em_head: 1 }, 'stray'), true);
});

/* ---- the map ---- */
test('every row has a place on the map and none overlap', () => {
  const { pos, width, height } = layoutGuide();
  assert.equal(Object.keys(pos).length, ITEMS.length);
  const list = Object.entries(pos);
  for (const [id, p] of list) {
    assert.ok(Number.isFinite(p.x) && Number.isFinite(p.y), id);
    assert.ok(p.x >= 0 && p.y >= 0 && p.x + NODE_W <= width && p.y + NODE_H <= height, `${id} is off the map`);
  }
  for (let i = 0; i < list.length; i++) {
    for (let j = i + 1; j < list.length; j++) {
      const [a, A] = list[i], [b, B] = list[j];
      assert.ok(Math.abs(A.x - B.x) >= NODE_W || Math.abs(A.y - B.y) >= NODE_H, `${a} sits on ${b}`);
    }
  }
});

test('the map runs left to right: a row never sits left of a parent it cost more than', () => {
  const { pos } = layoutGuide();
  for (const it of ITEMS) {
    if (it.group !== 'run') continue;
    for (const q of it.req) if (it.cost > BY_ID[q].cost) assert.ok(pos[it.id].x > pos[q].x, `${it.id} / ${q}`);
  }
});

test('every line joins two rows that exist', () => {
  const { pos } = layoutGuide();
  const edges = edgesOf();
  assert.ok(edges.length > ITEMS.length / 2);
  for (const e of edges) assert.ok(pos[e.a] && pos[e.b], `${e.a} -> ${e.b}`);
  assert.ok(edges.some((e) => e.cls === 'any'), 'Spiral Colours has an any line');
});

test('marks: yours, on offer, later, and the routes not picked', () => {
  const s = E.createRun();
  let m = marksOf(s);
  assert.equal(m.start, 'owned');
  assert.equal(m.stray, 'offer');
  assert.equal(m.drop, 'later');
  E.grant(s, 'f1');
  s.picks.f1 = 'eyes';
  E.recalc(s);
  m = marksOf(s);
  assert.equal(m.f1, 'owned');
  const eyes = ITEMS.find((it) => it.lane === 'eyes');
  const ears = ITEMS.find((it) => it.lane === 'ears');
  assert.notEqual(m[eyes.id], 'shut');
  assert.equal(m[ears.id], 'shut');
  assert.ok(!reachable(s).has(ears.id));
});

test('a stacking row you own and can buy again reads as on offer, and marks never throw on a read-only view', () => {
  const s = E.createRun();
  E.grant(s, 'stray');
  assert.equal(marksOf(s).stray, 'offer');
  const view = new Proxy(s, { set: () => false });
  assert.doesNotThrow(() => marksOf(view));
});
