import test from 'node:test';
import assert from 'node:assert/strict';
import { boudoirChart, remixEvents, BOUDOIR_CHART, BOUDOIR_AUDIO } from '../dev/boudoirChart.mjs';
import { courseSongs } from '../track/selection.js';
import { normalizeChart, beatOf, sectionOf } from '../audio/chart.js';
import { chartAudioUrl } from '../audio/music.js';
import { songGateCues } from '../sim/songGates.js';

const beat = { bpm: 99.02, beat0: .309, bar0: .309 };
const words = [{ w: 'Follow', s: 1.82, e: 2.1 }, { w: 'me', s: 2.1, e: 2.4 }, { w: 'Turn', s: 9, e: 9.5 }];
const arrangement = '# hook 1 (4.1-7.2)\n# bridge (8.1-12.3)\n# hook 2 (13.4-16.5)\n# outro (17.1-19)';
const events = remixEvents('t,bus,note\r\n5.2,classic,wh_follow_me\r\n9.4,classic,drop_voice\r\n10,sfx,"effect, with a quote ""inside"""\r\n');

test('Boudoir uses the selected grid and original word timings with exact v5 overlay placements', () => {
  const raw = boudoirChart(beat, words, arrangement, events, 20), chart = normalizeChart(raw);
  assert.equal(chart.bpm, 99.02); assert.equal(chart.downbeatS, .309);
  assert.equal(raw.audio, BOUDOIR_AUDIO); assert.equal(chartAudioUrl(BOUDOIR_CHART, chart), BOUDOIR_AUDIO);
  assert.deepEqual(chart.words.map(w => w.startS), [1.82, 2.1, 9]);
  assert.deepEqual(chart.words.map(w => w.line), [0, 0, 1]);
  assert.ok(Math.abs(beatOf(chart, .309 + 60 / 99.02).phase) < 1e-12);
  assert.deepEqual(songGateCues(chart).map(c => [c.startS, c.text]), [[5.2, 'follow me'], [9.4, 'DROP']]);
  assert.ok(chart.words.every(w => !w.gate && !w.bubble));
});

test('private arrangement supplies distinct hooks and bridge with no gaps in the recording timeline', () => {
  const chart = normalizeChart(boudoirChart(beat, words, arrangement, events, 20));
  for (const s of [4.1, 7.19, 13.4, 16.49]) assert.equal(sectionOf(chart, s).mood, 'peak');
  assert.equal(sectionOf(chart, 8.1).mood, 'break'); assert.equal(sectionOf(chart, 12.29).mood, 'break');
  assert.equal(sectionOf(chart, 19.9).mood, 'outro');
  assert.equal(chart.sections[0].startS, 0); assert.equal(chart.sections.at(-1).endS, 20);
  for (let i = 1; i < chart.sections.length; i++) assert.equal(chart.sections[i - 1].endS, chart.sections[i].startS);
});

test('missing or inconsistent private timing is refused instead of silently drifting', () => {
  for (const bad of [{}, { ...beat, bpm: NaN }, { ...beat, bpm: 0 }, { ...beat, bar0: 1 }])
    assert.throws(() => boudoirChart(bad, words, arrangement, events, 20));
  for (const bad of [[], words.toReversed(), [{ w: 'word', s: 21, e: 22 }]])
    assert.throws(() => boudoirChart(beat, bad, arrangement, events, 20));
  assert.throws(() => boudoirChart(beat, words, arrangement, events, 15));
  assert.throws(() => boudoirChart(beat, words, '', events, 20));
  assert.throws(() => boudoirChart(beat, words, arrangement, [{ startS: 21 }], 20));
  assert.equal(events[2].note, 'effect, with a quote "inside"');
  for (const bad of ['time,bus,note\n1,classic,wh_follow', 't,bus,note\n,classic,wh_follow', 't,bus,note\n1,classic,"open'])
    assert.throws(() => remixEvents(bad));
});

test('Boudoir private default preserves explicit selections and disable and refuses an unrelated host fallback', () => {
  assert.deepEqual(courseSongs('boudoir', '', ['/private-audio/pink-road/chart.json']), [BOUDOIR_CHART]);
  assert.deepEqual(courseSongs('boudoir', '?chart=0', []), []);
  assert.deepEqual(courseSongs('boudoir', '?chart=/mine.json', ['/mine.json']), ['/mine.json']);
  assert.deepEqual(courseSongs('boudoir', '', ['/host.json']), []);
  assert.deepEqual(courseSongs('threshold', '', ['/host.json']), ['/host.json']);
});
