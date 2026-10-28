/* Graphite instruments: bass reactors, melodic phosphor, snare scans and high LED sparks. */
import { float, vec2, vec3, floor, fract, mod, abs, mix, smoothstep, step, length, max, fwidth } from 'three/tsl';
import { floorPhrase, floorSpectrum } from './floorSpectrum.js';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity } from './musicVisualizer.js';
const one = v => float(1).sub(v);

export function mainframeTileLight(p, u, music, trueEdge, base) {
  const fast = one(u.gentle), at = abs(p.x).div(15).clamp(0, 1);
  const note = floorSpectrum(at, u), spectrum = vizSpectrum(at, u).mul(fast).add(note.mul(.35));
  const row = fract(p.y.div(24)), bank = fract(abs(p.x).div(1.65));
  // Voice and sustained notes draw phosphor traces, spectrum teeth or the recorded spectral waterfall.
  const pcm = vizWave(row, u).mul(fast).mul(3.8);
  const scope = vizLine(abs(p.x).sub(pcm.add(4)), .28).mul(u.mid.mul(.9).add(.12));
  const teeth = one(smoothstep(.29, .43, abs(bank.sub(.5))))
    .mul(one(smoothstep(spectrum.mul(.72), spectrum.mul(.72).add(.045), row)))
    .mul(smoothstep(0, .08, row)).mul(spectrum);
  const history = vizHistory(at, row, u).mul(fast).mul(note.mul(.3).add(.7));
  const phrase = music.tileBar.add(u.section.mul(8));
  const melody = floorPhrase([scope, teeth, history.mul(teeth.mul(.7).add(.12))], phrase);
  // Only a bass attack launches a reactor front. Snares cannot restart its radius.
  const core = vec2(p.x, fract(p.y.div(32).add(.5)).sub(.5).mul(32));
  const bass = vizLine(length(core).sub(u.kickAge.mul(22).add(1.5)), .85)
    .mul(u.kickStrength).mul(one(smoothstep(.3, .85, u.kickAge))).mul(fast);
  // Each snare alternates the scan direction and opens a narrow diagonal slash.
  const direction = mod(u.snareCount, 2).mul(2).sub(1);
  const diagonal = fract(p.y.div(22).add(p.x.mul(direction).div(18)).sub(u.snareAge.mul(1.1)));
  const snare = vizLine(diagonal.sub(.5), .035).mul(u.snare).mul(fast);
  // Sparse LED intersections have their own short high-frequency envelope.
  const cell = floor(p.div(1.8));
  const sparse = step(.68, fract(cell.x.mul(.37).add(cell.y.mul(.61)).add(u.hatsCount.mul(.17))));
  const dots = vizLine(fract(p.x.div(1.8)).sub(.5), .075)
    .mul(vizLine(fract(p.y.div(1.8)).sub(.5), .075)).mul(sparse);
  const highs = dots.mul(u.hats).mul(fast);
  const edge = smoothstep(.8, 2.4, trueEdge), centre = smoothstep(.75, 2, abs(p.x)).mul(.6).add(.4);
  const detail = one(smoothstep(.6, 2, max(fwidth(p.x), fwidth(p.y))));
  const active = vizActivity(u, music).mul(mix(float(1), float(.32), u.gentle)).mul(edge).mul(centre).mul(detail);
  const rest = base.mul(.28);
  const voiceLight = vec3(.025, .62, .2).mul(melody).mul(u.mid.mul(.5).add(.65));
  const bassLight = vec3(.35, .9, .055).mul(bass).mul(1.2);
  const snareLight = vec3(.025, .57, .9).mul(snare);
  const highLight = vec3(.72, .96, .46).mul(highs);
  return mix(base, rest.add(voiceLight).add(bassLight).add(snareLight).add(highLight).clamp(0, 1), active);
}
