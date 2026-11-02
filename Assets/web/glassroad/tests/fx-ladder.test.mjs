// Polish 5 (J1): the boost ladder. Feel wave 2026-10-26: the fov kick and boost shake of this ladder are retired
// (the camera never read them); tier in, speed lines out, and a boost never adds trauma.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';
import { boostLadder } from '../fx/cues.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const near = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);
const DT = 1 / 60;

function rig({ gentle = false } = {}) {
  const feel = createFeel(feelJson), bus = createBus();
  const g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const fx = createFx(bus, feel, { gentle: g, photosafe: createPhotosafe(feel) });
  return { feel, bus, fx, kart: createState() };
}

test('ladder: drift mini, tier 1, 2, 3 -> steps 0..3, lines climb to 1, no fov or shake fields', () => {
  const feel = createFeel(feelJson);
  const L = [0, 1, 2, 3].map((tier) => boostLadder(feel, { kind: 'drift', tier }));
  assert.deepEqual(L.map((l) => l.step), [0, 1, 2, 3]);
  for (let i = 1; i < 4; i++) assert.ok(L[i].lines > L[i - 1].lines, 'lines by tier');
  near(L[3].lines, 1);
  for (const l of L) assert.deepEqual(Object.keys(l).sort(), ['lines', 'step']);
});

test('ladder: launch, trick by grade, Surge step 2, amount-sized kinds sit between the steps', () => {
  const feel = createFeel(feelJson);
  near(boostLadder(feel, { kind: 'launch', tier: 2 }).step, 2);
  near(boostLadder(feel, { kind: 'trick', tier: 1 }).step, 1); near(boostLadder(feel, { kind: 'trick', tier: 2 }).step, 2);
  near(boostLadder(feel, { kind: 'surge', tier: 0, amount: 0.16 }).step, 2);
  const pad = boostLadder(feel, { kind: 'pad', amount: feel.get('pad.amount') });
  const bub = boostLadder(feel, { kind: 'bubble', amount: 0.05 }), slip = boostLadder(feel, { kind: 'slip', amount: 0.1 });
  assert.ok(pad.step > 2 && pad.step < 3, 'a pad sits between tier 2 and 3 by its amount ' + pad.step);
  assert.ok(bub.lines > 0 && bub.lines < boostLadder(feel, { kind: 'drift', tier: 0 }).lines, 'a bubble under the mini-turbo');
  assert.ok(slip.step > 0 && slip.step < 1);
  near(boostLadder(feel, { kind: 'pad' }).lines, pad.lines, 1e-9);
});

test('ladder through the fx: held speed lines per tier; a boost never adds trauma, Gentle or not', () => {
  for (const gentle of [false, true]) for (const tier of [0, 1, 2, 3]) {
    const { feel, bus, fx, kart } = rig({ gentle });
    const L = boostLadder(feel, { kind: 'drift', tier });
    bus.emit('boost-start', { kind: 'drift', tier });
    for (let t = 0; t < 1; t += DT) { fx.update(DT, kart); assert.equal(fx.state.trauma, 0); }
    near(fx.state.speedLines, L.lines);
    assert.equal(fx.state.fovKick, 0); assert.equal(fx.state.camPush, 0);
  }
});
