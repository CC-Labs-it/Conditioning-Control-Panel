/* Alternating comic tile banks with cyan zigzag replies and plucked lime strings. */
import { float, vec3, floor, abs, max, mix, smoothstep, fwidth } from 'three/tsl';
import { vizActivity } from './musicVisualizer.js';
import { tileInstrument, tileGrout } from './tileMusicFloor.js';
export function goonworksMusicFloor({ p, base, top, edge, half, pink, accent, reactive: u, music }) {
  const column = floor(p.x.div(2.2)).add(6), row = floor(p.y.div(3.2));
  const layers = tileInstrument({ p, column, row, halfSpan: max(half.sub(2), 1), kind: 3, reactive: u, music });
  const hot = mix(pink, vec3(.98, .025, .3), .7), cyan = mix(accent, vec3(.035, .7, .9), .8);
  const light = hot.mul(layers.bass).add(cyan.mul(layers.mids))
    .add(vec3(.72, .96, .25).mul(layers.highs)).add(vec3(.64, .88, .48).mul(layers.strings));
  const ink = base.mul(.18).add(vec3(.018, .003, .027)).add(light).mul(tileGrout(p, 2.2, 3.2)).clamp(0, 1);
  const visibility = float(1).sub(smoothstep(4, 10, max(fwidth(p.x), fwidth(p.y))));
  const active = vizActivity(u, music).mul(mix(float(1), float(.28), u.gentle))
    .mul(smoothstep(.8, 2.4, edge)).mul(top).mul(smoothstep(.8, 2.2, abs(p.x)).mul(.6).add(.4)).mul(visibility);
  return mix(base, ink, active);
}
