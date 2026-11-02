import test from 'node:test';
import assert from 'node:assert/strict';
import {createCardMedia} from '../host/cardMedia.js';
import {MAX_MEDIA_CLIPS,mediaClipLimit} from '../render/mediaVariety.js';
test('eight card faces share two decoded clips and source-off releases both owners', async t => {
  const slots = Array.from({ length: 8 }, (_, i) => ({ id: 'card-' + i, frame: 'card' }));
  const active = new Map(), videos = [], calls = [];
  const media = { slots, videoCapacity: 2, setSource(id, source) { if (source) active.set(id, source); else active.delete(id); } };
  t.mock.method(globalThis, 'fetch', async url => {
    calls.push(url); return new Response(new Uint8Array([1, 2, 3]), { headers: { 'content-type': 'video/mp4' } });
  });
  const adapter = createCardMedia(media, { origin: 'https://game.example', makeVideo(url) {
    const el = { url, pauses: 0, unloads: 0, pause() { this.pauses++; },
      removeAttribute(name) { assert.equal(name, 'src'); this.url = ''; }, load() { this.unloads++; } };
    videos.push(el); return el;
  } });
  t.after(() => adapter.dispose());
  const manifest = { enabled: true, sources: [0, 1, 2].map(i => ({ kind: 'video',
    url: '/__card-media/' + i, credit: 'scrolller', expiresAt: Date.now() + 60000 })) };
  await adapter.apply(manifest);
  assert.equal(active.size, 8);
  assert.equal(videos.length, 2, 'decoder count stays bounded as the number of cards grows');
  assert.equal(calls.length, 2);
  slots.forEach((slot, i) => {
    assert.equal(active.get(slot.id).el, videos[i % 2]);
    assert.equal(active.get(slot.id).borrowed, true);
    assert.equal(active.get(slot.id).credit, 'scrolller');
  });
  await adapter.apply(manifest);
  assert.equal(calls.length, 2, 'unchanged clips do not restart playback');
  await adapter.apply({ enabled: false });
  assert.equal(active.size, 0);
  assert.ok(videos.every(v => v.pauses === 1 && v.unloads === 1 && v.url === ''));
  adapter.dispose();
  await adapter.apply(manifest);
  assert.equal(calls.length, 2, 'a disposed adapter cannot acquire another clip');
});

const now=1792770000000;
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
const response = () => new Response(new Uint8Array([1, 2, 3]), { headers: { 'content-type': 'video/mp4' } });
const source = (id, expiresAt = Date.now() + 60000) => ({ kind: 'video', url: '/__card-media/' + id, credit: 'scrolller', expiresAt });
const manifest = (...sources) => ({ enabled: true, sources });
function mediaFixture(t, options = {}) {
  const { slots = Array.from({ length: 8 }, (_, i) => ({ id: 'card-' + i, frame: 'card' })), scrolllerShare,
    videoCapacity = MAX_MEDIA_CLIPS, gpuCapacity = Infinity, ...adapterOptions } = options;
  const active = new Map(), videos = [], revoked = [], assignments = [];
  t.mock.method(URL, 'createObjectURL', () => 'blob:fixture-' + videos.length);
  t.mock.method(URL, 'revokeObjectURL', url => revoked.push(url));
  const rejected = [];
  const media = { slots, scrolllerShare, get videoCapacity() { return typeof videoCapacity === 'function' ? videoCapacity() : videoCapacity; },
    setSource(id, src) {
      assignments.push([id, src]);
      if (!src) { active.delete(id); return true; }
      const owners = new Set([...active].filter(([key]) => key !== id).map(([, value]) => value.el).filter(Boolean));
      if (!owners.has(src.el) && owners.size >= gpuCapacity) { active.delete(id); rejected.push([id, src]); return false; }
      active.set(id, src); return true;
    } };
  const adapter = createCardMedia(media, { origin: 'https://game.example', ...adapterOptions, makeVideo(url) {
    const el = { url, currentTime: 0, pauses: 0, unloads: 0, pause() { this.pauses++; }, removeAttribute() { this.url = ''; }, load() { this.unloads++; } };
    videos.push(el); return el;
  } });
  t.after(() => adapter.dispose());
  return { adapter, active, videos, revoked, assignments, rejected };
}
function localLocation(t) {
  const old = Object.getOwnPropertyDescriptor(globalThis, 'location');
  Object.defineProperty(globalThis, 'location', { configurable: true, value: { hostname: 'localhost', origin: 'http://localhost:8855' } });
  t.after(() => { if (old) Object.defineProperty(globalThis, 'location', old); else delete globalThis.location; });
}

