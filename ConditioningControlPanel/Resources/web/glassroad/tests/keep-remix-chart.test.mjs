import test from 'node:test';
import assert from 'node:assert/strict';
import { keepRemixChart, KEEP_REMIX_CHART, KEEP_REMIX_AUDIO, KEEP_REMIX_SHA256 } from '../dev/keepRemixChart.mjs';
import { KEEP_CHART } from '../dev/lockedChart.mjs';
import { remixEvents } from '../dev/boudoirChart.mjs';
import { normalizeChart, beatOf, barOf, sectionOf } from '../audio/chart.js';
import { chartAudioUrl } from '../audio/music.js';
import { songGateCues } from '../sim/songGates.js';
import { courseSongs } from '../track/selection.js';

// Entirely synthetic timing and text; this fixture reads no private recording or lyric files.
const beat = { bpm: 120, beat0: .25, bar0: .75 };
const words = [
  { w: 'Amber', s: .5, e: .8 }, { w: 'river', s: .8, e: 1.2 },
  { w: 'Cobalt', s: 7, e: 7.5 }, { w: 'cloud', s: 13, e: 13.5 },
];
const arrangement = '# intro (0-2)\n# verse 1 (2-4)\n# chorus 1 (4-6)\n'
  + '# instrumental break 1 (6-8)\n# bridge (8-10)\n# final chorus (10-12)\n# outro (12-14)';
const events = remixEvents('t,bus,note\r\n10,classic,drop_test\r\n4.5,classic,wh_amber_river\r\n'
  + '7,answer,wh_other_words\r\n8,classic,sys_status\r\n9,sfx,"effect, with a quote ""inside"""\r\n');
const make = (overrides = {}) => keepRemixChart(overrides.beat ?? beat, overrides.words ?? words,
  overrides.arrangement ?? arrangement, overrides.events ?? events, overrides.durationS ?? 25, overrides.envelope);
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-10, `${actual} != ${expected}`);

test('Keep v5 preserves distinct measured beat and bar origins through runtime normalization', () => {
  const raw = make(), chart = normalizeChart(raw);
  assert.equal(raw.bpm, 120); assert.equal(raw.downbeatS, .75);
  assert.equal(raw.beats[0], .25); assert.equal(raw.downbeats[0], .75);
  for (const [grid, step] of [[raw.beats, .5], [raw.downbeats, 2]]) {
    assert.ok(grid.length > 10);
    for (let i = 1; i < grid.length; i++) near(grid[i] - grid[i - 1], step);
    assert.ok(grid.every(t => t >= 0 && t < raw.durationS));
  }
  near(beatOf(chart, .25).phase, 0); near(barOf(chart, .25).phase, .75);
  for (const t of [.75, 2.75, 8.75, 22.75]) {
    near(beatOf(chart, t).phase, 0); near(barOf(chart, t).phase, 0);
  }
  near(beatOf(chart, 25.25).phase, 0); near(barOf(chart, 26.75).phase, 0);
  assert.equal(raw.audio, KEEP_REMIX_AUDIO);
  assert.equal(chartAudioUrl(KEEP_REMIX_CHART, chart), KEEP_REMIX_AUDIO);
});

test('Keep v5 builds complete sections including instrumental breaks and the recording tail', () => {
  const raw = make(), chart = normalizeChart(raw);
  for (const [t, mood] of [[0, 'intro'], [2, 'build'], [4, 'peak'], [6, 'break'],
    [8, 'break'], [10, 'peak'], [12, 'outro'], [24.99, 'outro']])
    assert.equal(sectionOf(chart, t).mood, mood, `section at ${t}s`);
  assert.equal(raw.sections[0].startS, 0); assert.equal(raw.sections.at(-1).endS, 25);
  for (const [i, section] of raw.sections.entries()) {
    assert.ok(section.endS > section.startS);
    if (i) assert.equal(raw.sections[i - 1].endS, section.startS);
  }
  const gaps = make({ arrangement: '# verse 1 (2-4)\n# chorus 1 (6-8)' }).sections;
  assert.equal(gaps[0].startS, 0); assert.equal(gaps.at(-1).endS, 25);
  for (let i = 1; i < gaps.length; i++) assert.equal(gaps[i - 1].endS, gaps[i].startS);
});

