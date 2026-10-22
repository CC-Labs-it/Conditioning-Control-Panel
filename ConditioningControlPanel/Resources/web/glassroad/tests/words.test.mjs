// tests/words.test.mjs - the song's word bubbles (sim/words.js, MUSIC-ROAD-BRIEF.md section 4). Pass 4: only the
// picked impact words ride bubbles (sim/wordPick.js); the mechanics tests below use everyWord (pass 3: every word on
// the road in phrase lines, the Racing Thoughts rule), rows, the re-aim then freeze, pops, streaks and lines,
// misses, the song loop and pause; the pop juice, sound and the HUD count.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack } from './sim-fake.mjs';
import { normalizeChart } from '../audio/chart.js';
import { createWordRun, scriptBubbles, laneShares, hash01, faceOf, everyWord, WORD_LOOK as L } from '../sim/words.js';

const words = (list, extra = {}) => normalizeChart({ bpm: 120, sections: [], ...extra,
  words: list.map(([w, t, o = {}]) => ({ w, startS: t, endS: t + 0.3, bubble: false, ...o })) });
const track = () => fakeTrack({ nodes: 400, spacing: 10, w: 14 });

test('every word rides a bubble (bubble flag or not); words closer than mergeS share one; punctuation comes off', () => {
  const song = words([['hi,', 1], ['Bambi', 1.5], ['in', 3], ['a', 3.05], ['trance.', 3.3], ['the', 4]]);
  const b = scriptBubbles(song, L, everyWord);
  assert.deepEqual(b.map((x) => x.w), ['hi', 'Bambi', 'in a', 'trance', 'the']);
  assert.equal(faceOf('"you\'re,'), "you're");
});

test('pass 4: by default only the picked impact words ride a bubble; road words never show, never count', () => {
  const song = words([['you', 1, { line: 0 }], ['are', 1.3, { line: 0 }], ['so', 1.6, { line: 0 }], ['pink,', 2, { bubble: true, line: 0 }],
    ['the', 8, { line: 1 }], ['good', 10, { line: 2 }], ['girl', 10.3, { line: 2 }], ['and', 14, { line: 3 }], ['deeper', 15, { gate: true, line: 3 }],
    ['now', 15.4, { line: 3 }]], { triggers: [{ startS: 10, kind: 'word', w: 'good girl' }] });
  const b = scriptBubbles(song);
  assert.deepEqual(b.map((x) => [x.w, x.row]), [['pink', 0], ['good girl', 5], ['deeper', 3]]);
  const run = createWordRun(song, track(), { seed: 1 });
  assert.equal(run.total, 3, 'the HUD total is the bubble moments, not the sung words');
  let shown = 0;
  for (let t = 0; t < 17; t += 1 / 30) shown = Math.max(shown, run.update(t, { s: t * 20, lat: 99, speedMps: 20 }).length);
  assert.ok(run.shown === 3 && shown <= 2, 'only the three moments ever show');
  assert.equal(scriptBubbles(song, L, everyWord).length, 9, 'everyWord: the pass 3 script (good girl one row)');
});

test('phrases: a gap, a clause cut or a fifth bubble starts a new line; every bubble of a line shares its lane', () => {
  const t = [1, 1.2, 1.4, 1.6, 1.8, 3, 3.2, 6];
  const song = words(t.map((x, i) => ['w' + i, x]));
  const b = scriptBubbles(song, L, everyWord);
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
  const b = scriptBubbles(song, L, everyWord);
  assert.deepEqual(b.map((x) => [x.w, x.row]), [['you', 0], ['good girl', 5], ['deeper', 3], ['now', 0]]);
  assert.equal(new Set(b.map((x) => x.line)).size, 4);
  const run = createWordRun(song, track(), { seed: 2, pick: everyWord });
  run.update(0, { s: 0, lat: 0, speedMps: 20 });
  const row = run.update(1, { s: 0, lat: 0, speedMps: 20 }).find((v) => v.row === 5);
  assert.equal(row.n, 5);
  assert.ok(row.xs[0] < row.xs[1] && row.xs[4] > row.xs[3]);
  assert.equal(run.total, 4);
});

