import { test } from 'node:test';
import assert from 'node:assert/strict';
import { emptyRecord, cleanRecord } from '../meta/record.js';
import { applyRun } from '../meta/applyRun.js';
import { ACHIEVEMENTS, ACHIEVEMENT_BY_ID, evaluate } from '../meta/achievements.js';
import { UNLOCKS, unlockedSet } from '../meta/unlocks.js';
import { buildRecap, pickHighlights } from '../meta/recap.js';
import { MEDALS } from '../meta/medals.js';
import { COURSES } from '../track/catalog.js';

const sum = (o = {}) => ({
  v: 1, track: 'threshold', mode: 'race', online: false, laps: 3, seed: 1, assist: 'light', gentle: false, finished: true,
  place: 4, of: 8, timeS: 180, bestLapS: 59, lapTimesS: [61, 59, 60], sectorsS: [[20, 21, 20], [19, 20, 20], [20, 20, 20]],
  placeAtLap: [5, 4, 4], ledEveryLap: false, leadS: 0, topSpeedPct: 1.05, distanceM: 4000, driftStarts: 10, driftBoosts: [4, 2, 0],
  slipstreams: 2, pads: 5, airS: 6, longestAirS: 1.5, jumps: 6, tricks: 1, bestTrick: 'good', overtakes: 3, walls: 2, falls: 1,
  rescues: 1, fallsAtS: [300], hitsTaken: 1, itemsUsed: 4, itemsLanded: 2, tetherBreaks: 0, discBankHits: 0, bigSpiralMax: 0,
  traps: { keepGate: 0, keepChain: 0, moving: 0, goonworks: 0, scan: 0, perfume: 0, industry: 0 },
  mirrorsBroken: 0, mirrorStations: 0, wordsCaught: 0, wordsOffered: 0, songGatesPassed: 0, songGatesOffered: 0, cleanLaps: 0,
  winMarginS: 0, lengthM: 1500, at: '2026-10-26T12:00:00.000Z', ...o,
});
const rec = (fn) => { const r = emptyRecord(); if (fn) fn(r); return r; };
const allCourses = (fn) => (r) => { for (const c of COURSES) fn(r.courses[c.id]); };
const clean = { walls: 0, falls: 0, hitsTaken: 0 };

