import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createThresholdAtmosphere, thresholdAtmosphereLayout, thresholdStoneColour, THRESHOLD_PINKS } = await import('../render/thresholdAtmosphere.js');
const { stoneLayout, buildStones } = await import('../render/stones.js');
const { Group, Mesh, BoxGeometry, MeshStandardMaterial } = await import('three/webgpu');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));

test('Threshold atmosphere surrounds the whole lap with clouds, distant bubbles and close parallax', () => {
  const layout = thresholdAtmosphereLayout(track), phone = thresholdAtmosphereLayout(track, { phone: true });
  assert.deepEqual(layout, thresholdAtmosphereLayout(track), 'fixed seeded world positions');
  assert.ok(layout.clouds.length > 180 && layout.bubbles.length > 80);
  assert.ok(layout.bubbles.filter(b => b.close).length > 15);
  assert.ok(phone.clouds.length < layout.clouds.length && phone.bubbles.length < layout.bubbles.length);
  for (let s = 0; s < track.lengthM; s += 10) {
    const f = track.frameAt(s);
    for (const row of [...layout.bubbles, ...layout.clouds]) {
      const r = row.r * (row.colour === undefined ? 1 : 1.5) + 1.5;
      const d = row.p.map((v, i) => v - f.p[i]), dot = v => d.reduce((sum, n, i) => sum + n * v[i], 0);
      assert.ok(Math.abs(dot(f.t)) > r + 12 || dot(f.n) < -r - 5 || dot(f.n) > r + 12 || Math.abs(dot(f.b)) > f.w / 2 + r + 3, 'full scenery motion clears every deck');
    }
  }
  assert.deepEqual(thresholdAtmosphereLayout({ id: 'keep' }), { clouds: [], bubbles: [] });
});

test('ambient motion pauses, calms, resets and releases shared resources once', () => {
  const a = createThresholdAtmosphere(track), mesh = a.object.children[1];
  const start = [...mesh.instanceMatrix.array];
  a.update(.1); assert.notDeepEqual([...mesh.instanceMatrix.array], start);
  const moving = [...mesh.instanceMatrix.array];
  a.update(.1, { paused: true }); assert.deepEqual([...mesh.instanceMatrix.array], moving);
  a.update(.1, { gentle: true }); const calm = [...mesh.instanceMatrix.array];
  a.update(.1, { gentle: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.reset(); assert.deepEqual([...mesh.instanceMatrix.array], start);
  let disposed = 0; mesh.geometry.addEventListener('dispose', () => disposed++);
  a.dispose(); a.dispose(); assert.equal(disposed, 1);
});

test('accepted island models and missing-art fallback share the three lane colours without repainting source art', () => {
  const scene = new Group(), source = new MeshStandardMaterial({ color: '#ddccaa' }); source.name = 'surface';
  const geometry = new BoxGeometry(5, .8, 5); scene.add(new Mesh(geometry, source));
  const art = { model: () => ({ scene }) }, models = buildStones(track, art), fallback = buildStones(track);
  const rows = stoneLayout(track), seen = new Set();
  models.group.children.forEach((holder, i) => {
    const hex = '#' + holder.children[0].children[0].material.color.getHexString();
    assert.equal(hex, thresholdStoneColour(track, rows[i])); seen.add(hex);
  });
  assert.deepEqual([...seen].sort(), [...THRESHOLD_PINKS].sort());
  assert.equal(source.color.getHexString(), 'ddccaa');
  assert.ok(fallback.group.children[0].instanceColor);
  models.dispose(); fallback.dispose(); geometry.dispose(); source.dispose();
});
