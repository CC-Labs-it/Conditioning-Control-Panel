import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { phraseReboundPose } from '../render/kartPose.js';
import { MeshoptDecoder } from '../vendor/three/addons/libs/meshopt_decoder.module.js';

test('phrase recoil compresses, stretches, separates three layers and settles with bounded Gentle motion', () => {
  assert.ok(phraseReboundPose(-.04).squash > 0);
  assert.ok(phraseReboundPose(.04).squash > .2);
  assert.ok(phraseReboundPose(.26).squash < -.15);
  for (let t = -.3; t <= 1.3; t += 1 / 144) {
    const p = phraseReboundPose(t), gentle = phraseReboundPose(t, true);
    assert.ok(p.riderM >= p.cupM && p.cupM >= p.saucerM);
    assert.ok(p.riderM <= .37 && p.cupM <= .10 && p.saucerM >= -.08);
    for (const key of Object.keys(p)) assert.ok(Math.abs(gentle[key] - p[key] * .3) < 1e-12);
  }
  for (const t of [null, NaN, -1, 2]) assert.deepEqual(phraseReboundPose(t), { squash: 0, cupM: 0, riderM: 0, saucerM: 0 });
});

test('accepted R4 native joints separate in model-up and restore the actual animated assembly', async () => {
  const hooks = registerHooks({ resolve(s, context, next) {
    if (s === 'three' || s === 'three/webgpu') return { url: new URL('../vendor/three/build/three.core.js', import.meta.url).href, shortCircuit: true };
    return next(s, context);
  }});
  try {
    const { GLTFLoader } = await import('../vendor/three/addons/loaders/GLTFLoader.js');
    const { AnimationMixer, Vector3 } = await import('../vendor/three/build/three.core.js');
    const { createPhrasePieces } = await import('../render/phrasePieces.js');
    await MeshoptDecoder.ready;
    const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
    loader.register(parser => ({ name: 'geometryOnlyTest', beforeRoot() {
      function strip(value) {
        if (!value || typeof value !== 'object') return;
        for (const key of Object.keys(value)) if (key.endsWith('Texture')) delete value[key]; else strip(value[key]);
      }
      for (const m of parser.json.materials || []) strip(m);
    }}));
    const bytes = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
    const gltf = await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
    const model = gltf.scene, parts = createPhrasePieces(model), mixer = new AnimationMixer(model);
    assert.equal(parts.count, 3);
    mixer.clipAction(gltf.animations.find(c => c.name === 'hop_squash')).play();
    mixer.update(.1); model.updateMatrixWorld(true);
    const names = ['cup_body', 'rider_mount', 'saucer_body'];
    const nodes = names.map(n => model.getObjectByName(n));
    const local = nodes.map(n => n.position.clone()), base = nodes.map(n => n.getWorldPosition(new Vector3()));
    const pose = phraseReboundPose(.575);
    parts.update(pose); model.updateMatrixWorld(true);
    for (let i = 0; i < 3; i++) {
      const delta = nodes[i].getWorldPosition(new Vector3()).sub(base[i]);
      assert.ok(Math.abs(delta.x) < 1e-6 && Math.abs(delta.z) < 1e-6, names[i] + ' uses model vertical');
      assert.ok(Math.abs(delta.y - pose[['cupM', 'riderM', 'saucerM'][i]]) < 1e-6);
    }
    parts.update(pose); model.updateMatrixWorld(true);
    const second = nodes.map(n => n.getWorldPosition(new Vector3()));
    parts.beforePose();
    nodes.forEach((n, i) => assert.ok(n.position.distanceTo(local[i]) < 1e-12));
    parts.update(pose); model.updateMatrixWorld(true);
    nodes.forEach((n, i) => assert.ok(n.getWorldPosition(new Vector3()).distanceTo(second[i]) < 1e-9, 'no accumulated offset'));
    parts.update(pose, false);
    nodes.forEach((n, i) => assert.ok(n.position.distanceTo(local[i]) < 1e-12, 'other vehicles keep their complete assembly'));
    parts.update(pose);
    const updateWorld = model.updateMatrixWorld;
    let matrixCalls = 0;
    model.updateMatrixWorld = function (...args) { matrixCalls++; return updateWorld.apply(this, args); };
    parts.update(phraseReboundPose(null));
    nodes.forEach((n, i) => assert.ok(n.position.distanceTo(local[i]) < 1e-12, 'inactive pose restores the previous separation'));
    for (let i = 0; i < 60; i++) parts.update(phraseReboundPose(null));
    assert.equal(matrixCalls, 0, 'inactive karts never traverse world matrices for a phrase');
    model.updateMatrixWorld = updateWorld;
  } finally { hooks.deregister(); }
});
