// The web host's Scrolller pictures (host/scrolllerFeed.js): display only, flash bursts only, consent by echo.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { feedRows, createScrolllerFeed, MAX_FEED_ROWS } from '../host/scrolllerFeed.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

function fakeBridge() {
  const handlers = new Map(), sent = [];
  return {
    sent,
    on(type, fn) { handlers.set(type, fn); return () => handlers.delete(type); },
    send(m) { sent.push(m); },
    emit(type, m) { handlers.get(type)?.({ type, ...m }); },
    has(type) { return handlers.has(type); },
  };
}
const fakeImage = () => ({ complete: false, naturalWidth: 0, src: '', removeAttribute(k) { if (k === 'src') this.src = ''; } });

test('feed rows: v1, https scrolller hosts, stills and clips, deduped, capped', () => {
  const ok = 'https://images.scrolller.com/femto/a-640x360.jpg';
  assert.deepEqual(feedRows({ v: 1, rows: [{ url: ok, kind: 'image' }] }), [{ url: ok, kind: 'image' }]);
  const clip = 'https://iris.scrolller.com/a.mp4';
  assert.deepEqual(feedRows({ v: 1, rows: [{ url: clip, kind: 'video', poster: ok }] }), [{ url: clip, kind: 'video', poster: ok }]);
  assert.deepEqual(feedRows({ v: 1, rows: [{ url: clip, kind: 'video', poster: 'https://evil.example/p.jpg' }] }),
    [{ url: clip, kind: 'video' }], 'an off-host poster is dropped, the clip kept');
  for (const bad of [
    { url: 'http://images.scrolller.com/a.jpg', kind: 'image' },
    { url: 'https://evil.example/a.jpg', kind: 'image' },
    { url: 'https://scrolller.com.evil.example/a.jpg', kind: 'image' },
    { url: 'https://evil.example/a.mp4', kind: 'video' },
    { url: 'http://iris.scrolller.com/a.mp4', kind: 'video' },
    { url: 'https://iris.scrolller.com/a.mp4', kind: 'loop' },
    { url: 'blob:https://app.cclabs.app/x', kind: 'image' },
    { url: 42, kind: 'image' },
  ]) assert.deepEqual(feedRows({ v: 1, rows: [bad] }), [], JSON.stringify(bad));
  assert.deepEqual(feedRows({ v: 2, rows: [{ url: ok, kind: 'image' }] }), []);
  assert.equal(feedRows({ v: 1, rows: [{ url: ok, kind: 'image' }, { url: ok, kind: 'image' }] }).length, 1);
  const many = Array.from({ length: 40 }, (_, i) => ({ url: `https://images.scrolller.com/p${i}.jpg`, kind: 'image' }));
  assert.equal(feedRows({ v: 1, rows: many }).length, MAX_FEED_ROWS);
});

test('only loaded pictures are handed out; a closed consent drops them; the switch asks the host', () => {
  const bridge = fakeBridge();
  const feed = createScrolllerFeed({ bridge, makeImage: fakeImage });
  assert.equal(feed.sourceFor(0), null);
  bridge.emit('scrolller-feed', { v: 1, rows: [
    { url: 'https://images.scrolller.com/a.jpg', kind: 'image' }, { url: 'https://images.scrolller.com/b.jpg', kind: 'image' }] });
  assert.equal(feed.count, 2);
  assert.equal(feed.sourceFor(0), null, 'nothing loaded yet');
  // the browser finished one picture
  const seen = [];
  for (let s = 0; s < 4; s++) { const p = feed.sourceFor(s); if (p) seen.push(p); }
  assert.equal(seen.length, 0);
  feed.dispose();

  const b2 = fakeBridge(), imgs = [];
  const f2 = createScrolllerFeed({ bridge: b2, makeImage: () => { const i = fakeImage(); imgs.push(i); return i; } });
  b2.emit('scrolller-feed', { v: 1, rows: [{ url: 'https://images.scrolller.com/a.jpg', kind: 'image' }] });
  imgs[0].complete = true; imgs[0].naturalWidth = 640;
  assert.equal(f2.sourceFor(7), imgs[0]);
  const heard = [];
  f2.subscribe((on) => heard.push(on));
  b2.emit('setting', { key: 'media.remoteConsent', value: false });
  assert.equal(f2.consent, false);
  assert.deepEqual(heard, [false]);
  assert.equal(f2.sourceFor(0), null, 'consent off drops the pictures at once');
  assert.equal(imgs[0].src, '', 'and releases them');
  b2.emit('setting', { key: 'media.niches', value: ['hypno'] });
  assert.deepEqual(heard, [false], 'other keys are not this switch');
  f2.setConsent(true);
  assert.deepEqual(b2.sent.at(-1), { type: 'set-setting', key: 'media.remoteConsent', value: true });
  f2.dispose();
  assert.equal(b2.has('scrolller-feed'), false);
});

