/* Circuit tile staircases, cyan replies and vibrating phosphor strings. */
import { float, vec3, floor, abs, mix, smoothstep, max, fwidth } from 'three/tsl';
import { vizActivity } from './musicVisualizer.js';
import { tileInstrument, tileGrout } from './tileMusicFloor.js';
export function mainframeTileLight(p, u, music, trueEdge, base) {
  const column = floor(p.x.div(1.8)).add(7), row = floor(p.y.div(2.4));
  const layers = tileInstrument({ p, column, row, columns: 14, halfSpan: float(9), kind: 1, reactive: u, music });
  const light = vec3(.27, .9, .055).mul(layers.bass).add(vec3(.025, .58, .92).mul(layers.mids))
    .add(vec3(.72, .96, .46).mul(layers.highs)).add(vec3(.48, .9, .68).mul(layers.strings));
  const ink = base.mul(.25).add(light).mul(tileGrout(p, 1.8, 2.4)).clamp(0, 1);
  const edge = smoothstep(.8, 2.4, trueEdge), centre = smoothstep(.75, 2, abs(p.x)).mul(.6).add(.4);
  const detail = float(1).sub(smoothstep(.6, 2, max(fwidth(p.x), fwidth(p.y))));
  return mix(base, ink, vizActivity(u, music).mul(mix(float(1), float(.32), u.gentle)).mul(edge).mul(centre).mul(detail));
}
