// Loc: every shop_* key the page uses is in en.json, the ten language files carry the same shop_* set, the English
// fallbacks in state/strings.js match en.json, and the copy keeps the house voice (no em or en dashes, no exclamation).
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { LEX, tabKey, itemKey, itemDesc, makeT } from '../state/strings.js';
import { TAB_ORDER } from '../state/shop.js';

const here = path.dirname(fileURLToPath(import.meta.url)), page = path.join(here, '..');
const LANG_DIR = path.join(page, '../../../CCP.Core/Localization/Languages');   // Assets/web/prizecounter -> the Core catalogue
const LANGS = ['de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'pt-BR', 'ru', 'zh-CN'];
const read = (lang) => fs.readFileSync(path.join(LANG_DIR, `${lang}.json`), 'utf8');
const shopKeys = (text) => Object.keys(JSON.parse(text)).filter((k) => k.startsWith('shop_')).sort();
const sample = JSON.parse(fs.readFileSync(path.join(page, 'mock/state.sample.json'), 'utf8'));
// Retired ids (catalog revision 2) lose their name keys: an owner's stamped row falls back to the id's words.
const retired = JSON.parse(fs.readFileSync(path.join(page, 'assets/manifest.json'), 'utf8')).retired;

function usedKeys() {
  const files = ['main.js', ...['hud', 'state', 'scene'].filter((d) => fs.existsSync(path.join(page, d))).flatMap((d) => fs.readdirSync(path.join(page, d)).filter((f) => f.endsWith('.js')).map((f) => `${d}/${f}`))];
  const keys = new Set();
  for (const f of files) for (const m of fs.readFileSync(path.join(page, f), 'utf8').matchAll(/'(shop_[a-z0-9_]+)'/g)) keys.add(m[1]);
  TAB_ORDER.forEach((id) => keys.add(tabKey(id)));
  sample.tabs.forEach((tab) => tab.shelves.forEach((s) => s.rows.forEach((r) => r && !(r.id in retired) && keys.add(itemKey(r.id)))));
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

test('strings.js fallbacks are en.json, and every value keeps its placeholders and the voice', () => {
  const en = JSON.parse(read('en'));
  assert.deepEqual(Object.fromEntries(shopKeys(read('en')).map((k) => [k, en[k]])), Object.fromEntries(Object.keys(LEX).sort().map((k) => [k, LEX[k]])));
  for (const lang of LANGS) {
    const j = JSON.parse(read(lang));
    for (const k of shopKeys(read('en'))) {
      assert.doesNotMatch(j[k], /[–—!！]|Ã|â€/, `${lang} ${k}`);
      assert.equal((j[k].match(/\{\d\}/g) || []).join(), (en[k].match(/\{\d\}/g) || []).join(), `${lang} ${k} placeholders`);
    }
  }
});
