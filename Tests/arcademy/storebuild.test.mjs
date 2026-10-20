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
