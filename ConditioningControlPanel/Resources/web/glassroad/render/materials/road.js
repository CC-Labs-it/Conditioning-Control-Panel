/* render/materials/road.js - the glass ribbon: translucent, flowing colour bands along s.
 * uv: u = lateral metres, v = s metres (CONTRACT.md MeshData). The band phase is integrated on the
 * CPU (scene.js) so a speed change never jumps the pattern. contrast and the phase rate are what
 * Gentle halves. With the art set loaded the bands are Astra's wave mask (road-bands-mask): its
 * crests run across the road, one tile per artRoadTileM (image x along s, image y lateral), R maps
 * through the palette wheel and the seam rides each crest; the roughness map adds a faint glass haze.
 * Without it the procedural warp stands in.
 * Glass sheen (B1): road-glass-normal ripples a fixed key light (LOOK.sheenDir) and a view fresnel, a
 * cheap fake light with no extra pass; `sheen` (1, LOOK.sheenGentle under Gentle) scales it.
 *
 * Polish 2 (WORLD lane), all scaled by `sheen` so Gentle halves them, none with a time term:
 *   - depth: a second, larger band layer read "under" the surface, scrolling at depthScroll of the
 *     phase, dim and saturated; the speed difference between the two layers reads as thickness;
 *   - sky reflection: a Schlick fresnel of the palette sky toward the reflected view ray;
 *   - glitter: a sparse grid of tiny glints in the glass that catch as the view angle changes (so
 *     they twinkle with motion and hold still when you stop), faded out near and far so they never
 *     alias or strobe;
 *   - crisper crests: a thin hot line on each crest over the broad seam;
 *   - edge rim: a soft glow inside each road edge; with `track` (third argument) the width comes from
 *     a per-metre width texture, else a fixed half width.
 */
import { MeshBasicNodeMaterial, DoubleSide, DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, RepeatWrapping, ClampToEdgeWrapping } from 'three/webgpu';
import { uniform, uv, sin, cos, fract, mix, smoothstep, float, vec2, vec3, texture, PI2, normalWorld, positionWorld, cameraPosition,
  normalize, dot, pow, abs, clamp, max, reflect, floor, hash, length } from 'three/tsl';
import { LOOK } from '../look.js';

/** Road polish numbers; LOOK.roadPolish overrides any key. */
export const ROAD_POLISH = Object.freeze({
  depthTile: 2.2, depthScroll: 0.45, depthGain: 0.16, depthSat: 1.35,
  reflGain: 0.35, reflF0: 0.04, reflBump: 0.12,
  glintCellM: 0.38, glintDensity: 0.07, glintSizeM: 0.03, glintTwinkle: 4, glintPow: 6, glintGain: 0.45,
  glintNearM: [3, 7], glintFarM: [22, 34],
  crestLine: [0.965, 0.992], crestGain: 0.28,
  rimInM: 1.4, rimGain: 0.25, halfWidthM: 7,
});

/** Half road width per metre of s as a 1-row texture (R = halfWidth / 16). */
function widthTexture(track) {
  const n = Math.max(2, Math.ceil(track.lengthM)), data = new Uint8Array(n * 4);
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (let i = 0; i < n; i++) {
    const fr = track.frameAt((i + 0.5) * track.lengthM / n, f);
    data[i * 4] = Math.round(Math.min(1, fr.w / 2 / 16) * 255); data[i * 4 + 3] = 255;
  }
  const t = new DataTexture(data, n, 1, RGBAFormat, UnsignedByteType);
  t.wrapS = RepeatWrapping; t.wrapT = ClampToEdgeWrapping; t.magFilter = t.minFilter = LinearFilter; t.needsUpdate = true;
  return t;
}