test('Keep v5 uses recorded event placements and preserves the words in voiced drops', () => {
  const raw = make(), chart = normalizeChart(raw);
  assert.deepEqual(raw.triggers.map(t => [t.kind, t.startS, t.w || '']),
    [['word', 4.5, 'amber river'], ['word', 10, 'test']]);
  assert.deepEqual(songGateCues(chart).map(c => [c.startS, c.text]), [[4.5, 'amber river'], [10, 'test']]);
  assert.deepEqual(raw.words.map(w => [w.w, w.startS, w.endS]), words.map(w => [w.w, w.s, w.e]));
  assert.deepEqual(raw.words.map(w => w.line), [0, 0, 1, 2]);
  assert.ok(raw.words.every(w => !w.gate && !w.bubble));
  assert.deepEqual(make({ events: [] }).triggers, [], 'script headings alone invent no recorded cue');
});

test('Keep v5 rejects missing or inconsistent beat analysis and nonpositive duration', () => {
  for (const bad of [{}, { ...beat, bpm: NaN }, { ...beat, bpm: 0 }, { ...beat, beat0: -.1 },
    { ...beat, bar0: NaN }, { ...beat, bar0: .3 }, { ...beat, bar0: .1 }, { ...beat, bar0: 2.75 }])
    assert.throws(() => make({ beat: bad }));
  for (const durationS of [0, -1, NaN, Infinity]) assert.throws(() => make({ durationS }));
});

test('Keep v5 refuses out-of-recording words and events even on unused buses', () => {
  for (const bad of [[], words.toReversed(), [{ w: '', s: 1, e: 2 }], [{ w: 'test', s: -1, e: 1 }],
    [{ w: 'test', s: 2, e: 1 }], [{ w: 'test', s: 24, e: 26 }], [{ w: 'test', s: NaN, e: 2 }]])
    assert.throws(() => make({ words: bad }));
  for (const startS of [-.01, 25, 26, NaN, Infinity])
    assert.throws(() => make({ events: [{ startS, bus: 'sfx', note: 'unused' }] }));
  assert.doesNotThrow(() => make({ words: [{ w: 'Boundary', s: 24, e: 25 }],
    events: [{ startS: 0, bus: 'classic', note: 'drop_test' }] }));
});

test('Keep v5 refuses absent, reversed, overlapping or overlong arrangement spans', () => {
  for (const bad of ['', '# chorus 1 (4-2)', '# verse 1 (1-5)\n# chorus 1 (4-8)',
    '# verse 1 (1-26)', '# chorus 1 (6-8)\n# verse 1 (2-4)'])
    assert.throws(() => make({ arrangement: bad }));
});

test('Keep v5 defaults only in the private preview and preserves explicit historic charts', () => {
  assert.deepEqual(courseSongs('keep', '', ['/private-audio/pink-road/chart.json']), [KEEP_REMIX_CHART]);
  assert.deepEqual(courseSongs('keep', '?chart=0', []), []);
  assert.deepEqual(courseSongs('keep', '?chart=' + encodeURIComponent(KEEP_CHART), [KEEP_CHART]), [KEEP_CHART]);
  assert.deepEqual(courseSongs('keep', '?chart=/mine.json', ['/mine.json']), ['/mine.json']);
  assert.deepEqual(courseSongs('keep', '', ['/host.json']), []);
  assert.deepEqual(courseSongs('threshold', '', ['/host.json']), ['/host.json']);
});

test('Keep v5 chart content comes from supplied private data, with no bundled lyric fallback', () => {
  const envelope = { energy: { fps: 2, mix: [0, 90, 30] }, hits: { kick: [[.25, .7]] } };
  const alternate = [{ w: 'Synthetic', s: 1, e: 1.3 }, { w: 'fixture', s: 1.3, e: 1.6 }];
  const chart = make({ words: alternate, events: [], envelope });
  assert.deepEqual(chart.words.map(w => w.w), ['Synthetic', 'fixture']);
  assert.deepEqual(chart.lines.map(l => l.text), ['Synthetic fixture']);
  assert.deepEqual(chart.triggers, []); assert.deepEqual(chart.energy, envelope.energy);
  assert.deepEqual(chart.hits, envelope.hits);
  assert.match(KEEP_REMIX_SHA256, /^[a-f0-9]{64}$/);
  assert.equal(KEEP_REMIX_CHART, '/private-audio/keep/race-chart.json');
  assert.equal(KEEP_REMIX_AUDIO, '/private-audio/keep/remix-v5.mp3');
});
