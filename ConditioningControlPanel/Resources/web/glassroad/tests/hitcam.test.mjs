import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { TIERS } from '../render/quality.js';
import {
  HIT_CAM_DEFAULTS, hitCamNumbers, hitCamFrame, lensZoom, fovFor, shouldShow, hitCamAllowed, isPlayerHit, pickVictim,
  hitWord, HIT_WORDS, hitCamRect, orbitAz, rigOffsets, shakeAt, easeOutBack,
} from '../render/hitCamMath.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const K = hitCamNumbers(createFeel(feelJson));
const close = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);

test('hitcam: feel.json carries every hitCam key the module knows', () => {
  for (const k of Object.keys(HIT_CAM_DEFAULTS)) assert.ok(k in feelJson.hitCam, 'feel.json misses hitCam.' + k);
  assert.equal(hitCamNumbers(null).holdS, HIT_CAM_DEFAULTS.holdS);
  for (const t of Object.values(TIERS)) assert.ok(t.hitCam >= 0 && t.hitCam <= 1, t.name);
});

test('hitcam: easeOutBack overshoots then lands on 1', () => {
  close(easeOutBack(0), 0); close(easeOutBack(1), 1);
  assert.ok(easeOutBack(0.7, 1.35) > 1);
});

test('hitcam: timeline slides in with an overshoot, holds, slides out and ends', () => {
  const s = { openT: 0, contactT: 0.12, closeT: 0.12 + K.holdS };
  assert.equal(hitCamFrame(s, 0, K).slide, 1);                          // off to the left at open
  const mid = hitCamFrame(s, K.enterS * 0.75, K);
  assert.ok(mid.slide < 0, 'swings past the seat');                     // easeOutBack overshoot
  close(hitCamFrame(s, K.enterS + 0.01, K).slide, 0);
  const hold = hitCamFrame(s, 1.5, K);
  assert.equal(hold.visible, true); close(hold.slide, 0);
  assert.ok(hitCamFrame(s, s.closeT + K.exitS * 0.5, K).slide > 0);
  const end = hitCamFrame(s, s.closeT + K.exitS + 0.01, K);
  assert.equal(end.done, true); assert.equal(end.visible, false);
  assert.equal(hitCamFrame(null, 0, K).visible, false);
});

test('hitcam: contact beats decay: crash zoom, flash, shake, glow and the word', () => {
  const s = { openT: 0, contactT: 0.5, closeT: 3 };
  const before = hitCamFrame(s, 0.45, K), at = hitCamFrame(s, 0.5, K), later = hitCamFrame(s, 1.6, K);
  assert.equal(before.flash, 0); assert.equal(before.word.alpha, 0);
  close(at.flash, K.flash); close(at.shake, K.shakeM); close(at.glow, 1);
  assert.ok(at.zoom < before.zoom, 'crash zoom pulls in at contact');
  assert.equal(later.flash, 0); assert.equal(later.shake, 0); assert.equal(later.word.alpha, 0);
  const pop = hitCamFrame(s, 0.5 + 0.1, K).word;
  assert.ok(pop.alpha === 1 && pop.scale > 0.5);
  assert.ok(hitCamFrame(s, 0.5 + 0.14, K).word.scale >= 1);
  // INCOMING (no contact yet): the lens creeps in, no hit beats, the incoming flag is up
  const inc = hitCamFrame({ openT: 0, contactT: null, closeT: 2.5 }, 1.2, K);
  assert.equal(inc.incoming, true); assert.equal(inc.flash, 0); assert.ok(inc.zoom < 1);
});

test('hitcam: Gentle drops flash and shake, calms the zoom and the swing', () => {
  const s = { openT: 0, contactT: 0.5, closeT: 3 };
  const g = hitCamFrame(s, 0.5, K, true), n = hitCamFrame(s, 0.5, K, false);
  assert.equal(g.flash, 0); assert.equal(g.shake, 0);
  assert.ok(1 - g.zoom < 1 - n.zoom);
  assert.ok(hitCamFrame(s, K.enterS * 0.75, K, true).slide >= 0, 'no overshoot');
  assert.ok(g.glow <= 0.5 + 1e-9);
});

test('hitcam: lensZoom creeps, crashes at contact and releases', () => {
  assert.ok(lensZoom(-5, K) === 1);
  assert.ok(lensZoom(-0.1, K) < 1);
  close(lensZoom(0, K), 1 - K.creep - K.punch);
  assert.ok(lensZoom(3, K) > lensZoom(0.1, K));
  close(fovFor(44, 1), 44);
  assert.ok(fovFor(44, 0.8) < 44);
});

