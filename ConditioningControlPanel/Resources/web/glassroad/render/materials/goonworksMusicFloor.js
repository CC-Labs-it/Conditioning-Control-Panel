/* Broad soundstage graphics printed into the enamel, driven by the heard music. */
import { float, vec2, vec3, fract, abs, max, mix, smoothstep, sin, fwidth, atan } from 'three/tsl';
import { floorSpectrum, floorPhrase } from './floorSpectrum.js';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity, vizHit } from './musicVisualizer.js';

export function goonworksMusicFloor({ p, base, top, edge, half, pink, accent, reactive: u, music }) {
  const gentle = u.gentle, fast = float(1).sub(gentle), beat = music.tileBeat, bar = music.tileBar;
  const active = vizActivity(u, music).mul(mix(float(1), float(.28), gentle));
  const playable = smoothstep(.8, 2.4, edge).mul(top);
  // Keep the centre lettering clear without erasing the whole driving view.
  const centreRoom = smoothstep(.8, 2.2, abs(p.x)).mul(.3).add(.7);
  // These are metre-wide graphics. Only the far horizon loses them, not normal chase distance.
  const visibility = float(1).sub(smoothstep(4, 10, max(fwidth(p.x), fwidth(p.y))));
  const span = max(half.sub(2), 1), normalized = abs(p.x).div(span).clamp(0, 1);
  const band = floorSpectrum(normalized, u).mul(.55).add(vizSpectrum(normalized, u).mul(.45).mul(fast)).toVar();
  const hit = vizHit(u).mul(fast).toVar();
  const wave = vizWave(fract(p.y.div(32)), u).mul(fast).toVar();
  const history = vizHistory(normalized, fract(p.y.div(24)), u).mul(fast).toVar();

  // Eight mirrored columns grow in the road direction, like a giant equalizer under the kart.
  const column = fract(normalized.mul(8)), row = fract(p.y.div(24));
  const gutter = smoothstep(.04, .12, column).mul(float(1).sub(smoothstep(.88, .96, column)));
  const level = band.mul(.72).add(u.kick.mul(fast).mul(.18)).add(u.bass.mul(.14)).add(.06);
  const barFill = float(1).sub(smoothstep(level, level.add(.06), row));
  const cap = float(1).sub(smoothstep(.025, .07, abs(row.sub(level))));
  const equalizer = barFill.mul(.98).add(cap.mul(.5)).mul(gutter);
  const barBed = gutter.mul(float(1).sub(smoothstep(.92, .98, row)));

  // Comic starbursts stamp the kick. Their spectral outline answers the recording, never a spin timer.
  const q = vec2(abs(p.x).sub(span.mul(.48)).div(span.mul(.5)),
    fract(p.y.div(30).add(.5)).sub(.5).mul(2));
  const radius = q.length(), angle = atan(q.x, q.y);
  const spokeBand = vizSpectrum(abs(sin(angle.mul(.5))), u).mul(fast).add(band.mul(.25));
  const spikes = sin(angle.mul(8)).mul(.5).add(.5);
  const reach = spokeBand.mul(.38).add(.25).add(u.kick.mul(fast).mul(.32))
    .add(spikes.mul(u.snare.mul(fast).mul(.12).add(.13)));
  const disc = float(1).sub(smoothstep(reach, reach.add(.1), radius));
  const ring = vizLine(radius.sub(u.hitAge.mul(1.2)), .07).mul(u.hitStrength).mul(fast);
  const pinwheel = disc.mul(spikes.mul(.64).add(.26)).add(ring)
    .add(vizLine(radius.sub(reach), .035).mul(.45));
  const record = float(1).sub(smoothstep(.93, 1.03, radius));

  // Actual PCM writes fat mirrored ribbons, with spectral history printed behind in halftone rows.
  const riverAt = span.mul(.32).add(wave.mul(4.2)).add(u.mid.mul(1.8)), distance = abs(abs(p.x).sub(riverAt));
  const width = u.bass.mul(1.8).add(hit.mul(1.4)).add(1.2);
  const river = float(1).sub(smoothstep(width, width.add(.65), distance));
  const stereo = river.mul(.8).add(history.mul(.65)).add(vizLine(distance.sub(width), .18).mul(.45));
  const riverBed = float(1).sub(smoothstep(width.add(.7), width.add(1.5), distance));

  const phrase = bar.add(u.section.mul(8));
  const shape = floorPhrase([equalizer, pinwheel, stereo], phrase).clamp(0, 1).toVar();
  const bed = floorPhrase([barBed, record, riverBed], phrase).clamp(0, 1);
  const hue = band.mul(.75).add(history.mul(.25)).add(u.treble.mul(.2)).clamp(0, 1);
  const cyan = mix(accent, vec3(.025, .72, .78), u.hats.mul(fast).mul(.15).add(.7));
  // Sound paints the whole graphic; the small beat-grid accent only bridges the gaps between attacks.
  const answer = hit.mul(.7).add(sin(beat.mul(Math.PI * 2).sub(p.y.div(8)))
    .mul(.5).add(.5).mul(.3)).mul(fast);
  const hot = mix(pink, vec3(.94, .025, .36), .65);
  const tint = mix(hot, cyan, mix(hue, answer, fast.mul(.5)).clamp(0, 1));
  const ink = mix(vec3(.055, .012, .075), accent.mul(.18), .35);
  const colourBed = mix(base, tint, fast.mul(.28));
  const paint = mix(mix(colourBed, ink, bed.mul(.55)), tint, shape.mul(.98));
  // Apply energy once, after composition, so Gentle keeps the full slow envelope rise.
  return mix(base, paint, active.mul(playable).mul(centreRoom).mul(visibility));
}
