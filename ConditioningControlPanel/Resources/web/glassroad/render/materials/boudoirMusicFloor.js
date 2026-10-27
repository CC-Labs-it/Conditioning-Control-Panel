/* Think Pink: a silk spectrum, opening fans and embroidered sound ribbons on the heard clock. */
import { float, vec2, vec3, sin, cos, abs, fract, length, atan, mix, smoothstep, fwidth, max } from 'three/tsl';
import { floorSpectrum, floorPhrase, floorWave, floorLine } from './floorSpectrum.js';
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
  const gentle = music.tileGentle, beat = music.tileBeat, bar = music.tileBar;
  const travel = mix(beat.div(4), bar.div(8), gentle);
  const along = p.y.div(fit(L.repeatM)), across = abs(p.x);
  const band = floorSpectrum(across.div(13), u);
  const breath = mix(floorWave(beat), float(.5), gentle).mul(u.bass);
  // Large alternating silk panels answer each beat, with notes changing their width.
  // Equal-luminance hues keep the response colourful without whole-floor white flashes.
  const beatPaint = mix(floorWave(beat.sub(along.mul(.5)).add(across.div(18))), float(.5), gentle);
  const hook = music.big.mul(one().sub(music.calm.mul(.7)));
  // Mirrored ribbons open with the spectrum. Bass broadens them and the middle band bends them.
  const fold = sin(along.mul(TAU).sub(travel.mul(Math.PI))).mul(u.mid.mul(2.3).add(.7));
  const silkPhase = across.mul(.37).add(fold).sub(band.mul(2.6)).sub(travel.mul(.7));
  const silk = sin(silkPhase).mul(.5).add(.5);
  const ribbon = floorLine(sin(silkPhase), .13).mul(band.mul(.55).add(.3));
  // Large scalloped fans open from the centre, with frequency bands along their ribs.
  const q = vec2(p.x.div(14), fract(p.y.div(fit(L.rosetteM)).add(.5)).sub(.5));
  const radius = length(q), angle = atan(q.x, q.y);
  const radial = floorSpectrum(abs(sin(angle.mul(.5))), u);
  const petal = cos(angle.mul(7).add(travel.mul(.45))).mul(.075).add(.36)
    .add(radial.mul(.24)).add(breath.mul(.16));
  const fan = smoothstep(-.13, .13, petal.sub(radius));
  const lace = floorLine(radius.sub(petal), .012).add(floorLine(radius.sub(petal.mul(.65)), .009));
  // Bow-shaped ribbons cross and uncross to the bar; note energy opens their waist.
  const bow = sin(across.mul(.48).sub(sin(along.mul(TAU).sub(travel)).mul(band.mul(2).add(.6))));
  const weave = bow.mul(.5).add(.5);
  const shape = floorPhrase([silk, fan, weave], bar);
  const ink = floorPhrase([ribbon, lace.mul(.6), floorLine(bow, .09)], bar);
  const C = Object.fromEntries(Object.entries(BOUDOIR_MUSIC_COLOURS).map(([k, c]) => [k, vec3(...c)]));
  const rose = mix(C.pink, C.rose, band);
  const jewel = mix(C.plum, C.mint, hook.mul(.35).add(u.treble.mul(.28)));
  const beatColour = mix(C.pink, C.mint, beatPaint);
  let colour = mix(mix(rose, jewel, shape.mul(.86)), beatColour,
    one().sub(gentle).mul(.55).mul(band.mul(.35).add(.65)));
  colour = mix(colour, vec3(.76, .61, .69), ink.mul(.25).mul(u.treble.mul(.4).add(.6)));
  const near = smoothstep(...L.nearM, distance), far = one().sub(smoothstep(...L.farM, distance));
  const interior = smoothstep(...L.shoulderM, trueEdge);
  const centre = smoothstep(.7, 2.6, across);
  const detail = one().sub(smoothstep(.7, 2.5, max(fwidth(p.x), fwidth(p.y))));
  const active = music.tileOn.mul(mix(u.energy.mul(5).clamp(0, 1), u.energy, gentle));
  const coverage = active.mul(mix(float(L.coverage), float(L.gentleCoverage), gentle))
    .mul(interior).mul(centre.mul(.45).add(.55)).mul(near.mul(.2).add(.8)).mul(far).mul(detail)
    .mul(mix(float(1), float(L.wetCoverage), wet)).mul(one().sub(music.calm.mul(.35)));
  return mix(base, colour.add(base.mul(.12)), coverage);
}
