/* The Keep is a tile instrument: bass fronts, snare cuts, high glints and spectral organ banks. */
import { float, vec3, floor, fract, mod, abs, mix, smoothstep, step, select, max, min, sin, pow, fwidth } from 'three/tsl';
import { vizSpectrum, vizHistory, vizActivity } from './musicVisualizer.js';

const one = v => float(1).sub(v), decay = (age, seconds) => pow(float(Math.E), age.mul(-1 / seconds));

export function keepMusicFloor({ p, base, seam, reactive: u, music }) {
  // Every effect is evaluated at the slab index. Nothing draws a partial figure across a stone.
  const repeat = floor(p.x.div(12)), localX = fract(p.x.div(12)).mul(12);
  const within = step(2, localX).add(step(6, localX)).add(step(10, localX));
  const column = repeat.mul(4).add(within).toVar();
  const row = floor(p.y.div(8).add(step(6, localX).mul(.5))).toVar();
  const bank = mod(abs(column).add(row.mul(2)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(one(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  note = note.toVar();
  const gentle = u.gentle, fast = one(gentle), bar = music.tileBar;
  const kickAge = u.kickAge || float(9), snareAge = u.snareAge || float(9), hatsAge = u.hatsAge || float(9);
  const kickCount = u.kickCount || float(0), snareCount = u.snareCount || float(0), hatsCount = u.hatsCount || float(0);
  const kick = max(u.kick, (u.kickStrength || float(0)).mul(decay(kickAge, .55))).mul(fast);
  const snare = max(u.snare, (u.snareStrength || float(0)).mul(decay(snareAge, .2))).mul(fast);
  const hats = max(u.hats, (u.hatsStrength || float(0)).mul(decay(hatsAge, .075))).mul(fast);
  const spectrum = vizSpectrum(bank.add(.5).div(8), u);
  const history = vizHistory(bank.add(.5).div(8), mod(row, 12).div(12), u);
  const sustained = spectrum.mul(.62).add(note.mul(.38)).clamp(0, 1);

  // Three different spatial instruments: organ stacks, diamond choirs and broken chess processions.
  const organRow = mod(row, 8).add(.5).div(8);
  const organ = one(smoothstep(sustained, sustained.add(.1), organRow)).mul(sustained);
  const diamond = abs(mod(column.add(4), 8).sub(4)).mul(.16)
    .add(abs(mod(row.add(4), 8).sub(4)).mul(.12));
  const choir = one(smoothstep(sustained.add(history.mul(.24)), sustained.add(history.mul(.24)).add(.12), diamond))
    .mul(note.mul(.55).add(history.mul(.45)));
  const checker = mod(column.add(row).add(floor(music.tileBeat.div(8))), 2);
  const procession = step(.32, sustained).mul(checker).mul(sustained.mul(.6).add(history.mul(.4)));
  const phrase = bar.div(8), mode = mod(floor(phrase).add(u.section), 3);
  const choose = n => select(n.lessThan(.5), organ, select(n.lessThan(1.5), choir, procession));
  const melody = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.875, 1, fract(phrase)));

  // Bass sends wide golden rows outward; its position belongs only to the latest kick.
  const origin = mod(kickCount, 3).mul(2);
  const rowDistance = abs(mod(row.sub(origin).add(6), 12).sub(6));
  const bassFront = one(smoothstep(.55, u.bass.mul(.6).add(1.6), abs(rowDistance.sub(kickAge.mul(15))))).mul(kick);
  // Snare cuts strike a diagonal, then leave a quieter parallel echo. They never restart the bass.
  const diagonal = abs(mod(column.add(row).add(snareCount.mul(2)), 7).sub(3));
  const snareCut = one(smoothstep(.25, 1.1, diagonal)).mul(snare);
  const echoCut = one(smoothstep(.25, 1.1, abs(diagonal.sub(2))))
    .mul(decay(snareAge, .32)).mul(u.snareStrength || float(0)).mul(fast).mul(.22);
  // High percussion chooses a few complete jewel tiles, with a short independent sparkle lifetime.
  const seed = fract(sin(column.mul(12.9898).add(row.mul(78.233)).add(hatsCount.mul(3.11))).mul(43758.5453));
  const glints = step(.9, seed).mul(hats).mul(u.treble.mul(.35).add(.65));
  const drop = (music.waveAmp || float(0)).mul(fast);
  const dropRows = one(smoothstep(.35, 1.2, abs(mod(row, 8).sub(3.5)))).mul(drop);

  const brass = vec3(.85, .39, .045), rose = vec3(.95, .055, .24), jade = vec3(.025, .55, .34);
  const plum = vec3(.45, .045, .77), pearl = vec3(.62, .86, .68);
  const voiced = mix(plum, jade, sustained.mul(.7).add(history.mul(.3)));
  const rest = base.mul(mix(float(.22), float(.8), gentle));
  const melodyLight = voiced.mul(melody).mul(u.mid.mul(.65).add(.35)).mul(mix(float(.82), float(.18), gentle));
  const musicLight = melodyLight.add(brass.mul(bassFront).mul(.9)).add(rose.mul(snareCut.add(echoCut)).mul(.82))
    .add(pearl.mul(glints).mul(.8)).add(jade.mul(dropRows).mul(.55));
  const ink = rest.add(musicLight).clamp(0, .96);
  const visibility = one(smoothstep(4, 12, max(fwidth(p.x), fwidth(p.y))));
  const mortar = one(seam.mul(.98));
  return mix(base, ink, vizActivity(u, music).mul(visibility).mul(mortar));
}
