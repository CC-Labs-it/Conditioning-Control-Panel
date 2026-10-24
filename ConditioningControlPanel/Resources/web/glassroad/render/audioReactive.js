// All paths are built during scene construction, including silence. Updates only change uniforms.
import { uniform, float, vec2, vec3, sin, cos, abs, pow, mix, smoothstep, floor, fract, hash, length, fwidth, max } from 'three/tsl';
import { AUDIO_REACTIVE as L, stepAudioReactive } from './audioReactiveMath.js';
const UNIFORM_NAMES = Object.freeze(['bass', 'mid', 'treble', 'energy', 't', 'gentle']);

export function createAudioReactiveUniforms() {
  const u = { bass: uniform(0), mid: uniform(0), treble: uniform(0), energy: uniform(0), t: uniform(0), gentle: uniform(0) };
  // The state is allocated once, outside the render loop.
  u.state = { bass: 0, mid: 0, treble: 0, energy: 0, t: 0, gentle: 0 };
  return u;
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