test('a bubble shows leadS early where the player will be, re-aims on real progress, then only trims, then holds', () => {
  const run = createWordRun(words([['road', 10]]), track(), { seed: 3, pick: everyWord });
  assert.equal(run.update(7.3, { s: 0, lat: 0, speedMps: 20 }).length, 0);
  const v = run.update(7.6, { s: 6, lat: 0, speedMps: 20 })[0];   // 6 m in 0.3 s: 20 m/s of progress
  assert.equal(v.state, 'live');
  assert.ok(Math.abs(v.s - (6 + 20 * 2.4 + L.aimAheadM)) < 1e-6, 'aimed where the player will be at startS, contact lead on top');
  // real progress 30 m/s while speedMps reads 40 (a drift's sideways speed): the aim follows the progress
  let s = 6, t = 7.6 + 1 / 60;
  const step = (to, rate, speedMps, lat = 0) => { for (; t < to - 1e-9; t += 1 / 60) { s += rate / 60; run.update(t, { s, lat, speedMps }); } };
  step(10 - L.freezeS - 0.05, 30, 40, 8);
  assert.ok(Math.abs(run.live[0].s - (s + 30 * (10 - t) + L.aimAheadM)) < 1.5, 'aimed on progress, not on speedMps');
  // inside freezeS a sudden boost only trims it, at most trimMps
  step(10 - L.freezeS + 0.02, 30, 40, 8);
  const held = run.live[0].s;
  step(10 - L.freezeS + 0.12, 45, 45, 8);
  assert.ok(Math.abs(run.live[0].s - held) <= L.trimMps * 0.1 + 0.05, 'a trim, not a jump');
  // inside trimEndS it holds still whatever the kart does
  step(10 - L.trimEndS + 0.01, 45, 45, 8);
  const still = run.live[0].s;
  step(10 - 0.02, 10, 10, 8);
  assert.equal(run.live[0].s, still, 'held still once inside trimEndS');
});

test('sync: a kart driving through pops each bubble on its word, through boosts and brakes', () => {
  const list = []; for (let k = 0; k < 24; k++) list.push(['w' + k, 6 + k * 1.1]);
  const run = createWordRun(words(list), track(), { seed: 2, pick: everyWord });
  const errs = [];
  run.onPop((e) => { const b = run.words.find((x) => x.w === e.w); errs.push(e.tS - b.startS); });
  let s = 0, v = 0, lat = 0;
  for (let i = 0; i < 60 * 36; i++) {
    const t = i / 60;
    const target = t < 3 ? 0 : 30 + (Math.floor(t / 1.7) % 3 === 1 ? 10 : Math.floor(t / 1.7) % 3 === 2 ? -9 : 0);
    v += (target - v) * Math.min(1, 2.5 / 60);
    s += v / 60;
    const live = run.update(t, { s, lat, speedMps: v });
    const next = live.filter((b) => b.state === 'live').sort((a, b) => a.remainS - b.remainS)[0];
    if (next) lat = next.xs[0];
  }
  assert.ok(errs.length >= 20, 'pops ' + errs.length);
  const med = errs.map(Math.abs).sort((a, b) => a - b)[errs.length >> 1];
  assert.ok(med < 0.1, 'median |pop - word| ' + med.toFixed(3) + ' s');
});

test('driving through pops it once and reports the word and its point; bots never touch it', () => {
  const run = createWordRun(words([['road', 3]]), track(), { seed: 1, pick: everyWord });
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
  const run = createWordRun(words([['deeper', 4, { gate: true }]]), track(), { seed: 2, pick: everyWord });
  run.update(2, { s: 0, lat: 0, speedMps: 20 });
  const v = run.update(3.5, { s: 0, lat: 0, speedMps: 20 })[0];
  assert.equal(v.n, 3);
  assert.ok(v.xs[0] < 0 && v.xs[1] === 0 && v.xs[2] > 0);
  run.update(4, { s: v.s, lat: v.xs[2], speedMps: 20 });
  assert.equal(run.pops, 1);
  assert.equal(v.hit, 2);
});

test('passing it by, or the word long over, is a miss with no score; it fades then goes', () => {
  const run = createWordRun(words([['road', 3], ['slow', 8]]), track(), { seed: 1, pick: everyWord });
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
  const run = createWordRun(words([['road', 5]]), track(), { seed: 1, pick: everyWord });
  run.update(3, { s: 0, lat: 0, speedMps: 20 });
  const a = run.update(3.5, { s: 10, lat: 0, speedMps: 20 })[0].s;
  const b = run.update(3.5, { s: 10, lat: 0, speedMps: 60 })[0].s;
  assert.equal(a, b);
});

