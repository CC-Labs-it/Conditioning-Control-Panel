import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { slotName, slotIndex, tagFor, itemRoot, layoutTab, usedBays } from '../scene/layout.js';

const json = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url), 'utf8'));
const state = json('../mock/state.sample.json'), manifest = json('../assets/manifest.json');

test('row j sits in slot r{floor(j/3)}c{j%3}', () => {
  assert.deepEqual([0, 2, 3, 5, 8].map(slotName), ['r0c0', 'r0c2', 'r1c0', 'r1c2', 'r2c2']);
  assert.equal(slotName(9), null);
  assert.equal(slotName(-1), null);
  for (let j = 0; j < 9; j++) assert.equal(slotIndex(slotName(j)), j);
  assert.equal(slotIndex('slot_r2c1'), 7);
  assert.equal(slotIndex(4), 4);
  assert.equal(slotIndex('r3c0'), -1);
});

test('price tag while it sells, stamp once owned, bare while soon', () => {
  assert.equal(tagFor({ sale: 'on', owned: null }), 'price');
  assert.equal(tagFor({ sale: 'on', owned: { at: 'x', paidSp: 1 } }), 'owned');
  assert.equal(tagFor({ sale: 'soon', owned: null }), null);
});

test('item root names follow the contract', () => {
  assert.equal(itemRoot('chess.banner.house-pink'), 'item_chess_banner_house-pink');
  for (const i of manifest.items) assert.equal(itemRoot(i.id), i.root);
});

test('shelf i hangs in bay_i, extra shelves and rows are dropped', () => {
  const row = (id) => ({ id, sale: 'on', owned: null });
  const shelves = Array.from({ length: 5 }, (_, b) => ({ rows: Array.from({ length: 11 }, (_, j) => row(`b${b}.${j}`)) }));
  const out = layoutTab(shelves);
  assert.equal(out.length, 4 * 9);
  const p = out.find((x) => x.id === 'b2.7');
  assert.deepEqual([p.bay, p.slot, p.slotNode, p.tagNode], [2, 7, 'slot_r2c1', 'tag_r2c1']);
  assert.deepEqual(usedBays(shelves), [0, 1, 2, 3]);
  assert.deepEqual(usedBays([{ rows: [] }]), [0]);
  assert.deepEqual(layoutTab(null), []);
});

test('every mock row has art and lands on a real slot and tag', () => {
  const ids = new Set(manifest.items.map((i) => i.id));
  for (const tab of state.tabs) {
    for (const p of layoutTab(tab.shelves)) {
      assert.ok(ids.has(p.id), `${tab.id}: no art for ${p.id}`);
      assert.ok(manifest.shelf.nodes.includes(p.slotNode));
      assert.ok(manifest.shelf.nodes.includes(p.tagNode));
    }
    for (const b of usedBays(tab.shelves)) assert.ok(manifest.scene.nodes.includes('bay_' + b) && manifest.scene.nodes.includes('cam_bay_' + b));
  }
  const owned = state.tabs.flatMap((t) => layoutTab(t.shelves)).filter((p) => p.tag === 'owned');
  assert.equal(owned.length, 3);
});