// [id, summary, record] that earns it, then one that does not
const CASES = {
  'licensed': [[sum()], [sum({ finished: false })]],
  'five-rooms': [[sum(), rec(allCourses((c) => { c.finishes = 1; }))], [sum(), rec((r) => { r.courses.keep.finishes = 1; })]],
  'company': [[sum({ online: true })], [sum()]],
  'first-place': [[sum({ place: 1 })], [sum({ place: 1, of: 1 })]],
  'on-the-clock': [[sum({ mode: 'trial', place: 1, of: 1 })], [sum({ mode: 'trial', finished: false })]],
  'long-hold': [[sum({ driftBoosts: [0, 0, 1] })], [sum()]],
  'drift-habit': [[sum(), rec((r) => { r.totals.driftBoosts = 100; })], [sum(), rec((r) => { r.totals.driftBoosts = 99; })]],
  'spotless': [[sum(clean)], [sum({ ...clean, hitsTaken: 1 })]],
  'last-to-first': [[sum({ place: 1, placeAtLap: [8, 3, 1] })], [sum({ place: 2, placeAtLap: [8, 3, 2] })]],
  'wire-to-wire': [[sum({ place: 1, ledEveryLap: true })], [sum({ place: 1 })]],
  'hang-time': [[sum({ longestAirS: 4.2 })], [sum({ longestAirS: 3.9 })]],
  'slipstream-habit': [[sum({ slipstreams: 10 })], [sum({ slipstreams: 9 })]],
  'tricky': [[sum({ tricks: 5 })], [sum({ tricks: 4 })]],
  'clean-sweep': [[sum({ cleanLaps: 3 })], [sum({ laps: 1, cleanLaps: 1 })]],
  'island-hopper': [[sum({ falls: 0 })], [sum({ falls: 0, track: 'keep' })]],
  'gate-etiquette': [[sum({ track: 'keep' })], [sum({ track: 'keep', traps: { ...sum().traps, keepChain: 1 } })]],
  'seven-years': [[sum({ track: 'boudoir', mirrorStations: 3 })], [sum({ track: 'boudoir', mirrorStations: 2 })]],
  'clean-scan': [[sum({ track: 'mainframe' })], [sum({ track: 'mainframe', finished: false })]],
  'windmill-proof': [[sum({ track: 'goonworks' })], [sum({ track: 'goonworks', traps: { ...sum().traps, goonworks: 2 } })]],
  'word-perfect': [[sum({ wordsCaught: 9, wordsOffered: 9 })], [sum({ wordsCaught: 0, wordsOffered: 0 })]],
  'on-the-beat': [[sum({ songGatesPassed: 4, songGatesOffered: 4 })], [sum({ songGatesPassed: 3, songGatesOffered: 4 })]],
  'sing-along': [[sum(), rec((r) => { r.totals.wordsCaught = 500; })], [sum()]],
  'tow-truck': [[sum({ tetherBreaks: 1 })], [sum()]],
  'bank-shot': [[sum({ discBankHits: 1 })], [sum()]],
  'big-catch': [[sum({ bigSpiralMax: 3 })], [sum({ bigSpiralMax: 2 })]],
  'sharpshooter': [[sum(), rec((r) => { r.totals.itemsLanded = 100; })], [sum()]],
  'rescued': [[sum(), rec((r) => { r.totals.rescues = 1; })], [sum()]],
  'deep-end': [[sum(), rec((r) => { r.totals.falls = 100; })], [sum(), rec((r) => { r.totals.falls = 12; })]],
  'gold-leaf': [[sum(), rec(allCourses((c) => { c.medal = 'gold'; }))], [sum(), rec((r) => { allCourses((c) => { c.medal = 'glass'; })(r); r.courses.keep.medal = 'silver'; })]],
  'glass-driver': [[sum(), rec(allCourses((c) => { c.medal = 'glass'; }))], [sum(), rec(allCourses((c) => { c.medal = 'gold'; }))]],
  'regular': [[sum(), rec((r) => { r.totals.races = 100; })], [sum()]],
  'no-hands': [[sum({ place: 1, assist: 'off' })], [sum({ place: 2, assist: 'off' })]],
  'photo-finish': [[sum({ place: 1, winMarginS: 0.15 })], [sum({ place: 1, winMarginS: 0.4 })]],
  'blue-screen': [[sum({ track: 'mainframe', traps: { ...sum().traps, scan: 5 } })], [sum({ track: 'mainframe', traps: { ...sum().traps, scan: 4 } })]],
};

test('achievements: table shape, 3 to 5 hidden, rewards only the three cosmetics', () => {
  assert.ok(ACHIEVEMENTS.length >= 28 && ACHIEVEMENTS.length <= 36, String(ACHIEVEMENTS.length));
  const hidden = ACHIEVEMENTS.filter((a) => a.hidden).length;
  assert.ok(hidden >= 3 && hidden <= 5, String(hidden));
  const ids = new Set(ACHIEVEMENTS.map((a) => a.id)); assert.equal(ids.size, ACHIEVEMENTS.length);
  const rewards = ACHIEVEMENTS.map((a) => a.reward).filter(Boolean).sort();
  assert.deepEqual(rewards, Object.keys(UNLOCKS).sort());
  for (const [id, u] of Object.entries(UNLOCKS)) assert.equal(ACHIEVEMENT_BY_ID[u.by].reward, id);
  for (const a of ACHIEVEMENTS) {
    assert.ok(a.name && a.line, a.id);
    assert.doesNotMatch(a.name + a.line, /[!–—]/, a.id);
  }
});

