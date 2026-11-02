import { test } from 'node:test';
import assert from 'node:assert/strict';
import { allRoutes, playRoute } from '../sim/route.js';
import { MOOD_IDS } from '../sim/moods.js';
import { CONFIG } from '../data/config.js';
import * as E from '../sim/economy.js';

const minutes = (r) => r.end / 60;
const name = (p) => Object.values(p).join('/');
/* What dev/measure.mjs saw the real game do, as pure numbers: every prize pays its whole budget, modifiers hold at the knee. */
const real = { side: true, mods: [{ fps: (v) => v * CONFIG.MOD_FPS_KNEE, click: (v) => v * CONFIG.MOD_CLICK_KNEE }] };

test('192 routes', () => assert.equal(allRoutes().length, 192));

test('with no side money at all a route still ends, at most 45 minutes, and never gets stuck', () => {
  for (const p of allRoutes()) {
    const r = playRoute(p);
    assert.equal(r.stuck, false, `${name(p)} got stuck`);
    assert.ok(minutes(r) <= 45, `${name(p)} took ${minutes(r).toFixed(1)} min`);
    assert.ok(r.longest <= 90, `${name(p)} idled ${r.longest} s between buys`);
  }
});

test('with side money and modifiers every route takes 25 to 37 minutes at 4 taps/s, under 50 at 2, with no long wall', () => {
  const times = [];
  for (const p of allRoutes()) {
    const r = playRoute(p, real);
    times.push(minutes(r));
    assert.ok(minutes(r) >= 25 && minutes(r) <= 37, `${name(p)} took ${minutes(r).toFixed(1)} min`);
    assert.ok(r.longest <= 60, `${name(p)} idled ${r.longest} s between buys`);
    assert.ok(r.firstAlt / 60 >= 20, `${name(p)} could leave at ${(r.firstAlt / 60).toFixed(1)} min`);
    assert.ok(r.at.f1 <= 120, `${name(p)} first fork at ${r.at.f1} s`);
    assert.ok(r.at.f5 <= r.end * 0.6, `${name(p)} theme at ${(r.at.f5 / 60).toFixed(1)} of ${minutes(r).toFixed(1)} min`);
    const lazy = playRoute(p, { ...real, taps: 2 });
    assert.ok(minutes(lazy) <= 50, `${name(p)} lazy took ${minutes(lazy).toFixed(1)} min`);
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
      assert.ok(minutes(r) >= 20 && minutes(r) <= 50, `${mood} ${name(p)} took ${minutes(r).toFixed(1)} min`);
    }
  }
});

test('modifiers reach the timing: a route test can measure an unlock', () => {
  const p = allRoutes()[0];
  const faster = playRoute(p, { mods: [{ click: (v) => v * 2 }] });
  assert.ok(faster.end < playRoute(p).end);
});
