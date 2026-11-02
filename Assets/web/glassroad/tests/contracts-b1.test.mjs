// tests/contracts-b1.test.mjs - the B1 contract data (CONTRACT-B1.md): track items, schema, grantBoost, feel groups.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { validate, ITEM_KINDS } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { createFeel } from '../core/feel.js';
import { createQueue } from '../core/events.js';
import { createState, S, BOOST } from '../sim/state.js';
import { grantBoost } from '../sim/grant.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

test('threshold: nine rows of 3 to 4 item boxes across the extended lap and bubble clusters', () => {
  const rows = new Map();
  for (const it of json.items.filter((i) => i.kind === 'box')) rows.set(it.node, (rows.get(it.node) || 0) + 1);
  assert.equal(rows.size, 9, 'rows ' + rows.size);
  for (const [node, n] of rows) assert.ok(n >= 3 && n <= 4, 'row at node ' + node + ' has ' + n);
  const bubbles = json.items.filter((i) => i.kind === 'bubble');
  assert.ok(bubbles.length >= 9, 'bubbles ' + bubbles.length);
  for (const it of json.items) assert.ok(ITEM_KINDS.includes(it.kind));
});

test('threshold: loaded items carry s; boxes clear of the jump and the open corner', () => {
  const tr = loadTrack(json);
  assert.equal(tr.items.length, json.items.length);
  const L = tr.lengthM, sd = (a, b) => { const d = Math.abs(((a - b) % L + L) % L); return Math.min(d, L - d); };
  const openS = [];
  for (let i = 0; i < tr.nodeCount; i++) if (tr.nodeFlags(i).includes('open')) openS.push(tr.nodeS[i]);
  const j = tr.jumps[0];
  for (const it of tr.items) {
    assert.ok(it.s >= 0 && it.s < L);
    assert.ok(Math.abs(it.s - (tr.nodeS[it.node] + it.ds)) < 1e-6 || it.s < it.ds);
    assert.ok(openS.every((o) => sd(it.s, o) >= 25), it.kind + ' near open at ' + it.s);
    if (it.kind === 'box') assert.ok(it.s < j.s - 30 || it.s > j.landS + 30, 'box near the jump at ' + it.s);
    const f = tr.frameAt(it.s);
    assert.ok(Math.abs(it.x) < f.w / 2 - 0.5, 'item off the road at ' + it.s);
  }
});

test('schema: item kinds, ds, and items over gaps or off the road', () => {
  const t = JSON.parse(JSON.stringify(json));
  t.items.push({ node: 1, x: 0, kind: 'jackpot' });
  let r = validate(t);
  assert.equal(r.ok, true); assert.ok(r.warnings.some((w) => /unknown kind jackpot/.test(w)));
  t.items[t.items.length - 1] = { node: 1, x: 0, kind: 'box', ds: -1 };
  assert.ok(validate(t).errors.some((e) => /ds must be/.test(e)));
  t.items[t.items.length - 1] = { node: 1, x: 30, kind: 'box' };
  assert.ok(validate(t).errors.some((e) => /off the road/.test(e)));
  t.items[t.items.length - 1] = { node: json.jumps[0].node, x: 0, kind: 'box' };
  assert.ok(validate(t).errors.some((e) => /jump gap/.test(e)));
});

test('grantBoost: pad-run boost named by its source; a smaller one never cuts a bigger', () => {
  const feel = createFeel(feelJson), q = createQueue(), seen = [];
  const st = createState();
  st[S.engineV] = 20;
  assert.equal(grantBoost(st, 0.24, 1.2, 'surge', feel, q), true);
  assert.equal(st[S.boostKind], BOOST.PAD); assert.equal(st[S.boostAmount], Math.fround(0.24)); assert.equal(st[S.boostFromV], 20);
  assert.equal(grantBoost(st, 0.05, 0.35, 'bubble', feel, q), false);
  assert.equal(st[S.boostAmount], Math.fround(0.24));
  q.flush({ emit: (n, p) => seen.push([n, p.kind, p.tier]) });
  assert.deepEqual(seen, [['boost-start', 'surge', 0]]);
});

test('feel: B1 groups present, weights are [front, middle, back]', () => {
  const feel = createFeel(feelJson);
  for (const g of ['items', 'ai', 'contact']) assert.ok(feelJson[g] && typeof feelJson[g] === 'object', g);
  const ids = ['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter', 'surge', 'glass', 'echo', 'golden'];
  for (const id of ids) {
    const w = feel.get('items.' + id + 'Weights');
    assert.equal(w.length, 3, id);
    if (id !== 'glass') assert.ok(feel.get('items.' + id + 'S') > 0, id);
  }
  for (const id of ['blackout', 'shatter', 'golden', 'echo']) assert.equal(feel.get('items.' + id + 'Weights')[0], 0, id + ' never in front');
  assert.equal(feel.get('ai.paceMul').length, 7);
});

test('art/fx: the DtRH effect icons are in the tree', () => {
  for (const n of ['flash', 'spiral', 'braindrain', 'bambifreeze', 'blackout', 'pinkfilter', 'melt', 'prism', 'gold_droplet', 'bubble', 'glitch', 'golden'])
    assert.ok(existsSync(new URL('../art/fx/' + n + '.png', import.meta.url)), n);
});
