import { test } from 'node:test';
import assert from 'node:assert/strict';
import { allRoutes, playRoute } from '../sim/route.js';
import { MOOD_IDS } from '../sim/moods.js';
import { CONFIG } from '../data/config.js';
import * as E from '../sim/economy.js';

const minutes = (r) => r.end / 60;
const name = (p) => Object.values(p).join('/');
/* What dev/measure.mjs saw the real game do, as pure numbers: every prize pays its whole budget, modifiers hold at the knee. */
/* Bundles and PRICE_SCALE 1.5 (2026-11-04): the pure route reads about 55 minutes where the real page ends at 33-39
 * (dev/measure.mjs, arcade prizes and slots included), so these bounds guard the plan, not the length a player sees. */
const real = { side: true, mods: [{ fps: (v) => v * CONFIG.MOD_FPS_KNEE, click: (v) => v * CONFIG.MOD_CLICK_KNEE }] };

test('192 routes', () => assert.equal(allRoutes().length, 192));

/* Nobody plays like this, Levels and questions never pressed and no modifier counted; it is the floor. Bimbo is the worst of it: its pay is in its modifiers. */
test('buying every optional toy without collecting any prizes still ends within 95 minutes', () => {
  for (const p of allRoutes()) {
    const r = playRoute(p);
    assert.equal(r.stuck, false, `${name(p)} got stuck`);
    assert.ok(minutes(r) <= 95, `${name(p)} took ${minutes(r).toFixed(1)} min`);
    assert.ok(r.longest <= 210, `${name(p)} idled ${r.longest} s between buys`);
  }
});

test('with side money and modifiers every route takes 50 to 62 planned minutes at 4 taps/s, under 80 at 2, with no long wall', () => {
  const times = [];
  for (const p of allRoutes()) {
    const r = playRoute(p, real);
    times.push(minutes(r));
    assert.ok(minutes(r) >= 50 && minutes(r) <= 62, `${name(p)} took ${minutes(r).toFixed(1)} min`);
    assert.ok(r.longest <= 140, `${name(p)} idled ${r.longest} s between buys`);
    assert.ok(r.firstAlt / 60 >= 30, `${name(p)} could leave at ${(r.firstAlt / 60).toFixed(1)} min`);
    assert.ok(r.at.f1 <= 120, `${name(p)} first fork at ${r.at.f1} s`);
    assert.ok(r.at.f5 <= 240, `${name(p)} theme at ${(r.at.f5 / 60).toFixed(1)} min`);
    assert.ok(r.at.f3 > r.at.f5 && r.at.f3 <= 300, `${name(p)} pictures at ${(r.at.f3 / 60).toFixed(1)} min`);
    const lazy = playRoute(p, { ...real, taps: 2 });
    assert.ok(minutes(lazy) <= 80, `${name(p)} lazy took ${minutes(lazy).toFixed(1)} min`);
  }
  assert.ok(Math.max(...times) - Math.min(...times) < 10, `routes differ by ${Math.max(...times) - Math.min(...times)} min`);
});

test('side money stays a share of income however much is asked for, and never pays nothing', () => {
  const s = E.createRun();
  E.earn(s, 1000, 'plan');
  assert.equal(E.sideLump(s, 50, 'small'), 50, 'under the budget a prize pays in full');
  for (let i = 0; i < 2000; i++) {
    E.earn(s, 10, 'plan');
    assert.ok(E.sideLump(s, 500, 'greedy') >= 500 * CONFIG.SIDE_FLOOR);
  }
  const fair = E.createRun();
  for (let i = 0; i < 2000; i++) { E.earn(fair, 10, 'plan'); E.sideLump(fair, 20, 'greedy'); }
  const side = fair.sources.greedy / fair.total;
  assert.ok(side <= CONFIG.SIDE_SHARE / (1 + CONFIG.SIDE_SHARE) + 0.001, `side money was ${side} of income`);
  assert.ok(side >= 0.1, `side money was only ${side} of income`);
});

test('a weighted prize keeps more of its ask, and the budget still holds', () => {
  const s = E.createRun();
  for (let i = 0; i < 2000; i++) { E.earn(s, 10, 'plan'); E.sideLump(s, 20, 'noise'); if (i % 10 === 0) E.sideLump(s, 40, 'quiz'); }
  const right = E.sideLump(s, 40, 'quiz'), noise = E.sideLump(s, 40, 'noise');
  assert.ok(right >= noise * (CONFIG.SIDE_WEIGHT.quiz - 0.1), `right ${right} against ${noise}`);
  const side = (s.total - s.sources.plan) / s.total;
  assert.ok(side <= CONFIG.SIDE_SHARE / (1 + CONFIG.SIDE_SHARE) + 0.01, `side money was ${side} of income`);
});

test('one huge ask cannot pay past what the budget has, plus the floor', () => {
  const s = E.createRun();
  E.earn(s, 100000, 'plan');
  const ask = 1e9, got = E.sideLump(s, ask, 'levels');
  assert.ok(got <= Math.max(s.side.budget, ask * CONFIG.SIDE_FLOOR) + 1, `paid ${got}`);
});

test('modifiers past the knee keep counting at a fraction of their strength', () => {
  const s = E.createRun();
  E.grant(s, 'tunnel');
  const big = E.rates(s, [{ fps: (v) => v * 100 }]).fps, more = E.rates(s, [{ fps: (v) => v * 101 }]).fps;
  assert.ok(more > big, 'more is still more');
  assert.ok(big < s.base.fps * 25, `x100 paid ${big / s.base.fps}`);
  assert.equal(E.rates(s, [{ fps: (v) => v * 1.05 }]).fps, s.base.fps * 1.05, 'below the knee nothing changes');
});

test('the route respects its forks: Drop is the last buy, locked lanes stay out', () => {
  const p = allRoutes()[0];
  const r = playRoute(p);
  assert.equal(r.state.ended.id, 'drop');
  assert.deepEqual(r.state.picks, p);
  assert.equal(Object.keys(r.state.owned).some((id) => id.startsWith('a_')), false, 'Watch route bought a Listen row');
});

test('every mood still finishes every sampled route', () => {
  const routes = allRoutes().filter((_, i) => i % 16 === 0);
  for (const mood of MOOD_IDS) {
    for (const p of routes) {
      const r = playRoute(p, { mood });
      assert.equal(r.stuck, false, `${mood} ${name(p)} got stuck`);
      assert.ok(minutes(r) >= 45 && minutes(r) <= 90, `${mood} ${name(p)} took ${minutes(r).toFixed(1)} min`);
    }
  }
});

test('modifiers reach the timing: a route test can measure an unlock', () => {
  const p = allRoutes()[0];
  const faster = playRoute(p, { mods: [{ click: (v) => v * 2 }] });
  assert.ok(faster.end < playRoute(p).end);
});
