import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import test from 'node:test';
import { BY_ID } from '../data/items.js';
import { createRun, grant, isOpen } from '../sim/economy.js';
import { REGISTRY } from '../ui/unlocks/index.js';

test('Lo-fi Radio is a separate purchase after Pick a Theme', () => {
  const run = createRun();
  const radio = BY_ID.lofi_radio;
  assert.equal(isOpen(run, radio), false);
  grant(run, 'lo_head');
  assert.equal(run.picks.f5, 'locked');
  assert.equal(isOpen(run, radio), true);
  assert.ok(!run.owned.lofi_radio);
  assert.equal(typeof REGISTRY.lofi_radio.mount, 'function');
});

const guided = new URL('../sim/journey.js', import.meta.url);
test('the guided shop offers the radio with the first themed purchases', {skip: !existsSync(guided)}, async () => {
  const { createJourney } = await import(guided.href);
  const run = createRun(), shop = createJourney(run);
  for (let step = 0; step < 30; step++) {
    const { goal, rows } = shop.shop(6);
    if (run.picks.f5) {
      assert.ok([goal, ...rows].some(item => item.id === 'lofi_radio'));
      assert.ok(!run.owned.lofi_radio);
      return;
    }
    assert.ok(![goal, ...rows].some(item => item.id === 'lofi_radio'));
    const cost = shop.price(goal.id);
    run.total = Math.max(run.total, cost);
    run.fuzz = cost;
    const purchase = goal.kind === 'fork'
      ? shop.choose(goal.id, { f1: 'eyes', f5: 'locked' }[goal.id])
      : shop.buy(goal.id);
    assert.ok(purchase.ok, goal.id);
  }
  assert.fail('Pick a Theme was never offered');
});
