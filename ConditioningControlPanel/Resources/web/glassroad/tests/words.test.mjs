// tests/words.test.mjs - the song's word bubbles (sim/words.js, MUSIC-ROAD-BRIEF.md section 4): every word on
// the road in phrase lines (the Racing Thoughts rule), rows, the re-aim then freeze, pops, streaks and lines,
// misses, the song loop and pause; the pop juice, sound and the HUD count.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack } from './sim-fake.mjs';
import { normalizeChart } from '../audio/chart.js';
import { createWordRun, scriptBubbles, laneShares, hash01, faceOf, WORD_LOOK as L } from '../sim/words.js';

const words = (list, extra = {}) => normalizeChart({ bpm: 120, sections: [], ...extra,
  words: list.map(([w, t, o = {}]) => ({ w, startS: t, endS: t + 0.3, bubble: false, ...o })) });
const track = () => fakeTrack({ nodes: 400, spacing: 10, w: 14 });

test('every word rides a bubble (bubble flag or not); words closer than mergeS share one; punctuation comes off', () => {
  const song = words([['hi,', 1], ['Bambi', 1.5], ['in', 3], ['a', 3.05], ['trance.', 3.3], ['the', 4]]);
  const b = scriptBubbles(song);
  assert.deepEqual(b.map((x) => x.w), ['hi', 'Bambi', 'in a', 'trance', 'the']);
  assert.equal(faceOf('"you\'re,'), "you're");
});

test('phrases: a gap, a clause cut or a fifth bubble starts a new line; every bubble of a line shares its lane', () => {
  const t = [1, 1.2, 1.4, 1.6, 1.8, 3, 3.2, 6];
  const song = words(t.map((x, i) => ['w' + i, x]));
  const b = scriptBubbles(song);
  // w0-w3 one line (four max), w4 starts the next (still no gap), w5 after a 0.9 s silence, w7 after 2.5 s
  assert.deepEqual(b.map((x) => x.line), [0, 0, 0, 0, 1, 2, 2, 3]);
  assert.deepEqual(b.map((x) => x.last), [false, false, false, true, true, false, true, true]);
  const xs = laneShares(b, 7);
  assert.equal(xs[0], xs[3]);
  assert.equal(xs[4], xs[3], 'no silence to steer through: the line continues in its lane');
  assert.notEqual(xs[5], xs[4], 'a real silence: the lane moves');
  for (let i = 1; i < xs.length; i++) {
    const li = (x) => L.lanes.findIndex((l) => Math.abs(l - x) < 1e-5), step = Math.abs(li(xs[i]) - li(xs[i - 1]));
    assert.ok(step <= 1, 'one lane at a time');
  }
  assert.deepEqual([...laneShares(b, 7)], [...xs]);
  assert.ok(hash01(1, 2) >= 0 && hash01(1, 2) < 1);
});

test('a trigger phrase is one row of five wearing the phrase; a gate word a row of three; each a line of its own', () => {
  const song = words([['you', 1], ['good', 1.4], ['girl', 1.7], ['deeper', 2.2, { gate: true }], ['now', 2.4]],
    { triggers: [{ startS: 1.42, kind: 'word', w: 'good girl' }] });
  const b = scriptBubbles(song);
  assert.deepEqual(b.map((x) => [x.w, x.row]), [['you', 0], ['good girl', 5], ['deeper', 3], ['now', 0]]);
  assert.equal(new Set(b.map((x) => x.line)).size, 4);
  const run = createWordRun(song, track(), { seed: 2 });
  run.update(0, { s: 0, lat: 0, speedMps: 20 });
  const row = run.update(1, { s: 0, lat: 0, speedMps: 20 }).find((v) => v.row === 5);
  assert.equal(row.n, 5);
  assert.ok(row.xs[0] < row.xs[1] && row.xs[4] > row.xs[3]);
  assert.equal(run.total, 4);
});

