// The phone's Scrolller pictures (caps.scrolller.origin 'device'): the guard, the provider and the real screens' owners.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { feedRows, deviceRows, deviceUrl, createScrolllerFeed, MAX_FEED_ROWS } from '../host/scrolllerFeed.js';
import { createDevicePictures, decodeInto, fitEdge, PICTURE_EDGE, TURN_S, GENTLE_TURN_S, DECODE_TIMEOUT_MS } from '../host/devicePictures.js';
import { createCardMedia } from '../host/cardMedia.js';
import { setTrackTableLimit, trackTableMax } from '../render/spectrum.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { createMediaSlots } = await import('../render/media.js');
const { energyTexture } = await import('../render/materials/road.js');
hooks.deregister();

const ORIGIN = 'http://127.0.0.1:4173';
const same = (name) => ORIGIN + '/scrolller-media/' + name + '.jpg';
const settle = async () => { for (let i = 0; i < 6; i++) await new Promise((r) => setImmediate(r)); };
const read = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

function fakeBridge() {
  const handlers = new Map(), sent = [];
  return { sent, on(type, fn) { handlers.set(type, fn); return () => handlers.delete(type); }, send(m) { sent.push(m); },
    emit(type, m) { handlers.get(type)?.({ type, ...m }); } };
}
const rowsOf = (n, tag = 'p') => Array.from({ length: n }, (_, i) => ({ url: same(tag + i), kind: 'image', mime: 'image/jpeg' }));

test('device guard: only the page\'s own origin passes, a scrolller.com url is refused', () => {
  assert.deepEqual(deviceRows({ v: 1, rows: [{ url: same('a'), kind: 'image', mime: 'image/jpeg' }] }, ORIGIN), [{ url: same('a'), kind: 'image' }]);
  assert.deepEqual(deviceRows({ v: 1, rows: [{ url: same('a'), kind: 'image' }] }, ORIGIN).length, 1, 'mime is optional');
  for (const bad of [
    { url: 'https://images.scrolller.com/femto/a.jpg', kind: 'image' },
    { url: 'http://127.0.0.1:4174/scrolller-media/a.jpg', kind: 'image' },
    { url: 'https://127.0.0.1:4173/scrolller-media/a.jpg', kind: 'image' },
    { url: 'http://localhost:4173/scrolller-media/a.jpg', kind: 'image' },
    { url: 'http://127.0.0.1:4173@evil.example/a.jpg', kind: 'image' },
    { url: 'http://user:pw@127.0.0.1:4173/a.jpg', kind: 'image' },
    { url: '/scrolller-media/a.jpg', kind: 'image' },
    { url: '//127.0.0.1:4173/a.jpg', kind: 'image' },
    { url: 'blob:' + ORIGIN + '/x', kind: 'image' },
    { url: 'data:image/png;base64,AAAA', kind: 'image' },
    { url: same('a'), kind: 'video' },
    { url: same('a'), kind: 'loop' },
    { url: same('a'), kind: 'image', mime: 'image/svg+xml' },
    { url: same('a'), kind: 'image', mime: 'text/html' },
    { url: 42, kind: 'image' },
  ]) assert.deepEqual(deviceRows({ v: 1, rows: [bad] }, ORIGIN), [], JSON.stringify(bad));
  assert.deepEqual(deviceRows({ v: 2, rows: rowsOf(1) }, ORIGIN), []);
  assert.deepEqual(deviceRows({ v: 1, rows: rowsOf(1) }, ''), [], 'no origin, no rows');
  assert.equal(deviceUrl(same('a'), 'null'), '', 'an opaque origin passes nothing');
  assert.equal(deviceRows({ v: 1, rows: [...rowsOf(1), ...rowsOf(1)] }, ORIGIN).length, 1, 'deduped');
  assert.equal(deviceRows({ v: 1, rows: rowsOf(40) }, ORIGIN).length, MAX_FEED_ROWS, 'capped');
});

