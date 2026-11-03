import test from 'node:test';
import assert from 'node:assert/strict';
import { goalState } from '../ui/purchase-feel.js';

test('an affordable goal on first draw or reload stays quiet', () => {
  assert.equal(goalState(undefined, true).announce, false);
  assert.equal(goalState(undefined, false).announce, false);
});

test('a goal celebrates its first earned affordability, once across balance changes', () => {
  let state = goalState(undefined, false);
  state = goalState(state.affordable, true, state.announced);
  assert.equal(state.announce, true);
  state = goalState(state.affordable, false, state.announced);
  state = goalState(state.affordable, true, state.announced);
  assert.equal(state.announce, false);
});
