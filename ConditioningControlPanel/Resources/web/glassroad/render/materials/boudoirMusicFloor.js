/* Rose tile chevrons open on bass; mint relays and pearl strings carry the melody. */
import { float, vec3, floor, mod, abs, mix, smoothstep, max, fwidth } from 'three/tsl';
import { vizActivity } from './musicVisualizer.js';
import { tileInstrument, tileGrout } from './tileMusicFloor.js';
export const BOUDOIR_MUSIC_FLOOR = Object.freeze({
  repeatM: 42, rosetteM: 26, shoulderM: [.9, 2.5], nearM: [3, 12], farM: [90, 150],
  coverage: .98, gentleCoverage: .36, wetCoverage: .42, luminance: .23,
});
export const BOUDOIR_MUSIC_COLOURS = Object.freeze({
  pink: Object.freeze([.9, .035, .25]), plum: Object.freeze([.12, .008, .16]),
  mint: Object.freeze([.08, .68, .42]), rose: Object.freeze([.98, .16, .36]),
});
export function boudoirMusicFloor({ p, base, trueEdge, distance, wet, reactive: u, music, lapM = 0 }) {
  const L = BOUDOIR_MUSIC_FLOOR, one = v => float(1).sub(v);
  // Keep the tile repeat and string nodes continuous at the closed lap seam.
  const fit = n => lapM > 0 ? lapM / Math.max(1, Math.round(lapM / n)) : n;
  const cellY = fit(2.8 * 48) / 48, column = floor(p.x.div(1.8)).add(6), row = mod(floor(p.y.div(cellY)), 48);
  const layers = tileInstrument({ p, column, row, halfSpan: float(9), kind: 2, reactive: u, music, stringPeriod: fit(26) });
  const C = Object.fromEntries(Object.entries(BOUDOIR_MUSIC_COLOURS).map(([k, c]) => [k, vec3(...c)]));
  const light = C.rose.mul(layers.bass).add(C.mint.mul(layers.mids)).add(vec3(.98, .72, .86).mul(layers.highs))
    .add(vec3(.96, .62, .8).mul(layers.strings));
  const rest = base.mul(.24).add(C.plum.mul(.12));
  const ink = rest.add(light.mul(music.big.mul(.2).add(1))).mul(tileGrout(p, 1.8, cellY)).clamp(0, 1);
  const near = smoothstep(...L.nearM, distance), far = one(smoothstep(...L.farM, distance));
  const detail = one(smoothstep(.7, 2.5, max(fwidth(p.x), fwidth(p.y))));
  const coverage = vizActivity(u, music).mul(mix(float(L.coverage), float(L.gentleCoverage), u.gentle))
    .mul(smoothstep(...L.shoulderM, trueEdge)).mul(smoothstep(.7, 2.6, abs(p.x)).mul(.6).add(.4))
    .mul(near.mul(.2).add(.8)).mul(far).mul(detail).mul(mix(float(1), float(L.wetCoverage), wet))
    .mul(one(music.calm.mul(.35)));
  return mix(base, ink, coverage);
}
