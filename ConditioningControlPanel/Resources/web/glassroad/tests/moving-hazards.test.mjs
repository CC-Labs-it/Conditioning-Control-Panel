import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { bridgePose, movingPose, movingPoseAt, MOVING_REVISION } from '../track/movingHazards.js';
import { setMachineryBeat, hazardBeatAt } from '../track/machineryBeat.js';
import { createMovingHazards, movingOverlap } from '../sim/movingHazards.js';
import { S, RESPAWN } from '../sim/state.js';
import { fakeTrack, place, recorder, makeFeel } from './sim-fake.mjs';
import { courseHash } from '../net/courseHash.js';
const courses = ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks'];
const jsonFor = id => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));

test('every course has six physical traps and a shorter forming bridge with permanent main-road support', () => {
  for (const id of courses) {
    const track = loadTrack(jsonFor(id), { moving: true }), plan = track.moving, bridge = plan.bridge;
    assert.equal(plan.traps.length, 6, id);
    const fork = track.forks.find(f => f.id === bridge.forkId);
    assert.ok(fork.lengthM < (fork.toS - fork.fromS) * .91, id + ' offers a meaningful shortcut');
    const localM = (bridge.fromM + bridge.toM) / 2, f = fork.frameAt(localM);
    track.moving.timeS = 3;
    assert.ok(track.query(...f.p, f.seg).onRoad, id + ' extended deck supports a kart');
    track.moving.timeS = 10;
    assert.equal(track.query(...f.p, f.seg).onRoad, false, id + ' withdrawn deck is a real gap');
    const main = track.frameAt(fork.mainS(localM));
    assert.ok(track.query(...main.p, main.seg).onRoad, id + ' bypass never closes');
    for (const row of plan.traps) for (let beat = 0; beat < 4; beat += .03) {
      const p = movingPose(row, 0, { beat, beatS: .5, safe: false }), lane = -row.side * (row.roadWidth / 2 - 3);
      assert.ok(Math.abs(lane - p.x) > row.w / 2 + .7, id + ' outer lane is always clear');
      const road = track.frameAt(row.s), position = road.p.map((v, j) => v + road.b[j] * lane);
      assert.ok(track.query(...position, road.seg).onRoad, row.id + ' bypass has physical road beneath it');
    }
  }
});

test('bridge timer ignores tempo, has a warned exit and extends continuously across its wrap', () => {
  const b = { periodS: 14, openS: 8, transitionS: .8 };
  for (let index = 0; index < 15; index++) {
    assert.ok(bridgePose(b, 3, index, 15).supported);
    assert.ok(!bridgePose(b, 10, index, 15).supported);
    assert.ok(bridgePose(b, 6.1, index, 15).warning);
    assert.ok(Math.abs(bridgePose(b, 14 - .000001, index, 15).extension - bridgePose(b, 14, index, 15).extension) < .00001);
  }
});

test('long horizontal heads extend their real contact while retaining permanent bypasses', () => {
  assert.equal(MOVING_REVISION, 3);
  for (const id of courses) {
    const track = loadTrack(jsonFor(id), { moving: true });
    for (const row of track.moving.traps) {
      assert.ok(row.w >= 8 && row.len >= 5.6, row.id + ' has the larger physical footprint');
      const pose = movingPoseAt(track, row, .5);
      assert.ok(movingOverlap(row, { ...pose, active: true }, { x: pose.x + row.w / 2 + .69, z: 0, h: 1 }));
      assert.equal(movingOverlap(row, { ...pose, active: true }, { x: pose.x + row.w / 2 + .71, z: 0, h: 1 }), false);
      assert.ok(movingOverlap(row, { ...pose, active: true }, { x: pose.x, z: row.len / 2 + .69, h: 1 }));
      assert.equal(movingOverlap(row, { ...pose, active: true }, { x: pose.x, z: row.len / 2 + .71, h: 1 }), false);
    }
  }
});

test('horizontal strike is continuous at pickup and seek, hits a whole beat and freezes with the heard transport', () => {
  const row = { x: 10, side: 1, travelM: 10, periodBeats: 2, offsetBeats: 0 };
  const track = {};
  setMachineryBeat(track, { enabled: true, songS: 0, raceS: 0, bpm: 120 });
  assert.equal(movingPoseAt(track, row, .5).extension, 1);
  setMachineryBeat(track, { enabled: true, songS: .5, raceS: .5, bpm: 120, playing: false });
  assert.deepEqual(movingPoseAt(track, row, .5), movingPoseAt(track, row, 10));
  setMachineryBeat(track, { enabled: true, songS: 4, raceS: .6, bpm: 120 });
  assert.equal(movingPoseAt(track, row, .6).extension, 0);
  for (const offsetBeats of [0, 1]) {
    const r = { ...row, offsetBeats }, start = offsetBeats ? 1 : 0;
    const a = movingPose(r, 0, { beat: start - .000001, beatS: .5, safe: start <= 0, rearming: true });
    const b = movingPose(r, 0, { beat: start + .000001, beatS: .5, safe: false, rearming: true });
    assert.ok(Math.abs(a.x - b.x) < .0001);
  }
  assert.equal(hazardBeatAt({}, .5).beat, 1);
});

