// tests/words.test.mjs - the song's word bubbles (sim/words.js, MUSIC-ROAD-BRIEF.md section 4): thinning,
// seeded lanes, the re-aim then freeze, gate rows, pops, misses, the song loop and pause.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack } from './sim-fake.mjs';
import { normalizeChart } from '../audio/chart.js';
import { createWordRun, pickWords, laneShares, hash01, WORD_LOOK as L } from '../sim/words.js';

const words = (list) => normalizeChart({ bpm: 120, sections: [], words: list.map(([w, t, o = {}]) => ({ w, startS: t, endS: t + 0.3, bubble: true, ...o })) });
const track = () => fakeTrack({ nodes: 400, spacing: 10, w: 14 });

test('thinning keeps one bubble per minGapS, gates down to gateGapS, skips non-bubble words', () => {
  const song = normalizeChart({ bpm: 120, sections: [], words: [
    { w: 'road', startS: 1, endS: 1.3, bubble: true }, { w: 'pink', startS: 1.4, endS: 1.6, bubble: true },
    { w: 'deeper', startS: 1.6, endS: 2, bubble: true, gate: true }, { w: 'the', startS: 3, endS: 3.1, bubble: false },
    { w: 'slow', startS: 2.4, endS: 2.6, bubble: true }, { w: 'down', startS: 2.6, endS: 2.8, bubble: true } ] });
  // slow is 0.8 s after deeper (dropped), down 1.0 s after (kept)
  assert.deepEqual(pickWords(song).map((p) => p.w), ['road', 'deeper', 'down']);
});

test('lanes are deterministic per seed, bounded and differ across seeds', () => {
  const picked = pickWords(words(Array.from({ length: 30 }, (_, i) => ['w' + i, 1 + i * 1.2])));
  const a = laneShares(picked, 7), b = laneShares(picked, 7), c = laneShares(picked, 8);
  assert.deepEqual([...a], [...b]);
  assert.notDeepEqual([...a], [...c]);
  for (const x of a) assert.ok(Math.abs(x) <= L.laneMax + 1e-6);
  for (let i = 1; i < a.length; i++) assert.ok(Math.abs(a[i] - a[i - 1]) <= L.laneShiftPerS * 1.2 + 1e-6);
  assert.ok(hash01(1, 2) >= 0 && hash01(1, 2) < 1);
});

test('a bubble shows leadS early ahead of the player, re-aims, then freezes freezeS before its word', () => {
  const run = createWordRun(words([['road', 10]]), track(), { seed: 3 });
  assert.equal(run.update(7.4, { s: 0, lat: 0, speedMps: 20 }).length, 0);
  const v = run.update(7.6, { s: 0, lat: 0, speedMps: 20 })[0];
  assert.equal(v.state, 'live');
  assert.ok(Math.abs(v.s - 20 * 2.4) < 1e-6, 'aimed where the player will be at startS');
  // the player speeds up: the aim follows (smoothed in song time)
  let s = 0;
  for (let t = 7.6; t < 9.1; t += 0.05) { s += 30 * 0.05; run.update(t, { s, lat: 0, speedMps: 30 }); }
  const frozenAt = run.update(9.25, { s, lat: 0, speedMps: 30 })[0].s;
  run.update(9.6, { s: s + 1, lat: 8, speedMps: 5 });
  assert.equal(run.live[0].s, frozenAt, 'held still once inside freezeS');
  assert.ok(Math.abs(frozenAt - (s + 30 * 0.8)) < 6, 'near where a 30 m/s player is at the word');
});

test('driving through pops it once and reports the word and its point; bots never touch it', () => {
  const run = createWordRun(words([['road', 3]]), track(), { seed: 1 });
  const got = [];
  run.onPop((e) => got.push(e));
  run.update(0.6, { s: 0, lat: 0, speedMps: 20 });
  const v = run.update(2.3, { s: 0, lat: 0, speedMps: 20 })[0];
  const x = v.xs[0];
  run.update(3.0, { s: v.s, lat: x + 1, speedMps: 20 });
  assert.equal(run.pops, 1);
  assert.equal(got.length, 1);
  assert.equal(got[0].w, 'road');
  assert.equal(got[0].p.length, 3);
  assert.equal(run.live[0].state, 'popped');
  run.update(3.1, { s: v.s, lat: x, speedMps: 20 });
  assert.equal(run.pops, 1, 'never twice');
});

test('a gate word is a row of three across the road; any one pops the word', () => {
  const run = createWordRun(words([['deeper', 4, { gate: true }]]), track(), { seed: 2 });
  run.update(2, { s: 0, lat: 0, speedMps: 20 });
  const v = run.update(3.5, { s: 0, lat: 0, speedMps: 20 })[0];
  assert.equal(v.n, 3);
  assert.ok(v.xs[0] < 0 && v.xs[1] === 0 && v.xs[2] > 0);
  run.update(4, { s: v.s, lat: v.xs[2], speedMps: 20 });
  assert.equal(run.pops, 1);
  assert.equal(v.hit, 2);
});

