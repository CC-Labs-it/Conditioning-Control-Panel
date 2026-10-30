/* tests/render-ramps.test.mjs - the ramp wedge view builds with the shipped three.js classes (no DOM, no GPU: the
 * render-stones pattern): one mesh per layer for every ramp, shared/lip material reused, update() writes uniforms only. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { createRampView } = await import('../render/ramps.js');
const { createPalette } = await import('../render/materials/palette.js');
const { createPadMaterial } = await import('../render/materials/pads.js');
hooks.deregister();

function stadium(ramps) {
  const nodes = [], push = (x, z) => nodes.push({ p: [x, 0, z], roll: 0, w: 16, flags: nodes.length ? ['rail'] : ['start', 'rail'], speed: 1, surface: 'glass' });
  for (let i = 0; i < 40; i++) push(i * 12, 0);
  for (let i = 0; i < 25; i++) { const a = (i / 25) * Math.PI; push(480 + Math.sin(a) * 95, 95 - Math.cos(a) * 95); }
  for (let i = 0; i < 40; i++) push(480 - i * 12, 190);
  for (let i = 0; i < 25; i++) { const a = (i / 25) * Math.PI; push(-Math.sin(a) * 95, 95 + Math.cos(a) * 95); }
  return { format: 'glassroad-track', version: 1, id: 'ramp-fixture', name: { en: 'x' }, loop: true, laps: 3, units: 'm', nodes,
    sections: [{ id: 'a', from: 0, to: nodes.length, act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6 }],
    checkpoints: [0, 65], spawn: { node: 0, grid: [[0, -4]] }, items: [], boosts: [], jumps: [], hazards: [], media: [],
    ai: { lines: [], pace: [1] }, beat: { bpm: null, offsetMs: 0, map: [] }, limits: { fallY: -40, edgeGraceM: 1.2, edgeGraceS: 0.8 }, ...(ramps ? { ramps } : {}) };
}
const R = (id, node, x = 0) => ({ id, node, x, widthM: 6, lenM: 7, heightM: 1.2, upMps: 14, purpose: 'air' });

test('ramp view: three draws for any number of ramps, palette body, chevrons, the shared lip', () => {
  const track = loadTrack(stadium([R('a', 25), R('b', 52, 2), R('c', 90, -3)]));
  const pal = createPalette('threshold'), lip = createPadMaterial('#ffffff');
  const view = createRampView(track, pal, { lipMaterial: lip.material, accent: '#ff66cc' });
  assert.ok(view, 'built');
  const meshes = [];
  view.group.traverse((o) => { if (o.isMesh) meshes.push(o); });
  assert.equal(meshes.length, 3, 'one merged mesh per layer');
  assert.ok(meshes.some((m) => m.material === lip.material), 'the lip reuses the scene material');
  const body = meshes.find((m) => m.name === 'ramp-wedges');
  assert.ok(body.geometry.getAttribute('shade'), 'shade attribute for the palette body');
  assert.ok(body.geometry.index.count > 3 * 2 * 8 * 3, 'three wedges merged');
  view.update(0.4, 0.5);
  assert.equal(view.materials.chevrons.colorNode !== undefined, true);
  view.dispose();
  assert.equal(view.group.parent, null);
  assert.equal(createRampView(loadTrack(stadium(null)), pal, { lipMaterial: lip.material }), null, 'no ramps: nothing built');
});
