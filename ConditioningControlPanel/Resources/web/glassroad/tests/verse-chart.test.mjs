import { test } from 'node:test';
import assert from 'node:assert/strict';
import { verseChart, mixEnvelope, MAINFRAME_CHART } from '../dev/verseChart.mjs';
import { normalizeChart, energyOf } from '../audio/chart.js';
import { scriptBubbles } from '../sim/words.js';
import { scriptRoad } from '../sim/roadWords.js';
import { musicFrame } from '../render/musicRoadMath.js';
import { courseSongs } from '../track/selection.js';

const raw = () => ({ id: 'fixture', title: 'Timing fixture', durationS: 12, bpm: 96,
  sections: [{ id: 'induction', startS: 0, endS: 12 }],
  lines: [{ role: 'call', startS: 1, endS: 3, text: 'follow the signal' },
    { role: 'answer', startS: 6, endS: 7, text: 'sync' }],
  words: [{ w: 'follow', startS: 1, endS: 1.3, role: 'call' },
    { w: 'the', startS: 1.5, endS: 1.6, role: 'call' },
    { w: 'signal', startS: 2, endS: 2.5, role: 'call' },
    { w: 'sync', startS: 6, endS: 6.4, role: 'answer' }],
  gates: [{ w: 'sync', startS: 6, endS: 6.4, trigger: true }] });

test('verse timing feeds existing floor paint and lyric gate projection without moving words', () => {
  const c = verseChart(raw()), s = normalizeChart(c);
  assert.equal(c.audio, 'fixture_phone.mp3');
  assert.deepEqual(s.words.map(w => w.startS), [1, 1.5, 2, 6]);
  assert.deepEqual(s.words.map(w => w.line), [0, 0, 0, 1]);
  const gates = scriptBubbles(s), paint = scriptRoad(s);
  assert.equal(gates.length, 1);
  assert.equal(gates[0].w, 'sync'); assert.equal(gates[0].row, 5); assert.equal(gates[0].startS, 6);
  assert.ok(paint.some(w => w.text.includes('follow')));
  assert.ok(paint.every(w => !w.text.includes('sync')));
  assert.throws(() => verseChart({ ...raw(), fake: true }));
});

test('non-trigger verse gates retain three-wide rows and unrelated matching words stay on the floor', () => {
  const r = raw(); r.gates[0].trigger = false;
  r.words.unshift({ w: 'sync', startS: .1, endS: .3, role: 'call' });
  const s = normalizeChart(verseChart(r)), gates = scriptBubbles(s);
  assert.equal(gates.length, 1); assert.equal(gates[0].row, 3); assert.equal(gates[0].startS, 6);
  assert.equal(s.words[0].gate, false);
});

test('measured mix envelopes follow signal and silence, and give the music road deterministic frames', () => {
  const pcm = new Float32Array(8000 * 4);
  for (let i = 8000; i < 24000; i++) pcm[i] = .2 * Math.sin(i * 2 * Math.PI * 100 / 8000);
  const measured = mixEnvelope(pcm), s = normalizeChart(verseChart(raw(), measured));
  assert.equal(energyOf(s, 'mix', .5), 0);
  assert.ok(energyOf(s, 'bass', 1.5) > .8);
  assert.equal(energyOf(s, 'mix', 3.5), 0);
  assert.equal(s.energy.vocals.length, 0);
  assert.deepEqual(musicFrame(s, 1.5), musicFrame(s, 1.5));
  assert.ok(musicFrame(s, 1.5).bass > .8);
});

test('Mainframe selects its private mix only on the dev fallback, preserving explicit choices', () => {
  const fallback = ['/private-audio/pink-road/chart.json'];
  assert.deepEqual(courseSongs('mainframe', '', fallback), [MAINFRAME_CHART]);
  assert.deepEqual(courseSongs('threshold', '', fallback), fallback);
  assert.deepEqual(courseSongs('goonworks', '', fallback), []);
  assert.deepEqual(courseSongs('mainframe', '', ['/host-chart.json']), []);
  assert.deepEqual(courseSongs('mainframe', '?chart=0', []), []);
  assert.deepEqual(courseSongs('mainframe', '?chart=/own.json', ['/own.json']), ['/own.json']);
});
