// Each musical element has its own mark in the liquid and its own clock. Quiet water stays dark.
import { float, vec2, sin, abs, fract, floor, mod, length, mix, smoothstep, max, min, exp, step, fwidth } from 'three/tsl';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity } from './musicVisualizer.js';

export function waterMusicLight(p, u, tint, accent = tint) {
  const fast = float(1).sub(u.gentle), active = vizActivity(u);
  const along = fract(p.y.div(40).add(.5)).sub(.5).mul(40);
  const across = p.x.div(28).add(.5).clamp(0, 1);
  const wave = vizWave(fract(p.y.div(32)), u);
  const spectrum = vizSpectrum(across, u);
  const history = vizHistory(across, fract(p.y.div(40)), u);
  // Bass strikes a wide circular front, alternating its fixed source on successive kicks.
  const kickAge = u.kickAge.clamp(0, 2), kickX = mod(u.kickCount, 2).mul(6).sub(3);
  const kickRadius = kickAge.mul(17).add(.6), r = length(vec2(p.x.sub(kickX), along));
  const kickLife = exp(kickAge.div(-.4)).mul(float(1).sub(smoothstep(.8, 1.1, kickAge)));
  const kick = max(u.kick.mul(.8), u.kickStrength.mul(kickLife));
  const rings = max(vizLine(r.sub(kickRadius), .5), vizLine(r.sub(kickRadius.mul(.62)), .2).mul(.4)).mul(kick);

  // Mids and voice draw a narrow live PCM river, with spectral history as a dim refracted contour.
  const river = p.x.sub(wave.mul(u.mid.mul(2).add(4.4)));
  const ribbon = vizLine(river, .22).add(vizLine(river.sub(.7), .1).mul(.32))
    .add(vizLine(river, .8).mul(.1)).mul(u.mid.mul(1.1));
  const contour = vizLine(sin(history.mul(15).add(p.x.mul(.26))), .1)
    .mul(history).mul(u.mid).mul(.25);

  // Snares launch crossing crescent cuts from a different pair of pool centres.
  // Their 240 ms decay and alternating side cannot restart or change the bass front.
  const snareAge = u.snareAge.clamp(0, 2), side = mod(u.snareCount, 2).mul(2).sub(1);
  const snareRadius = snareAge.mul(23).add(3);
  const rA = length(vec2(p.x.sub(side.mul(6)), along.add(4)));
  const rB = length(vec2(p.x.add(side.mul(4)), along.sub(5)));
  const snareLife = exp(snareAge.div(-.24)).mul(float(1).sub(smoothstep(.45, .7, snareAge)));
  const snare = max(u.snare.mul(.75), u.snareStrength.mul(snareLife));
  const arcs = max(vizLine(rA.sub(snareRadius), .25), vizLine(rB.sub(snareRadius.mul(.8)), .18))
    .mul(smoothstep(-2, 2, p.x.mul(side))).mul(snare);

  // High attacks briefly light sparse four-point caustics, never the entire liquid.
  const cell = floor(p.div(1.8)), local = fract(p.div(1.8)).sub(.5).mul(1.8);
  const seed = fract(sin(cell.x.mul(127.1).add(cell.y.mul(311.7)).add(u.hatsCount.mul(13.7))).mul(43758.5453));
  const point = min(abs(local.x), abs(local.y)), extent = max(abs(local.x), abs(local.y));
  const star = vizLine(point, .035).mul(float(1).sub(smoothstep(.12, .34, extent))).mul(step(.9, seed));
  const hatsAge = u.hatsAge.clamp(0, 2);
  const hatsLife = exp(hatsAge.div(-.065)).mul(float(1).sub(smoothstep(.12, .2, hatsAge)));
  const hats = max(u.hats.mul(.75), u.hatsStrength.mul(hatsLife));
  const glints = star.mul(hats).mul(spectrum.mul(.3).add(.7));

  const riverTint = mix(tint, accent, spectrum.mul(.4).add(.45));
  const detailed = tint.mul(rings.mul(.95)).add(riverTint.mul(ribbon.add(contour)))
    .add(mix(accent, tint, .3).mul(arcs.mul(.95)))
    .add(mix(accent, float(1), .25).mul(glints.mul(.65))).mul(fast);
  // Gentle retains only its existing slow, low-contrast envelope. Full has honest resting space.
  const soft = mix(tint, accent, u.mid).mul(u.bass.mul(.035).add(u.mid.mul(.025))).mul(u.gentle);
  const detail = float(1).sub(smoothstep(.8, 3, max(fwidth(p.x), fwidth(p.y))));
  return detailed.mul(detail).add(soft).mul(active).min(.6);
}
