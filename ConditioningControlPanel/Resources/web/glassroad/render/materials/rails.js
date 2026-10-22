/* render/materials/rails.js - the rails as glass tubes. The rail mesh is a flat ribbon (uv: u = metres
 * up, v = s metres); across its height the shader bends a cylinder's normal from bottom to top, so
 * the ribbon reads as a round glass tube: clear in the middle, bright fresnel walls at both edges,
 * a sharp specular streak near the top (the lamp) and a soft one low down (the road's glow), the sky
 * palette reflected in the walls and a coloured core inside (rail-glow-mask when the art is in).
 * `flow` (metres, optional: scene.js may copy the road phase into it) slides a broad soft glint along
 * the tube, period LOOK.bandLenM so a wrapping phase never jumps it. Everything fades within
 * LOOK.railNearM of the camera so a rail beside the eye never walls the view.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uv, uniform, clamp, mix, float, vec2, vec3, texture, smoothstep, positionWorld, cameraPosition, pow, abs, sin, cos, max } from 'three/tsl';
import { LOOK } from '../look.js';

/** Tube numbers; LOOK.railTube overrides any key. */
export const RAIL_TUBE = Object.freeze({
  bodyAlpha: 0.1, wallAlpha: 0.55, wallPow: 3, wallGain: 0.55, streakAt: 0.78, streakW: 0.035, streakGain: 0.9,
  lowAt: 0.22, lowW: 0.06, lowGain: 0.25, coreGain: 0.5, glintGain: 0.18, glintPow: 10,
});

export function createRailMaterial(pal, art = null) {
  const T = { ...RAIL_TUBE, ...(LOOK.railTube || {}) };
  const u = { flow: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide, depthWrite: false });
  const glow = art && art.tex('rail-glow-mask');
  const h = clamp(uv().x.div(LOOK.railHeightM), 0, 1);
  // the tube's cross section: angle from -90 to +90 degrees, wall = how edge-on the glass is
  const ang = h.sub(0.5).mul(Math.PI);
  const wall = pow(abs(sin(ang)), T.wallPow);
  const facing = cos(ang);
  const band = (at, wd) => float(1).sub(smoothstep(0, wd, abs(h.sub(at))));
  const streak = band(T.streakAt, T.streakW).mul(T.streakGain);
  const low = band(T.lowAt, T.lowW).mul(T.lowGain);
  const core = glow ? texture(glow, vec2(h, uv().y.div(LOOK.artRoadTileM[1]))).r : facing.mul(facing).mul(0.6);
  const sky = mix(pal.col(2), pal.col(1), h).mul(LOOK.skyDim).add(pal.col(0).mul(0.25));
  const glint = pow(max(sin(uv().y.sub(u.flow).div(LOOK.bandLenM).mul(Math.PI * 2)), 0), T.glintPow).mul(T.glintGain).mul(facing);
  m.colorNode = pal.col(0).mul(LOOK.railGain).mul(facing.mul(0.5).add(0.3))
    .add(sky.mul(wall).mul(T.wallGain))
    .add(core.mul(LOOK.artRailCore * T.coreGain / 0.5).mul(pal.col(0).mul(0.5).add(0.5)))
    .add(vec3(1, 0.97, 0.99).mul(streak.add(low).add(glint)));
  const near = smoothstep(LOOK.railNearM[0], LOOK.railNearM[1], positionWorld.distance(cameraPosition));
  m.opacityNode = clamp(float(T.bodyAlpha).add(wall.mul(T.wallAlpha)).add(streak.mul(0.6)).add(low.mul(0.3))
    .add(core.mul(LOOK.artRailCoreAlpha)).add(glint.mul(0.5)), 0, 1).mul(near);
  return { material: m, uniforms: u };
}