test('the renderer share allocates 47 of 78 world faces while two clip owners stay bounded and source-off clears them', async t => {
  let calls = 0;
  t.mock.method(globalThis, 'fetch', async () => { calls++; return response(); });
  const slots = Array.from({ length: 78 }, (_, i) => ({ id: 'board-' + i, frame: 'billboard', s: i * 10 }));
  const { adapter, active, videos, revoked } = mediaFixture(t, { slots, scrolllerShare: .6, videoCapacity: 2 });
  await adapter.apply(manifest(source('one'), source('two'), source('unused')));
  assert.equal(active.size, 47); assert.equal(slots.length - active.size, 31);
  assert.equal(videos.length, 2); assert.equal(calls, 2);
  await adapter.apply({ enabled: false, sources: [] });
  assert.equal(active.size, 0, 'every eligible face returns to its procedural fallback');
  assert.equal(revoked.length, 2);
  assert.ok(videos.every(v => v.pauses === 1 && v.unloads === 1));
});

test('world media rejects image, still, canvas and poster manifests without acquiring a decoder', async t => {
  const slots = [
    ...Array.from({ length: 8 }, (_, i) => ({ id: 'bb-' + i, frame: 'billboard', s: i * 10, phone: true })),
    { id: 'card-one', frame: 'card' },
  ];
  const calls = [];
  t.mock.method(globalThis, 'fetch', async url => { calls.push(url); return response(); });
  const { adapter, active, videos, assignments } = mediaFixture(t, { slots });
  await adapter.apply(manifest(...['image', 'still', 'canvas', 'poster'].map(kind => ({ ...source(kind), kind }))));
  assert.deepEqual(calls, []); assert.equal(videos.length, 0); assert.equal(active.size, 0); assert.equal(assignments.length, 0);
  await adapter.apply(manifest(source('moving')));
  assert.equal(calls.length, 1); assert.equal(videos.length, 1);
  await adapter.apply(manifest({ ...source('replacement-still'), kind: 'image' }));
  assert.equal(active.size, 0); assert.equal(videos[0].pauses, 1);
});

test('shared clip limits are six on high, four on mid and three on phone', () => {
  assert.equal(MAX_MEDIA_CLIPS, 6);
  assert.deepEqual(['high', 'mid', 'phone'].map(mediaClipLimit), [6, 4, 3]);
  assert.equal(mediaClipLimit(), 6); assert.equal(mediaClipLimit('unknown'), 6);
});

test('out-of-order arrivals change only their intended panels and preserve healthy fallback playback', async t => {
  const pending = Array.from({ length: MAX_MEDIA_CLIPS }, deferred), calls = [];
  t.mock.method(globalThis, 'fetch', (url) => { calls.push(url); return pending[Number(url.split('/').at(-1))].promise; });
  const slots = Array.from({ length: 18 }, (_, i) => ({ id: 'card-' + i, frame: 'card', s: i }));
  const { adapter, active, videos, assignments, rejected } = mediaFixture(t, { slots, gpuCapacity: MAX_MEDIA_CLIPS });
  const m = manifest(...pending.map((_, i) => source(i))), applying = adapter.apply(m);
  const settle = async () => { await new Promise(resolve => setImmediate(resolve)); };
  const byIndex = new Map();
  try {
    await Promise.resolve(); assert.equal(calls.length, MAX_MEDIA_CLIPS);
    for (const index of [0, 5, 2, 4, 1, 3]) {
      const before = new Map(active), start = assignments.length;
      pending[index].resolve(response()); await settle();
      byIndex.set(index, videos.at(-1));
      if (index === 0) videos[0].currentTime = 12.5;
      else {
        for (let i = 0; i < slots.length; i++) if (i % MAX_MEDIA_CLIPS !== index)
          assert.equal(active.get(slots[i].id).el, before.get(slots[i].id).el, 'unrelated panel keeps its healthy fallback');
        assert.ok(assignments.slice(start).every(([id]) => Number(id.split('-').at(-1)) % MAX_MEDIA_CLIPS === index));
      }
    }
    await applying;
    for (let i = 0; i < slots.length; i++) assert.equal(active.get(slots[i].id).el, byIndex.get(i % MAX_MEDIA_CLIPS));
    const count = assignments.length;
    await adapter.apply(m);
    assert.equal(assignments.length, count); assert.equal(calls.length, MAX_MEDIA_CLIPS);
    assert.equal(videos[0].currentTime, 12.5); assert.equal(videos[0].pauses, 0); assert.deepEqual(rejected, []);
  } finally { pending.forEach(p => p.resolve(response())); await applying; }
});

