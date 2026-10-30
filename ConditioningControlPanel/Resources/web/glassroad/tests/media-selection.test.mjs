import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { goonworksMediaLayout } from '../render/goonworksMedia.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';

const rows = (frame, count, extra = {}) => Array.from({ length: count }, (_, i) =>
  ({ id: frame + '-' + i, frame, s: i * 20, ...extra }));

test('the default retains the established half-media spatial assignment and every trap card', () => {
  const slots = [...rows('card', 2), ...rows('billboard', 8).map((s, i) => ({ ...s, phone: i < 5 })),
    ...rows('halo', 5), ...rows('tunnel', 3)];
  assert.deepEqual(selectScrolllerSlots(slots).map(s => s.id),
    ['card-0', 'card-1', 'billboard-0', 'billboard-2', 'billboard-4', 'billboard-6',
      'halo-0', 'halo-2', 'halo-4', 'tunnel-0', 'tunnel-2']);
  assert.deepEqual(selectScrolllerSlots(slots, { share: .5 }), selectScrolllerSlots(slots));
});

test('sixty percent of 78 screens allocates 47 media faces and 31 spirals across the route', () => {
  const slots = rows('billboard', 78), before = structuredClone(slots);
  const selected = selectScrolllerSlots(slots, { share: .6 }), ids = new Set(selected.map(s => s.id));
  assert.equal(selected.length, 47); assert.equal(slots.length - selected.length, 31);
  assert.equal(ids.size, selected.length); assert.deepEqual(slots, before);
  assert.ok(selected.every(s => slots.includes(s)), 'returns the original objects');
  assert.deepEqual(selectScrolllerSlots([...slots].reverse(), { share: .6 }), selected, 'input order cannot change allocation');
  for (let i = 0; i < slots.length - 4; i++) {
    const n = slots.slice(i, i + 5).filter(s => ids.has(s.id)).length;
    assert.ok(n >= 2 && n <= 4, 'neighbourhood retains both media and spiral faces');
  }
});

test('custom ratios round the whole world once while retaining balanced visibility cohorts', () => {
  const cohorts = [rows('billboard', 33, { phone: true }), rows('billboard', 17, { phone: false }).map(s => ({ ...s, id: 'desktop-' + s.id })),
    rows('halo', 15), rows('tunnel', 13)];
  const cards = rows('card', 8), slots = [...cards, ...cohorts.flat()];
  for (const share of [0, .2, .4, .6, .8, 1]) {
    const selected = selectScrolllerSlots(slots, { share });
    assert.equal(selected.length, cards.length + Math.round(78 * share));
    assert.ok(cards.every(card => selected.includes(card)));
    for (const cohort of cohorts) {
      const n = cohort.filter(slot => selected.includes(slot)).length;
      assert.ok(Math.abs(n - cohort.length * share) < 1, 'cohort allocation stays within one face of its share');
    }
  }
});

test('invalid shares retain the default and bounded shares cannot overallocate', () => {
  const slots = rows('billboard', 9);
  for (const share of [undefined, NaN, Infinity, '0.6'])
    assert.deepEqual(selectScrolllerSlots(slots, { share }), selectScrolllerSlots(slots));
  assert.equal(selectScrolllerSlots(slots, { share: -1 }).length, 0);
  assert.equal(selectScrolllerSlots(slots, { share: 2 }).length, slots.length);
  assert.deepEqual(selectScrolllerSlots([], { share: .6 }), []);
});

test('GOONWORKS warms its actual eligible media faces while unavailable sources retain every procedural screen', async t => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
    return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
      : next(specifier, context);
  } });
  let createMediaSlots;
  try { ({ createMediaSlots } = await import('../render/media.js')); } finally { hooks.deregister(); }
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
  const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k in o ? o[k]
    : /Gradient/.test(k) ? () => gradient : () => {} });
  const make = (width, height) => ({ width, height, getContext: () => context });
  const layout = goonworksMediaLayout(track), expectedFaces = Math.round(layout.slots.length * .6);
  assert.ok(layout.slots.length >= 78 && expectedFaces >= 47, 'broader districts preserve the established coverage floor');
  const media = createMediaSlots(null, track, { layout, make, scrolllerShare: .6 });
  t.after(() => media.dispose());
  assert.equal(media.slots.length, layout.slots.length); assert.equal(media.scrolllerShare, .6);
  assert.equal(media.stats().selectedMediaFaces, expectedFaces); assert.equal(media.stats().readyMediaVideos, 0);
  assert.equal(media.stats().prewarmedVideoTextures, 6, 'more faces do not add decoder textures');
  assert.equal(media.object.children.filter(o => o.name.startsWith('media-world-source-')).length, expectedFaces);
  assert.ok(media.object.children.filter(o => o.name.startsWith('media-world-source-')).every(o => !o.visible));
  assert.equal(media.slots.length - selectScrolllerSlots(media.slots, { share: media.scrolllerShare }).length, layout.slots.length - expectedFaces);
  assert.ok(layout.slots.length - expectedFaces >= 31, 'procedural coverage grows with the real layout');
});
