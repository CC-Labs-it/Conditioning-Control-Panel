/* A jukebox floor printed into the final GOONWORKS enamel, using the one heard-song clock. */
import { float, vec2, vec3, floor, fract, mod, abs, min, max, mix, smoothstep, step, sin, cos, fwidth, select } from 'three/tsl';

export function goonworksMusicFloor({ p, base, top, edge, half, pink, accent, reactive: u, music }) {
  const gentleGain = mix(float(1), float(.28), u.gentle);
  const clock = music.tileBeat.mul(mix(float(1), float(.2), u.gentle));
  const beatBreath = cos(clock.mul(Math.PI)).mul(-.5).add(.5);
  const active = u.energy.mul(4).clamp(0, 1).mul(music.tileOn).mul(gentleGain);
  // The clear centre and fixed outer rule remain the route; patterns occupy the broad enamel.
  const playable = smoothstep(.8, 2.4, edge).mul(top);
  const centreRoom = smoothstep(1.7, 4.2, abs(p.x)).mul(.78).add(.16);
  const visibility = float(1).sub(smoothstep(.75, 2.1, max(fwidth(p.x), fwidth(p.y))));

  // Jukebox lamps: round lenses swell to eight measured note bands, with a smooth beat chase.
  const grid = vec2(p.x.div(3.4), p.y.div(5.2)), cell = floor(grid), local = fract(grid).sub(.5);
  const bank = mod(cell.x.add(cell.y.mul(3)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(float(1).sub(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  const radius = note.mul(.19).add(u.bass.mul(.055)).add(.12), distance = local.length();
  const aa = max(fwidth(distance), .012);
  const lens = float(1).sub(smoothstep(radius, radius.add(aa), distance));
  const rim = float(1).sub(smoothstep(.018, aa.add(.035), abs(distance.sub(radius.add(.055)))));
  const chase = cos(bank.sub(clock).mul(Math.PI / 4)).mul(.5).add(.5);
  const lamps = lens.mul(note.mul(.7).add(chase.mul(.45)).add(.12)).add(rim.mul(u.treble.mul(.4).add(.12)));

  // Vinyl grooves: broad expanding circles, never the masonry diamonds or a rectangular equalizer.
  const record = vec2(fract(p.x.div(15)).sub(.5).mul(1.15), fract(p.y.div(28)).sub(.5));
  const recordR = record.length(), groovePhase = recordR.mul(32).sub(clock.mul(Math.PI * .5));
  const groove = smoothstep(.1, .9, sin(groovePhase));
  const recordEdge = float(1).sub(smoothstep(.4, .51, recordR));
  const label = float(1).sub(smoothstep(.045, .075, recordR));
  const vinyl = groove.mul(recordEdge).mul(u.mid.mul(.65).add(u.bass.mul(.45)))
    .add(label.mul(beatBreath.mul(.45).add(.25)));

  // Pinball comets: paired strings of round lights travel along curved printed channels.
  const channel = half.mul(.42).add(sin(p.y.mul(.045).sub(clock.mul(.2))).mul(u.mid.mul(.4).add(1.2)));
  const sideDistance = abs(abs(p.x).sub(channel));
  const along = mod(p.y.sub(clock.mul(3)), 19);
  const cometDistance = vec2(sideDistance.div(.8), min(along, float(19).sub(along)).div(1.15)).length();
  const comet = float(1).sub(smoothstep(.55, max(fwidth(cometDistance), .05).add(1), cometDistance));
  const rail = float(1).sub(smoothstep(.06, max(fwidth(sideDistance), .035).add(.16), sideDistance));
  const tail = float(1).sub(smoothstep(1.1, 9, along));
  const pinball = comet.mul(u.bass.mul(.7).add(.35)).add(rail.mul(tail.mul(.45).add(.12)).mul(u.mid.mul(.6).add(.3)));

  // Each eight-bar phrase introduces the next toy. The crossfade lasts over a bar.
  const phrase = music.tileBar.div(8), mode = mod(floor(phrase), 3);
  const choose = index => select(index.lessThan(.5), lamps, select(index.lessThan(1.5), vinyl, pinball));
  const pattern = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.8, 1, fract(phrase)));
  const colourPhase = sin(bank.mul(.7).add(music.tileBar.mul(.18))).mul(.5).add(.5);
  const tint = mix(pink, accent, colourPhase).mul(.8).add(vec3(.13, .1, .07));
  const fill = pattern.mul(active).mul(playable).mul(centreRoom).mul(visibility).clamp(0, .82);
  return mix(base, base.mul(.35).add(tint), fill).add(tint.mul(fill).mul(.24));
}
