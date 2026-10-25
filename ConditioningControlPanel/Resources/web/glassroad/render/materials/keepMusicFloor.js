/* Music is projected through broad stained-glass shapes onto the Keep's fixed slate. */
import { float, vec2, vec3, floor, fract, mod, abs, mix, smoothstep, step, sin, cos, select, length, atan, max, fwidth } from 'three/tsl';

const TAU = Math.PI * 2, one = v => float(1).sub(v);
const line = (d, width) => {
  const aa = max(fwidth(d).mul(1.3), .018);
  return one(smoothstep(width, float(width).add(aa), abs(d))).mul(float(width).div(max(float(width), aa)));
};

/** Rose windows, organ pipes and woven vaults share heard beats and measured frequency bands. */
export function keepMusicFloor({ p, base, seam, reactive: u, music }) {
  const gentle = u.gentle, slow = music.tileBar.mul(TAU / 8);
  const beat = mix(cos(music.tileBeat.mul(TAU)).mul(.5).add(.5), float(.5), gentle);
  const bank = mod(floor(abs(p.x).div(1.8)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(one(step(.5, abs(bank.sub(i)))).mul(u['note' + i]));
  note = note.toVar();

  // Eight coloured petals swell independently in each window. Heavy lead lines retain the pattern.
  const q = vec2(p.x.div(10), fract(p.y.div(24).add(.5)).sub(.5).mul(2));
  const radius = length(q), angle = atan(q.y, q.x);
  const petals = cos(angle.mul(8).add(slow)).mul(u.mid.mul(.10).add(.12)).add(.58);
  const rim = petals.add(u.bass.mul(beat).mul(.12));
  const pane = one(smoothstep(rim.sub(.12), rim.add(.035), radius));
  const petalColour = sin(angle.mul(4).sub(slow)).mul(.5).add(.5);
  const lead = max(line(radius.sub(rim), .028), line(sin(angle.mul(4).add(slow.mul(.5))), .045).mul(smoothstep(.12, .23, radius)));
  const rose = pane.mul(.3).add(line(radius.sub(rim.mul(.63)), .055).mul(.65)).add(line(radius.sub(rim), .075)).mul(one(lead.mul(.82)));

  // Opposing organ banks grow with the measured notes, carrying a crest down the hall on each bar.
  const pipe = one(smoothstep(.24, .41, abs(fract(abs(p.x).div(1.8)).sub(.5))));
  const organY = fract(p.y.div(20).sub(music.tileBar.div(4)));
  const organHeight = note.mul(.68).add(u.bass.mul(.13)).add(.08);
  const organ = pipe.mul(one(smoothstep(organHeight, organHeight.add(.08), organY)))
    .mul(smoothstep(0, .09, organY)).mul(note.mul(.7).add(.3));

  // Interlaced vault ribbons open with the vocal/mid band; brass stitches answer high frequencies.
  const weavePhase = p.y.mul(TAU / 28).sub(slow);
  const vaultX = sin(weavePhase).mul(u.mid.mul(2.2).add(2.5));
  const weave = max(line(p.x.sub(vaultX), .27), line(p.x.add(vaultX), .27));
  const stitches = line(sin(p.y.mul(TAU / 3)), .09).mul(weave).mul(u.treble);
  const vaulted = weave.mul(.7).add(stitches.mul(.6));

  // A two-bar dissolve changes the composition every eight bars, not just its colour.
  const phrase = music.tileBar.div(8), mode = mod(floor(phrase), 3);
  const choose = n => select(n.lessThan(.5), rose, select(n.lessThan(1.5), organ, vaulted));
  const pattern = mix(choose(mode), choose(mod(mode.add(1), 3)), smoothstep(.75, 1, fract(phrase)));
  const plum = vec3(.43, .07, .50), roseTint = vec3(.75, .12, .29), brass = vec3(.65, .35, .11);
  const tint = mix(mix(plum, roseTint, petalColour), brass, note.mul(.34).add(stitches.mul(.22)));
  const detail = one(smoothstep(.45, 1.8, max(fwidth(p.x), fwidth(p.y))));
  const centre = smoothstep(.7, 2.1, abs(p.x)).mul(.7).add(.3);
  const active = music.tileOn.mul(mix(u.energy.mul(5).clamp(0, 1), u.energy, gentle)).mul(mix(float(1), float(.32), gentle));
  const fill = pattern.mul(active).mul(detail).mul(centre).mul(one(seam.mul(.94))).clamp(0, .82);
  return mix(base, base.mul(.48).add(tint), fill);
}
