/* render/materials/sky.js - the spiral dome: a large inward-facing sphere that follows the kart.
 * The spiral's eye sits below the horizon ahead (scene.js tilts it from the nadir toward a slow
 * heading), so the arms turn behind and under the glass road and falls still drop toward it. Arms
 * are bright palette bands over a darker ground; turn is integrated on the CPU; contrast is what
 * Gentle halves; rescue brightens the dome toward white and the eye glows (beat 7).
 * With the art set, the three palette galaxies (equirectangular haze) and star fields sit under the
 * arms, weighted by `galaxy` (dawn, deep, afterglow) from the same palette mix the road uses.
 */
import { MeshBasicNodeMaterial, BackSide, Vector3 } from 'three/webgpu';
import { uniform, positionLocal, normalize, atan, acos, asin, log, sin, mix, smoothstep, clamp, vec2, vec3, dot, texture, float, cameraPosition } from 'three/tsl';
import { LOOK } from '../look.js';

export const SKY_PALETTES = ['dawn', 'deep', 'afterglow'];

/** Polish 2 sky depth; LOOK.skyDepth overrides. layers: [radius m, u tiling, gain, palette tint]. */
export const SKY_DEPTH = Object.freeze({
  layers: Object.freeze([Object.freeze([650, 3, 0.55, 0.35]), Object.freeze([1400, 2, 0.4, 0.55])]),
  floor: 0.25, hazeR: 1800, hazeGain: 0.22,
});

export function createSkyMaterial(pal, art = null) {
  const u = { turn: uniform(0), twist: uniform(0), contrast: uniform(LOOK.skyContrast), rescue: uniform(0),
    eye: uniform(new Vector3(0, -1, 0)), eyeR: uniform(new Vector3(1, 0, 0)), eyeU: uniform(new Vector3(0, 0, -1)),
    galaxy: uniform(new Vector3(1, 0, 0)) };
  const d = normalize(positionLocal);
  const r = acos(clamp(dot(d, u.eye), -1, 1)).add(0.02);                 // angle from the eye
  const az = atan(dot(d, u.eyeU), dot(d, u.eyeR));
  const wave = sin(az.mul(LOOK.skyArms).add(log(r).mul(u.twist)).sub(u.turn)).mul(0.5).add(0.5);
  // soft-edged arms, faded out right at the eye so the log rings never alias
  const arm = smoothstep(0.1, 0.9, wave).mul(smoothstep(0.04, 0.35, r));
  const up = d.y.mul(0.5).add(0.5);
  let ground = mix(pal.col(2), pal.col(0), up).mul(LOOK.skyDim * LOOK.skyGapDim);
  const bands = mix(pal.col(0), pal.col(1), up).mul(LOOK.skyDim);
  if (art) {
    // equirectangular: u around the y axis, v from the zenith (KTX2 rows run top down)
    const eq = vec2(atan(d.z, d.x).div(Math.PI * 2).add(0.5), float(0.5).sub(asin(clamp(d.y, -1, 1)).div(Math.PI)));
    const w = [u.galaxy.x, u.galaxy.y, u.galaxy.z];
    let haze = vec3(0, 0, 0), stars = float(0);
    SKY_PALETTES.forEach((id, i) => {
      const g = art.tex(`sky-${id}-galaxy`), s = art.tex(`sky-${id}-stars`);
      if (g) haze = haze.add(texture(g, eq).rgb.mul(w[i]));
      if (s) stars = stars.add(texture(s, eq).r.mul(w[i]));
    });
    ground = ground.add(haze.mul(LOOK.artGalaxyGain).mul(u.contrast)).add(stars.mul(LOOK.artStarGain));
    // polish 2: parallax star layers. The dome rides with the kart, so these layers are read as if
    // they hung on spheres round the world origin (radius R): the view ray from the camera meets
    // them at camera + d * R, so nearer layers slide as you drive. Each layer turns the field to a
    // fresh orientation and tiles it finer, tinted toward the palette; no time term, no twinkle.
    const deepStars = art.tex('sky-deep-stars') || art.tex('sky-dawn-stars');
    if (deepStars) {
      const S = { ...SKY_DEPTH, ...(LOOK.skyDepth || {}) };
      S.layers.forEach(([R, tile, gain, tint], i) => {
        const q = normalize(cameraPosition.add(d.mul(R)));
        const r = i % 2 ? vec3(q.z, q.y, q.x.negate()) : vec3(q.x.negate(), q.y, q.z.negate());
        const e = vec2(atan(r.z, r.x).div(Math.PI * 2).add(0.5).mul(tile), float(0.5).sub(asin(clamp(r.y, -1, 1)).div(Math.PI)));
        const s = smoothstep(S.floor, 1, texture(deepStars, e).r);
        ground = ground.add(mix(vec3(1, 1, 1), pal.col(i % 3), tint).mul(s.mul(gain)));
      });
      // the galaxy haze also gets a slow far layer, so the clouds have a near and a far
      if (S.hazeGain > 0) {
        const q = normalize(cameraPosition.add(d.mul(S.hazeR)));
        const e = vec2(atan(q.x, q.z).div(Math.PI * 2).add(0.5), float(0.5).sub(asin(clamp(q.y, -1, 1)).div(Math.PI)));
        let far = vec3(0, 0, 0);
        SKY_PALETTES.forEach((id, i) => { const g = art.tex(`sky-${id}-galaxy`); if (g) far = far.add(texture(g, e).rgb.mul(w[i])); });
        ground = ground.add(far.mul(S.hazeGain).mul(u.contrast));
      }
    }
  }
  const col = mix(ground, bands, arm.mul(u.contrast));
  const eye = smoothstep(0, 1.2, r).oneMinus();
  const m = new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, fog: false });
  m.colorNode = mix(col, vec3(1, 1, 1), u.rescue.mul(eye.mul(0.5).add(LOOK.rescueWhite)));
  return { material: m, uniforms: u };
}
