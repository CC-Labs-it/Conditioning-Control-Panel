import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, createState } from '../sim/state.js';
import { createStandings } from '../sim/standings.js';
import { makeFeel, fakeTrack, recorder, DT } from './sim-fake.mjs';

// fake track: L = 40000 m, start line at s 0, checkpoints at 0, 10000, 20000, 30000, 3 laps
function field(n, tr) {
  return Array.from({ length: n }, (_, i) => ({
    i, st: createState(), meta: { name: i ? 'EMI bot' : 'You', isBot: i > 0 },
    fxv: { place: 0 }, finished: false, finishS: 0, bestLapS: 0, progressM: 0, place: 0,
  }));
}
const at = (r, s, lap = 0, cp = 0) => { r.st[S.s] = s; r.st[S.lap] = lap; r.st[S.checkpoint] = cp; };
const RACING = (t = 10) => ({ phase: 'racing', timeS: t });

test('progress: laps x length + distance past the line; the grid sits behind it', () => {
  const tr = fakeTrack(), L = tr.lengthM, st = createStandings(tr, makeFeel(), recorder()), list = field(3, tr);
  at(list[0], L - 14); at(list[1], 500, 0, 0); at(list[2], 15000, 1, 1);
  st.update(list, RACING(), DT);
  assert.ok(Math.abs(list[0].progressM + 14) < 1e-3, 'behind the line is negative');
  assert.equal(list[1].progressM, 500);
  assert.equal(list[2].progressM, L + 15000);
  assert.deepEqual(st.order, [2, 1, 0]);
  assert.deepEqual([st.placeOf(2), st.placeOf(1), st.placeOf(0)], [1, 2, 3]);
  assert.equal(list[2].place, 1); assert.equal(list[2].fxv.place, 1);
  assert.equal(st.gapToLeader(2), 0); assert.equal(st.gapToLeader(1), L + 14500);
});

test('places: hysteresis stops flicker, ties keep their order, events on a real pass', () => {
  const tr = fakeTrack(), rec = recorder(), st = createStandings(tr, makeFeel(), rec), list = field(3, tr);
  at(list[0], 100); at(list[1], 100); at(list[2], 100);
  st.update(list, RACING(), DT);
  assert.deepEqual(st.order, [0, 1, 2], 'ties: index order');
  at(list[2], 100.2);
  st.update(list, RACING(), DT);
  assert.deepEqual(st.order, [0, 1, 2], 'a 0.2 m nose is not a pass');
  assert.equal(rec.all('place-change').length, 0);
  at(list[2], 103);
  st.update(list, RACING(), DT);
  assert.deepEqual(st.order, [2, 0, 1]);
  const pc = rec.all('place-change').map((e) => e.payload);
  assert.ok(pc.some((p) => p.racer === 2 && p.from === 3 && p.to === 1));
  assert.ok(pc.some((p) => p.racer === 0 && p.from === 1 && p.to === 2));
  assert.deepEqual(rec.all('overtake').map((e) => e.payload), [{ racer: 2, passed: 0 }]);
  assert.deepEqual(rec.first('race-standings').payload.order, [2, 0, 1]);
  at(list[0], 110);
  st.update(list, RACING(), DT);
  assert.deepEqual(rec.all('overtake').map((e) => e.payload)[1], { racer: 0, passed: 2 });
});

test('no place events before go', () => {
  const tr = fakeTrack(), rec = recorder(), st = createStandings(tr, makeFeel(), rec), list = field(2, tr);
  at(list[0], 10); at(list[1], 20);
  st.update(list, { phase: 'countdown', timeS: 0 }, DT);
  at(list[0], 30);
  st.update(list, { phase: 'countdown', timeS: 0 }, DT);
  assert.equal(rec.all('place-change').length, 0);
  assert.deepEqual(st.order, [0, 1]);
});

test('finished racers rank by finish time ahead of anyone still driving', () => {
  const tr = fakeTrack(), st = createStandings(tr, makeFeel(), recorder()), list = field(3, tr), L = tr.lengthM;
  at(list[0], 500, 3); at(list[1], 100, 3); at(list[2], 9000, 2, 0);
  list[1].finished = true; list[1].finishS = 120;
  list[2].progressM = 0;
  st.update(list, RACING(130), DT);
  assert.equal(st.order[0], 1, 'home first');
  list[0].finished = true; list[0].finishS = 118;
  st.update(list, RACING(130), DT);
  assert.deepEqual(st.order.slice(0, 2), [0, 1], 'earlier finish ranks higher');
  assert.ok(L > 0);
});

test('result: waits for the field or ai.resultsWaitS, estimates the stragglers, emits once', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder(), st = createStandings(tr, feel, rec), list = field(3, tr), L = tr.lengthM;
  for (const r of list) r.bestLapS = 40;
  at(list[0], 100, 3); list[0].finished = true; list[0].finishS = 130;
  at(list[1], 200, 3); list[1].finished = true; list[1].finishS = 128;
  at(list[2], L - 1000, 2, 3);
  st.update(list, RACING(130), DT);
  assert.equal(st.result, null, 'one still driving');
  const race = { phase: 'finished', timeS: 130 };
  const n = Math.ceil(feel.get('ai.resultsWaitS') / DT) + 2;
  for (let i = 0; i < n && !st.result; i++) st.update(list, race, DT);
  const res = st.result;
  assert.ok(res, 'result after the wait');
  assert.equal(rec.all('race-result').length, 1);
  st.update(list, race, DT);
  assert.equal(rec.all('race-result').length, 1, 'once');
  assert.equal(res.laps, tr.laps);
  assert.deepEqual(res.rows.map((r) => r.i), [1, 0, 2]);
  assert.deepEqual(res.rows.map((r) => r.place), [1, 2, 3]);
  const last = res.rows[2];
  assert.equal(last.finished, false);
  assert.ok(last.timeS > 130, 'estimated after the clock');
  assert.deepEqual(res.me, { place: 2, timeS: 130, bestLapS: 40 });
  assert.equal(res.rows[1].me, true); assert.equal(res.rows[0].isBot, true);
  st.reset();
  assert.equal(st.result, null);
});

test('result: comes at once when everyone is home', () => {
  const tr = fakeTrack(), st = createStandings(tr, makeFeel(), recorder()), list = field(2, tr);
  for (const r of list) { at(r, 10, 3); r.finished = true; }
  list[0].finishS = 100; list[1].finishS = 101;
  st.update(list, { phase: 'finished', timeS: 101 }, DT);
  assert.ok(st.result && st.result.rows.every((r) => r.finished));
});