test('without the device origin the guard still takes scrolller.com and refuses same-origin rows', () => {
  const remote = 'https://images.scrolller.com/femto/a.jpg';
  assert.deepEqual(feedRows({ v: 1, rows: [{ url: remote, kind: 'image' }, { url: same('a'), kind: 'image' }] }), [{ url: remote, kind: 'image' }]);
  const bridge = fakeBridge(), made = [];
  const feed = createScrolllerFeed({ bridge, makeImage: () => { const i = { removeAttribute() {} }; made.push(i); return i; } });
  bridge.emit('scrolller-feed', { v: 1, rows: [{ url: remote, kind: 'image' }, { url: same('a'), kind: 'image' }] });
  assert.equal(feed.count, 1); assert.equal(made[0].src, remote);
  assert.equal(feed.deviceRows.length, 0, 'the device list stays empty on the web');
  feed.dispose();
});

test('a device feed makes no display element, and one that landed before the init is read again', () => {
  const bridge = fakeBridge(); let made = 0;
  const feed = createScrolllerFeed({ bridge, makeImage: () => { made++; return { removeAttribute() {} }; } });
  bridge.emit('scrolller-feed', { v: 1, rows: rowsOf(3), catalog: [{ id: 'hypno', label: 'Hypno' }] });
  assert.equal(made, 0, 'same-origin rows are refused until the origin is known');
  feed.useDeviceOrigin(ORIGIN);
  assert.equal(feed.deviceRows.length, 3); assert.equal(feed.count, 0); assert.equal(feed.ready, 0);
  assert.equal(feed.sourceFor(0), null); assert.equal(feed.stillFor(0), null);
  assert.equal(made, 0, 'the display-only route stays empty, so its faces stay closed');
  assert.deepEqual(feed.catalog, [{ id: 'hypno', label: 'Hypno' }], 'the Options chips use the catalog');
  const first = feed.deviceRows;
  assert.equal(feed.deviceRows, first, 'the same array until a feed replaces it');
  bridge.emit('scrolller-feed', { v: 1, rows: [{ url: 'https://images.scrolller.com/a.jpg', kind: 'image' }] });
  assert.deepEqual(feed.deviceRows, [], 'a scrolller.com row is refused here');
  bridge.emit('scrolller-feed', { v: 1, rows: rowsOf(2) });
  bridge.emit('setting', { key: 'media.remoteConsent', value: false });
  assert.deepEqual(feed.deviceRows, [], 'consent off drops the rows');
  bridge.emit('scrolller-feed', { v: 1, rows: rowsOf(2) });
  assert.deepEqual(feed.deviceRows, [], 'a late feed under closed consent stays empty');
  feed.dispose();
});

test('a device feed expires after 24 hours', () => {
  const bridge = fakeBridge(); let t = 1000;
  const feed = createScrolllerFeed({ bridge, now: () => t });
  feed.useDeviceOrigin(ORIGIN);
  bridge.emit('scrolller-feed', { v: 1, rows: rowsOf(2) });
  assert.equal(feed.deviceRows.length, 2);
  t += 24 * 60 * 60 * 1000;
  assert.equal(feed.deviceRows.length, 0);
  feed.dispose();
});

test('fitEdge caps the long edge and never enlarges', () => {
  assert.deepEqual(fitEdge(1080, 1920), { w: 360, h: 640 });
  assert.deepEqual(fitEdge(1920, 1080), { w: 640, h: 360 });
  assert.deepEqual(fitEdge(300, 200), { w: 300, h: 200 });
  assert.equal(PICTURE_EDGE, 640);
});

/** A fake renderer side: setSource / swapStill only. */
function fakeMedia({ faces = 9, capacity = 3 } = {}) {
  const slots = Array.from({ length: faces }, (_, i) => ({ id: 'bb-' + i, frame: 'billboard', s: i, phone: true }));
  const shown = new Map(), swaps = [];
  return { slots, shown, swaps, videoCapacity: capacity,
    setSource(id, src) { if (src) shown.set(id, src); else shown.delete(id); return true; },
    swapStill(from, to) {
      swaps.push([from, to]);
      for (const [id, s] of shown) if (s.el === from) shown.set(id, { ...s, el: to });
      return true;
    } };
}
function fixture(options = {}) {
  const media = fakeMedia(options), feed = { deviceRows: options.rows ?? rowsOf(12) };
  const decodes = [], state = { active: true };
  const decode = (url, canvas) => new Promise((resolve, reject) => decodes.push({ url, canvas, resolve, reject }));
  let id = 0;
  const pictures = createDevicePictures(media, { feed, decode, active: () => state.active, makeCanvas: () => ({ id: id++ }) });
  /** Finish the one decode in flight, then let the provider place it. */
  const finish = async () => { decodes.at(-1).resolve(); await settle(); };
  return { media, feed, decodes, state, pictures, finish };
}
const owners = (media) => new Set([...media.shown.values()].map((s) => s.el));