function setup() {
  const track = fakeTrack({ w: 36 }), f = track.frameAt(100);
  track.moving = { traps: [{ ...f, id: 'test-ram', s: 100, x: 8, w: 5, len: 4, side: 1,
    travelM: 8, periodBeats: 2, offsetBeats: 0, kind: 'piston', roadWidth: 36 }] };
  const r = { i: 0, kind: 'local', st: place(track, { s: 112, speed: 80 }), feel: makeFeel(), events: recorder(), fxv: { spinRad: 0 } };
  return { track, r, moving: createMovingHazards(track), previous: place(track, { s: 88, speed: 80 }) };
}

test('a high-speed swept piston hit pays one bounded spin, and reset clears it', () => {
  const { track, r, moving, previous } = setup();
  assert.equal(moving.apply(r, previous, .5), 'test-ram');
  assert.ok(r.st[S.stalled] <= .550001 && r.st[S.engineV] > 0);
  assert.equal(moving.apply(r, previous, .5), null);
  assert.equal(moving.inspect(.5).hits, 1);
  r.st = place(track, { s: 130, speed: 10 });
  moving.apply(r, r.st, .6);
  r.fxv.spinRad = 0; // Item modifiers write their own spin before trap decoration.
  moving.decorate([r], .6);
  assert.ok(Math.abs(r.fxv.spinRad) > .1 && Math.abs(r.fxv.spinRad) < Math.PI * 2);
  moving.reset(); r.fxv.spinRad = 0; moving.apply(r, r.st, .7); moving.decorate([r], .7);
  assert.equal(r.fxv.spinRad, 0); assert.equal(moving.inspect(.7).hits, 0);
});

test('a bypass, another deck, remote racers, rescue and teleports cannot trigger a piston', () => {
  for (const mode of ['bypass', 'upper-deck', 'remote', 'rescue', 'teleport']) {
    const { track, r, moving, previous } = setup();
    if (mode === 'bypass') {
      r.st = place(track, { s: 112, lateral: -14, speed: 80 }); previous.set(place(track, { s: 88, lateral: -14, speed: 80 }));
    }
    if (mode === 'upper-deck') { r.st[S.posY] += 8; previous[S.posY] += 8; }
    if (mode === 'remote') r.kind = 'remote';
    if (mode === 'rescue') r.st[S.respawnPhase] = RESPAWN.PULL;
    if (mode === 'teleport') r.st[S.posX] += 200;
    assert.equal(moving.apply(r, previous, .5), null, mode);
  }
});

test('online admission fingerprint includes timed geometry and machinery revision', async () => {
  const json = jsonFor('threshold'), track = loadTrack(json, { moving: true }), legacy = { aerialRevision: 3, aerials: track.aerials };
  assert.notEqual(await courseHash(json, {}, legacy), await courseHash(json, {}, { ...legacy, moving: track.moving }));
});

test('visible heads, connected rods and bridge slabs share physical poses and dispose their resources', async () => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    if (specifier === 'three/webgpu') return { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true };
    if (specifier === 'three/tsl') return { url: new URL('../vendor/three/build/three.tsl.min.js', import.meta.url).href, shortCircuit: true };
    return next(specifier, context);
  } });
  try {
    const { Scene } = await import('three/webgpu'), { createMovingHazardView } = await import('../render/movingHazards.js');
    for (const id of courses) {
      const track = loadTrack(jsonFor(id), { moving: true }), scene = new Scene(), view = createMovingHazardView(scene, track,
        { primaryHex: () => '#8f969e', accentHex: () => '#e6b464' });
      assert.ok(view.inspect().draws <= 6, 'all detailed pieces share bounded instanced draws');
      if (id === 'threshold') assert.deepEqual(view.inspect().colours, {
        paint: '#ff91cc', pale: '#fff5fc', accent: '#e60087', metal: '#bfc2d3', dark: '#38213b',
      });
      for (const t of [.5, 3, 9, 13.8]) {
        view.update(t);
        for (const row of track.moving.traps) {
          const root = view.group.getObjectByName(row.id), face = root.getObjectByName(row.kind === 'slider' ? 'sliding-block' : 'crusher-head'), head = face.parent;
          assert.equal(head.position.x, movingPoseAt(track, row, t).x);
          assert.equal(face.scale.x, row.w); assert.equal(face.scale.z, row.len);
          assert.equal(face.material.color.getHexString(), view.inspect().colours[row.kind === 'slider' ? 'pale' : 'paint'].slice(1));
          assert.ok(face.material.colorNode && face.material.roughnessNode, 'machined/enamel finish is visible shader data');
          assert.ok(root.getObjectByName('recessed-fastener') && root.getObjectByName('housing-vent'));
          assert.ok(root.getObjectByName('connected-piston-shaft').scale.x > 0);
        }
        const bridge = track.moving.bridge;
        for (let i = 0; i < bridge.slabs; i++) {
          const slab = view.group.getObjectByName('bridge-piston-' + i).getObjectByName('forming-road').parent;
          assert.equal(Math.abs(slab.position.x) < .00001, bridgePose(bridge, t, i, bridge.slabs).supported);
        }
      }
      view.dispose(); assert.equal(scene.children.length, 0);
    }
  } finally { hooks.deregister(); }
});
