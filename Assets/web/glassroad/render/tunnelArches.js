/* Solid swept ribs with a fitted industrial skin. The central driving aperture is unchanged. */
import { BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uv, vec2, vec3, float, sin, cos, abs, fract, smoothstep, fwidth,
  normalWorld, positionWorld, cameraPosition, normalize, dot, pow } from 'three/tsl';
import { archAt } from './mediaMath.js';
import { machineSurface } from './mainframeMaterials.js';

export function tunnelArchGeometry(tunnel, track, spec, segments = 24) {
  const positions = [], uvs = [], indices = [];
  const step = tunnel.rings.length > 1 ? tunnel.rings[1] - tunnel.rings[0] : (spec.stepM ?? 9);
  const stations = tunnel.rings.map(s => s - step / 2);
  if (tunnel.rings.length) stations.push(tunnel.rings.at(-1) + step / 2);
  const width = 1.15, thickness = .72;
  const quad = points => {
    const start = positions.length / 3;
    for (const [p, u, v] of points) { positions.push(...p); uvs.push(u, v); }
    indices.push(start, start + 1, start + 2, start, start + 2, start + 3);
  };
  for (const station of stations) {
    const rings = [-width / 2, width / 2].map(offset => {
      const frame = track.frameAt(station + offset), half = frame.w / 2 + (spec.padM ?? 3.5) - .15;
      return Array.from({ length: segments + 1 }, (_, i) => {
        const { P, inward } = archAt(frame, i / segments * Math.PI, half, tunnel.heightM - .15, 1);
        return [P, P.map((v, axis) => v - inward[axis] * thickness)];
      });
    });
    for (let i = 0; i < segments; i++) {
      const u0 = i / segments * Math.PI * tunnel.halfW, u1 = (i + 1) / segments * Math.PI * tunnel.halfW;
      for (const depth of [0, 1]) quad([[rings[0][i][depth], u0, 0], [rings[1][i][depth], u0, width],
        [rings[1][i + 1][depth], u1, width], [rings[0][i + 1][depth], u1, 0]]);
      for (const edge of [0, 1]) quad([[rings[edge][i][0], u0, 0], [rings[edge][i][1], u0, thickness],
        [rings[edge][i + 1][1], u1, thickness], [rings[edge][i + 1][0], u1, 0]]);
    }
    for (const i of [0, segments]) quad([[rings[0][i][0], 0, 0], [rings[1][i][0], width, 0],
      [rings[1][i][1], width, thickness], [rings[0][i][1], 0, thickness]]);
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new Float32BufferAttribute(uvs, 2));
  geometry.setIndex(indices); geometry.computeVertexNormals(); geometry.computeBoundingSphere();
  return geometry;
}

export function tunnelArchMaterial(time, contrast, trackId, art = null) {
  const keep = trackId === 'keep', material = new MeshBasicNodeMaterial({ side: DoubleSide });
  const p = uv(), base = keep ? vec3(.24, .12, .19) : vec3(.16, .28, .23);
  const accent = keep ? vec3(.95, .42, .14) : vec3(.15, .9, .55);
  const N = normalize(normalWorld), V = normalize(cameraPosition.sub(positionWorld)), L = normalize(vec3(.3, .8, .5));
  const light = abs(dot(N, L)).mul(.6).add(.4);
  const brushed = sin(p.x.mul(90).add(sin(p.y.mul(13)))).mul(.045)
    .mul(float(1).sub(smoothstep(.3, 1.2, fwidth(p.x.mul(90))))).add(1);
  const plate = machineSurface(p.mul(vec2(1, 4)), base, art?.detail?.('detail-gold-wear')).mul(brushed);
  const inset = smoothstep(.12, .22, p.y).mul(float(1).sub(smoothstep(.44, .54, p.y)));
  const segments = smoothstep(.08, .16, fract(p.x.div(2.8)))
    .mul(float(1).sub(smoothstep(.82, .92, fract(p.x.div(2.8)))));
  // A long packet travels around the physical rib, never a full-arch flash.
  const travel = cos(p.x.mul(.24).sub(time.mul(.55))).mul(.5).add(.5);
  const strip = inset.mul(segments).mul(travel.mul(.65).add(.22)).mul(contrast);
  const specular = pow(abs(dot(N, normalize(V.add(L)))), 28).mul(.16);
  material.colorNode = plate.mul(light).add(vec3(.5, .65, .6).mul(specular)).add(accent.mul(strip));
  material.name = keep ? 'keep-forged-tunnel-ribs' : 'mainframe-brushed-tunnel-ribs';
  return material;
}
