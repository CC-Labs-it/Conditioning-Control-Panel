import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// THE STORE TREE, rebuilt in memory the way the vendoring script's store targets
// build it on disk (core/storesafe.js): every X.store.js replaces X.js, the
// paths store-build.json drops are left out, and the manifest itself never
// ships. The word list below lives here, in a test, so it is never in any tree.
const ROOT = fileURLToPath(new URL('../../ConditioningControlPanel/Resources/web/arcademy/', import.meta.url));
const manifest = JSON.parse(readFileSync(join(ROOT, 'store-build.json'), 'utf8'));
const rel = (p) => relative(ROOT, p).split(sep).join('/');

function walk(dir, out = []) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p, out); else out.push(rel(p));
  }
  return out;
}
const all = walk(ROOT);
const isStoreFile = (f) => /\.store\.js$/.test(f);
const dropped = (f) => manifest.drop.some((d) => (d.endsWith('/') ? f.startsWith(d) : f === d));
const storeTree = new Map();
for (const f of all) {
  if (isStoreFile(f) || dropped(f) || f === 'store-build.json' || /^[^/]+\.md$/i.test(f)) continue;
  const swap = f.replace(/\.js$/, '.store.js');
  storeTree.set(f, all.includes(swap) ? swap : f);
}

const FORBIDDEN = [
  'bimbo', 'sissy', 'thong', 'panty', 'panties', 'latex', 'moans', 'grope', 'leash', 'domme',
  'dildo', 'boobs', 'busty', 'booty', 'twerk', 'horny', 'kinky', 'nudes', 'slave', 'semen',
  'slut', 'bambi', 'circe', 'good girl', 'good toy', 'empty head', 'open wide',
];
const forbiddenRe = new RegExp('\\b(' + FORBIDDEN.map((w) => w.replace(' ', '[\\s\'",\\[\\]]+')).join('|') + ')\\b|porn', 'i');

test('every store file replaces a real sibling with the same exports', async () => {
  const pairs = all.filter(isStoreFile);
  assert.ok(pairs.length >= 4, 'expected the store siblings to exist');
  for (const f of pairs) {
    const base = f.replace(/\.store\.js$/, '.js');
    assert.ok(all.includes(base), `${f} has no ${base} to replace`);
    const a = Object.keys(await import(pathToFileURL(join(ROOT, base)))).sort();
    const b = Object.keys(await import(pathToFileURL(join(ROOT, f)))).sort();
    assert.deepEqual(b, a, `${f} must export what ${base} exports`);
  }
});

test('the manifest names real paths and drops no swapped file', () => {
  for (const d of manifest.drop) assert.ok(existsSync(join(ROOT, d)), `drop path ${d} does not exist`);
  for (const [f, src] of storeTree) assert.ok(!dropped(src), `${f} is both swapped and dropped`);
  assert.ok(!all.filter(isStoreFile).some(dropped));
});

test('no explicit word survives in the store tree', () => {
  const hits = [];
  for (const [f, src] of storeTree) {
    if (!/\.(js|mjs|json|html|css)$/.test(f) || f.startsWith('vendor/')) continue;
    readFileSync(join(ROOT, src), 'utf8').split(/\r?\n/).forEach((line, i) => {
      if (forbiddenRe.test(line)) hits.push(`${f}:${i + 1}: ${line.trim().slice(0, 90)}`);
    });
  }
  assert.deepEqual(hits, []);
  assert.ok(!storeTree.has('assets/sublim/obey.mp3'));
});

test('the store word floor asks for no compliance', async () => {
  const m = await import(pathToFileURL(join(ROOT, 'core/house-words.store.js')));
  for (const w of ['OBEY', 'GIVE IN', 'SUBMIT']) assert.ok(!m.HOUSE_WORDS.includes(w), w);
  assert.ok(m.HOUSE_WORDS.length >= 12, 'a fallback day deals twelve');
});

test('the store pool is big enough and well formed', async () => {
  const m = await import(pathToFileURL(join(ROOT, 'games/daily-trigger/words-answers.store.js')));
  const toks = (s) => s.trim().split(/\s+/);
  const pool = new Set([...toks(m.THEME), ...toks(m.COMMON)].filter((w) => /^[a-z]{5}$/.test(w)));
  assert.ok(pool.size >= 400, `store pool is ${pool.size}, under the ~400 repeat-free floor`);
  for (const g of m.THEME_GROUPS) assert.ok(m.CAT_NAMES[g.cat], `band ${g.cat} has no name`);
  for (const p of m.PHRASES) {
    const n = p.join('').length;
    assert.ok(p.length === 2 && n >= 5 && n <= 10, `bad phrase ${p}`);
  }
});

