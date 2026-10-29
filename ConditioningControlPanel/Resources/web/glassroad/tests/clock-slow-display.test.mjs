// A phone drawing 9 to 30 fps must still race in real time: the sim catches up a whole capped frame.
// At maxSubsteps 2 a 20 fps phone ran the race at 67 percent speed and a 9 fps phone at 30 percent
// while the song played on in real time ("the game lags and gets stuck", owner 2026-10-26).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createClock } from '../core/clock.js';

const feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

function simShare(clockOpts, fps, seconds = 10) {
  const c = createClock(clockOpts);
  const gap = 1000 / fps;
  let t = 1000;
  c.frame(t);
  const frames = Math.round(seconds * fps);
  for (let i = 0; i < frames; i++) { t += gap; c.frame(t); }
  return c.simTime / (frames * gap / 1000);
}

test('clock: the shipped feel races in real time from 60 down to 10 fps', () => {
  const opts = { hz: feel.clock.hz, maxSubsteps: feel.clock.maxSubsteps, bpm: feel.clock.bpm };
  for (const fps of [60, 30, 24, 20, 15, 12, 10]) {
    const share = simShare(opts, fps);
    assert.ok(share > 0.98 && share < 1.02, `${fps} fps: sim ran at ${(share * 100).toFixed(1)} percent of real time`);
  }
});

test('clock: the cap still drops a stall past one capped frame', () => {
  const c = createClock({ hz: feel.clock.hz, maxSubsteps: feel.clock.maxSubsteps });
  c.frame(0);
  const f = c.frame(2000);                       // a 2 s stall (tab switch, GC): at most 0.1 s is paid
  assert.ok(f.steps * c.dt <= 0.1 + 1e-9, `paid ${f.steps} steps`);
  assert.ok(f.steps >= 5, 'one capped frame is caught up');
});

test('clock: two substeps was the slow-motion bug', () => {
  const share = simShare({ hz: 60, maxSubsteps: 2 }, 20);
  assert.ok(share < 0.7, `20 fps at two substeps ran at ${(share * 100).toFixed(1)} percent`);
});
