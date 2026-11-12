import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID, ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';
import { parse, serialize } from '../sim/save.js';
import { createLife } from '../sim/save.js';
import { COLLECT_MAX_SECONDS, bankOf, collect, levelSeconds, xpNeed } from '../sim/levels.js';
import { CONFIG } from '../data/config.js';
import { cleanOff, familyOf, liveOn, panelGroups, parentOf, switchable } from '../sim/switches.js';

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

test('a run of waiting asks for no more than the cap, and still takes every level', () => {
  const need = Array.from({ length: 200 }, (_, i) => xpNeed(i + 1)).reduce((a, b) => a + b, 0);
  const got = collect(1, need + 1);
  assert.equal(got.levels, 200);
  assert.equal(got.lvl, 201);
  assert.equal(got.seconds, COLLECT_MAX_SECONDS);
  assert.ok(COLLECT_MAX_SECONDS * CONFIG.SIDE_FLOOR <= 30, 'the floor of a capped collect is worth half a minute at most');
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

test('Questions follows EMI in the shop and old question owners keep their lesson and its host', () => {
  const run = E.createRun();
  E.grant(run, 'f2');
  assert.deepEqual(BY_ID.quiz.req, ['em_head']);
  for (const oldOrder of [['quiz'], ['quiz', 'em_head'], ['em_head', 'quiz']]) {
    const legacy = JSON.parse(serialize(run, createLife()));
    Object.assign(legacy.run.owned, Object.fromEntries(oldOrder.map(id => [id, 1])));
    legacy.run.order.push(...oldOrder);
    legacy.run.store.quiz = { lesson:2 };
    const restored = parse(JSON.stringify(legacy)).run;
    assert.equal(restored.owned.em_head, 1);
    assert.equal(restored.order.filter(id => id === 'em_head').length, 1);
    assert.ok(restored.order.indexOf('em_head') < restored.order.indexOf('quiz'));
    assert.equal(restored.store.quiz.lesson, 2);
    assert.deepEqual(parse(serialize(restored, createLife())).run.order, restored.order);
  }
  assert.equal(parentOf('quiz'), 'em_head');
  assert.equal(liveOn({em_head:1}, 'quiz'), false);
  assert.deepEqual(familyOf('quiz'), ['quiz']);
});
