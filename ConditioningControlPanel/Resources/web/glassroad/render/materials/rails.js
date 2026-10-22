/* render/materials/rails.js - rail ribbons: the palette's first (saturated) colour, fading out toward the top,
 * and fading out within LOOK.railNearM of the camera so a rail beside the eye never walls the view.
 * uv: u = metres up, v = s metres.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uv, clamp, mix, float, smoothstep, positionWorld, cameraPosition } from 'three/tsl';
import { LOOK } from '../look.js';

export function createRailMaterial(pal) {
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide, depthWrite: false });
  m.colorNode = pal.col(0).mul(LOOK.railGain);
  const near = smoothstep(LOOK.railNearM[0], LOOK.railNearM[1], positionWorld.distance(cameraPosition));
  m.opacityNode = mix(float(LOOK.railAlpha), float(0), clamp(uv().x.div(LOOK.railHeightM), 0, 1)).mul(near);
  return { material: m };
}
