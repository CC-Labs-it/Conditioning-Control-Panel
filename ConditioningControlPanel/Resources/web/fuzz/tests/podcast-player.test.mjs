import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mountPodcast, stationOrder, RING } from '../ui/unlocks/podcast-player.js';

function element() {
  return Object.assign(new EventTarget(), {
    style: {}, dataset: {}, classList: { toggle() {} }, setAttribute() {},
  });
}

async function station(t, manifest, store = {}, theme = 'trance') {
  const answer = typeof manifest === 'function' ? manifest : () => manifest;
  t.mock.method(globalThis, 'fetch', async url => { const m = answer(url); return { ok: !!m, json: async () => m }; });
  const streams = [], events = {}, made = [];
  const ctx = {
    item: { id: 'tr_pod' }, state: { order: ['tr_pod'], clock: 0 }, rng: () => 0,
    store: { get: k => store[k], set: (k, v) => { store[k] = v; } },
    css() {}, dock: element, el: () => { const e = element(); made.push(e); return e; }, say() {}, frame() {}, on: (event, f) => { events[event] = f; },
    audio: { stream(path) {
      const el = element();
      const stream = { path, el, stop() {}, setGain(v) { this.gain = v; } };
      streams.push(stream);
      return stream;
    } },
  };
  mountPodcast(ctx, { theme, show: 'Station' });
  await new Promise(resolve => setImmediate(resolve));
  const button = cls => made.find(e => e.className === cls);
  return { streams, events, ctx, store, button, end(stream) { stream.el.dispatchEvent(new Event('ended')); } };
}

test('an opener precedes the episode and an outstanding reaction finishes before the closer and next episode', async t => {
  const row = file => ({ file });
  const s = await station(t, {
    episodes: ['ep1.mp3', 'ep2.mp3'].map(row),
    clips: { opener: [row('hello.mp3')], react_buy: [row('buy.mp3')], closer: [row('bye.mp3')] },
  });
  const names = () => s.streams.map(stream => stream.path.split('/').pop());
  assert.deepEqual(names(), ['hello.mp3']);
  s.end(s.streams[0]);
  s.ctx.state.clock = 61000;
  s.events.buy();
  assert.deepEqual(names(), ['hello.mp3', 'ep1.mp3', 'buy.mp3']);
  assert.ok(s.streams[1].gain < 1);
  s.end(s.streams[1]);
  assert.equal(s.streams.length, 3);
  s.end(s.streams[2]);
  assert.equal(names().at(-1), 'bye.mp3');
  s.end(s.streams[3]);
  assert.equal(names().at(-1), 'ep2.mp3');
  assert.equal(s.store.ep, 1);
  assert.equal(s.store.at, 0);
});

test('a partial manifest with only clips still plays its opener and reacts to a buy', async t => {
  const s = await station(t, { clips: { opener: [{ file: 'start.mp3' }], react_buy: [{ file: 'buy.mp3' }] } });
  s.end(s.streams[0]);
  s.ctx.state.clock = 61000;
  s.events.buy();
  assert.deepEqual(s.streams.map(stream => stream.path), [
    'assets/audio/podcast/trance/start.mp3', 'assets/audio/podcast/trance/buy.mp3',
  ]);
});

test('the station starts on its own show and walks on round the ring of niches', () => {
  assert.deepEqual(stationOrder('sissy').slice(0, 5), ['sissy', 'bimbo', 'goon', 'chastity', 'drone']);
  assert.equal(stationOrder('drone')[0], 'drone');
  assert.deepEqual([...stationOrder('trance')].sort(), [...RING].sort());
});

test('episodes run on into the next show, and next and back move through the whole list', async t => {
  const eps = names => ({ episodes: names.map(file => ({ file })) });
  const s = await station(t, url => {
    if (url.endsWith('waves.json')) return null;
    if (url.includes('/sissy/')) return eps(['s1.mp3', 's2.mp3']);
    if (url.includes('/bimbo/')) return eps(['b1.mp3']);
    return null;
  }, { heard: 1 }, 'sissy');
  const path = () => s.streams.at(-1).path;
  assert.equal(path(), 'assets/audio/podcast/sissy/s1.mp3');
  s.button('pc-step').dispatchEvent(new Event('click'));
  assert.equal(path(), 'assets/audio/podcast/sissy/s2.mp3');
  s.end(s.streams.at(-1));
  assert.equal(path(), 'assets/audio/podcast/bimbo/b1.mp3');
  assert.equal(s.store.ep, 2);
  s.button('pc-step').dispatchEvent(new Event('click'));
  assert.equal(path(), 'assets/audio/podcast/sissy/s1.mp3');
  s.button('pc-step pc-back').dispatchEvent(new Event('click'));
  assert.equal(path(), 'assets/audio/podcast/bimbo/b1.mp3');
});