test('owners fill to the tier\'s capacity, one decode at a time, every face borrowed and credited', async () => {
  const { media, decodes, pictures, finish } = fixture();
  pictures.update(1 / 60); pictures.update(1 / 60);
  await settle(); pictures.update(1 / 60);
  assert.equal(decodes.length, 1, 'one decode in flight, however many are wanted');
  for (let k = 1; k <= 3; k++) {
    await finish(); pictures.update(1 / 60); await settle(); pictures.update(1 / 60);
    assert.equal(owners(media).size, k);
    assert.equal(decodes.length, k + 1, 'the next decode only starts once this one is placed');
  }
  assert.equal(media.shown.size, selectScrolllerSlots(media.slots).length, 'every selected face shows a picture');
  assert.ok([...media.shown.values()].every((s) => s.kind === 'canvas' && s.borrowed === true && s.credit === 'scrolller'));
  assert.equal(media.swaps.length, 0);
  pictures.dispose();
});

async function fill(f, n = 3) {
  for (let k = 0; k < n; k++) { f.pictures.update(1 / 60); await settle(); await f.finish(); f.pictures.update(1 / 60); await settle(); }
  f.pictures.update(1 / 60); await settle();
}

test('pictures advance in place on a seven second turn per owner, never past paused, and a ring keeps canvases small', async () => {
  const f = fixture(), { media, pictures, finish } = f;
  await fill(f);
  await finish();
  pictures.update(TURN_S / 3 - .1);
  assert.equal(media.swaps.length, 0, 'not due yet');
  pictures.update(1, true);
  assert.equal(media.swaps.length, 0, 'a pause moves nothing');
  pictures.update(.2);
  assert.equal(media.swaps.length, 1, 'one owner changes');
  assert.equal(media.shown.size, selectScrolllerSlots(media.slots).length, 'no face drops to a placeholder');
  assert.equal(owners(media).size, 3);
  for (let k = 0; k < 11; k++) { await settle(); pictures.update(1 / 60); await settle(); await finish(); pictures.update(TURN_S / 3 + .01); }
  assert.equal(media.swaps.length, 12);
  assert.equal(pictures.stats().canvases, 4, 'owners + the one being decoded, reused');
  pictures.dispose();
});

test('Gentle Visuals doubles the turn', async () => {
  const f = fixture(); await fill(f); await f.finish();
  f.pictures.update(TURN_S / 3 + .1, false, true);
  assert.equal(f.media.swaps.length, 0);
  f.pictures.update(GENTLE_TURN_S / 3 - TURN_S / 3, false, true);
  assert.equal(f.media.swaps.length, 1);
  f.pictures.dispose();
});

test('a short feed does not rotate, and a flash never gets the same picture twice', async () => {
  const f = fixture({ rows: rowsOf(1) }); await fill(f);
  assert.equal(owners(f.media).size, 3, 'one row still fills the owners');
  assert.equal(f.pictures.flashCount, 1, 'but counts as one distinct picture');
  assert.equal(f.pictures.sourceForFlash(0), f.pictures.sourceForFlash(5));
  const before = f.decodes.length;
  f.pictures.update(60); await settle(); f.pictures.update(60); await settle();
  assert.equal(f.decodes.length, before, 'nothing more to decode');
  assert.equal(f.media.swaps.length, 0);
  f.pictures.dispose();

  const g = fixture({ rows: rowsOf(12) }); await fill(g);
  assert.equal(g.pictures.flashCount, 3);
  const distinct = new Set([0, 1, 2].map((s) => g.pictures.sourceForFlash(s)));
  assert.equal(distinct.size, 3, 'consecutive seeds walk distinct pictures');
  g.pictures.dispose();
});