test('the sort door offers only the host catalog in a store build', async () => {
  const { sourceOptions } = await import(pathToFileURL(join(ROOT, 'games/sort/setup.js')));
  const cat = { remoteConsent: true, mediaSource: 'any', remoteCatalog: [], localFolders: [], assetPresets: [] };
  globalThis.window = {};
  assert.equal(sourceOptions(cat).online, true, 'the full build keeps its online door');
  window.__ccpStoreSafe = true;
  assert.equal(sourceOptions(cat).online, false, 'no catalog, no online door');
  assert.equal(sourceOptions({ ...cat, remoteCatalog: [{ id: 'a', label: 'A', subs: ['a'] }] }).online, true);
  delete globalThis.window;
  const src = readFileSync(join(ROOT, 'games/sort/setup.js'), 'utf8');
  const stop = src.indexOf('if (isStoreSafe()) return;');
  assert.ok(stop > 0 && stop < src.indexOf('/* the library: ORANGE pills') && stop < src.indexOf('/* the search box */'));
});

// The owner's store wording rule: no hypnosis and no conditioning on anything a
// player reads. Comments are not read by anyone, so they are blanked first
// (block comments keep their line breaks, so a hit still names its line).
const uncomment = (f, text) => (/\.(js|mjs|css)$/.test(f)
  ? text.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' ')).replace(/(^|[\s;,{}()])\/\/.*$/gm, '$1')
  : text.replace(/<!--[\s\S]*?-->/g, (m) => m.replace(/[^\n]/g, ' ')));

test('no hypnosis or conditioning wording outside comments in the store tree', () => {
  const hits = [];
  for (const [f, src] of storeTree) {
    if (!/\.(js|mjs|json|html)$/.test(f) || f.startsWith('vendor/')) continue;
    uncomment(f, readFileSync(join(ROOT, src), 'utf8')).split(/\r?\n/).forEach((line, i) => {
      if (/hypno|conditioning/i.test(line)) hits.push(`${f}:${i + 1}: ${line.trim().slice(0, 90)}`);
    });
  }
  assert.deepEqual(hits, []);
  assert.equal(storeTree.get('annex/docs-rm202.js'), 'annex/docs-rm202.store.js');
});

test('the store Room 202 paper keeps its shape and bolds every tldr phrase', async () => {
  const full = (await import(pathToFileURL(join(ROOT, 'annex/docs-rm202.js')))).RM202;
  const store = (await import(pathToFileURL(join(ROOT, 'annex/docs-rm202.store.js')))).RM202;
  const { DOCS } = await import(pathToFileURL(join(ROOT, 'annex/docs.js')));
  assert.equal(DOCS.rm202, full, 'docs.js files the paper under its old id');
  assert.equal(store.name, full.name);
  assert.deepEqual(store.attachments.map((a) => a.kind), full.attachments.map((a) => a.kind));
  assert.deepEqual(store.attachments[0].chart, full.attachments[0].chart);
  const bold = [...store.body.matchAll(/\*\*(.+?)\*\*/g)].map((m) => m[1].toLowerCase().replace(/\.$/, ''));
  for (const t of store.tldr) assert.ok(bold.includes(t), `tldr "${t}" is not bolded in the body`);
  assert.doesNotMatch(JSON.stringify(store), /trigger|recording|—/i);
});

test("the store door's fallback rows read what the store lexicon writes", async () => {
  const full = (await import(pathToFileURL(join(ROOT, 'games/sort/setup-lex.js')))).SETUP_LEX;
  const store = (await import(pathToFileURL(join(ROOT, 'games/sort/setup-lex.store.js')))).SETUP_LEX;
  assert.deepEqual(Object.keys(store), Object.keys(full), 'same rows, same order');
  const set = manifest.lexicon.set;
  for (const key of Object.keys(full)) {
    if (key in set) assert.equal(store[key], set[key], `${key} differs from store-build.json`);
    else assert.equal(store[key], full[key], `${key} is rewritten here but not in store-build.json`);
    assert.doesNotMatch(store[key], /\bsubs?\b|subreddit|niche/i, `${key} still names subs or niches`);
    assert.ok(store[key].length <= 96 && !store[key].includes('\u2014'), key);
  }
});