test('the song looping back starts a fresh pass; pops carry on; reset clears them', () => {
  const run = createWordRun(words([['road', 3]]), track(), { seed: 1, pick: everyWord });
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
  const tr = track(), run = createWordRun(words(Array.from({ length: 20 }, (_, i) => ['w' + i, 2 + i * 1.5])), tr, { seed: 5, pick: everyWord });
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
  const run = createWordRun(words([['a', 3], ['b', 3.2], ['c', 6]]), track(), { seed: 1, pick: everyWord });
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
  const run2 = createWordRun(words([['a', 3], ['b', 3.2]]), track(), { seed: 1, pick: everyWord });
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
  assert.equal(countText(3, 20), '3 / 20 words', 'pass 4: the impact words only');
  assert.equal(countText(0, 1), '0 / 1 word');
  assert.equal(countText(3, 0), '');
});

// ---- the look over time (render/wordBubblesMath.js) ----
import { bubbleLook, fitFontPx, BUBBLE_LOOK as BL } from '../render/wordBubblesMath.js';

test('look: grows in, swells as its word arrives, pops with a ring, a flash only when granted and never under Gentle', () => {
  const v = (o) => ({ state: 'live', inT: 0, outT: -1, remainS: 2, ...o });
  assert.equal(bubbleLook(v({ inT: 0 })).alpha, 0);
  assert.equal(bubbleLook(v({ inT: 5 })).alpha, 1);
  assert.ok(bubbleLook(v({ inT: 5, remainS: 0.1 })).scale > bubbleLook(v({ inT: 5, remainS: 2 })).scale, 'swells as its word nears');
  assert.ok(bubbleLook(v({ inT: 5, remainS: -0.05 })).scale > bubbleLook(v({ inT: 5, remainS: 2 })).scale, 'kicks on the word');
  // the karaoke light: off before the word, on while it is sung, gone after; Gentle rises slower and less
  const sung = (o, g) => bubbleLook(v({ inT: 5, lenS: 0.4, ...o }), { gentle: g }).sung;
  assert.equal(sung({ remainS: 0.01 }), 0);
  assert.ok(sung({ remainS: -0.1 }) > 0.95);
  assert.ok(sung({ remainS: -0.35 }) > 0.95, 'held while the voice sings it');
  assert.equal(sung({ remainS: -(0.4 + BL.sungFadeS + 0.01) }), 0);
  assert.ok(sung({ remainS: -0.1 }, true) < sung({ remainS: -0.1 }) * 0.7, 'Gentle: slower, softer');
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

// ---- pass 4: the lettering inside the bubble and the word rush (render/wordBubblesMath.js, render/wordRush.js) ----
import { letterLayout, asciiWord, bubbleFace, BUBBLE_TEXT as BT } from '../render/wordBubblesMath.js';
import { rushLook, rushTier, rushMs, createWordRush, RUSH } from '../render/wordRush.js';
import { FACES } from '../render/fonts.js';

test('lettering: one face per kind (a trigger keeps its identity), ASCII only, a phrase stacks on two lines when that is bigger', () => {
  for (const row of [0, 3, 5]) assert.ok(FACES[bubbleFace(row).face], 'a real face for row ' + row);
  assert.notEqual(bubbleFace(0).face, bubbleFace(5).face);
  assert.equal(bubbleFace(3).upper, true);
  assert.equal(asciiWord('\u201cyou\u2019re\u201d \u2014 caf\u00e9'), '"you\'re" - caf');
  const measure = (s) => s.length * 55;   // width at 100 px, about 0.55 em a glyph
  const one = letterLayout('pink', measure, 100, 256), two = letterLayout('bambi sleep', measure, 100, 256);
  assert.deepEqual(one.lines, ['pink']);
  assert.deepEqual(two.lines, ['bambi', 'sleep']);
  assert.ok(two.px > letterLayout('bambisleep', measure, 100, 256).px, 'two lines letter it bigger');
  assert.ok(two.px <= Math.round(256 * BT.twoLineMaxPx) && one.px <= Math.round(256 * BT.maxPx));
});

test('a popped bubble lets its word go at once (the word is the rush now); Gentle lets it go slower', () => {
  const p = (outT, gentle) => bubbleLook({ state: 'popped', inT: 3, outT, remainS: 0 }, { gentle }).text;
  assert.equal(bubbleLook({ state: 'live', inT: 3, outT: -1, remainS: 1 }).text, 1);
  assert.equal(p(0), 1);
  assert.equal(p(BL.wordOutS + 0.01), 0, 'gone before the glass swells around the camera');
  assert.ok(p(0.05, true) > p(0.05));
});

test('word rush: 60% to 130% toward the viewer over about a second, a hold, a dissolve with the ghost split; tiers by kind', () => {
  assert.equal(rushTier({ row: 0 }), 'small'); assert.equal(rushTier({ row: 3 }), 'big'); assert.equal(rushTier({ row: 5 }), 'hero');
  assert.ok(RUSH.vh.small < RUSH.vh.big && RUSH.vh.big < RUSH.vh.hero);
  const at = (t, tier = 'small', g = false) => rushLook(t, tier, g);
  assert.equal(at(0).scale, RUSH.from);
  assert.ok(at(RUSH.inMs).alpha === 1, 'there on the pop');
  assert.ok(Math.abs(at(RUSH.zoomMs).scale - RUSH.to) < 1e-6);
  for (let t = 10; t < RUSH.zoomMs; t += 10) assert.ok(at(t).scale > at(t - 10).scale, 'it only ever comes closer');
  assert.equal(at(RUSH.zoomMs + 50).ghost, 0, 'no ghosts before the dissolve');
  assert.ok(at(RUSH.zoomMs + RUSH.holdMs + RUSH.outMs * 0.4).ghost > 0.3);
  assert.ok(at(rushMs()).done && at(rushMs() - 1).alpha < 0.05);
  assert.ok(at(100, 'hero').dx !== 0 && at(100, 'small').dx === 0, 'the hero shakes on arrival');
  // Gentle: slower, softer, smaller, no ghost split, no shake, no lean, the 300 ms rise floor
  assert.ok(rushMs(true) > rushMs());
  for (let t = 0; t <= rushMs(true); t += 25) {
    const l = at(t, 'hero', true);
    assert.ok(l.ghost === 0 && l.dx === 0 && l.dy === 0 && l.rot === 0);
    assert.ok(l.scale <= RUSH.gentleTo * 1.04);
  }
  assert.ok(at(150, 'small', true).alpha < 0.6, 'Gentle rises over the floor, never a step');
});

/** A tiny DOM stand-in: elements with style, attributes and children. */
function fakeDoc() {
  const el = (tag) => ({ tag, style: {}, children: [], attrs: {}, className: '', textContent: '',
    appendChild(c) { this.children.push(c); c.parent = this; return c; }, setAttribute(k, v) { this.attrs[k] = v; },
    remove() { if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1); } });
  const doc = { createElement: el, head: el('head'), body: el('body') };
  return doc;
}

test('word rush: one at a time (a second pop replaces the first), it freezes while paused, a reset clears it, on the bus', () => {
  const doc = fakeDoc(), fns = {};
  const bus = { on(n, fn) { fns[n] = fn; return () => { delete fns[n]; }; } };
  const r = createWordRush({ bus, doc });
  const layer = doc.body.children[0];
  assert.equal(layer.className, 'gr-rush');
  assert.equal(r.active, false);
  fns['word-pop']({ w: 'pink', row: 3 });
  assert.ok(r.active && r.tier === 'big' && r.text === 'PINK');
  assert.equal(layer.style.display, 'block');
  r.update(0.5);
  fns['word-pop']({ w: 'good girl', row: 5 });
  assert.equal(r.text, 'good girl', 'replaced, never stacked');
  assert.equal(layer.children.length, 1, 'one box');
  const box = layer.children[0], before = box.style.transform;
  r.update(0.05, true);
  assert.equal(box.style.transform, before, 'paused: the rush holds still');
  for (let i = 0; i < 40; i++) r.update(0.05);
  assert.equal(r.active, false, 'gone after its run');
  assert.equal(layer.style.display, 'none');
  fns['word-pop']({ w: 'Bambi', row: 0 });
  r.clear();
  assert.equal(r.active, false);
  r.dispose();
  assert.equal(doc.body.children.length, 0);
  assert.equal(fns['word-pop'], undefined);
});
