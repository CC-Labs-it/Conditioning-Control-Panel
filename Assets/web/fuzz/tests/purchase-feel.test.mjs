import test from 'node:test';
import assert from 'node:assert/strict';
import { goalState, sparkBudget, SPARK_ROOM } from '../ui/purchase-feel.js';
import { CONFIG } from '../data/config.js';

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

test('landing sparkles stay under half the particle ceiling and never go negative', () => {
  assert.ok(SPARK_ROOM <= Math.max(6, CONFIG.MAX_PARTICLES / 2));
  assert.equal(sparkBudget(0, 10, 20), 10);
  assert.equal(sparkBudget(15, 10, 20), 5);
  assert.equal(sparkBudget(25, 10, 20), 0);
});