test('My files, Off and closed consent clear every face at once, paused too; a later pick comes back', async () => {
  const f = fixture(); await fill(f);
  f.state.active = false;   // boot: shared.filesOn or source off
  f.pictures.update(0, true);
  assert.equal(f.media.shown.size, 0);
  assert.equal(f.pictures.flashCount, 0); assert.equal(f.pictures.sourceForFlash(0), null);
  f.state.active = true;
  f.feed.deviceRows = [];   // the host's consent gate
  f.pictures.sync();
  assert.equal(f.media.shown.size, 0);
  f.feed.deviceRows = rowsOf(4, 'n');
  await fill(f);
  assert.ok(f.media.shown.size > 0, 'pictures return');
  f.pictures.dispose();
  assert.equal(f.media.shown.size, 0, 'a course switch releases everything');
});

test('a later feed replaces the list at once, paused too, and a late decode from the old list is dropped', async () => {
  const f = fixture(); await fill(f);
  const inFlight = f.decodes.at(-1);
  f.feed.deviceRows = rowsOf(2, 'n');
  f.pictures.update(1 / 60, true);
  assert.equal(f.media.shown.size, 0, 'every old picture left the screens while paused');
  inFlight.resolve(); await settle();
  f.pictures.update(1 / 60); await settle();
  assert.equal(f.decodes.at(-1).url, same('n0'), 'the old decode never lands, the new list starts');
  for (const name of ['n0', 'n1', 'n0']) {
    assert.equal(f.decodes.at(-1).url, same(name));
    await f.finish(); f.pictures.update(1 / 60); await settle(); f.pictures.update(1 / 60); await settle();
  }
  assert.equal(f.pictures.stats().owners, 3);
  assert.equal(f.pictures.flashCount, 2, 'only the new list is on the screens');
  const decoded = f.decodes.length;
  f.pictures.update(60); await settle();
  assert.equal(f.decodes.length, decoded, 'then it rests: two rows fit three owners');
  f.pictures.dispose();
});

test('a dropped row leaves at once, kept rows stay, and a repeated feed decodes and uploads nothing', async () => {
  const f = fixture({ rows: rowsOf(3) }); await fill(f);
  assert.equal(f.pictures.stats().owners, 3);
  const decoded = f.decodes.length, shown = f.media.shown.size;
  f.feed.deviceRows = rowsOf(3);   // the same urls in a new array
  f.pictures.update(1 / 60); await settle(); f.pictures.update(TURN_S * 2); await settle();
  assert.equal(f.decodes.length, decoded, 'no decode');
  assert.equal(f.media.swaps.length, 0, 'no upload');
  assert.equal(f.media.shown.size, shown);
  const kept = rowsOf(3).filter((_, i) => i !== 1), gone = [...f.media.shown.values()].map((s) => s.el);
  f.feed.deviceRows = kept;
  f.pictures.update(0, true);
  assert.equal(f.pictures.stats().owners, 2, 'the dropped row left while paused');
  assert.ok(f.media.shown.size < shown, 'its faces are back on the Loom');
  assert.ok(gone.some((c) => c.width === 1 && c.height === 1), 'its canvas keeps no pixels');
  assert.equal(f.decodes.length, decoded, 'kept pictures are not decoded again');
  f.pictures.update(1 / 60); await settle();
  assert.equal(f.decodes.at(-1).url, same('p0'), 'an empty owner refills from a kept row');
  await f.finish(); f.pictures.update(1 / 60); await settle();
  assert.equal(f.pictures.stats().owners, 3, 'a hole in the owners is refilled on its own faces');
  assert.equal(new Set([...f.media.shown.keys()]).size, f.media.shown.size);
  assert.equal(f.media.shown.size, shown, 'every selected face is back');
  f.pictures.dispose();
});

