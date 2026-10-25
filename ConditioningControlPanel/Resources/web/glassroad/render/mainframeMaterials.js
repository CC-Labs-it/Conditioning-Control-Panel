/* Recessed industrial panels. Geometry-local detail remains attached to moving machinery. */
import { MeshBasicNodeMaterial, Color } from 'three/webgpu';
import { vec2, vec3, float, uv, positionGeometry, positionWorld, normalWorld, cameraPosition, normalize, dot, abs, max, min, mix, fract, smoothstep, fwidth, pow, texture, sin, floor } from 'three/tsl';

export function coolantCaustics(p, time) {
  // Interfering warped wave fronts form moving cells, rather than a scrolling square grid.
  const a = p.x.mul(.82).add(time.mul(.23)).add(sin(p.y.mul(.67).add(time.mul(.41))).mul(.9));
  const b = p.y.mul(1.07).sub(time.mul(.17)).add(sin(p.x.mul(.59).sub(time.mul(.32))).mul(.8));
  const wave = abs(sin(a).add(sin(b)).add(sin(a.mul(.67).sub(b.mul(.53)).add(time.mul(.19))).mul(.36)));
  const cell = float(1).sub(smoothstep(.045, max(fwidth(wave), .025).add(.19), wave));
  return cell.mul(sin(a.mul(.4).add(b.mul(.3))).mul(.18).add(.82));
}

export function plateDetails(p, size = vec2(4, 6)) {
  const cell = fract(p.div(size)).mul(size), edge = min(cell, size.sub(cell));
  const px = max(fwidth(p), vec2(0.003)), filter = max(px.x, px.y);
  const visibility = float(1).sub(smoothstep(.18, .65, filter));
  const seam = float(1).sub(smoothstep(0.035, filter.add(0.075), min(edge.x, edge.y))).mul(visibility);
  const rim = smoothstep(0.065, filter.add(0.1), min(edge.x, edge.y))
    .mul(float(1).sub(smoothstep(0.16, filter.add(0.24), min(edge.x, edge.y))));
  // Opposing plate edges catch different light, giving the seam a readable lip.
  const side = smoothstep(0.2, 0.8, cell.x.div(size.x).mul(0.65).add(cell.y.div(size.y).mul(0.35)));
  const socketDistance = edge.sub(0.28).length();
  const socket = float(1).sub(smoothstep(0.12, filter.add(0.18), socketDistance));
  const screw = float(1).sub(smoothstep(0.065, filter.add(0.10), socketDistance));
  const slot = float(1).sub(smoothstep(0.015, filter.add(0.025), abs(edge.x.sub(edge.y)))).mul(screw);
  const variation = sin(floor(p.x.div(size.x)).mul(2.7).add(floor(p.y.div(size.y)).mul(1.3))).mul(0.10).add(0.94);
  return { seam, rim: rim.mul(visibility), side, socket: socket.mul(visibility),
    screw: screw.mul(visibility), slot: slot.mul(visibility), variation, cell, edge, filter, visibility };
}

export function machineSurface(p, base, wear = null, details = null, { flush = false } = {}) {
  const d = details || plateDetails(p);
  const brushed = wear ? texture(wear, p.div(vec2(2.2, 0.55))).r : float(0.5);
  // Broad pressed recesses read from the chase camera; fine machining disappears before aliasing.
  const hatch = max(abs(d.cell.x.sub(2)).sub(1.25), abs(d.cell.y.sub(3)).sub(2.1));
  const inset = float(1).sub(smoothstep(-.04, d.filter.add(.04), hatch)).mul(d.visibility);
  const lip = float(1).sub(smoothstep(.025, d.filter.add(.09), abs(hatch))).mul(d.visibility);
  const brushPhase = p.y.mul(82).add(sin(p.x.mul(.8)).mul(.6));
  const brushVisibility = float(1).sub(smoothstep(.3, 1.1, fwidth(brushPhase)));
  const machining = sin(brushPhase).mul(.05).mul(brushVisibility).add(1);
  const finish = base.mul(brushed.mul(0.24).add(0.82)).mul(d.variation).mul(machining)
    .mul(float(1).sub(inset.mul(flush ? 0 : .16)))
    .mul(float(1).sub(d.seam.mul(flush ? .22 : .8))).mul(float(1).sub(d.socket.mul(flush ? .15 : .72)));
  // Floor joints are flush paint and sealed fasteners. Only machinery has raised pressed lips.
  return finish.add(base.mul(d.rim).mul(d.side.mul(flush ? .08 : .78).sub(flush ? .025 : .2)))
    .add(base.mul(lip).mul(flush ? 0 : .23))
    .add(vec3(0.15, 0.19, 0.20).mul(d.screw).mul(flush ? .22 : 1))
    .mul(float(1).sub(d.slot.mul(flush ? .16 : .65)));
}

export function createMachineMaterial(hex, art, { glow = false, vent = false, coolant = null } = {}) {
  const c = new Color(hex), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
  material.name = 'machine-' + hex;
  if (glow) { material.colorNode = base; return material; }
  const N = normalize(normalWorld), p = positionWorld;
  const wear = art?.detail?.('detail-gold-wear');
  // UV paneling fits each manufactured face. Wear samples pre-instance source coordinates.
  const plane = uv().mul(vec2(6, 10));
  let painted = machineSurface(plane, base);
  const brushed = wear ? texture(wear, positionGeometry.xy.mul(vec2(3.5, 11))).r : float(0.5);
  painted = painted.mul(brushed.mul(0.22).add(0.84));
  if (vent) {
    const slit = abs(fract(uv().y.mul(7)).sub(0.5));
    const shade = smoothstep(0.12, max(fwidth(slit), 0.02).add(0.18), slit);
    painted = painted.mul(shade.mul(0.86).add(0.14)).add(base.mul(0.22).mul(smoothstep(0.2, 0.23, slit).mul(float(1).sub(smoothstep(0.23, 0.26, slit)))));
  }
  const L = normalize(vec3(0.3, 0.8, 0.5)), V = normalize(cameraPosition.sub(p)), H = normalize(L.add(V));
  const diffuse = max(dot(N, L), 0).mul(0.56).add(0.44);
  const spec = pow(max(dot(N, H), 0), brushed.mul(18).add(24)).mul(brushed.mul(-0.14).add(0.32));
  const edge = pow(float(1).sub(abs(dot(N, V))), 3).mul(0.025);
  material.colorNode = painted.mul(diffuse).add(vec3(0.62, 0.72, 0.73).mul(spec.add(edge)));
  if (coolant) material.colorNode = material.colorNode.mul(vec3(0.45, 0.8, 1.2))
    .add(vec3(0.025, 0.18, 0.25).mul(coolantCaustics(plane, coolant)));
  return material;
}
