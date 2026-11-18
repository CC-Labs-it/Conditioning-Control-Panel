import test from 'node:test';
import assert from 'node:assert/strict';
import { cheerForEvent, createCheerState, CHEER_DURATION } from '../board/cheer-reactions.js';

test('Emi reacts to public chess events from the equipped seat perspective', () => {
  assert.equal(cheerForEvent('capture', { by: 'w' }, 'w'), 'cheer');
  assert.equal(cheerForEvent('capture', { by: 'w' }, 'b'), 'worry');
  assert.equal(cheerForEvent('check', { side: 'w' }, 'w'), 'scared');
  assert.equal(cheerForEvent('gameover', { winner: 'b' }, 'b'), 'win');
  assert.equal(cheerForEvent('gameover', { winner: 'b' }, 'w'), 'comfort');
  assert.equal(cheerForEvent('land', { side: 'w', refused: true }, 'w'), null);
  assert.equal(cheerForEvent('iq', { value: 0 }, 'w'), null);
});

test('check persists above pets and idles until the position clears it', () => {
  const state=createCheerState();
  state.trigger('pet');state.setChecked(true);
  assert.equal(state.trigger('pet'),false);assert.equal(state.trigger('nod'),false);
  for(let i=0;i<100;i++)state.update(.1);
  assert.equal(state.update(0).action,'scared');
  state.reset();assert.equal(state.update(0).checked,true,'pausing cannot forget check');
  state.setChecked(false);assert.equal(state.update(0).action,null);
  assert.equal(state.trigger('pet'),true);
  state.setChecked(true);state.reset(true);assert.equal(state.update(0).checked,false);
});
test('duplicate events do not restart a cheer, lower priorities do not replace a result', () => {
  const state = createCheerState();
  assert.equal(state.trigger('cheer', 'capture|1'), true);
  state.update(.1);
  assert.equal(state.trigger('cheer', 'capture|1'), false);
  assert.equal(state.trigger('win', 'result'), true);
  assert.equal(state.trigger('nod', 'land'), false);
  assert.equal(state.update(0).action, 'win');
  for (let i = 0; i < 50; i++) state.update(.1);
  assert.equal(state.update(0).action, null);
});
test('paused clocks hold reactions and each native reaction expires', () => {
  const state = createCheerState();
  state.trigger('cheer');
  state.update(.1);
  assert.deepEqual(state.update(0), state.update(0));
  for (const [action, duration] of Object.entries(CHEER_DURATION)) {
    state.reset();
    assert.equal(state.trigger(action), true);
    assert.equal(state.update(0).action, action);
    for (let elapsed = 0; elapsed < duration + .2; elapsed += .1) state.update(.1);
    assert.equal(state.update(0).action, null);
  }
  state.reset();
  assert.equal(state.update(0).action, null);
});
