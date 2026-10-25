/* Every slate tile joins the heard-song choreography; the existing joints stay fixed. */
import { float, vec3, floor, fract, mod, abs, mix, smoothstep, step, sin, cos, select, max, fwidth } from 'three/tsl';

const TAU = Math.PI * 2, one = v => float(1).sub(v);

/** Whole-tile waves, alternating steps and crossing bands use the existing song and analyser. */
export function keepMusicFloor({ p, base, seam, reactive: u, music }) {
  // Match the authored 12 x 16 m seam texture: widths 2/4/4/2 m, staggered 8 m rows.
  const repeat = floor(p.x.div(12)), localX = fract(p.x.div(12)).mul(12);
  const within = step(2, localX).add(step(6, localX)).add(step(10, localX));
  const column = repeat.mul(4).add(within).toVar();
  const stagger = step(6, localX).mul(.5), row = floor(p.y.div(8).add(stagger)).toVar();
  const tileX = repeat.mul(12).add(select(within.lessThan(.5), float(1),
    select(within.lessThan(1.5), float(4), select(within.lessThan(2.5), float(8), float(11)))));
  const bank = mod(column.add(row.mul(2)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(one(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  note = note.toVar();

  const gentle = u.gentle, bar = music.tileBar;
  const motion = mix(music.tileBeat.mul(.5), bar.mul(.25), gentle);
  const beat = mix(cos(music.tileBeat.mul(TAU)).mul(.5).add(.5), float(.5), gentle);
  const swell = u.bass.mul(.22).mul(beat).add(note.mul(.3)).add(u.mid.mul(.12));

  // Broad fronts roll down every column, with measured notes changing their depth and colour.
  const wave = sin(row.mul(.72).sub(bar.mul(TAU / 2)).add(column.mul(.34)));
  const procession = smoothstep(-.85, .7, wave).mul(.66).add(.24).add(swell.mul(.35));

  // Whole rows and their alternating neighbours answer each other on the heard subdivisions.
  const checker = cos(column.add(row).mul(Math.PI).sub(motion.mul(Math.PI)));
  const steps = smoothstep(-.8, .8, checker).mul(.58).add(.26)
    .add(note.mul(.18)).add(u.bass.mul(beat).mul(.12));

  // Opposing diagonal bands braid through the tile field, each carried by a different band.
  const crossingA = sin(row.add(column).mul(Math.PI / 3).sub(bar.mul(TAU / 3)));
  const crossingB = cos(row.sub(column).mul(Math.PI / 3).add(bar.mul(TAU / 4)));
  const woven = smoothstep(-.75, .8, crossingA).mul(u.mid.mul(.18).add(.28))
    .add(smoothstep(-.75, .8, crossingB).mul(u.treble.mul(.16).add(.26)))
    .add(.24).add(note.mul(.16));

  // The whole field dissolves into a different arrangement over the last two of every eight bars.
  const phrase = bar.div(8), mode = mod(floor(phrase), 3);
  const choose = n => select(n.lessThan(.5), procession, select(n.lessThan(1.5), steps, woven));
  const pattern = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.75, 1, fract(phrase)));
  const tintPhase = sin(column.mul(.63).add(row.mul(.47)).sub(bar.mul(TAU / 8))).mul(.5).add(.5);
  const plum = vec3(.36, .045, .42), rose = vec3(.68, .10, .25), brass = vec3(.66, .32, .075);
  const tint = mix(mix(plum, rose, tintPhase), brass, note.mul(.42).add(u.treble.mul(.12)).clamp(0, .62));
  const distance = one(smoothstep(4, 12, max(fwidth(p.x), fwidth(p.y))));
  const centre = smoothstep(.7, 3, abs(tileX)).mul(.45).add(.55);
  const active = music.tileOn.mul(mix(u.energy.mul(4).clamp(0, 1), u.energy, gentle))
    .mul(mix(float(1), float(.32), gentle));
  const fill = pattern.mul(note.mul(.65).add(.6)).mul(.83).clamp(0, .86).mul(distance).mul(centre).mul(one(seam.mul(.96)));
  // Apply energy once, after the composition, so quiet and Gentle onset retain their smooth envelope.
  return mix(base, mix(base, base.mul(.32).add(tint), fill), active);
}
