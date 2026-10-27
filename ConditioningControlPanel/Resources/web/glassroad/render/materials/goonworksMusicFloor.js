/* Broad soundstage graphics printed into the enamel, driven by the heard music. */
import { float, vec2, vec3, fract, abs, max, mix, smoothstep, sin, fwidth, atan } from 'three/tsl';
import { floorSpectrum, floorPhrase, floorWave, floorLine } from './floorSpectrum.js';

export function goonworksMusicFloor({ p, base, top, edge, half, pink, accent, reactive: u, music }) {
  const gentle = u.gentle, beat = music.tileBeat, bar = music.tileBar;
  const clock = mix(beat.div(4), bar.div(8), gentle);
  const active = mix(u.energy.mul(5).clamp(0, 1), u.energy, gentle)
    .mul(music.tileOn).mul(mix(float(1), float(.28), gentle));
  const playable = smoothstep(.8, 2.4, edge).mul(top);
  // Keep the centre lettering clear without erasing the whole driving view.
  const centreRoom = smoothstep(.8, 2.2, abs(p.x)).mul(.3).add(.7);
  // These are metre-wide graphics. Only the far horizon loses them, not normal chase distance.
  const visibility = float(1).sub(smoothstep(4, 10, max(fwidth(p.x), fwidth(p.y))));
  const span = max(half.sub(2), 1), normalized = abs(p.x).div(span).clamp(0, 1);
  const band = floorSpectrum(normalized, u);
  const breath = mix(floorWave(beat), float(.5), gentle).mul(u.bass);

  // Eight mirrored columns grow in the road direction, like a giant equalizer under the kart.
  const column = fract(normalized.mul(8)), row = fract(p.y.div(24));
  const gutter = smoothstep(.04, .12, column).mul(float(1).sub(smoothstep(.88, .96, column)));
  const level = band.div(band.add(.2)).mul(.64).add(breath.mul(.16)).add(u.bass.mul(.14)).add(.08);
  const barFill = float(1).sub(smoothstep(level, level.add(.06), row));
  const cap = float(1).sub(smoothstep(.025, .07, abs(row.sub(level))));
  const equalizer = barFill.mul(.98).add(cap.mul(.5)).mul(gutter);
  const barBed = gutter.mul(float(1).sub(smoothstep(.92, .98, row)));

  // Paired vinyl pinwheels spread across the lane; each petal follows a measured frequency band.
  const q = vec2(abs(p.x).sub(span.mul(.48)).div(span.mul(.5)),
    fract(p.y.div(30).add(.5)).sub(.5).mul(2));
  const radius = q.length(), angle = atan(q.x, q.y);
  const spokeBand = floorSpectrum(abs(sin(angle.mul(.5))), u);
  const reach = spokeBand.mul(.36).add(.48).add(breath.mul(.12));
  const disc = float(1).sub(smoothstep(reach, reach.add(.1), radius));
  const petal = sin(angle.mul(8).add(clock.mul(2)).add(radius.mul(5))).mul(.5).add(.5);
  const pinwheel = disc.mul(petal.mul(.64).add(.26))
    .add(floorLine(radius.sub(reach), .035).mul(.45));
  const record = float(1).sub(smoothstep(.93, 1.03, radius));

  // Wide stereo ribbons weave through the central half of the road, with bass-driven wake rings.
  const wave = sin(p.y.mul(.19).sub(clock.mul(1.4)))
    .mul(u.mid.mul(2.6).add(1.2)).add(band.mul(2));
  const riverAt = span.mul(.32).add(wave), distance = abs(abs(p.x).sub(riverAt));
  const width = u.bass.mul(2.3).add(2.2);
  const river = float(1).sub(smoothstep(width, width.add(.65), distance));
  const wake = floorLine(abs(p.x).add(p.y.mul(.32)).sub(clock.mul(3)).mod(12).sub(6), .65);
  const stereo = river.mul(.8).add(wake.mul(.38).mul(u.treble.mul(.7).add(.3)));
  const riverBed = float(1).sub(smoothstep(width.add(.7), width.add(1.5), distance));

  const shape = floorPhrase([equalizer, pinwheel, stereo], bar).clamp(0, 1);
  const bed = floorPhrase([barBed, record, riverBed], bar).clamp(0, 1);
  const hue = floorWave(normalized.mul(.6).add(bar.div(16))).mul(.65).add(band.mul(.25));
  const cyan = mix(accent, vec3(.025, .72, .78), .7);
  // A broad travelling colour answer carries the beat even between the equalizer bars.
  const answer = mix(floorWave(beat.sub(p.y.div(48)).add(normalized.mul(.5))), float(.5), gentle);
  const hot = mix(pink, vec3(.94, .025, .36), .65);
  const tint = mix(hot, cyan, mix(hue, answer, float(1).sub(gentle).mul(.72)))
    .mul(.96).add(vec3(.015, .01, .015));
  const ink = mix(vec3(.055, .012, .075), accent.mul(.18), .35);
  const colourBed = mix(base, tint, float(1).sub(gentle).mul(.28));
  const paint = mix(mix(colourBed, ink, bed.mul(.55)), tint, shape.mul(.98));
  // Apply energy once, after composition, so Gentle keeps the full slow envelope rise.
  return mix(base, paint, active.mul(playable).mul(centreRoom).mul(visibility));
}
