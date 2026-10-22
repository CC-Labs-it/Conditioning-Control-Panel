/* render/materials/pads.js - boost pads and jump lip markers: stripes running along v, a pulse uniform.
 * uv: u across, v metres along from 0. phase is advanced on the CPU; pulse is fx.padRing driven.
 */
import { MeshBasicNodeMaterial, AdditiveBlending, Color } from 'three/webgpu';
import { uniform, uv, fract, smoothstep, vec3 } from 'three/tsl';
import { LOOK } from '../look.js';

export function createPadMaterial(hex) {
  const u = { phase: uniform(0), pulse: uniform(0) };
  const stripe = smoothstep(0.45, 0.55, fract(uv().y.div(LOOK.padStripeM).sub(u.phase)));
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const c = new Color(hex);
  m.colorNode = vec3(c.r, c.g, c.b).mul(stripe.mul(0.4).add(0.6)).mul(u.pulse.mul(LOOK.padPulseGain).add(1));
  return { material: m, uniforms: u };
}
