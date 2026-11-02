// Read the existing eight measured bands as a continuous strip. No new analyser or texture.
import { float, abs, max, cos, mix, mod, floor, fract, smoothstep, select, fwidth } from 'three/tsl';
export function floorSpectrum(at, u) {
  const x = at.clamp(0, 1).mul(7);
  let value = float(0);
  for (let i = 0; i < 8; i++) value = value.add(max(float(1).sub(abs(x.sub(i))), 0).mul(u['note' + i]));
  return value;
}
// Phrase changes let the old shape leave over a whole bar.
export function floorPhrase(shapes, bar) {
  const phrase = bar.div(8), mode = mod(floor(phrase), 3);
  const choose = n => select(n.lessThan(.5), shapes[0], select(n.lessThan(1.5), shapes[1], shapes[2]));
  return mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.875, 1, fract(phrase)));
}
export const floorWave = phase => cos(phase.mul(Math.PI * 2)).mul(.5).add(.5);
export function floorLine(distance, width) {
  const aa = max(fwidth(distance).mul(1.4), .012), w = float(width);
  return float(1).sub(smoothstep(w, w.add(aa), abs(distance))).mul(w.div(max(w, aa)));
}
