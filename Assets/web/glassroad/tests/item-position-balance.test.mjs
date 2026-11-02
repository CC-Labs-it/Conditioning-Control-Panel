import { test } from 'node:test';
import assert from 'node:assert/strict';
import { makeFeel } from './sim-fake.mjs';
import { bucketOf, weightsFor, rollItem, makeRand } from '../sim/itemTable.js';

test('leaders predominantly draw speed and shields, retaining a few rear traps and attacks', () => {
  const feel = makeFeel(), bucket = bucketOf(feel, 1, 0);
  const weights = Object.fromEntries(weightsFor(feel, bucket));
  const total = Object.values(weights).reduce((sum, n) => sum + n, 0);
  const useful = (weights.surge + weights.glass) / total;
  assert.ok(useful >= 0.6 && useful <= 0.7, 'speed and shields should make up about two thirds');
  assert.ok(weights.glass >= weights.surge, 'protection should be at least as common as speed');
  assert.ok((weights.pendulum + weights.disc) / total <= 0.12, 'few forward projectiles in front');
  assert.ok(weights.melt > 0 && weights.snap > 0, 'rear traps and close defence remain available');
  assert.ok(!weights.bigSpiral && !weights.tether, 'comeback tools stay behind the leader');
  const rand = makeRand(29);
  let speed = 0, shields = 0, attacks = 0;
  for (let n = 0; n < 10000; n++) {
    const id = rollItem(feel, bucket, rand);
    speed += Number(id === 'surge'); shields += Number(id === 'glass');
    attacks += Number(id === 'pendulum' || id === 'disc');
  }
  assert.ok(speed > 2500 && shields > 3000, 'both useful pickups appear often in actual draws');
  assert.ok(speed + shields > 6000 && attacks < 1200, 'seeded pickup mix matches the leading bias');
});

test('middle and rear pickups retain their comeback tools and stronger speed reward', () => {
  const feel = makeFeel();
  const middle = bucketOf(feel, 4, 60), back = bucketOf(feel, 8, 200);
  for (const bucket of [middle, back]) {
    const weights = Object.fromEntries(weightsFor(feel, bucket));
    const total = Object.values(weights).reduce((sum, n) => sum + n, 0);
    assert.ok((weights.surge + weights.glass) / total < 0.35);
    assert.ok(weights.tether > 0 && weights.disc > 0 && weights.pendulum > 0);
  }
  assert.ok(Object.fromEntries(weightsFor(feel, back)).bigSpiral > 0);
  const [frontBoost, middleBoost, backBoost] = feel.get('items.surgeAmount');
  assert.ok(frontBoost < middleBoost && middleBoost < backBoost);
});
