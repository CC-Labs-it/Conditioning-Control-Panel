import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { S } from '../sim/state.js';
import { place, makeFeel, recorder, acts, run } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
const layout = getKeepLayout(track), bridge = layout.drawbridge;
for (const speed of [0, 12, 26, 40]) test('Drawbridge clears on throttle without a manual hop, approach speed ' + speed, () => {
  const state = place(track, { s: bridge.rampS0 - 35, speed }), events = recorder();
  run(state, track, makeFeel(), acts({ throttle: 1 }), 15, events,
    st => st[S.s] > bridge.landingS0 + 25 && st[S.grounded] > 0);
  assert.ok(state[S.s] > bridge.landingS0 + 25, 'reaches landing ' + state[S.s]);
  assert.equal(events.all('drop').length, 0, 'no rescue across the drawbridge');
  assert.ok(events.all('airborne').some(e => e.payload.cause === 'lip'), 'real launch');
  assert.ok(events.all('land').length, 'lands again');
});
test('Missing rampart rails allow a real fall and rescue', () => {
  const edge = layout.brokenEdges[0], s = (edge.s0 + edge.s1) / 2, f = track.frameAt(s), side = edge.side === 'L' ? -1 : 1;
  const state = place(track, { s, lateral: side * (f.w / 2 - 2), speed: 20, headingDeg: side * 70 }), events = recorder();
  run(state, track, makeFeel(), acts({ throttle: 1 }), 5, events, () => events.all('rescue-release').length > 0);
  assert.ok(events.all('drop').length, 'exposed edge causes a fall');
  assert.ok(events.all('rescue-release').length, 'rescue returns the racer');
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene, Matrix4, Vector3 } = await import('three/webgpu');
const { createKeepDrawbridge } = await import('../render/keepDrawbridge.js');
hooks.deregister();
test('Drawbridge chains and raised leaf leave the launch, flight and landing corridor clear', () => {
  const scene = new Scene(), view = createKeepDrawbridge(scene, track), solids = [], instance = new Matrix4();
  assert.equal(view.stats.chains, 2); assert.equal(view.stats.gapM, 20);
  view.group.updateMatrixWorld(true);
  view.group.traverse(o => {
    if (!o.isMesh || o.geometry.type !== 'BoxGeometry') return;
    for (let i = 0; i < (o.isInstancedMesh ? o.count : 1); i++) {
      if (o.isInstancedMesh) o.getMatrixAt(i, instance); else instance.identity();
      solids.push({ name: o.name, inverse: instance.clone().premultiply(o.matrixWorld).invert() });
    }
  });
  for (let s = bridge.rampS0; s < bridge.landingS0 + 28; s += 1) {
    const f = track.frameAt(s), gap = s > bridge.gapS0 + 1 && s < bridge.gapS1 - 1;
    for (const lateral of [0, -15.8, 15.8]) for (const height of gap ? [-5, 0, 1.4] : [.03, 1.4]) {
      const p = new Vector3(...f.p).addScaledVector(new Vector3(...f.b), lateral).addScaledVector(new Vector3(...f.n), height);
      for (const solid of solids) {
        const q = p.clone().applyMatrix4(solid.inverse);
        assert.ok(Math.abs(q.x) >= .499 || Math.abs(q.y) >= .499 || Math.abs(q.z) >= .499, solid.name + ' obstructs ' + s);
      }
    }
  }
  view.dispose(); assert.equal(scene.children.length, 0);
});