export function createRoadMaterial(pal, art = null, track = null) {
  const P = { ...ROAD_POLISH, ...(LOOK.roadPolish || {}) };
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
  const w = (k, x) => cos(x.sub(k / 3).mul(PI2)).mul(0.5).add(0.5);
  const wheel = (x) => pal.col(0).mul(w(0, x)).add(pal.col(1).mul(w(1, x))).add(pal.col(2).mul(w(2, x))).div(1.5);
  const band = wheel(f);
  const avg = pal.col(0).add(pal.col(1)).add(pal.col(2)).div(3);
  const seam = (bands ? smoothstep(0.9, 1, crest) : smoothstep(0.94, 1, fract(f.mul(3)))).mul(u.contrast);
  const haze = rough ? mix(float(1 - LOOK.artHaze), float(1 + LOOK.artHaze), texture(rough, p.div(LOOK.artHazeTileM)).r) : float(1);
  const nrm = art && art.tex('road-glass-normal');
  const toCam = cameraPosition.sub(positionWorld), dist = toCam.length(), V = normalize(toCam);
  let sheen = float(0), nt = vec2(0, 0);
  if (nrm) {
    nt = texture(nrm, p.div(LOOK.artHazeTileM)).xy.mul(2).sub(1);
    const H = normalize(normalize(vec3(...LOOK.sheenDir)).add(V));
    const ndh = clamp(dot(normalWorld, H).add(nt.x.add(nt.y).mul(LOOK.sheenBump * 0.5)), 0, 1);
    const fres = pow(float(1).sub(abs(dot(normalWorld, V))), 4).mul(LOOK.sheenFresnel);
    sheen = pow(ndh, LOOK.sheenPow).mul(LOOK.sheenGain).add(fres).mul(u.sheen);
  }

  // depth: a larger, slower band layer under the surface
  const deepAlong = p.y.sub(u.phase.mul(P.depthScroll));
  const deepF = bands
    ? fract(texture(bands, vec2(deepAlong.div(tv * P.depthTile), p.x.div(tu * P.depthTile).add(0.37))).r.mul(LOOK.artBandSpan).add(0.33))
    : fract(deepAlong.div(LOOK.bandLenM * P.depthTile).add(0.33));
  const deepCol = max(mix(avg, wheel(deepF), float(P.depthSat)), vec3(0, 0, 0)).mul(P.depthGain).mul(u.contrast);

  // sky reflection along the bumped reflected ray (palette sky, the dome's own colours)
  const N = normalize(normalWorld.add(vec3(nt.x, 0, nt.y).mul(P.reflBump)));
  const R = reflect(V.negate(), N);
  const up = R.y.mul(0.5).add(0.5);
  const skyRef = mix(mix(pal.col(2), pal.col(0), up).mul(LOOK.skyGapDim), mix(pal.col(0), pal.col(1), up), 0.5).mul(LOOK.skyDim);
  const schlick = float(P.reflF0).add(pow(float(1).sub(abs(dot(normalWorld, V))), 5).mul(1 - P.reflF0));
  const refl = skyRef.mul(schlick).mul(P.reflGain);

  // glitter: one candidate glint per cell, a few lit; each catches over a band of view angles
  const cell = floor(p.div(P.glintCellM));
  const h1 = hash(cell.x.add(cell.y.mul(157.31))), h2 = hash(cell.x.mul(7.13).add(cell.y.mul(311.7)).add(19.1));
  const jit = vec2(h1, h2).sub(0.5).mul(0.5);
  const inCell = fract(p.div(P.glintCellM)).sub(0.5).sub(jit).mul(P.glintCellM);
  const spot = float(1).sub(smoothstep(0, P.glintSizeM, length(inCell)));
  const lit = smoothstep(1 - P.glintDensity, 1, h1);
  const seed = vec3(h2.sub(0.5), h1.mul(0.7), float(0.5).sub(h1));
  const catchA = pow(max(sin(dot(V, seed).mul(P.glintTwinkle * 6.2832).add(h2.mul(6.2832))), 0), P.glintPow);
  const range = smoothstep(P.glintNearM[0], P.glintNearM[1], dist).mul(float(1).sub(smoothstep(P.glintFarM[0], P.glintFarM[1], dist)));
  const glint = spot.mul(lit).mul(catchA).mul(range).mul(P.glintGain).mul(u.sheen);

  // crisp crest line over the broad seam
  const crestHot = bands ? smoothstep(P.crestLine[0], P.crestLine[1], crest).mul(P.crestGain).mul(u.contrast) : float(0);

  // soft rim inside each edge
  const half = track ? texture(widthTexture(track), vec2(p.y.div(track.lengthM), 0.5)).r.mul(16) : float(P.halfWidthM);
  const rim = float(1).sub(smoothstep(0, P.rimInM, half.sub(abs(p.x)))).mul(P.rimGain);

  const polish = refl.add(vec3(1, 0.97, 0.99).mul(crestHot)).add(pal.col(0).mul(0.6).add(0.4).mul(rim)).mul(u.sheen)
    .add(vec3(1, 0.97, 0.99).mul(glint));
  const surface = mix(avg, band, u.contrast).add(seam.mul(LOOK.seamGain)).mul(u.bright.add(1)).mul(haze).mul(LOOK.roadGain);
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide });
  m.colorNode = surface.add(deepCol).add(vec3(1, 0.96, 0.98).mul(sheen)).add(polish);
  m.opacityNode = clamp(float(LOOK.roadAlpha).add(seam.mul(1 - LOOK.roadAlpha)).add(sheen.mul(0.5))
    .add(schlick.mul(0.25)).add(rim.mul(0.4)).add(glint.mul(0.5)), 0, 1);
  return { material: m, uniforms: u };
}
