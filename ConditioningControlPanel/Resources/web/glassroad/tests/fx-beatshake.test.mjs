/* Polish 6 (BEAT): the beat channel of fx/shake.js on an injected music voice. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { normalizeChart } from '../audio/chart.js';
import { createShake } from '../fx/shake.js';
import { setLiveMusic } from '../audio/songShape.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

test('live race music never adds camera bumps by default, while physical kicks still work', () => {
  const r = rig();
  setLiveMusic(r.music);
  try {
    const shake = createShake(r.feel);
    for (let t = 8; t < 40; t += 1 / 60) {
      r.music.songS = t;
      assert.deepEqual(shake.update(1 / 60), [0, 0, 0]);
    }
    shake.kick('hit');
    assert.ok(shake.update(1 / 60)[1] > 0, 'real impact remains');
  } finally { setLiveMusic(null); }
});

/** 120 bpm: 4 intro bars then 28 drop bars; the music voice is a plain object the test moves. */
function rig({ reduced = false } = {}) {
  const beats = [], downbeats = [];
  for (let b = 0; b < 32; b++) { downbeats.push(b * 2); for (let k = 0; k < 4; k++) beats.push(b * 2 + k * 0.5); }
  const song = normalizeChart({ bpm: 120, beats, downbeats, durationS: 64,
    sections: [{ startS: 0, mood: 'intro', energy: 0.1 }, { startS: 8, mood: 'drop', energy: 0.9 }] });
  const music = { playing: true, songS: 0, song, analysis: null };
  const feel = createFeel(feelJson);
  let red = reduced;
  const sh = createShake(feel, { music, reduced: () => red });
  return { feel, music, sh, setReduced: (v) => { red = v; } };
}

/** Run the shake from song time a to b at 240 fps; returns the vertical samples. */
function run(r, a, b, dt = 1 / 240) {
  const ys = [];
  for (let t = a; t < b; t += dt) { r.music.songS = t; ys.push(r.sh.update(dt)[1]); }
  return ys;
}

test('beat shake: still before the rise, dips on the onset downbeat, vertical only', () => {
  const r = rig();
  assert.ok(run(r, 0, 7.9).every((y) => y === 0));
  r.music.songS = 8; const v = r.sh.update(1 / 240);
  assert.equal(v[0], 0); assert.equal(v[2], 0);
  assert.ok(v[1] < -0.005, 'dips down on the onset downbeat ' + v[1]);
  assert.ok(r.sh.beat.shape.onsetS === 8);
});

test('beat shake: every dip has an IN and an OUT (no hard step between frames)', () => {
  const r = rig();
  const ys = run(r, 7.5, 40);
  let maxStep = 0;
  for (let i = 1; i < ys.length; i++) maxStep = Math.max(maxStep, Math.abs(ys[i] - ys[i - 1]));
  const deepest = Math.min(...ys);
  assert.ok(deepest < -0.02, 'deepest ' + deepest);
  // a smoothstep rise over inS (30 ms) moves at most 1.5 * depth * dt / inS per frame: about a fifth at 240 fps
  assert.ok(maxStep < Math.abs(deepest) * 0.25, 'step ' + maxStep + ' of ' + deepest);
  let run0 = 0, longest = 0;
  for (const y of ys) { run0 = y < 0 ? run0 + 1 : 0; longest = Math.max(longest, run0); }
  assert.ok(longest >= 0.15 * 240, 'a dip lasts its in and out ' + longest);
});

test('beat shake: Gentle and reduced motion zero it', () => {
  const g = rig();
  g.feel.set('gentle.on', true);
  assert.ok(run(g, 8, 20).every((y) => y === 0));
  const m = rig({ reduced: true });
  assert.ok(run(m, 8, 20).every((y) => y === 0));
});

test('beat shake: a kick wins, the beat ducks under it and comes back', () => {
  const r = rig();
  run(r, 7, 20);
  r.music.songS = 20; r.sh.kick('boost');
  // during the boost run the vertical axis holds only what is left of the ducked beat
  const during = [];
  for (let t = 20; t < 20.35; t += 1 / 240) { r.music.songS = t; during.push(r.sh.update(1 / 240)[1]); }
  const free = rig();
  const ref = run(free, 7, 20.35).slice(-during.length);
  const sum = (a) => a.reduce((s, y) => s + Math.abs(y), 0);
  assert.ok(sum(during.slice(30)) < 0.2 * sum(ref.slice(30)), 'ducked ' + sum(during) + ' vs ' + sum(ref));
  const after = run(r, 21.5, 24);
  assert.ok(Math.min(...after) < -0.02, 'back after the kick');
});

test('beat shake: the song stopping mid-dip eases out, never cuts', () => {
  const r = rig();
  run(r, 7, 8.0);
  const at = r.sh.update(1 / 240)[1];
  assert.ok(at < -0.005);
  r.music.playing = false;
  const next = r.sh.update(1 / 240)[1];
  assert.ok(next < 0 && Math.abs(next - at) < Math.abs(at) * 0.5, at + ' -> ' + next);
  const tail = run(r, 8, 8.5);
  assert.equal(tail[tail.length - 1], 0);
});

test('shake: a side kick (bump) is camera-local x, sized by its amp, and the beat ducks under it', () => {
  const r = rig();
  run(r, 7, 20);
  r.music.songS = 20; r.sh.kick('side', 0.08);
  let peakX = 0, peakY = 0;
  for (let t = 20; t < 20.3; t += 1 / 240) {
    r.music.songS = t;
    const v = r.sh.update(1 / 240);
    assert.equal(v[2], 0);
    peakX = Math.max(peakX, Math.abs(v[0])); if (t > 20.12) peakY = Math.max(peakY, Math.abs(v[1]));
  }
  assert.ok(Math.abs(peakX - 0.08) < 2e-3, 'side peak ' + peakX);
  assert.ok(peakY < 0.01, 'beat ducked under the bump ' + peakY);
  const g = rig(); g.feel.set('gentle.on', true); g.sh.kick('side', 0.08);
  assert.deepEqual([...g.sh.update(0.05)], [0, 0, 0]);
});

test('shake: a run outside the channel (the bump side shake, otherKick) ducks the beat too', () => {
  const r = rig();
  run(r, 7, 20);
  const during = [];
  for (let t = 20; t < 20.35; t += 1 / 240) { r.music.songS = t; during.push(r.sh.update(1 / 240, true)[1]); }
  const ref = run(rig(), 7, 20.35).slice(-during.length);
  const sum = (a) => a.reduce((s, y) => s + Math.abs(y), 0);
  assert.ok(sum(during.slice(30)) < 0.2 * sum(ref.slice(30)), 'ducked ' + sum(during) + ' vs ' + sum(ref));
  assert.ok(Math.min(...run(r, 21.5, 24)) < -0.02, 'back once it ends');
});
