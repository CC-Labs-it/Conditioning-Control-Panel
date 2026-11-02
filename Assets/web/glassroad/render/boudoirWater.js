/* The bath shares the water engine, with a porcelain-and-mint palette of its own. */
import { Color } from 'three/webgpu';
import { vec3 } from 'three/tsl';

export function createBoudoirWaterPalette(palette) {
  const colours = ['#d7f0e4', '#65b5b0', '#185c66'].map(hex => {
    const colour = new Color(hex); return vec3(colour.r, colour.g, colour.b);
  });
  return { ...palette, col: index => colours[index] || colours[0] };
}
