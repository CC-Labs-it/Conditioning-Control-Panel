import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { installForks } from '../track/forks.js';
import { movingStations } from '../track/moving/mainframe.js';
import { industryZones, industryPose } from '../sim/mainframeIndustry.js';
import { scanZones, scanPose } from '../sim/mainframeScan.js';

const source = JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8'));
const track = loadTrack(source);
const { traps, bridge } = movingStations(track);

test('Mainframe rams occupy supported dry lanes with broad permanent bypasses', () => {
  assert.equal(traps.length, 6);
  assert.deepEqual(new Set(traps.map(row => row.kind)), new Set(['piston', 'slider']));
  for (const row of traps) {
    const f = track.frameAt(row.s), q = track.query(...f.p, f.seg);
    assert.ok(q.onRoad && !q.hole && !q.stone, row.id);
    assert.equal(f.surface, 'glass');
    assert.ok(f.n[1] > .94 && Math.abs(f.t[1]) < .08, row.id + ' avoids steep/banked machinery');
    assert.ok(f.w - row.w - row.travelM > 4 + 1.4, row.id + ' leaves a kart-wide bypass');
    assert.ok(!track.aerials.some(a => row.s >= a.rampS0 - 30 && row.s <= a.landingS1 + 30));
    assert.ok(!track.fields.some(a => row.s >= a.fromS - 30 && row.s <= a.toS + 30));
    assert.ok(!track.routes.some(a => row.s >= a.fromS - 30 && row.s <= a.toS + 30));
    for (const pad of track.boosts) assert.ok(Math.abs(pad.s - row.s) > pad.len / 2 + 20, row.id + ' clears speed pads');
    for (const old of [...industryZones(track), ...scanZones(track)])
      assert.ok(Math.abs(old.s - row.s) > old.len / 2 + 25, row.id + ' clears existing traps');
  }
  assert.deepEqual(movingStations({ id: 'keep' }), { traps: [], bridge: null });
});

test('Mainframe piston bridge is shorter than its permanent dry gantry bypass', () => {
  const { fork } = bridge, fromS = track.nodeS[fork.from], toS = track.nodeS[fork.to];
  assert.deepEqual(fork.points[0].p, track.frameAt(fromS).p);
  assert.deepEqual(fork.points.at(-1).p, track.frameAt(toS).p);
  assert.ok(!track.aerials.some(a => fromS <= a.landingS1 && toS >= a.rampS0));
  if (!track.forks?.some(f => f.id === fork.id)) installForks(track, [fork]);
  const branch = track.forks.find(f => f.id === fork.id);
  assert.ok((toS - fromS) / branch.lengthM > 1.1, 'a closed bridge always has a longer usable route');
  assert.ok(bridge.openS > branch.lengthM*(bridge.toU-bridge.fromU)/12+2,
    'the actual moving cut comfortably fits ordinary driving in the unchanged open window');
  const physical = loadTrack(source,{moving:true}).moving.bridge;
  assert.ok(physical.openS > (physical.toM-physical.fromM)/12+2,
    'render-aligned slab endpoints preserve the same safe crossing margin');
  for (let s = fromS; s < toS; s += 3) {
    const f = track.frameAt(s), q = track.query(...f.p, f.seg);
    assert.ok(q.onRoad && !q.hole, 'original gantry road stays supported');
  }
  for (let u = 0; u <= 1; u += .025) {
    const f = branch.frameAt(branch.lengthM * u);
    assert.ok(Math.abs(f.p[1] - 116) < .001 && f.n[1] > .93, 'the branch stays on the dry level');
  }
});

test('Mainframe press impacts fall on whole beats with quick strokes and a real clear window', () => {
  const row = { offsetS: 0, kind: 'piston' }, pose = beat => industryPose(row, 0, { beat, beatS: .625, safe: false });
  for (const beat of [2, 6, 10]) { assert.equal(pose(beat).lift, 0); assert.equal(pose(beat).impactIndex, (beat - 2) / 4); }
  assert.equal(pose(1).warning, true);
  assert.equal(pose(1).active, false);
  assert.ok(pose(1.85).lift > 0 && pose(1.85).lift < 7);
  assert.equal(pose(3).lift, 7);
  const safe = industryPose(row, 0, { beat: -1, beatS: .625, safe: true, rearming: true });
  assert.equal(safe.lift, 7); assert.equal(safe.impactIndex, null);
});

test('Mainframe scan crosses in half a beat and repeats every two beats at any tempo', () => {
  const row = { offset: 0, travel: 3 };
  for (const bpm of [60, 99.02, 142, 193.5]) {
    const at = beat => scanPose(row, beat * 60 / bpm, bpm);
    assert.equal(at(.7).warning, true);
    assert.equal(at(.7).active, false);
    assert.equal(at(1).active, true);
    assert.equal(at(1.5).x, 3);
    assert.equal(at(1.9).active, false);
    assert.equal(at(1.25).effect, 'slow');
    assert.equal(at(3.25).effect, 'boost');
    assert.equal(at(5.25).effect, 'slow');
  }
});
