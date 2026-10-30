/* render/motes.js - ambient drifting motes near the road (B1 juice).
 *
 * A seeded cloud wrapped in a box that rides with the player kart: each mote keeps a world position
 * (fract of its seed minus the box centre), drifts slowly and bobs, so the field never pops when the
 * kart moves. Coloured from the section palette, soft discs and petals, faded at the box edge and
 * near the camera. Density (the share of motes shown) follows the deep palette weight, so the deep
 * section thickens; Gentle thins and slows them. One draw; Q.motes budget per tier.
 * With the wave 2 particle atlas the motes draw its soft disc, petals (more of them in afterglow) and
 * swirls (in deep); without it the B1 shader disc and petal.
 * Polish 3 (atlas.js JUICE_LOOK): a moteSpectrum share wears six spectrum colours (render/spectrum.js
 * through juiceHues) that each mote drifts through slowly (hue only, its brightness stays), a little
 * bigger and brighter; the rest keep the section palette.
 */
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, AdditiveBlending, Vector3, Color } from 'three/webgpu';
import { instancedBufferAttribute, uniform, float, vec2, vec3, fract, floor, sin, step, mix, smoothstep, uv, select, cameraPosition } from 'three/tsl';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { currentArt } from './art.js';
import { atlasOf, atlasCoverage } from './particles.js';
import { FXART_LOOK, cellIndex, paletteWeights, moteMix, juiceLook, juiceHues, spreadHues } from './atlas.js';

/** Six spectrum colours spread round the wheel (as uniforms), and the colour at a 0..1 wheel position x. */
function spectrumWheel(hues) {
  const k = 6, pick = spreadHues(hues, k), cols = pick.map((h) => uniform(new Color(h)));
  const at = (i) => select(i.lessThan(0.5), cols[0], select(i.lessThan(1.5), cols[1], select(i.lessThan(2.5), cols[2],
    select(i.lessThan(3.5), cols[3], select(i.lessThan(4.5), cols[4], cols[5])))));
  return (x) => {
    const f = fract(x).mul(k), i = floor(f), j = select(i.lessThan(k - 1.5), i.add(1), float(0));
    return mix(at(i), at(j), smoothstep(0, 1, f.sub(i)));
  };
}

export function createMotes(pal, seed = 1, art = currentArt()) {
  const n = Q.motes, s = new Float32Array(n * 4), at = atlasOf(art), A = LOOK.fxArt || FXART_LOOK, J = juiceLook(LOOK);
  let st = (seed >>> 0) % 2147483646 + 1;
  for (let i = 0; i < s.length; i++) { st = (st * 16807) % 2147483647; s[i] = st / 2147483647; }
  const aSeed = instancedBufferAttribute(new InstancedBufferAttribute(s, 4), 'vec4');
  const box = vec3(...LOOK.moteBoxM);
  const u = { centre: uniform(new Vector3()), time: uniform(0), density: uniform(LOOK.moteBase), alpha: uniform(Math.min(1, LOOK.moteAlpha * J.moteAlphaMul)),
    petal: uniform(A.motePetal[0]), swirl: uniform(A.moteSwirl[0]) };
  const drift = vec3(...LOOK.moteDrift).mul(aSeed.w.add(0.5)).mul(u.time);
  const cell = fract(aSeed.xyz.add(drift).sub(u.centre.div(box)));
  const bob = vec3(0, sin(u.time.mul(0.7).add(aSeed.w.mul(6.283))).mul(0.25), 0);
  const p = u.centre.add(cell.sub(0.5).mul(box)).add(bob);
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = p;
  const show = step(aSeed.w, u.density);
  m.scaleNode = mix(LOOK.moteSizeM[0], LOOK.moteSizeM[1], fract(aSeed.w.mul(7.13))).mul(show).mul(at ? A.moteCellSize : 1).mul(J.moteSizeMul);
  const hue = fract(aSeed.w.mul(3.71)), wheel = spectrumWheel(juiceHues());
  const palCol = select(hue.lessThan(0.34), pal.col(0), select(hue.lessThan(0.67), pal.col(1), pal.col(2))).mul(0.8).add(0.2);
  // a share of the motes wear the spectrum, each drifting slowly round the wheel (hue only)
  const specCol = wheel(aSeed.y.add(u.time.mul(J.moteHueHz)));
  m.colorNode = select(fract(aSeed.w.mul(5.17)).lessThan(J.moteSpectrum), specCol, palCol);
  const a = cell.sub(0.5).mul(2).abs();
  const edge = smoothstep(0.65, 1, a.x.max(a.y).max(a.z)).oneMinus();
  const near = smoothstep(LOOK.moteNearM, LOOK.moteNearM * 3, p.distance(cameraPosition));
  let shape;
  if (at) {
    // one cell per mote: petals then swirls take their shares off the ends, soft discs in between
    const pick = fract(aSeed.w.mul(11.3)), cell = (id) => float(cellIndex(id, at.cells));
    shape = atlasCoverage(at, select(pick.lessThan(u.petal), cell('petal'), select(pick.greaterThan(u.swirl.oneMinus()), cell('swirl'), cell('disc'))));
  } else {
    const r = uv().sub(0.5).mul(2);
    const petal = fract(aSeed.w.mul(11.3)).lessThan(0.3);
    shape = smoothstep(0.3, 1, select(petal, r.mul(vec2(2, 1)).length(), r.length())).oneMinus();
  }
  m.opacityNode = u.alpha.mul(edge).mul(near).mul(shape);
  m.rotationNode = aSeed.x.mul(6.283).add(u.time.mul(aSeed.y.sub(0.5)));
  const sprite = new Sprite(m);
  sprite.count = n; sprite.frustumCulled = false; sprite.renderOrder = 1;

  // the section mix, seen as scene.js hands it to the palette (unless update is given weights)
  let seen = null;
  const set0 = pal && typeof pal.set === 'function' ? pal.set : null;
  if (set0 && at) pal.set = function set(from, to, t) { seen = paletteWeights(from, to, t); return set0.call(this, from, to, t); };

  return {
    object: sprite,
    /** centre = the player kart position; density 0..1 (kartPose.moteDensity); speed = Gentle scroll share;
     * weights = { paletteId: weight } (optional, else the palette's own last set). */
    update(dt, centre, density, speed = 1, weights = seen) {
      u.centre.value.copy(centre);
      u.time.value += dt * speed;
      u.density.value = density;
      sprite.count = Math.min(n, Q.motes);
      if (at && weights) { const k = moteMix(weights, A); u.petal.value = k.petal; u.swirl.value = k.swirl; }
    },
    dispose() { m.dispose(); if (set0 && pal.set !== set0) pal.set = set0; },
  };
}
