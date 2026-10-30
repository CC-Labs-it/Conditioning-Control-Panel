import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  if (specifier.startsWith('three/addons/')) return { url: new URL('../vendor/three/addons/' + specifier.slice(13), import.meta.url).href, shortCircuit: true };
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { float, vec3, uv, uniform } = await import('three/tsl');
const { FrontSide } = await import('three/webgpu');
const { createAudioReactiveUniforms, updateAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { createRoadMaterial } = await import('../render/materials/road.js');
const { goonworksMusicFloor } = await import('../render/materials/goonworksMusicFloor.js');
const { applyGoonworksRoad } = await import('../render/goonworksMaterials.js');
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

test('Goonworks jukebox floor consumes the shared bands, eight notes and heard beat without new media', () => {
  const reactive = createAudioReactiveUniforms(), music = musicUniforms(), top = float(.8), edge = float(7);
  const floor = goonworksMusicFloor({ p: uv(), base: vec3(.6), top, edge, half: float(18), pink: vec3(.8,.1,.4), accent: vec3(.2,.7,.6), reactive, music });
  const nodes = graph(floor);
  for (const name of ['kickAge','kickStrength','snareAge','snare','hatsCount','hats','mid','energy','gentle', ...Array.from({length:8}, (_,i) => 'note'+i)])
    assert.ok(nodes.has(reactive[name]), 'visible pattern consumes '+name);
  for (const key of ['tileBar','tileOn']) assert.ok(nodes.has(music[key]), 'shared phrase and audible music gate are retained');
  assert.ok(nodes.has(top) && nodes.has(edge), 'all patterns stay inside the existing top and navigation edge');
  assert.ok(!nodes.has(reactive.t), 'no independently running animation clock');
  assert.deepEqual(new Set([...nodes].filter(n => n.isTextureNode).map(n => n.value)), new Set([reactive.vizTexture]), 'no independent textures, media owners or render targets');
  assert.ok(nodes.size < 1450, 'bounded analytic graph: ' + nodes.size);
});

test('the final opaque Goonworks material contains the complete musical floor rather than discarding it', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
  const road = createRoadMaterial({ col: i => vec3(.2+.1*i,.3,.4) }, null, track);
  const owner = applyGoonworksRoad(road, null, track);
  t.after(() => { owner.dispose(); road.material.dispose(); });
  const nodes = graph(road.material.colorNode);
  for (let i=0; i<8; i++) assert.ok(nodes.has(road.uniforms.reactive['note'+i]));
  for (const name of ['tileBar','tileOn']) assert.ok(nodes.has(road.uniforms.music[name]));
  for (const name of ['kickAge','snareAge','hatsCount']) assert.ok(nodes.has(road.uniforms.reactive[name]));
  assert.ok(nodes.has(road.uniforms.reactive.energy));
  assert.equal(road.material.transparent, false); assert.equal(road.material.opacity, 1);
  assert.equal(road.material.side, FrontSide); assert.equal(road.material.positionNode, null);
});

test('floor input envelopes soften onset, hold on pause, settle to silence and retain Gentle gain', () => {
  const u = createAudioReactiveUniforms();
  const signal = {active:true,bass:1,mid:1,treble:1,energy:1,songS:4,...Object.fromEntries(Array.from({length:8},(_,i)=>['note'+i,1]))};
  updateAudioReactiveUniforms(u,signal,1/60,{gentle:true});
  assert.ok(u.energy.value > 0 && u.energy.value < .06); assert.equal(u.gentle.value, 1);
  for(let i=0;i<120;i++) updateAudioReactiveUniforms(u,signal,1/60,{gentle:true});
  const held={...u.state}; updateAudioReactiveUniforms(u,{...signal,songS:90},.1,{paused:true,gentle:true});
  assert.deepEqual(u.state,held);
  for(let i=0;i<900;i++) updateAudioReactiveUniforms(u,null,1/60,{gentle:true});
  assert.equal(u.energy.value,0); assert.equal(u.bass.value,0); assert.equal(u.treble.value,0);
});
