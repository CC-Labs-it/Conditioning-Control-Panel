import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { safeBoostTurn, BOOST_TURN } from '../track/boostSafety.js';
import { gripYaw } from '../sim/steer.js';
import { makeFeel } from './sim-fake.mjs';
import { onlineCourseHash } from '../net/courseHash.js';

const read = id => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));

test('boost rollout radii exceed the real full-lock steering requirement at pad and cruise speeds', () => {
  const feel = makeFeel();
  assert.ok(BOOST_TURN.radiusM > 36 / gripYaw(36, feel));
  assert.ok(BOOST_TURN.cruiseRadiusM > 30 / gripYaw(30, feel));
  assert.ok(BOOST_TURN.forcedM > 36 * (feel.get('pad.holdS') + feel.get('boost.tailTcS')));
});

for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
  test(id + ': every original and generated live boost has a steerable rollout and some boosts remain', () => {
    const json = read(id), source = loadTrack(json, { boostSafety: false }), live = loadTrack(json, { moving: true });
    assert.ok(live.boosts.length >= 6, 'retain meaningful acceleration choices');
    assert.ok(live.boosts.length < source.boosts.length, 'actual unsafe pads were removed');
    for (const pad of live.boosts) assert.ok(safeBoostTurn(live, pad), 'unsafe node ' + pad.node);
    assert.ok(source.boosts.some(p => !safeBoostTurn(source, p)), 'real regression fixture');
  });
}

test('Threshold first rainbow boost no longer forces the opening right turn at pad speed', () => {
  const json = read('threshold'), source = loadTrack(json, { boostSafety: false }), live = loadTrack(json);
  const first = source.boosts.find(p => p.node === json.boosts[0].node && p.x === 0);
  assert.ok(first && !safeBoostTurn(source, first));
  assert.ok(!live.boosts.some(p => p.node === first.node));
  assert.deepEqual(json, read('threshold'), 'authored geometry and jump anchors stay intact');
});

test('online identity includes the actual safe boost plan, so old authority cards refuse it', async () => {
  const json = read('threshold'), feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
  const safe = loadTrack(json, { moving: true }), old = loadTrack(json, { moving: true, boostSafety: false });
  assert.notEqual(await onlineCourseHash(json, feel, safe), await onlineCourseHash(json, feel, old));
});
