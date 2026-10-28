/* Comic instruments on ink: bass starbursts, melodic ribbons, snare cuts and high pinpricks. */
import { float, vec2, vec3, fract, floor, mod, abs, max, mix, smoothstep, sin, step, fwidth, atan } from 'three/tsl';
import { floorSpectrum, floorPhrase } from './floorSpectrum.js';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity } from './musicVisualizer.js';

export function goonworksMusicFloor({ p, base, top, edge, half, pink, accent, reactive: u, music }) {
  const fast = float(1).sub(u.gentle), span = max(half.sub(2), 1), across = abs(p.x);
  const normalized = across.div(span).clamp(0, 1), row = fract(p.y.div(24));
  const band = floorSpectrum(normalized, u).mul(.45).add(vizSpectrum(normalized, u).mul(.55).mul(fast)).toVar();
  const wave = vizWave(fract(p.y.div(32)), u).mul(fast).mul(4.2);
  // Three true musical silhouettes: bars, a spectral rosette and paired sound ribbons.
  const column = fract(normalized.mul(8)), gutter = smoothstep(.06, .14, column)
    .mul(float(1).sub(smoothstep(.86, .94, column)));
  const bars = float(1).sub(smoothstep(band.mul(.8), band.mul(.8).add(.05), row))
    .mul(smoothstep(0, .07, row)).mul(gutter).mul(band);
  const q = vec2(across.sub(span.mul(.48)).div(span.mul(.5)), fract(p.y.div(30).add(.5)).sub(.5).mul(2));
  const radius = q.length(), angle = atan(q.x, q.y);
  const spikes = sin(angle.mul(8)).mul(.5).add(.5);
  const rosette = vizLine(radius.sub(band.mul(.42).add(spikes.mul(.14)).add(.33)), .075).mul(band);
  const ribbonAt = across.sub(span.mul(.32).add(wave).add(u.mid.mul(1.2)));
  const ribbons = float(1).sub(smoothstep(.65, 1.3, abs(ribbonAt)))
    .mul(u.mid.mul(.65).add(.25)).add(vizHistory(normalized, row, u).mul(fast).mul(gutter).mul(.17));
  const melody = floorPhrase([bars, rosette, ribbons], music.tileBar.add(u.section.mul(8)));
  // Bass alone stamps and expands a jagged comic burst, leaving ink between attacks.
  const burstAt = u.kickAge.mul(1.3).add(.12).add(spikes.mul(.18));
  const bass = vizLine(radius.sub(burstAt), .12).mul(u.kickStrength)
    .mul(float(1).sub(smoothstep(.3, .9, u.kickAge))).mul(fast);
  // Snare cuts travel in alternating directions; halftone teeth give them a separate silhouette.
  const direction = mod(u.snareCount, 2).mul(2).sub(1);
  const slash = fract(p.y.div(21).add(p.x.mul(direction).div(19)).sub(u.snareAge));
  const teeth = step(.3, fract(p.x.div(.9).add(p.y.div(1.4))));
  const snare = vizLine(slash.sub(.5), .055).mul(teeth).mul(u.snare).mul(fast);
  const cell = floor(p.div(2)), sparse = step(.73, fract(cell.x.mul(.39).add(cell.y.mul(.57)).add(u.hatsCount.mul(.17))));
  const highs = vizLine(fract(p.x.div(2)).sub(.5), .075)
    .mul(vizLine(fract(p.y.div(2)).sub(.5), .075)).mul(sparse).mul(u.hats).mul(fast);
  const ink = vec3(.035, .006, .055), hot = mix(pink, vec3(.98, .025, .3), .7);
  const cyan = mix(accent, vec3(.035, .7, .9), .8);
  const rest = base.mul(.16).add(ink.mul(.5));
  const light = hot.mul(melody).add(hot.mul(bass).mul(1.25))
    .add(cyan.mul(snare)).add(vec3(.72, .96, .25).mul(highs));
  const visibility = float(1).sub(smoothstep(4, 10, max(fwidth(p.x), fwidth(p.y))));
  const active = vizActivity(u, music).mul(mix(float(1), float(.28), u.gentle))
    .mul(smoothstep(.8, 2.4, edge)).mul(top).mul(smoothstep(.8, 2.2, across).mul(.6).add(.4)).mul(visibility);
  return mix(base, rest.add(light).clamp(0, 1), active);
}
