import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createTrailBudget, incomeFeel } from '../ui/income-feel.js';

test('income decoration caps simultaneous packets and spaces starts across every source', () => {
  const budget = createTrailBudget();
  const first = budget.take(0);
  assert.equal(typeof first, 'function');
  assert.equal(budget.take(849), null);
  const second = budget.take(850);
  const third = budget.take(1700);
  assert.equal(budget.take(2550), null);
  first(); first();
  assert.equal(budget.live, 2, 'cleanup is safe when cancel and completion both run');
  const fourth = budget.take(2550);
  assert.equal(budget.live, 3);
  second(); third(); fourth();
  assert.equal(budget.live, 0);
});

test('non-generators get no income effects or timers', () => {
  incomeFeel({ item: { fps: 0 } }, () => { throw new Error('must not read layout'); });
});

test('Gentle mode and unearned effects do not read layout, pay money or start packets', () => {
  let tick;
  const ctx = {
    item: { id: 'test', fps: 2 }, world: { ownerDocument: {} }, state: { order: ['test'] },
    cleanup() {}, every(ms, fn) { tick = fn; }, frame() {}, gentle: true,
    rates: () => ({ fps: 2 }), has: () => false,
    pay() { throw new Error('decoration must never pay'); },
  };
  incomeFeel(ctx, () => { throw new Error('must not read layout'); });
  tick();
  ctx.gentle = false;
  tick();
});
