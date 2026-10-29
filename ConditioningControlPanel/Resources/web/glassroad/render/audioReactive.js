// All paths are built during scene construction, including silence. Updates only change uniforms.
import { uniform, float, vec2, vec3, sin, cos, abs, pow, mix, smoothstep, floor, fract, hash, length, fwidth, max, min, mod, step } from 'three/tsl';
import { AUDIO_REACTIVE as L, stepAudioReactive } from './audioReactiveMath.js';
import { createWaterDnaFrame, waterDnaFrame } from './waterDnaMath.js';
import { finalLapMods } from './worldFeel.js';
import { DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, ClampToEdgeWrapping, NoColorSpace } from 'three/webgpu';
import { createMusicVisualData, stepMusicVisualData, VISUAL_DATA, INSTRUMENT_FIELDS } from './musicVisualData.js';
import { createMusicPerformance, stepMusicPerformance, PERFORMANCE_FIELDS } from './musicPerformance.js';
import { markRows } from './dataTextureRows.js';
const UNIFORM_NAMES = Object.freeze(['bass', 'mid', 'treble', 'energy', 't', 'gentle', ...Array.from({ length: 8 }, (_, i) => 'note' + i)]);
const VISUAL_NAMES = Object.freeze(['drive', 'kick', 'snare', 'hats', 'onset', 'hitAge', 'hitStrength', 'section',
  ...INSTRUMENT_FIELDS.flatMap(fields => [fields.age, fields.strength, fields.count])]);

export function createAudioReactiveUniforms() {
  const u = { bass: uniform(0), mid: uniform(0), treble: uniform(0), energy: uniform(0), t: uniform(0), gentle: uniform(0) };
  // The state is allocated once, outside the render loop.
  u.state = { bass: 0, mid: 0, treble: 0, energy: 0, t: 0, gentle: 0 };
  for (let i = 0; i < 8; i++) { u['note' + i] = uniform(0); u.state['note' + i] = 0; }
  u.dna = createWaterDnaFrame();
  u.dnaPhase = uniform(0); u.dnaRadius = uniform(2.1); u.dnaHue = uniform(.5); u.dnaPulse = uniform(0);
  u.visual = createMusicVisualData();
  for (const key of VISUAL_NAMES) u[key] = uniform(u.visual[key]);
  u.performance = createMusicPerformance();
  for (const key of PERFORMANCE_FIELDS) u[key] = uniform(u.performance[key]);
  u.motion = uniform(1);
  const tex = new DataTexture(u.visual.data, VISUAL_DATA.width, VISUAL_DATA.height, RGBAFormat, UnsignedByteType);
  tex.minFilter = tex.magFilter = LinearFilter; tex.wrapS = tex.wrapT = ClampToEdgeWrapping;
  tex.colorSpace = NoColorSpace; tex.generateMipmaps = false; tex.needsUpdate = true;
  u.vizTexture = tex;
  return u;
}
export function disposeAudioReactiveUniforms(u) { u?.vizTexture?.dispose(); }

export { mainframeTileLight } from './materials/mainframeMusicFloor.js';

/** Paired strands, crossbars and travelling beads, anchored in the liquid's own coordinates. */
export function waterHelixLight(p, u, tint, accent = tint) {
  const lane = floor(p.x.add(4.5).div(9));
  const x = mod(p.x.add(4.5), 9).sub(4.5), phase = p.y.mul(.32).sub(u.dnaPhase).add(lane.mul(1.7));
  const wave = sin(phase).mul(u.dnaRadius);
  const width = max(u.dnaPulse.mul(.10).add(.09), fwidth(x).mul(1.2));
  const strand = d => float(1).sub(smoothstep(width, width.add(.12), abs(d)));
  const a = strand(x.sub(wave)), b = strand(x.add(wave));
  const rung = float(1).sub(smoothstep(.06, .14, abs(sin(phase.mul(3))))).mul(float(1).sub(smoothstep(abs(wave), abs(wave).add(.15), abs(x))));
  const bead = pow(sin(phase.sub(u.dnaPhase.mul(.5))).mul(.5).add(.5), 8).mul(a.add(b));
  const colour = u.dnaHue.mul(.72).add(sin(lane.mul(1.7)).mul(.14)).add(.14).clamp(0,1);
  const strandA = mix(tint, accent, colour), strandB = mix(tint, accent, float(1).sub(colour));
  const level = u.mid.mul(.7).add(u.treble.mul(.35)).add(u.bass.mul(.35)).mul(gain(u));
  return strandA.mul(a.add(rung.mul(.35).mul(u.dnaPulse.mul(.5).add(.5))))
    .add(strandB.mul(b)).add(mix(strandA, strandB, .5).mul(bead).mul(.35)).mul(level).min(.7);
}

const PULSED = new Set(['bass', 'mid', 'treble', 'energy', ...Array.from({ length: 8 }, (_, i) => 'note' + i)]);

export function updateAudioReactiveUniforms(u, signal, dt, options) {
  stepAudioReactive(u.state, signal, dt, options);
  // feel wave 2026-10-26 (lane C): the final-lap wake (options.finalLap 0..1, motion share already in) pulses harder
  const wake = finalLapMods(options?.finalLap || 0, options?.feel).reactiveMul;
  for (const name of UNIFORM_NAMES) u[name].value = PULSED.has(name) ? u.state[name] * wake : u.state[name];
  waterDnaFrame(u.dna, u.state, signal, options?.song, options);
  u.dnaPhase.value = u.dna.phase; u.dnaRadius.value = u.dna.radius;
  u.dnaHue.value = u.dna.hue; u.dnaPulse.value = u.dna.pulse;
  if (stepMusicVisualData(u.visual, signal, dt, options)) { markRows(u.vizTexture, 0, VISUAL_DATA.height - 1); u.vizTexture.needsUpdate = true; }
  for (const key of VISUAL_NAMES) u[key].value = u.visual[key];
  stepMusicPerformance(u.performance, signal, dt, options);
  for (const key of PERFORMANCE_FIELDS) u[key].value = u.performance[key];
  if (!options?.paused && !signal?.frozen) u.motion.value = options?.motion === 'off' ? 0 : options?.motion === 'less' ? .5 : 1;
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
