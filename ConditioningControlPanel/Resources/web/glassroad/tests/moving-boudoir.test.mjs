import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { movingStations } from '../track/moving/boudoir.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getBoudoirSetpieces, powderPressPhase, mascaraPhase, mirrorGatePhase, ribbonPhase } from '../track/boudoir-setpieces.js';
import { perfumePhase } from '../sim/boudoirPerfume.js';

const source = JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8'));
const track = loadTrack(source), plan = movingStations(track);
const frame = (beat, bpm = 99.02, extra = {}) => ({ beat, beatS: 60 / bpm, safe: beat < 0, ...extra });

test('Boudoir alternates six sliding and crushing stations on clear supported approach roads', () => {
  assert.equal(plan.traps.length, 6);
  assert.equal(plan.traps.filter(r => r.kind === 'piston').length, 3);
  assert.equal(plan.traps.filter(r => r.kind === 'slider').length, 3);
  const old = getBoudoirSetpieces(track), layout = getBoudoirLayout(track);
  for (const r of plan.traps) {
    const f = track.frameAt(r.s), q = track.query(...f.p, f.seg);
    assert.ok(q.onRoad && Math.abs(q.height) < .05, r.id);
    assert.ok(f.w >= 28 && f.n[1] > .97 && Math.abs(f.t[1]) <= .16, r.id);
    assert.ok(r.w + 4 < f.w / 2);
    assert.equal(r.periodBeats, 2);
    assert.ok(!track.boosts.some(b => Math.abs(b.s - r.s) < b.len / 2 + 14), r.id + ' boost');
    assert.ok(!track.aerials.some(a => r.s > a.rampS0 - 35 && r.s < a.landingS1 + 30), r.id + ' aerial');
    assert.ok(!track.fields.some(a => r.s > a.fromS - 30 && r.s < a.toS + 30), r.id + ' field');
    assert.ok(![...old.presses, ...old.sweepers, ...old.mirrorGates].some(a => Math.abs(a.s - r.s) < 24), r.id + ' old trap');
    assert.ok(![...old.conveyors, ...layout.perfume].some(a => r.s > a.s0 - 18 && r.s < a.s1 + 18), r.id + ' belt/mist');
  }
  assert.deepEqual(movingStations({ id: 'threshold' }), { traps: [], bridge: null });
});

test('Boudoir bridge forms on the shorter dry fork with a permanently supported water bypass', () => {
  const bridge = plan.bridge, fork = track.forks.find(f => f.id === bridge.forkId);
  assert.equal(fork.id, 'bath-dry-bridge');
  assert.ok(fork.lengthM < (fork.toS - fork.fromS) * .9);
  assert.ok(bridge.openS > (bridge.toU - bridge.fromU) * fork.lengthM / 12 + 2);
  assert.equal(bridge.periodS, 14);
  assert.ok(!track.aerials.some(a => fork.fromS < a.landingS1 && fork.toS > a.rampS0));
  for (let s = fork.fromS; s < fork.toS; s += 2) {
    const f = track.frameAt(s);
    assert.ok(track.query(...f.p, f.seg).onRoad, 'permanent bypass at ' + s);
  }
});

test('Boudoir presses strike every two beats and brushes swing across their arc every four', () => {
  const row = { offsetS: 0 };
  assert.equal(powderPressPhase(row, 0, frame(.5)).blocking, false);
  assert.equal(powderPressPhase(row, 0, frame(1)).liftM, 0);
  assert.equal(powderPressPhase(row, 0, frame(3)).liftM, 0);
  assert.equal(powderPressPhase(row, 0, frame(1.6)).blocking, false);
  const brush = { x: 0, armM: 8.6, pivotY: 10.25, amplitudeM: 2.9, offsetS: 0 };
  assert.ok(Math.abs(mascaraPhase(brush, 0, frame(1)).x) < 1e-9);
  assert.ok(Math.abs(mascaraPhase(brush, 0, frame(0)).x - mascaraPhase(brush, 0, frame(4)).x) < 1e-9);
});

test('Boudoir mirrors change lanes each four beats and retain an open lane through every transition', () => {
  for (const bpm of [40, 99.02, 240]) for (let beat = -.01; beat < 40; beat += .03) {
    const poses = [0, 1, 2].map(lane => mirrorGatePhase({ lane, offsetS: 0 }, 0, frame(beat, bpm)));
    assert.ok(poses.some(p => p.liftM === 8.4), 'open mirror lane at ' + beat);
  }
  assert.notEqual(mirrorGatePhase({}, 0, frame(3)).openLane, mirrorGatePhase({}, 0, frame(4)).openLane);
  for (const offsetS of [0, 1.4, -2.2]) {
    const offset = Math.round(offsetS / (60 / 99.02)), release = ((-offset % 4) + 4) % 4;
    for (const lane of [0, 1, 2]) {
      const a = mirrorGatePhase({ lane, offsetS }, 0, frame(release - .00001, 99.02, { rearming: true }));
      const b = mirrorGatePhase({ lane, offsetS }, 0, frame(release + .00001, 99.02, { rearming: true }));
      assert.equal(a.liftM, 8.4); assert.ok(Math.abs(a.liftM - b.liftM) < .001);
    }
  }
});

test('Boudoir ribbons reverse every two beats with continuous arrows and real rest windows', () => {
  assert.equal(ribbonPhase({}, 0, frame(.5)).direction, 1);
  assert.equal(ribbonPhase({}, 0, frame(1.75)).direction, 0);
  assert.equal(ribbonPhase({}, 0, frame(2.5)).direction, -1);
  assert.equal(ribbonPhase({}, 0, frame(3.75)).direction, 0);
  for (const boundary of [1.5, 2, 3.5, 4, 8]) {
    const a = ribbonPhase({}, 0, frame(boundary - .000001)).travelM;
    const b = ribbonPhase({}, 0, frame(boundary + .000001)).travelM;
    assert.ok(Math.abs(a - b) < .001);
  }
  assert.equal(ribbonPhase({}, 0, frame(-1, 99.02, { rearming: true })).direction, 0);
});

test('Boudoir perfume uses short beat phrases with at least 600 ms fades and rearm warning', () => {
  for (const bpm of [40, 99.02, 142, 240]) {
    const bs = 60 / bpm, fadeBeats = Math.max(1, Math.ceil(.6 / bs));
    const activeBeat = 2 + fadeBeats, fadeBeat = activeBeat + 1, period = Math.ceil((fadeBeat + fadeBeats + 1) / 4) * 4;
    assert.ok(period <= 12);
    assert.ok(fadeBeats * bs >= .6);
    assert.equal(perfumePhase({}, 0, {}, frame(1.5, bpm)).phase, 'warn');
    assert.equal(perfumePhase({}, 0, {}, frame(activeBeat, bpm)).active, true);
    assert.equal(perfumePhase({}, 0, {}, frame(activeBeat + period, bpm)).active, true);
    assert.equal(perfumePhase({}, 0, {}, frame(-1, bpm, { rearming: true })).active, false);
    for (const boundary of [2, activeBeat, fadeBeat, fadeBeat + fadeBeats, period]) {
      const a = perfumePhase({}, 0, {}, frame(boundary - .000001, bpm)).mist;
      const b = perfumePhase({}, 0, {}, frame(boundary + .000001, bpm)).mist;
      assert.ok(Math.abs(a - b) < .001);
    }
  }
});
