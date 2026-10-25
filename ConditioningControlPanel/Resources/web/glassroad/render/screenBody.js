import { BufferGeometry, Float32BufferAttribute, MeshStandardNodeMaterial } from 'three/webgpu';
import { positionGeometry, uniform, float, abs, max, fract, smoothstep, fwidth } from 'three/tsl';

const outline = (r, cut, z) => [[-r + cut, -r, z], [r - cut, -r, z], [r, -r + cut, z], [r, r - cut, z],
  [r - cut, r, z], [-r + cut, r, z], [-r, r - cut, z], [-r, -r + cut, z]];

/** Shared low-poly cabinet: bevelled front, tapered sidewalls and a closed sculpted back.
 * Coordinates scale by the image width/height and physical depth, not the aperture size. */
export function screenBodyGeometry(rim = false) {
  const positions = [], colours = [];
  function triangle(a, b, c, shade = 1) {
    positions.push(...a, ...b, ...c);
    for (let i = 0; i < 3; i++) colours.push(shade, shade, shade);
  }
  function band(a, b, inside = false) {
    for (let i = 0; i < 8; i++) {
      const j = (i + 1) % 8, shade = i === 4 ? 1 : i === 0 ? .58 : i >= 5 ? .87 : .68;
      if (inside) { triangle(a[i], b[i], b[j], shade); triangle(a[i], b[j], a[j], shade); }
      else { triangle(a[i], a[j], b[j], shade); triangle(a[i], b[j], b[i], shade); }
    }
  }
  if (rim) {
    // The opening exactly contains the full rectangular image, including its corners.
    const outer = outline(.54, .028, .06), inner = outline(.501, .0005, .006);
    band(outer, inner);
    band(outline(.54, .028, -.08), outer);
    band(inner, outline(.501, .0005, -.08));
  } else {
    const front = outline(.54, .028, -.08), shoulder = outline(.54, .028, -.28), back = outline(.506, .045, -1);
    band(front, shoulder, true); band(shoulder, back, true);
    for (let i = 0; i < 8; i++) {
      const j = (i + 1) % 8;
      triangle([0, 0, -.08], front[i], front[j], .55);
      triangle([0, 0, -1], back[j], back[i], .48);
    }
  }
  const g = new BufferGeometry();
  g.setAttribute('position', new Float32BufferAttribute(positions, 3));
  g.setAttribute('color', new Float32BufferAttribute(colours, 3));
  g.computeVertexNormals(); g.computeBoundingBox(); g.computeBoundingSphere();
  return g;
}

export function screenBodyMaterial(rim = false, options = {}) {
  const material = new MeshStandardNodeMaterial({ color: rim ? '#b9bcc4' : '#414553', vertexColors: true,
    roughness: rim ? .29 : .53, metalness: rim ? .62 : .32,
    emissive: rim ? '#101017' : '#080a10', emissiveIntensity: .45, ...options });
  if (!rim) {
    const p = positionGeometry;
    // Source-local coordinates keep the finish attached to orbiting and instanced cabinets.
    // Derivative-sized edges quietly blend out at distance instead of crawling or shimmering.
    const line = (distance, width) => float(1).sub(smoothstep(width, fwidth(distance).max(.001).add(width), distance));
    const rear = float(1).sub(smoothstep(-.98, -.9, p.z));
    const panelDistance = max(abs(p.x).sub(.435), abs(p.y).sub(.41));
    const seam = line(abs(panelDistance), .0045);
    const window = float(1).sub(smoothstep(.30, .34, abs(p.x)))
      .mul(float(1).sub(smoothstep(.155, .185, abs(p.y))));
    const grooveDistance = abs(fract(p.y.mul(16)).sub(.5));
    const vents = line(grooveDistance, .085).mul(window);
    const lip = line(abs(fract(p.y.mul(16).add(.13)).sub(.5)), .04).mul(window);
    const shade = float(1).sub(rear.mul(seam.mul(.35).add(vents.mul(.48)))).add(rear.mul(lip).mul(.12));
    material.colorNode = uniform(material.color).mul(shade);
    material.roughnessNode = float(material.roughness).add(rear.mul(vents).mul(.22));
  }
  return material;
}
