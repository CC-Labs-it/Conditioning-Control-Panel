/* Golden slab chords, jade staircases and four plucked strings on the Keep's real masonry. */
import { float, vec3, floor, fract, step, mix, smoothstep, max, fwidth } from 'three/tsl';
import { vizActivity } from './musicVisualizer.js';
import { tileInstrument } from './tileMusicFloor.js';
export function keepMusicFloor({ p, base, seam, reactive: u, music }) {
  const repeat = floor(p.x.div(12)), localX = fract(p.x.div(12)).mul(12);
  const column = repeat.mul(4).add(step(2, localX)).add(step(6, localX)).add(step(10, localX)).toVar();
  const row = floor(p.y.div(8).add(step(6, localX).mul(.5))).toVar();
  const layers = tileInstrument({ p, column, row, halfSpan: float(9), reactive: u, music });
  const light = vec3(.92, .49, .055).mul(layers.bass)
    .add(vec3(.04, .68, .4).mul(layers.mids)).add(vec3(.75, .9, .63).mul(layers.highs))
    .add(vec3(.72, .83, .54).mul(layers.strings));
  const ink = base.mul(mix(float(.24), float(.8), u.gentle)).add(light).clamp(0, .96);
  const visibility = float(1).sub(smoothstep(4, 12, max(fwidth(p.x), fwidth(p.y))));
  return mix(base, ink, vizActivity(u, music).mul(visibility).mul(float(1).sub(seam.mul(.98))));
}
