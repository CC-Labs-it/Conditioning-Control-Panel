import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  if (specifier.startsWith('three/addons/')) return { url: new URL('../vendor/three/addons/' + specifier.slice(13), import.meta.url).href, shortCircuit: true };
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { float, vec3, uv, uniform } = await import('three/tsl');
const { createAudioReactiveUniforms, updateAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { createRoadMaterial } = await import('../render/materials/road.js');
const { keepMusicFloor } = await import('../render/materials/keepMusicFloor.js');
const { applyKeepRoad } = await import('../render/keepDressing.js');
hooks.deregister();

const musicUniforms = () => ({ tileBeat: uniform(0), tileBar: uniform(0), tileOn: uniform(0) });

function graph(root) {
  const nodes = new Set(), pending = [root];
  while (pending.length) {
    const node = pending.pop(); if (nodes.has(node)) continue; nodes.add(node);
    for (const child of node.getChildren()) pending.push(child);
  }
  return nodes;
}

test('Keep visible masonry responds to all heard bands and the shared song beat, with no extra resource', () => {
  const reactive = createAudioReactiveUniforms(), music = musicUniforms(), mortar = float(.5);
  const floor = keepMusicFloor({ p: uv(), base: vec3(.1, .03, .12), seam: mortar, reactive, music });
  const nodes = graph(floor);
  for (const name of ['bass', 'mid', 'treble', 'energy', 'gentle', ...Array.from({ length: 8 }, (_, i) => 'note' + i)])
    assert.ok(nodes.has(reactive[name]), 'visible fills consume shared ' + name);
  assert.ok(nodes.has(music.tileBeat)); assert.ok(nodes.has(music.tileBar));
  assert.ok(nodes.has(music.tileOn), 'the absence of a recording restores the slate finish');
  assert.ok(!nodes.has(reactive.t), 'tile groups move to the beat rather than an independent animation clock');
  assert.ok(nodes.has(mortar), 'existing dark joints mask the fills');
  assert.deepEqual(new Set([...nodes].filter(n => n.isTextureNode).map(n => n.value)), new Set([reactive.vizTexture]), 'only the shared bounded PCM atlas is sampled');
  assert.ok(nodes.size < 1450, 'three compositions remain bounded inside the existing floor draw');
});

test('the final Keep override lights the actual opaque road and retains the original water graph', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
  const road = createRoadMaterial({ col: i => vec3(.2 + .1 * i, .3, .4) }, null, track);
  t.after(() => road.material.dispose());
  const original = road.material.colorNode;
  applyKeepRoad(road, null);
  const nodes = graph(road.material.colorNode);
  assert.ok(nodes.has(original), 'stream and pool rendering remains the original shared shader');
  assert.ok(nodes.has(road.uniforms.music.tileBeat));
  for (let i = 0; i < 8; i++) assert.ok(nodes.has(road.uniforms.reactive['note' + i]));
  assert.equal(road.material.transparent, false);
  assert.equal(road.material.opacity, 1);
  assert.equal(road.material.positionNode, null, 'floor colour never moves the physical deck');
});

test('Keep fill inputs soften onset, freeze when paused and return to exact silence through shared envelopes', () => {
  const u = createAudioReactiveUniforms(), music = musicUniforms();
  const floor = keepMusicFloor({ p: uv(), base: vec3(.1), seam: float(0), reactive: u, music });
  const before = graph(floor);
  const signal = { active: true, bass: 1, mid: 1, treble: 1, energy: 1, ...Object.fromEntries(Array.from({ length: 8 }, (_, i) => ['note' + i, 1])), songS: 18 };
  updateAudioReactiveUniforms(u, signal, 1 / 60);
  assert.ok(u.energy.value > 0 && u.energy.value < .06, 'no single-frame bright onset');
  for (let i = 0; i < 120; i++) updateAudioReactiveUniforms(u, signal, 1 / 60);
  const snapshot = { ...u.state };
  updateAudioReactiveUniforms(u, { ...signal, songS: 40 }, .1, { paused: true });
  assert.deepEqual(u.state, snapshot);
  for (let i = 0; i < 900; i++) updateAudioReactiveUniforms(u, null, 1 / 60);
  assert.equal(u.energy.value, 0, 'zero energy makes the final fill mix return to the slate base');
  assert.equal(u.bass.value, 0); assert.equal(u.mid.value, 0); assert.equal(u.treble.value, 0);
  assert.deepEqual(graph(floor), before, 'song changes only shared uniforms, never material resources');
});
