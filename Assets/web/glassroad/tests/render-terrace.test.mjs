import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { sampleTrack, placeColossi, worldLook } from '../render/colossiMath.js';
import { TERRACE, planTerrace, terraceBoxesOverlap, terraceRoadBoxes, terraceHitsBody } from '../render/terraceMath.js';

const axes = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
const box = (p, half) => ({ p, axes, half });
const size = [12.3, 2.228897, 1.6775];
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const straight = () => ({ lengthM: 160, sections: [], routes: [], frameAt(s) {
  return { p: [s, 0, 0], t: [1, 0, 0], n: [0, 1, 0], b: [0, 0, 1], w: 16, flags: ['rail'] };
} });

test('terrace: solid boxes detect crossing face interiors without contained corners', () => {
  assert.equal(terraceBoxesOverlap(box([0, 0, 0], [8, 0.2, 1]), box([0, 0, 0], [1, 0.1, 8])), true);
  assert.equal(terraceBoxesOverlap(box([0, 0, 0], [8, 0.2, 1]), box([0, 1, 0], [1, 0.1, 8])), false);
});

test('terrace: capsule crossing the face centre is rejected', () => {
  assert.equal(terraceHitsBody(box([0, 0, 0], [8, 0.3, 2]), { B: [0, -8, 0], top: [0, 8, 0], r: 0.1 }), true);
  assert.equal(terraceHitsBody(box([0, 0, 0], [8, 0.3, 2]), { B: [0, -8, 8], top: [0, 8, 8], r: 0.1 }), false);
});

test('terrace: opposing rows face inward with a proper basis and remain on their platforms', () => {
  const plan = planTerrace(straight(), size);
  assert.equal(plan.placements.length, 2);
  for (const p of plan.placements) {
    const [x, y, z] = p.axes;
    assert.deepEqual([x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]].map((v) => v || 0), z.map((v) => v || 0));
    assert.ok(-z[2] * p.side < 0, 'local -Z faces the track');
    assert.ok(Math.abs(p.p[2] - p.platform.p[2]) + size[2] / 2 < p.platform.half[2]);
    assert.ok(Math.abs((p.platform.p[1] + p.platform.half[1]) - p.p[1]) < 1e-9);
  }
});

test('terrace: rejects an opening in the interior of the platform span', () => {
  const tr = straight(); tr.routes = [{ openS0: 37, openS1: 39 }];
  assert.equal(planTerrace(tr, size, { L: { ...TERRACE, candidates: [38] } }).placements.length, 0);
});

test('terrace: a crossing deck through the platform centre cannot pass a corner-only check', () => {
  const tr = straight(), samples = sampleTrack(tr, 2);
  samples.push({ p: [38, 1, 10.9], t: [0, 0, 1], n: [0, 1, 0], b: [-1, 0, 0], w: 2 });
  const plan = planTerrace(tr, size, { samples, L: { ...TERRACE, candidates: [38] } });
  assert.equal(plan.placements.length, 0);
  assert.ok(plan.diagnostics.some((r) => r.reason === 'road-volume'));
});

test('terrace: missing dimensions, steep road and landmark conflicts produce no partial pair', () => {
  assert.equal(planTerrace(straight(), [12, NaN, 2]).placements.length, 0);
  const tr = straight(), frame = tr.frameAt;
  tr.frameAt = (s) => ({ ...frame(s), n: [0, 0.5, 0.866] });
  assert.equal(planTerrace(tr, size).placements.length, 0);
  assert.equal(planTerrace(straight(), size, { avoid: [{ B: [38, -2, 10.9], top: [38, 5, 10.9], r: 30 }] }).placements.length, 0);
});

test('terrace: real P6 pair clears every deck at finer spacing and the accepted colossi', () => {
  const manifest = JSON.parse(readFileSync(new URL('../art/manifest.json', import.meta.url), 'utf8'));
  const layout = JSON.parse(readFileSync(new URL('../tracks/threshold.colossi.json', import.meta.url), 'utf8'));
  const avoid = placeColossi(layout.colossi, track, (id) => manifest.find((r) => r.id === id), worldLook());
  const plan = planTerrace(track, size, { avoid });
  assert.equal(plan.placements.length, 2, JSON.stringify(plan.diagnostics));
  const roads = terraceRoadBoxes(sampleTrack(track, 0.5));
  for (const p of plan.placements) for (const b of [p.body, p.platform]) {
    assert.ok(roads.every((road) => !terraceBoxesOverlap(b, road)), 'clear at 0.5 m sampling');
    assert.ok(avoid.every((body) => !terraceHitsBody(b, body)));
  }
});
