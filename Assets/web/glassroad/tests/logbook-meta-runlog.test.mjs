import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createBus } from '../core/events.js';
import { S, createState } from '../sim/state.js';
import { createRunLog, roomResult } from '../meta/runLog.js';

const KEYS = ['v', 'track', 'mode', 'online', 'laps', 'seed', 'assist', 'gentle', 'finished', 'place', 'of', 'timeS', 'bestLapS',
  'lapTimesS', 'sectorsS', 'placeAtLap', 'ledEveryLap', 'leadS', 'topSpeedPct', 'distanceM', 'driftStarts', 'driftBoosts',
  'slipstreams', 'pads', 'airS', 'longestAirS', 'jumps', 'tricks', 'bestTrick', 'overtakes', 'walls', 'falls', 'rescues',
  'fallsAtS', 'hitsTaken', 'itemsUsed', 'itemsLanded', 'tetherBreaks', 'discBankHits', 'bigSpiralMax', 'traps',
  'mirrorsBroken', 'mirrorStations', 'wordsCaught', 'wordsOffered', 'songGatesPassed', 'songGatesOffered', 'cleanLaps', 'at'];

const result = (place = 1, timeS = 120, of = 3, second = 120.1) => ({ rows: [
  { place, me: true, timeS, bestLapS: 39, finished: true },
  ...Array.from({ length: of - 1 }, (_, k) => ({ place: k + (place === 1 ? 2 : 1), me: false, timeS: second + k, bestLapS: 40, finished: true })),
], me: { place, timeS, bestLapS: 39 } });

function drive(log, st, L, place, fromS, toS, seconds) {
  const frames = Math.round(seconds * 60);
  for (let i = 1; i <= frames; i++) {
    st[S.s] = (fromS + ((toS - fromS) * i) / frames) % L;
    st[S.speedPct] = 0.9 + (i === frames ? 0.2 : 0);
    log.sample(st, place, 1 / 60);
  }
}

test('runLog: every RunSummary v1 field is present with zeros on an empty run', () => {
  const bus = createBus(), log = createRunLog(bus, { track: 'keep', laps: 3, seed: 7, lengthM: 900 });
  const s = log.finish(null);
  for (const k of KEYS) assert.ok(k in s, k);
  assert.equal(s.v, 1); assert.equal(s.finished, false); assert.equal(s.place, 0); assert.equal(s.timeS, 0);
  assert.deepEqual(s.driftBoosts, [0, 0, 0]); assert.equal(s.bestTrick, null); assert.equal(s.assist, 'light');
  assert.deepEqual(Object.values(s.traps), [0, 0, 0, 0, 0, 0, 0]);
});

test('runLog: player events count, bots and other karts do not', () => {
  const bus = createBus(), log = createRunLog(bus, { track: 'boudoir', mode: 'race', laps: 2, lengthM: 900, assist: 'off' });
  bus.emit('drift-start', {}); bus.emit('drift-boost', { tier: 3 }); bus.emit('drift-boost', { tier: 0 }); bus.emit('drift-boost', { tier: 1 });
  bus.emit('racer-event', { racer: 2, name: 'drift-boost', p: { tier: 3 } });
  bus.emit('slipstream', { racer: 0, state: 'fire' }); bus.emit('slipstream', { racer: 4, state: 'fire' }); bus.emit('slipstream', { racer: 0, state: 'in' });
  bus.emit('land', { airTime: 2.4 }); bus.emit('land', { airTime: 0.1 }); bus.emit('land', { airTime: 1 });
  bus.emit('trick', { grade: 'good' }); bus.emit('trick-land', { grade: 'great' });
  bus.emit('overtake', { racer: 0, passed: 3 }); bus.emit('overtake', { racer: 3, passed: 0 });
  bus.emit('item-fire', { racer: 0, id: 'disc' }); bus.emit('disc-bounce', { by: 0 }); bus.emit('item-hit', { victim: 2, by: 0, kind: 'disc', verdict: 'hit' });
  bus.emit('item-fire', { racer: 0, id: 'bigSpiral' });
  for (const v of [1, 2, 3]) bus.emit('item-hit', { victim: v, by: 0, kind: 'bigSpiral', verdict: 'hit' });
  bus.emit('item-hit', { victim: 4, by: 0, kind: 'flash', verdict: 'mercy' });
  bus.emit('item-fire', { racer: 5, id: 'disc' });
  bus.emit('tether-break', { by: 0, target: 2, reason: 'pass' }); bus.emit('tether-break', { by: 0, target: 2, reason: 'range' });
  bus.emit('scan-hit', { racer: 0 }); bus.emit('scan-hit', { racer: 3 }); bus.emit('keep-chain-hit', { id: 'c1' });
  bus.emit('boudoir-mirror-break', { id: 'mirror-shutter-0-1', racer: 0 }); bus.emit('boudoir-mirror-break', { id: 'mirror-shutter-0-2', racer: 0 });
  bus.emit('boudoir-mirror-break', { id: 'mirror-shutter-2-0', racer: 0 }); bus.emit('boudoir-mirror-break', { id: 'mirror-shutter-1-0', racer: 6 });
  bus.emit('word-pop', {}); bus.emit('word-pop', {});
  bus.emit('song-gate', { id: 'song-0', passed: true }); bus.emit('song-gate', { id: 'song-1', passed: false });
  bus.emit('pad', { index: 1 }); bus.emit('rescue-release', {});
  const s = log.finish(result(1, 100, 3, 100.15));
  assert.equal(s.driftStarts, 1); assert.deepEqual(s.driftBoosts, [1, 0, 1]);
  assert.equal(s.slipstreams, 1); assert.equal(s.jumps, 2); assert.equal(s.airS, 3.4); assert.equal(s.longestAirS, 2.4);
  assert.equal(s.tricks, 1); assert.equal(s.bestTrick, 'great'); assert.equal(s.overtakes, 1);
  assert.equal(s.itemsUsed, 2); assert.equal(s.itemsLanded, 4); assert.equal(s.discBankHits, 1); assert.equal(s.bigSpiralMax, 3);
  assert.equal(s.tetherBreaks, 1); assert.equal(s.traps.scan, 1); assert.equal(s.traps.keepChain, 1);
  assert.equal(s.mirrorsBroken, 3); assert.equal(s.mirrorStations, 2);
  assert.equal(s.wordsCaught, 2); assert.equal(s.wordsOffered, 2); assert.equal(s.songGatesPassed, 1); assert.equal(s.songGatesOffered, 2);
  assert.equal(s.pads, 1); assert.equal(s.rescues, 1); assert.equal(s.assist, 'off');
  assert.equal(s.finished, true); assert.equal(s.place, 1); assert.equal(s.of, 3); assert.equal(s.winMarginS, 0.15);
});

