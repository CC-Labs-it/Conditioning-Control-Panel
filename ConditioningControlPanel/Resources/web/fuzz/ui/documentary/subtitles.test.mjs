import test from 'node:test';
import assert from 'node:assert/strict';
import { NARRATION_SUBTITLES, alignSubtitlePhrases, subtitleAt, subtitleTiming } from './subtitles.js';
import { NARRATION_SUBTITLES as ESTIMATED } from './narration-subtitles.js';
import { NARRATION_ALIGNMENT } from './narration-alignment.js';
import { projectionTimeline } from './narration-slides.js';
import { pacingTimeline } from './narration-pacing.js';

test('all four ride transcripts use acoustic word timings while other recordings keep honest estimates', () => {
  for (const [id, cues] of Object.entries(NARRATION_SUBTITLES)) {
    assert.deepEqual(cues.map(cue => cue[2]), ESTIMATED[id].map(cue => cue[2]), `${id} preserves the approved wording`);
    assert.equal(subtitleTiming(id), /^r[1234]$/.test(id) ? 'aligned' : 'estimated');
    if (!NARRATION_ALIGNMENT[id]) assert.equal(cues, ESTIMATED[id]);
  }
  const first = NARRATION_SUBTITLES.r1[0][0] * NARRATION_ALIGNMENT.r1.duration;
  assert.equal(first, 2.24, 'the opening throat clear no longer advances the opening caption');
  assert.equal(subtitleAt('r1', 2, NARRATION_ALIGNMENT.r1.duration), '');
  assert.equal(subtitleAt('r1', 2.3, NARRATION_ALIGNMENT.r1.duration), 'This is the story of a person');
});

test('the animal, floorboard and replay slides use actual recorded phrase onsets', () => {
  const duration = NARRATION_ALIGNMENT.r3.duration;
  const slides = projectionTimeline('r3').filter(cue => cue.slot);
  const expected = [4.08, 8.16, 14.72, 18.08, 28.56, 33.04];
  slides.forEach((cue, index) => assert.ok(Math.abs(cue.at * duration - expected[index]) < 1e-6));
  assert.equal(pacingTimeline('r3', duration).find(point => point.z === 301.11).at, 8.16);
  const wakeup = projectionTimeline('r4').filter(cue => cue.slot);
  assert.deepEqual(wakeup.map(cue => cue.projector), ['wakeup', 'conversation']);
  assert.ok(Math.abs(wakeup[0].at * NARRATION_ALIGNMENT.r4.duration - 15.52) < 1e-6);
  assert.ok(Math.abs(wakeup[1].at * NARRATION_ALIGNMENT.r4.duration - 34.72) < 1e-6);
});

test('hyphen tokenization preserves phrase timing, and mismatched transcripts cannot falsely claim alignment', () => {
  const cues = [[0, .5, 'Forty-one tabs,'], [.5, 1, 'one spreadsheet.']];
  const alignment = { duration: 6, words: [
    { word: 'fortyone', start: .8, end: 1.6 }, { word: 'tabs', start: 1.6, end: 2 },
    { word: 'one', start: 3, end: 3.2 }, { word: 'spreadsheet', start: 3.2, end: 4 },
  ] };
  const actual = alignSubtitlePhrases(cues, alignment);
  assert.equal(actual[0][0], .8 / 6); assert.equal(actual[1][0], .5);
  assert.equal(alignSubtitlePhrases([[0, 1, 'Different words.']], alignment), null);
});
