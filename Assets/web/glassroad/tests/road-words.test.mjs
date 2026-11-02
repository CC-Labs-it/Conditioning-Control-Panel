// sim/roadWords.js + render/roadWordsMath.js: the ROAD words painted on the track like road markings (pass 4).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack } from './sim-fake.mjs';
import { normalizeChart } from '../audio/chart.js';
import { ROAD, BUBBLE } from '../sim/wordPick.js';
import { scriptRoad, createRoadRun, markText, ROAD_LOOK as L } from '../sim/roadWords.js';
import { STYLE, STYLES, phraseStyle, paintText, packAtlas, markSung, markLook, frameLook, ROAD_WORD_LOOK as RL } from '../render/roadWordsMath.js';
import { createMusicFrame } from '../render/musicRoadMath.js';

const track = () => fakeTrack({ nodes: 400, spacing: 10, w: 14 });
// a chart: [word, startS, line?, extra?]; every word is a road word unless the pick says otherwise
const chart = (list, extra = {}) => normalizeChart({ bpm: 120, sections: [{ id: 's1', startS: 0, endS: 999, mood: 'groove', loop: 0.5 }],
  words: list.map(([w, s, line = 0, x = {}]) => ({ w, startS: s, endS: s + 0.2, line, ...x })), ...extra });
const allRoad = (song) => new Uint8Array(song.words.length);

/** Drives a run at a steady speed and returns, per marking, the views seen. */
function drive(song, { speed = 32, lat = 0, kinds = null, until = 40 } = {}) {
  const tr = track(), sc = scriptRoad(song, L, kinds || allRoad(song)), run = createRoadRun(song, tr, { script: sc });
  const seen = new Map();
  for (let t = 0, s = 0; t < until; t += 1 / 60) {
    s += speed / 60;
    for (const v of run.update(t, { s, lat, speedMps: speed })) {
      if (!seen.has(v.id)) seen.set(v.id, []);
      seen.get(v.id).push({ t, kartS: s, s: v.s, x: v.x, state: v.state, frozen: v.frozen, dS: v.dS, remainS: v.remainS });
    }
  }
  return { sc, run, seen, tr };
}

test('marking text is ASCII, straight quotes, no clause punctuation', () => {
  assert.equal(markText('“hello,'), 'hello');
  assert.equal(markText('don’t.'), "don't");
  assert.equal(markText('café!'), 'caf');
  assert.equal(markText('...'), '');
});

test('the script merges close words, splits phrases on lines and gaps, and keeps bubble moments clear', () => {
  const song = chart([['to', 1], ['the', 1.08], ['road', 1.5], ['again', 3], ['pink', 5, 1], ['near', 5.1, 1], ['far', 6, 1]]);
  const kinds = allRoad(song); kinds[4] = BUBBLE;
  const sc = scriptRoad(song, L, kinds);
  assert.deepEqual(sc.map((e) => e.text), ['to the', 'road', 'again', 'far']);   // 'near' is inside the bubble's clear time
  assert.deepEqual(sc.map((e) => e.phrase), [0, 0, 1, 2]);
  assert.ok(sc[0].first && !sc[0].last && sc[1].last);
  assert.equal(sc.bubbles.length, 1);
});

test('each marking lands where the kart is when its word is sung', () => {
  const list = [];
  for (let i = 0; i < 30; i++) list.push(['word' + (i % 7), 4 + i * 0.9, Math.floor(i / 4)]);
  const { seen } = drive(chart(list), { speed: 30 });
  const err = [];
  for (const views of seen.values()) {
    const at = views.find((v) => v.remainS <= 0);
    if (at) err.push(at.dS - L.aheadM);
  }
  err.sort((a, b) => a - b);
  assert.ok(err.length >= 28);
  assert.ok(Math.abs(err[err.length >> 1]) < 1.2, 'median aim error ' + err[err.length >> 1]);
});

