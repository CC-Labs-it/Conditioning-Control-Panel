import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { safeBoostTurn, BOOST_TURN, BOOST_FEEL, boostTurnProfile, boostTurnRadius } from '../track/boostSafety.js';
import { gripYaw } from '../sim/steer.js';
import { makeFeel } from './sim-fake.mjs';
import { onlineCourseHash } from '../net/courseHash.js';

const read = id => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));

test('boost rollout radii exceed the real full-lock steering requirement at pad and cruise speeds', () => {
  const feel = makeFeel();
  const maxSolo = feel.get('speed.topMps') * (1 + feel.get('cleanPace.cleanGain') + feel.get('cleanPace.musicGain'));
  const maxPad = maxSolo * Math.max(1, feel.get('items.tetherTowCap')) * (1 + feel.get('pad.amount'));
  assert.ok(Math.abs(BOOST_TURN.padSpeedMps - maxPad) < 1e-8);
  assert.ok(BOOST_TURN.radiusM > maxPad / gripYaw(maxPad, feel));
  assert.ok(BOOST_TURN.cruiseRadiusM > 30 / gripYaw(30, feel));
  assert.ok(BOOST_TURN.forcedM > maxPad * (feel.get('pad.holdS') + 5 * feel.get('boost.tailTcS')));
});

test('room-card defaults match actual tuning, and changed tuning expands the required rollout', () => {
  const feel = makeFeel();
  for (const [group, values] of Object.entries(BOOST_FEEL)) for (const [key, value] of Object.entries(values)) {
    assert.equal(feel.get(group + '.' + key), value, group + '.' + key + ' must update the shared room profile');
  }
  const original = boostTurnProfile(feel);
  feel.set('cleanPace.musicGain', .4);
  const faster = boostTurnProfile(feel);
  assert.ok(faster.radiusM > original.radiusM && faster.rolloutM > original.rolloutM);
});

test('an old accepted 100m boost curve is refused at the real solo speed', () => {
  const circle = radius => ({ nodeS: [0], surfaceAt(s, x) {
    const r = radius - x, a = s / radius;
    return { p: [r * Math.sin(a), 0, r * (1 - Math.cos(a))] };
  } });
  const pad = { node: 0, s: 0, x: 0, w: 10, len: 7.5 };
  assert.ok(!safeBoostTurn(circle(100), pad));
  assert.ok(!safeBoostTurn(circle(280), pad), 'towing also needs more room than the old solo-only radius');
  const broadRadius = BOOST_TURN.radiusM + pad.w + 10;
  assert.ok(safeBoostTurn(circle(broadRadius), pad));
  assert.ok(boostTurnRadius(circle(broadRadius), pad) > BOOST_TURN.radiusM);
});

for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
  test(id + ': every original and generated live boost has a steerable rollout and some boosts remain', () => {
    const json = read(id), source = loadTrack(json, { boostSafety: false }), live = loadTrack(json, { moving: true });
    assert.ok(live.boosts.length >= 6, 'retain meaningful acceleration choices');
    const unsafe = source.boosts.filter(pad => !safeBoostTurn(source, pad));
    assert.ok(unsafe.length > 0, 'real unsafe authoring fixtures exist');
    for (const old of unsafe) assert.ok(!live.boosts.some(pad => pad.s === old.s && pad.x === old.x),
      'unsafe original entry is absent even when a new safe train is added');
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
