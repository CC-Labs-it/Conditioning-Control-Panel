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
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms, updateAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { waterMusicLight } = await import('../render/materials/waterMusic.js');
hooks.deregister();
const roles = ['kick', 'snare', 'hats'];

function fixture(t, { pcmPeak = .8 } = {}) {
  const p = uniform(new Vector2()), u = createAudioReactiveUniforms();
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const role of roles) for (const suffix of ['Age', 'Strength', 'Count'])
    assert.ok(u[role + suffix]?.isUniformNode, 'shared instrument control ' + role + suffix);
  u.energy.value = .4; u.drive.value = .55;
  const data = u.vizTexture.image.data;
  for (let i = 0; i < 64; i++) {
    data[i * 4] = Math.round((.5 + pcmPeak * .5 * Math.sin(i * .32)) * 255);
    data[i * 4 + 1] = Math.round((.3 + .3 * Math.cos(i * .23)) * 255);
    for (let row = 1; row < 18; row++) data[(row * 64 + i) * 4] =
      Math.round((.3 + .25 * Math.sin(i * .19 + row * .42)) * 255);
  }
  const root = waterMusicLight(p, u, vec3(.8, .18, .28), vec3(.08, .6, .7));
  const reset = () => {
    for (const role of roles) { u[role].value = u[role + 'Strength'].value = 0; u[role + 'Age'].value = 9; u[role + 'Count'].value = 0; }
    u.mid.value = u.bass.value = u.treble.value = 0;
  };
  const capture = () => {
    const values = [];
    for (let y = -17.1; y <= 17.11; y += .9) for (let x = -11.7; x <= 11.71; x += .45) {
      p.value.set(x, y); const c = evaluateColour(root);
      assert.ok(c.every(v => Number.isFinite(v) && v >= 0 && v <= .6), 'bounded liquid accents');
      values.push(c[0] * .2126 + c[1] * .7152 + c[2] * .0722);
    }
    return values;
  };
  const play = (role, strength = .95) => {
    reset();
    if (role === 'mid') u.mid.value = strength;
    else { u[role].value = strength; u[role + 'Strength'].value = strength; u[role + 'Age'].value = role === 'hats' ? .015 : .18; u[role + 'Count'].value = 1; }
    return capture();
  };
  reset(); return { p, u, root, reset, capture, play };
}
const difference = (a, b) => a.filter((v, i) => Math.abs(v - b[i]) > .008).length;

test('ordinary quiet recording levels still draw bright marks and a metre-wide PCM river', t => {
  const f = fixture(t, { pcmPeak: .08 }); f.u.energy.value = f.u.drive.value = .25;
  for (const role of ['kick', 'mid', 'snare', 'hats']) {
    const values = f.play(role, .25);
    assert.ok(Math.max(...values) > .08, role + ' remains visible at .25 measured level');
    const lit = values.filter(v => v > .02).length;
    assert.ok(lit > 8 && lit / values.length < .32, role + ' retains sparse high-contrast masks');
  }
  f.play('mid', .25);
  let offCentrePeak = 0;
  for (let y = 0; y <= 32; y += .25) for (const x of [-1.3, -1.05, 1.05, 1.3]) {
    f.p.value.set(x, y); const c = evaluateColour(f.root);
    offCentrePeak = Math.max(offCentrePeak, c[0] * .2126 + c[1] * .7152 + c[2] * .0722);
  }
  assert.ok(offCentrePeak > .07, 'actual +/-0.08 PCM visibly bends beyond one metre from the centre');
});

test('bass, voice, snare and high water accents have distinct sparse shapes and luminance', t => {
  const f = fixture(t), rest = f.capture();
  const nodes = new Set(), pending = [f.root];
  while (pending.length) { const n = pending.pop(); if (nodes.has(n)) continue; nodes.add(n); pending.push(...n.getChildren()); }
  assert.ok(nodes.size < 950, 'compact water music graph: ' + nodes.size);
  assert.ok(!nodes.has(f.u.hitAge) && !nodes.has(f.u.onset), 'instruments never share the generic onset clock');
  assert.ok(rest.every(v => v === 0), 'no permanent full-water colour wash between musical elements');
  const layers = ['kick', 'mid', 'snare', 'hats'].map(role => [role, f.play(role)]);
  for (const [role, values] of layers) {
    const lit = values.filter(v => v > .015).length;
    assert.ok(lit > 8, role + ' has visible spatial extent: ' + lit);
    assert.ok(lit / values.length < .32, role + ' leaves substantial resting liquid: ' + lit / values.length);
    assert.ok(Math.max(...values) > .1, role + ' has honest bright/rest contrast');
  }
  for (let i = 0; i < layers.length; i++) for (let j = i + 1; j < layers.length; j++) {
    const a = layers[i][1], b = layers[j][1];
    const overlap = a.filter((v, k) => v > .015 && b[k] > .015).length;
    const smaller = Math.min(a.filter(v => v > .015).length, b.filter(v => v > .015).length);
    assert.ok(overlap / smaller < .8, layers[i][0] + '/' + layers[j][0] + ' do different spatial jobs');
  }
});

test('independent attack clocks move their own water marks and never restart another instrument', t => {
  const f = fixture(t);
  for (const role of roles) {
    f.play(role); f.u[role].value = 0;
    const before = f.capture(); // Isolate the last attack's clock from the separate live envelope.
    for (const other of roles.filter(r => r !== role)) { f.u[other + 'Age'].value = .03; f.u[other + 'Count'].value = 4; }
    assert.deepEqual(f.capture(), before, 'unheard other elements cannot move ' + role);
    f.u[role + 'Age'].value = .4;
    assert.ok(difference(before, f.capture()) > 8, role + ' advances or decays on its own attack clock');
  }
  f.play('kick'); f.u.kick.value = 0; f.u.kickAge.value = 2; const settled = f.capture();
  f.u.kickAge.value = 9; assert.deepEqual(f.capture(), settled, 'bass front finishes rather than periodically looping');
});

test('water instruments honor silence, motion off, paused shared controls and Gentle', t => {
  const f = fixture(t);
  updateAudioReactiveUniforms(f.u, { active: true, songS: 2, bass: .8, mid: .6, treble: .4, energy: .5,
    rawBass: .8, rawMid: .6, rawTreble: .4, rawEnergy: .5 }, .1);
  const live = f.capture();
  updateAudioReactiveUniforms(f.u, { active: true, songS: 40, rawBass: 1, rawMid: 1, rawTreble: 1 }, .1, { paused: true });
  assert.deepEqual(f.capture(), live, 'pause freezes every independent picture');
  f.u.motion.value = 0; assert.ok(f.capture().every(v => v === 0), 'motion off is authoritative'); f.u.motion.value = 1;
  f.u.energy.value = f.u.drive.value = 0; assert.ok(f.capture().every(v => v === 0), 'silent water adds no optical art');
  f.u.energy.value = .6; f.u.drive.value = .8; f.u.gentle.value = 1;
  const soft = f.capture();
  for (const role of roles) { f.u[role].value = 1; f.u[role + 'Strength'].value = 1; f.u[role + 'Age'].value = .1; f.u[role + 'Count'].value = 7; }
  f.u.vizTexture.image.data.fill(255);
  assert.deepEqual(f.capture(), soft, 'Gentle ignores all fast instrumental and PCM changes');
  assert.ok(Math.max(...soft) < .03, 'Gentle keeps a quiet wash instead of bright struck marks');
});