test('a flash card keeps its picture through a burst, and a takedown still wins at once', async () => {
  const f = fixture(); await fill(f); await f.finish();
  let busy = true;
  f.pictures.holdFlash(() => busy);
  const seeds = [0, 1, 2], before = seeds.map((s) => f.pictures.sourceForFlash(s));
  assert.equal(new Set(before).size, 3, 'no still repeats in a burst');
  f.pictures.update(TURN_S * 4); await settle();
  assert.equal(f.media.swaps.length, 0, 'no swap while a burst is live');
  assert.deepEqual(seeds.map((s) => f.pictures.sourceForFlash(s)), before, 'each card keeps its canvas');
  const url = (c) => [...f.media.shown.values()].find((s) => s.el === c);
  assert.ok(url(before[1]), 'the card shows what a screen shows');
  f.feed.deviceRows = f.feed.deviceRows.filter((r) => r.url !== same('p1'));   // a takedown mid-burst
  assert.equal(f.pictures.sourceForFlash(1), null, 'the taken-down card answers null at once');
  assert.equal(f.pictures.sourceForFlash(0), before[0], 'the others keep theirs');
  assert.equal(f.pictures.sourceForFlash(2), before[2]);
  assert.equal(f.pictures.flashCount, 2);
  busy = false;
  f.pictures.update(1 / 60); await settle();
  f.pictures.update(TURN_S); await settle();
  assert.ok(f.pictures.stats().swaps + f.pictures.stats().owners >= 2, 'placing resumes after the burst');
  f.pictures.dispose();
});

test('the off switch and a course switch leave no picture pixels in any canvas', async () => {
  const made = [], media = fakeMedia(), feed = { deviceRows: rowsOf(12) }, decodes = [];
  const decode = (url, canvas) => new Promise((resolve) => { canvas.width = 360; canvas.height = 640; decodes.push(resolve); });
  const pictures = createDevicePictures(media, { feed, decode, makeCanvas: () => { const c = { width: 300, height: 150 }; made.push(c); return c; } });
  const step = async () => { pictures.update(1 / 60); await settle(); decodes.at(-1)?.(); await settle(); pictures.update(1 / 60); await settle(); };
  for (let i = 0; i < 4; i++) await step();
  assert.ok(made.length >= 3);
  assert.ok(made.some((c) => c.width === 360));
  feed.deviceRows = [];
  pictures.update(0, true);
  assert.ok(made.every((c) => c.width === 1 && c.height === 1), 'off: every canvas is 1x1');
  feed.deviceRows = rowsOf(12);
  for (let i = 0; i < 4; i++) await step();
  assert.ok(made.some((c) => c.width === 360));
  const pending = decodes.length; pictures.update(1 / 60); await settle();
  pictures.dispose();
  decodes.at(-1)?.(); decodes[pending - 1]?.(); await settle();
  assert.ok(made.every((c) => c.width === 1 && c.height === 1), 'dispose: the ring and a late decode are 1x1 too');
  assert.equal(media.shown.size, 0);
});

test('decodeInto: a hung decode gives up, clears the source and frees the provider; crossOrigin is set', async () => {
  assert.equal(DECODE_TIMEOUT_MS, 10000);
  const hung = { removed: 0, removeAttribute(k) { if (k === 'src') { this.src = ''; this.removed++; } }, decode: () => new Promise(() => {}) };
  await assert.rejects(decodeInto(same('a'), {}, { makeImage: () => hung, timeoutMs: 5 }), /timed out/);
  assert.equal(hung.src, '', 'the source is cleared');
  assert.equal(hung.crossOrigin, 'anonymous');
  const drawn = [], img = { naturalWidth: 1080, naturalHeight: 1920, removeAttribute() {}, decode: () => Promise.resolve() };
  const canvas = { getContext: () => ({ drawImage: (...a) => drawn.push(a) }) }, cib = globalThis.createImageBitmap;
  globalThis.createImageBitmap = () => Promise.reject(new Error('no options on old WebKit'));
  try { await decodeInto(same('a'), canvas, { makeImage: () => img }); } finally { globalThis.createImageBitmap = cib; }
  assert.equal(img.crossOrigin, 'anonymous');
  assert.deepEqual([canvas.width, canvas.height], [360, 640]);
  assert.equal(drawn[0][0], img, 'the decoded image draws when the bitmap route refuses');

  // A provider built on it recovers from the hang and moves to the next row.
  const media = fakeMedia(), images = [];
  const pictures = createDevicePictures(media, { feed: { deviceRows: rowsOf(2) }, makeCanvas: () => ({}),
    decode: (url, c) => decodeInto(url, c, { timeoutMs: 5, makeImage: () => { const h = { url, removeAttribute() {}, decode: () => new Promise(() => {}) }; images.push(h); return h; } }) });
  pictures.update(1 / 60); await settle();
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(pictures.stats().decoding, false, 'loading is not jammed');
  pictures.update(2); await settle();
  assert.equal(images.length, 2, 'the next row is tried');
  pictures.dispose();
  await new Promise((r) => setTimeout(r, 20));
});

