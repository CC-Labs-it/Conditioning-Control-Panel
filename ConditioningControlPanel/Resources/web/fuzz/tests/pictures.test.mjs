import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPictures, PICTURE_SOURCES } from '../ui/pictures.js';
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
  assert.equal(ctx.pictures.source, 'online');
  ctx.pictures.setSource('none');
  assert.equal(ctx.pictures.source, 'none');
  assert.deepEqual(heard, ['none']);
});
