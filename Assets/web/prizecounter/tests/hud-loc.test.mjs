// Loc: every shop_* key the page uses is in en.json, the ten language files carry the same shop_* set, the English
// fallbacks in state/strings.js match en.json, and the copy keeps the house voice (no em or en dashes, no exclamation).
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { LEX, tabKey, itemKey, itemDesc, makeT } from '../state/strings.js';
import { SHEET_LEX } from '../sheet/strings.js';
import { TAB_ORDER } from '../state/shop.js';
import { previewSample } from '../mock/preview.js';

// The 3D page's words (state/strings.js) and the 2D sheet's (sheet/strings.js): together they are en.json's shop_* set.
const ALL_LEX = { ...LEX, ...SHEET_LEX };

const here = path.dirname(fileURLToPath(import.meta.url)), page = path.join(here, '..');
const LANG_DIR = path.join(page, '../../../CCP.Core/Localization/Languages');   // Assets/web/prizecounter -> the Core catalogue
const LANGS = ['de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'pt-BR', 'ru', 'zh-CN'];
const read = (lang) => fs.readFileSync(path.join(LANG_DIR, `${lang}.json`), 'utf8');
const shopKeys = (text) => Object.keys(JSON.parse(text)).filter((k) => k.startsWith('shop_')).sort();
const sample = JSON.parse(fs.readFileSync(path.join(page, 'mock/state.sample.json'), 'utf8'));
// Retired ids (catalog revision 2) lose their name keys: an owner's stamped row falls back to the id's words.
const retired = JSON.parse(fs.readFileSync(path.join(page, 'assets/manifest.json'), 'utf8')).retired;

function usedKeys() {
  const files = ['main.js', ...['hud', 'state', 'scene', 'sheet'].filter((d) => fs.existsSync(path.join(page, d))).flatMap((d) => fs.readdirSync(path.join(page, d)).filter((f) => f.endsWith('.js')).map((f) => `${d}/${f}`))];
  const keys = new Set();
  // a key built from a stem ('shop_sheet_noun_' + slot) is checked through its stems below, not as itself
  for (const f of files) for (const m of fs.readFileSync(path.join(page, f), 'utf8').matchAll(/'(shop_[a-z0-9_]+)'/g)) if (!m[1].endsWith('_')) keys.add(m[1]);
  TAB_ORDER.forEach((id) => keys.add(tabKey(id)));
  sample.tabs.forEach((tab) => tab.shelves.forEach((s) => s.rows.forEach((r) => r && !(r.id in retired) && keys.add(itemKey(r.id)))));
  // the sheet's stems: every effect slot's section, shows line and noun, every place, and every effect row's name
  for (const slot of ['bubble', 'card', 'behind', 'fog', 'word']) for (const stem of ['sec', 'shows', 'noun']) keys.add(`shop_sheet_${stem}_${slot}`);
  for (const k of ['kart', 'chess', 'profile', 'desk', 'prime']) for (const stem of ['sec', 'shows']) keys.add(`shop_sheet_${stem}_${k}`);
  for (const p of ['desktop', 'web', 'phone']) for (const stem of ['place', 'where']) keys.add(`shop_sheet_${stem}_${p}`);
  previewSample(sample).tabs[0].shelves.forEach((s) => s.rows.forEach((r) => { keys.add(itemKey(r.id)); if (r.how) keys.add(itemKey(r.id).replace(/_name$/, '_how')); }));
  return [...keys];
}

test('every shop_* key used in code, every tab and every sample row name is in en.json', () => {
  const en = JSON.parse(read('en'));
  const used = usedKeys();
  assert.ok(used.length > 60, `found ${used.length}`);
  assert.deepEqual(used.filter((k) => !(k in en)), []);
});

test('every item with art has a name in en.json, and no retired id keeps one', () => {
  const en = JSON.parse(read('en')), items = JSON.parse(fs.readFileSync(path.join(page, 'assets/manifest.json'), 'utf8')).items;
  assert.deepEqual(items.map((i) => itemKey(i.id)).filter((k) => !(k in en)), []);
  assert.deepEqual(Object.keys(retired).map(itemKey).filter((k) => k in en), []);
});

test('every item with art has a description', () => {
  const t = makeT(JSON.parse(read('en'))), items = JSON.parse(fs.readFileSync(path.join(page, 'assets/manifest.json'), 'utf8')).items;
  assert.deepEqual(items.filter((i) => !itemDesc(t, i.id)).map((i) => i.id), []);
});

test('all ten language files carry the same shop_* key set, once each, no BOM', () => {
  const want = shopKeys(read('en'));
  for (const lang of LANGS) {
    const text = read(lang);
    assert.notEqual(text.charCodeAt(0), 0xfeff, `${lang} BOM`);
    assert.deepEqual(shopKeys(text), want, lang);
    for (const k of want) assert.equal(text.split(`"${k}":`).length, 2, `${lang} ${k} twice`);
  }
});

test('state/strings.js + sheet/strings.js fallbacks are en.json, and every value keeps its placeholders and the voice', () => {
  const en = JSON.parse(read('en'));
  assert.deepEqual(Object.fromEntries(shopKeys(read('en')).map((k) => [k, en[k]])), Object.fromEntries(Object.keys(ALL_LEX).sort().map((k) => [k, ALL_LEX[k]])));
  // a language may reorder {0} and {1} (French puts the noun first), never drop or add one
  const holes = (s) => (s.match(/\{(\d|companion)\}/g) || []).sort().join();
  for (const lang of LANGS) {
    const j = JSON.parse(read(lang));
    for (const k of shopKeys(read('en'))) {
      // mojibake is a lead byte read as Latin-1 (Ã or Â) before a continuation byte; Portuguese Âmbar is a real word
      assert.doesNotMatch(j[k], /[–—!！]|[ÃÂ][\u0080-¿]|â€/, `${lang} ${k}`);
      assert.equal(holes(j[k]), holes(en[k]), `${lang} ${k} placeholders`);
    }
  }
});

test('every sheet key is in all ten files and the English there is the sheet\'s fallback', () => {
  assert.ok(Object.keys(SHEET_LEX).length > 150, `found ${Object.keys(SHEET_LEX).length}`);
  for (const lang of LANGS) {
    const j = JSON.parse(read(lang));
    assert.deepEqual(Object.keys(SHEET_LEX).filter((k) => typeof j[k] !== 'string' || !j[k].trim()), [], lang);
  }
  const en = JSON.parse(read('en'));
  assert.deepEqual(Object.keys(SHEET_LEX).filter((k) => en[k] !== SHEET_LEX[k]), []);
});
