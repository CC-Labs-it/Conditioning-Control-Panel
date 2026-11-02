import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createPictures, PICTURE_SOURCES } from '../ui/pictures.js';
import { pictureCardDue, pictureRow, PICTURE_ROUTES } from '../ui/picture-card.js';
import { PICTURE_CARD } from '../data/content/terms.js';
import { createEngine } from '../ui/engine.js';
import * as E from '../sim/economy.js';
import { createLife } from '../sim/save.js';

test('three sources, and an unknown one reads as none', () => {
  assert.deepEqual([...PICTURE_SOURCES], ['online', 'own', 'none']);
  assert.equal(createPictures({ source: 'scrolller' }).source, 'none');
});

test('none resolves null, online resolves a local placeholder with no network', async () => {
  const p = createPictures({ source: 'none' });
  assert.equal(await p.next({ niche: 'x' }), null);
  p.setSource('online');
  const got = await p.next({ niche: 'x' });
  assert.ok(got.url.startsWith('data:image/svg+xml,'));
  assert.equal(got.credit, null);
});

test('own files stay blob urls on this device, rotate, and skip anything that is not an image', async () => {
  const p = createPictures({ source: 'own' });
  assert.equal(await p.next(), null, 'no files yet is an empty frame, not an error');
  const kept = p.addOwn([new Blob(['a'], { type: 'image/png' }), new Blob(['b'], { type: 'text/plain' }), new Blob(['c'], { type: 'image/webp' })]);
  assert.equal(kept, 2);
  const a = await p.next(), b = await p.next(), c = await p.next();
  assert.ok(a.url.startsWith('blob:'));
  assert.notEqual(a.url, b.url);
  assert.equal(a.url, c.url);
  p.clearOwn();
  assert.equal(p.ownCount, 0);
  assert.equal(await p.next(), null);
});

test('a source change reaches unlocks as a pictures event, and through ctx.pictures', () => {
  const heard = [];
  let ctx;
  const run = E.createRun();
  E.grant(run, 'stray');
  const ui = { doc: {}, buttonFor: () => ({}), layer: () => ({}) };
  const engine = createEngine({ run, life: createLife(), ui, registry: { stray: { mount(c) { ctx = c; c.on('pictures', (e) => heard.push(e.source)); return {}; } } } });
  engine.mount('stray');
  assert.equal(ctx.pictures.source, 'none', 'empty frames until the player chooses');
  ctx.pictures.setSource('own');
  assert.equal(ctx.pictures.source, 'own');
  assert.deepEqual(heard, ['own']);
});

test('a new pick replaces the last one, and a pick with nothing usable changes nothing', async () => {
  const heard = [];
  const p = createPictures({ source: 'own', onChange: (s) => heard.push(s) });
  assert.equal(p.replaceOwn([new Blob(['a'], { type: 'image/png' }), new Blob(['g'], { type: 'image/gif' })]), 2);
  const first = await p.next();
  assert.equal(p.replaceOwn([new Blob(['t'], { type: 'text/plain' })]), 0);
  assert.equal(p.ownCount, 2);
  assert.equal(p.replaceOwn([new Blob(['b'], { type: 'image/webp' })]), 1);
  assert.equal(p.ownCount, 1);
  assert.notEqual((await p.next()).url, first.url);
  assert.deepEqual(heard, ['own', 'own']);
});

test('the card comes up the first time, and again when the files chosen before are gone', () => {
  assert.equal(pictureCardDue({ source: 'none', ownCount: 0 }, false), true);
  assert.equal(pictureCardDue({ source: 'none', ownCount: 0 }, true), false);
  assert.equal(pictureCardDue({ source: 'own', ownCount: 0 }, true), true);
  assert.equal(pictureCardDue({ source: 'own', ownCount: 3 }, true), false);
  assert.deepEqual([...PICTURE_ROUTES], ['slides', 'scroll']);
});

test('the pause row says what is true', () => {
  assert.match(pictureRow({ source: 'none', ownCount: 0 }), /empty/);
  assert.match(pictureRow({ source: 'own', ownCount: 2 }), /2/);
  assert.match(pictureRow({ source: 'own', ownCount: 0 }), /not loaded/);
});

test('nothing in the picture code fetches: sign-in is a stub until the website build', () => {
  const card = readFileSync(new URL('../ui/picture-card.js', import.meta.url), 'utf8');
  assert.ok(!/fetch\(|XMLHttpRequest|scrolller\.com/i.test(card));
  assert.match(PICTURE_CARD.signinStub, /website build/);
});
