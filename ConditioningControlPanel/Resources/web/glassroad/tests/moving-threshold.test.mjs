import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { installForks } from '../track/forks.js';
import { movingStations } from '../track/moving/threshold.js';

const source = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const track = loadTrack(source), layout = movingStations(track);
const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));

test('Threshold introduces six alternating horizontal traps on wide supported galleries', () => {
  assert.equal(layout.traps.length, 6);
  assert.equal(layout.traps.filter(t => t.kind === 'piston').length, 3);
  assert.equal(layout.traps.filter(t => t.kind === 'slider').length, 3);
  for (const row of layout.traps) {
    const f = track.frameAt(row.s), q = track.query(...f.p, f.seg);
    assert.ok(q.onRoad && Math.abs(q.height) < .05, row.id);
    assert.ok(f.w >= 24 && f.n[1] > .97 && Math.abs(f.t[1]) <= .16);
    assert.equal(row.periodBeats, 2);
    assert.ok(row.w + 4 < f.w / 2, 'permanent opposite-side bypass');
    assert.ok(!track.boosts.some(b => Math.abs(b.s - row.s) < b.len / 2 + 14));
    assert.ok(!track.aerials.some(a => row.s > a.rampS0 - 35 && row.s < a.landingS1 + 25));
    assert.ok(!track.fields.some(a => row.s > a.fromS - 30 && row.s < a.toS + 30));
  }
});

test('Threshold lens bridge is a shorter optional road with flat joined mouths and its own timer', () => {
  const bridge = layout.bridge;
  assert.ok(bridge);
  const fromS = track.nodeS[bridge.fork.from], toS = track.nodeS[bridge.fork.to];
  assert.ok(!track.aerials.some(a => fromS < a.landingS1 + 25 && toS > a.rampS0 - 25));
  for (let s = fromS; s <= toS; s += 2) {
    const f = track.frameAt(s);
    assert.ok(track.query(...f.p, f.seg).onRoad, 'permanent main-road bypass');
  }
  const subject = loadTrack(source);
  if (!subject.forks?.some(f => f.id === bridge.forkId)) installForks(subject, [bridge.fork]);
  const fork = subject.forks.find(f => f.id === bridge.forkId);
  assert.ok(fork.lengthM < (toS - fromS) * .9, 'main route must be materially longer');
  assert.ok(distance(fork.frameAt(0).p, track.frameAt(fromS).p) < .01);
  assert.ok(distance(fork.frameAt(fork.lengthM).p, track.frameAt(toS).p) < .01);
  assert.ok(bridge.openS > fork.lengthM * (bridge.toU - bridge.fromU) / 12 + 2);
  assert.equal(bridge.periodS, 14);
  assert.equal(bridge.transitionS, .8);
  assert.ok(layout.traps.every(t => Math.abs(t.s - fromS) > 70 && Math.abs(t.s - toS) > 70));
});

test('Threshold layout adapts to wider galleries without mutating course data', () => {
  const wide = loadTrack({ ...source, nodes: source.nodes.map(n => ({ ...n, w: n.w * 1.2 })) });
  assert.equal(movingStations(wide).traps.length, 6);
  assert.deepEqual(movingStations({ id: 'keep' }), { traps: [], bridge: null });
});
