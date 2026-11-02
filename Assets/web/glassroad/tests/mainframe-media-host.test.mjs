import test from 'node:test';
import assert from 'node:assert/strict';
import { createCardMedia, validCardSource } from '../host/cardMedia.js';

test('host media uses complete same-origin clips, shares owners and clears them on source-off', async t => {
  const previousFetch = globalThis.fetch, calls = [], videos = [], shown = new Map();
  globalThis.fetch = async url => { calls.push(url); return new Response(new Uint8Array([0, 1, 2, 3]), { headers: { 'Content-Type': 'video/mp4' } }); };
  t.after(() => { globalThis.fetch = previousFetch; });
  const media = { videoCapacity: 2,
    slots: Array.from({ length: 8 }, (_, i) => ({ id: 'board-' + i, frame: 'billboard', s: i * 20, phone: true })),
    setSource(id, source) { if (source) shown.set(id, source); else shown.delete(id); return true; },
  };
  const host = createCardMedia(media, { origin: 'http://127.0.0.1:18910', makeVideo(url) {
    const video = { src: url, pauses: 0, loads: 0, pause() { this.pauses++; }, load() { this.loads++; }, removeAttribute() {} };
    videos.push(video); return video;
  } });
  t.after(() => host.dispose());
  const source = i => ({ kind: 'video', url: '/__card-media/' + i, expiresAt: Date.now() + 60000, credit: 'scrolller' });
  const manifest = { enabled: true, sources: [source(1), source(2), source(3)] };
  await host.apply(manifest);
  assert.equal(calls.length, 2); assert.equal(shown.size, 4);
  assert.equal(new Set([...shown.values()].map(s => s.el)).size, 2);
  assert.ok([...shown.values()].every(s => s.borrowed && s.credit === 'scrolller'));
  await host.apply(manifest);
  assert.equal(calls.length, 2, 'polling retains healthy owners');
  await host.apply({ enabled: false, sources: [] });
  assert.equal(shown.size, 0);
  assert.ok(videos.every(v => v.pauses === 1 && v.loads === 1));
});

test('host media rejects uncached external, expired and over-age sources', () => {
  const now = Date.now(), base = { kind: 'video', url: '/__card-media/local', expiresAt: now + 1000, credit: 'scrolller' };
  assert.equal(validCardSource(base, 'http://127.0.0.1:18910', now), true);
  for (const change of [{ url: 'https://media.scrolller.com/direct.mp4' }, { expiresAt: now },
    { expiresAt: now + 86400001 }, { kind: 'image' }, { credit: '' }]) {
    assert.equal(validCardSource({ ...base, ...change }, 'http://127.0.0.1:18910', now), false);
  }
});
