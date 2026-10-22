/* render/materials/rails.js - rail ribbons: the palette's first (saturated) colour, fading out toward the top,
 * and fading out within LOOK.railNearM of the camera so a rail beside the eye never walls the view.
 * uv: u = metres up, v = s metres. With the art set, rail-glow-mask adds a bright core line across
 * the ribbon's height.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uv, clamp, mix, float, vec2, texture, smoothstep, positionWorld, cameraPosition } from 'three/tsl';
import { LOOK } from '../look.js';

export function createRailMaterial(pal, art = null) {
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide, depthWrite: false });
  const glow = art && art.tex('rail-glow-mask');
  const h = clamp(uv().x.div(LOOK.railHeightM), 0, 1);
  const core = glow ? texture(glow, vec2(h, uv().y.div(LOOK.artRoadTileM[1]))).r : float(0);
  m.colorNode = pal.col(0).mul(LOOK.railGain).add(core.mul(LOOK.artRailCore));
  const near = smoothstep(LOOK.railNearM[0], LOOK.railNearM[1], positionWorld.distance(cameraPosition));
  m.opacityNode = mix(float(LOOK.railAlpha), float(0), h).add(core.mul(LOOK.artRailCoreAlpha)).mul(near);
  return { material: m };
}
