import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { AFTERIMAGE_STYLES, pickAfterimageStyle, createAfterimageState } from '../render/afterimageBackdropState.js';

const hook = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createAfterimageBackdrop } = await import('../render/afterimageBackdrop.js');
const { Group, Vector3, BackSide } = await import('three/webgpu');
hook.deregister();

test('the retained ten dense designs are reachable with one random sample each', () => {
  const expected = ['velvet-aurora', 'mercury-tides', 'neon-mycelium', 'moire-drift', 'spectral-shear',
    'phase-textile', 'chromatic-turbulence', 'caustic-drift', 'curl-diffusion', 'granular-current'];
  assert.deepEqual(AFTERIMAGE_STYLES.map(style => style.id), expected);
  for (let i = 0; i < expected.length; i++) {
    let calls = 0;
    assert.equal(pickAfterimageStyle(() => { calls++; return (i + .5) / 10; }).id, expected[i]);
    assert.equal(calls, 1);
  }
  assert.equal(pickAfterimageStyle(() => 1).id, expected.at(-1));
  assert.equal(pickAfterimageStyle(() => NaN).id, expected[0]);
});

test('music has a slow sky response and no synthetic reaction to silence', () => {
  const state = createAfterimageState(), silent = { audioReactive: { active: false, bass: 1, mid: 1, treble: 1 } };
  state.update(.1, silent);
  assert.equal(state.frame.bass, 0); assert.equal(state.frame.mid, 0); assert.equal(state.frame.high, 0);
  const loud = { audioReactive: { active: true, bass: 1, mid: .5, treble: .8 } };
  state.update(.1, loud);
  assert.ok(state.frame.bass > 0 && state.frame.bass < .15, 'no beat-sized whole-sky flash');
  for (let i = 0; i < 120; i++) state.update(.1, silent);
  assert.equal(state.frame.bass, 0);
});

test('pause, frozen audio and Motion Off hold the background, Gentle lowers motion and energy', () => {
  const audioReactive = { active: true, bass: 1, mid: 1, treble: 1 }, state = createAfterimageState();
  state.update(.1, { audioReactive });
  const playing = { ...state.frame };
  for (const view of [{ paused: true, audioReactive }, { audioReactive: { ...audioReactive, frozen: true } }]) {
    state.update(1, view); assert.deepEqual(state.frame, playing);
  }
  state.update(.1, { audioReactive, reducedMotion: true });
  const still = { ...state.frame };
  for (let i = 0; i < 20; i++) state.update(.1, { audioReactive, reducedMotion: true });
  assert.deepEqual(state.frame, still); assert.equal(still.bass, 0); assert.equal(still.time, playing.time);
  const full = createAfterimageState(), gentle = createAfterimageState();
  for (let i = 0; i < 20; i++) { full.update(.1, { audioReactive }); gentle.update(.1, { audioReactive, gentle: true }); }
  assert.ok(gentle.frame.time - 7 < (full.frame.time - 7) * .21);
  assert.ok(gentle.frame.bass < full.frame.bass * .3);
});

test('every selected backdrop stays a bounded background across resets and disposes exactly once', () => {
  for (const phone of [false, true]) for (const style of AFTERIMAGE_STYLES) {
    let randomCalls = 0;
    const backdrop = createAfterimageBackdrop({ phone, style: style.id, random: () => { randomCalls++; return .5; } });
    const root = new Group(); root.add(backdrop.object);
    assert.equal(backdrop.style, style.id); assert.equal(randomCalls, 0);
    const mesh = backdrop.object;
    assert.equal(mesh.children.length, 0); assert.equal(mesh.material.side, BackSide);
    assert.equal(mesh.material.depthWrite, false); assert.equal(mesh.material.fog, false);
    assert.equal(mesh.layers.mask, 1, 'not added to water capture');
    assert.ok(mesh.geometry.attributes.position.count < 1300);
    const playerPos = new Vector3(100, 25, -80);
    backdrop.update(.1, { playerPos }); assert.deepEqual(mesh.position.toArray(), playerPos.toArray());
    const time = backdrop.uniforms.time.value;
    backdrop.update(.1, { paused: true }); assert.equal(backdrop.uniforms.time.value, time);
    backdrop.reset(); assert.equal(backdrop.style, style.id); assert.equal(backdrop.uniforms.time.value, 7);
    let geometryDisposals = 0, materialDisposals = 0;
    mesh.geometry.addEventListener('dispose', () => geometryDisposals++);
    mesh.material.addEventListener('dispose', () => materialDisposals++);
    backdrop.dispose(); backdrop.dispose(); backdrop.update(.1, {}); backdrop.reset();
    assert.equal(geometryDisposals, 1); assert.equal(materialDisposals, 1); assert.equal(root.children.length, 0);
  }
});
