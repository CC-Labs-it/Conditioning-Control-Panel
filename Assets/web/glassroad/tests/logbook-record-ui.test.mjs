import { test } from 'node:test';
import assert from 'node:assert/strict';
import { emptyRecord, cleanRecord } from '../meta/record.js';
import { COURSES } from '../track/catalog.js';
import { courseRows, totalRows, recentRows, placeLine, achievementStamps, formatDate, formatSpan, TABS } from '../hud/record.js';
import { KARTS, LOCKED, DEFAULT, FREE_KART, COUNTER_LINE, cleanGarage, stepKart, routeTo, lockedKarts, lockLine, ownedKarts, kartGrant } from '../hud/garageState.js';
import { UNLOCKS, unlockedSet } from '../meta/unlocks.js';

const BANNED = /[–—!]/;

test('record ui: dates and spans read plainly', () => {
  assert.equal(formatDate('2026-10-26T12:00:00.000Z'), '26 Oct 2026');
  assert.equal(formatDate('never'), '');
  assert.equal(formatSpan(41.4), '41 s'); assert.equal(formatSpan(725), '12 min 5 s'); assert.equal(formatSpan(120), '2 min');
  assert.equal(formatSpan(8040), '2 h 14 min'); assert.equal(formatSpan(-3), '0 s'); assert.equal(formatSpan('x'), '0 s');
});

test('record ui: a course row shows bests, counts, medal and the trial best only when set', () => {
  const r = emptyRecord();
  Object.assign(r.courses.keep, { races: 4, wins: 1, podiums: 3, bestRaceS: 182.41, bestLapS: 58.9, medal: 'gold', trialBestS: 175.5 });
  const rows = courseRows(r, COURSES);
  assert.equal(rows.length, COURSES.length);
  const keep = rows.find((x) => x.id === 'keep');
  assert.deepEqual([keep.name, keep.medal, keep.bestRace, keep.bestLap, keep.races, keep.wins, keep.podiums, keep.trial],
    ['The Keep', 'Gold', '3:02.41', '0:58.90', '4', '1', '3', '2:55.50']);
  const fresh = rows.find((x) => x.id === 'threshold');
  assert.deepEqual([fresh.medal, fresh.bestRace, fresh.bestLap, fresh.races, fresh.trial], ['-', '-', '-', '0', null]);
});

test('record ui: totals use plain labels and units', () => {
  const r = cleanRecord({ v: 1, totals: { distanceM: 4310, driftBoosts: 28, airS: 725, falls: 2, tetherBreaks: 3, wordsCaught: 14, races: 5, wins: 2 } });
  const rows = Object.fromEntries(totalRows(r).map((x) => [x.label, x.value]));
  assert.deepEqual(rows, { Races: '5', Wins: '2', 'Km driven': '4.3', 'Drift boosts': '28', 'Time in the air': '12 min 5 s', Falls: '2', 'Rivals Tethered': '3', 'Words caught': '14' });
});

test('record ui: recent rows name the course, place of field and time; trials read Trial', () => {
  const r = cleanRecord({ v: 1, recent: [
    { track: 'keep', mode: 'race', place: 2, of: 8, timeS: 182.4, at: '2026-10-26T12:00:00Z' },
    { track: 'boudoir', mode: 'trial', place: 1, of: 1, timeS: 150, at: '2026-10-25T12:00:00Z' },
  ] });
  assert.deepEqual(recentRows(r, COURSES), [
    { course: 'The Keep', place: '2 of 8', time: '3:02.40', date: '26 Oct 2026' },
    { course: 'Boudoir', place: 'Trial', time: '2:30.00', date: '25 Oct 2026' },
  ]);
});

test('record ui: the place line runs oldest to newest with 1st at the top', () => {
  assert.equal(placeLine([{ place: 1, of: 8 }]), null);
  const line = placeLine([{ place: 1, of: 8 }, { place: 8, of: 8 }, { place: 4, of: 8 }], 200, 44, 5);
  // recent is newest first: the 4th place is the oldest race and draws on the left
  assert.deepEqual(line.points, [[5, 19.6], [100, 39], [195, 5]]);
  assert.equal(line.d, 'M5 19.6 L100 39 L195 5');
});

