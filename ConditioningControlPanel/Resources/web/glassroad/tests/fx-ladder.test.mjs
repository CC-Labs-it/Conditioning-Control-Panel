// Polish 5 (J1): the boost camera ladder. Tier in, fov kick and shake budget out.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState, BOOST } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';
import { boostLadder } from '../fx/cues.js';
import { shakeRun } from '../fx/shake.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';
import { createKick, boostPunch } from '../render/cameraMath.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const near = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);
const DT = 1 / 60;

function rig({ gentle = false } = {}) {
  const feel = createFeel(feelJson), bus = createBus();
  const g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const fx = createFx(bus, feel, { gentle: g, photosafe: createPhotosafe(feel) });
  const kart = createState();
  return { feel, bus, fx, kart };
}
const budget = (feel, L) => shakeRun(L.shakeM, L.shakeMul, feel.get('shake.stopBelowM'), feel.get('shake.periodS'));

test('ladder: drift mini, tier 1, 2, 3 -> fov 2 / 4 / 6 / 9, lines climb, shake only from tier 2 and shorter', () => {
  const feel = createFeel(feelJson);
  const L = [0, 1, 2, 3].map((tier) => boostLadder(feel, { kind: 'drift', tier }));
  assert.deepEqual(L.map((l) => l.fovDeg), [2, 4, 6, 9]);
  for (let i = 1; i < 4; i++) assert.ok(L[i].lines > L[i - 1].lines, 'lines by tier');
  near(L[3].lines, 1);
  assert.deepEqual(L.map((l) => l.shakeM > 0), [false, false, true, true]);
  const b2 = budget(feel, L[2]), b3 = budget(feel, L[3]);
  assert.ok(b2.peakM < b3.peakM, 'tier 2 shakes smaller');
  near(b3.peakM, feel.get('shake.boostAmpM'));
  assert.ok(b2.seconds <= b3.seconds && b3.seconds <= 0.45, 'a tier 3 shake is over in ' + b3.seconds);
  const old = shakeRun(feel.get('shake.boostAmpM'), feel.get('shake.repeatMul'), feel.get('shake.stopBelowM'), feel.get('shake.periodS'));
  assert.ok(b3.seconds < old.seconds / 2, `shorter than the old law (${b3.seconds} vs ${old.seconds})`);
  assert.deepEqual([budget(feel, L[0]).lobes, budget(feel, L[1]).lobes], [0, 0]);
});

test('ladder: launch shakes in full, trick fits by grade, Surge is step 2, amount-sized kinds never shake', () => {
  const feel = createFeel(feelJson);
  const launch = boostLadder(feel, { kind: 'launch', tier: feel.get('boost.launchTier') });
  near(launch.fovDeg, 6); near(launch.shakeM, feel.get('shake.boostAmpM'));
  const good = boostLadder(feel, { kind: 'trick', tier: 1 }), great = boostLadder(feel, { kind: 'trick', tier: 2 });
  near(good.fovDeg, 4); near(great.fovDeg, 6);
  assert.ok(good.shakeM === 0 && great.shakeM > 0 && great.shakeM < launch.shakeM);
  const surge = boostLadder(feel, { kind: 'surge', tier: 0, amount: 0.16 });
  near(surge.fovDeg, 6); assert.ok(surge.shakeM > 0);
  const pad = boostLadder(feel, { kind: 'pad', amount: feel.get('pad.amount') });
  const bub = boostLadder(feel, { kind: 'bubble', amount: 0.05 }), slip = boostLadder(feel, { kind: 'slip', amount: 0.1 });
  assert.ok(pad.fovDeg > 6 && pad.fovDeg < 9, 'a pad sits between tier 2 and 3 by its amount ' + pad.fovDeg);
  assert.ok(bub.fovDeg > 0 && bub.fovDeg < 2, 'a bubble under the mini-turbo ' + bub.fovDeg);
  assert.ok(slip.fovDeg > 2 && slip.fovDeg < 4);
  for (const l of [pad, bub, slip]) assert.equal(l.shakeM, 0);
  near(boostLadder(feel, { kind: 'pad' }).fovDeg, pad.fovDeg, 1e-9);
});

test('ladder through the fx: held fov and speed lines per tier, the shake runs its budget then stops', () => {
  for (const tier of [0, 1, 2, 3]) {
    const { feel, bus, fx, kart } = rig();
    const L = boostLadder(feel, { kind: 'drift', tier });
    bus.emit('boost-start', { kind: 'drift', tier });
    let peak = 0, last = -1;
    for (let t = 0; t < 1; t += DT) {
      fx.update(DT, kart);
      const s = Math.hypot(...fx.state.shake);
      peak = Math.max(peak, s);
      if (s > 0) last = t;
    }
    near(fx.state.fovKick, L.fovDeg);
    near(fx.state.speedLines, L.lines);
    const b = budget(feel, L);
    if (!b.lobes) { assert.equal(peak, 0, 'tier ' + tier + ' does not shake'); continue; }
    assert.ok(peak <= b.peakM + 1e-9 && peak > b.peakM * 0.9, `tier ${tier} peak ${peak} vs ${b.peakM}`);
    assert.ok(last < b.seconds + DT, `tier ${tier} stops by ${b.seconds} (last ${last})`);
  }
});

test('ladder: Gentle zeroes the fov kick and the shake on every step and on launch', () => {
  for (const p of [{ kind: 'drift', tier: 3 }, { kind: 'drift', tier: 2 }, { kind: 'launch', tier: 2 }, { kind: 'surge' }]) {
    const { bus, fx, kart } = rig({ gentle: true });
    bus.emit('boost-start', p);
    for (let t = 0; t < 0.5; t += DT) {
      fx.update(DT, kart);
      assert.deepEqual(fx.state.shake, [0, 0, 0]);
    }
    assert.equal(fx.state.fovKick, 0);
  }
});

test('ladder: with the camera snap on the IN the seen fov peak stays near the ladder and keeps its order', () => {
  const feel = createFeel(feelJson), inS = feel.get('fov.kickInS');
  const ss = (x) => (x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x));
  const peaks = [[0, BOOST.DRIFT1], [1, BOOST.DRIFT1], [2, BOOST.DRIFT2], [3, BOOST.DRIFT3]].map(([tier, kind]) => {
    const held = boostLadder(feel, { kind: 'drift', tier }).fovDeg, k = createKick();
    k.hit('boost', boostPunch(kind));
    let pk = 0;
    for (let t = DT; t < 1; t += DT) pk = Math.max(pk, k.update(DT).fov + held * ss(t / inS));
    assert.ok(pk >= held && pk <= held * 1.15, `tier ${tier} peak ${pk} near ${held}`);
    return pk;
  });
  for (let i = 1; i < 4; i++) assert.ok(peaks[i] > peaks[i - 1] + 1.5);
});
