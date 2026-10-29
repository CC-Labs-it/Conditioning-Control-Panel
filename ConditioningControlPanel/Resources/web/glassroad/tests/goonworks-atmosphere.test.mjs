import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hook = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createGoonworksAtmosphere, goonworksAtmosphereLayout, GOON_SKY } = await import('../render/goonworksAtmosphere.js');
const { BackSide, Vector3 } = await import('three/webgpu');
hook.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));

test('mixed sky keepsakes cover all five themes, the complete lap and clear every upper deck', () => {
  const plan = goonworksAtmosphereLayout(track), phone = goonworksAtmosphereLayout(track, { phone: true });
  assert.deepEqual(plan, goonworksAtmosphereLayout(track));
  assert.equal(plan.rows.length, 75); assert.equal(phone.rows.length, 35);
  assert.deepEqual([...new Set(plan.rows.map(row => row.kind))].sort(), ['bow', 'bubble', 'cloud', 'hex', 'lock']);
  for (const row of plan.rows) assert.ok(row.p[1] - row.scale * 1.6 - 1.2 > plan.roof + 20);
  assert.ok(plan.rows[0].s < track.lengthM * .02 && plan.rows.at(-1).s > track.lengthM * .98);
  assert.deepEqual(goonworksAtmosphereLayout({ id: 'keep' }), { rows: [], roof: 0 });
});

test('spiral is an inward full sky that follows position and draws behind the world, ahead of old backdrop', () => {
  const a = createGoonworksAtmosphere(track), sky = a.object.children[0];
  assert.equal(sky.geometry.parameters.radius, GOON_SKY.radius);
  assert.equal(sky.geometry.parameters.thetaLength, Math.PI);
  assert.equal(sky.geometry.parameters.phiLength, Math.PI * 2);
  assert.equal(sky.material.side, BackSide); assert.equal(sky.material.depthWrite, false);
  assert.equal(sky.material.depthTest, false); assert.equal(sky.renderOrder, -1);
  assert.equal(sky.userData.screenPlacementIgnore, true);
  const point = new Vector3(40, 15, 80); a.update(.1, { playerPos: point });
  assert.deepEqual(sky.position.toArray(), point.toArray());
  assert.equal(a.uniforms.clock.value, .1);
  const nodes = new Set(); sky.material.colorNode.traverse(n => nodes.add(n));
  assert.ok(nodes.has(a.uniforms.clock) && nodes.has(a.uniforms.contrast));
  a.dispose();
});

test('pause freezes sky and keepsakes, motion-off calms both and reset/dispose release owned resources once', () => {
  const a = createGoonworksAtmosphere(track), mesh = a.object.children[2], start = [...mesh.instanceMatrix.array];
  a.update(.1); assert.notDeepEqual([...mesh.instanceMatrix.array], start);
  const moving = [...mesh.instanceMatrix.array], clock = a.uniforms.clock.value;
  a.update(.1, { paused: true }); assert.deepEqual([...mesh.instanceMatrix.array], moving);
  assert.equal(a.uniforms.clock.value, clock);
  a.update(.1, { gentle: true }); const quiet = [...mesh.instanceMatrix.array];
  assert.equal(a.uniforms.clock.value, 0); assert.equal(a.uniforms.contrast.value, .38);
  a.update(.1, { reducedMotion: true }); assert.deepEqual([...mesh.instanceMatrix.array], quiet);
  assert.equal(a.uniforms.clock.value, 0);
  a.reset(); assert.deepEqual([...mesh.instanceMatrix.array], start);
  assert.equal(a.uniforms.contrast.value, 1); assert.equal(a.stats.draws, 6);
  assert.equal(new Set(a.object.children.map(m => m.material)).size, 3);
  let geometryFrees = 0, materialFrees = 0;
  mesh.geometry.addEventListener('dispose', () => geometryFrees++);
  mesh.material.addEventListener('dispose', () => materialFrees++);
  a.dispose(); a.dispose(); a.update(.1); a.reset();
  assert.equal(geometryFrees, 1); assert.equal(materialFrees, 1);
});
