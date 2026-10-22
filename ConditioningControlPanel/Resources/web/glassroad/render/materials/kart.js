/* render/materials/kart.js - the kart materials. The greybox kart and the floating props are unlit-shaded
 * (colours on the vertex `color` attribute, a fixed key direction, an ambient floor and a view rim, all
 * from LOOK). The art kart's kart_surface (createKartSurface) carries every racer's colourway as
 * per-material UNIFORMS, so all eight karts share one shader program (the polish 1 review found seven,
 * one per baked colourway). Colours and tints are uniforms everywhere here for the same reason.
 */
import { MeshBasicNodeMaterial, MeshPhysicalNodeMaterial, Color, Vector3 } from 'three/webgpu';
import {
  vertexColor, normalWorld, normalView, normalize, dot, max, vec3, abs, uniform, attribute, texture, float, mix, pow,
  materialColor, materialEmissive, positionViewDirection,
} from 'three/tsl';
import { LOOK } from '../look.js';

export function shade(colour) {
  const L = normalize(vec3(...LOOK.lightDir));
  const lit = max(dot(normalWorld, L), 0).mul(1 - LOOK.ambient).add(LOOK.ambient);
  const rim = abs(normalView.z).oneMinus().mul(LOOK.rimGain);
  return colour.mul(lit).add(rim);
}

/** tint: [r, g, b] multiplied into the vertex colours (greybox rivals). uniforms.tint is live. */
export function createKartMaterial(tint = [1, 1, 1]) {
  const m = new MeshBasicNodeMaterial(), u = { tint: uniform(new Vector3(...tint)) };
  m.colorNode = shade(vertexColor().mul(u.tint));
  return { material: m, uniforms: u };
}

export function createPropMaterial(hex) {
  const m = new MeshBasicNodeMaterial({ transparent: true, opacity: 0.85 }), u = { colour: uniform(new Color(hex)) };
  m.colorNode = shade(u.colour);
  return { material: m, uniforms: u };
}

const SKIP = new Set(['uuid', 'colorNode', 'emissiveNode', 'opacityNode']);

/**
 * A racer's kart_surface: the loader material copied into a node material (as the renderer's own
 * fromMaterial does), then the masked cup / saucer tint (weight = mask x base alpha, ART.md "Tint binding")
 * and a coloured view rim added to the emissive (the ghost wash still drives .emissive). look = a
 * kartPose.rivalLook (cupMul, saucerMul, rimLin) or null for EMI's own porcelain (identity, no rim).
 * R: rimPow, rimGain (the same for every racer, so the program is shared).
 */
export function createKartSurface(src, look, R) {
  const m = new MeshPhysicalNodeMaterial();
  for (const key in src) if (!SKIP.has(key)) m[key] = src[key];
  const u = { cup: uniform(new Vector3(1, 1, 1)), saucer: uniform(new Vector3(1, 1, 1)), rim: uniform(new Vector3(0, 0, 0)) };
  const keep = src.map ? texture(src.map).a : float(1);
  const tintBy = (name, mul) => mix(vec3(1, 1, 1), mul, attribute(name, 'float').mul(keep));
  m.colorNode = materialColor.rgb.mul(tintBy('_cup_tint', u.cup)).mul(tintBy('_saucer_tint', u.saucer));
  m.opacityNode = float(1);
  const fres = pow(float(1).sub(abs(normalView.dot(positionViewDirection))), R.rimPow);
  m.emissiveNode = materialEmissive.add(u.rim.mul(fres.mul(R.rimGain)));
  if (look) { u.cup.value.fromArray(look.cupMul); u.saucer.value.fromArray(look.saucerMul); u.rim.value.fromArray(look.rimLin); }
  return { material: m, uniforms: u };
}
