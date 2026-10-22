/* render/materials/road.js - the glass ribbon: translucent, flowing colour bands along s.
 * uv: u = lateral metres, v = s metres (CONTRACT.md MeshData). The band phase is integrated on the
 * CPU (scene.js) so a speed change never jumps the pattern. contrast and the phase rate are what
 * Gentle halves. With the art set loaded the bands are Astra's wave mask (road-bands-mask): its
 * crests run across the road, one tile per artRoadTileM (image x along s, image y lateral), R maps
 * through the palette wheel and the seam rides each crest; the roughness map adds a faint glass haze.
 * Without it the procedural warp stands in.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uniform, uv, sin, cos, fract, mix, smoothstep, float, vec2, texture, PI2 } from 'three/tsl';
import { LOOK } from '../look.js';

export function createRoadMaterial(pal, art = null) {
  const u = { phase: uniform(0), contrast: uniform(1), bright: uniform(0) };
  const p = uv();
  const along = p.y.sub(u.phase);
  const bands = art && art.tex('road-bands-mask'), rough = art && art.tex('road-glass-roughness');
  const [tu, tv] = LOOK.artRoadTileM;
  const crest = bands ? texture(bands, vec2(along.div(tv), p.x.div(tu))).r : null;
  const f = bands
    ? fract(crest.mul(LOOK.artBandSpan))
    : fract(along.div(LOOK.bandLenM).add(sin(p.x.mul(LOOK.bandWarpPerM)).mul(LOOK.bandWarpAmp)));
  // three colours on a cosine wheel; the weights always sum to 1.5
  const w = (k) => cos(f.sub(k / 3).mul(PI2)).mul(0.5).add(0.5);
  const band = pal.col(0).mul(w(0)).add(pal.col(1).mul(w(1))).add(pal.col(2).mul(w(2))).div(1.5);
  const avg = pal.col(0).add(pal.col(1)).add(pal.col(2)).div(3);
  const seam = (bands ? smoothstep(0.9, 1, crest) : smoothstep(0.94, 1, fract(f.mul(3)))).mul(u.contrast);
  const haze = rough ? mix(float(1 - LOOK.artHaze), float(1 + LOOK.artHaze), texture(rough, p.div(LOOK.artHazeTileM)).r) : float(1);
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide });
  m.colorNode = mix(avg, band, u.contrast).add(seam.mul(LOOK.seamGain)).mul(u.bright.add(1)).mul(haze).mul(LOOK.roadGain);
  m.opacityNode = float(LOOK.roadAlpha).add(seam.mul(1 - LOOK.roadAlpha));
  return { material: m, uniforms: u };
}