test('hitcam: only worth it far away, behind or off screen; the player decides nothing else', () => {
  assert.equal(shouldShow({ distM: 8, behind: false, onScreen: true }, K), false);
  assert.equal(shouldShow({ distM: K.minDistM + 1, behind: false, onScreen: true }, K), true);
  assert.equal(shouldShow({ distM: 3, behind: true, onScreen: true }, K), true);
  assert.equal(shouldShow({ distM: 3, behind: false, onScreen: false }, K), true);
  assert.equal(isPlayerHit({ by: 0, victim: 3, verdict: 'hit' }), true);
  assert.equal(isPlayerHit({ by: 0, victim: 0, verdict: 'hit' }), false);   // your own hit has its own camera kick
  assert.equal(isPlayerHit({ by: 2, victim: 3, verdict: 'hit' }), false);
  assert.equal(isPlayerHit({ by: -1, victim: 3, verdict: 'hit' }), false);  // online: a remote racer's shot
  assert.equal(isPlayerHit({ by: 0, victim: 3, verdict: 'glass' }), false);
  const ok = { on: true, tierScale: 1, racing: true };
  assert.equal(hitCamAllowed(ok), true);
  for (const bad of [{ on: false }, { tierScale: 0 }, { racing: false }, { paused: true }, { card: true }, { finished: true }])
    assert.equal(hitCamAllowed({ ...ok, ...bad }), false, JSON.stringify(bad));
});

test('hitcam: a multi-hit tick shows the farthest victim worth showing', () => {
  const pick = pickVictim([
    { victim: 2, distM: 6, behind: false, onScreen: true },     // plainly on screen: skipped
    { victim: 4, distM: 30, behind: false, onScreen: true },
    { victim: 5, distM: 22, behind: false, onScreen: false },
  ], K);
  assert.equal(pick.victim, 4);
  assert.equal(pickVictim([{ victim: 1, distM: 4, behind: false, onScreen: true }], K), null);
  assert.equal(pickVictim([], K), null);
});

test('hitcam: a word per item kind, deterministic, with a fallback', () => {
  assert.equal(hitWord('pendulum', 0), HIT_WORDS.pendulum[0]);
  assert.equal(hitWord('pendulum', HIT_WORDS.pendulum.length), HIT_WORDS.pendulum[0]);
  assert.ok(HIT_WORDS.other.includes(hitWord('nope', 7)));
  assert.ok(HIT_WORDS.pink.includes(hitWord('pink', -1)));
});

test('hitcam: the inset rect sits top left, 16:9, clear of the HUD blocks', () => {
  const d = hitCamRect(1280, 720);
  assert.equal(d.phone, false); assert.equal(d.x, 16);
  assert.ok(d.y >= 80 && d.y <= 100, 'under the lap and time block');
  assert.ok(d.w >= 220 && d.w <= 320); close(d.w / d.h, 16 / 9, 0.02);
  const p = hitCamRect(390, 844);                      // portrait phone: under the moved minimap
  assert.equal(p.phone, true); assert.ok(p.y >= 150 && p.w <= 176);
  const l = hitCamRect(844, 390);                      // phone on its side
  assert.equal(l.phone, true); assert.ok(l.y + l.h <= 390 * 0.62 + 1);
  assert.equal(hitCamRect(1280, 720, { left: 40, top: 30 }).x, 40);
  assert.ok(hitCamRect(1280, 720, { top: 30 }).y > d.y);
});

test('hitcam: the rig sits low behind and to one side, orbits, aims ahead', () => {
  const fwd = [0, 0, -1], up = [0, 1, 0];
  const o = rigOffsets(fwd, up, 0, K);
  close(o.eye[2], K.backM); close(o.eye[1], K.upM); close(o.eye[0], 0);
  close(o.look[2], -K.aheadM); close(o.look[1], K.lookUpM);
  const side = rigOffsets(fwd, up, orbitAz(0, 1, K), K);
  assert.ok(side.eye[0] > 0 && side.eye[2] > 0, 'right and behind');
  close(Math.hypot(side.eye[0], side.eye[2]), K.backM);
  assert.ok(orbitAz(2, 1, K) > orbitAz(0, 1, K));
  close(orbitAz(100, -1, K), -(K.sideDeg + K.orbitMaxDeg) * Math.PI / 180);
  // a pitched kart keeps the seat in its own plane
  const tilt = rigOffsets([0, 0.5, -1], up, 0, K);
  close(tilt.eye[1], K.upM);
  assert.deepEqual(shakeAt(0.1, 0), [0, 0]);
  assert.ok(Math.hypot(...shakeAt(0.1, 0.2)) <= 0.2 * Math.SQRT2 + 1e-9);
});
