import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { ITEMS, BY_ID, PINNED, ENDING_ID } from '../data/items.js';
import { CIRCE, MANTRA, VOICE, ENDING, clipUrl } from '../data/words.js';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

test('every id is unique and every prerequisite names an item that comes earlier', () => {
  const seen = new Set();
  ITEMS.forEach((it, i) => {
    assert.ok(!seen.has(it.id), `duplicate id ${it.id}`);
    seen.add(it.id);
    for (const r of it.req || []) {
      assert.ok(BY_ID[r], `${it.id} requires unknown ${r}`);
      assert.ok(ITEMS.indexOf(BY_ID[r]) < i, `${it.id} requires ${r}, which comes later in the ladder`);
    }
  });
  for (const p of PINNED) assert.ok(BY_ID[p]);
  assert.equal(ITEMS[ITEMS.length - 1].id, ENDING_ID, 'the ending is the last tile');
});

test('the ladder climbs: base prices never fall, 3 to 2,000,000', () => {
  assert.equal(ITEMS[0].cost, 3);
  assert.equal(BY_ID[ENDING_ID].cost, 2_000_000);
  for (let i = 1; i < ITEMS.length; i++) assert.ok(ITEMS[i].cost >= ITEMS[i - 1].cost, `${ITEMS[i].id} is cheaper than ${ITEMS[i - 1].id}`);
});

test('copy keeps the house voice: no exclamation marks, no em-dashes, one short line each', () => {
  for (const it of ITEMS) {
    for (const text of [it.name, it.line]) {
      assert.ok(!/[!—–]/.test(text), `${it.id}: "${text}"`);
    }
    assert.ok(it.line.length <= 80, `${it.id}: line too long`);
    assert.ok(it.name.length <= 24, `${it.id}: name too long`);
  }
});

test('every icon and every word clip ships with the tree', () => {
  for (const it of ITEMS) assert.ok(existsSync(join(ROOT, it.icon)), `${it.id}: missing ${it.icon}`);
  for (const slug of CIRCE) assert.ok(existsSync(join(ROOT, clipUrl(slug))), `missing clip ${slug}`);
  for (const slug of [...MANTRA, ...ENDING, ...Object.values(VOICE).flat()]) assert.ok(CIRCE.includes(slug), `${slug} is not a shipped clip`);
  assert.equal(clipUrl('bambi-sleep'), null, 'unknown words stay silent, never synthesised');
});

test('repeatable items carry a cap; one-shot items carry none', () => {
  for (const it of ITEMS) {
    if (it.grow) assert.ok(it.max > 0, `${it.id}: repeatable without max`);
    else assert.equal(it.max, undefined, `${it.id}: max on a one-shot item`);
  }
});
