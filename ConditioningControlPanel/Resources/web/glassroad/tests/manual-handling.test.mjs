import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S } from '../sim/state.js';
import { slewSteer, gripYaw, baseYaw } from '../sim/steer.js';
import { driftYaw } from '../sim/drift.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

test('manual steering pulses release yaw promptly without a lingering opposite-side bounce', () => {
  const feel = makeFeel(), track = fakeTrack({ w: 24, flags: () => [] }), st = place(track, { speed: 30 });
  const events = recorder(); let maxLateral = 0;
  const watch = s => { maxLateral = Math.max(maxLateral, Math.abs(s[S.lateral])); return false; };
  for (const side of [1, -1, 1, -1]) {
    run(st, track, feel, acts({ throttle: 1, steer: side }), .25, events, watch);
    run(st, track, feel, acts({ throttle: 1 }), .2, events, watch);
    assert.ok(Math.abs(st[S.yawRate]) < .012, 'released turning is under .7 deg/s');
  }
  assert.ok(maxLateral < 4, 'small corrections stay within four metres: ' + maxLateral);
  assert.ok(!events.first('wall') && !events.first('drop'), 'no rail or rescue masks handling');
});

test('full-lock countersteering changes yaw side within 50 ms and old feel files work', () => {
  const feel = makeFeel(); let steer = 1;
  for (let i = 0; i < 3; i++) steer = slewSteer(steer, -1, feel, DT);
  assert.ok(steer < 0, 'countersteer acts before a second correction is needed');
  const legacy = { get(key) { if (key === 'steer.releaseS') throw new Error('old feel'); return feel.get(key); } };
  assert.equal(slewSteer(1, 0, legacy, DT), slewSteer(1, 0, feel, DT));
});

test('countersteered and released hop drift stays on a normal 24 m road without assist', () => {
  for (const dir of [-1, 1]) {
    const feel = makeFeel(), track = fakeTrack({ w: 24, flags: () => [] }), st = place(track, { speed: 30 });
    const events = recorder(); let maxLateral = 0;
    const watch = s => { maxLateral = Math.max(maxLateral, Math.abs(s[S.lateral])); return false; };
    run(st, track, feel, acts({ throttle: 1, hop: true, hopPressed: true, steer: dir }), .35, events, watch);
    run(st, track, feel, acts({ throttle: 1, hop: true, steer: -dir }), .35, events, watch);
    run(st, track, feel, acts({ throttle: 1, steer: -dir }), .3, events, watch);
    run(st, track, feel, acts({ throttle: 1 }), .3, events, watch);
    assert.ok(events.first('drift-release'), 'manual release remains available after countersteering');
    assert.equal(st[S.driftDir], 0, 'letting hop go ends the slide');
    assert.ok(maxLateral < 8, 'countersteering leaves visible road room: ' + maxLateral);
    assert.ok(!events.first('wall') && !events.first('drop'), 'no wall, rescue or auto clipping');
  }
});

test('speed calm bounds drift at boost speeds while tight Threshold bends remain available', () => {
  const feel = makeFeel(), track = fakeTrack(), st = place(track); st[S.driftDir] = 1;
  for (const speed of [20, 30, 39]) {
    const now = driftYaw(st, 1, feel, gripYaw(speed, feel));
    const before = baseYaw(speed, feel) * 1.45;
    assert.ok(now < before * .6, speed + ' m/s drift avoids the old yaw spike');
    assert.ok(driftYaw(st, -1, feel, gripYaw(speed, feel)) < now * .4, 'countersteer widens the arc');
  }
  assert.ok(driftYaw(st, 1, feel, gripYaw(30, feel)) >= 30 / 36, 'full inward lock holds the 36 m helix');
});
