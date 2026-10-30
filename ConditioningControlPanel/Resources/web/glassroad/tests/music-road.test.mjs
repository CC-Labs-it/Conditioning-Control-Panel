// The music road: chart reader (audio/chart.js), conductor (render/musicRoadMath.js), helix geometry
// (render/helixMath.js). handoff-1022/MUSIC-ROAD-BRIEF.md.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack } from './sim-fake.mjs';
import { normalizeChart, beatOf, barOf, sectionOf, hitEnv, energyOf, nextBarS, lastAtOrBefore } from '../audio/chart.js';
import { musicFrame, createMusicFrame, sectionModes, songEvents, MUSIC_LOOK } from '../render/musicRoadMath.js';
import { helixGeometryData, HELIX_LOOK } from '../render/helixMath.js';

// 120 bpm: a beat every 0.5 s, a bar every 2 s from 1 s
function chart(extra = {}) {
  const beats = [], downbeats = [];
  for (let t = 1; t < 61; t += 0.5) beats.push(+t.toFixed(3));
  for (let t = 1; t < 61; t += 2) downbeats.push(t);
  return normalizeChart({
    id: 't', bpm: 120, downbeatS: 1, durationS: 60, beats, downbeats,
    sections: [
      { id: 's1', startS: 1, endS: 9, mood: 'break', energy: 0.2, loop: 0.5 },
      { id: 's2', startS: 9, endS: 25, mood: 'groove', energy: 0.5, loop: 0.95 },
      { id: 's3', startS: 25, endS: 61, mood: 'drop', energy: 0.9, loop: 0.3 },
    ],
    hits: { kick: beats.map((t) => [t, 0.9]), snare: [[2, 0.6]], hats: [] },
    energy: { fps: 25, mix: Array(1500).fill(128), bass: Array(1500).fill(255), vocals: [], drums: [] },
    words: [{ w: 'road', startS: 13.3, endS: 13.7, line: 1, bubble: true, gate: false }],
    echoes: [{ line: 1, startS: 14, endS: 15.4, pan: -0.6, text: 'road' }],
    triggers: [{ startS: 25, kind: 'drop' }, { startS: 40.2, kind: 'word', w: 'good girl' }],
    sfx: [{ startS: 25.05, id: 'drop-swell', kind: 'drop' }],
    ...extra,
  });
}

test('normalizeChart fills every list, sorts and clamps, and survives junk', () => {
  const c = normalizeChart({ bpm: 0, beats: [3, 1, 'x', 2], sections: [{ startS: 5, mood: 'nope' }, { startS: 1 }],
    hits: { kick: [[2, 4], [1, 0.5]] }, energy: { mix: [300, -5] } });
  assert.equal(c.bpm, 96);
  assert.deepEqual(c.beats, [1, 2, 3]);
  assert.deepEqual(c.sections.map((s) => s.startS), [1, 5]);
  assert.equal(c.sections[1].mood, 'groove');
  assert.deepEqual([...c.hits.kick.t], [1, 2]);
  assert.equal(c.hits.kick.v[1], 1);
  assert.deepEqual([...c.energy.mix], [255, 0]);
  assert.deepEqual(c.words, []);
  const empty = normalizeChart(null);
  assert.equal(empty.sections.length, 0);
  assert.ok(Math.abs(beatOf(empty, 1.25).phase - (1.25 * 96 / 60 % 1)) < 1e-9);   // no beats: the bpm grid
});

test('beat and bar come from the listed times, then keep the grid going past the ends', () => {
  const c = chart();
  const b = beatOf(c, 2.25);
  assert.equal(b.index, 2); assert.ok(Math.abs(b.phase - 0.5) < 1e-9); assert.equal(b.lenS, 0.5);
  const bar = barOf(c, 4);
  assert.equal(bar.index, 1); assert.ok(Math.abs(bar.phase - 0.5) < 1e-9);
  const before = beatOf(c, 0.75);
  assert.ok(Math.abs(before.phase - 0.5) < 1e-9);
  assert.equal(nextBarS(c, 3.2), 5);
  assert.equal(sectionOf(c, 10).id, 's2');
  assert.equal(sectionOf(c, 0).id, 's1');
  assert.equal(lastAtOrBefore([1, 2, 3], 2), 1);
});

test('hit envelopes rise with the hit and decay after it; energy reads the curve', () => {
  const c = chart();
  assert.ok(hitEnv(c, 'kick', 3) > 0.85);
  assert.ok(hitEnv(c, 'kick', 3.3) < 0.2);
  assert.equal(hitEnv(c, 'hats', 3), 0);
  assert.ok(Math.abs(energyOf(c, 'mix', 10) - 128 / 255) < 1e-9);
  assert.equal(energyOf(c, 'vocals', 10), 0);
});

