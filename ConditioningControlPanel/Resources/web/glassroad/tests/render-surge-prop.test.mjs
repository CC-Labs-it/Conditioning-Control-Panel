import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { S, BOOST, createState } from '../sim/state.js';

const hooks = registerHooks({ resolve(s, c, next) {
  const files = { three: 'build/three.webgpu.min.js', 'three/webgpu': 'build/three.webgpu.min.js', 'three/tsl': 'build/three.tsl.min.js' };
  const file = files[s] || (s.startsWith('three/addons/') ? 'addons/' + s.slice(13) : null);
  return file ? { url: new URL('../vendor/three/' + file, import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { buildKarts } = await import('../render/karts.js');
const { BoxGeometry } = await import('three/webgpu');
hooks.deregister();

test('Surge leaves flat gold chevrons, pauses and resets without a sphere aura', () => {
  const karts = buildKarts(null, 1, new BoxGeometry()), k = karts.list[0], st = createState();
  st[S.boostKind] = BOOST.PAD; k.react.surgeAt = 0;
  for (let i = 0; i < 40; i++) karts.pose(k, st, {}, 1 / 60, 2, {});
  assert.equal(k.aura.mesh.visible, false); assert.equal(k.surge.mesh.visible, true);
  const g = k.surge.mesh.geometry; g.computeBoundingBox();
  assert.equal(g.attributes.position.count / 3, 12);
  assert.equal(g.boundingBox.min.y, g.boundingBox.max.y, 'flat below the saucer');
  assert.ok(g.boundingBox.min.z > 0, 'behind the kart, away from the driving line');
  const frozen = [k.surge.time, k.surge.alpha, k.surge.mesh.position.z];
  karts.pose(k, st, {}, .1, 2, { paused: true });
  assert.deepEqual([k.surge.time, k.surge.alpha, k.surge.mesh.position.z], frozen);
  karts.pose(k, st, {}, .1, 2, { gentle: true }); assert.equal(k.surge.mesh.position.z, 0);
  const gentleTime = k.surge.time;
  karts.pose(k, st, {}, .1, 2, { gentle: true }); assert.equal(k.surge.time, gentleTime);
  karts.resetReactions(); assert.equal(k.surge.mesh.visible, false); assert.equal(k.surge.alpha, 0);
  k.react.surgeAt = 0;
  karts.pose(k, st, {}, .1, 2, { gentle: true }); karts.pose(k, st, {}, .1, 2, { gentle: true });
  assert.ok(k.surge.alpha < 1, 'Gentle cannot rise fully in less than 300ms');
  let disposed = 0; g.addEventListener('dispose', () => disposed++);
  karts.dispose(); karts.dispose(); assert.equal(disposed, 1);
});
