import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizeChart, beatOf, barOf } from '../audio/chart.js';
import { MUSIC_BANDS as L, BAND_COLOURS, DROP_COLOUR, musicBandBeat, musicBandCurve, musicBandShape, musicBandMask, musicBandLine, musicBandDrop, musicBandFootprint } from '../render/musicBandsMath.js';

const close = (a, b, e = 1e-7) => assert.ok(Math.abs(a - b) < e, `${a} != ${b}`);
const chart = normalizeChart({ bpm: 120, beats: [1, 1.45, 2.05, 2.49, 3.02, 3.5], downbeats: [1, 3.02] });
const at = (s, gentle = false) => {
  const b = beatOf(chart, s), bar = barOf(chart, s);
  return musicBandShape({ beat: b.index + b.phase, bar: bar.index + bar.phase, gentle, bass: 1, vocals: 0.8, halfWidth: 10, ahead: 30 });
};

test('wave onset follows the chart actual beat times, including uneven intervals', () => {
  for (let i = 0; i < chart.beats.length - 1; i++) {
    const t = chart.beats[i], end = chart.beats[i + 1];
    close(at(t).pulse, 1);
    assert.ok(at(t + (end - t) * 0.55).pulse < 0.04, 'clear decay between beats');
    assert.ok(at(t).amplitude > at(t + (end - t) * 0.55).amplitude * 2.7);
  }
});

test('wave onset is continuous across beat wrap and deterministic after seek or pause', () => {
  close(musicBandBeat(2 - 1e-5), musicBandBeat(2 + 1e-5));
  const before = at(2.17);
  at(80); at(0.4); at(3.4);
  assert.deepEqual(at(2.17), before);
  for (let beat = -2; beat < 20; beat += 0.013) assert.ok(musicBandBeat(beat) >= 0 && musicBandBeat(beat) <= 1);
});

test('Gentle removes the fast beat shape and follows only smaller slow bar motion', () => {
  const args = { gentle: true, bar: 3.2, bass: 1, vocals: 1, halfWidth: 10, ahead: 30 };
  assert.deepEqual(musicBandShape({ ...args, beat: 0 }), musicBandShape({ ...args, beat: 0.5 }));
  assert.notEqual(musicBandShape({ ...args, bar: 4 }).lateral, musicBandShape({ ...args, bar: 3 }).lateral);
  assert.ok(at(2.05, true).amplitude < at(2.05).amplitude * 0.2);
});

test('wave amplitude responds to stems and adapts to road width without reaching the centre', () => {
  assert.ok(musicBandShape({ beat: 1, bass: 1 }).amplitude > musicBandShape({ beat: 1, bass: 0 }).amplitude);
  for (const halfWidth of [5, 7, 8, 10, 12]) for (let beat = 0; beat < 8; beat += 0.17) {
    const s = musicBandShape({ halfWidth, beat, bass: 1, vocals: 1, ahead: 30 });
    assert.ok(s.amplitude <= L.amplitudeM && s.amplitude <= halfWidth * L.amplitudeShare);
    assert.ok(s.lateral - L.backingM > L.centreM[0]);
  }
});

test('cheap cubic lobes stay smooth at crests and periodic joins, with vocal shape response', () => {
  close(musicBandCurve(0), 0); close(musicBandCurve(0.25), 1); close(musicBandCurve(0.75), -1);
  assert.ok(musicBandCurve(0.1, 1) > musicBandCurve(0.1, 0));
  for (let phase = -1; phase < 2; phase += 0.003) {
    const value = musicBandCurve(phase, 1);
    close(musicBandCurve(phase + 1, 1), value);
    assert.ok(Math.abs(value) <= 1);
    const slope = Math.abs(musicBandCurve(phase + 1e-5, 1) - value) / 1e-5;
    assert.ok(slope * L.amplitudeM / L.wavelengthM <= L.maxSlope + 1e-6);
  }
  for (const phase of [-0.25, 0.25, 0.75, 1.25]) {
    const value = musicBandCurve(phase, 1);
    assert.ok(Math.abs(musicBandCurve(phase + 1e-5, 1) - value) < 1e-8);
    assert.ok(Math.abs(musicBandCurve(phase - 1e-5, 1) - value) < 1e-8);
  }
});

test('raw-coordinate footprint covers the steepest possible lobe and suppresses crest undersampling', () => {
  close(musicBandFootprint(0.03, 0.1), 0.03 + L.maxSlope * 0.1);
  assert.ok(musicBandLine(0, L.lineM, musicBandFootprint(0.01, 0.01)) > 0.99);
  assert.ok(musicBandLine(0, L.lineM, musicBandFootprint(0.1, 25)) < 0.001);
});

test('no song, the centre line, outer shoulder and distant subpixel detail remain clear', () => {
  const ctx = { halfWidth: 10, x: 5.4, distance: 25, ahead: 25 };
  assert.equal(musicBandMask(ctx), 1);
  assert.equal(musicBandMask({ ...ctx, on: false }), 0);
  for (const x of [0, 0.5, -0.5, 9, -9, 10, -10]) assert.equal(musicBandMask({ ...ctx, x }), 0);
  assert.equal(musicBandMask({ ...ctx, distance: 3 }), 0);
  assert.equal(musicBandMask({ ...ctx, distance: 180 }), 0);
  assert.equal(musicBandMask({ ...ctx, ahead: -8 }), 0);
  assert.ok(musicBandLine(0, L.lineM, 10) < 0.002);
  close(musicBandLine(0, L.lineM, 0.001), 1);
});

test('a drop front survives solid tiles and adds no duplicate front to ordinary glass', () => {
  const ctx = { on: true, x: 5.4, halfWidth: 10, distance: 30, ahead: 30, waveD: 30, waveAmp: 1 };
  close(musicBandDrop({ ...ctx, tileMask: 1 }), L.dropCoverage);
  assert.equal(musicBandDrop({ ...ctx, tileMask: 0 }), 0);
  assert.equal(musicBandDrop({ ...ctx, tileMask: 1, waveAmp: 0 }), 0);
  assert.equal(musicBandDrop({ ...ctx, tileMask: 1, on: false }), 0);
  assert.ok(musicBandDrop({ ...ctx, tileMask: 1, waveD: 0 }) < 1e-8);
});

test('wave colours are fixed below bloom white and never carry a beat multiplier', () => {
  for (const c of [...BAND_COLOURS, DROP_COLOUR]) assert.ok(c.every((v) => v >= 0 && v <= 0.7));
  assert.ok(L.coverage <= 1 && L.backingDark < 1 && L.dropCoverage < 0.5);
});
