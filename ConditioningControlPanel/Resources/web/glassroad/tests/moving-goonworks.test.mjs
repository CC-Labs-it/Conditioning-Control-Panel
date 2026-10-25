import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { installForks } from '../track/forks.js';
import { movingStations } from '../track/moving/goonworks.js';
import { goonworksSetpieces, goonworksPose, GOONWORKS_TOYS } from '../track/goonworks-setpieces.js';
const source = JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8'));
const track = loadTrack(source), plan = movingStations(track), old = goonworksSetpieces(track);

test('GOONWORKS puts six horizontal traps on supported approaches with permanent opposite bypasses', () => {
  assert.equal(plan.traps.length, 6);
  assert.equal(new Set(plan.traps.map(row => row.kind)).size, 2);
  for (const row of plan.traps) {
    const f = track.frameAt(row.s);
    assert.ok(track.query(...f.p, f.seg).onRoad, row.id);
    assert.ok(f.w >= 28 && f.n[1] >= .97 && Math.abs(f.t[1]) <= .12);
    assert.ok(row.w + 4 < f.w / 2);
    assert.equal(row.periodBeats, 2);
    assert.ok(old.every(other => Math.abs(other.s - row.s) >= (other.depth || 6) / 2 + 20));
    assert.ok(track.boosts.every(other => Math.abs(other.s - row.s) >= other.len / 2 + 16));
    assert.ok(!track.aerials.some(other => row.s > other.rampS0 - 30 && row.s < other.landingS1 + 30));
    assert.ok(!track.fields.some(other => row.s > other.fromS - 30 && row.s < other.toS + 30));
  }
});

test('GOONWORKS bridge cuts the final U-turn while the longer main road stays supported', () => {
  const bridge = plan.bridge;
  if (!track.forks?.some(f => f.id === bridge.forkId)) installForks(track, [bridge.fork]);
  const fork = track.forks.find(row => row.id === bridge.forkId);
  assert.ok(fork);
  assert.ok(fork.lengthM < (fork.toS - fork.fromS) * .9);
  for (let s = fork.fromS; s <= fork.toS; s += 3) {
    const f = track.frameAt(s);
    assert.ok(track.query(...f.p, f.seg).onRoad, 'main bypass ' + s);
  }
  for (const u of [0, 1]) {
    const f = fork.frameAt(fork.lengthM * u), main = track.frameAt(u ? fork.toS : fork.fromS);
    assert.ok(Math.hypot(...f.p.map((v, j) => v - main.p[j])) < .001);
    assert.ok(f.t.reduce((sum, v, j) => sum + v * main.t[j], 0) > .999);
  }
  assert.ok(bridge.openS > fork.lengthM * (bridge.toU - bridge.fromU) / 16 + 1);
  assert.equal(bridge.periodS, 14);
  assert.ok(!track.aerials.some(row => fork.fromS < row.landingS1 && fork.toS > row.rampS0));
});

test('GOONWORKS wheels pass one opening every beat and shutters traverse in four beats', () => {
  const beat = b => ({ beat: b, beatS: 60 / 96.75, safe: b < 0, rearming: false });
  assert.equal(GOONWORKS_TOYS.wheelRadiansPerBeat, Math.PI / 2);
  for (const row of old.filter(row => row.kind === 'wheel')) {
    const a = goonworksPose(row, 0, beat(3)), b = goonworksPose(row, 0, beat(4));
    assert.ok(Math.abs(Math.abs(b.angle - a.angle) - Math.PI / 2) < 1e-9);
  }
  const shutter = { ...old.find(row => row.kind === 'shutter'), offsetS: 0 };
  assert.ok(goonworksPose(shutter, 0, beat(1)).x > shutter.x + shutter.travel - .001);
  assert.ok(goonworksPose(shutter, 0, beat(3)).x < shutter.x - shutter.travel + .001);
});

test('GOONWORKS blocks and flippers warn, strike whole beats and clear inside two beats', () => {
  const beat = b => ({ beat: b, beatS: .5, safe: false, rearming: false });
  for (const kind of ['block', 'flipper']) {
    const row = { ...old.find(row => row.kind === kind), offsetS: 0 };
    for (const b of [.4, .6, .79]) {
      const p = goonworksPose(row, 0, beat(b));
      assert.ok(p.warning && !p.active && p.height === 0);
    }
    assert.ok(goonworksPose(row, 0, beat(.95)).active);
    assert.equal(goonworksPose(row, 0, beat(1)).height, row.height);
    assert.equal(goonworksPose(row, 0, beat(1.6)).height, 0);
    assert.deepEqual(goonworksPose(row, 0, beat(.6)), goonworksPose(row, 0, beat(2.6)));
  }
});
