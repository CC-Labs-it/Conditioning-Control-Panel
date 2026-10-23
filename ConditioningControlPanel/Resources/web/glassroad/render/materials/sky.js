/* render/materials/sky.js - the spiral dome: a large inward-facing sphere that follows the kart.
 * The spiral's eye sits below the horizon ahead (scene.js tilts it from the nadir toward a slow
 * heading), so the arms turn behind and under the glass road and falls still drop toward it. Arms
 * are bright palette bands over a darker ground; turn is integrated on the CPU; contrast is what
 * Gentle halves; rescue brightens the dome toward white and the eye glows (beat 7).
 * With the art set, the three palette galaxies (equirectangular haze) and star fields sit under the
 * arms, weighted by `galaxy` (dawn, deep, afterglow) from the same palette mix the road uses.
 *
 * Polish 3 (SPECTRUM lane): a spectacle of colour. The arms take the full-spectrum ramp (pal.ramp) by
 * direction around the dome (SKY_SPECTRUM.armTurns whole turns, so no seam), the gaps fill with a
 * multi-hue nebula (a low-frequency field of the ramp with soft cloud structure) and the galaxy haze is
 * partly recoloured through the same field; stars stay white. The hue clock (pal.hue, halved under
 * Gentle) turns it all slowly; the ramp is luminance-levelled, so nothing flashes. Gentle's contrast
 * still flattens the arms.
 *
 * Polish 4 (owner: "muddy red and green blotches"): a dimmed red, orange, yellow or green turns maroon,
 * brown or olive, so the dome no longer samples the full-spectrum ramp. It runs a clean periodic gradient
 * (SKY_SPECTRUM.cleanStops: pink, magenta, violet, blue, cyan, violet), tinted a little toward the
 * section colour, on the same hue clock; the nebula spans half the gradient (a wide smooth wash, no
 * blotches) and its cloud term only breathes. The warm hues stay on the road, props and spirals.
 */
import { MeshBasicNodeMaterial, BackSide, Vector3 } from 'three/webgpu';
import { uniform, positionLocal, normalize, atan, acos, asin, log, sin, mix, smoothstep, clamp, vec2, vec3, dot, texture, float, cameraPosition,
  fract, abs, max, min, fwidth, select } from 'three/tsl';
import { Color } from 'three/webgpu';
import { LOOK } from '../look.js';

/** An equirect u (atan / 2pi + 0.5) without the meridian seam: the u jumps 1 to 0 at the atan cut, so the
 * pixels there read the coarsest mip in a 1 px line. Two copies cut half a turn apart; each pixel keeps
 * the one with the smaller fwidth (the textures repeat in u, so both sample the same texel). */
const seamU = (x) => { const a = fract(x), b = fract(x.add(0.5)).sub(0.5); return select(fwidth(a).lessThanEqual(fwidth(b)), a, b); };

export const SKY_PALETTES = ['dawn', 'deep', 'afterglow'];

/** Polish 2 sky depth; LOOK.skyDepth overrides. layers: [radius m, u tiling, gain, palette tint]. */
export const SKY_DEPTH = Object.freeze({
  layers: Object.freeze([Object.freeze([650, 3, 0.55, 0.35]), Object.freeze([1400, 2, 0.4, 0.55])]),
  floor: 0.25, hazeR: 1800, hazeGain: 0.22,
});

/** Polish 3 sky colour numbers; LOOK.skySpectrum overrides. */
export const SKY_SPECTRUM = Object.freeze({
  armTurns: 2, armR: 0.06, armGain: 1.25, nebGain: 0.75, nebMix: 0.8, nebCloud: [0.35, 1], hazeRecolour: 0.85, hazeGain: 1.5,
  // polish 4: the clean gradient (periodic), its share of the section tint, the nebula span and cloud breath
  cleanStops: Object.freeze(['#ff4fb8', '#d43dff', '#7a4dff', '#2f6bff', '#19d3ff', '#8a3dff']), cleanTint: 0.2,
  nebSpan: 0.5, cloudFloor: 0.75,
});

/** The clean periodic gradient at x (TSL float, wraps): tent weights over the stops, so it is smooth. */
export function cleanRamp(stops, x) {
  const N = stops.length, t = fract(x).mul(N);
  let out = vec3(0, 0, 0);
  stops.forEach((hex, i) => {
    const c = new Color(hex), d = abs(t.sub(i));
    const w = max(float(1).sub(min(d, float(N).sub(d))), 0);
    out = out.add(vec3(c.r, c.g, c.b).mul(w));
  });
  return out;
}

