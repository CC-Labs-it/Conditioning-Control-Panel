/* render/materials/road.js - the glass ribbon: translucent, flowing colour bands along s.
 * uv: u = lateral metres, v = s metres (CONTRACT.md MeshData). The band phase is integrated on the
 * CPU (scene.js) so a speed change never jumps the pattern. contrast and the phase rate are what
 * Gentle halves.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uniform, uv, sin, cos, fract, mix, smoothstep, float, PI2 } from 'three/tsl';
import { LOOK } from '../look.js';

export function createRoadMaterial(pal) {
  const u = { phase: uniform(0), contrast: uniform(1), bright: uniform(0) };
  const p = uv();
  const warp = sin(p.x.mul(LOOK.bandWarpPerM)).mul(LOOK.bandWarpAmp);
  const f = fract(p.y.sub(u.phase).div(LOOK.bandLenM).add(warp));
  // three colours on a cosine wheel; the weights always sum to 1.5
  const w = (k) => cos(f.sub(k / 3).mul(PI2)).mul(0.5).add(0.5);
  const band = pal.col(0).mul(w(0)).add(pal.col(1).mul(w(1))).add(pal.col(2).mul(w(2))).div(1.5);
  const avg = pal.col(0).add(pal.col(1)).add(pal.col(2)).div(3);
  const seam = smoothstep(0.94, 1, fract(f.mul(3))).mul(u.contrast);
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide });
  m.colorNode = mix(avg, band, u.contrast).add(seam.mul(LOOK.seamGain)).mul(u.bright.add(1)).mul(LOOK.roadGain);
  m.opacityNode = float(LOOK.roadAlpha).add(seam.mul(1 - LOOK.roadAlpha));
  return { material: m, uniforms: u };
}
