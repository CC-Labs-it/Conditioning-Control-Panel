/* render/materials/water.js - the water surface of stream and pool vertices, as TSL nodes the road material
 * mixes in by the `surface` attribute (3 stream, 4 pool; anything else is glass). No draw of its own: the
 * road is still one batch, this is a branch inside its shader.
 *
 * Two ripple fields scroll with the `flow` attribute (direction and speed from the
 * current; a still pool breathes at WATER_LOOK.baseScrollMps) make the normal; foam gathers toward the
 * rails and on a pool's edges; the body takes the section palette (pal.col, never a fixed colour), a faint
 * underglow from below, a soft specular from LOOK.sheenDir and a sky reflection on the fresnel.
 * The raised water volume supplies Astra's existing masks through `fields = { slope(q), foam(q) }`.
 * Missing art uses the same procedural field coordinates and keeps the contact geometry intact.
 * Gentle: the scroll clock runs at half speed; the shared music envelope reduces optical wave/light gain.
 */
import { float, vec2, vec3, sin, cos, mix, smoothstep, clamp, max, abs, dot, pow, normalize, length, select, reflect, attribute } from 'three/tsl';
import { LOOK } from '../look.js';
import { WATER_LOOK } from '../waterMath.js';
import { reactiveWaterNodes, waterHelixLight } from '../audioReactive.js';

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
export function createWaterSurface({ surface, flow, p, trueEdge, V, N0, pal, u, skyRamp = null, fields = null, deformation = null, reactive = null, L = WATER_LOOK }) {
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
  const response = reactive ? reactiveWaterNodes(p, reactive) : null;
  const smallSlope = slopeOf(qA).add(slopeOf(qB).mul(0.6)).mul(L.bump);
  // Heightfield slopes are measured in lateral/along metres. They follow the same transported
  // tangent frame as the displaced vertices, including banked turns and the curved canal walls.
  // Short capillary ripples break up reflections between the larger displaced swells.
  const rippleA=proceduralSlope(p.mul(vec2(.39,.31)).add(vec2(u.t.mul(.24),u.t.mul(-.19))));
  const rippleB=proceduralSlope(p.mul(vec2(-.61,.47)).add(vec2(u.t.mul(-.16),u.t.mul(.27))));
  const fineRipples=rippleA.mul(.075).add(rippleB.mul(.045));
  const wakeSlope = deformation ? deformation.slope.add(smallSlope.mul(0.55)).add(fineRipples) : smallSlope;
  const slope = response ? wakeSlope.add(response.slope) : wakeSlope;
  // the road normal tilted by the slope (u along the road's right, v along its tangent: both lie in the plane)
  const N = deformation ? normalize(N0.sub(deformation.B.mul(slope.x)).sub(deformation.T.mul(slope.y)))
    : normalize(N0.add(vec3(slope.x, 0, slope.y)));
  // foam: toward the rails, more on a pool, a little toward the side a lateral current runs into
  const rail = float(1).sub(smoothstep(L.foamRailM[0], L.foamRailM[1], max(trueEdge, 0)));
  const sideFoam = clamp(flow.y.mul(p.x).sign().max(0).mul(abs(flow.y)).mul(L.foamLatMul), 0, 1);
  const crest = smoothstep(0.2, 0.9, slope.y.mul(2).add(0.5));
  const foamBase = fields && fields.foam ? fields.foam(qA) : crest;
  const bankFoam = clamp(rail.mul(pool.mul(L.poolFoamMul - 1).add(1)).add(sideFoam.mul(rail)), 0, 1).mul(foamBase.mul(0.6).add(0.4));
  // The simulation supplies the trail footprint; the existing mask breaks its edge into small flecks.
  // Thresholding keeps dilute residual foam from bleaching whole atlas cells into white patches.
  const foam = deformation ? smoothstep(0.055, 0.24, deformation.foam)
    .mul(foamBase.mul(0.65).add(0.35)).add(bankFoam.mul(0.12)).clamp(0,1) : bankFoam;
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
  let colour = body.add(glow).add(sky.mul(fres)).add(white.mul(spec)).add(white.mul(foam.mul(L.foamGain)));
  if (deformation) {
    // Actual nearby architecture and screens reflect through the displaced normal.
    // Transmission stays clear enough to reveal the recessed physical bed.
    const elevation = (deformation.height || float(0)).mul(1.6).add(0.5).clamp(0,1);
    const transmitted = pal.col(2).mul(0.08).add(vec3(0.035,0.24,0.265)).mul(elevation.mul(0.48).add(0.52));
    const fallback = pal.col(0).mul(0.13).add(vec3(.015,.035,.045));
    const reflected = deformation.reflection ? mix(fallback,deformation.reflection(R),deformation.reflectionReady || float(1)) : fallback;
    const grazing = pow(float(1).sub(ndv),5).mul(.98).add(.02);
    const sharpSpec = pow(clamp(dot(N,H),0,1),64).mul(0.95);
    const crestLight=smoothstep(.56,.92,elevation).mul(.065);
    colour = mix(transmitted,reflected,grazing.mul(.67).add(.12))
      .add(vec3(0.72,0.9,1).mul(sharpSpec))
      .add(vec3(.35,.7,.76).mul(crestLight))
      .add(white.mul(foam).mul(0.12));
  }
  if (response) {
    // Soft refracted bands and crest light use the same phase as the optical slope. Neither
    // displaces the physical field nor changes the kart wake, spray origin or collision surface.
    const liquidTint = pal.col(0).mul(0.55).add(pal.col(1).mul(0.45));
    colour = colour.add(liquidTint.mul(response.light)).add(white.mul(response.crest));
    colour = colour.add(waterHelixLight(p, reactive, liquidTint));
  }
  const alpha = deformation ? float(.52).add(pow(float(1).sub(ndv),3).mul(.34)).add(foam.mul(.06)).clamp(.52,.93)
    : clamp(float(L.alpha).add(foam.mul(0.1)).add(spec.mul(0.3)), 0, 1);
  return { w, colour, alpha, foam, N, pool };
}
