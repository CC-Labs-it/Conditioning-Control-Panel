import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
import { AUDIO_REACTIVE as L, stepAudioReactive, reactiveWaterSlope, perceptualAudioBand } from '../render/audioReactiveMath.js';
import { loadTrack } from '../track/load.js';

const rest = () => ({ bass: 0, mid: 0, treble: 0, energy: 0, t: 0, gentle: 0 });
const loud = { active: true, bass: 1, mid: 1, treble: 1, energy: 1, songS: 18 };

test('display compression reveals measured quiet bands while preserving bounds, silence and slow onset', () => {
  for (const value of [NaN, Infinity, -Infinity, -1, 0]) assert.equal(perceptualAudioBand(value), 0);
  assert.equal(perceptualAudioBand(1), 1); assert.equal(perceptualAudioBand(99), 1);
  for (let v = 0.001; v < 1; v += 0.017) assert.ok(perceptualAudioBand(v) >= v && perceptualAudioBand(v) <= 1);
  const state = rest(), measured = { active: true, bass: .15, mid: .109, treble: .078, energy: .517, songS: 12 };
  stepAudioReactive(state, measured, 1 / 60);
  assert.ok(state.bass < Math.sqrt(.15) * .06, 'compression precedes the 320 ms attack');
  for (let i = 0; i < 600; i++) stepAudioReactive(state, measured, 1 / 60);
  assert.ok(Math.abs(state.bass - Math.sqrt(.15)) < 1e-6);
  assert.ok(Math.abs(state.mid - Math.sqrt(.109)) < 1e-6);
  assert.ok(Math.abs(state.treble - Math.sqrt(.078)) < 1e-6);
  assert.ok(Math.abs(state.energy - .517) < 1e-6, 'total energy remains the measured energy');
});

test('all visual bands have a >=300 ms attack, bounded input and a soft silence release', () => {
  const state = rest();
  stepAudioReactive(state, loud, 1 / 60);
  assert.ok(state.bass > 0 && state.bass < 0.06, 'a loud onset cannot flash in one frame');
  for (let i = 1; i < 19; i++) stepAudioReactive(state, loud, 1 / 60);
  assert.ok(state.bass > 0.6 && state.bass < 0.65);
  const peak = state.bass;
  stepAudioReactive(state, { ...loud, active: false }, 1 / 60);
  assert.ok(state.bass < peak && state.bass > peak * 0.95);
  for (let i = 0; i < 900; i++) stepAudioReactive(state, null, 1 / 60);
  assert.deepEqual(state, { ...rest(), t: 18 });
  stepAudioReactive(state, { ...loud, bass: Infinity, mid: -1, treble: NaN, energy: 99 }, 0.1);
  assert.equal(state.bass, 0); assert.equal(state.mid, 0); assert.equal(state.treble, 0);
  assert.ok(state.energy > 0 && state.energy < 1);
});

test('pause freezes every visual band and clock while seeks use real song time', () => {
  const state = rest(); stepAudioReactive(state, loud, 0.1);
  const before = { ...state };
  stepAudioReactive(state, { ...loud, songS: 100 }, 0.1, { paused: true, gentle: true });
  assert.deepEqual(state, before);
  stepAudioReactive(state, { ...loud, frozen: true, songS: 18 }, 0.1);
  assert.deepEqual(state, before, 'browser audio interruptions hold the second visual envelope too');
  stepAudioReactive(state, { ...loud, songS: 2 }, 0.1);
  assert.equal(state.t, 2);
  stepAudioReactive(state, null, 0.1); assert.equal(state.t, 2, 'silence invents no beat clock');
  const gentle = rest(); stepAudioReactive(gentle, loud, 0.1, { gentle: true });
  assert.ok(gentle.bass < before.bass); assert.equal(gentle.gentle, 1);
});