test('consent: the press asks on the device origin only, a declined ask is heard once, web frames are unchanged', () => {
  const web = fakeBridge(), wf = createScrolllerFeed({ bridge: web });
  wf.setConsent(true); wf.setConsent(true, true); wf.setConsent(false);
  assert.deepEqual(web.sent, [
    { type: 'set-setting', key: 'media.remoteConsent', value: true },
    { type: 'set-setting', key: 'media.remoteConsent', value: true },
    { type: 'set-setting', key: 'media.remoteConsent', value: false }], 'never an ask without the device origin');
  wf.dispose();

  const b = fakeBridge(), feed = createScrolllerFeed({ bridge: b }); let no = 0;
  feed.useDeviceOrigin(ORIGIN); feed.onDeclined(() => no++);
  feed.setConsent(false, true);
  assert.deepEqual(b.sent.at(-1), { type: 'set-setting', key: 'media.remoteConsent', value: false }, 'off never asks');
  b.emit('setting', { key: 'media.remoteConsent', value: false });
  assert.equal(no, 0, 'false with no pending ask is not a decline');
  feed.setConsent(true, true);
  assert.deepEqual(b.sent.at(-1), { type: 'set-setting', key: 'media.remoteConsent', value: true, ask: true });
  b.emit('setting', { key: 'media.remoteConsent', value: false });
  assert.equal(no, 1, 'a declined sheet');
  b.emit('setting', { key: 'media.remoteConsent', value: false });
  assert.equal(no, 1, 'the ask is answered once');
  feed.setConsent(true, true);
  b.emit('setting', { key: 'media.remoteConsent', value: true });
  assert.equal(no, 1, 'yes is not a decline');
  assert.equal(feed.consent, true);
  feed.dispose();
});

