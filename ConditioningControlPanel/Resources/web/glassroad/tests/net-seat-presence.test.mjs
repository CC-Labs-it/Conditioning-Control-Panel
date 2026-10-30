/* tests/net-seat-presence.test.mjs - the owner's first phone race (2026-10-30): neither player saw the other's kart,
 * the friend started about a second late, and nobody was told when someone left.
 *  - A seat the room marked left and then let back in (a reconnect inside the 30 s grace) clears `left` with a whole
 *    `seats` list, never a `seat` message: the racer must follow it, or its kart stays hidden all race (scene.js hides
 *    a racer whose meta.left is set). A record relayed for a seat marked left also brings it back.
 *  - The kart sim catches up with the room's clock after a slow frame instead of dropping the time (net/catchUp.js).
 *  - '<name> left' for a racer who leaves or drops, in the room and in the race. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRoomSession } from '../net/room.js';
import { encodeFrame, createKart, BIN, NET_V } from '../net/protocol.js';
import { catchUpSteps, CATCH_UP_STEPS } from '../net/catchUp.js';
import { seatsGone, goneLine, applySeats, applySeat } from '../hud/lobbyMath.js';
import { createRacers } from '../sim/racers.js';
import { threshold } from './sim-racers-harness.mjs';
import { makeFeel } from './sim-fake.mjs';

const track = threshold();
const look = { outfit: null, vehicle: 'emi-teacup-kart' };
const CFG = { track: 'threshold', laps: 3, seats: 2, mode: 'friends', laneKeeper: false, staked: false, botFill: false, autoStartS: 0 };
const seatRow = (seat, more = {}) => ({ seat, kind: 'human', name: seat ? 'Friend' : 'Owner', platform: 'phone', look, host: !seat,
  ready: true, idle: false, left: false, ...more });

async function race2() {
  let t = 100_000;
  const sockets = [];
  class FakeWS {
    constructor(url) { this.url = url; this.sent = []; sockets.push(this); }
    send(d) { this.sent.push(d); }
    close(code = 1000) { this.onclose && this.onclose({ code }); }
    recv(m) { this.onmessage({ data: JSON.stringify(m) }); }
    bin(u8) { this.onmessage({ data: u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength) }); }
  }
  const s = createRoomSession({ track, feel: makeFeel(), platform: 'phone', WS: FakeWS, now: () => t,
    timers: { setTimeout: () => 1, clearTimeout() {} } });
  const p = s.connect({ roomId: 'abcdefabcdef', ticket: 'T0', wsUrl: 'ws://x/room/abcdefabcdef', seat: 0 });
  const ws = sockets[0];
  ws.onopen();
  ws.recv({ t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat: 0, t0: t - 10_000, serverMs: t, phase: 'lobby', cfg: CFG, seed: 7,
    seats: [seatRow(0), seatRow(1)], keep: [] });
  await p;
  ws.recv({ t: 'start', countAt: t + 100, startAt: t + 3100, goTick: 780, seed: 7 });
  const q = s.tap({ emit() {} });
  const racers = createRacers(track, makeFeel(), q, { net: s.layout(), seed: 7 });
  s.mount(racers, { phase: 'racing', timeS: 0, lapStartS: 0, finish() {} }, q);
  t += 4000;
  const friend = racers.list.find((r) => r.meta.seat === 1);
  const record = (seq) => ws.bin(encodeFrame(BIN.BATCH, t - 90_000, [{ ...createKart(), seat: 1, seq, tick: Math.floor((t - 90_000) * 60 / 1000), s: 40, grounded: true }]));
  return { s, ws, racers, friend, record, get t() { return t; } };
}

test('a seat back from a reconnect (a seats list) shows its kart again', async () => {
  const r = await race2();
  assert.equal(r.friend.kind, 'remote');
  assert.equal(r.friend.meta.left, false);
  r.ws.recv({ t: 'seat', seat: 1, left: true, reason: 'gone' });
  assert.equal(r.friend.meta.left, true, 'a dropped seat is hidden');
  // the room's rejoin answer to everyone else: a whole seats list with left cleared (rooms/src/room.js hello)
  r.ws.recv({ t: 'seats', seats: [seatRow(0), seatRow(1)] });
  assert.equal(r.friend.meta.left, false, 'the rejoined seat is drawn again');
});

test('a record relayed for a seat marked left brings its kart back', async () => {
  const r = await race2();
  r.ws.recv({ t: 'seat', seat: 1, left: true, reason: 'gone' });
  r.record(5);
  assert.equal(r.friend.meta.left, false);
  assert.ok(r.s.interp.has(1));
});

test('a seats list that marks a seat left or empties it hides that kart; idle and paused follow too', async () => {
  const r = await race2();
  r.ws.recv({ t: 'seats', seats: [seatRow(0), seatRow(1, { left: true })] });
  assert.equal(r.friend.meta.left, true);
  r.ws.recv({ t: 'seats', seats: [seatRow(0), seatRow(1, { paused: true })] });
  assert.equal(r.friend.meta.left, false);
  assert.equal(r.friend.meta.paused, true);
  r.ws.recv({ t: 'seats', seats: [seatRow(0), { seat: 1, kind: 'empty', name: '', platform: null, look, host: false, ready: false, idle: false, left: false }] });
  assert.equal(r.friend.meta.left, true);
  assert.equal(r.racers.player.meta.left, false, 'your own kart is never hidden');
});

test('a reconnect welcome reconciles the racers with the room seats', async () => {
  const r = await race2();
  r.ws.recv({ t: 'seat', seat: 1, left: true, reason: 'gone' });
  r.ws.recv({ t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat: 0, t0: r.t - 14_000, serverMs: r.t, phase: 'racing', cfg: CFG, seed: 7,
    seats: [seatRow(0), seatRow(1)], keep: [], countAt: r.t - 3900, startAt: r.t - 900, goTick: 780 });
  assert.equal(r.friend.meta.left, false);
});

test('catch up: a slow frame owes steps, a few per frame, none when level', () => {
  const dt = 1 / 60;
  assert.equal(catchUpSteps(0, 2, dt), 0);
  assert.equal(catchUpSteps(2 * dt, 2, dt), 0, 'a frame that already runs what it owes adds nothing');
  assert.equal(catchUpSteps(-0.5, 2, dt), 0, 'ahead of the room: never slows down');
  assert.equal(catchUpSteps(5 * dt, 2, dt), 3);
  assert.equal(catchUpSteps(1, 2, dt), CATCH_UP_STEPS, 'a 1 s GO hitch is paid back over a few frames');
  assert.equal(catchUpSteps(NaN, 2, dt), 0);
  // a 1 s hitch is level again within half a second of 60 Hz frames
  let behind = 1, frames = 0;
  while (behind > dt && frames < 100) { const n = 1 + catchUpSteps(behind + dt, 1, dt); behind += dt - n * dt; frames++; }
  assert.ok(frames <= 30, `caught up in ${frames} frames`);
  // a phone at 24 fps (2-step cap): the extra steps keep it level instead of losing 8 ms every frame
  behind = 0;
  for (let f = 0; f < 240; f++) { behind += 1 / 24; const own = 2; behind -= (own + catchUpSteps(behind, own, dt)) * dt; }
  assert.ok(behind < 2 * dt, `24 fps phone stays level (behind ${behind.toFixed(3)} s)`);
});

test('boot runs the catch-up online and keeps the race clock on simulated time', () => {
  const src = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.match(src, /simSteps \+= catchUpSteps\(behind, steps, clock\.dt\)/);
  assert.match(src, /for \(let i = 0; i < simSteps; i\+\+\)/);
  assert.match(src, /online\.simS \+= clock\.dt;/);
  assert.match(src, /race\.timeS = online\.simS/);
  assert.match(src, /hud\.on\('online-gone'/);
});

test('seatsGone: a human who left or whose seat emptied, never me, never twice', () => {
  const was = applySeats([seatRow(0), seatRow(1), { seat: 2, kind: 'bot', name: 'EMI bot', look }]);
  assert.deepEqual(seatsGone(was, applySeat(was, { seat: 1, left: true }), 0), [{ seat: 1, name: 'Friend' }]);
  assert.deepEqual(seatsGone(was, applySeats([seatRow(0)]), 0), [{ seat: 1, name: 'Friend' }], 'a lobby leave empties the seat');
  assert.deepEqual(seatsGone(was, applySeat(was, { seat: 0, left: true }), 0), [], 'never your own seat');
  const left = applySeat(was, { seat: 1, left: true });
  assert.deepEqual(seatsGone(left, applySeats([seatRow(0)]), 0), [], 'a seat already gone is not said again');
  assert.deepEqual(seatsGone(was, applySeats([seatRow(0), seatRow(1)]), 0), []);
  assert.equal(goneLine('Friend'), 'Friend left');
  assert.doesNotMatch(goneLine('Friend'), /[!–—]/);
});

test('the lobby says who left while the room waits and tells the race', () => {
  const src = readFileSync(new URL('../hud/lobby.js', import.meta.url), 'utf8');
  assert.match(src, /emit\('online-gone', g\)/);
  assert.match(src, /seatGone\(was\); seatsChanged\(\)/);
  assert.match(src, /seatGone\(was\); seatsChanged\(m\.seat\)/);
});