test('achievements: every rule is true once and false once', () => {
  assert.deepEqual(Object.keys(CASES).sort(), ACHIEVEMENTS.map((a) => a.id).sort());
  for (const a of ACHIEVEMENTS) {
    const [[sy, ry], [sn, rn]] = CASES[a.id];
    assert.equal(!!a.test(sy, ry || rec()), true, a.id + ' should pass');
    assert.equal(!!a.test(sn, rn || rec()), false, a.id + ' should fail');
  }
});

test('achievements: evaluate skips held ids and keeps table order', () => {
  const r = rec((x) => { x.achievements.licensed = '2026-10-01T00:00:00Z'; });
  const ids = evaluate(r, sum({ place: 1, driftBoosts: [0, 0, 2] }));
  assert.ok(!ids.includes('licensed')); assert.ok(ids.includes('first-place')); assert.ok(ids.includes('long-hold'));
  assert.ok(ids.indexOf('first-place') < ids.indexOf('long-hold'));
});

test('applyRun: race bests, counters, recent, never mutates its input', () => {
  const before = rec((r) => { r.courses.threshold.bestRaceS = 190; r.courses.threshold.bestLapS = 58; r.courses.threshold.races = 2; r.totals.races = 2; });
  const frozen = JSON.stringify(before), s = sum({ place: 1 });
  const { record, diff } = applyRun(before, s);
  assert.equal(JSON.stringify(before), frozen);
  const c = record.courses.threshold;
  assert.equal(c.races, 3); assert.equal(c.finishes, 1); assert.equal(c.wins, 1); assert.equal(c.podiums, 1);
  assert.equal(c.bestRaceS, 180); assert.equal(c.bestLapS, 58); assert.deepEqual(c.bestSectorsS, [19, 20, 20]);
  assert.equal(diff.pbRace, true); assert.equal(diff.pbLap, false); assert.equal(diff.medal, null);
  assert.equal(record.totals.races, 3); assert.equal(record.totals.driftBoosts, 6); assert.equal(record.totals.distanceM, 4000);
  assert.equal(record.recent.length, 1); assert.equal(record.recent[0].place, 1);
  assert.ok(diff.newAchievements.includes('licensed')); assert.ok(diff.newAchievements.includes('first-place'));
  assert.equal(record.achievements.licensed, s.at);
  assert.deepEqual(cleanRecord(record), record);
});

test('applyRun: an unfinished run counts a race and sets no bests', () => {
  const { record, diff } = applyRun(emptyRecord(), sum({ finished: false, place: 1, timeS: 10, bestLapS: 5 }));
  const c = record.courses.threshold;
  assert.equal(c.races, 1); assert.equal(c.finishes, 0); assert.equal(c.wins, 0);
  assert.equal(c.bestRaceS, 0); assert.equal(c.bestLapS, 0); assert.deepEqual(c.bestSectorsS, [0, 0, 0]);
  assert.equal(diff.pbRace, false); assert.equal(record.totals.races, 1);
  assert.ok(!diff.newAchievements.includes('licensed'));
});

test('applyRun: trial bests and medals; zero medal times award nothing', () => {
  const s = sum({ mode: 'trial', place: 1, of: 1, timeS: 150, bestLapS: 49 });
  const a = applyRun(emptyRecord(), s);
  const c = a.record.courses.threshold;
  assert.equal(c.trials, 1); assert.equal(c.races, 0); assert.equal(c.trialBestS, 150); assert.equal(c.trialBestLapS, 49);
  assert.equal(c.bestRaceS, 0); assert.equal(a.diff.pbTrial, false);
  const zero = MEDALS.threshold.gold === 0;
  if (zero) assert.equal(a.diff.medal, null);
  const b = applyRun(a.record, { ...s, timeS: 140 });
  assert.equal(b.diff.pbTrial, true); assert.equal(b.record.courses.threshold.trialBestS, 140);
});

