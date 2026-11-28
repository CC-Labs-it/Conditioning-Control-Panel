import test from 'node:test';
import assert from 'node:assert/strict';
import { createRhythm, takeBeat, clampBpm, weightY, bpmAtWeight, beatPeriod, createPulse, advancePulse, retimePulse } from '../ui/unlocks/routes/metronome-model.js';

test('lever and main button cannot collect the same beat twice', () => {
  const state = createRhythm();
  assert.equal(takeBeat(state, 1000, 1000), 1);
  assert.equal(takeBeat(state, 1000, 1000), 0);
  assert.equal(takeBeat(state, 1130, 1000), 0);
  assert.equal(state.streak, 1);
  assert.equal(takeBeat(state, 1870, 1000), 2);
  assert.equal(takeBeat(state, 2000, 2000), 0);
  assert.equal(state.streak, 2);
});

test('valid edge-to-edge beats earn bounded streaks and misses reset them', () => {
  const state = createRhythm();
  assert.equal(takeBeat(state, 1130, 1000), 1);
  assert.equal(takeBeat(state, 1870, 1000), 2);
  assert.equal(takeBeat(state, 3000, 3000), 3);
  assert.equal(takeBeat(state, 4000, 4000), 4);
  assert.equal(takeBeat(state, 5000, 5000), 4);
  assert.equal(takeBeat(state, 5500, 5000), 0);
  assert.equal(takeBeat(state, 6000, 6000), 1);
  assert.equal(takeBeat(state, NaN, 6000), 0);
});

test('weight position follows a real metronome: up is slower, down faster, bounded and reversible', () => {
  assert.equal(clampBpm(undefined), 60); assert.equal(clampBpm(NaN), 60);
  assert.equal(bpmAtWeight(-100), 40); assert.equal(bpmAtWeight(1000), 208);
  for (const bpm of [40, 60, 80, 100, 120, 160, 208]) assert.equal(bpmAtWeight(weightY(bpm)), bpm);
  assert.ok(weightY(40) < weightY(208));
  assert.equal(beatPeriod(120), 500); assert.equal(beatPeriod(40), 1500);
});

test('changing tempo while held freezes the swing, then resumes smoothly at the new cadence', () => {
  const p = createPulse(60, 0);
  assert.equal(advancePulse(p, 250), false); assert.equal(p.phase, .25);
  p.held = true; retimePulse(p, 120, 3000);
  assert.equal(advancePulse(p, 5000), false); assert.equal(p.phase, .25); assert.equal(p.side, 1);
  retimePulse(p, p.bpm, 5000); p.held = false;
  assert.equal(advancePulse(p, 5374), false);
  assert.equal(advancePulse(p, 5375), true); assert.equal(p.side, 0); assert.equal(p.phase, 0);
  assert.equal(advancePulse(p, 5875), true); assert.equal(p.side, 1);
  assert.equal(advancePulse(p, 7375), true); assert.equal(p.lastTick, 7375, 'late frames keep the beat grid');
});

test('slow and fast rhythms pay once per beat, including early and late presses', () => {
  for (const bpm of [40, 120, 208]) {
    const period = beatPeriod(bpm), window = Math.min(130, period * .2), s = createRhythm();
    assert.equal(takeBeat(s, period - window + .01, 0, period), 1);
    assert.equal(takeBeat(s, period + window - .01, period, period), 0, 'same beat cannot pay twice');
    assert.equal(takeBeat(s, period * 2, period * 2, period), 2);
    assert.equal(takeBeat(s, period * 2.5, period * 2, period), 0);
  }
});
