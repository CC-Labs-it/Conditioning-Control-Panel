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