test('runLog: samples give laps, sectors, places, lead, distance, top speed and clean laps', () => {
  const bus = createBus(), L = 900, st = createState();
  const log = createRunLog(bus, { track: 'threshold', laps: 2, lengthM: L, offered: () => 5 });
  st[S.s] = 0; log.sample(st, 2, 1 / 60);
  drive(log, st, L, 2, 0, 899, 30);
  bus.emit('lap', { lap: 1, lapTimeS: 30, timeS: 30 });
  bus.emit('wall', { dv: 4 });
  bus.emit('drop', {});
  drive(log, st, L, 1, 0, 899, 28);
  bus.emit('lap', { lap: 2, lapTimeS: 28, timeS: 58 });
  bus.emit('word-pop', {});
  const s = log.finish(result(1, 58, 3));
  assert.deepEqual(s.lapTimesS, [30, 28]); assert.deepEqual(s.placeAtLap, [2, 1]); assert.equal(s.ledEveryLap, false);
  assert.equal(s.sectorsS.length, 2);
  for (const lap of s.sectorsS) { assert.equal(lap.length, 3); for (const x of lap) assert.ok(x > 8 && x < 12, String(lap)); }
  assert.ok(Math.abs(s.leadS - 28) < 0.2, String(s.leadS));
  assert.ok(s.distanceM > 1700 && s.distanceM < 1800, String(s.distanceM));
  assert.equal(s.topSpeedPct, 1.1); assert.equal(s.cleanLaps, 1); assert.equal(s.walls, 1); assert.equal(s.falls, 1);
  assert.equal(s.fallsAtS.length, 1); assert.equal(s.wordsOffered, 5); assert.equal(s.lengthM, 900);
});

test('runLog: an unfinished or trial run, dispose stops counting, fallsAtS caps at 20', () => {
  const bus = createBus(), log = createRunLog(bus, { track: 'keep', mode: 'trial', lengthM: 500 });
  for (let i = 0; i < 25; i++) bus.emit('drop', {});
  const t = log.finish(result(1, 99, 1));
  assert.equal(t.mode, 'trial'); assert.equal(t.place, 1); assert.equal(t.of, 1); assert.equal(t.falls, 25); assert.equal(t.fallsAtS.length, 20);
  log.dispose(); bus.emit('drop', {});
  assert.equal(log.finish(null).falls, 25);
  assert.equal(log.finish(null).finished, false);
});

test('runLog: roomResult maps the room rows to the player', () => {
  const r = roomResult([{ place: 2, seat: 3, finished: true, timeMs: 61000, bestLapMs: 20000 }, { place: 1, seat: 1, finished: true, timeMs: 60000 }], 3);
  assert.deepEqual(r.me, { place: 2, timeS: 61, bestLapS: 20 });
  const bus = createBus(), s = createRunLog(bus, { track: 'keep', online: true }).finish(r);
  assert.equal(s.place, 2); assert.equal(s.of, 2); assert.equal(s.online, true); assert.equal(s.finished, true); assert.equal(s.winMarginS, 0);
});
