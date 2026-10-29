import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { setMaxListeners } from 'node:events';
import { loadTrack } from '../track/load.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';
import { mediaClipLimit } from '../render/mediaVariety.js';
import { setTier } from '../render/quality.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { createMediaSlots } = await import('../render/media.js');
hooks.deregister();
const read = name => JSON.parse(readFileSync(new URL('../tracks/' + name, import.meta.url), 'utf8'));
const track = loadTrack(read('mainframe.json')), layout = read('mainframe.media.json');
const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k in o ? o[k]
  : k === 'createLinearGradient' || k === 'createRadialGradient' ? () => gradient : () => {} });
const make = (width, height) => ({ width, height, getContext: () => context });

for (const tier of ['high', 'mid', 'phone']) test('Mainframe moving media shares prewarmed textures and leaves half its world on Loom: ' + tier, t => {
  setTier(tier);
  const media = createMediaSlots(null, track, { layout, make, tier });
  t.after(() => { media.dispose(); setTier('high'); });
  const selected = selectScrolllerSlots(media.slots), capacity = mediaClipLimit(tier);
  assert.ok(selected.length >= capacity);
  for (const frame of ['billboard', 'halo', 'tunnel']) {
    const total = media.slots.filter(slot => slot.frame === frame).length;
    assert.ok(total >= 3, frame + ' is present');
    assert.equal(selected.filter(slot => slot.frame === frame).length, Math.ceil(total / 2));
  }
  assert.equal(media.stats().prewarmedVideoTextures, 6);
  assert.equal(media.videoCapacity, capacity);
  const before = media.object.children.length;
  const clips = Array.from({ length: capacity }, () => {
    const el = new EventTarget(); setMaxListeners(0, el);
    return Object.assign(el, { currentTime: 0, duration: 10, loop: true, paused: true, ended: false, readyState: 2,
      videoWidth: 640, videoHeight: 360, seeking: false, play() { this.paused = false; return Promise.resolve(); },
      pause() { this.paused = true; }, removeAttribute() {}, load() {} });
  });
  selected.forEach((slot, i) => assert.equal(media.setSource(slot.id,
    { kind: 'video', el: clips[i % capacity], borrowed: true, credit: 'scrolller' }), true));
  for (let i = 0; i < 30; i++) media.update(1 / 60, { eye: selected[0].C });
  assert.equal(media.object.children.length, before, 'arrival creates no materials after warmup');
  assert.equal(media.stats().readyWorldVideos, selected.length);
  assert.equal(media.stats().sharedVideoTextures, capacity);
  selected.forEach(slot => media.setSource(slot.id, null));
  assert.equal(media.stats().readyWorldVideos, 0, 'source-off removes every moving face');
});
