import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createClock } from '../core/clock.js';
import { createActionMap } from '../core/actions.js';
import { createKeyboard } from '../input/keyboard.js';
import { createGamepad } from '../input/gamepad.js';
import { steerFromDx, createTouch } from '../input/touch.js';
import { createRace } from '../sim/race.js';
import { createProgress } from '../sim/progress.js';
import { S, BOOST } from '../sim/state.js';
import { makeFeel, fakeTrack, place, recorder, acts, DT } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);

test('clock: fixed steps, two substeps at most, no catch-up, beat edges', () => {
  const c = createClock({ hz: 60, maxSubsteps: 2, bpm: 120 });
  assert.equal(c.frame(1000).steps, 0);
  assert.equal(c.frame(1000 + 1000 / 60).steps, 1);
  let f = c.frame(1000 + 1000 / 60 + 25);          // 25 ms: one step, alpha carries the rest
  assert.equal(f.steps, 1); near(f.alpha, (0.025 - 1 / 60) * 60, 1e-3);
  f = c.frame(1000 + 1000 / 60 + 25 + 80);         // 80 ms: two steps, the rest dropped
  assert.equal(f.steps, 2); assert.ok(f.alpha < 1);
  f = c.frame(1000 + 1000 / 60 + 25 + 80 + 500);   // a stall: frameDt capped at 0.1
  assert.equal(f.steps, 2); near(f.frameDt, 0.1, 1e-9);
  assert.equal(c.tick, 6); near(c.simTime, 6 / 60, 1e-9);
  c.pause(true);
  assert.equal(c.frame(5000).steps, 0); assert.equal(c.tick, 6);
  c.pause(false);
  assert.equal(c.frame(9000).steps, 0, 'no catch-up after a pause');
  c.reset(0);
  let beats = 0, t = 0;
  for (let i = 0; i < 45; i++) { t += 1000 / 60; beats += c.frame(t).beats; }
  assert.equal(beats, 1); assert.equal(c.beat.index, 1); near(c.beat.phase, 0.5, 0.01);
  c.setBpm(60); assert.equal(c.beat.bpm, 60);
});

test('action map: steer by magnitude, max, OR, never remapped', () => {
  const src = (o) => ({ poll: (into) => Object.assign(into, o), dispose() {} });
  const map = createActionMap([src({ steer: -0.3, throttle: 1 }), null, src({ steer: 0.8, brake: 0.5, hopPressed: true }), src({ steer: -0.5, hop: true })]);
  const a = map.sample();
  assert.deepEqual(a, { steer: 0.8, throttle: 1, brake: 0.5, hop: true, hopPressed: true, pause: false });
  assert.equal(map.sample(), a, 'same object each call');
});

test('keyboard: WASD/arrows, Shift/Space hop with one edge per press, Escape pause', () => {
  const target = new EventTarget();
  const key = (type, code, extra = {}) => { const e = new Event(type, { cancelable: true }); Object.assign(e, { code }, extra); target.dispatchEvent(e); return e; };
  const kb = createKeyboard({ target }), into = {};
  const d = key('keydown', 'KeyD'); key('keydown', 'ArrowUp'); key('keydown', 'Space'); key('keydown', 'ShiftLeft');
  assert.ok(d.defaultPrevented);
  kb.poll(into);
  assert.equal(into.steer, 1); assert.equal(into.throttle, 1); assert.equal(into.hop, true); assert.equal(into.hopPressed, true);
  kb.poll(into);
  assert.equal(into.hopPressed, false, 'edge latched once');
  key('keydown', 'ArrowLeft'); kb.poll(into); assert.equal(into.steer, 0);
  key('keyup', 'KeyD'); kb.poll(into); assert.equal(into.steer, -1);
  key('keydown', 'Escape'); key('keydown', 'KeyS'); kb.poll(into);
  assert.equal(into.pause, true); assert.equal(into.brake, 1);
  target.dispatchEvent(new Event('blur')); kb.poll(into);
  assert.equal(into.steer, 0); assert.equal(into.hop, false);
  kb.dispose();
});

