// Classic scope, spectral trail and transient building blocks, sampled from one bounded music atlas.
import { float, vec2, texture, abs, max, smoothstep, fwidth, mix } from 'three/tsl';
const x = at => float(at).clamp(0, 1).mul(63 / 64).add(.5 / 64);
export function vizWave(at, u) { return texture(u.vizTexture, vec2(x(at), .5 / 18)).r.mul(2).sub(1); }
export function vizSpectrum(at, u) { return texture(u.vizTexture, vec2(x(at), .5 / 18)).g; }
export function vizHistory(at, age, u) { return texture(u.vizTexture, vec2(x(at), float(age).clamp(0, 1).mul(16 / 18).add(1.5 / 18))).r; }
export function vizLine(distance, width) {
  const aa = max(fwidth(distance).mul(1.2), .018), w = float(width);
  return float(1).sub(smoothstep(w, w.add(aa), abs(distance))).mul(w.div(max(w, aa)));
}
export function vizActivity(u, music = null) {
  const level = max(u.drive, u.energy), normal = level.div(level.add(.08));
  return mix(normal, u.energy, u.gentle).mul(music ? music.tileOn : 1).mul(u.motion);
}
export function vizHit(u) { return max(max(u.kick, u.snare.mul(.8)), u.hats.mul(.5)).mul(float(1).sub(u.gentle)); }
