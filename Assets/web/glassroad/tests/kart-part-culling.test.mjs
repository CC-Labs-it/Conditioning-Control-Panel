// Rigid kart parts (vehicle-life mechanisms, vehicle accents) are frustum culled per camera. Their bounds are
// exact in their own node space, so every drawn vertex stays inside the culling sphere at every pose.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as THREE from '../vendor/three/build/three.core.js';

const runtimeSource = readFileSync(new URL('../render/vehicleLife.js', import.meta.url), 'utf8')
  .replace("'three/webgpu'", JSON.stringify(new URL('../vendor/three/build/three.core.js', import.meta.url).href))
  .replace("'./vehicleLifeMath.js'", JSON.stringify(new URL('../render/vehicleLifeMath.js', import.meta.url).href))
  .replace("'./feedbackMeter.js'", JSON.stringify(new URL('../render/feedbackMeter.js', import.meta.url).href))
  .replace("'./vehicleAccents.js'", JSON.stringify('data:text/javascript,export function createVehicleAccents(){return null}'));
const { createVehicleLife } = await import('data:text/javascript;base64,' + Buffer.from(runtimeSource).toString('base64'));

function fixture(mode) {
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute([2,0,0, 3,0,0, 2,1,0, 2,0,1, 3,0,1, 2,1,1], 3));
  geometry.setAttribute('_life', new THREE.Uint8BufferAttribute([1,1,1,0,0,0], 1));
  geometry.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(new Array(24).fill(0), 4));
  geometry.setAttribute('skinWeight', new THREE.Float32BufferAttribute(Array.from({ length: 24 }, (_, i) => i % 4 === 0 ? 1 : 0), 4));
  geometry.setIndex([0,1,2,3,4,5]); geometry.computeVertexNormals();
  const bone = new THREE.Bone(); bone.position.set(2, .5, -1); bone.rotation.y = .4;
  const mesh = new THREE.SkinnedMesh(geometry, new THREE.MeshBasicMaterial()); mesh.bind(new THREE.Skeleton([bone]), new THREE.Matrix4());
  const root = new THREE.Group(); root.add(bone, mesh);
  return { root, bone, mesh, definition: { groups: [{ id: 1, name: 'm', mode, axis: [0, 1, 0], pivot: [2, 0, 0], rate: 2, amp: .4 }] } };
}

test('vehicle-life mechanisms cull per camera with bounds that hold every pose', () => {
  for (const mode of ['spin', 'swing', 'pulse', 'press']) {
    const f = fixture(mode), life = createVehicleLife([f.mesh], f.definition);
    const part = f.bone.children[0].children[0];
    assert.equal(part.frustumCulled, true, mode);
    const sphere = new THREE.Sphere(), v = new THREE.Vector3(), p = part.geometry.getAttribute('position');
    for (let i = 0; i < 40; i++) {
      life.update(1 / 20, { speed: 1, boost: i % 2, bump: i % 7 === 0 ? 1 : 0, air: i % 5 === 0 ? 1 : 0, time: i / 20 });
      f.root.updateMatrixWorld(true);
      sphere.copy(part.geometry.boundingSphere).applyMatrix4(part.matrixWorld);
      for (let k = 0; k < p.count; k++) assert.ok(sphere.distanceToPoint(v.fromBufferAttribute(p, k).applyMatrix4(part.matrixWorld)) <= 1e-6, mode + ' pose ' + i);
    }
    // A camera facing away does not see it; one facing it does.
    const frustum = new THREE.Frustum(), cam = new THREE.PerspectiveCamera(60, 1, .1, 100);
    const at = sphere.center.clone();
    for (const [dz, seen] of [[5, true], [-5, false]]) {
      cam.position.copy(at).add(new THREE.Vector3(0, 0, dz)); cam.lookAt(dz > 0 ? at : at.clone().add(new THREE.Vector3(0, 0, dz * 2)));
      cam.updateMatrixWorld(true);
      frustum.setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(cam.projectionMatrix, cam.matrixWorldInverse));
      assert.equal(frustum.intersectsObject(part), seen, mode + ' facing ' + dz);
    }
    life.dispose();
  }
});

test('vehicle accents and mechanisms never opt out of culling again', () => {
  for (const file of ['../render/vehicleAccents.js', '../render/vehicleLife.js'])
    assert.doesNotMatch(readFileSync(new URL(file, import.meta.url), 'utf8'), /frustumCulled\s*=\s*false/, file);
});
