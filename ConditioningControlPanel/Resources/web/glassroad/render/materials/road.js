/* render/materials/road.js - the glass ribbon: translucent, flowing colour bands along s.
 * uv: u = lateral metres, v = s metres (CONTRACT.md MeshData). The band phase is integrated on the
 * CPU (scene.js) so a speed change never jumps the pattern. contrast and the phase rate are what
 * Gentle halves. With the art set loaded the bands are Astra's wave mask (road-bands-mask): its
 * crests run across the road, one tile per artRoadTileM (image x along s, image y lateral), R maps
 * through the palette wheel and the seam rides each crest; the roughness map adds a faint glass haze.
 * Without it the procedural warp stands in.
 * Glass sheen (B1): road-glass-normal ripples a fixed key light (LOOK.sheenDir) and a view fresnel, a
 * cheap fake light with no extra pass; `sheen` (1, LOOK.sheenGentle under Gentle) scales it.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uniform, uv, sin, cos, fract, mix, smoothstep, float, vec2, vec3, texture, PI2, normalWorld, positionWorld, cameraPosition,
  normalize, dot, pow, abs, clamp } from 'three/tsl';
import { LOOK } from '../look.js';

export function createRoadMaterial(pal, art = null) {
  const u = { phase: uniform(0), contrast: uniform(1), bright: uniform(0), sheen: uniform(1) };
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
  const nrm = art && art.tex('road-glass-normal');
  let sheen = float(0);
  if (nrm) {
    const nt = texture(nrm, p.div(LOOK.artHazeTileM)).xy.mul(2).sub(1);
    const V = normalize(cameraPosition.sub(positionWorld)), H = normalize(normalize(vec3(...LOOK.sheenDir)).add(V));
    const ndh = clamp(dot(normalWorld, H).add(nt.x.add(nt.y).mul(LOOK.sheenBump * 0.5)), 0, 1);
    const fres = pow(float(1).sub(abs(dot(normalWorld, V))), 4).mul(LOOK.sheenFresnel);
    sheen = pow(ndh, LOOK.sheenPow).mul(LOOK.sheenGain).add(fres).mul(u.sheen);
  }
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide });
  m.colorNode = mix(avg, band, u.contrast).add(seam.mul(LOOK.seamGain)).mul(u.bright.add(1)).mul(haze).mul(LOOK.roadGain)
    .add(vec3(1, 0.96, 0.98).mul(sheen));
  m.opacityNode = clamp(float(LOOK.roadAlpha).add(seam.mul(1 - LOOK.roadAlpha)).add(sheen.mul(0.5)), 0, 1);
  return { material: m, uniforms: u };
}
