// The sheet's rendered item pictures (sheet/pictures.js + Scripts/render-shop-item-pictures.mjs): every item the shop
// can show has a picture or a declared fallback, and every picture in the map is a real small file on disk.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { PICTURED, pictureOf, itemArt } from '../sheet/pictures.js';
import { artOf } from '../sheet/model.js';
import { previewSample } from '../mock/preview.js';
import { FINISH_IDS } from '../../piecebypiece/skins/finish-ids.js';
import { BOARD_THEME_IDS } from '../../piecebypiece/board/theme-ids.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const INDEX = JSON.parse(readFileSync(new URL('../assets/preview/index.json', import.meta.url), 'utf8'));
const MANIFEST = JSON.parse(readFileSync(new URL('../assets/manifest.json', import.meta.url), 'utf8'));
const ROOT = new URL('../', import.meta.url);

// Rain V2 is postponed (hidden on every host that does not send init.rainV2): her rows keep the family glyph for now.
const DECLARED_GLYPH = (id) => id.startsWith('rain.');
const shopRows = () => previewSample(SAMPLE).tabs.flatMap((tab) => tab.shelves.flatMap((s) => s.rows.filter(Boolean).map((r) => ({ id: r.id, tab: tab.id }))))
  .filter((r) => r.tab !== 'effects');   // effects draw themselves live (sheet/live.js), never from a picture

test('every shop item in the mock state has a picture, its own flat art, or the declared fallback', () => {
  const rows = shopRows();
  assert.ok(rows.length > 50, 'the sample lists the whole catalogue');
  for (const { id, tab } of rows) {
    const art = itemArt(id, INDEX, tab, './');
    if (pictureOf(id)) { assert.deepEqual(art, { kind: 'pic', src: `./sheet/art/items/${id}.webp` }, id); continue; }
    assert.deepEqual(art, artOf(id, INDEX, tab, './'), id + ': an unlisted id keeps artOf exactly');
    if (DECLARED_GLYPH(id)) assert.equal(art.kind, 'glyph', id);
    else assert.notEqual(art.kind, 'glyph', id + ' shows a bare family glyph: render it (Scripts/render-shop-item-pictures.mjs) or give it flat art');
  }
});

test('every picture is a small WebP from a shelf model or the real chess renderer', () => {
  const models = new Set(MANIFEST.items.map((i) => i.id));
  assert.equal(new Set(PICTURED).size, PICTURED.length, 'no id twice');
  let total = 0;
  for (const id of PICTURED) {
    const f = new URL(pictureOf(id), ROOT);
    assert.ok(existsSync(f), id + ': ' + pictureOf(id) + ' is missing');
    const buf = readFileSync(f), size = statSync(f).size;
    assert.equal(buf.subarray(0, 4).toString('latin1'), 'RIFF', id);
    assert.equal(buf.subarray(8, 12).toString('latin1'), 'WEBP', id);
    assert.ok(size < 40 * 1024, `${id} is ${size} bytes`);
    // a chess finish has no shelf model: it paints the game's own sculpt (piecebypiece/board/finishes.js)
    if (id.startsWith('chess.finish.')) assert.ok(FINISH_IDS.includes(id), id + ' is a finish the game knows');
    else if (id.startsWith('chess.board.')) assert.ok(BOARD_THEME_IDS.includes(id), id + ' is a board the game knows');
    else if (id === 'chess.companion.emi') assert.ok(existsSync(new URL('../../piecebypiece/assets/cheerleader/emi-racing.glb', import.meta.url)), 'Emi uses the approved game model');
    else assert.ok(models.has(id), id + ' has a model in assets/manifest.json to render from');
    total += size;
  }
  assert.ok(total < 1.5 * 1024 * 1024, `pictures total ${total} bytes`);
});

test('every chess finish the game knows is in the shop with a picture, an L price, its grant and words', () => {
  const chess = SAMPLE.tabs.find((tb) => tb.id === 'chess').shelves.flatMap((s) => s.rows);
  for (const id of FINISH_IDS) {
    assert.ok(PICTURED.includes(id), id + ' pictured');
    const row = chess.find((r) => r && r.id === id);
    assert.ok(row, id + ' on the Chess shelf');
    assert.equal(row.tier, 'L', id);
    assert.deepEqual(row.grants, ['cos.' + id], id);
  }
  assert.equal(FINISH_IDS.length, 5);
});

test('each board and Emi has one priced shelf row, its own grant and a real product picture', () => {
  const chess = SAMPLE.tabs.find(tab => tab.id === 'chess').shelves.flatMap(s => s.rows).filter(Boolean);
  const items = [...BOARD_THEME_IDS, 'chess.companion.emi'];
  assert.equal(items.length, 5);
  for (const id of items) {
    const rows = chess.filter(row => row.id === id), companion = id === 'chess.companion.emi';
    assert.equal(rows.length, 1, id);
    assert.equal(rows[0].tier, companion ? 'XL' : 'L');
    assert.equal(rows[0].priceSp, companion ? 2540 : 730);
    assert.deepEqual(rows[0].grants, ['cos.' + id]);
    assert.equal(rows[0].sale, 'on');
    assert.ok(PICTURED.includes(id));
  }
});

test('an unknown id falls back to artOf, and base prefixes the picture path', () => {
  assert.equal(pictureOf('nope'), null);
  assert.deepEqual(itemArt('nope', null, 'backroom'), { kind: 'glyph', family: 'backroom' });
  assert.equal(itemArt('kart.body.petal', null, 'kart', '../').src, '../sheet/art/items/kart.body.petal.webp');
  assert.equal(itemArt('profile.frame.lace', INDEX, 'profile').kind, 'card');
});