test('the boot never raises a device consent on its own, and a decline turns the course pick off', () => {
  const boot = read('boot.js').replace(/\r\n/g, '\n');
  assert.match(boot, /if \(!consentOn\(\) && !deviceOrigin\) scrolller\.setConsent\(true\);/);
  assert.equal(boot.match(/setConsent\(true\)/g).length, 1, 'that guarded line is the only unasked raise');
  assert.match(boot, /scrolller\.setConsent\(to === 'scrolller', deviceOrigin\)/);
  assert.match(boot, /onDeclined\(\(\) => \{\s*if \(coursePick\.source === 'scrolller'\) \{ coursePick\.source = 'off'; saveCustomization\(shared\.coursePicks\); refreshMedia\(\); \}/);
  assert.match(boot, /devicePictures\?\.update\(frameDt, frozen \|\| state === 'menu' \|\| lights\.covered \|\| !!\(deferred && !deferred\.done\),/, 'no decode or swap while nothing draws the world');
  assert.match(boot, /holdFlash\(\(\) => cueBurst\.busy\)/);
});

test('svg urls are refused whatever the mime says', () => {
  for (const url of [same('a').replace('.jpg', '.svg'), same('a').replace('.jpg', '.SVG'), same('a').replace('.jpg', '.svg?x=1'), same('a').replace('.jpg', '.svg#f')])
    assert.deepEqual(deviceRows({ v: 1, rows: [{ url, kind: 'image', mime: 'image/png' }] }, ORIGIN), [], url);
  assert.equal(deviceRows({ v: 1, rows: [{ url: same('a').replace('.jpg', '.svgz.jpg'), kind: 'image' }] }, ORIGIN).length, 1);
});

test('the tick allocates no flash list: the same answers come from one cached list', async () => {
  const f = fixture(); await fill(f);
  const src = read('host/devicePictures.js');
  assert.ok(!/flashable|new Set/.test(src), 'no list rebuilt per call');
  assert.equal(f.pictures.sourceForFlash(4), f.pictures.sourceForFlash(4));
  f.pictures.dispose();
});

test('the phone does not poll the dev manifest; the default is untouched', () => {
  const realFetch = globalThis.fetch, realLocation = Object.getOwnPropertyDescriptor(globalThis, 'location'); let polls = 0;
  globalThis.fetch = async () => { polls++; return { ok: false }; };
  Object.defineProperty(globalThis, 'location', { value: { origin: ORIGIN, hostname: '127.0.0.1' }, configurable: true });
  try {
    const off = createCardMedia(fakeMedia(), { bridge: fakeBridge(), local: false });
    assert.equal(polls, 0, 'local: false never polls');
    off.dispose();
    const dev = createCardMedia(fakeMedia(), { bridge: fakeBridge() });
    assert.equal(polls, 1, 'the default on loopback is the dev adapter, as before');
    dev.dispose();
  } finally {
    globalThis.fetch = realFetch;
    if (realLocation) Object.defineProperty(globalThis, 'location', realLocation); else delete globalThis.location;
  }
  assert.match(read('boot.js'), /\.\.\.\(deviceOrigin \? \{ local: false \} : null\)/);
});

test('the song energy row follows the GPU table limit and keeps its clock', () => {
  const song = { energy: { fps: 25, mix: new Uint8Array(6000), vocals: new Uint8Array(6000), drums: new Uint8Array(6000), bass: new Uint8Array(6000) } };
  try {
    setTrackTableLimit(4096);
    assert.equal(trackTableMax(), 4096);
    const small = energyTexture(song);
    assert.equal(small.width, 4096);
    assert.ok(Math.abs(small.fps - 25 * 4096 / 6000) < 1e-9, 'eFps scales so the row still spans the song');
    setTrackTableLimit(8192);
    const full = energyTexture(song);
    assert.equal(full.width, 6000); assert.equal(full.fps, 25);
    small.texture.dispose(); full.texture.dispose();
  } finally { setTrackTableLimit(8192); }
  assert.match(read('render/renderer.js'), /setTrackTableLimit\(three\.backend\?\.isWebGPUBackend \?/);
});

test('a broken row is skipped, and a wholly broken feed waits instead of spinning', async () => {
  const f = fixture({ rows: rowsOf(2) });
  f.pictures.update(1 / 60); await settle();
  f.decodes[0].reject(new Error('404')); await settle();
  f.pictures.update(.5); await settle();
  assert.equal(f.decodes.length, 1, 'a short wait after a failure');
  f.pictures.update(.6); await settle();
  assert.equal(f.decodes.length, 2); assert.equal(f.decodes[1].url, same('p1'));
  f.decodes[1].reject(new Error('404')); await settle();
  f.pictures.update(10); await settle();
  assert.equal(f.decodes.length, 2, 'all rows failed: long wait');
  f.pictures.update(21); await settle();
  assert.equal(f.decodes.length, 3);
  f.pictures.dispose();
});

// The real screens: same owners as a clip, one texture and material per owner, one upload per picture.
const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k === 'createLinearGradient' || k === 'createRadialGradient' ? () => gradient : () => {} });
const make = (width, height) => ({ width, height, getContext: () => context });
function realMedia(count = 9) {
  const extraSlots = Array.from({ length: count }, (_, i) => ({ id: 'card-' + i, frame: 'card', kind: 'landscape', s: 5, side: 1, w: 16, h: 9,
    C: [i, 8, 0], X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1], phone: true }));
  return createMediaSlots(null, { id: 'test' }, { layout: {}, samples: [], extraSlots, make, tier: 'high' });
}
const still = (w = 360, h = 640) => ({ width: w, height: h, getContext: () => context });

test('real screens: a borrowed still shares a prewarmed owner, goes up once, and swaps in place with a new crop', () => {
  const media = realMedia(), a = still(), b = still(640, 360);
  for (let i = 0; i < 4; i++) assert.equal(media.setSource('card-' + i, { kind: 'canvas', el: a, borrowed: true, credit: 'scrolller' }), true);
  const face = () => media.object.children.find((c) => c.name === 'media-card-source-card-0');
  const material = face().material, tex = face().userData.mediaTex;
  assert.equal(media.stats().stillOwners, 1);
  assert.equal(media.object.children.find((c) => c.name === 'media-card-source-card-3').material, material, 'one material per owner');
  assert.equal(media.stats().readyStillFaces, 0, 'the first upload is budgeted, not immediate');
  media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(media.stats().readyStillFaces, 4);
  assert.equal(tex.image, a); assert.equal(media.stats().sharedVideoUploadsTotal, 1);
  for (let i = 0; i < 30; i++) media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(media.stats().sharedVideoUploadsTotal, 1, 'a still is one upload, not a video rate');
  const crop = face().userData.mediaCrop, cropBefore = crop.toArray();

  assert.equal(media.swapStill(a, b), true);
  assert.equal(media.stats().readyStillFaces, 4, 'faces keep the old pixels: no placeholder between pictures');
  assert.equal(tex.image, a); assert.deepEqual(crop.toArray(), cropBefore, 'old pixels, old crop until the upload');
  media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(tex.image, b); assert.equal(face().material, material, 'same warmed material');
  assert.equal(face().userData.mediaTex, tex, 'same texture object');
  assert.notDeepEqual(crop.toArray(), cropBefore, 'the crop follows the new aspect');
  assert.equal(media.stats().sharedVideoUploadsTotal, 2);
  media.update(0, { eye: [0, 8, 0], paused: true });
  assert.equal(media.swapStill(b, a), true);
  media.update(0, { eye: [0, 8, 0], paused: true });
  assert.equal(tex.image, b, 'nothing goes up while paused');
  media.dispose();
});

test('real screens: a clip is not swappable, and stills cap at the tier capacity like clips', () => {
  const media = realMedia(8);
  const clip = { readyState: 2, videoWidth: 640, videoHeight: 360, removeAttribute() {}, addEventListener() {}, removeEventListener() {}, pause() {}, load() {}, play: () => Promise.resolve() };
  media.setSource('card-0', { kind: 'video', el: clip, borrowed: true, credit: 'scrolller' });
  assert.equal(media.swapStill(clip, still()), false);
  const capacity = media.videoCapacity;
  let taken = 1;
  for (let i = 1; i < 8; i++) if (media.setSource('card-' + i, { kind: 'canvas', el: still(), borrowed: true, credit: 'scrolller' })) taken++;
  assert.equal(taken, capacity, 'the owner limit holds');
  assert.equal(media.stats().sharedVideoTextures, capacity);
  media.dispose();
});

test('the provider on real screens: faces, owners and a turn', async () => {
  const media = realMedia(9), feed = { deviceRows: rowsOf(12) };
  const pictures = createDevicePictures(media, { feed, decode: async (url, canvas) => { canvas.width = 360; canvas.height = 640; }, makeCanvas: () => ({ getContext: () => context }) });
  for (let i = 0; i < 12; i++) { pictures.update(1 / 60); await settle(); media.update(1 / 60, { eye: [0, 8, 0] }); }
  const selected = selectScrolllerSlots(media.slots).length, s = media.stats();
  assert.equal(s.stillOwners, Math.min(media.videoCapacity, selected));
  assert.equal(s.readyStillFaces, selected, 'every selected face shows a real picture');
  const uploads = s.sharedVideoUploadsTotal;
  pictures.update(TURN_S); media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(media.stats().sharedVideoUploadsTotal, uploads + 1, 'the turn cost one upload, whatever the face count');
  pictures.dispose();
  assert.equal(media.stats().stillOwners, 0, 'textures go back to the pool on a course switch');
  media.dispose();
});

test('wiring: boot only flips on the device origin, keeps the display route for My files, and no hash input moved', () => {
  const boot = read('boot.js');
  assert.match(boot, /init\.caps\.scrolller\?\.origin === 'device'/);
  assert.match(boot, /deviceOrigin && [^\n]*createDevicePictures\(world\.media/);
  assert.match(boot, /active: \(\) => !shared\.filesOn && coursePick\.source === 'scrolller'/);
  assert.match(boot, /setDisplayFeed\(pictures, renderer\.canvas\)/);
  const provider = read('host/devicePictures.js');
  assert.ok(!/\bfetch\(/.test(provider), 'the provider never fetches');
  assert.ok(!/feel\.json|localStorage|indexedDB|caches\./.test(provider), 'no feel input and no stored copy of a picture');
});