export function createSkyMaterial(pal, art = null) {
  const C = { ...SKY_SPECTRUM, ...(LOOK.skySpectrum || {}) };
  if (pal.theme === 'mainframe') Object.assign(C, { cleanStops: ['#20674d', '#245851', '#175669', '#2c8b73'], armGain: 0.5, nebGain: 0.3, hazeGain: 0.4 });
  // the clean gradient on the palette's hue clock, a little of the section colour mixed in
  const hueClock = pal.hue || float(0);
  const ramp = (x) => mix(cleanRamp(C.cleanStops, x.add(hueClock)), pal.col(1), C.cleanTint);
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
  let bands = mix(pal.col(0), pal.col(1), up).mul(LOOK.skyDim);
  // nebula field: a slow, smooth coordinate over the dome (no seams: plain trig of the direction)
  const nx = d.x.mul(0.35).add(d.y.mul(0.3)).add(sin(d.z.mul(2.3).add(d.y.mul(1.7))).mul(0.18)).add(sin(d.x.mul(1.3).sub(d.z.mul(0.9))).mul(0.12));
  const cloud = smoothstep(C.nebCloud[0], C.nebCloud[1], sin(d.x.mul(3.1).add(d.z.mul(2.2))).mul(sin(d.y.mul(2.7).sub(d.x.mul(1.4)))).mul(0.5).add(0.5)
    .add(sin(d.z.mul(5.3).add(d.y.mul(3.9))).mul(0.15)));
  if (ramp) {
    const neb = ramp(nx.mul(C.nebSpan)).mul(cloud.mul(1 - C.cloudFloor).add(C.cloudFloor)).mul(LOOK.skyDim * LOOK.skyGapDim * C.nebGain * 1.6);
    ground = mix(ground, neb, C.nebMix);
    bands = ramp(az.div(Math.PI * 2).mul(C.armTurns).add(log(r).mul(C.armR))).mul(LOOK.skyDim * C.armGain);
  }
  if (art) {
    // equirectangular: u around the y axis, v from the zenith (KTX2 rows run top down)
    const eq = vec2(seamU(atan(d.z, d.x).div(Math.PI * 2).add(0.5)), float(0.5).sub(asin(clamp(d.y, -1, 1)).div(Math.PI)));
    const w = [u.galaxy.x, u.galaxy.y, u.galaxy.z];
    let haze = vec3(0, 0, 0), stars = float(0);
    SKY_PALETTES.forEach((id, i) => {
      const g = art.tex(`sky-${id}-galaxy`), s = art.tex(`sky-${id}-stars`);
      if (g) haze = haze.add(texture(g, eq).rgb.mul(w[i]));
      if (s) stars = stars.add(texture(s, eq).r.mul(w[i]));
    });
    if (ramp) {
      const lum = dot(haze, vec3(0.2126, 0.7152, 0.0722));
      haze = mix(haze, ramp(nx.mul(C.nebSpan).add(0.35)).mul(lum.mul(C.hazeGain)), C.hazeRecolour);
    }
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
        const e = vec2(seamU(atan(r.z, r.x).div(Math.PI * 2).add(0.5)).mul(tile), float(0.5).sub(asin(clamp(r.y, -1, 1)).div(Math.PI)));
        const s = smoothstep(S.floor, 1, texture(deepStars, e).r);
        ground = ground.add(mix(vec3(1, 1, 1), pal.col(i % 3), tint).mul(s.mul(gain)));
      });
      // the galaxy haze also gets a slow far layer, so the clouds have a near and a far
      if (S.hazeGain > 0) {
        const q = normalize(cameraPosition.add(d.mul(S.hazeR)));
        const e = vec2(seamU(atan(q.x, q.z).div(Math.PI * 2).add(0.5)), float(0.5).sub(asin(clamp(q.y, -1, 1)).div(Math.PI)));
        let far = vec3(0, 0, 0);
        SKY_PALETTES.forEach((id, i) => { const g = art.tex(`sky-${id}-galaxy`); if (g) far = far.add(texture(g, e).rgb.mul(w[i])); });
        // recoloured through the clean gradient too (the raw galaxy carried the red and green)
        const farLum = dot(far, vec3(0.2126, 0.7152, 0.0722));
        ground = ground.add(ramp(nx.mul(C.nebSpan).add(0.6)).mul(farLum.mul(C.hazeGain)).mul(S.hazeGain).mul(u.contrast));
      }
    }
  }
  // Atmospheric distance is a static broad value shape, not another moving overlay. Keep the
  // horizon behind the road dark enough for the next bend and giant silhouettes to separate.
  const depth = { horizonGain: 0.7, zenithGain: 0.9, horizonBand: 0.42, ...(LOOK.skySeparation || {}) };
  const separation = mix(float(depth.horizonGain), float(depth.zenithGain), smoothstep(0, depth.horizonBand, abs(d.y)));
  const col = mix(ground, bands, arm.mul(u.contrast)).mul(separation);
  const eye = smoothstep(0, 1.2, r).oneMinus();
  const m = new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, fog: false });
  m.colorNode = mix(col, vec3(1, 1, 1), u.rescue.mul(eye.mul(0.5).add(LOOK.rescueWhite)));
  return { material: m, uniforms: u };
}
