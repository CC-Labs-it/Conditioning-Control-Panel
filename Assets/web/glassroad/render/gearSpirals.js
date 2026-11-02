import { positionGeometry, normalGeometry, texture, vec2, vec3, float, mix, abs, smoothstep } from 'three/tsl';
import { acquireLoomAtlas, loomAtlasUv } from './loomAtlas.js';

// Paint the actual cut-sector cap. Its physical wheel carries the ink around.
export function createGearSpiralPaint(material, radius, index = 0) {
  if (typeof document === 'undefined' && typeof OffscreenCanvas === 'undefined') return { dispose() {} };
  const atlas = acquireLoomAtlas();
  const at = positionGeometry.xy.div(Math.max(.01, radius) * 2).add(vec2(.5, .5));
  const paint = texture(atlas.texture, loomAtlasUv(at, float(((index % 6) + 6) % 6))).rgb;
  const face = smoothstep(.7, .95, abs(normalGeometry.z));
  const base = material.colorNode || vec3(material.color.r, material.color.g, material.color.b);
  const ink = paint.dot(vec3(.2126, .7152, .0722));
  material.colorNode = mix(base, base.mul(ink.mul(1.3).add(.08)), face.mul(.92));
  return { dispose: atlas.dispose };
}
