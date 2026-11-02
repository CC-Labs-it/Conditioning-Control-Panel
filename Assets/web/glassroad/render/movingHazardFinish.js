import { MeshStandardNodeMaterial } from 'three/webgpu';
import { uniform, positionGeometry, sin, abs, fract, smoothstep, fwidth, float } from 'three/tsl';

const THEMES = {
  threshold: ['#ff91cc', '#fff5fc', '#e60087', '#bfc2d3', '#38213b'],
  mainframe: ['#286a50', '#dcffed', '#5fff9b', '#b6d8cf', '#152d29'],
  keep: ['#521744', '#e8d6dc', '#ed3e9d', '#c7a06c', '#291a2a'],
  boudoir: ['#dd7399', '#fff0e2', '#cf2479', '#d3ad8b', '#482337'],
  goonworks: ['#d72283', '#fff7f1', '#f960ba', '#cbc0cc', '#321529'],
};

export function movingHazardColours(id, pal) {
  const [paint, pale, accent, metal, dark] = THEMES[id] || [pal.primaryHex(), '#fff5fc', pal.accentHex(), '#c8d1d6', '#30343b'];
  return { paint, pale, accent, metal, dark };
}

/** Source-local machining stays attached to the moving instanced parts. */
export function movingHazardFinish(hex, finish = 'enamel') {
  const metal = finish === 'metal', rubber = finish === 'rubber';
  const m = new MeshStandardNodeMaterial({ color: hex, roughness: metal ? .28 : rubber ? .85 : .34,
    metalness: metal ? .72 : rubber ? .05 : .18, emissive: hex, emissiveIntensity: rubber ? .04 : .15 });
  m.name = 'moving-trap-' + finish;
  const p = positionGeometry, d = abs(fract(p.x.mul(metal ? 9 : 3)).sub(.5));
  const seam = float(1).sub(smoothstep(.012, fwidth(d).add(.035), d));
  const tooth = sin(p.y.mul(metal ? 180 : 75).add(p.z.mul(19)))
    .mul(float(1).sub(smoothstep(.2, 1, fwidth(p.y.mul(metal ? 180 : 75)))));
  const shade = float(.98).add(tooth.mul(metal ? .045 : .016)).sub(seam.mul(metal ? .08 : .035));
  m.colorNode = uniform(m.color).mul(shade);
  m.roughnessNode = float(m.roughness).add(tooth.mul(.025)).add(seam.mul(.06));
  return m;
}
