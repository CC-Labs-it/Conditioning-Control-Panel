/* render/materials/rails.js - the rails as glowing spectrum tubes. The rail mesh is a flat ribbon (uv:
 * u = metres up, v = s metres); across its height the shader bends a cylinder's normal from bottom to
 * top, so the ribbon reads as a round glass tube: bright fresnel walls at both edges, a sharp specular
 * streak near the top (the lamp), a soft one low down (the road's glow) and a coloured core inside
 * (rail-glow-mask when the art is in).
 *
 * Polish 3 (SPECTRUM lane): the core is the full-spectrum ramp (pal.ramp) streaming along the tube with
 * pal.flow, the same flow as the road, so colour pours toward the player faster with speed; the walls
 * reflect the spectrum half a turn away, and a few soft glints ride the flow. Hue changes along the tube
 * at levelled luminance, so nothing flashes. Periods are fitted to the lap (pal.flowWrap, which the road
 * sets) so the colour never jumps when the flow wraps. Gentle: pal.motion halves the flow and also
 * stands in for contrast (1 or 0.5), flattening the core toward the palette. Everything fades within LOOK.railNearM of the camera
 * so a rail beside the eye never walls the view.
 */
import { MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uv, uniform, clamp, mix, float, vec2, vec3, texture, smoothstep, positionWorld, cameraPosition, pow, abs, sin, cos, max } from 'three/tsl';
import { LOOK } from '../look.js';
import { fitPeriod } from '../spectrum.js';

/** Tube numbers; LOOK.railTube overrides any key. */
export const RAIL_TUBE = Object.freeze({
  bodyAlpha: 0.16, wallAlpha: 0.6, wallPow: 3, wallGain: 0.6, streakAt: 0.78, streakW: 0.035, streakGain: 0.9,
  lowAt: 0.22, lowW: 0.06, lowGain: 0.25, coreGain: 1.1, coreAlpha: 0.45, glintGain: 0.35, glintPow: 10,
  rampM: 48, glintM: 24,
});

export function createRailMaterial(pal, art = null) {
  const T = { ...RAIL_TUBE, ...(LOOK.railTube || {}) };
  // pulse / vib / t: the music road (render/musicRoad.js) glows the tube with the bass and shakes its streak with the hits
  const u = { flow: uniform(0), contrast: uniform(1), pulse: uniform(0), vib: uniform(0), t: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide, depthWrite: false });
  const glow = art && art.tex('rail-glow-mask');
  const h = clamp(uv().x.div(LOOK.railHeightM), 0, 1);
  const flow = pal.flow || u.flow, contrast = pal.motion || u.contrast;
  const lap = pal.flowWrap > 0 && pal.flowWrap < 1e5 ? pal.flowWrap : 0;
  const rampM = lap ? fitPeriod(lap, T.rampM) : T.rampM, glintM = lap ? fitPeriod(lap, T.glintM) : T.glintM;
  const a = uv().y.add(flow);
  const ramp = pal.ramp ? (x) => pal.ramp(x) : (x) => mix(pal.col(0), pal.col(1), sin(x.mul(Math.PI * 2)).mul(0.5).add(0.5));
  // the tube's cross section: angle from -90 to +90 degrees, wall = how edge-on the glass is
  const ang = h.sub(0.5).mul(Math.PI);
  const wall = pow(abs(sin(ang)), T.wallPow);
  const facing = cos(ang);
  const band = (at, wd) => float(1).sub(smoothstep(0, wd, abs(h.sub(at))));
  const shake = sin(uv().y.mul(0.9).sub(u.t.mul(14))).mul(u.vib).div(LOOK.railHeightM);
  const streak = band(shake.add(T.streakAt), T.streakW).mul(T.streakGain);
  const low = band(T.lowAt, T.lowW).mul(T.lowGain);
  const core = glow ? texture(glow, vec2(h, uv().y.div(LOOK.artRoadTileM[1]))).r.mul(0.6).add(facing.mul(facing).mul(0.4)) : facing.mul(facing);
  const x = a.div(rampM);
  const avg = pal.col(0).mul(0.6).add(pal.col(1).mul(0.4));
  const coreCol = mix(avg, ramp(x), contrast);
  const wallCol = ramp(x.add(0.5)).mul(LOOK.skyDim * 1.4).add(pal.col(0).mul(0.15));
  const glint = pow(max(sin(a.div(glintM).mul(Math.PI * 2)), 0), T.glintPow).mul(T.glintGain).mul(facing).mul(contrast);
  m.colorNode = coreCol.mul(core.mul(T.coreGain).add(0.2))
    .add(wallCol.mul(wall).mul(T.wallGain))
    .add(vec3(1, 0.97, 0.99).mul(streak.add(low).add(glint))).mul(u.pulse.add(1));
  const near = smoothstep(LOOK.railNearM[0], LOOK.railNearM[1], positionWorld.distance(cameraPosition));
  m.opacityNode = clamp(float(T.bodyAlpha).add(wall.mul(T.wallAlpha)).add(streak.mul(0.6)).add(low.mul(0.3))
    .add(core.mul(T.coreAlpha)).add(glint.mul(0.5)), 0, 1).mul(near);
  return { material: m, uniforms: u };
}

