import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createKeepAtmosphere, keepAtmosphereLayout } = await import('../render/keepAtmosphere.js');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));

test('Keep dreamscape is deterministic, covers the lap and clears the highest road deck', () => {
  const plan = keepAtmosphereLayout(track), phone = keepAtmosphereLayout(track, { phone: true });
  assert.deepEqual(plan, keepAtmosphereLayout(track));
  assert.equal(plan.locks.length, 56); assert.equal(phone.locks.length, 28);
  assert.equal(plan.clouds.length, 224); assert.equal(plan.pixels.length, 280);
  for (const row of plan.locks) assert.ok(row.p[1] - row.r * 1.2 - 2 > plan.roof + 20);
  for (const row of plan.clouds) assert.ok(row.p[1] - row.size[1] - 2 > plan.roof + 50);
  assert.deepEqual(keepAtmosphereLayout({ id: 'mainframe' }), { clouds: [], pixels: [], locks: [] });
});

test('Keep cloud and lock motion pauses, calms and resets without moving the backdrop', () => {
  const a = createKeepAtmosphere(track), mesh = a.object.children[2];
  const start = [...mesh.instanceMatrix.array];
  a.update(.1); assert.notDeepEqual([...mesh.instanceMatrix.array], start);
  const moving = [...mesh.instanceMatrix.array];
  a.update(.1, { paused: true }); assert.deepEqual([...mesh.instanceMatrix.array], moving);
  a.update(.1, { gentle: true }); const calm = [...mesh.instanceMatrix.array];
  a.update(.1, { gentle: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.update(.1, { reducedMotion: true }); assert.deepEqual([...mesh.instanceMatrix.array], calm);
  a.reset(); assert.deepEqual([...mesh.instanceMatrix.array], start);
  assert.equal(a.object.position.length(), 0); assert.equal(a.stats.draws, 9);
  assert.equal(mesh.count, 56); assert.equal(new Set(a.object.children.map(m => m.material)).size, 6);
  let frees = 0; mesh.geometry.addEventListener('dispose', () => frees++);
  a.dispose(); a.dispose(); a.update(.1); a.reset(); assert.equal(frees, 1);
});

test('padlocks have open curved shackles, dark keyholes on both faces and brass inlay', () => {
  const a = createKeepAtmosphere(track, { phone: true }), names = a.object.children.map(m => m.name);
  for (const name of ['keep-floating-lock-shackles', 'keep-lock-keyholes', 'keep-lock-reverse-keyholes', 'keep-lock-brass-inlay']) assert.ok(names.includes(name));
  const shackle = a.object.children.find(m => m.name.endsWith('shackles'));
  assert.equal(shackle.geometry.parameters.arc, Math.PI);
  a.dispose();
});