test('no song: the frame rests (the road looks as it always did)', () => {
  const f = musicFrame(null, 3);
  assert.equal(f.on, false);
  assert.equal(f.vibM, 0); assert.equal(f.helix, 0); assert.equal(f.shatter, 0); assert.equal(f.waveAmp, 0);
  assert.equal(musicFrame(chart(), NaN).on, false);
});

test('modes: break is calm, a repetitive groove winds the helix, a drop goes big', () => {
  assert.equal(sectionModes({ mood: 'break', loop: 1 }).calm, 1);
  assert.equal(sectionModes({ mood: 'break', loop: 1 }).helix, 0);
  assert.equal(sectionModes({ mood: 'groove', loop: 0.95 }).helix, 1);
  assert.equal(sectionModes({ mood: 'groove', loop: 0.5 }).helix, 0);
  assert.equal(sectionModes({ mood: 'peak', loop: 0 }).big, 1);
});

test('a mode change crossfades over the first bar of the new section, never mid-beat', () => {
  const c = chart();
  const at = (t) => musicFrame(c, t);
  assert.equal(at(8.9).helix, 0);
  assert.equal(at(8.9).calm, 1);
  const mid = at(10);
  assert.ok(mid.helix > 0.3 && mid.helix < 0.7, 'halfway through the first bar');
  assert.equal(at(11.01).helix, 1);
  assert.equal(at(11.01).calm, 0);
});

test('the drop wave starts a bar ahead and reaches the kart on the drop', () => {
  const c = chart();
  assert.deepEqual([...songEvents(c).drops], [25]);   // the trigger and its sfx merge into one drop
  const early = musicFrame(c, 22.9);
  assert.equal(early.waveAmp, 0);
  const lead = musicFrame(c, 23.5);
  assert.ok(lead.waveAmp > 0 && lead.waveD > 0 && lead.waveD < MUSIC_LOOK.waveAheadM);
  const on = musicFrame(c, 25);
  assert.ok(Math.abs(on.waveD) < 1e-6);
  assert.ok(on.waveAmp > 0.9);
  const after = musicFrame(c, 25.4);
  assert.ok(after.waveD < 0 && after.waveAmp > 0 && after.waveAmp < on.waveAmp);
  assert.equal(musicFrame(c, 27).waveAmp, 0);
});

test('a trigger word shatters the road for about two bars, smaller under Gentle', () => {
  const c = chart();
  assert.equal(musicFrame(c, 40.1).shatter, 0);
  const hit = musicFrame(c, 40.4);
  assert.ok(hit.shatter > 0.9);
  assert.ok(hit.melodyM > MUSIC_LOOK.melodyShatterM * 0.8);
  assert.equal(musicFrame(c, 44.5).shatter, 0);
  const soft = musicFrame(c, 40.4, { gentle: true });
  assert.ok(soft.shatter < hit.shatter * 0.6);
  assert.ok(musicFrame(c, 30, { gentle: true }).vibM < musicFrame(c, 30).vibM);
});

test('an echo shimmers the edge on its own side', () => {
  const f = musicFrame(chart(), 14.7);
  assert.ok(f.echoL > 0.3);
  assert.equal(f.echoR, 0);
  assert.equal(musicFrame(chart(), 16).echoL, 0);
});

test('the frame is a function of the song time alone (pause, seek, any call order)', () => {
  const c = chart(), out = createMusicFrame();
  const a = JSON.stringify(musicFrame(c, 33.3, {}, out));
  musicFrame(c, 5, {}, out); musicFrame(c, 55, {}, out);
  const b = JSON.stringify(musicFrame(c, 33.3, {}, out));
  assert.equal(a, b);
});

test('helix strands cross the road plane outside the road edge, never on the surface', () => {
  const tr = fakeTrack({ nodes: 60, spacing: 10, w: 14 });
  const d = helixGeometryData(tr);
  const L = HELIX_LOOK;
  for (const r of d.radius) {
    const lateralAtPlane = Math.sqrt(r * r - L.liftM * L.liftM);
    assert.ok(lateralAtPlane >= 7 + L.outM - 1e-9);
  }
  assert.equal(d.attrs.aC.length, d.count * 3);
  assert.ok(d.index.every((i) => i < d.count));
  assert.ok(d.attrs.aKind.includes(1), 'base-pair stubs');
});
