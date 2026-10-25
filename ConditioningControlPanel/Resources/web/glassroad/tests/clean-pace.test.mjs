import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cleanPace, resetCleanPace, songDrive } from '../sim/cleanPace.js';
import { S, RESPAWN, createState } from '../sim/state.js';
import { createRacers } from '../sim/racers.js';
import { createContact } from '../sim/contact.js';
import { createActions } from '../core/actions.js';
import { makeFeel, fakeTrack, DT } from './sim-fake.mjs';

const hands = { throttle: 1, brake: 0 };
const run = (st, feel, seconds, drive = 0, act = hands) => {
  let value = 1;
  for (let t = 0; t < seconds; t += DT) value = cleanPace(st, act, DT, feel, { enabled: true, drive });
  return value;
};
const moving = () => { const st = createState(); st[S.engineV] = 30; return st; };

test('clean pace grows gradually, remains bounded, and faster music raises its ceiling', () => {
  const feel = makeFeel(), quiet = moving(), loud = moving();
  assert.ok(run(quiet, feel, 1) < 1.03);
  assert.ok(run(quiet, feel, 15) > 1.29);
  const fast = run(loud, feel, 20, 1);
  assert.ok(fast > 1.55 && fast <= 1.56);
  assert.ok(Math.abs(run(loud, feel, 20, 1) - 1.56) < 1e-8);
});

test('bumps reset the clean streak and hold its rebuild; brake, stall and rescue cannot farm pace', () => {
  const feel = makeFeel(), st = moving();
  run(st, feel, 15, 1); resetCleanPace(st, true);
  assert.equal(run(st, feel, 1, 1), 1);
  assert.ok(run(st, feel, 2, 1) < 1.08);
  run(st, feel, 15, 1);
  assert.equal(run(st, feel, 4, 1, { throttle: 0, brake: 1 }), 1);
  for (const [key, value] of [[S.stalled, 1], [S.respawnPhase, RESPAWN.PULL]]) {
    st[key] = value; assert.equal(run(st, feel, 15, 1), 1); st[key] = 0;
  }
});

test('pause freezes the streak and disabling it clears all carried advantage', () => {
  const feel = makeFeel(), st = moving(); run(st, feel, 6, .5);
  const before = cleanPace(st, hands, 0, feel, { enabled: true });
  for (let i = 0; i < 100; i++) assert.equal(cleanPace(st, hands, 0, feel, { enabled: true, drive: 1 }), before);
  assert.equal(cleanPace(st, hands, DT, feel), 1);
  assert.equal(cleanPace(st, hands, 0, feel, { enabled: true }), 1);
});

test('song drive follows local beat spacing, intensity and decoded-file analysis', () => {
  const song = { bpm: 90, beats: [0, 1, 2, 2.4, 2.8], sections: [], energy: { fps: 1, mix: [30,30,240,240] } };
  assert.ok(songDrive(song, 2.1) > songDrive(song, .1) + .4);
  assert.equal(songDrive(null, 2), 0); assert.equal(songDrive(song, NaN), 0);
  assert.ok(songDrive({ bpm: 140, downbeats: [0, 2], barEnergy: [.01, .25] }, 2.1) > songDrive({ bpm: 140, downbeats: [0, 2], barEnergy: [.01, .25] }, .1));
});

test('solo racer engine reaches the raised ceiling, wall/reset remove it, online never applies it', () => {
  const feel = makeFeel(), track = fakeTrack(), queue = { emit() {} }, actions = Object.assign(createActions(), hands);
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() {} };
  const racers = createRacers(track, feel, queue, { count: 1 });
  for (let i = 0; i < 1200; i++) { race.timeS += DT; racers.step(DT, actions, race, { cleanPace: true, songDrive: 1 }); }
  assert.ok(racers.player.st[S.engineV] > 42, 'real engine uses the higher cap');
  racers.player.events.emit('wall', { dv: 2 });
  racers.step(DT, actions, race, { cleanPace: true, songDrive: 1 });
  assert.equal(racers.player.feel.get('speed.topMps'), 30);
  racers.place(); assert.equal(racers.player.feel.get('speed.topMps'), 30);
  const online = createRacers(track, feel, queue, { net: [{ seat: 0, kind: 'player', local: true }] });
  online.player.st[S.engineV] = 30;
  for (let i = 0; i < 1200; i++) online.step(DT, actions, race, { cleanPace: true, songDrive: 1 });
  assert.equal(online.player.feel.get('speed.topMps'), 30);
});

test('physical racer contact clears both clean streaks, including contacts below cue threshold', () => {
  const feel = makeFeel(), track = fakeTrack(), queue = { emit() {} }, racers = createRacers(track, feel, queue, { count: 2 });
  const [a,b] = racers.list;
  a.st[S.engineV] = b.st[S.engineV] = 30;
  run(a.st, feel, 15, 1); run(b.st, feel, 15, 1);
  b.st[S.posX] = a.st[S.posX]; b.st[S.posY] = a.st[S.posY]; b.st[S.posZ] = a.st[S.posZ] + .5;
  createContact(track, feel, queue).update(DT, racers.list, { phase: 'racing', timeS: 10 });
  assert.equal(run(a.st, feel, .5, 1), 1); assert.equal(run(b.st, feel, .5, 1), 1);
});
