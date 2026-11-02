/* render/lens.js - the lens rule as TSL nodes for every additive sprite cloud (numbers and the JS mirror in
 * render/lensMath.js). One shared uniform (lensView = 2 tan(vFov / 2), the view height at 1 m) follows the
 * rendering camera: scene.js calls setLensCamera once per frame.
 */
import { uniform, modelViewMatrix, vec4, min, mix, smoothstep, varying } from 'three/tsl';
import { LENS } from './lensMath.js';

export { LENS };

/** The view height at 1 m in front of the camera; 1.4 is a 70 degree view. */
export const lensView = uniform(1.4);

/** Per frame, from the rendering camera (a PerspectiveCamera; anything else keeps the last value). */
export function setLensCamera(cam) {
  if (cam && cam.isPerspectiveCamera && cam.fov > 0) lensView.value = 2 * Math.tan(cam.fov * Math.PI / 360) / (cam.zoom || 1);
}

/** Depth (metres in front of the camera) of a world (or model) position node, as a varying. */
export const lensDepth = (posNode) => varying(modelViewMatrix.mul(vec4(posNode, 1)).z.negate().max(0.01));

/** The size node capped to the lens share of the view height at depth d. */
export function lensScale(sizeNode, d, L = LENS) {
  const frac = mix(L.nearFrac, L.farFrac, smoothstep(L.nearM, L.farM, d));
  return min(sizeNode, d.mul(lensView).mul(frac));
}

/** Alpha share at depth d: 0 at the lens, dim inside dimM, 1 beyond. */
export function lensAlpha(d, L = LENS) {
  return smoothstep(L.fadeM[0], L.fadeM[1], d).mul(mix(L.dim, 1, smoothstep(L.dimM[0], L.dimM[1], d)));
}
