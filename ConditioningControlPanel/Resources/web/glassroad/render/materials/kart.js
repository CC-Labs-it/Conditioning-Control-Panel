/* render/materials/kart.js - one unlit-shaded material for the greybox kart (colours ride on the
 * vertex `color` attribute) and the floating props. No scene lights: a fixed key direction, an ambient
 * floor and a view rim, all from LOOK.
 */
import { MeshBasicNodeMaterial, Color } from 'three/webgpu';
import { vertexColor, normalWorld, normalView, normalize, dot, max, vec3, abs } from 'three/tsl';
import { LOOK } from '../look.js';

export function shade(colour) {
  const L = normalize(vec3(...LOOK.lightDir));
  const lit = max(dot(normalWorld, L), 0).mul(1 - LOOK.ambient).add(LOOK.ambient);
  const rim = abs(normalView.z).oneMinus().mul(LOOK.rimGain);
  return colour.mul(lit).add(rim);
}

/** tint: [r, g, b] multiplied into the vertex colours (rival karts, render/kartPose.js tintRgb). */
export function createKartMaterial(tint = [1, 1, 1]) {
  const m = new MeshBasicNodeMaterial();
  m.colorNode = shade(vertexColor().mul(vec3(...tint)));
  return { material: m };
}

export function createPropMaterial(hex) {
  const c = new Color(hex);
  const m = new MeshBasicNodeMaterial({ transparent: true, opacity: 0.85 });
  m.colorNode = shade(vec3(c.r, c.g, c.b));
  return { material: m };
}
