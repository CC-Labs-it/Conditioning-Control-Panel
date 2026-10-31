import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createBridge } from '../host/bridge.js';
import { createHostScrolllerFeed } from '../host/scrolllerFeed.js';
import { createCards } from '../hud/cards.js';

// src and poster assignments are browser requests too, even without fetch().
test('Linux init refuses early and late Scrolller stills, clips and posters', async t => {
  const requests = [], sent = [];
  t.mock.method(globalThis, 'fetch', async url => { requests.push(url); throw Error('unexpected fetch'); });
  for (const disabled of [true, false]) {
    let message;
    const win = { chrome: { webview: { postMessage: m => sent.push(m),
      addEventListener: (_, fn) => { message = fn; } } },
      setInterval: () => 1, clearInterval() {}, setTimeout: () => 2, clearTimeout() {} };
    const bridge = createBridge({ win });
    const media = () => ({ set src(url) { requests.push(url); }, set poster(url) { requests.push(url); },
      removeAttribute() {}, load() {} });
    const pending = createHostScrolllerFeed({ bridge, makeImage: media, makeVideo: media });
    const feed = { type: 'scrolller-feed', v: 1, rows: [
      { kind: 'image', url: 'https://images.scrolller.com/still.jpg' },
      { kind: 'video', url: 'https://iris.scrolller.com/clip.mp4', poster: 'https://images.scrolller.com/poster.jpg' }] };
    const emit = m => message({ data: m });
    emit(feed);
    assert.equal(requests.length, 0, 'no media before the capability arrives');
    emit({ type: 'init', caps: { scrolller: disabled ? false : { consent: true } } });
    const result = await pending;
    emit({ type: 'setting', key: 'media.remoteConsent', value: true });
    emit(feed);
    if (disabled) {
      assert.equal(result, null);
      assert.deepEqual(requests, [], 'Linux must request no Scrolller URL');
      assert.deepEqual(sent, [], 'no remote consent or niche requests');
    } else {
      assert.equal(requests.length, 6, 'positive control: early and late media really load');
      result.dispose();
    }
    bridge.dispose();
  }
});

test('init timeout keeps pictures and Options closed through a late Linux init', async t => {
  const requests = [], sent = [];
  t.mock.method(globalThis, 'fetch', async url => { requests.push(url); throw Error('unexpected fetch'); });
  let message, timeout;
  const win = { chrome: { webview: { postMessage: m => sent.push(m),
    addEventListener: (_, fn) => { message = fn; } } },
    setInterval: () => 1, clearInterval() {},
    setTimeout(fn, ms) { assert.equal(ms, 3000); timeout = fn; return 2; }, clearTimeout() {} };
  const bridge = createBridge({ win });
  t.after(() => bridge.dispose());
  const media = () => ({ set src(url) { requests.push(url); }, set poster(url) { requests.push(url); },
    removeAttribute() {}, load() {} });
  const pending = createHostScrolllerFeed({ bridge, makeImage: media, makeVideo: media });
  const emit = data => message({ data });
  const feed = { type: 'scrolller-feed', v: 1, rows: [
    { kind: 'image', url: 'https://images.scrolller.com/early.jpg' },
    { kind: 'video', url: 'https://iris.scrolller.com/clip.mp4', poster: 'https://images.scrolller.com/poster.jpg' }] };
  emit(feed);
  timeout(); // resolve bridge.init with browser defaults, with no Scrolller capability
  const result = await pending;
  t.after(() => result?.dispose());
  assert.deepEqual(requests, [], 'timeout must not load the buffered feed');
  const card = element(), cards = createCards(card, { doc: element(), emit() {} });
  t.after(() => cards.dispose());
  const assertClosed = () => {
    cards.setScrolller({ scrolllerAvailable: !!result, source: 'off' });
    cards.show('options');
    const html = card.querySelector('.card-extra').innerHTML;
    assert.doesNotMatch(html, /data-source="scrolller"/);
    assert.match(html, /data-source="files"[^>]*>My files/);
    assert.match(html, /data-source="off"[^>]*>Off/);
    assert.deepEqual(requests, [], 'no src, poster or fetch requests');
  };
  assertClosed();
  emit(feed); // media after timeout, before the disabling init
  emit({ type: 'init', caps: { scrolller: false } });
  emit({ type: 'setting', key: 'media.remoteConsent', value: true });
  emit(feed); // media after the late Linux init
  assertClosed();
  assert.equal(result, null);
  assert.equal(sent.some(m => m.type === 'set-setting' || m.type === 'scrolller-niches'), false);
});

function element() {
  const nodes = new Map();
  return { dataset: {}, style: {}, hidden: false, innerHTML: '', textContent: '', offsetWidth: 1,
    classList: { add() {}, remove() {}, toggle() {}, contains: () => false },
    querySelector(s) { if (!nodes.has(s)) nodes.set(s, element()); return nodes.get(s); },
    querySelectorAll: () => [], addEventListener() {}, removeEventListener() {},
    setAttribute() {}, removeAttribute() {}, remove() {}, focus() {}, contains: () => false };
}

test('Options on Linux offers My files and Off, with the local Choose action', () => {
  const card = element(), doc = element();
  const cards = createCards(card, { doc, emit() {} });
  try {
    for (const available of [false, true]) {
      cards.setScrolller({ scrolllerAvailable: available, source: 'files', files: 0 });
      cards.show('options');
      const html = card.querySelector('.card-extra').innerHTML;
      assert.match(html, /data-source="files"[^>]*>My files/);
      assert.match(html, /data-source="off"[^>]*>Off/);
      assert.match(html, /data-act="scrolller-files"[^>]*>Choose/);
      assert.equal(html.includes('data-source="scrolller"'), available);
    }
  } finally { cards.dispose(); }
});

test('boot uses the capability-gated feed and carries the Options capability', () => {
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.match(boot, /shared.scrolllerFeedTask = createHostScrolllerFeed/);
  assert.match(boot, /shared.scrolllerFeed = await shared.scrolllerFeedTask/);
  assert.match(boot, /scrolllerAvailable: !!scrolller/);
});
