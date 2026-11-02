import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { cueEvents, withPrivateCues } from '../dev/cueFiles.mjs';

test('private source events preserve their exact keys and chronological occurrences', () => {
  assert.deepEqual(cueEvents({ events: [{ kind: 'hit', t: 4 }, { bus: 'fx', note: 'tap', startS: 2 },
    { event: 'hit', startS: 1 }, { kind: 'bad', t: NaN }],
  triggers: [{ kind: 'drop', startS: 3 }], sfx: [{ id: 'bell', startS: 5 }] }), [
    { event: 'hit', startS: 1 }, { event: 'fx:tap', startS: 2 }, { event: 'trigger:drop', startS: 3 },
    { event: 'hit', startS: 4 }, { event: 'sfx:bell', startS: 5 },
  ]);
});

test('private cue files refresh without replacing chart words or guessing unresolved anchors', async t => {
  const root = await mkdtemp(join(tmpdir(), 'glassroad-cues-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(join(root, 'setpieces'));
  const chart = { words: [{ w: 'signal', startS: 2 }], durationS: 10 };
  const file = join(root, 'setpieces', 'threshold.cues.json');
  assert.deepEqual((await withPrivateCues(root, 'threshold', chart)).cues, []);
  const cues = [{ id: 'fixture', kind: 'VEIL', mode: 'CROSS', anchor: { word: 'missing', n: 1 } }];
  await writeFile(file, JSON.stringify({ version: 1, cues }));
  const merged = await withPrivateCues(root, 'threshold', chart);
  assert.deepEqual(merged.cues, cues); assert.equal(merged.words, chart.words);
  assert.equal(merged.cues[0].startS, undefined); assert.equal(chart.cues, undefined);
  await writeFile(file, JSON.stringify({ version: 1, cues: [] }));
  assert.deepEqual((await withPrivateCues(root, 'threshold', chart)).cues, []);
  await writeFile(file, JSON.stringify({ version: 2, cues: [] }));
  await assert.rejects(withPrivateCues(root, 'threshold', chart), /Invalid private cue/);
  await assert.rejects(withPrivateCues(root, '../escape', chart), /Unsupported cue course/);
});
