import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ITEMS } from '../sim/itemTable.js';
import { iconOf } from '../hud/format.js';

const toys = ['pendulum', 'disc', 'tether', 'melt', 'snap', 'surge', 'bigSpiral'];
test('every toy has a distinct matching HUD and world icon', () => {
  assert.equal(new Set(toys.map(id => ITEMS[id].icon)).size, toys.length);
  for (const id of toys) assert.equal(ITEMS[id].icon, iconOf(id), id);
});
test('toy sprites ship at 256px with full RGBA alpha', () => {
  for (const id of toys) {
    const png = readFileSync(new URL('../' + iconOf(id), import.meta.url));
    assert.equal(png.toString('hex', 0, 8), '89504e470d0a1a0a', id);
    assert.equal(png.readUInt32BE(16), 256, id + ' width');
    assert.equal(png.readUInt32BE(20), 256, id + ' height');
    assert.equal(png[24], 8, id + ' depth');
    assert.equal(png[25], 6, id + ' RGBA');
  }
});
