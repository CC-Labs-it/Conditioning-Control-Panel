import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ITEMS, BY_ID, LANES, FORKS } from '../data/items.js';
import { BRAND, brandText } from '../data/brand.js';
import { CONFIG } from '../data/config.js';
import { DESIGN, itemsSource } from '../dev/gen-items.mjs';

const tree = fileURLToPath(new URL('..', import.meta.url));

test('every unlock from the design is here, with a real parent', () => {
  assert.equal(ITEMS.length, 283);
  assert.equal(new Set(ITEMS.map((i) => i.id)).size, 283);
  for (const it of ITEMS) for (const q of it.req) assert.ok(BY_ID[q], `${it.id} needs ${q}`);
});

test('no row sits behind an ending: buying the ending would end the run first', () => {
  for (const it of ITEMS) for (const q of it.req) assert.notEqual(BY_ID[q].kind, 'end', it.id);
});

test('no row is cheaper than a parent, and each theme opens on its head then its podcast', () => {
  for (const it of ITEMS) for (const q of it.req) assert.ok(it.cost >= BY_ID[q].cost, `${it.id} ${it.cost} is cheaper than ${q} ${BY_ID[q].cost}`);
  for (const lane of Object.keys(FORKS.f5.opts)) {
    const rows = ITEMS.filter((i) => i.lane === lane && i.kind !== 'end').sort((a, b) => a.cost - b.cost);
    assert.ok(rows[1].id.endsWith('_pod'), `${lane} opens with ${rows[1].id}`);
    assert.deepEqual(rows[1].req, [rows[0].id]);
    assert.ok(rows[0].req.includes('f5'));
  }
});

test('a label never names a multiple: modifiers bend and prizes share a budget, so a shown number is a paid number', () => {
  const walk = (d) => readdirSync(d).flatMap((n) => { const p = join(d, n); return statSync(p).isDirectory() ? walk(p) : p.endsWith('.js') ? [p] : []; });
  const allowed = /Prices x2/;  // a real price modifier, nothing bends those
  for (const f of [...walk(join(tree, 'ui', 'unlocks')), ...walk(join(tree, 'data', 'content'))]) {
    for (const line of readFileSync(f, 'utf8').split(/\r?\n/)) {
      if (/^\s*(\/\*|\*|\/\/)/.test(line) || allowed.test(line)) continue;
      assert.ok(!/['"`][^'"`]*(?<![{\w])x\d+(\.\d+)?\b[^'"`]*['"`]/.test(line), `${f}: ${line.trim().slice(0, 90)}`);
    }
  }
  for (const it of ITEMS) if (it.lane !== 'meta') assert.ok(!/\b(double|doubles|triple|triples)\b/i.test(it.what), `${it.id}: ${it.what}`);
});

test('thirteen endings, five forks, every fork lane named', () => {
  assert.equal(ITEMS.filter((i) => i.kind === 'end').length, 13);
  assert.deepEqual(Object.keys(FORKS), ['f1', 'f2', 'f3', 'f4', 'f5']);
  for (const [f, def] of Object.entries(FORKS)) {
    assert.equal(BY_ID[f].kind, 'fork');
    for (const lane of Object.keys(def.opts)) assert.equal(LANES.find((l) => l.id === lane)?.fork, f);
  }
});

test('copy keeps the house voice: no dashes, no exclamation marks', () => {
  for (const it of ITEMS) assert.ok(!/[–—!]/.test(`${it.name} ${it.what} ${it.line}`), it.id);
  for (const f of Object.values(FORKS)) for (const o of [f, ...Object.values(f.opts)]) assert.ok(!/[–—!]/.test(`${o.name} ${o.what || ''} ${o.ask || ''}`));
});

test('the open owner calls sit in config at their agreed defaults', () => {
  assert.equal(CONFIG.AD_CLICK_PAYS_SECONDS, 10);
  assert.equal(CONFIG.BAMBI_VOICE_ON_WEB, false);
});

/* ---- the name is not decided ---- */

test('brand tokens expand from one module', () => {
  assert.equal(brandText('{game} pays in {cur}'), `${BRAND.game} pays in ${BRAND.currencyPlural}`);
});

test('the currency name appears only in data/brand.js', () => {
  const word = /\bFuzz\b/;
  for (const it of ITEMS) assert.ok(!word.test(`${it.name} ${it.what} ${it.line}`), it.id);
  const walk = (d) => readdirSync(d).flatMap((n) => { const p = join(d, n); return statSync(p).isDirectory() ? walk(p) : [p]; });
  const files = [join(tree, 'index.html'), join(tree, 'main.js'), ...walk(join(tree, 'ui')), ...walk(join(tree, 'data'))]
    .filter((p) => /\.(js|html)$/.test(p) && !p.replace(/\\/g, '/').endsWith('data/brand.js'));
  for (const p of files) {
    // Comments are not player-facing.
    const src = readFileSync(p, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
    assert.ok(!word.test(src), `${p} names the currency`);
  }
});

test('data/items.js is what the generator makes, when the design source is on this machine', { skip: !existsSync(DESIGN) }, () => {
  // A Windows checkout may hold CRLF; git stores LF. Compare what git would store.
  const lf = (s) => s.split('\r\n').join('\n');
  assert.equal(lf(readFileSync(join(tree, 'data', 'items.js'), 'utf8')), lf(itemsSource()));
});
