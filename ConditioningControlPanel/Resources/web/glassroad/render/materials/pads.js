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
import { uniform, uv, fract, smoothstep, vec2, vec3, texture, clamp, abs, float, max, mix } from 'three/tsl';
import { LOOK } from '../look.js';
import { spectrumRamp } from './palette.js';

/** Pad flow numbers; LOOK.padFlow overrides any key. */
export const PAD_FLOW = Object.freeze({
  trailM: 0.9, trailGain: 0.45, leadM: 0.35, leadGain: 0.6,
  edgeAt: 0.92, edgeW: 0.05, edgeGain: 0.7, centreGain: 0.25,
  hueStep: 0.2, hueMix: 0.75, lipHueMix: 0.35, gain: 1.5, leadWhite: 0.8,
});

export function createPadMaterial(hex, mask = null) {
  const F = { ...PAD_FLOW, ...(LOOK.padFlow || {}) };
  const u = { phase: uniform(0), pulse: uniform(0) };
  const p = uv();
  const halfW = LOOK.artChevronWideM / 2;
  const across = mask ? clamp(p.x.div(LOOK.artChevronWideM).add(0.5), 0, 1) : null;
  const at = (back) => fract(p.y.add(back).div(LOOK.artChevronM).sub(u.phase));
  const stripe = mask
    ? texture(mask, vec2(across, at(0))).r
    : smoothstep(0.45, 0.55, fract(p.y.div(LOOK.padStripeM).sub(u.phase)));
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
  return { material: m, uniforms: u };
}
