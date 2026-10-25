// All paths are built during scene construction, including silence. Updates only change uniforms.
import { uniform, float, vec2, vec3, sin, cos, abs, pow, mix, smoothstep, floor, fract, hash, length, fwidth, max, min, mod, step } from 'three/tsl';
import { AUDIO_REACTIVE as L, stepAudioReactive } from './audioReactiveMath.js';
const UNIFORM_NAMES = Object.freeze(['bass', 'mid', 'treble', 'energy', 't', 'gentle', ...Array.from({ length: 8 }, (_, i) => 'note' + i)]);

export function createAudioReactiveUniforms() {
  const u = { bass: uniform(0), mid: uniform(0), treble: uniform(0), energy: uniform(0), t: uniform(0), gentle: uniform(0) };
  // The state is allocated once, outside the render loop.
  u.state = { bass: 0, mid: 0, treble: 0, energy: 0, t: 0, gentle: 0 };
  for (let i = 0; i < 8; i++) { u['note' + i] = uniform(0); u.state['note' + i] = 0; }
  return u;
}

/** Fixed green/graphite panels take turns on the heard beat, with measured bands shaping each fill. */
export function mainframeTileLight(p, u, music, trueEdge, base) {
  const size = vec2(4, 6), cell = floor(p.div(size)), q = fract(p.div(size)).sub(.5);
  const lane = mod(cell.x.add(cell.y.mul(3)), 8);
  let note = float(0);
  for (let i = 0; i < 8; i++) note = note.add(float(1).sub(step(.5, abs(lane.sub(i)))).mul(u['note' + i]));
  const pixel = max(fwidth(q), vec2(.001));
  const inset = float(1).sub(smoothstep(.42, .48, max(abs(q.x), abs(q.y))));
  const bank = mod(cell.x.add(cell.y.mul(2)), 4);
  const clock = music.tileBeat.mul(mix(float(1),float(.25),u.gentle));
  const phase = fract(clock.sub(bank).div(4));
  // Four banks crossfade on successive beats. Each individual panel rises for >=300ms at 96 BPM.
  const beatDistance = min(phase,float(1).sub(phase)).mul(4);
  const pulse = float(1).sub(smoothstep(0,.7,beatDistance));
  const ringAt = pulse.mul(.21).add(.17), ring = abs(abs(q.x).add(abs(q.y)).sub(ringAt));
  const trace = float(1).sub(smoothstep(.025, max(pixel.x, pixel.y).mul(1.5).add(.055), ring));
  const shade = hash(cell.x.add(cell.y.mul(71.3)));
  const tint = mix(vec3(.015,.42,.035),vec3(.32,.85,.025),shade);
  const gold = smoothstep(.94,1,sin(music.tileBar.mul(.196))).mul(.14);
  const green = mix(tint,vec3(.62,.48,.055),gold);
  const active = music.tileOn.mul(u.energy.mul(6).clamp(0,1));
  const drive = pulse.mul(.85).add(note.mul(.38)).mul(active).mul(gain(u));
  const graphite = base.mul(mix(float(.14),float(.78),step(.5,shade)));
  const lit = graphite.add(green.mul(drive)).add(green.mul(trace).mul(u.mid).mul(.32).mul(gain(u)));
  return mix(base,lit,inset.mul(smoothstep(.8,2.4,trueEdge)));
}

/** Paired strands, crossbars and travelling beads, anchored in the liquid's own coordinates. */
export function waterHelixLight(p, u, tint) {
  const time = u.t.mul(mix(float(.5), float(.18), u.gentle)), lane = floor(p.x.div(9));
  const x = mod(p.x.add(4.5), 9).sub(4.5), phase = p.y.mul(.32).sub(time).add(lane.mul(1.7));
  const wave = sin(phase).mul(u.mid.mul(.65).add(2.1));
  const width = max(float(.075), fwidth(x).mul(1.2));
  const strand = d => float(1).sub(smoothstep(width, width.add(.12), abs(d)));
  const a = strand(x.sub(wave)), b = strand(x.add(wave));
  const rung = float(1).sub(smoothstep(.06, .14, abs(sin(phase.mul(3))))).mul(float(1).sub(smoothstep(abs(wave), abs(wave).add(.15), abs(x))));
  const bead = pow(sin(phase.sub(time.mul(.25))).mul(.5).add(.5), 12).mul(a.add(b));
  const level = u.mid.mul(1.1).add(u.treble.mul(.65)).add(u.bass.mul(.3)).mul(gain(u));
  return tint.mul(a.add(rung.mul(.4))).add(mix(tint,vec3(.7,.9,1),.55).mul(b)).add(vec3(.65,1,.9).mul(bead).mul(.5)).mul(level);
}

