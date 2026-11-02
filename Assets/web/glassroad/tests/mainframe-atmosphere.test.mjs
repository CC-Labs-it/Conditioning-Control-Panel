import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createMainframeAtmosphere, mainframeAtmosphereLayout, mainframeCodeAtlas } = await import('../render/mainframeAtmosphere.js');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));

test('green hexes and Matrix rain span the lap and clear every deck with their complete motion', () => {
  const layout = mainframeAtmosphereLayout(track), phone = mainframeAtmosphereLayout(track, { phone: true });
  assert.deepEqual(layout, mainframeAtmosphereLayout(track));
  assert.ok(layout.hexes.length >= 60); assert.ok(layout.cascades.length >= 60);
  assert.ok(phone.hexes.length < layout.hexes.length && phone.cascades.length < layout.cascades.length);
  assert.ok(layout.hexes.at(-1).s > track.lengthM * .95);
  for (let s = 0; s < track.lengthM; s += 10) {
    const f = track.frameAt(s);
    for (const row of [...layout.hexes, ...layout.cascades]) {
      const radius = row.radius ? row.radius + 2 : Math.hypot(row.width, row.height) / 2 + 2;
      const d = row.p.map((v, k) => v - f.p[k]), project = a => d.reduce((sum, n, k) => sum + n * a[k], 0);
      assert.ok(Math.abs(project(f.t)) > radius + 12 || project(f.n) < -radius - 6 ||
        project(f.n) > radius + 14 || Math.abs(project(f.b)) > f.w / 2 + radius + 8);
    }
  }
  assert.deepEqual(mainframeAtmosphereLayout({ id: 'keep' }), { hexes: [], cascades: [] });
});

test('original hexadecimal glyph atlas is deterministic, transparent and bounded to 128 KiB', () => {
  const atlas = mainframeCodeAtlas();
  assert.equal(atlas.width, 128); assert.equal(atlas.height, 256);
  assert.equal(atlas.data.byteLength, 128 * 1024);
  assert.deepEqual(atlas.data, mainframeCodeAtlas().data);
  let written = 0, blank = 0;
  for (let i = 0; i < atlas.data.length; i += 4) {
    if (atlas.data[i + 3]) { written++; assert.equal(atlas.data[i + 1], 255); }
    else blank++;
  }
  assert.ok(written > 3000 && blank > written, 'readable separate glyph strokes over transparent space');
});

test('four shared draws animate, pause, calm, reset and dispose only their owned resources', () => {
  const a = createMainframeAtmosphere(track), meshes = a.object.children;
  assert.equal(meshes.length, 4); assert.ok(meshes.every(mesh => mesh.isInstancedMesh));
  const mesh = meshes[0], start = [...mesh.instanceMatrix.array];
  assert.ok(meshes[1].material.opacityNode && meshes[2].material.opacityNode, 'glyph texture alpha drives actual shader opacity');
  a.update(.1); const moved = [...mesh.instanceMatrix.array]; assert.notDeepEqual(moved, start);
  a.update(.1, { paused: true }); assert.deepEqual([...mesh.instanceMatrix.array], moved);
  a.update(.1, { reducedMotion: true }); const calm = [...mesh.instanceMatrix.array];
  a.update(.1, { reducedMotion: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.update(.1, { gentle: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.reset(); assert.deepEqual([...mesh.instanceMatrix.array], start);
  let geometries = 0, materials = 0;
  for (const child of meshes) {
    child.geometry.addEventListener('dispose', () => geometries++);
    child.material.addEventListener('dispose', () => materials++);
  }
  a.dispose(); a.dispose(); a.update(.1); a.reset();
  assert.equal(geometries, 4); assert.equal(materials, 4);
});
