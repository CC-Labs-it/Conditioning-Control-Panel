/* The machine floor is a phosphor display printed into the fixed graphite panels. */
import { float, vec2, vec3, floor, fract, mod, abs, mix, smoothstep, step, sin, cos, select, length, max, fwidth } from 'three/tsl';

const TAU = Math.PI * 2, one = v => float(1).sub(v);
const line = (d, width) => {
  const aa = max(fwidth(d).mul(1.35), .025);
  return one(smoothstep(width, float(width).add(aa), abs(d))).mul(float(width).div(max(float(width), aa)));
};

export function mainframeTileLight(p, u, music, trueEdge, base) {
  const gentle = u.gentle, slow = music.tileBar.mul(TAU / 4);
  const beat = mix(cos(music.tileBeat.mul(TAU)).mul(.5).add(.5), float(.5), gentle);
  const bank = mod(floor(abs(p.x).div(1.65)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(one(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  note = note.toVar();

  // Paired oscilloscope traces turn the bass and vocal band into readable moving amplitude.
  const signalPhase = p.y.mul(TAU / 18).sub(slow);
  const carrier = sin(signalPhase).mul(u.mid.mul(2.2).add(.5))
    .add(sin(signalPhase.mul(2)).mul(u.treble.mul(.55)));
  const traceX = carrier.add(u.bass.mul(beat).mul(.8)).add(2.5);
  const oscilloscope = max(line(p.x.sub(traceX), .16), line(p.x.add(traceX), .16));
  const afterglow = max(line(p.x.sub(traceX), .6), line(p.x.add(traceX), .6)).mul(.19);

  // Long spectrum teeth have visibly independent heights instead of colouring whole floor tiles.
  const stripe = one(smoothstep(.27, .43, abs(fract(abs(p.x).div(1.65)).sub(.5))));
  const barY = fract(p.y.div(16).sub(music.tileBar.div(2)));
  const height = note.mul(.72).add(.06);
  const spectrum = stripe.mul(one(smoothstep(height, height.add(.055), barY)))
    .mul(smoothstep(0, .08, barY)).mul(note.mul(.65).add(.35));

  // Reactor arcs travel out from fixed cores; the bass moves the ring and highs write the ticks.
  const core = vec2(p.x, fract(p.y.div(28).add(.5)).sub(.5).mul(22));
  const radius = length(core), ringRadius = u.bass.mul(beat).mul(1.5).add(3.7);
  const ring = max(line(radius.sub(ringRadius), .22), line(radius.sub(ringRadius.mul(1.65)), .13).mul(.6));
  const ticks = line(sin(p.x.mul(1.1).add(p.y.mul(.6))), .1).mul(ring).mul(u.treble);
  const reactor = ring.add(ticks.mul(.7));

  const phrase = music.tileBar.div(8), mode = mod(floor(phrase), 3);
  const choose = n => select(n.lessThan(.5), oscilloscope.add(afterglow), select(n.lessThan(1.5), spectrum, reactor));
  const pattern = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.75, 1, fract(phrase)));
  const detail = one(smoothstep(.45, 1.5, max(fwidth(p.x), fwidth(p.y))));
  const edge = smoothstep(.8, 2.4, trueEdge), centre = smoothstep(.75, 2, abs(p.x)).mul(.65).add(.35);
  const active = music.tileOn.mul(mix(u.energy.mul(6).clamp(0, 1), u.energy, gentle)).mul(mix(float(1), float(.32), gentle));
  const fill = pattern.mul(active).mul(edge).mul(centre).mul(detail).clamp(0, .85);
  const green = mix(vec3(.018, .42, .055), vec3(.39, .75, .065), note.mul(.65).add(u.treble.mul(.2)));
  return mix(base, base.mul(.42).add(green), fill);
}