export function updateAudioReactiveUniforms(u, signal, dt, options) {
  stepAudioReactive(u.state, signal, dt, options);
  for (const name of UNIFORM_NAMES) u[name].value = u.state[name];
}

const gain = u => mix(float(1), float(L.gentleGain), u.gentle);
export function reactiveTint(pal, trackId) {
  if (trackId === 'boudoir') return vec3(0.78, 0.46, 0.31);
  if (trackId === 'keep') return vec3(0.76, 0.5, 0.19);
  if (trackId === 'mainframe') return vec3(0.14, 0.66, 0.64);
  return pal.col(0).mul(0.6).add(pal.col(1).mul(0.4));
}

/** Sparse inlays add light above the authored finish; the steady navigation edge remains above them. */
export function reactiveRoadLight({ p, trueEdge, distance, u, tint }) {
  const g = gain(u), shoulder = float(1).sub(smoothstep(0.7, 2.8, trueEdge));
  const interior = smoothstep(0.6, 1.3, trueEdge);
  const reach = float(1).sub(smoothstep(70, 115, distance));
  const packet = pow(sin(p.y.mul(0.28).sub(u.t.mul(0.72)).add(abs(p.x).mul(0.08))).mul(0.5).add(0.5).max(0), 6);
  const seamD = abs(sin(p.x.mul(Math.PI / 2.4)));
  const seamWidth = max(float(0.045), fwidth(seamD).mul(0.8));
  const seam = float(1).sub(smoothstep(0, seamWidth, seamD));
  const gridFade = float(1).sub(smoothstep(0.16, 0.55, max(fwidth(p.x), fwidth(p.y))));
  const cell = floor(p.div(1.7)), seed = hash(cell.x.add(cell.y.mul(137.1)));
  const offset = vec2(seed, hash(seed.mul(127.3))).sub(0.5).mul(0.55);
  const spotD = length(fract(p.div(1.7)).sub(0.5).sub(offset));
  const dotWidth = max(float(0.035), fwidth(spotD).mul(1.5));
  const spot = float(1).sub(smoothstep(0, dotWidth, spotD)).mul(float(0.035).div(dotWidth).min(1));
  const sparkle = spot.mul(smoothstep(0.9, 0.99, seed)).mul(gridFade)
    .mul(sin(u.t.mul(0.6).add(seed.mul(6.28))).mul(0.25).add(0.75));
  const bass = shoulder.mul(u.bass).mul(0.3);
  const mid = shoulder.mul(0.7).add(seam.mul(gridFade).mul(interior).mul(0.3)).mul(packet).mul(u.mid).mul(0.38);
  const treble = sparkle.mul(u.treble).mul(0.5);
  return tint.mul(bass.add(mid).add(treble)).mul(reach).mul(g);
}

/** Analytic gradient of two slow waves, layered over the transported physical wake normals. */
export function reactiveWaterNodes(p, u) {
  const a = p.y.mul(L.waveAlong).sub(u.t.mul(L.waveSpeed)).add(sin(p.x.mul(0.19)).mul(0.5));
  const b = p.x.mul(L.waveAcross).add(p.y.mul(0.17)).add(u.t.mul(0.43));
  const amp = u.bass.mul(L.slopeGain).mul(gain(u));
  const cross = u.mid.mul(L.slopeGain * 0.55).mul(gain(u));
  const slope = vec2(cos(a).mul(cos(p.x.mul(0.19))).mul(0.095).mul(amp).add(cos(b).mul(L.waveAcross).mul(cross)),
    cos(a).mul(L.waveAlong).mul(amp).add(cos(b).mul(0.17).mul(cross)));
  const caustic = pow(sin(a).mul(sin(b)).mul(0.5).add(0.5).max(0), 5);
  const light = caustic.mul(u.mid.mul(0.32).add(u.bass.mul(0.18))).mul(gain(u));
  const crest = pow(cos(a).mul(0.5).add(0.5).max(0), 7).mul(u.treble).mul(0.23).mul(gain(u));
  return { slope, light, crest };
}