test('Scrolller pictures reach display-only flashes and world windows, never a texture path', () => {
  const boot = src('boot.js');
  assert.doesNotMatch(boot, /scrolller(Feed)?\??\.sourceFor/, 'world clips reach only the display-only screens');
  const uses = boot.split('\n').filter((l) => /scrolller(Feed)?\??\.stillFor/.test(l));
  assert.equal(uses.length, 1, 'one consumer of stills');
  assert.match(uses[0], /createFlashBurst\(\{ source:/);
  assert.doesNotMatch(boot, /setItemMediaSource\([^)]*scrolller/i);
  assert.doesNotMatch(boot, /createCardMedia\([^)]*scrolller/i);
  assert.match(boot, /setDisplayFeed\(scrolller, renderer.canvas\)/);
  const feed = src('host/scrolllerFeed.js');
  assert.doesNotMatch(feed, /\bfetch\(|Texture|getImageData|toDataURL|crossOrigin/);
  // the flash canvas is a page canvas that is never read back
  assert.doesNotMatch(src('fx/flashMedia.js'), /getImageData|toDataURL|Texture/);
});

test('the Options switch: drawn only where the host offers the feed, echo-driven, plain copy', () => {
  const cards = src('hud/cards.js');
  assert.match(cards, /host\.scrolller \? '<div class="c-scrolller">' \+ scrolllerHtml\(\)/);
  assert.match(cards, /<span>Online pictures<small>From Scrolller, on screens and in flashes<\/small>/);
  assert.match(cards, /emit\('scrolller', \{ on: !host\.scrolller\.on \}\)/);
  for (const l of cards.split('\n').filter((x) => /Online pictures|Pictures on|Track default|this track/i.test(x)))
    assert.doesNotMatch(l, /[—–!]/, l);
  assert.match(src('boot.js'), /init\.caps\.scrolller \? shared\.scrolllerFeed : null/);
});

test('display-only pictures expire in 24 hours and a late feed cannot reopen a closed switch', () => {
  const bridge = fakeBridge(), imgs = []; let clock = 10;
  const feed = createScrolllerFeed({ bridge, now: () => clock, makeImage: () => {
    const img = fakeImage(); img.complete = true; img.naturalWidth = 640; imgs.push(img); return img;
  } });
  const msg = { v: 1, rows: [{ url: 'https://images.scrolller.com/a.jpg', kind: 'image' }] };
  bridge.emit('scrolller-feed', msg);
  assert.equal(feed.ready, 1);
  clock += 24 * 60 * 60 * 1000;
  assert.equal(feed.sourceFor(0), null); assert.equal(feed.count, 0); assert.equal(imgs[0].src, '');
  bridge.emit('setting', { key: 'media.remoteConsent', value: false });
  bridge.emit('scrolller-feed', msg);
  assert.equal(feed.ready, 0); assert.equal(feed.sourceFor(0), null); feed.dispose();
});

const fakeVideo = () => ({ tagName: 'VIDEO', readyState: 0, error: null, src: '', poster: '', preload: '', loads: 0,
  removeAttribute(k) { this[k] = ''; }, load() { this.loads++; } });

test('clips: metadata-only <video> per clip row, world faces get them, flashes never do, consent off frees the decoder', () => {
  const bridge = fakeBridge(), imgs = [], vids = []; let clock = 0;
  const feed = createScrolllerFeed({ bridge, now: () => clock,
    makeImage: () => { const i = fakeImage(); imgs.push(i); return i; },
    makeVideo: () => { const v = fakeVideo(); vids.push(v); return v; } });
  bridge.emit('scrolller-feed', { v: 1, rows: [
    { url: 'https://iris.scrolller.com/a.mp4', kind: 'video', poster: 'https://images.scrolller.com/a.jpg' },
    { url: 'https://images.scrolller.com/b.jpg', kind: 'image' }] });
  assert.equal(feed.count, 2); assert.equal(vids.length, 1); assert.equal(imgs.length, 1);
  assert.equal(vids[0].preload, 'metadata'); assert.equal(vids[0].poster, 'https://images.scrolller.com/a.jpg');
  assert.equal(vids[0].crossOrigin, undefined, 'display only: no CORS ask');
  assert.equal(feed.sourceFor(0), null, 'no metadata yet');
  vids[0].readyState = 1;
  assert.equal(feed.sourceFor(0), vids[0]);
  assert.equal(feed.stillFor(0), null, 'a clip never reaches the flash canvas');
  imgs[0].complete = true; imgs[0].naturalWidth = 640;
  for (let s = 0; s < 6; s++) assert.equal(feed.stillFor(s), imgs[0]);
  vids[0].error = { code: 4 };
  assert.equal(feed.ready, 1, 'a broken clip is not handed out');
  vids[0].error = null;
  bridge.emit('setting', { key: 'media.remoteConsent', value: false });
  assert.equal(feed.sourceFor(0), null);
  assert.deepEqual([vids[0].src, vids[0].poster, vids[0].loads], ['', '', 1], 'src gone and load() frees the decoder');
  bridge.emit('setting', { key: 'media.remoteConsent', value: true });
  bridge.emit('scrolller-feed', { v: 1, rows: [{ url: 'https://iris.scrolller.com/c.mp4', kind: 'video' }] });
  vids[1].readyState = 4;
  assert.equal(feed.sourceFor(0), vids[1]);
  clock += 24 * 60 * 60 * 1000;
  assert.equal(feed.sourceFor(0), null); assert.equal(vids[1].src, ''); assert.equal(vids[1].loads, 1, '24 h expiry frees clips too');
  feed.dispose();
});
