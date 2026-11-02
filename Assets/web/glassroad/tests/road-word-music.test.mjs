import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeChart } from '../audio/chart.js';
import { ROAD_LOOK, scriptRoad } from '../sim/roadWords.js';
import { ROAD, BUBBLE } from '../sim/wordPick.js';
import { createMusicFrame, musicFrame } from '../render/musicRoadMath.js';
import { createMusicVisualData, stepMusicVisualData } from '../render/musicVisualData.js';
import { planRoadWordAccents, frameRoadWordMusic } from '../render/roadWordMusic.js';

const chart = (times, extra = {}) => normalizeChart({ bpm: 120,
  sections: [{ id: 'normal', startS: 0, endS: 60, mood: 'groove' }],
  words: times.map((t, i) => ({ w: 'word' + i, startS: t, endS: t + .16, line: i })), ...extra });
const script = song => scriptRoad(song, ROAD_LOOK, new Uint8Array(song.words.length).fill(ROAD));
const selected = values => [...values].flatMap((value, index) => value ? [index] : []);

test('ordinary floor words stay uncoloured; sparse accents use the actual drop hit rather than its swell start', () => {
  const plain = chart([0, 2, 5, 9]), markings = script(plain);
  assert.deepEqual([...planRoadWordAccents(plain, markings)], [0, 0, 0, 0]);
  const song = chart([0, 2, 5, 9], { triggers: [{ kind: 'drop', startS: 2 }], sfx: [{ kind: 'drop', startS: 0, hitS: 2 }] });
  const accents = planRoadWordAccents(song, script(song));
  assert.ok(accents instanceof Float32Array);
  assert.deepEqual([...accents], [0, 1, 0, 0], 'merged drop and swell choose one floor word at the heard hit');
});

test('peak accents mark only the section start and strong word moments, never the entire section', () => {
  const song = chart([1, 5.1, 8, 10, 14.1, 18], {
    sections: [{ id: 'peak', startS: 5, endS: 30, mood: 'peak' }],
    triggers: [{ kind: 'word', startS: 14, strength: .9 }, { kind: 'word', startS: 18, strength: .2 }],
  });
  const accents = planRoadWordAccents(song, script(song));
  assert.deepEqual(selected(accents), [1, 4]);
  assert.ok(Math.abs(accents[1] - .65) < 1e-6 && Math.abs(accents[4] - .65) < 1e-6);
});

test('drops win shared phrases, repeated moments stay sparse and plans are deterministic', () => {
  const song = chart([1, 1.4, 4, 6.2, 9], { triggers: [
    { kind: 'word', startS: 1 }, { kind: 'drop', startS: 1.4 },
    { kind: 'word', startS: 4 }, { kind: 'word', startS: 4.1 },
    { kind: 'drop', startS: 6.2 },
  ] });
  const markings = script(song); markings[0].phrase = markings[1].phrase;
  const first = planRoadWordAccents(song, markings);
  assert.deepEqual(selected(first), [1, 2, 3]);
  assert.equal(first[1], 1); assert.equal(first[0], 0);
  assert.deepEqual(planRoadWordAccents(song, markings), first);
  assert.deepEqual(planRoadWordAccents(song, markings), first, 'seeking cannot consume or duplicate an accent');
});

test('an excluded bubble can lend its nearby accent to an eligible floor word without changing the script', () => {
  const song = chart([5.3, 6, 9], { triggers: [{ kind: 'drop', startS: 6 }] });
  const kinds = new Uint8Array(song.words.length).fill(ROAD); kinds[1] = BUBBLE;
  const markings = scriptRoad(song, ROAD_LOOK, kinds), before = JSON.stringify(markings);
  const accents = planRoadWordAccents(song, markings);
  assert.deepEqual([...accents], [1, 0], '.7 seconds uses the bounded fallback when the exact hit word is a bubble');
  assert.equal(JSON.stringify(markings), before, 'no excluded word is restored or retimed');
  const remote = chart([4.7, 9], { triggers: [{ kind: 'drop', startS: 6 }] });
  assert.deepEqual([...planRoadWordAccents(remote, script(remote))], [0, 0], 'no word beyond two beats / 1.2 seconds is borrowed');
});

