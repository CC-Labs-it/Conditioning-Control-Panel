import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { CONFIG } from '../data/config.js';
import { BY_ID } from '../data/items.js';
import { serialize, parse, createLife } from '../sim/save.js';

/* Toy budgets (owner 2026-11-10, handoff-1110/TC-EVEN-BEAT-BRIEF.md PAY): every on-screen game draws on its own budget,
 * a share of base income while it is owned; the shared side money is for everything else. */
const TOYS = Object.keys(CONFIG.TOY_SHARE);
const INCOME = 100;   // base income a second in these drives

function run(owned = []) {
  const s = E.createRun();
  for (const id of owned) { s.owned[id] = 1; s.order.push(id); }
  return s;
}
/** One second of play: the clock moves and base income comes in, as E.tick does. */
function second(s) { s.clock += 1000; E.earn(s, INCOME, 'fps'); }

test('every toy share names a real game, and the shares add up to one', () => {
  for (const id of TOYS) assert.ok(BY_ID[id], `${id} is an item`);
  const sum = TOYS.reduce((a, id) => a + CONFIG.TOY_SHARE[id], 0);
  assert.ok(Math.abs(sum - 1) < 1e-9, `shares add to ${sum}`);
  for (const id of TOYS) assert.ok(!CONFIG.FULL_PRIZE_SOURCES.includes(id) && !CONFIG.SIDE_WEIGHT[id], `${id} only uses its own budget`);
});

test('one toy pays the same whether or not the other toys are owned and played', () => {
  const alone = run(['toy_box']), crowd = run(TOYS);
  const a = [], b = [];
  for (let t = 1; t <= 1800; t++) {
    second(alone); second(crowd);
    if (t % 7 === 0) for (const id of TOYS) if (id !== 'toy_box') E.sideLump(crowd, 5000, id);
    if (t % 11 === 0) E.sideLump(crowd, 3000, 'golden');
    if (t % 30 === 0) { a.push(E.sideLump(alone, 4000, 'toy_box')); b.push(E.sideLump(crowd, 4000, 'toy_box')); }
  }
  assert.deepEqual(b, a);
  assert.deepEqual(crowd.side.budget, alone.side.budget, 'toys never touch the shared side budget');
});

test('a toy that is not owned or is switched off builds no budget and pays only the floor', () => {
  const s = run(['wheel']);
  s.off.wheel = 1;
  for (let t = 0; t < 600; t++) second(s);
  assert.equal(E.sideLump(s, 1000, 'wheel'), 1000 * CONFIG.SIDE_FLOOR);
  assert.equal(E.sideLump(s, 1000, 'dispenser'), 1000 * CONFIG.SIDE_FLOOR, 'not owned');
});

test('an unplayed toy budget stops at about three minutes of its share', () => {
  const s = run(['wheel', 'toy_box']);
  for (let t = 0; t < 1800; t++) second(s);
  const cap = CONFIG.TOY_SHARE.wheel * INCOME * CONFIG.TOY_CAP_S;
  const pot = E.toyPot(s, 'wheel');
  assert.ok(pot.budget - pot.paid <= cap + 1e-6 && pot.budget - pot.paid >= cap * 0.9, `unused ${pot.budget - pot.paid} against ${cap}`);
  const got = E.sideLump(s, 1e4, 'wheel');
  assert.ok(got <= cap + 1e-6 && got >= cap * 0.9, `a late big ask paid ${got}`);
  /* the toy box missed nothing because the wheel waited */
  assert.ok(Math.abs(E.toyPot(s, 'toy_box').budget - CONFIG.TOY_SHARE.toy_box * INCOME * CONFIG.TOY_CAP_S) < 1e-6);
});

test('a toy bought mid-run starts with a minute of its share, once', () => {
  const s = run();
  for (let t = 0; t < 600; t++) second(s);
  s.owned.mini_slots = 1; s.order.push('mini_slots');
  second(s);
  const start = CONFIG.TOY_SHARE.mini_slots * INCOME * CONFIG.TOY_START_S;
  assert.ok(Math.abs(E.toyPot(s, 'mini_slots').budget - start) < CONFIG.TOY_SHARE.mini_slots * INCOME * 12, `started with ${E.toyPot(s, 'mini_slots').budget}`);
  const first = E.sideLump(s, start, 'mini_slots');
  assert.ok(first >= start * 0.9, `the first win paid ${first} of ${start}`);
  second(s);
  assert.ok(E.toyPot(s, 'mini_slots').budget < start * 1.2, 'no second head start');
});

test('a small ask under the budget pays in full, a greedy one gets the budget, never under the floor', () => {
  const s = run(['mini_race']);
  for (let t = 0; t < 120; t++) second(s);
  assert.equal(E.sideLump(s, 50, 'mini_race'), 50);
  for (let t = 0; t < 300; t++) {
    second(s);
    if (t % 20 === 0) assert.ok(E.sideLump(s, 1e5, 'mini_race') >= 1e5 * CONFIG.SIDE_FLOOR);
  }
});

function playAll(fraction) {
  const s = run(TOYS);
  const played = TOYS.slice(0, Math.round(TOYS.length * fraction));
  for (let t = 1; t <= 3600; t++) {
    second(s);
    /* each played toy asks about twice its share every 20-45 s, as the games do when played well */
    played.forEach((id, i) => {
      const every = 20 + 5 * i;
      if (t % every === 0) E.sideLump(s, 2 * CONFIG.TOY_SHARE[id] * INCOME * every, id);
    });
  }
  const base = s.sources.fps;
  const toys = TOYS.reduce((a, id) => a + (s.sources[id] || 0), 0);
  return { s, ratio: toys / base, played };
}

test('every toy played pays about the base income again; half of them about their shares', () => {
  const all = playAll(1);
  assert.ok(all.ratio >= 0.9 && all.ratio <= 1.1, `all toys paid ${all.ratio} of base`);
  const half = playAll(0.5);
  const want = half.played.reduce((a, id) => a + CONFIG.TOY_SHARE[id], 0);
  assert.ok(Math.abs(half.ratio - want) < 0.1 * want + 0.01, `half paid ${half.ratio}, shares ${want}`);
});

test('toy budgets and recent income survive a save', () => {
  const s = run(['quiz']);
  s.owned.em_head = 1; s.order.splice(1, 0, 'em_head');
  for (let t = 0; t < 400; t++) second(s);
  E.sideLump(s, 500, 'quiz');
  const back = parse(serialize(s, createLife())).run;
  assert.deepEqual(back.toys.quiz, s.toys.quiz);
  assert.equal(back.inc.k, s.inc.k);
  assert.ok(Math.abs(back.inc.sum - s.inc.sum) < 1e-6);
  assert.equal(E.quoteSideLump(back, 2000, 'quiz'), E.quoteSideLump(s, 2000, 'quiz'));
});
