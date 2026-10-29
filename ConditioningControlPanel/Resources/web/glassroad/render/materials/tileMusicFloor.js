/* Whole-tile instruments share a heard clock; only the plucked strings draw continuous curves. */
import { float, abs, floor, fract, mod, mix, smoothstep, step, max, min, sin, pow, fwidth } from 'three/tsl';
import { floorSpectrum, floorPhrase } from './floorSpectrum.js';
import { vizSpectrum, vizLine } from './musicVisualizer.js';
const one = v => float(1).sub(v);
const near = (v, width) => one(smoothstep(width, float(width).add(.08), abs(v)));
const decay = (age, seconds) => pow(float(Math.E), age.mul(-1 / seconds));
const field = (u, name, fallback = 0) => u[name] || float(fallback);
const display = (level, knee = .16) => level.div(level.add(knee));

// The wrapper supplies actual slab IDs for Keep and a fixed tile grid for the other decks.
export function tileInstrument({ p, column, row, columns = 12, halfSpan = float(8), kind = 0, reactive: u, music, stringPeriod = 26 }) {
  const gentle = u.gentle, fast = one(gentle), col = mod(column, columns).toVar();
  const middle = (columns - 1) / 2, count = field(u, 'kickCount');
  const note = mix(count, floor(music.tileBar), gentle).toVar();
  // Gentle moves the bank once a bar: its layer fades out before the boundary and back in after (a quarter
  // bar each way, never a snap), and fades in over gentleMix when Gentle is switched on.
  const barAt = fract(music.tileBar), barGate = smoothstep(0, .25, barAt).mul(one(smoothstep(.75, 1, barAt)));
  const gentleBass = (u.gentleMix ? u.gentleMix.mul(gentle) : gentle).mul(barGate).toVar();
  const drop = field(u, 'drop').mul(fast).toVar(), lead = field(u, 'lead').toVar();
  // Section walks smoothly under Gentle (musicVisualData), so floorPhrase crossfades instead of swapping.
  const phase = music.tileBar.add(field(u, 'section').mul(8)).toVar();
  const zigSpan = columns - 4;
  const zig = abs(mod(floor(row.div(2)), zigSpan * 2).sub(zigSpan)).add(1.5).toVar();
  const alternate = mod(note, 2), side = mix(float(2), float(columns - 3), alternate);
  const width = drop.mul(.95).add(1.02);
  let original;
  if (kind === 1) {
    const stairAt = mod(floor(row.div(3)).add(note), columns - 3).add(1).toVar();
    const stairWidth = drop.mul(.7).add(.76);
    original = max(near(col.sub(stairAt), stairWidth), near(col.sub(float(columns - 1).sub(stairAt)), stairWidth).mul(.46));
  }
  else if (kind === 2) original = near(abs(col.sub(middle)).sub(mod(floor(row.div(3)).add(mod(note, 3)), 4).add(1)), drop.mul(.75).add(.8));
  else {
    const tooth = kind === 3 ? mod(floor(row.div(2)), 4) : float(0);
    const bank = side.add(tooth.mul(one(alternate.mul(2))));
    original = near(col.sub(bank), width).mul(one(step(2, mod(floor(row.div(4)).add(floor(note.div(2))), 3))));
  }
  const relay = near(abs(col.sub(middle)).sub(mod(note, 3).add(2)), drop.mul(.8).add(.78))
    .mul(one(step(1, mod(floor(row.div(4)), 2))));
  const splitSide = mix(float(2), float(columns - 3), mod(floor(row.div(5)).add(note), 2));
  const split = near(col.sub(splitSide), drop.mul(.9).add(1.28));
  const bassMask = floorPhrase([original, relay, split], phase).toVar();
  // Replies are limited to selected stretches, so every hit has a clear primary strike.
  const reply = step(4, mod(floor(row.div(7)), 6)).mul(.1).mul(fast);
  const kickAge = max(field(u, 'kickAge', 9).sub(reply), 0).toVar();
  const kick = smoothstep(0, .018, kickAge).mul(decay(kickAge, .16))
    .mul(display(max(field(u, 'kickStrength'), field(u, 'kick')))).mul(fast);
  const bass = bassMask.mul(kick.mul(1.55).add(drop.mul(.64)))
    .add(bassMask.mul(u.bass).mul(gentleBass).mul(.24)).clamp(0, 1).toVar();
  const at = mod(abs(column).add(row), 8).div(7);
  const measured = floorSpectrum(at, u).mul(.4).add(vizSpectrum(at, u).mul(.6).mul(fast)).toVar();
  const held = display(measured, .18).toVar();
  const snareAge = field(u, 'snareAge', 9);
  const snare = display(max(field(u, 'snare'), field(u, 'snareStrength'))).mul(decay(snareAge, .19)).mul(fast);
  const stairAt = kind === 1 || kind === 3 ? float(columns - 1).sub(zig) : zig;
  const stair = near(col.sub(stairAt), measured.mul(2).add(.6));
  const shift = mod(floor(music.tileBeat.div(2)), 3).sub(1).mul(fast);
  const relayStep = near(col.sub(float(columns - 1).sub(stairAt).add(shift)), measured.mul(2).add(.6));
  const replyBank = mix(float(2), float(columns - 3), mod(floor(row.div(3)).add(field(u, 'snareCount').mul(fast)), 2));
  const splitStep = near(col.sub(replyBank), 1.05);
  const mids = floorPhrase([stair.mul(.68), relayStep, splitStep.mul(.8)], phase)
    .mul(held.mul(display(u.mid, .12)).mul(1.35).add(snare.mul(.9)).add(lead.mul(.32)))
    .mul(mix(float(1), float(.3), gentle)).clamp(0, 1).toVar();
  const hatsAge = field(u, 'hatsAge', 9), hatsCount = field(u, 'hatsCount');
  const sparse = one(step(1, mod(col.add(floor(row.div(4))).add(hatsCount), 7)))
    .mul(one(step(1, mod(row.add(hatsCount), 3))));
  const highs = sparse.mul(max(field(u, 'hats'), field(u, 'hatsStrength').mul(decay(hatsAge, .065))))
    .mul(u.treble.mul(.4).add(.6)).mul(fast).toVar();
  // Fixed world nodes make these read as strings on a moving road, rather than floating ribbons.
  let strings = float(0);
  const nodes = sin(p.y.mul(Math.PI * 2 / stringPeriod)).toVar();
  const bend = mix(float(.3).add(lead.mul(.65)).add(drop.mul(.55)), float(.045), gentle).toVar();
  for (let i = 0; i < 4; i++) {
    const age = field(u, 'pluckAge' + i, 9), strength = field(u, 'pluckStrength' + i);
    const envelope = smoothstep(0, mix(float(.025), float(.3), gentle), age)
      .mul(decay(age, kind === 2 ? .65 : .48)).mul(display(strength, .13)).toVar();
    const carrier = sin(age.mul(Math.PI * 10.8)).add(sin(age.mul(Math.PI * 16.2).add(i)).mul(.22));
    const displacement = nodes.mul(mix(carrier, float(1), gentle)).mul(envelope).mul(bend);
    const origin = halfSpan.mul((i / 3 * 2 - 1) * .64);
    const line = vizLine(p.x.sub(origin).sub(displacement), envelope.mul(.05).add(.065));
    strings = strings.add(line.mul(envelope.mul(1.25)));
  }
  return { bass, mids, highs, strings: strings.clamp(0, 1), drop };
}

// Fine dark grout belongs to the fixed floor, not the music envelope. It melts below a pixel.
export function tileGrout(p, cellX, cellY) {
  const x = fract(p.x.div(cellX)), y = fract(p.y.div(cellY));
  const edge = min(min(x, one(x)), min(y, one(y)));
  const pixel = max(max(fwidth(p.x.div(cellX)), fwidth(p.y.div(cellY))), .006);
  return mix(smoothstep(.025, pixel.add(.035), edge), float(.92), smoothstep(.12, .45, pixel));
}
