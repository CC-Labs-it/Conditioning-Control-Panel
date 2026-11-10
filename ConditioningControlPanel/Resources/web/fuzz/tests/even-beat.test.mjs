// Even beat, lane SIM (owner 2026-11-10, handoff-1110/TC-EVEN-BEAT-BRIEF.md): a buy about every 30-40 s over a ~90 minute
// run, kinds alternate, looks and copies spread out, toys arrive one at a time, and the last half hour is not all ranks.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { allRoutes } from '../sim/route.js';
import { buildPlan } from '../sim/journey.js';
import { buyKind, TOYS, PACE_MINUTES, PLAN_BUYS, JOURNEY_CHAPTERS, journeyChapters, SAVOUR_BUYS } from '../sim/journey-effects.js';
import { measureJourney, windows, orderReport } from '../dev/journey-timing.mjs';

const routes = allRoutes().filter((_, i) => [0, 39, 84, 125, 159, 190].includes(i));
const reports = routes.map((r, i) => measureJourney(r, { taps: 4, share: .125, option: i % 2 }));

test('the beat: a quick hook, at most 22 buys in the first 10 minutes, 12-20 in every later 10, no gap over 60 s', () => {
  for (const rep of reports) {
    const w = windows(rep);
    assert.ok(w[0].buys <= 22, `first 10 min: ${w[0].buys} buys`);
    const first3 = rep.paid.filter((p) => p.at <= 180).length;
    assert.ok(first3 >= 8 && first3 <= 11, `first 3 min: ${first3} buys`);
    // Every full 10 minute window after the first (the run's last part window is shorter).
    for (const x of w.slice(1, Math.floor(rep.minutes / 10))) assert.ok(x.buys >= 12 && x.buys <= 20, `minute ${x.from}: ${x.buys} buys`);
    assert.ok(rep.minutes >= 88 && rep.minutes <= 92, `${rep.minutes} min`);
    // The reference player claims its collect banks once a minute, so a late gap can reach that minute and no further.
    assert.ok(rep.max <= 60.5, `longest gap ${rep.max} s`);
  }
});

test('gaps only rise: about 32-35 s to minute 30, 38 to minute 60, 46 after', () => {
  const rep = reports[0];
  const mean = (lo, hi) => { const g = rep.paid.filter((p) => p.at > lo * 60 && p.at <= hi * 60).map((p) => p.gap); return g.reduce((a, b) => a + b, 0) / g.length; };
  assert.ok(mean(3, 30) > 30 && mean(3, 30) < 37, `3-30: ${mean(3, 30)}`);
  assert.ok(mean(30, 60) > 35 && mean(30, 60) < 42, `30-60: ${mean(30, 60)}`);
  assert.ok(mean(60, 90) > 42 && mean(60, 90) < 50, `60-90: ${mean(60, 90)}`);
  for (let n = 6; n < PLAN_BUYS; n++) assert.ok(PACE_MINUTES[n + 1] - PACE_MINUTES[n] >= PACE_MINUTES[n] - PACE_MINUTES[n - 1] - 1e-9, `planned gap ${n}`);
  assert.equal(JOURNEY_CHAPTERS.at(-1)[1], PLAN_BUYS);
  assert.ok(Math.abs(journeyChapters(45).at(-1)[0] * 2 - JOURNEY_CHAPTERS.at(-1)[0]) < 1e-9, 'a shorter renewed run scales every minute');
});

test('order: no three of a kind in a row, at most 2 looks in any 4, copies spread, every route', () => {
  for (const rep of reports) {
    const o = orderReport(rep);
    assert.ok(o.longest.n <= 2, `${o.longest.n} ${o.longest.kind} in a row at buy ${o.longest.at}`);
    assert.ok(o.copyLongest <= 2, `${o.copyLongest} copies in a row`);
    assert.ok(o.looks4 <= 2, `${o.looks4} looks in 4 buys`);
    assert.ok(o.copiesBy15 <= 8, `${o.copiesBy15} copies by minute 15`);
  }
  // The planner itself, on every route: the paid rows of the plan keep the same rules.
  for (const r of allRoutes()) {
    const kinds = buildPlan(r).filter((e) => e.item.kind !== 'fork').map((e) => buyKind(e.item.id, e.item.lane));
    for (let i = 2; i < kinds.length; i++) assert.ok(!(kinds[i] === kinds[i - 1] && kinds[i] === kinds[i - 2]), `${Object.values(r)}: three ${kinds[i]} at ${i + 1}`);
    for (let i = 0; i + 4 <= kinds.length; i++) assert.ok(kinds.slice(i, i + 4).filter((k) => k === 'look').length <= 2, `${Object.values(r)}: looks at ${i + 1}`);
  }
});

test('toys arrive one at a time on the owner minutes; the next 6 buys after one hold no other', () => {
  const want = { wheel: 4, mini_slots: 9, quiz: 12, mini_race: 15, boxes: 19, mini_breakout: 22, mini_deep: 30, toy_box: 44, dispenser: 56 };
  for (const rep of reports) {
    for (const [id, m] of Object.entries(want)) {
      const p = rep.paid.find((x) => x.id === id);
      assert.ok(p, id);
      assert.ok(Math.abs(p.at / 60 - m) <= 2.2, `${id} at minute ${(p.at / 60).toFixed(1)}, wanted about ${m}`);
    }
    const o = orderReport(rep);
    assert.ok(o.toyGap >= 7, `toys ${o.toyGap} buys apart`);
    assert.deepEqual(TOYS.filter((id) => rep.paid.some((p) => p.id === id)).length, TOYS.length);
  }
  assert.equal(SAVOUR_BUYS, 15);
});

test('the last 30 minutes are at most half plain ranks', () => {
  for (const rep of reports) {
    const o = orderReport(rep);
    assert.ok(o.lateRanks * 2 <= o.lateBuys, `${o.lateRanks} plain ranks of ${o.lateBuys} buys`);
  }
});

test('kinds: copies, looks, plain ranks, toys, toy upgrades, features', () => {
  assert.equal(buyKind('stray', 'trunk'), 'copy');
  assert.equal(buyKind('starburst', 'look'), 'look');
  assert.equal(buyKind('rank_stray_2'), 'rank');
  assert.equal(buyKind('rank_wheel_1'), 'upgrade');
  assert.equal(buyKind('rank_wheel_3'), 'rank');
  assert.equal(buyKind('spec_dispenser'), 'upgrade');
  assert.equal(buyKind('spec_rebounds'), 'feature');
  assert.equal(buyKind('quiz', 'trouble'), 'toy');
  assert.equal(buyKind('boxes', 'side'), 'toy');
  assert.equal(buyKind('overlay', 'trunk'), 'feature');
});
