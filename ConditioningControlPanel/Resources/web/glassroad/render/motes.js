/* render/motes.js - ambient drifting motes near the road (B1 juice).
 *
 * A seeded cloud wrapped in a box that rides with the player kart: each mote keeps a world position
 * (fract of its seed minus the box centre), drifts slowly and bobs, so the field never pops when the
 * kart moves. Coloured from the section palette, soft discs and petals, faded at the box edge and
 * near the camera. Density (the share of motes shown) follows the deep palette weight, so the deep
 * section thickens; Gentle thins and slows them. One draw; Q.motes budget per tier.
 */
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, AdditiveBlending, Vector3 } from 'three/webgpu';
import { instancedBufferAttribute, uniform, vec2, vec3, fract, sin, step, mix, smoothstep, uv, select, cameraPosition } from 'three/tsl';
import { LOOK } from './look.js';
import { Q } from './quality.js';

export function createMotes(pal, seed = 1) {
  const n = Q.motes, s = new Float32Array(n * 4);
  let st = (seed >>> 0) % 2147483646 + 1;
  for (let i = 0; i < s.length; i++) { st = (st * 16807) % 2147483647; s[i] = st / 2147483647; }
  const aSeed = instancedBufferAttribute(new InstancedBufferAttribute(s, 4), 'vec4');
  const box = vec3(...LOOK.moteBoxM);
  const u = { centre: uniform(new Vector3()), time: uniform(0), density: uniform(LOOK.moteBase), alpha: uniform(LOOK.moteAlpha) };
  const drift = vec3(...LOOK.moteDrift).mul(aSeed.w.add(0.5)).mul(u.time);
  const cell = fract(aSeed.xyz.add(drift).sub(u.centre.div(box)));
  const bob = vec3(0, sin(u.time.mul(0.7).add(aSeed.w.mul(6.283))).mul(0.25), 0);
  const p = u.centre.add(cell.sub(0.5).mul(box)).add(bob);
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = p;
  const show = step(aSeed.w, u.density);
  m.scaleNode = mix(LOOK.moteSizeM[0], LOOK.moteSizeM[1], fract(aSeed.w.mul(7.13))).mul(show);
  const hue = fract(aSeed.w.mul(3.71));
  m.colorNode = select(hue.lessThan(0.34), pal.col(0), select(hue.lessThan(0.67), pal.col(1), pal.col(2))).mul(0.8).add(0.2);
  const a = cell.sub(0.5).mul(2).abs();
  const edge = smoothstep(0.65, 1, a.x.max(a.y).max(a.z)).oneMinus();
  const near = smoothstep(LOOK.moteNearM, LOOK.moteNearM * 3, p.distance(cameraPosition));
  const r = uv().sub(0.5).mul(2);
  const petal = fract(aSeed.w.mul(11.3)).lessThan(0.3);
  const shape = smoothstep(0.3, 1, select(petal, r.mul(vec2(2, 1)).length(), r.length())).oneMinus();
  m.opacityNode = u.alpha.mul(edge).mul(near).mul(shape);
  m.rotationNode = aSeed.x.mul(6.283).add(u.time.mul(aSeed.y.sub(0.5)));
  const sprite = new Sprite(m);
  sprite.count = n; sprite.frustumCulled = false; sprite.renderOrder = 1;

  return {
    object: sprite,
    /** centre = the player kart position; density 0..1 (kartPose.moteDensity); speed = Gentle scroll share. */
    update(dt, centre, density, speed = 1) {
      u.centre.value.copy(centre);
      u.time.value += dt * speed;
      u.density.value = density;
      sprite.count = Math.min(n, Q.motes);
    },
    dispose() { m.dispose(); },
  };
}
