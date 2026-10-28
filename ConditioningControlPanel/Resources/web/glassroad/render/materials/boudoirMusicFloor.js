/* Dark satin leaves room for bass fans, melodic silk, snare pearl cuts and high lace glints. */
import { float, vec2, vec3, sin, cos, abs, fract, floor, mod, length, atan, mix, smoothstep, step, fwidth, max } from 'three/tsl';
import { floorSpectrum, floorPhrase } from './floorSpectrum.js';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity } from './musicVisualizer.js';
export const BOUDOIR_MUSIC_FLOOR = Object.freeze({
  repeatM: 42, rosetteM: 26, shoulderM: [.9, 2.5], nearM: [3, 12], farM: [90, 150],
  coverage: .98, gentleCoverage: .36, wetCoverage: .42, luminance: .23,
});
export const BOUDOIR_MUSIC_COLOURS = Object.freeze({
  pink: Object.freeze([.9, .035, .25]), plum: Object.freeze([.12, .008, .16]),
  mint: Object.freeze([.08, .68, .42]), rose: Object.freeze([.98, .16, .36]),
});
const one = v => float(1).sub(v);
export function boudoirMusicFloor({ p, base, trueEdge, distance, wet, reactive: u, music, lapM = 0 }) {
  const L = BOUDOIR_MUSIC_FLOOR, fast = one(u.gentle);
  const fit = n => lapM > 0 ? lapM / Math.max(1, Math.round(lapM / n)) : n;
  const across = abs(p.x), along = p.y.div(fit(L.repeatM)), at = across.div(13).clamp(0, 1);
  const band = floorSpectrum(at, u).mul(.4).add(vizSpectrum(at, u).mul(.6).mul(fast)).toVar();
  const pcm = vizWave(fract(along), u).mul(fast).mul(3.7);
  // A narrow silk ribbon changes its actual outline with voice/PCM rather than repainting every tile.
  const silkAt = across.sub(pcm.add(band.mul(1.8)).add(3.2));
  const silk = one(smoothstep(.5, 1.05, abs(silkAt))).mul(u.mid.mul(.75).add(.25));
  const q = vec2(p.x.div(14), fract(p.y.div(fit(L.rosetteM)).add(.5)).sub(.5));
  const radius = length(q), angle = atan(q.x, q.y);
  const petals = cos(angle.mul(7)).mul(.09).add(.32).add(band.mul(.3));
  const fan = vizLine(radius.sub(petals), .045).mul(u.mid.mul(.7).add(.2));
  const history = vizHistory(at, fract(along), u).mul(fast);
  const embroidery = vizLine(sin(across.mul(.65).add(pcm)), .16).mul(history);
  const melody = floorPhrase([silk, fan, embroidery], music.tileBar.add(u.section.mul(8)));
  // Bass opens a bright fan ring with its own finite front and decay.
  const bass = vizLine(radius.sub(u.kickAge.mul(1.15).add(.1)), .055)
    .mul(one(smoothstep(.28, .9, u.kickAge))).mul(u.kickStrength).mul(fast);
  // Snares send pearl cuts across alternating mirror diagonals. They do not widen the bass fan.
  const direction = mod(u.snareCount, 2).mul(2).sub(1);
  const slash = fract(p.y.div(20).add(p.x.mul(direction).div(24)).sub(u.snareAge.mul(.9)));
  const snare = vizLine(slash.sub(.5), .033).mul(u.snare).mul(fast);
  // Tiny lace intersections sparkle only on high attacks; most satin remains unlit.
  const cell = floor(p.div(2.1)), rare = step(.72, fract(cell.x.mul(.43).add(cell.y.mul(.63)).add(u.hatsCount.mul(.19))));
  const glint = vizLine(fract(p.x.div(2.1)).sub(.5), .07)
    .mul(vizLine(fract(p.y.div(2.1)).sub(.5), .07)).mul(rare).mul(u.hats).mul(fast);
  const C = Object.fromEntries(Object.entries(BOUDOIR_MUSIC_COLOURS).map(([k, c]) => [k, vec3(...c)]));
  const rest = base.mul(.24).add(C.plum.mul(.18));
  const light = mix(C.pink, C.rose, band).mul(melody)
    .add(C.rose.mul(bass).mul(1.2)).add(C.mint.mul(snare)).add(vec3(.98, .72, .86).mul(glint));
  const near = smoothstep(...L.nearM, distance), far = one(smoothstep(...L.farM, distance));
  const detail = one(smoothstep(.7, 2.5, max(fwidth(p.x), fwidth(p.y))));
  const coverage = vizActivity(u, music).mul(mix(float(L.coverage), float(L.gentleCoverage), u.gentle))
    .mul(smoothstep(...L.shoulderM, trueEdge)).mul(smoothstep(.7, 2.6, across).mul(.6).add(.4))
    .mul(near.mul(.2).add(.8)).mul(far).mul(detail).mul(mix(float(1), float(L.wetCoverage), wet))
    .mul(one(music.calm.mul(.35)));
  return mix(base, rest.add(light.mul(music.big.mul(.2).add(1))).clamp(0, 1), coverage);
}
