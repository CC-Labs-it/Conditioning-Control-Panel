/* Static machine surfaces: metre-sized panel seams, brushed wear and recessed fasteners.
 * All geometry is static, so world coordinates keep one physical texel scale across instanced parts. */
import { MeshBasicNodeMaterial, Color } from 'three/webgpu';
import { vec2, vec3, float, uv, positionWorld, normalWorld, cameraPosition, normalize, dot, abs, max, min, mix, fract, smoothstep, fwidth, pow, texture, sin } from 'three/tsl';

export function coolantCaustics(p, time) {
  const a = p.x.mul(0.63).add(sin(p.y.mul(0.42).add(time.mul(0.2))));
  const b = p.y.mul(0.71).add(sin(p.x.mul(0.37).sub(time.mul(0.16))));
  const wave = abs(sin(a).mul(sin(b)));
  return float(1).sub(smoothstep(0.035, max(fwidth(wave), 0.025).add(0.16), wave));
}

export function machineSurface(p, base, wear = null) {
  const q = p.div(vec2(3, 6)), cell = fract(q), edge = min(cell, float(1).sub(cell));
  const pixel = max(fwidth(q), vec2(0.0001));
  const seams = max(float(1).sub(smoothstep(0.004, pixel.x.add(0.009), edge.x)), float(1).sub(smoothstep(0.003, pixel.y.add(0.006), edge.y)));
  const bolt = float(1).sub(smoothstep(0.012, max(pixel.x, pixel.y).add(0.023), cell.sub(0.055).length()));
  const brushed = wear ? texture(wear, p.div(vec2(2, 0.35))).r : float(0.5);
  return base.mul(brushed.mul(0.16).add(0.88)).mul(float(1).sub(seams.mul(0.48)))
    .add(vec3(0.09, 0.12, 0.13).mul(bolt));
}

export function createMachineMaterial(hex, art, { glow = false, vent = false, coolant = null } = {}) {
  const c = new Color(hex), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
  material.name = 'machine-' + hex;
  if (glow) { material.colorNode = base; return material; }
  const N = normalize(normalWorld), weights = abs(N), p = positionWorld;
  const wear = art?.detail?.('detail-gold-wear');
  const plane = p.xz.mul(weights.y).add(p.xy.mul(weights.z)).add(p.zy.mul(weights.x));
  let painted = machineSurface(plane, base, wear);
  if (vent) {
    const slit = abs(fract(uv().y.mul(7)).sub(0.5));
    painted = painted.mul(smoothstep(0.12, max(fwidth(slit), 0.02).add(0.18), slit).mul(0.75).add(0.25));
  }
  const L = normalize(vec3(0.3, 0.8, 0.5)), V = normalize(cameraPosition.sub(p)), H = normalize(L.add(V));
  const diffuse = max(dot(N, L), 0).mul(0.55).add(0.45);
  const spec = pow(max(dot(N, H), 0), 28).mul(0.18);
  material.colorNode = painted.mul(diffuse).add(vec3(0.3, 0.42, 0.4).mul(spec));
  if (coolant) material.colorNode = material.colorNode.mul(vec3(0.45, 0.8, 1.2))
    .add(vec3(0.025, 0.18, 0.25).mul(coolantCaustics(plane, coolant)));
  return material;
}