test('a marking never moves once it is readable, and markings never overlap', () => {
  const list = [];
  for (let i = 0; i < 60; i++) list.push(['w' + i, 3 + i * 0.12, Math.floor(i / 6)]);   // faster than the road can hold
  const { seen } = drive(chart(list), { speed: 34, until: 20 });
  for (const views of seen.values()) {
    const fz = views.filter((v) => v.frozen);
    for (let i = 1; i < fz.length; i++) assert.equal(fz[i].s, fz[0].s);
    for (const v of views) if (!v.frozen && v.state === 'live') assert.ok(v.dS > L.readM - 1, 'moved while readable');
  }
  // at every moment the live markings sit at least lenM + gapM apart
  const byT = new Map();
  for (const views of seen.values()) for (const v of views) if (v.state === 'live') { const k = Math.round(v.t * 60); (byT.get(k) || byT.set(k, []).get(k)).push(v.kartS + v.dS); }
  for (const ss of byT.values()) { ss.sort((a, b) => a - b); for (let i = 1; i < ss.length; i++) assert.ok(ss[i] - ss[i - 1] >= L.lenM + L.gapM - 1e-6); }
});

test('a phrase keeps one lateral spot, toward the centre and inside the rails', () => {
  const list = [['one', 3, 0], ['two', 3.4, 0], ['three', 3.8, 0], ['four', 6, 1], ['extraordinarily', 6.4, 1]];
  const { seen, sc } = drive(chart(list), { speed: 30, lat: 5 });
  const xOf = (i) => seen.get(i)[0].x;
  assert.equal(xOf(0), xOf(1)); assert.equal(xOf(1), xOf(2));
  for (const views of seen.values()) for (const v of views) assert.equal(v.x, views[0].x);
  assert.ok(xOf(0) > 0 && xOf(0) < 5 * L.followLat + 1e-9);
  const usable = 7 - L.edgeMarginM;
  for (const e of sc) assert.ok(Math.abs(xOf(e.i)) + Math.min(L.maxWidthM, e.chars * L.charM) / 2 <= usable + 1e-9);
});

test('a bubble moment keeps its stretch of road clear', () => {
  const song = chart([['a', 4], ['b', 4.3], ['bubble', 4.6], ['c', 4.9], ['d', 5.2]]);
  const kinds = allRoad(song); kinds[2] = BUBBLE;
  const speed = 30, { seen } = drive(song, { speed, kinds });
  const bubbleS = speed * 4.6 + L.bubbleAheadM;
  for (const views of seen.values()) {
    const v = views[views.length - 1], c = v.kartS + v.dS;
    assert.ok(c + L.lenM / 2 <= bubbleS - L.clearM + 1.5 || c - L.lenM / 2 >= bubbleS + L.clearM - 1.5, 'marking at ' + c + ' in the bubble stretch ' + bubbleS);
  }
});

test('markings fade behind the kart, a pause moves nothing, a loop starts a fresh pass', () => {
  const tr = track(), song = chart([['road', 3], ['slow', 8]]), run = createRoadRun(song, tr, { script: scriptRoad(song, L, allRoad(song)) });
  let s = 0, states = new Set();
  for (let t = 0; t < 14; t += 1 / 60) { s += 30 / 60; for (const v of run.update(t, { s, lat: 0, speedMps: 30 })) states.add(v.id + v.state); }
  assert.ok(states.has('0live') && states.has('0out') && states.has('1out'));
  assert.equal(run.update(14, { s, lat: 0, speedMps: 30 }).length, 0);
  const a = run.update(1, { s: 0, lat: 0, speedMps: 30 });   // the song looped: back to the top
  assert.equal(a.length, 1);
  const before = a[0].s, again = run.update(1, { s: 0, lat: 0, speedMps: 30 });
  assert.equal(again[0].s, before);
  assert.equal(run.update(NaN, { s: 0 }).length, 1);
});

test('the s of a marking wraps with the lap', () => {
  const tr = track(), song = chart(Array.from({ length: 12 }, (_, i) => ['w' + i, 2 + i * 0.8])), run = createRoadRun(song, tr, { script: scriptRoad(song, L, allRoad(song)) });
  for (let t = 0; t < 14; t += 1 / 60) for (const v of run.update(t, { s: (tr.lengthM - 60 + t * 25) % tr.lengthM, lat: 0, speedMps: 25 })) {
    assert.ok(v.s >= 0 && v.s < tr.lengthM);
    assert.ok(Math.abs(v.dS) < 120);
  }
});

