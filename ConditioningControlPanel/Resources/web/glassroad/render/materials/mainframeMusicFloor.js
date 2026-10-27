/* A phosphor instrument deck: the recording writes its waveform, spectrum and history into graphite. */
import { float, vec2, vec3, floor, fract, mod, abs, mix, smoothstep, step, cos, select, length, max, fwidth } from 'three/tsl';
import { vizWave, vizSpectrum, vizHistory, vizLine, vizActivity, vizHit } from './musicVisualizer.js';

const TAU = Math.PI * 2, one = v => float(1).sub(v);

export function mainframeTileLight(p, u, music, trueEdge, base) {
  const gentle = u.gentle, fast = one(gentle), beat = music.tileBeat;
  const bank = mod(floor(abs(p.x).div(1.65)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(one(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  note = note.toVar();
  const frequency = bank.add(.5).div(8), spectrum = vizSpectrum(frequency, u);
  const hit = vizHit(u).mul(fast), grid = cos(beat.mul(TAU)).mul(.5).add(.5).mul(u.bass).mul(fast);

  // An actual PCM trace, with a dim phosphor skirt. No synthetic sine pretends to be the waveform.
  const sampleAt = fract(p.y.div(24));
  const trace = vizWave(sampleAt, u).mul(u.mid.mul(1.8).add(1.3)).mul(fast);
  const traceX = trace.add(3.2).add(u.kick.mul(.8).mul(fast));
  const traceDistance = abs(abs(p.x).sub(traceX));
  const scope = vizLine(traceDistance, .18).add(vizLine(traceDistance, .7).mul(.17));

  // Stationary spectrum teeth change height immediately; the past sound remains in the panel beneath.
  const stripe = one(smoothstep(.29, .44, abs(fract(abs(p.x).div(1.65)).sub(.5))));
  const row = fract(p.y.div(18)), height = spectrum.mul(.65).add(note.mul(.2)).add(.05);
  const teeth = stripe.mul(one(smoothstep(height, height.add(.045), row)))
    .mul(smoothstep(0, .07, row));
  const history = vizHistory(frequency, row, u).mul(.65).add(note.mul(.16));
  const spectral = teeth.mul(.68).add(history.mul(.42));

  // Each detected hit launches a finite front out of repeated reactor cores, then leaves the deck.
  const core = vec2(p.x, fract(p.y.div(32).add(.5)).sub(.5).mul(32));
  const radius = length(core), front = u.hitAge.mul(22).add(1.5);
  const ring = vizLine(radius.sub(front), .7).mul(u.hitStrength).mul(fast);
  const reactor = ring.add(spectral.mul(.38)).add(scope.mul(u.snare.mul(fast).mul(.5).add(.15)));

  const phrase = music.tileBar.div(8), mode = mod(floor(phrase).add(u.section), 3);
  const choose = n => select(n.lessThan(.5), scope.add(spectral.mul(.24)), select(n.lessThan(1.5), spectral, reactor));
  const pattern = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.875, 1, fract(phrase)))
    .add(ring.mul(.65)).add(grid.mul(.1));
  const detail = one(smoothstep(.6, 2, max(fwidth(p.x), fwidth(p.y))));
  const edge = smoothstep(.8, 2.4, trueEdge), centre = smoothstep(.75, 2, abs(p.x)).mul(.5).add(.5);
  const active = vizActivity(u, music).mul(mix(float(1), float(.32), gentle));
  const fill = pattern.mul(active).mul(edge).mul(centre).mul(detail).clamp(0, .85);
  const emerald = vec3(.018, .37, .08), lime = vec3(.38, .31, .025), cyan = vec3(.025, .32, .52);
  const green = mix(mix(emerald, lime, note.mul(.55).add(u.treble.mul(.35))), cyan,
    hit.mul(.55).add(u.hats.mul(fast).mul(.2)).clamp(0, 1));
  return mix(base, base.mul(.4).add(green), fill);
}
