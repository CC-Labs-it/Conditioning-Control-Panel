import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { createBoudoirRoadSurface, boudoirSurfaceZones, boudoirSurfaceWeights, boudoirDetailWeight, BOUDOIR_SURFACES } = await import('../render/boudoirMaterials.js');
const { uniform } = await import('three/tsl');
const { Vector2, Vector3 } = await import('three/webgpu'); hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const zones = boudoirSurfaceZones(track);

test('five physical finishes cover the complete authored lap, including wet and connecting segments', () => {
  assert.equal(zones.length, 7); assert.equal(zones[0].s0, 0); assert.equal(zones.at(-1).s1, track.lengthM);
  assert.equal(new Set(zones.map(z => z.kind)).size, 5);
  zones.forEach((z, i) => { assert.ok(z.s1 > z.s0 + 8); if (i) assert.equal(z.s0, zones[i - 1].s1); });
  const expected = { 'vanity-grid': 'vanity', 'perfume-court': 'vanity', 'drawer-hall': 'mirror',
    'shelf-climb': 'gallery', 'upper-gallery': 'gallery', 'wardrobe-descent': 'wardrobe',
    'pearl-slalom-cross': 'wardrobe', 'bath-stream': 'spa', 'bath-pool': 'spa', 'compacts': 'spa',
    'mirror-ramp': 'mirror', 'mirror-landing': 'mirror', home: 'vanity' };
  for (const landmark of getBoudoirLayout(track).landmarks) {
    const weights = boudoirSurfaceWeights(zones, (landmark.s0 + landmark.s1) / 2);
    assert.equal(Object.values(weights).filter(w => w > 0).length, 1, landmark.id);
    if (expected[landmark.id]) assert.equal(weights[expected[landmark.id]], 1, landmark.id);
  }
  assert.deepEqual(boudoirSurfaceZones({ id: 'keep' }), []);
  assert.equal(new Set(Object.values(BOUDOIR_SURFACES).map(v => v.base)).size, 5);
});

test('district fades conserve colour weight and remain continuous at the lap seam', () => {
  for (let s = -12; s < track.lengthM + 12; s += .75) {
    const weights = Object.values(boudoirSurfaceWeights(zones, s));
    assert.ok(Math.abs(weights.reduce((a, b) => a + b, 0) - 1) < 1e-9);
    assert.ok(weights.every(w => Number.isFinite(w) && w >= 0 && w <= 1));
  }
  for (let i = 1; i < zones.length; i++) {
    const weights = boudoirSurfaceWeights(zones, zones[i].s0);
    assert.ok(Math.abs(weights[zones[i - 1].kind] - .5) < 1e-10);
    assert.ok(Math.abs(weights[zones[i].kind] - .5) < 1e-10);
  }
  assert.deepEqual(boudoirSurfaceWeights(zones, -.01), boudoirSurfaceWeights(zones, .01));
  assert.deepEqual(boudoirSurfaceWeights(zones, 45), boudoirSurfaceWeights(zones, track.lengthM * 2 + 45));
});

test('grain and stitches fade before undersampling and all fine detail rests at long distance', () => {
  for (const period of [.6, .9, 4.5, 12]) {
    assert.equal(boudoirDetailWeight(.01, 20, period), 1);
    assert.equal(boudoirDetailWeight(period, 20, period), 0);
    assert.equal(boudoirDetailWeight(.01, 120, period), 0);
    let previous = 1;
    for (let px = 0; px <= period; px += period / 50) {
      const value = boudoirDetailWeight(px, 20, period);
      assert.ok(value >= 0 && value <= previous + 1e-12); previous = value;
    }
  }
  assert.ok(boudoirDetailWeight(.02, 75, .6) > 0 && boudoirDetailWeight(.02, 75, .6) < 1);
});

test('material graph accepts only static spatial inputs without clocks, textures or new uniforms', () => {
  const inputs = { p: uniform(new Vector2()), trueEdge: uniform(8), distance: uniform(20),
    view: uniform(new Vector3(0, 1, 1).normalize()), normal: uniform(new Vector3(0, 1, 0)) };
  const result = createBoudoirRoadSurface({ track, ...inputs });
  assert.ok(result.colour.isNode && result.sheen.isNode); assert.deepEqual(result.zones, zones);
  const seen = new Set(), pending = [result.colour, result.sheen], uniforms = new Set();
  while (pending.length) {
    const node = pending.pop(); if (seen.has(node)) continue; seen.add(node);
    assert.ok(!node.isTextureNode, 'finish must not allocate or sample a texture');
    if (node.isUniformNode) uniforms.add(node);
    for (const child of node.getChildren()) pending.push(child);
  }
  assert.ok(seen.size < 1600, 'the five finishes share a bounded shader graph');
  assert.deepEqual(uniforms, new Set(Object.values(inputs)));
  assert.equal(createBoudoirRoadSurface({ track: { id: 'keep' }, ...inputs }), null);
});
