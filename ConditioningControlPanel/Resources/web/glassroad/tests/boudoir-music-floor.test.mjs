import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { float, vec3, uv, uniform } = await import('three/tsl');
const { createAudioReactiveUniforms, updateAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { boudoirMusicFloor, BOUDOIR_MUSIC_COLOURS, BOUDOIR_MUSIC_FLOOR } = await import('../render/materials/boudoirMusicFloor.js');
const { createRoadMaterial } = await import('../render/materials/road.js');
hooks.deregister();
const musicUniforms = () => Object.fromEntries(['tileBeat', 'tileBar', 'tileOn', 'tileGentle', 'big', 'calm'].map(k => [k, uniform(0)]));
function graph(root) {
  const nodes = new Set(), pending = [root];
  while (pending.length) {
    const node = pending.pop(); if (nodes.has(node)) continue; nodes.add(node);
    for (const child of node.getChildren()) pending.push(child);
  }
  return nodes;
}

test('Boudoir colour spectacle follows the heard beat and real bands without a timer or new resources', () => {
  const reactive = createAudioReactiveUniforms(), music = musicUniforms();
  const inputs = { p: uv(), base: vec3(.2, .1, .1), trueEdge: uniform(8), distance: uniform(20), wet: uniform(0), reactive, music, lapM: 4380 };
  const nodes = graph(boudoirMusicFloor(inputs));
  for (const name of ['bass', 'mid', 'treble', 'energy', ...Array.from({length: 8}, (_, i) => 'note' + i)]) assert.ok(nodes.has(reactive[name]), name);
  for (const name of Object.keys(music)) assert.ok(nodes.has(music[name]), name);
  for (const name of ['base', 'trueEdge', 'distance', 'wet']) assert.ok(nodes.has(inputs[name]), name);
  assert.ok(!nodes.has(reactive.t), 'there is no second phase clock');
  assert.ok(![...nodes].some(n => n.isTextureNode), 'same material, no textures, media or render targets');
  assert.ok(nodes.size < 900, `bounded shader graph: ${nodes.size}`);
});

test('broad shifting hues share luminance and Gentle keeps less than half the normal coverage', () => {
  const colours = Object.values(BOUDOIR_MUSIC_COLOURS), L = BOUDOIR_MUSIC_FLOOR;
  for (const colour of colours) {
    assert.ok(colour.every(v => Number.isFinite(v) && v >= 0 && v <= 1));
    const light = colour[0] * .2126 + colour[1] * .7152 + colour[2] * .0722;
    assert.ok(Math.abs(light - L.luminance) < 1e-12);
  }
  assert.equal(new Set(colours.map(c => c.join(','))).size, 4);
  assert.ok(L.gentleCoverage < L.coverage / 2);
  assert.ok(L.wetCoverage < .5, 'water keeps its visible character');
});

test('final Boudoir road and forks use the new paint without moving the physical surface', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
  const road = createRoadMaterial({ col: i => vec3(.2 + i * .1, .3, .4) }, null, track);
  t.after(() => road.material.dispose());
  const nodes = graph(road.material.colorNode);
  for (const name of ['tileBeat', 'tileBar', 'tileOn', 'tileGentle', 'big', 'calm']) assert.ok(nodes.has(road.uniforms.music[name]), name);
  for (const name of ['bass', 'mid', 'treble', 'energy']) assert.ok(nodes.has(road.uniforms.reactive[name]), name);
  assert.ok(nodes.has(road.uniforms.water.t), 'shared water remains rendered');
  assert.equal(road.material.positionNode, null, 'patterns are paint, not extra physical obstacles');
  const forks = readFileSync(new URL('../render/boudoirForks.js', import.meta.url), 'utf8');
  assert.match(forks, /add\(data\.road, roadMaterial, 'fork-road'\)/, 'forks use this exact final road material');
});

test('audio envelopes hold all floor inputs on pause, soften attacks and return to silence', () => {
  const u = createAudioReactiveUniforms();
  const signal = { active: true, bass: .6, mid: .7, treble: .8, energy: .6, songS: 40 };
  updateAudioReactiveUniforms(u, signal, 1 / 60, { gentle: true });
  assert.ok(u.energy.value > 0 && u.energy.value < .03);
  for (let i = 0; i < 120; i++) updateAudioReactiveUniforms(u, signal, 1 / 60, { gentle: true });
  const snapshot = { ...u.state };
  updateAudioReactiveUniforms(u, { ...signal, songS: 80 }, .1, { paused: true });
  assert.deepEqual(u.state, snapshot);
  for (let i = 0; i < 900; i++) updateAudioReactiveUniforms(u, null, 1 / 60);
  for (const name of ['bass', 'mid', 'treble', 'energy']) assert.equal(u[name].value, 0);
});