test('optical music waves have coherent gradients, bounded slopes and zero silent disturbance', () => {
  const height = (x, s, t) => Math.sin(s * L.waveAlong - t * L.waveSpeed + Math.sin(x * 0.19) * 0.5) * L.slopeGain
    + Math.sin(x * L.waveAcross + s * 0.17 + t * 0.43) * L.slopeGain * 0.55;
  const h = 1e-4;
  for (let s = -20; s <= 20; s += 2.3) for (let x = -10; x <= 10; x += 1.7) {
    const t = 17.2, slope = reactiveWaterSlope(x, s, t, 1, 1);
    assert.ok(Math.abs(slope[0] - (height(x + h, s, t) - height(x - h, s, t)) / (2 * h)) < 1e-9);
    assert.ok(Math.abs(slope[1] - (height(x, s + h, t) - height(x, s - h, t)) / (2 * h)) < 1e-9);
    assert.ok(Math.hypot(...slope) < 0.12, 'physical wake normals remain dominant');
    assert.ok(reactiveWaterSlope(x, s, t, 0, 0).every(value => value === 0));
    const gentle = reactiveWaterSlope(x, s, t, 1, 1, true);
    assert.ok(Math.abs(gentle[0] - slope[0] * L.gentleGain) < 1e-12);
  }
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { vec3 } = await import('three/tsl');
const { createAudioReactiveUniforms, updateAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { createRoadMaterial } = await import('../render/materials/road.js');
const { buildWaterVolume } = await import('../render/waterVolume.js');
const { createWaterfall } = await import('../render/waterfall.js');
const { buildMainframe } = await import('../render/mainframe.js');
hooks.deregister();

function graph(root) {
  const nodes = new Set(), pending = [root];
  while (pending.length) {
    const node = pending.pop(); if (nodes.has(node)) continue; nodes.add(node);
    for (const child of node.getChildren()) pending.push(child);
  }
  return nodes;
}

test('one prewarmed envelope drives real road, water skin, depth and waterfall graphs without resources', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  const pal = { col: i => vec3(0.2 + i * 0.1, 0.5, 0.7) };
  const road = createRoadMaterial(pal, null, track), reactive = road.uniforms.reactive;
  const volume = buildWaterVolume(track, pal, null, { reactive });
  const fall = createWaterfall(track, pal, { reactive });
  t.after(() => { road.material.dispose(); volume.dispose(); fall.dispose(); });
  assert.equal(volume.uniforms.reactive, reactive); assert.equal(fall.uniforms.reactive, reactive);
  // Static drain groups have no material graph; the three liquid surfaces still do.
  for (const name of ['water-surface-raised', 'water-visible-depth', 'water-spills-into-void'])
    assert.ok(volume.object.getObjectByName(name)?.material?.colorNode, name);
  const materials = [road.material, ...volume.object.children.filter(m => m.material?.colorNode).map(m => m.material),
    ...fall.object.children.filter(m => m.material?.colorNode).map(m => m.material)];
  const roots = materials.map(m => m.colorNode), before = roots.map(graph);
  for (const nodes of before.slice(0, 4)) {
    assert.ok(nodes.has(reactive.bass) && nodes.has(reactive.mid));
    assert.ok(nodes.size < 8000, 'response stays a small addition to existing material graphs');
  }
  assert.ok(before[0].has(reactive.treble) && before[1].has(reactive.treble));
  for (const control of ['kick', 'snare', 'hats', 'hitAge', 'hitStrength'])
    assert.ok(graph(volume.object.getObjectByName('water-surface-raised').material.colorNode).has(reactive[control]),
      'visible raised water consumes ' + control);
  const resources = before.map(nodes => [...nodes].filter(n => n.isTextureNode).length);
  for (let i = 0; i < 60; i++) updateAudioReactiveUniforms(reactive, { ...loud, songS: i / 60 }, 1 / 60);
  assert.ok(reactive.bass.value > 0.95); assert.equal(reactive.t.value, 59 / 60);
  materials.forEach((m, i) => { assert.equal(m.colorNode, roots[i]);
    assert.equal([...graph(m.colorNode)].filter(n => n.isTextureNode).length, resources[i]); });
  const snapshot = Object.fromEntries(Object.entries(reactive).filter(([k]) => k !== 'state').map(([k, v]) => [k, v.value]));
  updateAudioReactiveUniforms(reactive, null, 0.1, { paused: true });
  assert.deepEqual(Object.fromEntries(Object.entries(reactive).filter(([k]) => k !== 'state').map(([k, v]) => [k, v.value])), snapshot);
  assert.deepEqual(Object.keys(createAudioReactiveUniforms()).sort(), Object.keys(reactive).sort());
});

test('all authored track floors retain their finish and the reservoir shares the same music envelope', t => {
  const pal = { col: i => vec3(0.2 + i * 0.1, 0.5, 0.7) };
  for (const id of ['threshold', 'keep', 'mainframe', 'boudoir']) {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    const road = createRoadMaterial(pal, null, track); t.after(() => road.material.dispose());
    const nodes = graph(road.material.colorNode);
    assert.ok(nodes.has(road.uniforms.reactive.bass), id);
    assert.equal(road.material.positionNode, null, id + ' never deforms the driving deck');
    if (id === 'mainframe') {
      assert.equal(road.material.transparent, false);
      assert.ok(nodes.has(road.uniforms.music.tileBeat), 'tile lighting follows the heard beat grid');
      assert.ok(nodes.has(road.uniforms.music.tileBar), 'accent cycle follows the same song clock');
      assert.ok(nodes.has(road.uniforms.reactive.note0), 'measured melodic energy still shapes the tiles');
      const world = buildMainframe(track, null, { reactive: road.uniforms.reactive }); t.after(() => world.dispose());
      const reservoir = world.group.getObjectByName('mainframe-reservoir-surface');
      const waterNodes = graph(reservoir.material.colorNode);
      for (const band of ['bass', 'mid', 'treble', 't', 'kick', 'snare', 'hats', 'hitAge', 'hitStrength'])
        assert.ok(waterNodes.has(road.uniforms.reactive[band]));
      assert.equal(reservoir.material.positionNode, null, 'reservoir waterline stays shared with its contacts');
    }
  }
});
