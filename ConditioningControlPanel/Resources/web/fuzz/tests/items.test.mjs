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
  assert.equal(ITEMS.length, 299);  // + dualwield (owner 2026-11-08), + the four EMI Takeover rows (owner 2026-11-09)
  assert.equal(new Set(ITEMS.map((i) => i.id)).size, 299);
  assert.equal(BY_ID.tr_breath, undefined);
  assert.deepEqual(BY_ID.tr_feed.req, ['tr_still']);
  for (const it of ITEMS) for (const q of it.req) assert.ok(BY_ID[q], `${it.id} needs ${q}`);
});

test('every row a player can buy says what it does, in the final copy', () => {
  for (const it of ITEMS) if (it.kind !== 'fork') assert.ok(it.what, it.id);
  assert.equal(BY_ID.stray.name, 'Bouncing Button');
});

test('no row sits behind an ending: buying the ending would end the run first', () => {
  for (const it of ITEMS) for (const q of it.req) assert.notEqual(BY_ID[q].kind, 'end', it.id);
});

test('no row is cheaper than a parent, and each theme opens on its head, with rows on offer before its podcast', () => {
  for (const it of ITEMS) for (const q of it.req) assert.ok(it.cost >= BY_ID[q].cost, `${it.id} ${it.cost} is cheaper than ${q} ${BY_ID[q].cost}`);
  for (const lane of Object.keys(FORKS.f5.opts)) {
    const rows = ITEMS.filter((i) => i.lane === lane && i.kind !== 'end').sort((a, b) => a.cost - b.cost);
    assert.ok(rows[0].id.endsWith('_head'), `${lane} opens with ${rows[0].id}`);
    assert.ok(rows[0].req.includes('f5'));
    const at = rows.findIndex((r) => r.id.endsWith('_pod'));
    assert.deepEqual(rows[at].req, [rows[0].id], `${lane}: the podcast needs only the head`);
    assert.ok(at >= 3, `${lane}: the pick should be felt before the podcast, only ${at - 1} rows sit under it`);
  }
});

test('a label never names a multiple: modifiers bend and prizes share a budget, so a shown number is a paid number', () => {
  const walk = (d) => readdirSync(d).flatMap((n) => { const p = join(d, n); return statSync(p).isDirectory() ? walk(p) : p.endsWith('.js') ? [p] : []; });
  // Prices x2 is a real price modifier. Frenzy x7 and Deep x5 are timed Golden Button buffs that skip the knees, so they
  // pay exactly what they say (owner 2026-11-07, sim/economy.js rates). The Drone theme words the same two prizes as
  // Throughput x7 and Idle efficiency x5 (approved copy 2026-11-10, data/content/theme-text.js).
  const allowed = /Prices x2|Frenzy x7|Deep x5|Throughput x7|Idle efficiency x5/;
  for (const f of [...walk(join(tree, 'ui', 'unlocks')), ...walk(join(tree, 'data', 'content'))]) {
    for (const line of readFileSync(f, 'utf8').split(/\r?\n/)) {
      if (/^\s*(\/\*|\*|\/\/)/.test(line) || allowed.test(line)) continue;
      assert.ok(!/['"`][^'"`]*(?<![{\w])x\d+(\.\d+)?\b[^'"`]*['"`]/.test(line), `${f}: ${line.trim().slice(0, 90)}`);
    }
  }
  for (const it of ITEMS) if (it.lane !== 'meta') assert.ok(!/\b(double|doubles|triple|triples)\b(?! tap)/i.test(it.what), `${it.id}: ${it.what}`);
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

test('every row has a name and words for its card', () => {
  for (const it of ITEMS) {
    assert.ok(it.name, `${it.id} has no name`);
    assert.ok(it.what, `${it.id} has no words`);
  }
});