test('function-only merged markings do not steal an accent from nearby content words', () => {
  const song = normalizeChart({ bpm: 120, sections: [], triggers: [{ kind: 'drop', startS: 4 }], words: [
    { w: 'and', startS: 4, endS: 4.05, line: 0 }, { w: 'the', startS: 4.08, endS: 4.2, line: 0 },
    { w: 'rhythm', startS: 4.7, endS: 4.9, line: 1 },
  ] });
  const markings = script(song), before = JSON.stringify(markings);
  assert.equal(markings[0].text, 'and the', 'exercise the actual merged-word API');
  assert.deepEqual([...planRoadWordAccents(song, markings)], [0, 1], 'reject function-only text before nearest-word ranking');
  assert.equal(JSON.stringify(markings), before, 'the accent filter never edits or removes painted words');
  for (const text of ['mmm', 'and rhythm']) {
    const eligible = [{ text, startS: 4, endS: 4.2, phrase: 0 }];
    assert.deepEqual([...planRoadWordAccents(song, eligible)], [1], 'content mixtures and vocal sounds remain eligible');
  }
});

test('a supplied silent visual source overrules chart hits and has no free-running vibration', () => {
  const mf = { on: true, kick: 1, snare: 1, hats: 1 }, v = createMusicVisualData();
  for (const t of [0, .1, 1.234, 60]) assert.deepEqual(frameRoadWordMusic(t, mf, { visual: v }),
    { time: t, kick: 0, snare: 0, hats: 0, vibration: 0 });
  assert.ok(frameRoadWordMusic(.013, mf, null).kick > 0, 'a caller without any live source retains chart fallback');
});

test('actual independent visual envelopes and flux drive each pulse without leaking between instruments', () => {
  const v = createMusicVisualData(), mf = createMusicFrame(), song = chart([], { hits: { kick: [[1, 1]] } });
  const signal = { active: true, rawEnergy: .2, rawBass: .01, rawMid: .01, rawTreble: .01, songS: 1 };
  stepMusicVisualData(v, signal, 1 / 60, { song });
  const frame = frameRoadWordMusic(1, mf, { visual: v });
  assert.equal(frame.kick, 1); assert.equal(frame.snare, 0); assert.equal(frame.hats, 0);
  v.kick = 0; v.flux[2] = .2;
  const high = frameRoadWordMusic(1.03, mf, v);
  assert.equal(high.kick, 0); assert.equal(high.snare, 0); assert.ok(high.hats > .69 && high.hats < .71);
  const wrapped = { active: { value: true }, kick: { value: .4 }, snare: { value: .2 }, hats: { value: .1 } };
  assert.equal(frameRoadWordMusic(.2, mf, wrapped).kick, .4);
});

test('vibration is bounded, reuses its output and follows the frozen heard clock across pauses and seeks', () => {
  const mf = { on: true, kick: 1, snare: 1, hats: 1 }, out = {};
  let maximum = 0;
  for (let i = 0; i < 1000; i++) {
    assert.equal(frameRoadWordMusic(i / 240, mf, null, {}, out), out);
    maximum = Math.max(maximum, Math.abs(out.vibration));
    assert.ok(Math.abs(out.vibration) <= .08);
  }
  assert.ok(maximum > .035, 'real hits produce a small visible movement');
  const held = { ...frameRoadWordMusic(3.133, mf, null) };
  frameRoadWordMusic(50, mf, null, {}, out);
  assert.deepEqual(frameRoadWordMusic(3.133, mf, null, {}, out), held, 'a seek is a pure function of heard time');
  assert.deepEqual(frameRoadWordMusic(3.133, mf, null), held, 'unchanged song time holds exactly on pause');
});

test('Gentle and Motion Off suppress fast pulses while Less halves the same motion', () => {
  const mf = { on: true, kick: 1, snare: .7, hats: .3 }, t = .073;
  const full = frameRoadWordMusic(t, mf, null), less = frameRoadWordMusic(t, mf, null, { motion: 'less' });
  assert.equal(less.vibration, full.vibration * .5);
  for (const options of [{ gentle: true }, { motion: 'off' }])
    assert.deepEqual(frameRoadWordMusic(t, mf, null, options), { time: t, kick: 0, snare: 0, hats: 0, vibration: 0 });
  assert.deepEqual(frameRoadWordMusic(NaN, mf, null), { time: 0, kick: 0, snare: 0, hats: 0, vibration: 0 });
  const song = chart([], { hits: { snare: [[1, .8]] } }), frame = musicFrame(song, 1);
  assert.ok(frameRoadWordMusic(1, frame, undefined).snare > .79, 'fallback reads the real music-frame API');
});
