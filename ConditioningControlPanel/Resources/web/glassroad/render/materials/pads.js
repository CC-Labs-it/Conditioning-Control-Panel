/* render/materials/pads.js - boost pads and jump lip markers: stripes running along v, a pulse uniform.
 * uv: u across, v metres along from 0. phase is advanced on the CPU; pulse is fx.padRing driven.
 * Given a mask (boost-chevron-mask) the pad scrolls chevrons, one per artChevronM, across
 * artChevronWideM; without one it scrolls plain stripes.
 *
 * Polish 2 (WORLD lane): the chevrons flow. Each one has a hot leading edge and leaves a soft
 * trailing smear behind it, two bright guide lines run down the pad's sides and the floor glows
 * warmer at the centre line. Everything is spatial and rides the same CPU phase (Gentle's slower
 * scroll slows it too); there is no brightness swell in time, so nothing on a pad can pulse.
 *
 * Polish 3 (SPECTRUM lane): every chevron wears its own spectrum hue (the shared ramp, palette.js
 * spectrumRamp, PAD_FLOW.hueStep of a turn per chevron length, fixed along the pad) over the pad colour, with a white-hot leading
 * edge, so a pad reads as a clear run of bright arrows. Lips (no mask) take a lighter rainbow tint.
 */
import { MeshBasicNodeMaterial, AdditiveBlending, Color } from 'three/webgpu';
import { uniform, uv, fract, smoothstep, vec2, vec3, texture, clamp, abs, float, max, mix, fwidth, attribute, step, sin, cos, floor, mod } from 'three/tsl';
import { acquireLoomAtlas, loomAtlasUv } from '../loomAtlas.js';
import { ROAD_AA } from '../roadAaMath.js';
import { LOOK } from '../look.js';
import { spectrumRamp } from './palette.js';

/** Pad flow numbers; LOOK.padFlow overrides any key. */
export const PAD_FLOW = Object.freeze({
  trailM: 0.9, trailGain: 0.45, leadM: 0.35, leadGain: 0.6,
  edgeAt: 0.92, edgeW: 0.05, edgeGain: 0.7, centreGain: 0.25,
  hueStep: 0.2, hueMix: 0.75, lipHueMix: 0.35, gain: 1.5, leadWhite: 0.8,
});

export function createPadMaterial(hex, mask = null, { artPads = false } = {}) {
  const F = { ...PAD_FLOW, ...(LOOK.padFlow || {}) };
  const u = { phase: uniform(0), pulse: uniform(0), spiralTurn: uniform(0) };
  const p = uv();
  const halfW = LOOK.artChevronWideM / 2;
  const across = mask ? clamp(p.x.div(LOOK.artChevronWideM).add(0.5), 0, 1) : null;
  // no fract here: the mask repeats along v, and a fract made a 1 px seam of the coarsest mip at every wrap
  const at = (back) => p.y.add(back).div(LOOK.artChevronM).sub(u.phase);
  // a lip's plain stripes: on over the back half of each period, both edges blended over a pixel (the wrap
  // edge was a hard step) and melted to the half-lit average where the stripes pass a pixel (road widen-and-melt)
  const sx = p.y.div(LOOK.padStripeM).sub(u.phase), spx = fwidth(sx);
  const sw = max(spx.mul(ROAD_AA.stepMul), 0.05);
  const stripe = mask
    ? texture(mask, vec2(across, at(0))).r
    : mix(smoothstep(sw.negate(), sw, float(0.25).sub(abs(fract(sx).sub(0.75)))), float(0.5), smoothstep(ROAD_AA.meltFrom, ROAD_AA.meltTo, spx));
  // a smear behind each chevron (the mask read a little further back, fainter)
  const trail = mask
    ? texture(mask, vec2(across, at(F.trailM))).r.mul(F.trailGain).add(texture(mask, vec2(across, at(F.trailM * 2))).r.mul(F.trailGain * 0.45))
    : float(0);
  // a hot leading edge on each chevron (where the mask a little ahead is already dark)
  const lead = mask ? max(stripe.sub(texture(mask, vec2(across, at(-F.leadM))).r), 0).mul(F.leadGain) : float(0);
  const side = abs(p.x).div(halfW);
  // guide lines and the centre glow only on boost pads (a lip's u is the road's lateral, not centred)
  const edge = mask ? float(1).sub(smoothstep(0, F.edgeW, abs(side.sub(F.edgeAt)))).mul(F.edgeGain) : float(0);
  const centre = mask ? float(1).sub(smoothstep(0, 0.6, side)).mul(F.centreGain) : float(0);
  const lo = mask ? LOOK.artChevronFloor : 0.6;
  const body = max(stripe, trail).add(lead);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const c = new Color(hex);
  // a fixed rainbow along the pad (row 0 of the spectrum ramp); the chevrons run through it, so each
  // lights in its own hue and none ever changes colour in place (the phase wraps, the hue never does)
  const along = p.y.div(mask ? LOOK.artChevronM : LOOK.padStripeM);
  const hueCol = texture(spectrumRamp(), vec2(fract(along.mul(F.hueStep).add(0.08).add(abs(p.x).mul(0.03))), 1 / 6)).rgb.mul(1.6);
  const tint = mix(vec3(c.r, c.g, c.b), hueCol, mask ? F.hueMix : F.lipHueMix);
  m.colorNode = tint.mul(body.mul(1 - lo).add(lo).add(centre)).mul(F.gain).add(vec3(1, 0.97, 0.92).mul(edge.add(lead.mul(F.leadWhite))))
    .mul(u.pulse.mul(LOOK.padPulseGain).add(1));
  if (artPads && (typeof document !== 'undefined' || typeof OffscreenCanvas !== 'undefined')) {
    const atlas = acquireLoomAtlas();
    const shape = attribute('padDesign', 'vec4'), q = vec2(p.x.div(abs(shape.x).mul(.72)), p.y.div(shape.y.mul(.65)).sub(1 / 1.3));
    const angle = u.spiralTurn, at = vec2(q.x.mul(cos(angle)).sub(q.y.mul(sin(angle))), q.x.mul(sin(angle)).add(q.y.mul(cos(angle)))).add(.5);
    const style = mod(floor(abs(shape.z).div(17)), 6);
    const inset = float(1).sub(smoothstep(.46,.5,max(abs(q.x),abs(q.y)))).mul(float(1).sub(step(0,shape.x)));
    const ink = texture(atlas.texture,loomAtlasUv(at,style)).rgb.dot(vec3(.2126,.7152,.0722));
    m.colorNode = mix(m.colorNode, tint.mul(ink.mul(1.25).add(.1)), inset);
    m.addEventListener('dispose', atlas.dispose);
  }
  return { material: m, uniforms: u };
}
