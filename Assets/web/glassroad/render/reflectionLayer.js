// render/reflectionLayer.js - what the water probe sees.
// The cube capture used to compile a no-shadow variant of EVERY material in the scene (a second program
// per material) and draw the whole scene six times at warm, then one face every 160 ms. A 64 px cube
// only ever shows broad surfaces, so the probe gets its own layer, assigned once at warm to objects whose
// world bounding radius reaches REFLECT_MIN_M: road, rails, edges, sky, water, colossi, scatter rows and
// the big dressing. Karts, items, cue windows, marquees, veils and wakes fall out by size.
// userData.reflect = true forces an object in, false keeps it out. Objects off the default layer (the
// layer-2 shadow catchers, whose ShadowNodeMaterial cannot compile in a no-shadow pass) are never touched.
import { Sphere } from 'three/webgpu';

export const REFLECT_LAYER = 3;
export const REFLECT_MIN_M = 3;
const sphere = new Sphere();

/** World-space bounding radius of a mesh (every instance of an InstancedMesh); sprites have none. */
export function reflectedRadius(object) {
  if (!object.isMesh || !object.geometry) return 0;
  if (object.isInstancedMesh) {
    if (!object.boundingSphere) object.computeBoundingSphere();
    sphere.copy(object.boundingSphere);
  } else {
    if (!object.geometry.boundingSphere) object.geometry.computeBoundingSphere();
    sphere.copy(object.geometry.boundingSphere);
  }
  return sphere.radius * object.matrixWorld.getMaxScaleOnAxis();
}

/** Enable REFLECT_LAYER on every object the probe should see; returns how many went in and out. */
export function assignReflectionLayer(scene, { minM = REFLECT_MIN_M, exclude = null } = {}) {
  const excluded = new Set();
  exclude?.traverse?.(o => excluded.add(o));
  scene.updateMatrixWorld?.(true);
  let on = 0, off = 0;
  scene.traverse?.(o => {
    if (!(o.isMesh || o.isSprite) || excluded.has(o)) return;
    if ((o.layers.mask & 1) === 0) return;
    const want = typeof o.userData.reflect === 'boolean' ? o.userData.reflect : reflectedRadius(o) >= minM;
    if (want) { o.layers.enable(REFLECT_LAYER); on++; } else { o.layers.disable(REFLECT_LAYER); off++; }
  });
  return { on, off };
}