test('the look of a phrase follows the section mood', () => {
  const song = normalizeChart({ bpm: 120, sections: [
    { id: 'a', startS: 0, mood: 'intro' }, { id: 'b', startS: 10, mood: 'build' }, { id: 'c', startS: 20, mood: 'drop', loop: 0.9 },
    { id: 'd', startS: 30, mood: 'break' }, { id: 'e', startS: 40, mood: 'groove', loop: 0.5 }, { id: 'f', startS: 50, mood: 'groove', loop: 0.85 },
    { id: 'g', startS: 60, mood: 'peak' }] });
  assert.equal(phraseStyle(song, 1, 0), STYLE.ROUNDED);
  assert.equal(phraseStyle(song, 11, 0), STYLE.EDITORIAL);
  assert.deepEqual([phraseStyle(song, 21, 0), phraseStyle(song, 22, 1)], [STYLE.NEON, STYLE.GLOSS]);
  assert.equal(phraseStyle(song, 31, 0), STYLE.SILK);
  assert.equal(phraseStyle(song, 41, 0), STYLE.STENCIL);
  assert.equal(phraseStyle(song, 51, 0), STYLE.MONO);
  assert.equal(phraseStyle(song, 61, 1), STYLE.NEON);
  assert.equal(paintText('Good road', STYLE.STENCIL), 'GOOD ROAD');
  assert.equal(paintText('Good road', STYLE.SILK), 'good road');
  for (const st of STYLES) assert.ok(['condensed', 'display', 'editorial', 'mono', 'rounded', 'silk'].includes(st.face));
});

test('the atlas packs rows inside the sheet or says it cannot', () => {
  const p = packAtlas([500, 500, 500, 500, 900, 100], 2048, 100, 400);
  assert.deepEqual(Array.from(p.y), [0, 0, 0, 0, 100, 100]);
  assert.deepEqual(Array.from(p.x), [0, 500, 1000, 1500, 0, 900]);
  assert.equal(p.heightPx, 200);
  assert.equal(packAtlas(new Array(50).fill(1000), 2048, 100, 400), null);
});

test('IN wipe, karaoke light and OUT; Gentle is slower and still', () => {
  const v = { state: 'live', inT: 0, outT: -1, remainS: 1, lenS: 0.3 };
  const a = markLook(v, {}, {});
  assert.equal(a.inK, 0); assert.equal(a.sung, 0); assert.equal(a.alpha, 1);
  assert.equal(markLook({ ...v, inT: RL.inS }, {}).inK, 1);
  assert.ok(markLook({ ...v, inT: RL.inS }, { gentle: true }).inK < 1);
  assert.equal(markSung({ remainS: 0.01, lenS: 0.3 }), 0);
  assert.ok(markSung({ remainS: -0.1, lenS: 0.3 }) > 0.99);
  assert.ok(markSung({ remainS: -0.1, lenS: 0.3 }, true) < 0.7, 'gentle rises slower');
  const rest = markSung({ remainS: -5, lenS: 0.3 });
  assert.ok(Math.abs(rest - RL.sungRest) < 1e-9);
  const out = markLook({ ...v, state: 'out', outT: RL.outS }, {});
  assert.equal(out.alpha, 0);
});

test('the frame look: kick pulse and glitch off under Gentle, the drop boost only when granted', () => {
  const mf = createMusicFrame();
  assert.equal(frameLook(mf, {}).glitch, 0);
  Object.assign(mf, { on: true, kick: 1, helix: 1, beatPhase: 0, mix: 0.8, big: 1, waveAmp: 1, waveIdx: 0, calm: 0 });
  const a = frameLook(mf, {}, {}), g = frameLook(mf, { gentle: true }, {});
  assert.ok(a.pulse > 0.9 && a.glitch > RL.glitchM * 0.9);
  assert.equal(g.glitch, 0); assert.ok(g.pulse <= RL.breathGentle);
  assert.ok(frameLook(mf, { dropOk: true }, {}).glow > a.glow);
  assert.ok(g.wave < a.wave);
});
