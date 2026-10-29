import test from 'node:test';
import assert from 'node:assert/strict';
import { lockedChart, lockedChunks, KEEP_CHART } from '../dev/lockedChart.mjs';
import { courseSongs } from '../track/selection.js';
const take = () => ({ fake: false, alignment: { characters: [...'follow me lock'],
  character_start_times_seconds: Array.from({ length: 14 }, (_, i) => i * .1),
  character_end_times_seconds: Array.from({ length: 14 }, (_, i) => i * .1 + .1) } });
test('cached voice chart reproduces the exact slice, opening, gap and closing without generating audio', () => {
  const chart = lockedChart('# title\nfollow me\nlock', [take()], [44100 * 2], { seconds: 4.98, bpm: 128 });
  assert.equal(chart.audio, 'locked-cock_phone.mp3');
  assert.equal(chart.lines.length, 2); assert.equal(chart.words.length, 3);
  assert.equal(chart.lines[0].startS, 1.2);
  assert.ok(Math.abs(chart.lines[1].startS - 2.2) < .001);
  assert.ok(Math.abs(chart.durationS - 4.98) < .001);
  assert.throws(() => lockedChart('follow me\nlock', [{ ...take(), fake: true }], [88200], { seconds: 4.98, bpm: 128 }));
  assert.throws(() => lockedChart('follow me\nlock', [take()], [88200], { seconds: 100, bpm: 128 }));
});
test('private Keep selection preserves explicit chart, disabled music and non-loopback host choices', () => {
  assert.deepEqual(courseSongs('keep', '?chart=' + KEEP_CHART, [KEEP_CHART]), [KEEP_CHART]);
  assert.deepEqual(courseSongs('keep', '?chart=0', []), []);
  assert.deepEqual(courseSongs('keep', '?chart=/mine.json', ['/mine.json']), ['/mine.json']);
  assert.deepEqual(courseSongs('keep', '', ['/other-host.json']), []);
  assert.equal(lockedChunks('# title\n\none\ntwo').length, 1);
});
