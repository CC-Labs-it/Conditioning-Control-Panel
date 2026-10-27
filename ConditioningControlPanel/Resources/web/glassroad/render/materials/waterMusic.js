// Sound drawn in the liquid: a live oscilloscope, spectral caustics and struck interference rings.
// Fixed road coordinates keep the picture attached to the water. Only heard samples and hit ages move it.
import { float, vec2, sin, abs, fract, length, atan, mix, smoothstep, max, fwidth } from 'three/tsl';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity, vizHit } from './musicVisualizer.js';

export function waterMusicLight(p, u, tint, accent = tint) {
  const fast = float(1).sub(u.gentle), active = vizActivity(u);
  const along = fract(p.y.div(40).add(.5)).sub(.5).mul(40);
  const across = p.x.div(28).add(.5).clamp(0, 1);
  const wave = vizWave(fract(p.y.div(32)), u);
  const spectrum = vizSpectrum(across, u);
  const history = vizHistory(across, fract(p.y.div(40)), u);
  const hit = vizHit(u);

  // A broad river of the ACTUAL waveform, with a coloured phosphor afterimage on one side.
  // There is no mirrored sine pair or rung lattice, so this cannot read as another DNA helix.
  const river = p.x.sub(wave.mul(u.mid.mul(2).add(4.4)));
  const ribbon = vizLine(river, .22).add(vizLine(river.sub(.7), .1).mul(.34));
  const glow = vizLine(river, .9).mul(.14);

  // Each spoke of a submerged rose is a measured frequency. Its contour opens with that bin.
  const q = vec2(p.x.div(12), along.div(15)), radius = length(q);
  const angle = atan(q.y, q.x);
  const polar = vizSpectrum(abs(angle).div(Math.PI), u);
  const reach = polar.mul(.62).add(.25).add(hit.mul(.12));
  const rose = vizLine(radius.sub(reach), .025);
  const echo = vizLine(radius.sub(reach.mul(.64)), .018).mul(.45);
  const spokes = sin(angle.mul(10).add(polar.mul(5))).mul(.5).add(.5);
  const fan = float(1).sub(smoothstep(reach, reach.add(.12), radius)).mul(spokes).mul(.14);

  // A spectrogram leaves layered refracted contours behind the current frequency picture.
  // This is measured history, not a travelling clock texture that continues through a musical rest.
  const contour = vizLine(sin(history.mul(15).add(p.x.mul(.26))), .12)
    .mul(history).mul(.25);
  const interference = sin(radius.mul(18).sub(wave.mul(3)).sub(spectrum.mul(5)))
    .mul(.5).add(.5).mul(history).mul(.12);

  // Every real attack strikes two fixed pool centres; fronts travel out and cross, then settle.
  const age = u.hitAge.clamp(0, 2), ringM = age.mul(19).add(.4);
  const fade = float(1).sub(smoothstep(.1, 1.05, age)).mul(u.hitStrength);
  const rA = length(vec2(p.x.add(3.5), along));
  const rB = length(vec2(p.x.sub(3.5), along.add(5)));
  const rings = max(vizLine(rA.sub(ringM), .22), vizLine(rB.sub(ringM), .22))
    .add(vizLine(rA.sub(ringM.mul(.65)), .12).mul(.3)).mul(fade);

  // Tint remains the course's palette. Fast detail fades away under Gentle; a soft band wash remains.
  const tone = spectrum.mul(.55).add(u.treble.mul(.2)).clamp(0, 1);
  const colour = mix(tint, accent, tone), counter = mix(accent, tint, .22);
  const detailed = colour.mul(ribbon.mul(.36).add(glow).add(contour).add(interference))
    .add(counter.mul(rose.add(echo).mul(.3).add(fan).add(rings.mul(.45)))).mul(fast);
  const soft = mix(tint, accent, u.mid).mul(u.bass.mul(.035).add(u.mid.mul(.025)));
  const detail = float(1).sub(smoothstep(.8, 3, max(fwidth(p.x), fwidth(p.y))));
  return detailed.mul(detail).add(soft).mul(active).min(.6);
}
