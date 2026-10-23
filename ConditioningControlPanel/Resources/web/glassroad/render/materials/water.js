/* render/materials/water.js - the water surface of stream and pool vertices, as TSL nodes the road material
 * mixes in by the `surface` attribute (3 stream, 4 pool; anything else is glass). No draw of its own: the
 * road is still one batch, this is a branch inside its shader.
 *
 * Procedural for now: two ripple fields scrolled with the `flow` attribute (direction and speed from the
 * current; a still pool breathes at WATER_LOOK.baseScrollMps) make the normal; foam gathers toward the
 * rails and on a pool's edges; the body takes the section palette (pal.col, never a fixed colour), a faint
 * underglow from below, a soft specular from LOOK.sheenDir and a sky reflection on the fresnel.
 * Astra's masks later replace the fields: pass `fields = { slope(q) -> vec2, foam(q) -> float }` built on
 * textures sampled at the same scrolled coordinates and nothing else changes.
 * Gentle: the scroll clock runs at half speed (the caller's uniform); nothing pulses.
 */
import { float, vec2, vec3, sin, cos, mix, smoothstep, clamp, max, abs, dot, pow, normalize, length, select, reflect, attribute } from 'three/tsl';
import { LOOK } from '../look.js';
import { WATER_LOOK } from '../waterMath.js';

const TAU = Math.PI * 2;

/** The road's water attributes (absent on an old mesh: scene.js meshFrom fills zeros = glass, no flow). */
export function waterAttributes() {
  return { surface: attribute('surface', 'float'), flow: attribute('flow', 'vec2') };
}

/** Water weight 0..1 from the surface code (3 / 4 water; the glass-to-water join eases over the triangle). */
export function waterWeight(surface) {
  return smoothstep(2.5, 3, surface).mul(float(1).sub(smoothstep(4.5, 5, surface)));
}

/** Procedural ripple slope at q (field coordinate in wavelengths): the analytic gradient of a bent sine. */
function proceduralSlope(q) {
  const bend = sin(q.y.mul(TAU * 0.7)).mul(0.8);
  const ph = q.x.mul(TAU).add(bend);
  const c = cos(ph);
  return vec2(c.mul(TAU), c.mul(cos(q.y.mul(TAU * 0.7))).mul(TAU * 0.7 * 0.8)).mul(1 / TAU);
}

/**
 * surface / flow: the attribute nodes; p: uv (u lateral m, v s m); trueEdge: metres in from the road edge;
 * V: unit vector to the camera; N0: the road normal; pal: the section palette; u: { t } the scroll clock
 * (seconds, Gentle slowed by the caller); skyRamp: pal.ramp wrapper for the reflection.
 * Returns { w, colour, alpha, foam, N }.
 */
export function createWaterSurface({ surface, flow, p, trueEdge, V, N0, pal, u, skyRamp = null, fields = null, L = WATER_LOOK }) {
  const w = waterWeight(surface);
  const pool = smoothstep(3.5, 4, surface);
  // scroll from the flow attribute (render/waterMath.js rippleScroll in numbers): along = v, lateral = u
  const mag = length(flow), dir = select(mag.greaterThan(1e-4), vec2(flow.y, flow.x).div(max(mag, 1e-4)), vec2(0, 1));
  const layer = (i) => {
    const c = Math.cos(L.rippleTurnRad[i]), s = Math.sin(L.rippleTurnRad[i]);
    const d = vec2(dir.x.mul(c).sub(dir.y.mul(s)), dir.x.mul(s).add(dir.y.mul(c)));
    const v = mag.mul(L.rippleSpeedMul[i]).add(L.baseScrollMps);
    return p.sub(d.mul(v).mul(u.t)).div(L.rippleM[i]);
  };
  const qA = layer(0), qB = layer(1);
  const slopeOf = fields && fields.slope ? fields.slope : proceduralSlope;
  const slope = slopeOf(qA).add(slopeOf(qB).mul(0.6)).mul(L.bump);
  // the road normal tilted by the slope (u along the road's right, v along its tangent: both lie in the plane)
  const N = normalize(N0.add(vec3(slope.x, 0, slope.y)));
  // foam: toward the rails, more on a pool, a little toward the side a lateral current runs into
  const rail = float(1).sub(smoothstep(L.foamRailM[0], L.foamRailM[1], max(trueEdge, 0)));
  const sideFoam = clamp(flow.y.mul(p.x).sign().max(0).mul(abs(flow.y)).mul(L.foamLatMul), 0, 1);
  const crest = smoothstep(0.2, 0.9, slope.y.mul(2).add(0.5));
  const foamBase = fields && fields.foam ? fields.foam(qA) : crest;
  const foam = clamp(rail.mul(pool.mul(L.poolFoamMul - 1).add(1)).add(sideFoam.mul(rail)), 0, 1).mul(foamBase.mul(0.6).add(0.4));
  // the body: the palette's deep colour under the surface, the bright one on the fresnel, an underglow from below
  const ndv = clamp(abs(dot(N, V)), 0, 1);
  const fres = pow(float(1).sub(ndv), 3);
  const deep = pal.col(2).mul(0.35).add(pal.col(1).mul(0.25));
  const body = mix(deep, pal.col(0).mul(0.7), fres.mul(0.8));
  const glow = pal.col(1).mul(L.underglow).mul(float(1).sub(fres));
  const R = reflect(V.negate(), N);
  const sky = skyRamp ? skyRamp(R.y.mul(0.35).add(R.x.mul(0.2)).add(0.5)).mul(LOOK.skyDim * 0.6) : pal.col(0).mul(0.3);
  const H = normalize(normalize(vec3(...LOOK.sheenDir)).add(V));
  const spec = pow(clamp(dot(N, H), 0, 1), L.specPow).mul(L.specGain);
  const white = vec3(1, 0.98, 0.99);
  const colour = body.add(glow).add(sky.mul(fres)).add(white.mul(spec)).add(white.mul(foam.mul(L.foamGain)));
  const alpha = clamp(float(L.alpha).add(foam.mul(0.1)).add(spec.mul(0.3)), 0, 1);
  return { w, colour, alpha, foam, N, pool };
}