test('full-pool rebinding releases every changing reference before assigning replacements', async t => {
  const calls = [];
  t.mock.method(globalThis, 'fetch', async url => { calls.push(url); return response(); });
  const { adapter, active, videos, assignments, rejected } = mediaFixture(t, { gpuCapacity: MAX_MEDIA_CLIPS });
  const originals = Array.from({ length: MAX_MEDIA_CLIPS }, (_, i) => source('old-' + i));
  await adapter.apply(manifest(...originals));
  const owners = [...videos]; owners.forEach((v, i) => { v.currentTime = 10 + i; });
  let start = assignments.length;
  await adapter.apply(manifest(...originals.toReversed()));
  const swaps = assignments.slice(start);
  assert.equal(swaps.length, 16);
  assert.ok(swaps.slice(0, 8).every(([, src]) => src === null), 'all changing slots release before the first attach');
  assert.ok(swaps.slice(8).every(([, src]) => src?.el));
  assert.equal(calls.length, MAX_MEDIA_CLIPS);
  assert.ok(owners.every((v, i) => v.currentTime === 10 + i && v.pauses === 0));
  start = assignments.length;
  await adapter.apply(manifest(...originals.map((_, i) => source('new-' + i))));
  assert.ok(assignments.slice(start, start + 8).every(([, src]) => src === null));
  assert.equal(active.size, 8); assert.equal(new Set([...active.values()].map(s => s.el)).size, MAX_MEDIA_CLIPS);
  assert.ok(owners.every(v => v.pauses === 1 && v.unloads === 1));
  assert.deepEqual(rejected, [], 'full pool admits every replacement without losing a panel');
});

test('tier changes reconcile decoder counts while retained clips preserve playback position', async t => {
  let tier = 'high'; const calls = [];
  t.mock.method(globalThis, 'fetch', async url => { calls.push(url); return response(); });
  const { adapter, active, videos, revoked, rejected } = mediaFixture(t,
    { videoCapacity: () => mediaClipLimit(tier), gpuCapacity: MAX_MEDIA_CLIPS });
  const m = manifest(...Array.from({ length: MAX_MEDIA_CLIPS + 2 }, (_, i) => source('tier-' + i)));
  await adapter.apply(m); assert.equal(videos.length, 6);
  const retained = videos.slice(0, 3); retained.forEach((v, i) => { v.currentTime = i + 20; });
  tier = 'mid'; await adapter.apply(m);
  assert.equal(new Set([...active.values()].map(s => s.el)).size, 4); assert.equal(revoked.length, 2);
  tier = 'phone'; await adapter.apply(m);
  assert.equal(new Set([...active.values()].map(s => s.el)).size, 3); assert.equal(revoked.length, 3);
  assert.equal(calls.length, 6, 'tier reductions only release excess clips');
  tier = 'high'; await adapter.apply(m);
  assert.equal(new Set([...active.values()].map(s => s.el)).size, 6); assert.equal(calls.length, 9);
  assert.ok(retained.every((v, i) => v.currentTime === i + 20 && v.pauses === 0));
  assert.deepEqual(rejected, []);
});

test('zero renderer capacity acquires nothing and a later capacity increase resumes on the next manifest', async t => {
  let capacity = 0, calls = 0;
  t.mock.method(globalThis, 'fetch', async () => { calls++; return response(); });
  const { adapter, active } = mediaFixture(t, { videoCapacity: () => capacity });
  const m = manifest(...Array.from({ length: MAX_MEDIA_CLIPS }, (_, i) => source('zero-' + i)));
  await adapter.apply(m); assert.equal(calls, 0); assert.equal(active.size, 0);
  capacity = 3; await adapter.apply(m); assert.equal(calls, 3); assert.equal(active.size, 8);
  capacity = 0; await adapter.apply(m); assert.equal(active.size, 0);
});

