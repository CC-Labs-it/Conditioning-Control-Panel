// Per-track Scrolller niches (host/scrolllerNiches.js): track defaults, a saved pick per track, chip limits.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import { TRACK_NICHES, NICHES_KEY, MAX_NICHES, cleanPick, loadPicks, nichesFor, isDefault, savePick, togglePick }
  from '../host/scrolllerNiches.js';
import { feedCatalog, createScrolllerFeed } from '../host/scrolllerFeed.js';

function memStorage() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k), m };
}
function withStorage(fn) {
  const prev = globalThis.localStorage, s = memStorage();
  globalThis.localStorage = s;
  try { return fn(s); } finally { if (prev === undefined) delete globalThis.localStorage; else globalThis.localStorage = prev; }
}

test('every course has a default of one to four niches', () => {
  const ids = readdirSync(new URL('../tracks/', import.meta.url)).filter((f) => /^[a-z]+\.json$/.test(f)).map((f) => f.slice(0, -5));
  assert.equal(ids.length, 5);
  for (const id of ids) {
    const d = TRACK_NICHES[id];
    assert.ok(Array.isArray(d) && d.length >= 1 && d.length <= MAX_NICHES, id);
  }
  assert.deepEqual(nichesFor('threshold'), TRACK_NICHES.threshold);
});

test('a saved pick wins for its track only; reset goes back to the default', () => withStorage((s) => {
  assert.equal(isDefault('keep'), true);
  assert.deepEqual(savePick('keep', ['goon', 'hypno']), ['goon', 'hypno']);
  assert.deepEqual(nichesFor('keep'), ['goon', 'hypno']);
  assert.equal(isDefault('keep'), false);
  assert.deepEqual(nichesFor('boudoir'), TRACK_NICHES.boudoir);
  assert.ok(s.getItem(NICHES_KEY));
  assert.deepEqual(savePick('keep', null), TRACK_NICHES.keep);
  assert.equal(isDefault('keep'), true);
}));

test('picks are cleaned: plain ids, no repeats, at most four; junk storage reads as no picks', () => withStorage((s) => {
  assert.deepEqual(cleanPick(['hypno', 'hypno', 'Bad Id!', 7, 'goon', 'sissy', 'bimbo', 'futa']), ['hypno', 'goon', 'sissy', 'bimbo']);
  s.setItem(NICHES_KEY, '{not json');
  assert.deepEqual(loadPicks(), {});
  assert.deepEqual(nichesFor('mainframe'), TRACK_NICHES.mainframe);
}));

test('a chip toggle never empties the pick and never passes four', () => {
  assert.deepEqual(togglePick(['hypno'], 'goon'), ['hypno', 'goon']);
  assert.deepEqual(togglePick(['hypno', 'goon'], 'hypno'), ['goon']);
  assert.equal(togglePick(['hypno'], 'hypno'), null);
  assert.equal(togglePick(['a', 'b', 'c', 'd'], 'e'), null);
});

test('the feed reads the host catalog and asks for a track\'s niches', () => {
  assert.equal(feedCatalog({}), null);
  assert.deepEqual(feedCatalog({ catalog: [{ id: 'hypno', label: 'Hypno' }, { id: 'hypno', label: 'x' }, { id: 'BAD ID', label: 'y' }] }),
    [{ id: 'hypno', label: 'Hypno' }]);
  const sent = [], handlers = new Map();
  const bridge = { on: (t, fn) => { handlers.set(t, fn); return () => handlers.delete(t); }, send: (m) => sent.push(m) };
  const feed = createScrolllerFeed({ bridge, makeImage: () => ({ removeAttribute() {} }) });
  let heard = 0;
  feed.subscribe(() => heard++);
  handlers.get('scrolller-feed')({ v: 1, rows: [], catalog: [{ id: 'goon', label: 'Goon' }] });
  assert.deepEqual(feed.catalog, [{ id: 'goon', label: 'Goon' }]);
  assert.equal(heard, 1);
  handlers.get('scrolller-feed')({ v: 1, rows: [], catalog: [{ id: 'goon', label: 'Goon' }] });
  assert.equal(heard, 1, 'an unchanged catalog is quiet');
  feed.setNiches('keep', ['beta']);
  assert.deepEqual(sent.at(-1), { type: 'scrolller-niches', track: 'keep', niches: ['beta'] });
  feed.dispose();
});
