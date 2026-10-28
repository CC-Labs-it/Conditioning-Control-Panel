// "My files" (host/localLibrary.js): own pictures as blob urls, never uploaded, revoked on change; Scrolller off meanwhile.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { pickFiles, createLocalLibrary, feedSwitch, sourceMode, nextSource, sourceLabel, loadFilesChoice, saveFilesChoice,
  MEDIA_SOURCE_KEY, MAX_LIBRARY_FILES, SOURCES } from '../host/localLibrary.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const file = (name, type, size = 10) => ({ name, type, size });
const fakeEl = (tagName) => ({ tagName, attrs: {}, loads: 0, complete: true, naturalWidth: 64, readyState: 1, error: null,
  removeAttribute(k) { if (k === 'src') this.src = ''; }, load() { this.loads++; } });

test('a pick keeps pictures and clips only, deduped, capped, sampled from a big folder', () => {
  const list = [file('a.jpg', 'image/jpeg'), file('a.jpg', 'image/jpeg'), file('b.mp4', 'video/mp4'), file('c.txt', 'text/plain'),
    file('empty.png', 'image/png', 0), file('d.gif', 'image/gif')];
  assert.deepEqual(pickFiles(list).map((f) => f.name), ['a.jpg', 'b.mp4', 'd.gif']);
  const many = Array.from({ length: 50 }, (_, i) => file('p' + i + '.jpg', 'image/jpeg'));
  const picked = pickFiles(many, { rng: () => 0.5 });
  assert.equal(picked.length, MAX_LIBRARY_FILES);
  assert.notDeepEqual(picked.map((f) => f.name), many.slice(0, MAX_LIBRARY_FILES).map((f) => f.name), 'a mix, not the first files');
  assert.equal(pickFiles(null).length, 0);
});

test('the library shows object urls and revokes every one on replace, clear and dispose', () => {
  const made = [], revoked = [];
  const lib = createLocalLibrary({ makeImage: () => fakeEl('IMG'), makeVideo: () => fakeEl('VIDEO'),
    createURL: (f) => { const u = 'blob:x/' + f.name; made.push(u); return u; }, revokeURL: (u) => revoked.push(u) });
  assert.equal(lib.credit, '', 'own files carry no Scrolller credit');
  assert.equal(lib.setFiles([file('a.jpg', 'image/jpeg'), file('b.mp4', 'video/mp4')]), 2);
  assert.equal(lib.count, 2); assert.equal(lib.ready, 2); assert.equal(lib.stillCount, 1);
  assert.equal(lib.stillFor(5).tagName, 'IMG', 'flash bursts get stills only');
  assert.match(lib.sourceFor(1).src, /^blob:/);
  lib.setFiles([file('c.png', 'image/png')]);
  assert.deepEqual(revoked, ['blob:x/a.jpg', 'blob:x/b.mp4']);
  lib.clear();
  assert.deepEqual(revoked, made); assert.equal(lib.count, 0); assert.equal(lib.sourceFor(0), null);
  lib.setFiles([file('d.png', 'image/png')]); lib.dispose();
  assert.deepEqual(revoked, made);
});

test('the feed switch follows the source and keeps the Scrolller credit only for Scrolller', () => {
  const scrolller = { ready: 3, count: 3, stillCount: 2, sourceFor: () => 'S', stillFor: () => 'SS' };
  const lib = { credit: '', ready: 1, count: 1, stillCount: 1, sourceFor: () => 'L', stillFor: () => 'LS' };
  let files = false;
  const feed = feedSwitch(() => (files ? lib : scrolller));
  assert.equal(feed.credit, 'via Scrolller'); assert.equal(feed.sourceFor(0), 'S'); assert.equal(feed.stillCount, 2);
  files = true;
  assert.equal(feed.credit, ''); assert.equal(feed.stillFor(0), 'LS'); assert.equal(feed.ready, 1);
  assert.equal(feedSwitch(() => null).ready, 0);
});

test('source choice: the local pick wins, else the stored Scrolller switch; only "files" is stored', () => {
  assert.equal(sourceMode({ files: true, consent: true }), 'files');
  assert.equal(sourceMode({ consent: true }), 'scrolller');
  assert.equal(sourceMode({}), 'off');
  assert.deepEqual(SOURCES.map(nextSource), ['files', 'off', 'scrolller']);
  assert.deepEqual(SOURCES.map(sourceLabel), ['Scrolller', 'My files', 'Off']);
  const box = new Map(), storage = { getItem: (k) => box.get(k) ?? null, setItem: (k, v) => box.set(k, v), removeItem: (k) => box.delete(k) };
  assert.equal(loadFilesChoice(storage), false);
  saveFilesChoice(true, storage); assert.equal(box.get(MEDIA_SOURCE_KEY), 'files'); assert.equal(loadFilesChoice(storage), true);
  saveFilesChoice(false, storage); assert.equal(box.has(MEDIA_SOURCE_KEY), false);
  const broken = { getItem() { throw Error('blocked'); }, setItem() { throw Error('blocked'); } };
  assert.equal(loadFilesChoice(broken), false); saveFilesChoice(true, broken);
});

test('own files never leave the page: no fetch, no bridge, no upload; Scrolller switched off while they show', () => {
  const lib = src('host/localLibrary.js');
  assert.doesNotMatch(lib, /\bfetch\(|XMLHttpRequest|sendBeacon|bridge\??\.|FormData|WebSocket/);
  const boot = src('boot.js');
  assert.doesNotMatch(boot, /bridge\.send\([^)]*(library|files)/i);
  assert.match(boot, /else if \(consentOn\(\)\) scrolller\.setConsent\(false\)/);
  assert.match(boot, /if \(!files\) library\.clear\(\)/);
  for (const l of [...lib.split('\n'), ...src('hud/cards.js').split('\n')].filter((x) => /My files|Your files|this device|'Choose'|'Change'/.test(x)))
    assert.doesNotMatch(l, /[—–!]/, l);
});
