import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createSongCache } from '../audio/songCache.js';

test('loading prefetches chart and recording once; playback receives intact reusable bodies', async () => {
  const fetched = [], cache = createSongCache({ fetchFn: async url => {
    fetched.push(url);
    return new Response(url.endsWith('.json') ? JSON.stringify({ bpm: 120, sections: [], audio: 'song.mp3' }) : new Uint8Array([1,2,3]));
  } });
  assert.equal(await cache.preload('https://example.test/song.json'), true);
  assert.equal((await (await cache.fetch('https://example.test/song.json')).json()).bpm, 120);
  assert.deepEqual(new Uint8Array(await (await cache.fetch('https://example.test/song.mp3')).arrayBuffer()), new Uint8Array([1,2,3]));
  assert.equal(await cache.preload('https://example.test/song.json'), true);
  assert.equal(fetched.length, 2);
  cache.clear(); await cache.preload('https://example.test/song.json');
  assert.equal(fetched.length, 4);
});

test('failed authorization or network loads are not retained as a silent permanent song', async () => {
  let calls = 0;
  const cache = createSongCache({ fetchFn: async () => ++calls === 1 ? new Response('', { status: 401 }) : new Response('{"bpm":120,"sections":[],"audio":"song.mp3"}') });
  assert.equal(await cache.preload('https://example.test/song.json'), false);
  assert.equal(await cache.preload('https://example.test/song.json'), true);
  assert.equal(calls, 3);
});
