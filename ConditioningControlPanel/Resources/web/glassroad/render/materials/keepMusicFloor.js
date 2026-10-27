/* The slate becomes a stained-glass sound mosaic. Every physical slab receives one complete fill. */
import { float, vec3, floor, fract, mod, abs, mix, smoothstep, step, cos, select, max, fwidth } from 'three/tsl';
import { vizSpectrum, vizHistory, vizActivity, vizHit } from './musicVisualizer.js';

const TAU = Math.PI * 2, one = v => float(1).sub(v);

export function keepMusicFloor({ p, base, seam, reactive: u, music }) {
  // Match the fixed 12 x 16 m slate joints: 2/4/4/2 m slabs, staggered 8 m rows.
  const repeat = floor(p.x.div(12)), localX = fract(p.x.div(12)).mul(12);
  const within = step(2, localX).add(step(6, localX)).add(step(10, localX));
  const column = repeat.mul(4).add(within).toVar();
  const row = floor(p.y.div(8).add(step(6, localX).mul(.5))).toVar();
  const tileX = repeat.mul(12).add(select(within.lessThan(.5), float(1),
    select(within.lessThan(1.5), float(4), select(within.lessThan(2.5), float(8), float(11)))));
  const bank = mod(column.add(row.mul(2)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(one(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  note = note.toVar();
  const gentle = u.gentle, fast = one(gentle), bar = music.tileBar;
  const frequency = bank.add(.5).div(8), spectrum = vizSpectrum(frequency, u);
  const history = vizHistory(frequency, mod(row, 12).div(12), u);
  const hit = vizHit(u).mul(fast);
  const beat = cos(music.tileBeat.mul(TAU).sub(column.mul(.7)).add(row.mul(.4))).mul(.5).add(.5).mul(fast);

  // Eight musical banks light complete coloured panes; a changed spectrum rearranges the mosaic.
  const mosaic = note.mul(.38).add(spectrum.mul(.42)).add(history.mul(.2));
  const choir = mix(u.mid, u.treble, mod(column.add(row), 2)).mul(.25);
  const glass = mosaic.add(choir).add(.24);

  // Onsets send a finite procession along tile rows. It expands only when the music strikes.
  const rowDistance = abs(mod(row.add(6), 12).sub(6));
  const front = one(smoothstep(.7, 2, abs(rowDistance.sub(u.hitAge.mul(14)))))
    .mul(u.hitStrength).mul(fast);
  const procession = front.mul(.65).add(note.mul(.35)).add(spectrum.mul(.4)).add(history.mul(.18)).add(.24);

  // A chessboard answer, with alternating whole slabs changing hue on heard beats and drum hits.
  const checker = mod(column.add(row), 2);
  const answer = mix(beat, one(beat), checker).mul(u.bass.mul(.7).add(.3));
  const chess = answer.mul(.44).add(mosaic.mul(.36)).add(hit.mul(.18)).add(.2);
  const phrase = bar.div(8), mode = mod(floor(phrase).add(u.section), 3);
  const choose = n => select(n.lessThan(.5), glass, select(n.lessThan(1.5), procession, chess));
  const pattern = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.875, 1, fract(phrase)));

  // Similar-luminance jewel colours let the beat change whole tiles without a white strobe.
  const plum = vec3(.68, .04, .93), rose = vec3(.88, .046, .276);
  const brass = vec3(.45, .2, .022), jade = vec3(.016, .313, .176);
  const jewel = mix(plum, rose, note.mul(.65).add(u.mid.mul(.25)));
  const drum = mix(brass, jade, answer.mul(.7).add(u.snare.mul(.3)).clamp(0, 1));
  const tint = mix(jewel, drum, fast.mul(.55).add(front.mul(.35)).clamp(0, 1));
  const distance = one(smoothstep(4, 12, max(fwidth(p.x), fwidth(p.y))));
  const centre = smoothstep(.7, 3, abs(tileX)).mul(.2).add(.8);
  const drop = music.waveAmp || float(0);
  const fill = pattern.mul(.68).add(.26).add(front.mul(.25)).add(drop.mul(.16)).clamp(0, .94)
    .mul(distance).mul(centre).mul(one(seam.mul(.96)));
  const active = vizActivity(u, music).mul(mix(float(1), float(.32), gentle));
  return mix(base, mix(base, base.mul(.24).add(tint), fill), active);
}
