// Polish 6 (JUICE): the item slot's states and moments, and the drift pip moments (hud/slotMath.js).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { slotState, slotMoment, gentleMoment, pipMoment, SLOT_STATES, USED_S } from '../hud/slotMath.js';

test('slot: empty, rolling, ready and used from the item and the clock', () => {
  assert.deepEqual(slotState(null, null), { state: 'empty', id: null });
  assert.deepEqual(slotState({ id: 'spiral', rolling: true }, { id: 'flash', tick: 3 }), { state: 'rolling', id: 'flash' });
  assert.deepEqual(slotState({ id: 'spiral', rolling: true }, { id: 'spiral', tick: -1 }), { state: 'ready', id: 'spiral' }, 'a settled roll reads ready');
  assert.deepEqual(slotState({ id: 'spiral', rolling: false }, null), { state: 'ready', id: 'spiral' });
  // fired: used until usedUntil, with the item it was, then empty
  assert.deepEqual(slotState(null, null, { nowS: 10, usedUntil: 10 + USED_S, usedId: 'pink' }), { state: 'used', id: 'pink' });
  assert.deepEqual(slotState(null, null, { nowS: 10 + USED_S, usedUntil: 10 + USED_S, usedId: 'pink' }), { state: 'empty', id: null });
  // a new card grabbed while the used flare runs wins at once
  assert.equal(slotState({ id: 'flash', rolling: true }, { id: 'melt', tick: 0 }, { nowS: 10, usedUntil: 11 }).state, 'rolling');
  for (const s of ['empty', 'rolling', 'ready', 'used']) assert.ok(SLOT_STATES.includes(s));
});

test('slot: one moment per edge, each with its class; Gentle keeps fades only', () => {
  assert.equal(slotMoment('empty', 'rolling'), 'catch');
  assert.equal(slotMoment('used', 'rolling'), 'catch');
  assert.equal(slotMoment('rolling', 'ready'), 'land');
  assert.equal(slotMoment('ready', 'used'), 'use');
  assert.equal(slotMoment('used', 'empty'), 'none', 'the use flare already played its OUT');
  assert.equal(slotMoment('ready', 'empty'), 'drop', 'an item lost some other way fades');
  assert.equal(slotMoment('ready', 'ready'), 'none');
  assert.equal(gentleMoment('land', false), 'm-land');
  assert.equal(gentleMoment('catch', true), 'm-soft');
  assert.equal(gentleMoment('use', true), 'm-use-soft');
  assert.equal(gentleMoment('none', false), null);
});

test('pips: a new tier pops its own pip, a release flashes the row out, a cancel does not', () => {
  assert.deepEqual(pipMoment(0, 1), { pop: 0, out: false });
  assert.deepEqual(pipMoment(1, 2), { pop: 1, out: false });
  assert.deepEqual(pipMoment(2, 3), { pop: 2, out: false });
  assert.deepEqual(pipMoment(3, 3), { pop: -1, out: false });
  assert.deepEqual(pipMoment(3, 0), { pop: -1, out: false });
  assert.deepEqual(pipMoment(2, 0, { released: true }), { pop: -1, out: true });
  assert.deepEqual(pipMoment(0, 0, { released: true }), { pop: -1, out: false }, 'nothing lit, nothing to flash');
});
