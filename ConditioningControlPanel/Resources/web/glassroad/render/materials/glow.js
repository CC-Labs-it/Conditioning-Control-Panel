/* render/materials/glow.js - an additive glow with an opacity uniform (rescue tendril, pad ring). */
import { MeshBasicNodeMaterial, AdditiveBlending, DoubleSide, Color } from 'three/webgpu';
import { uniform, vec3 } from 'three/tsl';

export function createGlowMaterial(hex) {
  const u = { opacity: uniform(0) };
  const c = new Color(hex);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  m.colorNode = vec3(c.r, c.g, c.b).mul(u.opacity);
  return { material: m, uniforms: u };
}
