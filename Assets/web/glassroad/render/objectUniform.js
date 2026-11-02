// render/objectUniform.js - one material for a whole pool of copies.
// r186 keys its node builders on node ids, so a material per copy builds the same shader graph again for
// every copy: 1,096 of a Threshold load's 1,704 graph builds emitted source already built (about 4.6 s on
// Edge WebGPU, more on Firefox). A pool shares ONE material instead, and the per-copy values are read per
// drawn object from mesh.userData.fx, the way render/items/materials.js ghost() reads echoA.
import { uniform } from 'three/tsl';

/** A uniform fed from object.userData.fx[name].value of the object being drawn (fallback when it has none). */
export function objectUniform(name, fallback) {
  return uniform(fallback).onObjectUpdate(({ object }) => {
    const fx = object && object.userData.fx, slot = fx && fx[name];
    return slot ? slot.value : fallback;
  });
}

/** Per-copy state for a shared material: { name: { value } } holders on mesh.userData.fx, written like uniforms. */
export function objectState(target, defaults) {
  const fx = {};
  for (const name of Object.keys(defaults)) fx[name] = { value: defaults[name] };
  for (const object of [].concat(target)) object.userData.fx = fx;
  return fx;
}