test('a video-labelled manifest cannot put an image response on a world panel', async t => {
  t.mock.method(globalThis, 'fetch', async () => new Response(new Uint8Array([1, 2, 3]), { headers: { 'content-type': 'image/gif' } }));
  const slots = [0, 1].map(i => ({ id: 'halo-' + i, frame: 'halo', s: 0 }));
  const { adapter, active, videos } = mediaFixture(t, { slots });
  await adapter.apply(manifest(source('wrong-content-type')));
  assert.equal(active.size, 0); assert.equal(videos.length, 0);
});

test('off and disposal abort both pending clip downloads, and late responses cannot create owners', async t => {
  for (const action of ['off', 'dispose']) await t.test(action, async t => {
    const pending = [deferred(), deferred()], signals = [];
    t.mock.method(globalThis, 'fetch', (url, opts) => { signals.push(opts.signal); return pending[signals.length - 1].promise; });
    const { adapter, active, videos } = mediaFixture(t);
    const applying = adapter.apply(manifest(source('one'), source('two')));
    await Promise.resolve(); assert.equal(signals.length, 2);
    if (action === 'off') await adapter.apply({ enabled: false }); else adapter.dispose();
    assert.ok(signals.every(signal => signal.aborted));
    pending.forEach(p => p.resolve(response())); await applying;
    assert.equal(videos.length, 0); assert.equal(active.size, 0); assert.equal(signals.length, 2);
  });
});

test('replacement aborts obsolete content without allowing its late body to overwrite the new clip', async t => {
  const oldBody = deferred(); let oldSignal;
  t.mock.method(globalThis, 'fetch', async (url, opts) => {
    if (url.endsWith('/old')) { oldSignal = opts.signal; return { ok: true, headers: new Headers({ 'content-type': 'video/mp4' }), blob: () => oldBody.promise }; }
    return response();
  });
  const { adapter, active, videos } = mediaFixture(t);
  const applying = adapter.apply(manifest(source('old')));
  await Promise.resolve(); await Promise.resolve();
  await adapter.apply(manifest(source('new')));
  assert.ok(oldSignal.aborted); assert.equal(videos.length, 1);
  oldBody.resolve(new Blob(['old'], { type: 'video/mp4' })); await applying;
  assert.equal(videos.length, 1); assert.ok([...active.values()].every(s => s.el === videos[0]));
});

test('a stale manifest response or JSON body cannot revive playback after newer bridge source-off', async t => {
  for (const delayed of ['response', 'json']) await t.test(delayed, async t => {
    localLocation(t);
    const pending = deferred(); let receive, signal; const clips = [];
    t.mock.method(globalThis, 'fetch', (url, opts) => {
      if (url.endsWith('manifest.json')) {
        signal = opts.signal;
        return delayed === 'response' ? pending.promise : Promise.resolve({ ok: true, json: () => pending.promise });
      }
      clips.push(url); return Promise.resolve(response());
    });
    const bridge = { on(type, fn) { assert.equal(type, 'card-media'); receive = fn; return () => {}; } };
    const { adapter, active } = mediaFixture(t, { bridge });
    await Promise.resolve();
    await receive({ enabled: false }); assert.ok(signal.aborted);
    const stale = manifest(source('old'));
    pending.resolve(delayed === 'response' ? { ok: true, json: async () => stale } : stale);
    await adapter.ready;
    assert.deepEqual(clips, []); assert.equal(active.size, 0);
  });
});

