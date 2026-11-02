import { test } from 'node:test';
import assert from 'node:assert/strict';
import { allRoutes, playRoute } from '../sim/route.js';
import { MOOD_IDS } from '../sim/moods.js';

const minutes = (r) => r.end / 60;
const name = (p) => Object.values(p).join('/');

test('192 routes', () => assert.equal(allRoutes().length, 192));

test('every route finishes for a greedy player at 4 taps/s in 25 to 37 minutes and never gets stuck', () => {
  for (const p of allRoutes()) {
    const r = playRoute(p);
    assert.equal(r.stuck, false, `${name(p)} got stuck`);
    assert.ok(minutes(r) >= 25 && minutes(r) <= 37, `${name(p)} took ${minutes(r).toFixed(1)} min`);
    assert.ok(r.longest <= 90, `${name(p)} idled ${r.longest} s between buys`);
  }
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
      assert.ok(minutes(r) >= 20 && minutes(r) <= 40, `${mood} ${name(p)} took ${minutes(r).toFixed(1)} min`);
    }
  }
});

test('modifiers reach the timing: a route test can measure an unlock', () => {
  const p = allRoutes()[0];
  const faster = playRoute(p, { mods: [{ click: (v) => v * 2 }] });
  assert.ok(faster.end < playRoute(p).end);
});
