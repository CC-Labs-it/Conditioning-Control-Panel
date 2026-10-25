/* The Keep's final stone finish owns its music colour, above the opaque slate override. */
import { float, vec3, floor, fract, mod, abs, mix, smoothstep, step, cos, select } from 'three/tsl';

/** Fixed masonry groups become a broad equalizer. All motion comes from the heard song clock. */
export function keepMusicFloor({ p, base, seam, reactive: u, music }) {
  const row = floor(p.y.div(6)), brick = floor(p.x.div(4).add(mod(row, 2).mul(.5)));
  const column = mod(brick.add(4), 8), bank = mod(row.add(brick), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) {
    const selected = float(1).sub(step(.5, abs(column.sub(i))));
    note = note.add(selected.mul(u['note' + i]));
  }
  const gentleGain = mix(float(1), float(.32), u.gentle);
  const clock = music.tileBeat.mul(mix(float(1), float(.25), u.gentle));
  const wave = phase => cos(phase.mul(Math.PI * 2)).mul(.5).add(.5);
  // Eight-bar phrases crossfade between marching rows, opposing chevrons and a diamond lattice.
  // Smooth waves keep the masonry readable instead of blinking the whole road on each kick.
  const phraseClock = music.tileBar.div(8), mode = mod(floor(phraseClock), 3);
  const processions = [wave(clock.div(4).sub(row.div(6))),
    wave(clock.div(4).sub(abs(brick).add(row).div(8))),
    wave(clock.div(4).sub(abs(brick).add(abs(mod(row, 8).sub(4))).div(6)))];
  const choose = slot => select(slot.lessThan(.5), processions[0], select(slot.lessThan(1.5), processions[1], processions[2]));
  const procession = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.8, 1, fract(phraseClock)));
  const height = note.mul(1.5).add(u.mid.mul(.35)).add(u.bass.mul(.25)).clamp(0, 1);
  const stepHeight = mod(row, 6).div(10).add(.025);
  const equalizer = smoothstep(stepHeight, stepHeight.add(.18), height);
  const beatFill = procession.mul(u.bass.mul(.9).add(u.mid.mul(.5)));
  const measured = equalizer.mul(.4).add(beatFill.mul(1.1)).add(u.treble.mul(.12));
  // Group colours turn slowly through plum, rose and brass. Mortar remains dark and readable.
  const phrase = cos(music.tileBar.div(8).add(bank.div(8)).mul(Math.PI * 2)).mul(.5).add(.5);
  const plum = vec3(.4, .09, .58), rose = vec3(.86, .09, .39), brass = vec3(.9, .49, .18);
  const tint = mix(mix(plum, rose, phrase), brass, smoothstep(.72, 1, phrase).mul(.42));
  const tileX = fract(p.x.div(4).add(mod(row, 2).mul(.5))), tileY = fract(p.y.div(6));
  const inset = smoothstep(0, .055, tileX).mul(float(1).sub(smoothstep(.945, 1, tileX)))
    .mul(smoothstep(0, .035, tileY)).mul(float(1).sub(smoothstep(.965, 1, tileY)));
  const active = u.energy.mul(4).clamp(0, 1).mul(gentleGain);
  const fill = measured.mul(active).mul(1.5).mul(inset).mul(float(1).sub(seam.mul(.94))).clamp(0, .86);
  return mix(base, base.mul(.6).add(tint), fill);
}
