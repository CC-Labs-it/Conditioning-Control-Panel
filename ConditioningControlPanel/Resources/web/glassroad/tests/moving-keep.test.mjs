import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { installForks } from '../track/forks.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { movingStations } from '../track/moving/keep.js';
import { keepGatePhase } from '../sim/keepGates.js';
import { keepChainPhase } from '../sim/keepChains.js';
const source = () => JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8'));
const beat = b => ({ beat: b, beatS: 60 / 142, safe: b < 0, rearming: false });

test('Keep horizontal ironwork leaves open deck bypasses and clear approach windows', () => {
  const track = loadTrack(source()), { traps } = movingStations(track), layout = getKeepLayout(track);
  assert.equal(traps.length, 6);
  assert.equal(traps.filter(row => row.kind === 'piston').length, 3);
  for (const row of traps) {
    const frame = track.frameAt(row.s);
    assert.ok(!frame.flags.includes('jump') && frame.n[1] > .99, row.id + ' supported deck');
    assert.ok(frame.w / 2 - Math.abs(row.x) - row.w / 2 >= 1, row.id + ' outside margin');
    assert.ok(frame.w / 2 + Math.abs(row.x) - row.w / 2 >= 4, row.id + ' permanent bypass');
    assert.ok(!track.aerials.some(a => row.s > a.rampS0 - 20 && row.s < a.landingS1 + 20));
    assert.ok(!track.boosts.some(b => Math.abs(row.s - b.s) < b.len / 2 + row.len / 2 + 10));
    assert.ok(![...layout.gates, ...layout.chains].some(g => Math.abs(row.s - g.s) < 30));
  }
});

test('Keep forming bridge cuts the serpentine while the longer main route remains continuous', () => {
  const track = loadTrack(source()), before = track.query, { bridge } = movingStations(track);
  assert.equal(bridge.periodS, 14); assert.equal(bridge.openS, 8);
  assert.deepEqual(bridge.fork.points[0].p, track.frameAt(track.nodeS[bridge.fork.from]).p);
  assert.deepEqual(bridge.fork.points.at(-1).p, track.frameAt(track.nodeS[bridge.fork.to]).p);
  if (!track.forks?.some(fork => fork.id === bridge.forkId)) installForks(track, [bridge.fork]);
  const fork = track.forks.find(fork => fork.id === bridge.forkId);
  assert.ok(fork.lengthM < (fork.toS - fork.fromS) * .9, 'meaningfully shorter branch');
  for (let s = fork.fromS; s <= fork.toS; s += 3) {
    const frame = track.frameAt(s), q = before(...frame.p, frame.seg);
    assert.ok(q.onRoad, 'permanent road support at ' + s);
    assert.ok(!track.aerials.some(a => s >= a.rampS0 && s <= a.landingS1));
  }
  for (const u of [.3, .4, .5, .6, .65]) {
    const frame = fork.frameAt(fork.lengthM * u);
    assert.ok(frame.n[1] > .99 && Math.abs(frame.t[1]) < .02, 'level piston decks');
  }
});

test('Keep portcullises land every two beats and swinging weights cross on alternating beats', () => {
  for (const b of [1, 3, 5, 11]) assert.equal(keepGatePhase({}, 0, {}, beat(b)).liftM, 0);
  for (const b of [.5, 2.5, 4.5]) assert.equal(keepGatePhase({}, 0, {}, beat(b)).phase, 'warn');
  for (const b of [1.6, 3.6, 5.6]) assert.equal(keepGatePhase({}, 0, {}, beat(b)).blocking, false);
  for (const b of [1, 3, 5, 7]) assert.ok(Math.abs(keepChainPhase({}, 0, beat(b)).x) < 1e-10);
  for (const b of [0, .2, 1.1, 3.99]) {
    const a = keepChainPhase({}, 0, beat(b)), c = keepChainPhase({}, 0, beat(b + 4));
    assert.ok(Math.hypot(a.x - c.x, a.y - c.y) < 1e-10);
  }
});
