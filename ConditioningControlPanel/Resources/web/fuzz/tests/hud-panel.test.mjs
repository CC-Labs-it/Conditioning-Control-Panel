import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pillPlan, stripHeight, PILL_GAP, STRIP_MAX_ROWS } from '../ui/dock.js';
import { panelTabs, panelTabOf, PANEL_TABS } from '../ui/unlocks/wave2/meta.js';

test('icon pills always fit the strip: rows grow to three, then pills squeeze, never a scroll', () => {
  for (const avail of [328, 344, 600, 1264]) {
    for (let n = 1; n <= 80; n++) {
      const { rows, basis } = pillPlan(n, avail);
      assert.ok(rows >= 1 && rows <= 8, `rows ${rows}`);
      const across = Math.ceil(n / rows);
      assert.ok(across * basis + (across - 1) * PILL_GAP <= avail, `${n} pills at ${avail}px overflow`);
      assert.ok(basis <= 44);
    }
  }
  assert.equal(pillPlan(10, 344).rows, 1);
  assert.ok(pillPlan(30, 344).rows >= 2 && pillPlan(30, 344).rows <= STRIP_MAX_ROWS);
  assert.equal(stripHeight(1), null);
  assert.ok(stripHeight(2) > 0 && stripHeight(3) > stripHeight(2));
});

test('control panel tabs sort lanes into Effects, Looks, Sound and Toys and drop empty tabs', () => {
  assert.deepEqual(PANEL_TABS.map((t) => t.id), ['effects', 'looks', 'sound', 'toys']);
  assert.equal(panelTabOf('look'), 'looks');
  assert.equal(panelTabOf('ears'), 'sound');
  assert.equal(panelTabOf('side'), 'toys');
  assert.equal(panelTabOf('eyes'), 'effects');
  assert.equal(panelTabOf('bimbo'), 'effects');
  const g = (lane, n) => ({ lane, name: lane, rows: Array.from({ length: n }, (_, i) => ({ id: `${lane}${i}`, lane })) });
  const tabs = panelTabs([g('trunk', 3), g('look', 2), g('side', 4), g('eyes', 1)]);
  assert.deepEqual(tabs.map((t) => [t.id, t.count]), [['effects', 4], ['looks', 2], ['toys', 4]]);
});
