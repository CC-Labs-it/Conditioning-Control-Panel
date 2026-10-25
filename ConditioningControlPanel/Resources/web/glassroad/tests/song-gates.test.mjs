import test from 'node:test';
import assert from 'node:assert/strict';
import { createSongGateRun, songGateCues } from '../sim/songGates.js';
import { createWordRun } from '../sim/words.js';
import { createState, S, setPos, setVel } from '../sim/state.js';
import { playPhraseGate } from '../audio/phraseGates.js';
import { normalizeChart } from '../audio/chart.js';

const song = () => normalizeChart({ durationS: 30, triggers: [{ kind: 'word', w: 'follow the signal', startS: 6 }, { kind: 'drop', startS: 12 }] });
const track = { lengthM: 10000, frameAt(s, out = {}) { return Object.assign(out, { p: [0, 0, s], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, 1], w: 20, flags: ['rail'] }); } };
const kart = (z, v = 30, x = 0, y = 0) => { const st = createState(); setPos(st, [x, y, z]); setVel(st, [0, 0, v]); st[S.s] = z; st[S.grounded] = 1; return st; };

test('song cues use recorded phrases, expand selected words to their line, and keep drops distinct', () => {
  const s = normalizeChart({ lines: [{ i: 0, text: 'follow the signal', startS: 2, endS: 3 }],
    words: [{ w: 'signal', line: 0, startS: 2.5, endS: 3, gate: true }],
    triggers: [{ kind: 'drop', startS: 2.6 }, { kind: 'drop', startS: 10 }, { kind: 'word', w: 'stay with me', startS: 15 }] });
  assert.deepEqual(songGateCues(s).map(c => [c.startS, c.text, c.kind]),
    [[2.5, 'follow the signal', 'phrase'], [10, 'DROP', 'drop'], [15, 'stay with me', 'phrase']]);
});
test('constant-speed drivers cross on the audible cue at 30, 60 and 120 frames per second', () => {
  for (const fps of [30, 60, 120]) for (const speed of [14, 30, 44]) {
    const run = createSongGateRun(song(), track), hits = [];
    for (let i = 0; i < 15 * fps; i++) {
      const t = i / fps, result = run.update(t, kart(200 + speed * t, speed));
      if (result.hit) hits.push({ t, cue: result.hit.startS });
    }
    assert.equal(hits.length, 2, `${fps} fps, ${speed} m/s`);
    for (const h of hits) assert.ok(Math.abs(h.t - h.cue) <= 1 / fps + .001);
  }
});
test('approach responds to boost, locks before impact and never follows the driver after a miss', () => {
  const run = createSongGateRun(song(), track);
  let z = 100, hit = null, lockedS = null;
  for (let i = 0; i <= 7 * 60; i++) {
    const t = i / 60, v = t > 5 ? 42 : 24; z += v / 60;
    const result = run.update(t, kart(z, v));
    const gate = result.live.find(g => g.startS === 6);
    if (gate?.locked) { if (lockedS === null) lockedS = gate.s; assert.equal(gate.s, lockedS); }
    if (result.hit) hit = t;
  }
  assert.ok(Math.abs(hit - 6) < .1);
  const miss = createSongGateRun(song(), track);
  for (let i = 0; i < 9 * 60; i++) miss.update(i / 60, kart(100 + i / 2, 30, 30));
  assert.equal(miss.hits, 0);
  assert.equal(miss.update(9, kart(400)).live.length, 0);
});
test('stopping, reverse travel, upper roads, teleport, rescue and late seek cannot fake a song gate', () => {
  for (const variant of ['stop', 'reverse', 'upper', 'teleport', 'rescue', 'seek']) {
    const run = createSongGateRun(song(), track);
    for (let i = 0; i <= 8 * 60; i++) {
      const t = i / 60;
      if (variant === 'seek' && t > 5 && t < 7) continue;
      const st = kart(200 + 30 * t, variant === 'reverse' ? -30 : 30, 0, variant === 'upper' ? 12 : 0);
      if (variant === 'stop' && t > 5.7) { setPos(st, [0, 0, 371]); st[S.s] = 371; setVel(st, [0, 0, 0]); }
      if (variant === 'teleport' && t >= 5.9) { st[S.posZ] += 200; st[S.s] += 200; }
      if (variant === 'rescue' && t >= 5.7) st[S.respawnPhase] = 1;
      run.update(t, st);
    }
    assert.equal(run.hits, 0, variant);
  }
});
test('song pause is immutable; loop and restart rearm one crossing per cue', () => {
  const run = createSongGateRun(song(), track), st = kart(350);
  run.update(5, st); const before = JSON.stringify(run.update(5.1, kart(353)).live);
  assert.equal(JSON.stringify(run.update(5.1, kart(500)).live), before);
  for (let i = 0; i < 8 * 60; i++) run.update(i / 60, kart(200 + i / 2));
  assert.equal(run.hits, 1);
  run.reset(); assert.equal(run.hits, 0);
});
test('gates use physical fork metres rather than mapped standings progress', () => {
  const fork = { id: 'branch', lengthM: 1000, toS: 8000, localS: s => (s - 1000) / 4, mainS: s => 1000 + s * 4,
    frameAt(s, out = {}) { track.frameAt(s, out); out.p[0] = 100; out.s = 1000 + s * 4; return out; } };
  const branched = { ...track, forkAtSeg: seg => seg === 999 ? fork : null, pathFrameAt: (s, seg, out) => fork.frameAt(fork.localS(s), out) };
  const run = createSongGateRun(song(), branched);
  for (let i = 0; i < 8 * 60; i++) {
    const z = 100 + i / 2, st = kart(z, 30, 100); st[S.seg] = 999; st[S.s] = fork.mainS(z);
    run.update(i / 60, st);
  }
  assert.equal(run.hits, 1);
});
test('song gates replace gate bubble rows and never overlay a second musical or vocal cue', () => {
  const s = normalizeChart({ words: [{ w: 'signal', startS: 6, endS: 7, gate: true }] });
  assert.equal(createWordRun(s, track).total, 1);
  assert.equal(createWordRun(s, track, { songGates: true }).total, 0);
  playPhraseGate({ tone() { assert.fail('song gate must use the recording'); } }, { song: true });
});
