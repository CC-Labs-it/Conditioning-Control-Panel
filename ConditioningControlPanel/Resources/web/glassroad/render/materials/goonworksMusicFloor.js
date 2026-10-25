/* A pinball soundstage printed into the enamel. Shapes answer the shared measured spectrum. */
import { float, vec2, vec3, fract, abs, max, mix, smoothstep, sin, fwidth, atan } from 'three/tsl';
import { floorSpectrum, floorPhrase, floorWave, floorLine } from './floorSpectrum.js';
export function goonworksMusicFloor({ p, base, top, edge, half, pink, accent, reactive: u, music }) {
  const gentle = u.gentle, beat = music.tileBeat, bar = music.tileBar;
  const clock = mix(beat.div(4), bar.div(8), gentle);
  const active = mix(u.energy.mul(4).clamp(0, 1), u.energy, gentle).mul(music.tileOn).mul(mix(float(1), float(.28), gentle));
  const playable = smoothstep(.8, 2.4, edge).mul(top);
  const centreRoom = smoothstep(1.4, 3.6, abs(p.x)).mul(.78).add(.14);
  const visibility = float(1).sub(smoothstep(.75, 2.1, max(fwidth(p.x), fwidth(p.y))));
  const normalized = abs(p.x).div(max(half.sub(2), 1)), band = floorSpectrum(normalized, u);
  const beatBreath = mix(floorWave(beat.div(2)), float(.5), gentle).mul(u.bass);
  // Stereo rivers bend with eight frequency bands; the travelling ball rides the heard beat.
  const along = p.y.div(32), bend = sin(along.mul(Math.PI * 2).sub(clock)).mul(u.mid.mul(2).add(.6));
  const channel = half.mul(.48).add(bend).add(band.mul(2.4));
  const side = abs(p.x).sub(channel), river = floorLine(side, .35);
  const head = fract(along.sub(clock.div(2)));
  const ball = float(1).sub(smoothstep(.2, .45, vec2(side.div(2), head.sub(.5).mul(7)).length()));
  const stereo = river.mul(band.mul(.5).add(.35)).add(ball.mul(.65));
  // Record-sized pinwheels stretch individual petals to the spectrum.
  const q = vec2(p.x.div(half.mul(1.4)), fract(p.y.div(36).add(.5)).sub(.5));
  const r = q.length(), a = atan(q.x, q.y);
  const spokeBand = floorSpectrum(abs(sin(a.mul(.5))), u);
  const spoke = sin(a.mul(8).add(clock.mul(1.7)).add(r.mul(8))).mul(.5).add(.5);
  const ringR = spokeBand.mul(.18).add(.22).add(beatBreath.mul(.06));
  const disc = float(1).sub(smoothstep(ringR, ringR.add(.08), r));
  const pinwheel = spoke.mul(disc).mul(.7).add(floorLine(r.sub(ringR), .018).mul(.45));
  // A mirrored spectrum staircase grows from both shoulders toward a quiet word lane.
  const stairHeight = band.mul(.66).add(u.bass.mul(.16));
  const row = fract(p.y.div(24).sub(clock.div(4)));
  const steps = smoothstep(row.sub(.08), row.add(.08), stairHeight);
  const lens = floorLine(sin(p.x.mul(1.35)), .19);
  const spectrum = steps.mul(lens.mul(.35).add(.45)).add(floorLine(row.sub(stairHeight), .025).mul(.3));
  const pattern = floorPhrase([stereo, pinwheel, spectrum], bar);
  const hue = floorWave(normalized.mul(.5).add(bar.div(16))).mul(.6).add(band.mul(.3));
  const tint = mix(pink, accent, hue).mul(.85).add(vec3(.10, .07, .08));
  const fill = pattern.mul(active).mul(playable).mul(centreRoom).mul(visibility).clamp(0, .82);
  return mix(base, base.mul(.38).add(tint), fill).add(tint.mul(fill).mul(u.treble.mul(.16).add(.08)));
}
