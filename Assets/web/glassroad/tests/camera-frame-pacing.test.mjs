import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { createRacers } from '../sim/racers.js';
import { createRace } from '../sim/race.js';
import { createAi } from '../sim/ai.js';
import { createClock } from '../core/clock.js';
import { createActions } from '../core/actions.js';
import { S } from '../sim/state.js';
import { makeFeel } from './sim-fake.mjs';

const hooks = registerHooks({ resolve(name, context, next) {
  return name === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createChaseCamera } = await import('../render/camera.js');
hooks.deregister();

// Owner report Oct 26 (phone, live web build): the camera bumps up and down on ordinary driving.
// A phone near or under 30 fps makes the fixed-step clock drop time, so the drawn kart (P.view) moves less
// than frameDt some frames and more on others. The aim holds the drawn racer exactly while the eye used
// to predict with the wall dt, so the view pitch flickered frame to frame on every slope.
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')), { moving: true });

/** AI drives the player for `seconds` on frames of `frameMs(i)`; returns the grounded pitch jitter (rms of
 * the frame-to-frame second difference, mrad) and the camera eye vs drawn kart height spread. */
function drive(frameMs, seconds, passViewDt = true) {
  const feel = makeFeel(), queue = { emit() {} }, race = createRace(track, feel, queue);
  const racers = createRacers(track, feel, queue, { count: 1, seed: 7 }), ai = createAi(track, feel, { seed: 7 });
  const clock = createClock({ hz: 60, maxSubsteps: 2 }), P = racers.player, a = createActions();
  const aiCtx = { racers: racers.list, items: null, standings: null, race };
  racers.interpolate(1);
  const cam = createChaseCamera(feel); cam.snap(P.view, track, 16 / 9);
  race.start();
  const rows = []; let now = 0, lastView = null;
  for (let i = 0; now < seconds * 1000; i++) {
    now += frameMs(i);
    const c = clock.frame(now);
    for (let k = 0; k < c.steps; k++) { ai.drive(P, aiCtx, clock.dt, a); race.update(clock.dt, a, P.st); racers.step(clock.dt, a, race, { ai }); }
    racers.interpolate(c.alpha);
    const viewDt = lastView != null && c.renderTime >= lastView ? c.renderTime - lastView : c.frameDt;
    lastView = c.renderTime;
    cam.update(P.view, null, track, c.frameDt, 16 / 9, 'race', null, passViewDt ? { viewDt } : {});
    const e = cam.camera.position, q = cam.camera.quaternion, dy = -2 * (q.y * q.z + q.w * q.x);   // view -Z, world y
    rows.push({ t: now / 1000, air: P.view[S.airborne] > 0.5 || P.view[S.respawnPhase] !== 0, pitch: Math.asin(Math.max(-1, Math.min(1, dy))), lift: e.y - P.view[S.posY] });
  }
  const airT = rows.filter((r) => r.air).map((r) => r.t);
  let sum = 0, n = 0;
  for (let i = 1; i < rows.length - 1; i++) {
    const r = rows[i];
    if (r.t < 6 || airT.some((t) => Math.abs(t - r.t) < 1.2)) continue;   // jumps and rescues may move the view
    const d2 = rows[i + 1].pitch - 2 * r.pitch + rows[i - 1].pitch;
    sum += d2 * d2; n++;
  }
  cam.dispose();
  return { pitchMrad: 1000 * Math.sqrt(sum / Math.max(1, n)), frames: n };
}

let seedN = 3;
const noise = () => ((seedN = (seedN * 16807) % 2147483647) / 2147483647 - 0.5) * 6;   // +-3 ms rAF jitter

test('a dropped-time phone frame rate never flickers the chase pitch on ordinary driving', () => {
  const smooth = drive(() => 1000 / 60, 40);
  const phone24 = drive(() => 1000 / 24, 40), phone30 = drive(() => 1000 / 30 + noise(), 40);
  assert.ok(smooth.frames > 1000 && phone24.frames > 400 && phone30.frames > 600, 'enough grounded road');
  // measured Oct 26 (8-kart 120 s trace before the fix: 60 fps 0.45, 24 fps 53, noisy 30 fps 14 mrad); after: 1.0, 3.2, 2.3 on this 1-kart drive (old 46.6 and 10.7)
  assert.ok(smooth.pitchMrad < 2, '60 fps baseline stays calm: ' + smooth.pitchMrad);
  assert.ok(phone24.pitchMrad < 7, '24 fps pitch flicker: ' + phone24.pitchMrad);
  assert.ok(phone30.pitchMrad < 4.5, 'noisy 30 fps pitch flicker: ' + phone30.pitchMrad);
});

test('without the view clock the same drive flickers (the guard measures the real bug)', () => {
  assert.ok(drive(() => 1000 / 24, 40, false).pitchMrad > 20);
});

test('boot feeds the drawn karts clock step to the chase camera', () => {
  const src = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.match(src, /const viewDt = lastViewTime != null && renderTime >= lastViewTime \? renderTime - lastViewTime : frameDt;/);
  assert.match(src, /cam\.update\(P\.view, fs, track, frameDt, renderer\.aspect, mode, lookInput, \{\s*active: state === 'race', rearView, viewDt,/);
});
