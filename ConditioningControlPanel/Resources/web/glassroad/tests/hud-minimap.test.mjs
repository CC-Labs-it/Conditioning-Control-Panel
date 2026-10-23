import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fitMap, projectInto, mapPoints, thinLoop } from '../hud/minimap.js';
import { loadTrack } from '../track/load.js';

test('fit keeps the aspect, centres and pads', () => {
  const xz = [0, 0, 200, 0, 200, 100, 0, 100];   // 200 x 100 m
  const fit = fitMap(xz, 132, 104, 6);
  assert.ok(Math.abs(fit.s - 0.6) < 1e-9);        // (132 - 12) / 200, height would allow 0.92
  const p = [0, 0];
  assert.deepEqual(projectInto(fit, 0, 0, p), [6, 22]);
  projectInto(fit, 200, 100, p);
  assert.ok(Math.abs(p[0] - 126) < 1e-9 && Math.abs(p[1] - 82) < 1e-9);
  // world x right, world z down
  const a = projectInto(fit, 100, 10, [0, 0]), b = projectInto(fit, 100, 90, [0, 0]);
  assert.ok(b[1] > a[1]);
});

test('degenerate loops never throw or divide by zero', () => {
  const f0 = fitMap([], 100, 80);
  assert.equal(f0.s, 1);
  const f1 = fitMap([5, 5, 5, 5], 100, 80, 6);
  assert.ok(Number.isFinite(f1.s));
  const p = projectInto(f1, 5, 5, [0, 0]);
  assert.ok(Number.isFinite(p[0]) && Number.isFinite(p[1]));
});

test('the real track fits inside the map box and thins to a short loop', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  const xz = new Float32Array(track.nodeCount * 2);
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (let i = 0; i < track.nodeCount; i++) { track.frameAt(track.nodeS[i], f); xz[i * 2] = f.p[0]; xz[i * 2 + 1] = f.p[2]; }
  const thin = thinLoop(xz, 50);
  assert.ok(thin.length <= 100);
  const fit = fitMap(xz, 132, 104, 7);
  const pts = mapPoints(fit, xz);
  for (let i = 0; i < pts.length; i += 2) {
    assert.ok(pts[i] >= 7 - 1e-3 && pts[i] <= 125 + 1e-3, 'x ' + pts[i]);
    assert.ok(pts[i + 1] >= 7 - 1e-3 && pts[i + 1] <= 97 + 1e-3, 'y ' + pts[i + 1]);
  }
  assert.equal(thinLoop(xz, 10000), xz);
});

// ---- polish 6: the stacked course ----
import { bandOf, deckRuns, nearestOnLoop, thinLoopY, DECK_EDGES_M } from '../hud/minimap.js';

test('decks: heights fall in three bands, runs cover a band and meet the next', () => {
  assert.deepEqual([0, 6.9, 7, 14, 20.9, 21, 28, 60].map((y) => bandOf(y)), [0, 0, 1, 1, 1, 2, 2, 2]);
  assert.equal(DECK_EDGES_M.length, 2);
  const y = [0, 0, 0, 14, 14, 28, 28, 14, 0, 0];
  assert.deepEqual(deckRuns(y, 2), [[4, 7]]);
  assert.deepEqual(deckRuns(y, 1), [[2, 5], [6, 8]]);
  // the ground run wraps the lap line
  assert.deepEqual(deckRuns(y, 0), [[7, 13]]);
  assert.deepEqual(deckRuns([1, 2, 3], 0), [[0, 3]], 'one band: the whole loop');
  assert.deepEqual(deckRuns([1, 2, 3], 2), []);
});

test('decks: the player keeps its own deck where two pass over each other', () => {
  // a figure of eight in plan: points 2 and 7 sit at the same x, z on different decks
  const xz = [0, 0, 10, 0, 20, 0, 30, 0, 30, 10, 20, 10, 20, 5, 20, 0.5, 20, -5, 10, -5];
  assert.equal(nearestOnLoop(xz, 20, 0.2, 2), 2, 'near its last place, the lower pass');
  assert.equal(nearestOnLoop(xz, 20, 0.2, 7, 1), 7, 'the upper pass when it came from there');
  assert.equal(nearestOnLoop(xz, 30, 9, -1), 4, 'no hint: the nearest in plan');
  assert.equal(nearestOnLoop(xz, 0, 0, 5, 1, 3), 0, 'a far jump (a rescue) searches everything');
  assert.equal(nearestOnLoop([], 0, 0), -1);
  const t = thinLoopY(new Float32Array(2000), new Float32Array(1000).map((_, i) => i), 100);
  assert.equal(t.xz.length, 200); assert.equal(t.y.length, 100); assert.equal(t.y[50], 500);
});

test('decks: threshold stacks on three bands', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  const y = [];
  for (let i = 0; i < track.nodeCount; i++) y.push(track.frameAt(track.nodeS[i]).p[1]);
  assert.deepEqual([...new Set(y.map((v) => bandOf(v)))].sort(), [0, 1, 2]);
  for (const b of [0, 1, 2]) assert.ok(deckRuns(y, b).length >= 1);
});