test('passing it by, or the word long over, is a miss with no score; it fades then goes', () => {
  const run = createWordRun(words([['road', 3], ['slow', 8]]), track(), { seed: 1 });
  run.update(1, { s: 0, lat: 0, speedMps: 20 });
  const v = run.update(2.5, { s: 0, lat: 0, speedMps: 20 })[0];
  run.update(3.2, { s: v.s + L.missBehindM + 1, lat: 0, speedMps: 20 });
  assert.equal(run.live[0].state, 'missed');
  assert.equal(run.pops, 0);
  run.update(3.2 + L.keepS + 0.1, { s: v.s + 30, lat: 0, speedMps: 20 });
  assert.equal(run.live.filter((x) => x.w === 'road').length, 0);
  // a parked player: the word ends, the miss still lands
  run.update(6, { s: 500, lat: 0, speedMps: 0 });
  run.update(8 + 0.3 + L.missAfterS + 0.05, { s: 500, lat: 0, speedMps: 0 });
  assert.equal(run.live[0].state, 'missed');
});

test('pause: the same tS moves nothing', () => {
  const run = createWordRun(words([['road', 5]]), track(), { seed: 1 });
  run.update(3, { s: 0, lat: 0, speedMps: 20 });
  const a = run.update(3.5, { s: 10, lat: 0, speedMps: 20 })[0].s;
  const b = run.update(3.5, { s: 10, lat: 0, speedMps: 60 })[0].s;
  assert.equal(a, b);
});

test('the song looping back starts a fresh pass; pops carry on; reset clears them', () => {
  const run = createWordRun(words([['road', 3]]), track(), { seed: 1 });
  run.update(1, { s: 0, lat: 0, speedMps: 20 });
  const v = run.update(2.5, { s: 0, lat: 0, speedMps: 20 })[0];
  run.update(3, { s: v.s, lat: v.xs[0], speedMps: 20 });
  assert.equal(run.pops, 1);
  run.update(200, { s: 900, lat: 0, speedMps: 20 });
  assert.equal(run.update(0.2, { s: 1000, lat: 0, speedMps: 20 }).length, 0);
  assert.equal(run.update(1, { s: 1000, lat: 0, speedMps: 20 })[0].state, 'live', 'the word comes round again');
  assert.equal(run.pops, 1);
  run.reset(9);
  assert.equal(run.pops, 0);
  assert.equal(run.update(1, { s: 0, lat: 0, speedMps: 20 })[0].state, 'live');
});

test('bubbles sit inside the road, clear of the rails, and lap-wrap their s', () => {
  const tr = track(), run = createWordRun(words(Array.from({ length: 20 }, (_, i) => ['w' + i, 2 + i * 1.5])), tr, { seed: 5 });
  for (let t = 0; t < 34; t += 0.1) {
    for (const v of run.update(t, { s: tr.lengthM - 50 + t * 20, lat: 99, speedMps: 20 })) {
      assert.ok(v.s >= 0 && v.s < tr.lengthM);
      for (let k = 0; k < v.n; k++) assert.ok(Math.abs(v.xs[k]) <= 14 / 2 - L.edgeMarginM + 1e-6);
    }
  }
  assert.equal(run.pops, 0, 'lat 99 never pops');
});

// ---- the look over time (render/wordBubblesMath.js) ----
import { bubbleLook, fitFontPx, BUBBLE_LOOK as BL } from '../render/wordBubblesMath.js';

test('look: grows in, swells as its word arrives, pops with a ring, a flash only when granted and never under Gentle', () => {
  const v = (o) => ({ state: 'live', inT: 0, outT: -1, remainS: 2, ...o });
  assert.equal(bubbleLook(v({ inT: 0 })).alpha, 0);
  assert.equal(bubbleLook(v({ inT: 5 })).alpha, 1);
  assert.ok(bubbleLook(v({ inT: 5, remainS: 0 })).scale > bubbleLook(v({ inT: 5, remainS: 2 })).scale);
  const pop = (o) => bubbleLook({ state: 'popped', inT: 3, outT: 0.05, remainS: 0 }, o);
  assert.ok(pop({}).ring > 0);
  assert.equal(pop({}).flash, 0);
  assert.ok(pop({ flashOk: true }).flash > 0);
  assert.equal(pop({ flashOk: true, gentle: true }).flash, 0);
  assert.ok(bubbleLook({ state: 'popped', inT: 3, outT: 1, remainS: 0 }).alpha === 0);
  const miss = bubbleLook({ state: 'missed', inT: 3, outT: BL.missS, remainS: -1 });
  assert.ok(miss.alpha === 0 && miss.lift > 0);
  // Gentle fades slower
  assert.ok(bubbleLook(v({ inT: 0.2 }), { gentle: true }).alpha < bubbleLook(v({ inT: 0.2 })).alpha);
});

test('look: long words get a smaller font, inside the cell bounds', () => {
  assert.ok(fitFontPx('comfortable', 256) < fitFontPx('road', 256));
  assert.ok(fitFontPx('a', 256) <= Math.round(256 * 0.3) && fitFontPx('supercalifragilistic', 256) >= Math.round(256 * 0.12));
});
