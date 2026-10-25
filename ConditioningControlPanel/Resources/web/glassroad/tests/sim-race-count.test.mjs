/* tests/sim-race-count.test.mjs - polish 5 S3 / S4: race.goInS (the song is offset against go, go never moves)
 * and race.syncCount (online the count's clock follows server time: bells and go on the room's time). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRace } from '../sim/race.js';
import { createState } from '../sim/state.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';

function setup() {
  const feel = makeFeel(), ev = [];
  const race = createRace(null, feel, { emit: (name, p) => ev.push([name, p, race.goInS]) });
  return { feel, race, ev, st: createState(), a: createActions() };
}

test('goInS: the whole count before start, counting down, then minus race time', () => {
  const { feel, race, st, a } = setup();
  const goS = feel.get('juice.countdownPulses') * 60 / feel.get('clock.bpm');
  assert.ok(Math.abs(race.goInS - goS) < 1e-9);
  race.start();
  assert.ok(Math.abs(race.goInS - goS) < 1e-9);
  for (let i = 0; i < 30; i++) race.update(DT, a, st);
  assert.ok(Math.abs(race.goInS - (goS - 30 * DT)) < 1e-6);
  while (race.phase === 'countdown') race.update(DT, a, st);
  for (let i = 0; i < 60; i++) race.update(DT, a, st);
  assert.ok(race.goInS < -0.99 && race.goInS > -1.02, String(race.goInS));
  race.reset();
  assert.ok(Math.abs(race.goInS - goS) < 1e-9);
});

test('go never moves: the same steps reach go at the same tick with or without the song', () => {
  const ticks = [];
  for (let k = 0; k < 2; k++) {
    const { race, st, a } = setup();
    race.start();
    let n = 0;
    while (race.phase === 'countdown') { race.update(DT, a, st); n++; void race.goInS; }
    ticks.push(n);
  }
  assert.equal(ticks[0], ticks[1]);
});

test('syncCount: an outside clock carries the count; pulses never repeat, go fires once on time', () => {
  const { feel, race, ev, st, a } = setup();
  const n = feel.get('juice.countdownPulses'), beat = 60 / feel.get('clock.bpm');
  race.syncCount(1);                                   // not counting: ignored
  assert.equal(race.phase, 'ready');
  race.start();
  // a slow frame: the outside clock says 1.5 beats went by, the sim stepped one tick
  race.syncCount(1.5 * beat - DT);
  race.update(DT, a, st);
  assert.deepEqual(ev.filter((e) => e[0] === 'countdown').map((e) => e[1].n), [n, n - 1]);
  // a clock a little behind never re-fires a pulse
  race.syncCount(0.5 * beat);
  race.update(DT, a, st);
  assert.equal(ev.filter((e) => e[0] === 'countdown').length, 2);
  race.syncCount(n * beat - DT);
  race.update(DT, a, st);
  assert.equal(race.phase, 'racing');
  assert.equal(ev.filter((e) => e[0] === 'go').length, 1);
  assert.equal(ev.filter((e) => e[0] === 'countdown').length, n);
  race.syncCount(99);                                  // racing: ignored
  assert.ok(race.timeS < 0.05);
  race.syncCount(-3);
});

test('catching up after GO cannot turn a late throttle press into a launch boost', () => {
  for (const lateS of [.5, 3]) {
    const { feel, race, ev, st, a } = setup();
    race.start();
    race.syncCount(feel.get('juice.countdownPulses') * 60 / feel.get('clock.bpm') + lateS);
    a.throttle = 1;
    race.update(DT, a, st);
    assert.equal(race.phase, 'racing');
    assert.equal(ev.filter(e => e[0] === 'go').length, 1);
    assert.equal(ev.filter(e => e[0] === 'launch-boost').length, 0, 'press arrived after GO');
  }
});