test('applyRun: rewards of new achievements become unlocks once', () => {
  const before = rec((r) => { for (const c of COURSES) if (c.id !== 'goonworks') r.courses[c.id].finishes = 1; });
  const { record, diff } = applyRun(before, sum({ track: 'goonworks', place: 1, placeAtLap: [8, 2, 1] }));
  assert.ok(diff.newAchievements.includes('five-rooms')); assert.ok(diff.newAchievements.includes('last-to-first'));
  assert.deepEqual(diff.newUnlocks.sort(), ['hat:umbrella', 'kart:velvet-lock']);
  assert.deepEqual([...unlockedSet(record)].sort(), ['hat:umbrella', 'kart:velvet-lock']);
  const again = applyRun(record, sum({ track: 'goonworks', place: 1, placeAtLap: [8, 2, 1] }));
  assert.deepEqual(again.diff.newUnlocks, []); assert.ok(!again.diff.newAchievements.includes('five-rooms'));
  assert.deepEqual([...unlockedSet({ unlocks: ['kart:nope', 'hat:umbrella'] })], ['hat:umbrella']);
});

test('recap: exactly three string highlights with and without history', () => {
  for (const s of [sum(), sum({ overtakes: 0, airS: 0, itemsLanded: 0, driftBoosts: [0, 0, 0], longestAirS: 0, slipstreams: 0, tricks: 0, leadS: 0, topSpeedPct: 0, distanceM: 0 })]) {
    for (const before of [emptyRecord(), rec((r) => { r.totals.races = 10; r.totals.overtakes = 10; r.totals.airS = 200; r.totals.falls = 20; })]) {
      const h = pickHighlights(s, before);
      assert.equal(h.length, 3);
      for (const x of h) { assert.equal(typeof x.key, 'string'); assert.equal(typeof x.label, 'string'); assert.equal(typeof x.value, 'string'); }
      assert.equal(new Set(h.map((x) => x.key)).size, 3);
    }
  }
  // against history: three times the usual overtakes stands out over an ordinary air time
  const h = pickHighlights(sum({ overtakes: 9, airS: 20, falls: 0 }), rec((r) => { r.totals.races = 10; r.totals.overtakes = 30; r.totals.airS = 200; r.totals.falls = 20; r.totals.driftBoosts = 100; r.totals.itemsLanded = 30; }));
  assert.equal(h[0].key, 'overtakes'); assert.ok(h.some((x) => x.key === 'noFalls'));
});

test('recap: payload carries pb, medal, achievements, unlocks and a sketch', () => {
  const s = sum({ mode: 'trial' });
  const diff = { pbRace: false, pbLap: true, pbTrial: true, medal: { from: null, to: 'gold' }, newAchievements: ['five-rooms', 'nope'], newUnlocks: ['hat:umbrella'] };
  const before = rec((r) => { r.courses.threshold.bestSectorsS = [30, 20, 20]; });
  const r = buildRecap(s, diff, before);
  assert.equal(r.mode, 'trial'); assert.equal(r.highlights.length, 3); assert.equal(r.xp, null);
  assert.deepEqual(r.pb, { race: false, lap: true, trial: true });
  assert.deepEqual(r.medal, { from: null, to: 'gold', times: { ...MEDALS.threshold } });
  assert.deepEqual(r.achievements, [{ id: 'five-rooms', name: 'Five Rooms', line: ACHIEVEMENT_BY_ID['five-rooms'].line }]);
  assert.deepEqual(r.unlocks, [{ id: 'hat:umbrella', label: 'Umbrella' }]);
  assert.deepEqual(r.sketch, { fallsAtS: [300], bestSector: 0, lengthM: 1500 });
  assert.equal(buildRecap(null, diff, before), null);
  const plain = buildRecap(sum({ sectorsS: [] }), null, emptyRecord());
  assert.equal(plain.medal, null); assert.equal(plain.sketch.bestSector, null); assert.deepEqual(plain.achievements, []);
});