test('a bubble shows leadS early ahead of the player, re-aims, then freezes freezeS before its word', () => {
  const run = createWordRun(words([['road', 10]]), track(), { seed: 3 });
  assert.equal(run.update(7.3, { s: 0, lat: 0, speedMps: 20 }).length, 0);
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

test('streak counts clean pops and a miss breaks it; a line taken whole says lineDone on its last bubble', () => {
  // line 0: a b (0.2 s apart); line 1: c after a silence
  const run = createWordRun(words([['a', 3], ['b', 3.2], ['c', 6]]), track(), { seed: 1 });
  const got = [];
  run.onPop((e) => got.push(e));
  let s = 0;
  // drive at 20 m/s down the line's lane and through every bubble
  for (let t = 0.3; t < 7; t += 1 / 60) {
    s += 20 / 60;
    const v = run.live.find((x) => x.state === 'live');
    run.update(t, { s, lat: v ? v.xs[0] : 0, speedMps: 20 });
  }
  assert.deepEqual(got.map((e) => [e.w, e.streak, e.lineDone]), [['a', 1, false], ['b', 2, true], ['c', 3, true]]);
  assert.equal(run.best, 3);
  assert.equal(run.lines, 2);
  // the same song, every bubble missed: no pops, the streak stays at zero
  const run2 = createWordRun(words([['a', 3], ['b', 3.2]]), track(), { seed: 1 });
  for (let t = 0.3; t < 6; t += 1 / 60) run2.update(t, { s: t * 20, lat: 99, speedMps: 20 });
  assert.equal(run2.pops, 0);
  assert.equal(run2.streak, 0);
});

// ---- the pop juice and sound (render/wordBubblesMath.js popJuice, audio/sfxmap.js wordPopPlan) ----
import { popJuice, POP_JUICE as PJ } from '../render/wordBubblesMath.js';
import { wordPopPlan, wordPopRung, sfxPlan } from '../audio/sfxmap.js';

test('pop juice grows with the streak, doubles for a row or a line, flares on the big ones, halves under Gentle', () => {
  const one = popJuice({ streak: 1 }), ten = popJuice({ streak: 9 });
  assert.ok(ten.particles > one.particles && ten.streaks > one.streaks);
  assert.equal(one.flare, false);
  const row = popJuice({ streak: 1, row: 5 }), line = popJuice({ streak: 2, lineDone: true });
  assert.ok(row.flare && line.flare && line.gold);
  assert.ok(row.ringM > one.ringM && line.ringM > row.ringM);
  assert.ok(popJuice({ streak: 10 }).gold);
  const g = popJuice({ streak: 1, row: 3 }, { gentle: true });
  assert.equal(g.flare, false);
  assert.ok(g.particles <= Math.ceil(popJuice({ streak: 1, row: 3 }).particles * PJ.gentleMul));
  assert.ok(popJuice({ streak: 500 }).particles <= PJ.particlesMax);
});

test('the word pop sound: the bubble pop climbing the ladder by streak, a fifth on a row, an octave on a line', () => {
  assert.equal(wordPopRung(1, 8), 0);
  assert.equal(wordPopRung(9, 8), 0, 'wraps to the foot');
  const a = wordPopPlan({ streak: 1 }), b = wordPopPlan({ streak: 3 });
  assert.equal(a.length, 1);
  assert.equal(a[0].id, 'bubble-pop');
  assert.ok(b[0].rate > a[0].rate);
  assert.equal(wordPopPlan({ streak: 1, row: 5 }).length, 2);
  assert.equal(wordPopPlan({ streak: 1, row: 5, lineDone: true }).length, 3);
  assert.deepEqual(sfxPlan('word-pop', { streak: 1 }), a);
});

// ---- the HUD count (hud/words.js) ----
import { countText } from '../hud/words.js';

test('the HUD reads popped out of the script total', () => {
  assert.equal(countText(12, 249), '12 / 249 words');
  assert.equal(countText(0, 1), '0 / 1 word');
  assert.equal(countText(3, 0), '');
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
