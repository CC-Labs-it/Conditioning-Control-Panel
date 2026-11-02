import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';

const hooks = registerHooks({ resolve(s, c, n) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : n(s, c);
} });
const { Color, MeshBasicNodeMaterial } = await import('three/webgpu');
const { applyGoonworksRoad, GOONWORKS_DISTRICT_PALETTES, GOONWORKS_PALETTE } = await import('../render/goonworksMaterials.js');
hooks.deregister();

function fixture(t) {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
  const road = { material: new MeshBasicNodeMaterial() }, owner = applyGoonworksRoad(road, null, track);
  t.after(() => { owner.dispose(); road.material.dispose(); });
  const nodes = new Set(), pending = [road.material.colorNode], textures = new Set();
  while (pending.length) {
    const node = pending.pop(); if (nodes.has(node)) continue; nodes.add(node);
    if (node.isTextureNode) textures.add(node.value);
    for (const child of node.getChildren()) pending.push(child);
  }
  assert.equal(textures.size, 1, 'the final opaque material samples one shared road lookup');
  return { track, table: [...textures][0], layout: getGoonworksLayout(track) };
}

test('the enlarged GOONWORKS road lookup fits the default WebGPU texture limit', t => {
  const { track, table } = fixture(t);
  assert.ok(track.lengthM > 8192, 'the real course exercises the previously failing long-road case');
  assert.equal(table.image.width, 8192);
  assert.equal(table.image.height, 4);
  assert.equal(table.image.data.length, 8192 * 4 * 4);
  assert.equal(table.generateMipmaps, false);
});

test('the capped GOONWORKS lookup still covers every district and the complete lap', t => {
  const { track, table, layout } = fixture(t), { width, data } = table.image;
  const halls = ['mill-hall', 'quiz-ballroom', 'pinball-table'].map(id => layout.byId[id]);
  const indices = new Set([0, width - 1]);
  for (const section of layout.sections) indices.add(Math.min(width - 1, Math.floor((section.s0 + section.s1) * .5 / track.lengthM * width)));
  for (const index of indices) {
    const s = (index + .5) * track.lengthM / width;
    const section = layout.sections.find(row => s >= row.s0 && s < row.s1);
    const palette = GOONWORKS_DISTRICT_PALETTES[section?.id] || GOONWORKS_DISTRICT_PALETTES.start;
    assert.equal(data[index * 4], Math.round(Math.min(1, track.frameAt(s).w / 120) * 255), 'width samples the complete physical lap at ' + s);
    assert.equal(data[index * 4 + 1], halls.some(row => s >= row.s0 && s <= row.s1) ? 255 : 0);
    for (const [row, key] of ['road', 'pink', 'accent'].entries()) {
      const colour = new Color(GOONWORKS_PALETTE[palette[key]]).toArray();
      const offset = ((row + 1) * width + index) * 4;
      assert.deepEqual([...data.subarray(offset, offset + 3)], colour.map(v => Math.round(v * 255)), 'district ' + (section?.id || 'start') + ' keeps its ' + key);
      assert.equal(data[offset + 3], 255);
    }
  }
  assert.ok((width - .5) * track.lengthM / width > track.lengthM - 2, 'the last texel reaches the lap end rather than stopping at 8192 metres');
});