test('gamepad: standard mapping, dead zone, edges; touch steer curve', () => {
  const pad = { connected: true, mapping: 'standard', axes: [0.58, 0], buttons: Array.from({ length: 17 }, () => ({ value: 0, pressed: false })) };
  const gp = createGamepad({ nav: { getGamepads: () => [null, pad] } }), into = {};
  pad.buttons[7].value = 0.7; pad.buttons[0].pressed = true; pad.buttons[0].value = 1;
  gp.poll(into);
  near(into.steer, 0.5, 1e-9); near(into.throttle, 0.7, 1e-9); assert.equal(into.hopPressed, true);
  gp.poll(into); assert.equal(into.hopPressed, false); assert.equal(into.hop, true);
  pad.axes[0] = -0.1; gp.poll(into); assert.equal(into.steer, 0);
  assert.equal(steerFromDx(-72), -1); assert.equal(steerFromDx(5), 0); near(steerFromDx(39), 0.5, 1e-9);
  assert.equal(createTouch({ root: null }), null);
});

test('race: countdown on the beat, go, launch window', () => {
  const feel = makeFeel(), beat = 60 / feel.get('clock.bpm'), n = feel.get('juice.countdownPulses');
  const goT = n * beat, win = feel.get('boost.launchWindowS');
  const go = (pressAt) => {
    const rec = recorder(), race = createRace(fakeTrack(), feel, rec), st = place(fakeTrack(), {});
    const a = acts();
    race.update(DT, a, st);
    race.start();
    for (let t = DT; race.phase === 'countdown'; t += DT) {
      rec.t = t; a.throttle = pressAt != null && t >= pressAt ? 1 : 0;
      race.update(DT, a, st);
    }
    return { rec, race, st };
  };
  const { rec, race, st } = go(goT - win + 2 * DT);
  assert.deepEqual(rec.all('countdown').map((e) => e.payload.n), [3, 2, 1].slice(0, n));
  near(rec.all('countdown')[1].t, beat, DT);
  near(rec.first('go').t, goT, DT);
  assert.deepEqual(rec.first('launch-boost').payload, { tier: feel.get('boost.launchTier') });
  assert.equal(st[S.boostKind], BOOST.LAUNCH); assert.equal(race.phase, 'racing');
  assert.ok(!go(goT - 0.6).rec.first('launch-boost'), 'held from early: no launch');
  assert.ok(!go(null).rec.first('launch-boost'));
});

test('progress: checkpoints in order, laps, sections, finish', () => {
  const tr = fakeTrack({ laps: 2 }), L = tr.lengthM, rec = recorder(), st = place(tr, { s: L - 4 });
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() { this.phase = 'finished'; } };
  const prog = createProgress(tr, rec);
  prog.reset(st);
  assert.equal(st[S.sectionIdx], 1);
  let at = L - 4;
  const drive = (to) => { while (at < to) { at = Math.min(at + 25, to); st[S.s] = at % L; race.timeS += 1; prog.update(st, race); } };
  drive(L + 50);                                    // over the start line from the grid: no lap
  assert.equal(st[S.lap], 0); assert.equal(rec.all('checkpoint').length, 0);
  drive(L + 25000);
  assert.deepEqual(rec.all('checkpoint').map((e) => e.payload.idx), [1, 2]);
  st[S.s] = 5000; prog.update(st, race);            // a rescue pulls back: nothing counts
  st[S.s] = (at % L); prog.update(st, race);
  assert.equal(rec.all('checkpoint').length, 2);
  drive(2 * L + 10);
  assert.equal(st[S.lap], 1);
  assert.equal(rec.first('lap').payload.lap, 1);
  assert.ok(rec.all('section').length >= 2);
  drive(3 * L + 10);
  assert.equal(st[S.lap], 2);
  assert.ok(rec.first('finish')); assert.equal(race.phase, 'finished');
});
