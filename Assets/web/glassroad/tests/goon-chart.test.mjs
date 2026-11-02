import test from 'node:test';
import assert from 'node:assert/strict';
import { goonChart, GOON_CHART, GOON_AUDIO } from '../dev/goonChart.mjs';
import { courseSongs } from '../track/selection.js';
import { normalizeChart, beatOf } from '../audio/chart.js';
import { songGateCues } from '../sim/songGates.js';

const beat = { bpm: 193.5, bar0: .22 };
const words = [{ w: 'Follow', s: 2.5, e: 2.7 }, { w: 'me', s: 2.7, e: 3.1 },
  { w: 'Turn', s: 9, e: 9.5 }, { w: 'again', s: 9.5, e: 10 }];
const arrangement = 'BPM = 96.75; BAR0 = 0.22\nclassic("wh_follow_me", 4.0)\nswell(12.4, BAR)\n# swell(7.0, BAR)';
test('GOONWORKS keeps verified halftime grid and exact existing word starts', () => {
  const raw = goonChart(beat, words, arrangement, 187.6), chart = normalizeChart(raw);
  assert.equal(raw.durationS, 187.6); assert.equal(raw.audio, GOON_AUDIO);
  assert.equal(chart.bpm, 96.75); assert.equal(chart.downbeatS, .22);
  assert.deepEqual(chart.words.map(w => w.startS), [2.5, 2.7, 9, 9.5]);
  assert.deepEqual(chart.words.map(w => w.line), [0, 0, 1, 1]);
  assert.ok(Math.abs(beatOf(chart, .22 + 60 / 96.75).phase) < 1e-12);
  assert.deepEqual(songGateCues(chart).map(c => [c.startS, c.text]), [[2.5, 'Follow me'], [12.4, 'DROP']]);
  assert.ok(chart.words.every(w => !w.gate && !w.bubble));
});
test('private alignment and corroborating beat data fail closed when inconsistent', () => {
  assert.throws(() => goonChart({ ...beat, bpm: 96 }, words, arrangement, 20));
  assert.throws(() => goonChart({ ...beat, bar0: 1 }, words, arrangement, 20));
  assert.throws(() => goonChart({}, words, arrangement, 20));
  assert.throws(() => goonChart(beat, words, arrangement, 9));
  assert.throws(() => goonChart(beat, words.toReversed(), arrangement, 20));
  assert.throws(() => goonChart(beat, [], arrangement, 20));
});
test('GOONWORKS private fallback preserves explicit music and disable choices', () => {
  assert.deepEqual(courseSongs('goonworks', '', ['/private-audio/pink-road/chart.json']), [GOON_CHART]);
  assert.deepEqual(courseSongs('goonworks', '?chart=0', []), []);
  assert.deepEqual(courseSongs('goonworks', '?chart=/mine.json', ['/mine.json']), ['/mine.json']);
  assert.deepEqual(courseSongs('goonworks', '', ['/host.json']), []);
});
