import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { installForks } from '../track/forks.js';
import { movingStations } from '../track/moving/goonworks.js';
import { goonworksSetpieces, goonworksPose, GOONWORKS_TOYS } from '../track/goonworks-setpieces.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';
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

test('GOONWORKS complete head envelopes stay above the real floor during rest and full extension', () => {
  const physical = loadTrack(source, { moving: true }), layout = getGoonworksLayout(physical);
  const first = physical.moving.traps[0], gallery = layout.byId['grotto-gallery'];
  assert.ok(first.s >= gallery.s0 && first.s < gallery.s0 + 12, 'first stroke teaches its timing at the flat gallery entrance');
  assert.ok(physical.boosts.every(other => Math.abs(other.s - first.s) >= other.len / 2 + 125), 'long unboosted approach');
  for (const row of physical.moving.traps) {
    for (const extension of [0, .25, .5, .75, 1]) {
      const centre = row.x - row.side * row.travelM * extension;
      for (const along of [-row.len / 2, 0, row.len / 2]) {
        for (let lateral = -row.w / 2; lateral <= row.w / 2; lateral += row.w / 8) {
          const s = row.s + along, x = centre + lateral;
          assert.ok(physical.canalProfile(s, x, {}).height < .05,
            row.id + ' visible head clears curved floor at extension=' + extension + ' x=' + x);
          const surface = physical.surfaceAt(s, x), q = physical.query(...surface.p, surface.seg);
          assert.ok(q.onRoad && Math.abs(q.height) < .05, row.id + ' head sweep has physical road underneath');
        }
      }
    }
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

test('GOONWORKS wheels pass one opening per machinery pulse and shutters traverse in four pulses', () => {
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

test('GOONWORKS blocks warn, strike whole machinery pulses and clear inside two pulses', () => {
  const beat = b => ({ beat: b, beatS: .5, safe: false, rearming: false });
  for (const kind of ['block']) {
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
