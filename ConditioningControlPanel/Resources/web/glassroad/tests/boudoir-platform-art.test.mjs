import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', three: 'three.webgpu.min.js' };
  if (files[specifier]) return { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true };
  if (specifier.startsWith('three/addons/')) return { url: new URL('../vendor/three/addons/' + specifier.slice(13), import.meta.url).href, shortCircuit: true };
  return next(specifier, context);
} });
const { buildStones } = await import('../render/stones.js');
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('three/addons/libs/meshopt_decoder.module.js');
const { Raycaster, Vector3 } = await import('three/webgpu');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const bytes = readFileSync(new URL('../art/models/stone-boudoir.glb', import.meta.url));
const source = await new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
const art = { model: id => id === 'stone-boudoir' ? source : null };

test('Actual round compact receives a rectangular tray covering every physically supported corner', () => {
  source.scene.updateMatrixWorld(true);
  const ray = new Raycaster(new Vector3(2, 3, 2), new Vector3(0, -1, 0));
  assert.equal(ray.intersectObject(source.scene, true).length, 0, 'the real round asset has no square corner');
  const view = buildStones(track, art), holder = view.group.children[0], row = view.rows[0];
  assert.equal(view.group.children.length, 7); assert.ok(holder.getObjectByName('boudoir-compact-tray'));
  holder.updateMatrixWorld(true);
  for (const x of [-2.499, -2, -1, 0, 1, 2, 2.499]) for (const z of [-2.499, -2, -1, 0, 1, 2, 2.499]) {
    const origin = holder.localToWorld(new Vector3(x, 3, z)), down = new Vector3(...row.n).negate();
    ray.set(origin, down);
    const hit = ray.intersectObject(holder, true)[0]; assert.ok(hit, 'visible support at ' + x + ',' + z);
    const local = holder.worldToLocal(hit.point.clone());
    assert.ok(local.y >= .509 && local.y <= .801, 'matching physical surface height ' + local.y);
  }
  for (const [x, z] of [[2.501, 2.501], [-2.501, -2.501]]) {
    ray.set(holder.localToWorld(new Vector3(x, 3, z)), new Vector3(...row.n).negate());
    assert.equal(ray.intersectObject(holder, true).length, 0, 'visual tray stays within physical footprint');
  }
  assert.equal(holder.scale.x * 5, 32); assert.equal(holder.scale.z * 5, 26);
  view.dispose();
});

test('Tray follows compact landing motion and shares owned resources with idempotent disposal', () => {
  const view = buildStones(track, art), holders = view.group.children, tray = holders[0].getObjectByName('boudoir-compact-tray');
  const trays = holders.map(h => h.getObjectByName('boudoir-compact-tray'));
  assert.equal(new Set(trays.map(t => t.geometry)).size, 1); assert.equal(new Set(trays.map(t => t.material)).size, 1);
  view.group.updateMatrixWorld(true); const start = tray.getWorldPosition(new Vector3());
  view.hit(view.rows[0].stone); view.update(.3); view.group.updateMatrixWorld(true);
  const down = tray.getWorldPosition(new Vector3()); assert.ok(start.y - down.y > .27);
  view.update(.5, { paused: true }); view.group.updateMatrixWorld(true);
  assert.deepEqual(tray.getWorldPosition(new Vector3()), down);
  view.reset(); view.group.updateMatrixWorld(true); assert.ok(tray.getWorldPosition(new Vector3()).distanceTo(start) < .001);
  let geometry = 0, material = 0, sourceGeometry = 0;
  tray.geometry.addEventListener('dispose', () => geometry++); tray.material.addEventListener('dispose', () => material++);
  const originals = new Set(); source.scene.traverse(o => { if (o.geometry) originals.add(o.geometry); });
  for (const g of originals) g.addEventListener('dispose', () => sourceGeometry++);
  view.dispose(); view.dispose(); assert.equal(geometry, 1); assert.equal(material, 1); assert.equal(sourceGeometry, 0);
});
