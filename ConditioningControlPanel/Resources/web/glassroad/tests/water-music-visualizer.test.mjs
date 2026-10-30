import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { Vector2 } = await import('three');
const { uniform, vec3 } = await import('three/tsl');
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { waterMusicLight } = await import('../render/materials/waterMusic.js');
hooks.deregister();

function fixture(t) {
  const p = uniform(new Vector2()), u = createAudioReactiveUniforms();
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const key of ['bass', 'mid', 'treble', 'energy', 'drive']) u[key].value = .4;
  u.hitAge.value = .22; u.hitStrength.value = .75; u.kick.value = .7;
  const root = waterMusicLight(p, u, vec3(.72, .12, .24), vec3(.04, .52, .61));
  const setData = ({ wave = 0, spectrum = 0, history = 0 } = {}) => {
    const data = u.vizTexture.image.data;
    for (let i = 0; i < 64; i++) {
      data[i * 4] = Math.round((.5 + .5 * wave * Math.sin(i * .29)) * 255);
      data[i * 4 + 1] = Math.round(spectrum * (.5 + .5 * Math.cos(i * .17)) * 255);
      for (let row = 1; row < 18; row++) data[(row * 64 + i) * 4] =
        Math.round(history * (.5 + .5 * Math.sin(i * .25 + row * .51)) * 255);
    }
  };
  const capture = () => {
    const values = [];
    for (let y = -18; y <= 18; y += 3) for (let x = -10; x <= 10; x += 2) {
      p.value.set(x, y); values.push(...evaluateColour(root));
    }
    return values;
  };
  return { u, root, setData, capture };
}
const changed = (a, b, tolerance = .002) => a.filter((v, i) => Math.abs(v - b[i]) > tolerance).length;

test('actual water shader independently draws measured waveform, spectrum and spectral history', t => {
  const f = fixture(t); f.setData(); const rest = f.capture();
  for (const channel of ['wave', 'spectrum', 'history']) {
    f.setData({ [channel]: .8 });
    assert.ok(changed(rest, f.capture()) > 12, channel + ' changes a visible spatial field');
  }
  f.setData({ wave: .9, spectrum: .8, history: .75 });
  for (const v of f.capture()) assert.ok(Number.isFinite(v) && v >= 0 && v <= .6, 'bounded palette light');
});

test('real attacks make travelling water fronts that settle instead of looping on a timer', t => {
  const f = fixture(t); f.setData({ wave: .3, spectrum: .2, history: .2 });
  f.u.kick.value = 0; f.u.kickStrength.value = .9;
  f.u.kickAge.value = .1; const strike = f.capture();
  f.u.kickAge.value = .4; const crossing = f.capture();
  assert.ok(changed(strike, crossing) > 12, 'a real hit moves broad physical-metre rings');
  f.u.kickAge.value = 2; const settled = f.capture();
  f.u.kickAge.value = 9;
  assert.deepEqual(f.capture(), settled, 'the front dies rather than replaying on elapsed song time');
  const nodes = new Set(), queue = [f.root];
  while (queue.length) { const n = queue.pop(); if (nodes.has(n)) continue; nodes.add(n); queue.push(...n.getChildren()); }
  assert.ok(nodes.size < 950, 'water music stays within one compact material graph: ' + nodes.size);
  assert.ok(!nodes.has(f.u.dnaPhase) && !nodes.has(f.u.dnaRadius), 'no DNA animation remains in the visible graph');
});

test('silent water adds no picture; Gentle excludes fast data and keeps a proportional soft wash', t => {
  const f = fixture(t); f.setData({ wave: .9, spectrum: .8, history: .7 });
  f.u.energy.value = f.u.drive.value = 0;
  assert.ok(f.capture().every(v => v === 0), 'silence leaves the physical liquid intact');
  f.u.gentle.value = 1; f.u.energy.value = 1; f.u.drive.value = .8;
  const soft = f.capture();
  f.setData({ wave: -.9, spectrum: .1, history: .2 });
  f.u.hitAge.value = .5; f.u.hitStrength.value = 1; f.u.kick.value = 1;
  assert.deepEqual(f.capture(), soft, 'fast waveform, spectra and hit changes cannot animate Gentle');
  f.u.energy.value = .1;
  const onset = f.capture();
  for (let i = 0; i < soft.length; i++) assert.ok(Math.abs(onset[i] - soft[i] * .1) < 1e-12,
    'Gentle colour respects the full slow envelope rise');
});
