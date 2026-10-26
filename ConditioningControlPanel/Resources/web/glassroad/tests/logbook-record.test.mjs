import { test } from 'node:test';
import assert from 'node:assert/strict';
import { emptyRecord, cleanRecord, mergeRecords, loadRecord, saveRecord, RECORD_KEY, RECENT_MAX } from '../meta/record.js';
import { medalFor, medalRank, MEDALS } from '../meta/medals.js';

const memory = () => { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), m }; };

test('record: empty record covers every course with zeroed counters', () => {
  const r = emptyRecord();
  assert.deepEqual(Object.keys(r.courses).sort(), Object.keys(MEDALS).sort());
  assert.equal(r.courses.keep.bestRaceS, 0); assert.deepEqual(r.courses.keep.bestSectorsS, [0, 0, 0]);
  assert.equal(r.totals.falls, 0); assert.deepEqual(r.recent, []); assert.deepEqual(r.unlocks, []);
});

test('record: clean drops unknown keys, bad numbers, bad ids and wrong versions', () => {
  assert.deepEqual(cleanRecord({ v: 2, totals: { falls: 9 } }), emptyRecord());
  const r = cleanRecord({ v: 1, junk: 1, courses: { keep: { races: -4, wins: 2.7, bestLapS: 'x', medal: 'platinum' }, moon: { races: 3 } },
    totals: { falls: Infinity, airS: 3.5 }, achievements: { 'long-hold': '2026-10-26T10:00:00Z', 'BAD ID': '2026-10-26T10:00:00Z', x: 'never' },
    unlocks: ['kart:undertow', 'kart:undertow', 7, '<script>'], recent: [{ track: 'keep', at: '2026-10-26T10:00:00Z', place: 3, of: 8, timeS: 200, mode: 'evil', extra: 1 }] });
  assert.equal(r.junk, undefined); assert.equal(r.courses.moon, undefined);
  assert.equal(r.courses.keep.races, 0); assert.equal(r.courses.keep.wins, 2); assert.equal(r.courses.keep.bestLapS, 0); assert.equal(r.courses.keep.medal, null);
  assert.equal(r.totals.falls, 0); assert.equal(r.totals.airS, 3.5);
  assert.deepEqual(Object.keys(r.achievements), ['long-hold']);
  assert.deepEqual(r.unlocks, ['kart:undertow']);
  assert.equal(r.recent.length, 1); assert.equal(r.recent[0].mode, 'race'); assert.equal(r.recent[0].extra, undefined);
});

test('record: merge never lowers and keeps the earliest unlock time', () => {
  const a = emptyRecord(), b = emptyRecord();
  a.courses.threshold.races = 10; b.courses.threshold.races = 4;
  a.courses.threshold.bestRaceS = 190; b.courses.threshold.bestRaceS = 182.4;
  a.courses.threshold.bestLapS = 0; b.courses.threshold.bestLapS = 58.9;
  a.courses.threshold.medal = 'gold'; b.courses.threshold.medal = 'silver';
  a.totals.falls = 3; b.totals.falls = 30;
  a.achievements.licensed = '2026-10-20T00:00:00Z'; b.achievements.licensed = '2026-10-25T00:00:00Z'; b.achievements['long-hold'] = '2026-10-25T00:00:00Z';
  a.unlocks = ['kart:undertow']; b.unlocks = ['hat:umbrella'];
  const m = mergeRecords(a, b);
  assert.equal(m.courses.threshold.races, 10); assert.equal(m.courses.threshold.bestRaceS, 182.4); assert.equal(m.courses.threshold.bestLapS, 58.9);
  assert.equal(m.courses.threshold.medal, 'gold'); assert.equal(m.totals.falls, 30);
  assert.equal(m.achievements.licensed, '2026-10-20T00:00:00Z'); assert.ok(m.achievements['long-hold']);
  assert.deepEqual(m.unlocks, ['hat:umbrella', 'kart:undertow']);
  assert.deepEqual(mergeRecords(m, m), m);
  assert.deepEqual(mergeRecords(a, b), mergeRecords(b, a));
});

test('record: recent merges by time, dedupes and keeps the newest 20', () => {
  const a = emptyRecord(), b = emptyRecord();
  for (let i = 0; i < 15; i++) a.recent.push({ track: 'keep', mode: 'race', place: 1, of: 8, timeS: 100 + i, at: new Date(Date.UTC(2026, 9, 1, 0, i)).toISOString() });
  for (let i = 10; i < 25; i++) b.recent.push({ track: 'keep', mode: 'race', place: 1, of: 8, timeS: 100 + i, at: new Date(Date.UTC(2026, 9, 1, 0, i)).toISOString() });
  const m = mergeRecords(a, b);
  assert.equal(m.recent.length, RECENT_MAX);
  assert.equal(m.recent[0].timeS, 124);
  assert.equal(new Set(m.recent.map((x) => x.at)).size, RECENT_MAX);
});

test('record: storage round trip, and a throwing storage falls back to an empty record', () => {
  const s = memory(), r = emptyRecord();
  r.totals.races = 5;
  assert.equal(saveRecord(r, s), true);
  assert.ok(s.m.has(RECORD_KEY));
  assert.equal(loadRecord(s).totals.races, 5);
  const bad = { getItem() { throw Error('denied'); }, setItem() { throw Error('denied'); } };
  assert.deepEqual(loadRecord(bad), emptyRecord());
  assert.equal(saveRecord(r, bad), false);
  assert.equal(saveRecord(r, null), false);
});

test('medals: unset courses award nothing, ranks order none to glass', () => {
  assert.equal(medalFor('threshold', 10), null);
  assert.equal(medalFor('moon', 10), null);
  assert.equal(medalRank(null), 0); assert.equal(medalRank('glass'), 4); assert.ok(medalRank('gold') > medalRank('silver'));
});
