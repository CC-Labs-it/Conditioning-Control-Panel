import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeChart } from '../audio/chart.js';
import { createWaterDnaFrame, waterDnaFrame } from '../render/waterDnaMath.js';

const song = normalizeChart({ bpm: 100, downbeatS: .3, durationS: 120 });
const bands = { bass: .4, mid: .33, treble: .28, note0: .2, note1: .1, note4: .3, note6: .1 };
const signal = time => ({ active: true, songS: time });
const at = (time, values = bands, gentle = false) => waterDnaFrame(createWaterDnaFrame(), values, signal(time), song, { gentle });

test('water strands visibly travel and breathe on the real beat grid at ordinary music levels', () => {
  const a = at(.3), b = at(.9), c = at(1.5);
  assert.ok(Math.abs(b.phase - a.phase - .9) < 1e-9, 'one heard beat travels nearly three water metres');
  assert.ok(Math.abs(c.phase - a.phase - 1.8) < 1e-9);
  assert.ok(b.radius - a.radius > .27, 'bass visibly opens the paired strands');
  assert.ok(Math.abs(c.radius - a.radius) < 1e-9, 'two-beat breathing returns on the grid');
  assert.ok(Math.abs(at(5.1).hue - a.hue) > .25, 'phrases change strand colours');
});

test('water strand colour and shape respond independently to measured low and melodic bands', () => {
  const bass = at(4.5, { ...bands, bass: .8, mid: 0, treble: 0, note1: 0, note4: 0 });
  const melody = at(4.5, { ...bands, bass: 0, mid: .6, treble: .5, note1: .8, note4: .7 });
  assert.ok(Math.abs(bass.hue - melody.hue) > .1);
  assert.ok(Math.abs(bass.radius - melody.radius) > .2);
  assert.notEqual(bass.pulse, melody.pulse);
});

test('pause, audio interruption and silence hold every DNA coordinate; seeks and reset follow the recording', () => {
  const state = at(7.5), held = structuredClone(state);
  for (const [s, options] of [[signal(70), { paused: true }], [{ ...signal(70), frozen: true }, {}],
    [{ active: false, songS: 70 }, {}], [null, {}]]) {
    assert.equal(waterDnaFrame(state, bands, s, song, options), state);
    assert.deepEqual(state, held);
  }
  waterDnaFrame(state, bands, signal(1.5), song);
  assert.deepEqual(state, at(1.5));
  waterDnaFrame(state, bands, signal(.3), song);
  assert.deepEqual(state, at(.3));
});

test('Gentle reduces travel and breathing, all responses remain bounded, and picked recordings use heard seconds', () => {
  for (let t = .3; t < 30; t += .19) {
    const normal = at(t), soft = at(t, bands, true);
    assert.ok(soft.phase <= normal.phase + 1e-9);
    assert.ok(soft.radius >= 2.1 && soft.radius <= 2.61);
    assert.ok(soft.pulse <= normal.pulse);
    assert.ok(normal.hue >= 0 && normal.hue <= 1 && normal.radius <= 3.55);
  }
  const raw = waterDnaFrame(createWaterDnaFrame(), bands, signal(5), null);
  assert.equal(raw.phase, 4.5);
  assert.ok(Number.isFinite(raw.radius) && Number.isFinite(raw.hue));
});
