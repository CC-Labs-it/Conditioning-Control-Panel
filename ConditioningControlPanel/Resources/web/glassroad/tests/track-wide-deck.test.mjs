import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadTrack } from '../track/load.js';
import { fixtureJson } from './sim-fixture-p6.mjs';

const json = fixtureJson(), narrow = loadTrack(json);
const upper = json.nodes.findIndex(n => n.p[0] === 110 && n.p[1] === 14 && n.p[2] === -16);
const lower = json.nodes.findIndex(n => n.p[0] === 140 && n.p[1] === 0 && n.p[2] === -16);
assert.ok(upper >= 0 && lower >= 0);
for (const n of json.nodes) if (n.p[0] === 110 && n.p[1] === 14) n.w = 104;
// Exercise query geometry independently of course-specific schema width limits.
const wide = loadTrack(json, { validate: false }), s = wide.nodeS[upper] + 1;
const point = (x, h) => { const f = wide.frameAt(s); return f.p.map((v, k) => v + f.b[k] * x + f.n[k] * h); };

test('wide upper-deck shoulder wins over the nearer lower centreline for every hint', () => {
  for (const h of [0, 0.2, 3.5]) for (const x of [30, 38, 45]) for (const hint of [upper, lower, -1]) {
    const q = wide.query(...point(x, h), hint);
    assert.ok(Math.abs(q.s - s) < 1e-3, `wrong deck at x=${x}, h=${h}, hint=${hint}`);
    assert.ok(Math.abs(q.height - h) < 1e-3); assert.ok(Math.abs(q.lateral - x) < 1e-3);
    assert.equal(q.onRoad, true); assert.equal(q.surface, 'glass');
  }
});

test('wide-deck correction leaves high flight, fallen karts and off-deck points on their original route', () => {
  for (const [x, h] of [[38, 10], [38, -3], [55, 0.2]]) {
    const p = point(x, h), old = narrow.query(...p), q = wide.query(...p);
    assert.ok(Math.abs(q.s - old.s) < 1e-3, `preserve route at x=${x}, h=${h}`);
  }
  const f = wide.frameAt(wide.nodeS[lower] - 2), p = f.p.map((v, k) => v + f.n[k] * 0.2);
  const q = wide.query(...p, lower);
  assert.ok(Math.abs(q.s - (wide.nodeS[lower] - 2)) < 1e-3, 'the lower road still owns its racer');
});

test('below lookup still catches the highest support and falls through to the lower road', () => {
  const above = wide.query.below(...point(38, 1));
  assert.ok(above && Math.abs(above.s - s) < 1e-3);
  const p = point(38, -3), old = narrow.query.below(...p), q = wide.query.below(...p);
  assert.ok(old && q); assert.ok(Math.abs(q.s - old.s) < 1e-3);
  assert.ok(q.p[1] < 3, 'the kart has fallen below the upper platform');
});