test('record ui: stamps ink earned rows with a date, hidden unearned rows read Hidden', () => {
  const table = [
    { id: 'a', name: 'Open Road', line: 'Finish a race.', kind: 'race', hidden: false, reward: null },
    { id: 'b', name: 'Secret', line: 'Do the thing.', kind: 'race', hidden: true, reward: 'kart:undertow' },
    { id: 'c', name: 'Found It', line: 'Find it.', kind: 'race', hidden: true, reward: 'kart:velvet-lock' },
  ];
  const r = emptyRecord(); r.achievements.c = '2026-10-26T09:00:00Z';
  const [a, b, c] = achievementStamps(r, table);
  assert.deepEqual([a.earned, a.name, a.line, a.date], [false, 'Open Road', 'Finish a race.', '']);
  assert.deepEqual([b.earned, b.hidden, b.name, b.reward], [false, true, 'Hidden', '']);
  assert.ok(!JSON.stringify(b).includes('Secret') && !JSON.stringify(b).includes('thing'));
  assert.deepEqual([c.earned, c.name, c.date, c.reward], [true, 'Found It', '26 Oct 2026', 'Unlocks Velvet Lock']);
});

test('record ui: tab labels and fixed copy carry no dashes or exclamation marks', () => {
  for (const [, label] of TABS) assert.ok(!BANNED.test(label));
  for (const row of totalRows(emptyRecord())) assert.ok(!BANNED.test(row.label));
  for (const kart of LOCKED) assert.ok(!BANNED.test(lockLine(kart) || ''));
});

test('garage locks: the dark set is every body but the free one, minus earned and owned karts', () => {
  assert.deepEqual([...lockedKarts()].sort(), [...LOCKED].sort());
  assert.equal(LOCKED.size, KARTS.length - 1);
  assert.equal(LOCKED.has(FREE_KART), false);
  const r = emptyRecord(); r.unlocks = ['kart:undertow', 'hat:umbrella'];
  const dark = lockedKarts(unlockedSet(r));
  assert.equal(dark.has('undertow'), false);
  assert.equal(dark.size, LOCKED.size - 1);
  assert.equal(LOCKED.has('undertow'), true, 'the catalog list never changes');
  assert.equal(lockedKarts(new Set(['kart:nope', 'junk'])).size, LOCKED.size);
  const bought = lockedKarts(unlockedSet(r), ownedKarts(['cos.kart.body.velvet-lock', 'cos.kart.body.petal']));
  assert.equal(bought.has('velvet-lock') || bought.has('petal') || bought.has('undertow'), false, 'earned or bought, either opens it');
  for (const id of ['kart:velvet-lock', 'kart:undertow', 'hat:umbrella']) assert.ok(UNLOCKS[id] && UNLOCKS[id].label && UNLOCKS[id].by);
});

test('garage locks: an unlocked kart can be saved, stepped onto and routed to', () => {
  const open = lockedKarts(new Set(['kart:undertow'])), undertow = KARTS.findIndex((k) => k[0] === 'undertow');
  const free = KARTS.findIndex((k) => k[0] === FREE_KART);
  assert.equal(cleanGarage({ kart: 'undertow' }).kart, DEFAULT.kart);
  assert.equal(cleanGarage({ kart: 'undertow' }, open).kart, 'undertow');
  assert.equal(cleanGarage({ kart: 'velvet-lock' }, open).kart, DEFAULT.kart);
  assert.equal(routeTo(free, undertow), null);
  assert.deepEqual(routeTo(free, undertow, open), { index: undertow, steps: undertow - free });
  assert.equal(stepKart(free, 1, open).index, undertow, 'every dark kart between is skipped');
  assert.equal(stepKart(free, -1, open).index, undertow);
  const all = lockedKarts(new Set(['kart:undertow', 'kart:velvet-lock']), ownedKarts(KARTS.map(([id]) => kartGrant(id))));
  assert.equal(all.size, 0);
  const seen = new Set(); let index = 0;
  for (let i = 0; i < KARTS.length; i++) { index = stepKart(index, 1, all).index; seen.add(index); }
  assert.equal(seen.size, KARTS.length);
});

test('garage locks: a dark kart points at the Prize Counter, the logbook karts name their achievement too', () => {
  for (const kart of LOCKED) {
    if (Object.values(UNLOCKS).some((u) => u.garage === kart)) assert.match(lockLine(kart), /^Unlocks with .+, or in the Prize Counter$/);
    else assert.equal(lockLine(kart), COUNTER_LINE);
  }
  assert.equal(COUNTER_LINE, 'In the Prize Counter');
  assert.equal(lockLine('lovebomb'), null);
  assert.equal(lockLine('nope'), null);
});