test('a failing peer backs off while the healthy owner and its eight assignments keep playing', async t => {
  let clock = now, broken = true; const calls = [];
  t.mock.method(Date, 'now', () => clock);
  t.mock.method(globalThis, 'fetch', async url => {
    calls.push([url, clock]); return url.endsWith('/bad') && broken ? new Response('', { status: 404 }) : response();
  });
  const { adapter, videos, active, assignments } = mediaFixture(t);
  const m = manifest(source('good', now + 300000), source('bad', now + 300000));
  await adapter.apply(m); const healthy = videos[0];
  assert.equal(active.size, 8); assert.equal(videos.length, 1);
  for (const elapsed of [100, 500, 999, 1000, 1100, 2000, 2999, 3000, 4000, 6999, 7000, 15000, 31000, 60999]) {
    clock = now + elapsed; await adapter.apply(m);
  }
  assert.equal(calls.filter(([url]) => url.endsWith('/good')).length, 1);
  assert.deepEqual(calls.filter(([url]) => url.endsWith('/bad')).map(([, at]) => at - now), [0, 1000, 3000, 7000, 15000, 31000]);
  assert.equal(assignments.length, 8); assert.equal(healthy.pauses, 0);
  clock = now + 61000; broken = false; await adapter.apply(m);
  assert.equal(videos.length, 2); assert.equal(healthy.pauses, 0);
  assert.equal(new Set([...active.values()].map(s => s.el)).size, 2);
});

test('an expiry during peer download unloads the expired owner instead of attaching it again', async t => {
  let clock = now; const peer = deferred();
  t.mock.method(Date, 'now', () => clock);
  t.mock.method(globalThis, 'fetch', async url => url.endsWith('/peer') ? peer.promise : response());
  const { adapter, active, videos, revoked } = mediaFixture(t);
  const first = source('first', now + 100), second = source('peer', now + 60000);
  const applying = adapter.apply(manifest(first, second));
  await adapter.apply(manifest(first));
  assert.equal(videos.length, 1); const expired = videos[0];
  const loadingPeer = adapter.apply(manifest(first, second));
  clock = now + 150; peer.resolve(response());
  await Promise.all([applying, loadingPeer]);
  assert.equal(expired.pauses, 1); assert.equal(revoked.length, 1);
  assert.ok([...active.values()].every(s => s.el !== expired));
});

test('the original expiry deadline releases owners and slots without waiting for another manifest', async t => {
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now });
  t.mock.method(globalThis, 'fetch', async () => response());
  const { adapter, active, videos, revoked } = mediaFixture(t);
  await adapter.apply(manifest(source('short', now + 100)));
  t.mock.timers.tick(99); assert.equal(active.size, 8);
  t.mock.timers.tick(1); assert.equal(active.size, 0);
  assert.equal(videos[0].pauses, 1); assert.equal(revoked.length, 1);
});

test('failed bridge sources retry on their own timer without new messages', async t => {
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now });
  let calls = 0;
  t.mock.method(globalThis, 'fetch', async () => ++calls === 1 ? new Response('', { status: 503 }) : response());
  const { adapter, active } = mediaFixture(t), m = manifest(source('retry'));
  await adapter.apply(m); assert.equal(calls, 1); assert.equal(active.size, 0);
  t.mock.timers.tick(999); await Promise.resolve(); assert.equal(calls, 1);
  t.mock.timers.tick(1); await Promise.resolve(); assert.equal(calls, 2);
  await adapter.apply(m); assert.equal(active.size, 8);
});

test('a phone readyCount settles ready after the first complete clips; the rest keep arriving and desktop waits for all', async t => {
  for (const readyCount of [2, Infinity]) await t.test(String(readyCount), async t => {
    localLocation(t);
    const late = [deferred(), deferred()]; let lateIdx = 0;
    t.mock.method(globalThis, 'fetch', url => {
      if (url.endsWith('manifest.json')) return Promise.resolve({ ok: true, json: async () => manifest(source('a'), source('b'), source('c'), source('d')) });
      if (url.endsWith('/start')) return Promise.resolve(response());
      return url.endsWith('/a') || url.endsWith('/b') ? Promise.resolve(response()) : late[lateIdx++].promise;
    });
    const { adapter, videos } = mediaFixture(t, { readyCount });
    const settled = await Promise.race([adapter.ready.then(() => 'ready'), new Promise(r => setTimeout(r, 50, 'waiting'))]);
    assert.equal(settled, readyCount === 2 ? 'ready' : 'waiting');
    assert.equal(videos.length, 2, 'only complete downloads are attached, never a half clip');
    late.forEach(d => d.resolve(response()));
    await adapter.ready;
    await new Promise(r => setTimeout(r, 10));
    assert.equal(videos.length, 4, 'the rest arrive behind the menu');
  });
});
