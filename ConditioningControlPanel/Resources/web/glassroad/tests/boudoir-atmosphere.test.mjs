import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createBoudoirAtmosphere, boudoirAtmosphereLayout } = await import('../render/boudoirAtmosphere.js');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));

test('Boudoir keepsakes cover the lap with deterministic near/far placement and clear all decks', () => {
  const plan = boudoirAtmosphereLayout(track), phone = boudoirAtmosphereLayout(track, { phone: true });
  assert.deepEqual(plan, boudoirAtmosphereLayout(track));
  assert.ok(plan.decorations.length >= 70); assert.ok(phone.decorations.length < plan.decorations.length);
  assert.deepEqual([...new Set(plan.decorations.map(r => r.kind))].sort(), ['bow', 'compact', 'lace', 'lipstick', 'pearls']);
  assert.ok(plan.decorations.some(r => r.near) && plan.decorations.some(r => !r.near));
  const samples = [];
  for (let s = 0; s < track.lengthM; s += 10) samples.push(track.frameAt(s));
  for (const fork of track.forks || []) samples.push(...fork.samples(10));
  for (const f of samples) {
    for (const row of plan.decorations) {
      const d = row.p.map((v, i) => v - f.p[i]), dot = v => d.reduce((sum, n, i) => sum + n * v[i], 0), r = row.radius;
      assert.ok(Math.abs(dot(f.t)) > r + 12 || dot(f.n) > r + 14 || dot(f.n) < -r - 6 || Math.abs(dot(f.b)) > f.w / 2 + r + 8);
    }
  }
  assert.deepEqual(boudoirAtmosphereLayout({ id: 'keep' }), { decorations: [] });
});

test('Boudoir motion respects pause and quiet modes, resets and releases shared resources once', () => {
  const a = createBoudoirAtmosphere(track), mesh = a.object.children[0], start = [...mesh.instanceMatrix.array];
  a.update(.1); assert.notDeepEqual([...mesh.instanceMatrix.array], start);
  const moving = [...mesh.instanceMatrix.array]; a.update(.1, { paused: true }); assert.deepEqual([...mesh.instanceMatrix.array], moving);
  a.update(.1, { gentle: true }); const calm = [...mesh.instanceMatrix.array];
  a.update(.1, { gentle: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.update(.1, { reducedMotion: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.reset(); assert.deepEqual([...mesh.instanceMatrix.array], start);
  assert.equal(a.stats.draws, 10); assert.equal(new Set(a.object.children.map(m => m.material)).size, 4);
  assert.equal(a.object.position.length(), 0);
  let geometryFrees = 0, materialFrees = 0;
  mesh.geometry.addEventListener('dispose', () => geometryFrees++); mesh.material.addEventListener('dispose', () => materialFrees++);
  a.dispose(); a.dispose(); a.update(.1); a.reset(); assert.equal(geometryFrees, 1); assert.equal(materialFrees, 1);
});

test('vanity pieces have dimensional open bows, scalloped lace, pearls and open cosmetic compacts', () => {
  const a = createBoudoirAtmosphere(track, { phone: true });
  for (const mesh of a.object.children) {
    assert.ok(mesh.count > 0); assert.ok(mesh.geometry.attributes.normal);
    for (const v of mesh.geometry.attributes.position.array) assert.ok(Number.isFinite(v));
  }
  const bow = a.object.children.find(m => m.name === 'boudoir-satin-bows'); bow.geometry.computeBoundingBox();
  assert.ok(bow.geometry.boundingBox.min.y < -1.6 && bow.geometry.boundingBox.max.x > 1.7);
  assert.ok(a.object.children.find(m => m.name === 'boudoir-open-compact-lids'));
  a.dispose();
});
