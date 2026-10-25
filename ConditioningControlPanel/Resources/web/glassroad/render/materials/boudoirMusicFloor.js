/* Think Pink: satin folds, pearl lace and liquid harlequin. One existing road draw. */
import { float, vec2, vec3, sin, cos, abs, fract, length, atan, mix, smoothstep, fwidth, max, min } from 'three/tsl';

export const BOUDOIR_MUSIC_FLOOR = Object.freeze({
  repeatM: 36, rosetteM: 18, shoulderM: [.9, 2.5], nearM: [3, 12], farM: [90, 150],
  coverage: .83, gentleCoverage: .37, wetCoverage: .45, luminance: .23,
});
// Equal luminance colour families let the broad floor change hue without a full-road flash.
const level = c => Object.freeze(c.map(v => v * BOUDOIR_MUSIC_FLOOR.luminance /
  (c[0] * .2126 + c[1] * .7152 + c[2] * .0722)));
export const BOUDOIR_MUSIC_COLOURS = Object.freeze({
  pink: level([.9, .045, .32]), plum: level([.49, .10, .74]),
  mint: level([.055, .32, .27]), rose: level([.76, .12, .24]),
});
const TAU = Math.PI * 2, one = () => float(1);

/** Final paint on dry and wet road, including fork decks sharing this same material.
 * Clocks are heard beats/bars only. Measured bands enter through the shared slow envelope.
 * No song returns the authored finish. Nothing here changes support, opacity or navigation art. */
export function boudoirMusicFloor({ p, base, trueEdge, distance, wet, reactive: u, music, lapM = 0 }) {
  const L = BOUDOIR_MUSIC_FLOOR;
  const fit = desired => lapM > 0 ? lapM / Math.max(1, Math.round(lapM / desired)) : desired;
  const gentle = music.tileGentle;
  const phase = music.tileBar.mul(TAU / 8);
  const along = p.y.mul(TAU / fit(L.repeatM));
  const beat = cos(music.tileBeat.mul(TAU)).mul(.5).add(.5);
  // Gentle retains the slow folds but removes the beat expansion, with no clock rescaling jump.
  const breath = mix(beat, float(.5), gentle).mul(u.bass);
  const hook = music.big.mul(one().sub(music.calm.mul(.7)));
  const warp = sin(along.add(phase)).mul(u.mid.mul(.32).add(.6));
  const satin = sin(p.x.mul(.48).add(warp).sub(phase.mul(.7))).mul(.5).add(.5);
  const cross = sin(p.x.mul(.52).add(along).add(phase.mul(.5)))
    .mul(sin(p.x.mul(.52).sub(along).sub(phase.mul(.5))));
  const diamond = smoothstep(-.3, .3, cross);
  const phrase = sin(music.tileBar.mul(TAU / 16)).mul(.5).add(.5);
  const shape = mix(satin, diamond, phrase.mul(.55).add(hook.mul(.25)));
  const C = Object.fromEntries(Object.entries(BOUDOIR_MUSIC_COLOURS).map(([k, c]) => [k, vec3(...c)]));
  const pink = mix(C.pink, C.rose, satin.mul(.35));
  const iridescent = mix(C.plum, C.mint, smoothstep(.5, 1, satin).mul(hook.mul(.5).add(.25)));
  let colour = mix(pink, iridescent, shape.mul(.68));

  // Six-petal lace medallions breathe in place. No tiling seam crosses a visible petal.
  const q = fract(p.div(vec2(10, fit(L.rosetteM))).add(.5)).sub(.5);
  const radius = length(q), angle = atan(q.x, q.y);
  const petals = cos(angle.mul(6).add(phase)).mul(.035).add(.205).add(breath.mul(.025));
  const laceDistance = min(abs(radius.sub(petals)), abs(radius.sub(petals.mul(.68))));
  const pixel = max(fwidth(q.x), fwidth(q.y));
  const width = max(float(.012), pixel.mul(1.5));
  const detail = one().sub(smoothstep(.05, .2, pixel));
  const lace = one().sub(smoothstep(width.mul(.25), width, laceDistance))
    .mul(float(.012).div(width)).mul(detail);
  const pearl = vec3(.76, .61, .69);
  colour = mix(colour, pearl, lace.mul(u.treble.mul(.12).add(.12)).mul(hook.mul(.4).add(.6)));

  // Broad satin crossings follow bass, while little pearl stitches follow the high band.
  const ribbonD = abs(sin(p.x.mul(.28).add(sin(along.sub(phase)).mul(.95)).sub(breath.mul(.13))));
  const ribbon = one().sub(smoothstep(.10, .24, ribbonD));
  colour = mix(colour, C.pink, ribbon.mul(u.bass.mul(.2).add(.24)));
  const near = smoothstep(...L.nearM, distance), far = one().sub(smoothstep(...L.farM, distance));
  const interior = smoothstep(...L.shoulderM, trueEdge);
  // The center retains its finish and words; open edges keep their steady dark shoulder.
  const centre = smoothstep(.35, 1.1, abs(p.x));
  const active = music.tileOn.mul(u.energy.mul(5).clamp(0, 1));
  const coverage = active.mul(mix(float(L.coverage), float(L.gentleCoverage), gentle))
    .mul(interior).mul(centre.mul(.55).add(.45)).mul(near.mul(.4).add(.6)).mul(far)
    .mul(mix(float(1), float(L.wetCoverage), wet)).mul(one().sub(music.calm.mul(.35)));
  return mix(base, colour.add(base.mul(.12)), coverage);
}
