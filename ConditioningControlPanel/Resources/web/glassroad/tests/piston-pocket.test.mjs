import test from 'node:test';
import assert from 'node:assert/strict';
import { pistonPocket, pistonPocketPose } from '../track/pistonPocket.js';
import { installMovingSupport, movingPoseAt, pistonHopUp } from '../track/movingHazards.js';
import { setMachineryBeat } from '../track/machineryBeat.js';
import { createMovingHazards, movingOverlap } from '../sim/movingHazards.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { S } from '../sim/state.js';
import { fakeTrack, place, recorder, makeFeel, acts } from './sim-fake.mjs';

function setup() {
  const track = fakeTrack({ w: 36 }), frame = track.frameAt(100);
  const row = { ...frame, id: 'recess', kind: 'piston', s: 100, x: 13, w: 8, len: 6,
    side: 1, travelM: 9, periodBeats: 2, offsetBeats: 0, roadWidth: 36 };
  row.pocket = pistonPocket(row);
  installMovingSupport(track, { traps: [row], bridge: null });
  track.moving.timeS = .5;
  setMachineryBeat(track, { enabled: true, songS: .5, raceS: .5, bpm: 120, playing: false });
  return { track, row };
}
const point = (row, x, z = 0, h = 0) => row.p.map((v, j) => v + row.b[j] * x + row.t[j] * z + row.n[j] * h);

test('pocket opens before the half-sunk head enters, then closes only after withdrawal', () => {
  const { row } = setup();
  for (const side of [-1, 1]) {
    const r = { ...row, side, x: Math.abs(row.x) * side }, p = pistonPocket(r);
    assert.ok(p.minX >= 2.8 || p.maxX <= -2.8, 'centre bypass remains complete');
    for (let extension = 0; extension <= 1; extension += .01) {
      const pose = pistonPocketPose(r, extension);
      assert.equal(pose.headY + p.heightM, 1.6);
      if (pose.open < 1) assert.ok(Math.abs(pose.headX) - r.w / 2 > r.roadWidth / 2, 'head remains outside while lid moves');
      if (Math.abs(pose.panelShift) > 0) assert.ok(pose.panelY <= -2.1 + 1e-8, 'lid is below parked head before sliding');
    }
    assert.equal(Math.abs(pistonPocketPose(r, 0).panelShift), 0);
    assert.equal(pistonPocketPose(r, 1).headX, p.activeX);
  }
});

test('support matches the sliding lid, actual cut and exposed head crown', () => {
  const { track, row } = setup(), pose = movingPoseAt(track, row, .5);
  let q = track.query(...point(row, pose.x, 0, 1.6), 10);
  assert.equal(q.piston, row.id); assert.ok(q.onRoad); assert.ok(Math.abs(q.height) < 1e-8);
  assert.equal(track.query(...point(row, pose.x), 10).onRoad, false, 'side approach cannot snap to head top');
  assert.equal(track.query(...point(row, 17), 10).onRoad, false, 'uncovered side recess is a physical gap');
  assert.ok(track.query(...point(row, 0), 10).onRoad, 'centre bypass is permanent');
  setMachineryBeat(track, { enabled: true, songS: 0, raceS: .5, bpm: 120, playing: false });
  q = track.query(...point(row, 17), 10);
  assert.ok(q.onRoad); assert.ok(!q.piston); assert.ok(Math.abs(q.height) < 1e-8, 'returned cover is flush');
});

test('jump assistance is limited to the active head lane, preserves normal hops and handles reverse approach', () => {
  const { track, row } = setup(), x = row.pocket.activeX;
  const query = (s, lateral = x) => ({ s, lateral, onRoad: true });
  assert.equal(pistonHopUp(track, query(85), 33, 6.8), 10.8);
  assert.equal(pistonHopUp(track, query(115), -33, 6.8), 10.8);
  for (const q of [query(50), query(85, -10), { ...query(85), forkId: 'another-deck' }, { ...query(100), piston: row.id }])
    assert.equal(pistonHopUp(track, q, 33, 6.8), 6.8);
  setMachineryBeat(track, { enabled: true, songS: 0, raceS: .5, bpm: 120, playing: false });
  assert.equal(pistonHopUp(track, query(85), 33, 6.8), 6.8, 'closed plate does not boost hops');
});

test('actual forward and reverse kart hops clear the exposed head and preserve momentum', () => {
  for (const reverse of [false, true]) {
    const { track, row } = setup(), speed = 33, feel = makeFeel(), events = recorder();
    const st = place(track, { s: reverse ? 115 : 85, lateral: row.pocket.activeX, speed, headingDeg: reverse ? 180 : 0 });
    const r = { i: 0, kind: 'local', st, feel, events }, hazards = createMovingHazards(track), a = acts({ throttle: 1, hopPressed: true });
    let apex = 0;
    for (let i = 0; i < 60; i++) {
      const previous = st.slice(); step(st, a, track, feel, 1 / 60, events); a.hopPressed = false;
      hazards.apply(r, previous, .5 + i / 60); apex = Math.max(apex, st[S.posY]);
    }
    assert.ok(apex > 1.9, 'jump rises above half head');
    assert.equal(hazards.inspect().hits, 0, reverse ? 'reverse hop' : 'forward hop');
    assert.ok(st[S.engineV] > 28, 'clean crossing retains speed');
  }
});

test('low crossings still hit, high crossings and other decks do not', () => {
  const { row } = setup(), pose = { x: row.pocket.activeX, active: true };
  assert.ok(movingOverlap(row, pose, { x: pose.x, z: 0, h: .7 }));
  assert.equal(movingOverlap(row, pose, { x: pose.x, z: 0, h: 1.6 }), false);
  assert.equal(movingOverlap(row, pose, { x: pose.x, z: 0, h: 8 }), false);
});

test('a descending kart lands on the piston top and can hop off it', () => {
  const { track, row } = setup(), feel = makeFeel(), events = recorder();
  const st = place(track, { s: 100, lateral: row.pocket.activeX, height: 2.2, airborne: true });
  st[S.velY] = -3;
  const actions = acts();
  for (let i = 0; i < 30 && !st[S.grounded]; i++) step(st, actions, track, feel, 1 / 60, events);
  assert.equal(st[S.grounded], 1);
  assert.ok(Math.abs(st[S.posY] - 1.6) < .001);
  actions.hopPressed = true; step(st, actions, track, feel, 1 / 60, events);
  assert.equal(st[S.airborne], 1); assert.ok(st[S.posY] > 1.6);
});

test('a missed jump into the open recess reaches normal recovery instead of hovering', () => {
  const { track, row } = setup(), feel = makeFeel(), events = recorder();
  const st = place(track, { s: 100, lateral: 17, airborne: true }), actions = acts();
  let fell = false, recovered = false;
  for (let i = 0; i < 360; i++) {
    step(st, actions, track, feel, 1 / 60, events);
    updateRespawn(st, track, feel, 1 / 60, events);
    fell ||= st[S.posY] < -.1;
    recovered ||= st[S.respawnPhase] > 0;
  }
  assert.ok(fell, 'unsupported gap allows a real fall');
  assert.ok(recovered, 'ordinary rescue takes over');
  assert.ok(events.all('drop').some(e => e.payload.cause === 'hole'));
  assert.ok(events.names().includes('rescue-pull'));
});
