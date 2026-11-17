// The fog shelf after the owner's notes (2026-11-17): ONE product, Two-tone. Colour one is the user's own pink filter
// colour; the second comes free from a fixed palette, the same as the desktop's (CCP.Core FogLooks.Palette), and one too
// close to the user's own hands over to the next with one short line saying so.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createPreviewServer } from '../mock/preview.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';
import { FOGS, secondFog, fogClose } from '../sheet/looks.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const CORE = readFileSync(new URL('../../../../../CCP.Core/Super/FogLooks.cs', import.meta.url), 'utf8');

test('the palette is the desktop palette, in the same order, and no two of its colours swirl into one', () => {
  const core = [...CORE.matchAll(/\((\w+), \(\(byte\)0x(\w\w), \(byte\)0x(\w\w), \(byte\)0x(\w\w)\)\)/g)]
    .map((m) => [m[1].toLowerCase(), `#${m[2]}${m[3]}${m[4]}`.toLowerCase()]);
  assert.deepEqual(Object.entries(FOGS), core);
  assert.match(CORE, /CloseDistance = 48;/);
  for (const a of Object.keys(FOGS)) for (const b of Object.keys(FOGS)) if (a !== b) assert.equal(fogClose(FOGS[a], FOGS[b]), false, `${a} ${b}`);
});

test('the second colour steps on when it sits too close to the user\'s own, as the desktop does', () => {
  assert.deepEqual(secondFog('mint', '#ff69b4'), { kind: 'mint', skipped: false });
  assert.deepEqual(secondFog('pink', '#ff69b4'), { kind: 'lilac', skipped: true });   // hot pink is too close to pink
  assert.deepEqual(secondFog('lilac', '#a08cfa'), { kind: 'mint', skipped: true });
  assert.deepEqual(secondFog('peach', '#ff9678'), { kind: 'pink', skipped: true });   // wraps round
  assert.deepEqual(secondFog('bogus', '#40c8be'), { kind: 'lilac', skipped: false });
});

test('the fog shelf sells Two-tone only; its fitting room offers every palette swatch and says when one is too close', async () => {
  const server = createPreviewServer(SAMPLE);
  const fog = server.state().tabs[0].shelves.flatMap((s) => s.rows).filter((r) => r.slot === 'fog').map((r) => r.id);
  assert.deepEqual(fog, ['fx.fog.twotone']);
  assert.equal((await server.handle('shop-equip', { slot: 'fog2', itemId: 'fx.fog.sky' })).equip.fog2, 'fx.fog.sky');
  assert.equal((await server.handle('shop-equip', { slot: 'fog2', itemId: 'fx.fog.twotone' })).reason, 'bad_input');
  assert.equal((await server.handle('shop-equip', { slot: 'fog2', itemId: 'fx.fog.pink' })).equip.fog2, 'fx.fog.pink');

  const host = asHost({ init: { motion: 'full', tab: 'effects' }, request: (op, b, i) => server.handle(op, b, i) });
  const doc = fakeDocument(), root = doc.createElement('main');
  const page = await mountSheet(host, { root, doc, win: { addEventListener() {}, removeEventListener() {} }, now: () => 1e6, frame: (f) => f(), later() {}, loadIndex: () => null });
  page.pick('fx.fog.twotone');
  const swatches = page.view.room.all().filter((n) => n.dataset && /^fog2:/.test(n.dataset.key || '')).map((n) => n.dataset.key);
  assert.deepEqual(swatches, Object.keys(FOGS).map((k) => 'fog2:' + k));
  // the host does not send the user's colour yet, so pink stands in: a picked pink is too close, lilac swirls
  assert.match(page.view.room.text(), /Pink is too close to your colour, so Lilac swirls instead\./);
  assert.match(page.view.room.text(), /Your pink filter colour and a second one move through each other/);
});
