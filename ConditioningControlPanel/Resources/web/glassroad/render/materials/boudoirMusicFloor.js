/* Think Pink: a silk spectrum, opening fans and embroidered sound ribbons on the heard clock. */
import { float, vec2, vec3, sin, cos, abs, fract, length, atan, mix, smoothstep, fwidth, max } from 'three/tsl';
import { floorSpectrum, floorPhrase } from './floorSpectrum.js';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity, vizHit } from './musicVisualizer.js';
export const BOUDOIR_MUSIC_FLOOR = Object.freeze({
  repeatM: 42, rosetteM: 26, shoulderM: [.9, 2.5], nearM: [3, 12], farM: [90, 150],
  coverage: .98, gentleCoverage: .36, wetCoverage: .42, luminance: .23,
});
const level = c => Object.freeze(c.map(v => v * BOUDOIR_MUSIC_FLOOR.luminance /
  (c[0] * .2126 + c[1] * .7152 + c[2] * .0722)));
export const BOUDOIR_MUSIC_COLOURS = Object.freeze({
  pink: level([.9, .045, .32]), plum: level([.49, .10, .74]),
  mint: level([.055, .32, .27]), rose: level([.76, .12, .24]),
});
const TAU = Math.PI * 2, one = () => float(1);
export function boudoirMusicFloor({ p, base, trueEdge, distance, wet, reactive: u, music, lapM = 0 }) {
  const L = BOUDOIR_MUSIC_FLOOR;
  const fit = desired => lapM > 0 ? lapM / Math.max(1, Math.round(lapM / desired)) : desired;
  const gentle = music.tileGentle, fast = one().sub(gentle), bar = music.tileBar;
  const along = p.y.div(fit(L.repeatM)), across = abs(p.x), at = across.div(13).clamp(0, 1);
  const band = floorSpectrum(at, u).mul(.35).add(vizSpectrum(at, u).mul(.65).mul(fast)).toVar();
  const wave = vizWave(fract(along), u).mul(fast).toVar();
  const history = vizHistory(at, fract(along), u).mul(fast).toVar();
  const hit = vizHit(u).mul(fast).toVar();
  const hook = music.big.mul(one().sub(music.calm.mul(.7)));
  // Actual PCM folds the satin; kicks open its waist and snares throw embroidered echoes.
  const fold = wave.mul(3.2).add(u.mid.mul(1.5)).add(band.mul(2.2));
  const silkAt = across.sub(float(3.5).add(fold));
  const silkWidth = u.bass.mul(1.2).add(u.kick.mul(fast).mul(1.8)).add(.8);
  const silk = one().sub(smoothstep(silkWidth, silkWidth.add(.65), abs(silkAt)));
  const ribbon = vizLine(abs(silkAt).sub(silkWidth), .16)
    .add(vizLine(silkAt.sub(wave.mul(1.4)), .12).mul(.6));
  // Spectrum opens a fixed lace fan. Its ribs stay planted while the heard notes change its outline.
  const q = vec2(p.x.div(14), fract(p.y.div(fit(L.rosetteM)).add(.5)).sub(.5));
  const radius = length(q), angle = atan(q.x, q.y);
  const radial = vizSpectrum(abs(sin(angle.mul(.5))), u).mul(fast).add(band.mul(.25));
  const petal = cos(angle.mul(7)).mul(.075).add(.25)
    .add(radial.mul(.4)).add(hit.mul(.22)).add(u.bass.mul(.12));
  const fan = smoothstep(-.13, .13, petal.sub(radius));
  const lace = vizLine(radius.sub(petal), .018).add(vizLine(radius.sub(petal.mul(.65)), .014))
    .mul(u.hats.mul(fast).mul(.45).add(.55));
  // Sound history becomes watered silk; each fresh onset sends a pearl ring across the rosette.
  const interference = sin(across.mul(.7).add(wave.mul(2.6)))
    .mul(cos(along.mul(TAU).sub(history.mul(5)))).mul(.5).add(.5);
  const ring = vizLine(radius.sub(u.hitAge.mul(.95)), .045)
    .mul(one().sub(smoothstep(.05, 1.15, u.hitAge))).mul(u.hitStrength).mul(fast);
  const phrase = bar.add(u.section.mul(8));
  const shape = floorPhrase([silk, fan, interference], phrase).toVar();
  const ink = floorPhrase([ribbon, lace.mul(.6), history.mul(.7)], phrase).add(ring).clamp(0, 1);
  const C = Object.fromEntries(Object.entries(BOUDOIR_MUSIC_COLOURS).map(([k, c]) => [k, vec3(...c)]));
  const rose = mix(C.pink, C.rose, band);
  const jewel = mix(C.plum, C.mint, hook.mul(.35).add(u.treble.mul(.28)));
  const pulseColour = mix(C.pink, C.mint, u.snare.mul(fast).mul(.65).add(band.mul(.35)));
  let colour = mix(mix(rose, jewel, shape.mul(.86)), pulseColour, hit.mul(.45));
  colour = mix(colour, C.mint, ink.mul(.7).mul(u.treble.mul(.4).add(.6)));
  // A subtle beat-grid weave survives between hits; real sound owns the large changes.
  colour = mix(colour, C.rose, cos(music.tileBeat.mul(TAU).sub(along)).mul(.5).add(.5).mul(fast).mul(.16));
  const near = smoothstep(...L.nearM, distance), far = one().sub(smoothstep(...L.farM, distance));
  const interior = smoothstep(...L.shoulderM, trueEdge);
  const centre = smoothstep(.7, 2.6, across);
  const detail = one().sub(smoothstep(.7, 2.5, max(fwidth(p.x), fwidth(p.y))));
  const active = vizActivity(u, music);
  const coverage = active.mul(mix(float(L.coverage), float(L.gentleCoverage), gentle))
    .mul(interior).mul(centre.mul(.45).add(.55)).mul(near.mul(.2).add(.8)).mul(far).mul(detail)
    .mul(mix(float(1), float(L.wetCoverage), wet)).mul(one().sub(music.calm.mul(.35)));
  return mix(base, colour.add(base.mul(.12)), coverage);
}
