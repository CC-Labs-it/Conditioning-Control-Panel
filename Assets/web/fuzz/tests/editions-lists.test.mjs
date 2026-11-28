/* The rest of the lists in the small edition (lane S3b, 2026-11-22): the sticker book, the endings gallery, the cursor
 * shop and the preview bar never list a row the small edition cuts; the full edition lists exactly what it did. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { SMALL_CUT_IDS } from '../data/editions.js';
import { STICKERS } from '../data/content/stickers.js';
import { ENDINGS } from '../data/content/endings.js';
import { OFF_SHELF } from '../sim/economy.js';
import { HERD_ENDINGS } from '../data/fate.js';
import { createLife } from '../sim/save.js';
import { shown, stickerAsleep } from '../sim/stickers.js';
import { cursorModifiers } from '../ui/cursor-shop.js';
import { BAR_ENDINGS, barEndings } from '../ui/preview-bar.js';

const small = Ed.readEdition({ caps: { edition: 'small' } }, { hostname: 'tc.cclabs.app', hosted: true });
const cut = new Set(SMALL_CUT_IDS);
const ids = (list) => list.map((s) => s.id);

test('the small sticker book never shows a sticker it cannot give as missing, and keeps one the save holds', () => {
  const life = createLife();
  const asleep = ['e_ll_end', 'n_toybox', 'n_disp', 'n_deep', 'n_brk', 'p_renew', 'p_depth5', 'p_core'];
  for (const id of asleep) assert.ok(STICKERS.some((s) => s.id === id), `${id} exists`);
  assert.deepEqual(STICKERS.filter((s) => stickerAsleep(s, small)).map((s) => s.id).sort(), [...asleep].sort());
  const book = ids(shown(life, small));
  for (const id of asleep) assert.equal(book.includes(id), false, `${id} shown as missing in small`);
  life.stickers.e_ll_end = 2;
  life.stickers.n_toybox = 1;
  const kept = ids(shown(life, small));
  assert.ok(kept.includes('e_ll_end') && kept.includes('n_toybox'), 'an earned one still shows');
  /* Full: the same book as before. */
  const fresh = createLife();
  assert.deepEqual(ids(shown(fresh)), ids(STICKERS.filter((s) => !s.legacy)));
  assert.deepEqual(ids(shown(fresh, null)), ids(shown(fresh)));
  assert.equal(STICKERS.some((s) => stickerAsleep(s, null)), false);
});

test('the endings gallery never lists the Expo as a goal in small, and keeps it when seen', () => {
  const all = Object.keys(ENDINGS);
  const old = (got) => all.filter((eid) => (!OFF_SHELF.has(eid) && !HERD_ENDINGS.includes(eid)) || got.has(eid));
  for (const got of [new Set(), new Set(['ll_end', 'drop'])]) assert.deepEqual(E.galleryIds(all, got, E.createRun()), old(got), 'full unchanged');
  const run = E.createRun({ ed: small });
  const none = E.galleryIds(all, new Set(), run);
  for (const eid of none) assert.equal(cut.has(eid), false, `${eid} listed in small`);
  assert.ok(none.length >= 5, 'the other endings are listed');
  assert.ok(E.galleryIds(all, new Set(['ll_end']), run).includes('ll_end'), 'a seen Expo stays in the gallery');
});

test('the cursor shop and the preview bar list no cut row in small', () => {
  assert.deepEqual(cursorModifiers(E.createRun()), ['ring', 'hijack']);
  assert.deepEqual(cursorModifiers(E.createRun({ ed: small })), ['ring']);
  assert.deepEqual(barEndings(E.createRun()), BAR_ENDINGS);
  const bar = barEndings(E.createRun({ ed: small }));
  assert.equal(bar.includes('ll_end'), false);
  assert.deepEqual(bar, BAR_ENDINGS.filter((id) => !cut.has(id)));
});
