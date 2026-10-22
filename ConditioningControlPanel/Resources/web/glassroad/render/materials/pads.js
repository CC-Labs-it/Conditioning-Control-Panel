/* render/materials/pads.js - boost pads and jump lip markers: stripes running along v, a pulse uniform.
 * uv: u across, v metres along from 0. phase is advanced on the CPU; pulse is fx.padRing driven.
 * Given a mask (boost-chevron-mask) the pad scrolls chevrons, one per artChevronM, across
 * artChevronWideM; without one it scrolls plain stripes.
 */
import { MeshBasicNodeMaterial, AdditiveBlending, Color } from 'three/webgpu';
import { uniform, uv, fract, smoothstep, vec2, vec3, texture, clamp } from 'three/tsl';
import { LOOK } from '../look.js';

export function createPadMaterial(hex, mask = null) {
  const u = { phase: uniform(0), pulse: uniform(0) };
  const stripe = mask
    ? texture(mask, vec2(clamp(uv().x.div(LOOK.artChevronWideM).add(0.5), 0, 1), fract(uv().y.div(LOOK.artChevronM).sub(u.phase)))).r
    : smoothstep(0.45, 0.55, fract(uv().y.div(LOOK.padStripeM).sub(u.phase)));
  const lo = mask ? LOOK.artChevronFloor : 0.6;
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const c = new Color(hex);
  m.colorNode = vec3(c.r, c.g, c.b).mul(stripe.mul(1 - lo).add(lo)).mul(u.pulse.mul(LOOK.padPulseGain).add(1));
  return { material: m, uniforms: u };
}
