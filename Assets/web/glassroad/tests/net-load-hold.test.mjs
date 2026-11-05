/* tests/net-load-hold.test.mjs - the room's start waits until this page has loaded (owner 2026-10-30): a phone that
 * took longer to load started second with the race already on. A session that loads says so in hello, sends `loaded`
 * once, and says it again on a new socket, because the room counts a new socket as a seat still loading. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRoomSession } from '../net/room.js';
import { NET_V, decodeMsg } from '../net/protocol.js';
import { threshold } from './sim-racers-harness.mjs';
import { makeFeel } from './sim-fake.mjs';

const CFG = { track: 'threshold', laps: 1, seats: 2, mode: 'friends', laneKeeper: false, staked: false, botFill: false, autoStartS: 0 };
const seatRow = (seat) => ({ seat, kind: 'human', name: 'P' + seat, platform: 'web', look: { outfit: 'a', vehicle: 'b' }, host: seat === 0, ready: true, idle: false, left: false });

function make(loads) {
  const sockets = [];
  class FakeWS {
    constructor() { this.sent = []; sockets.push(this); }
    send(d) { this.sent.push(d); }
    close(code = 1000) { this.onclose && this.onclose({ code }); }
    recv(m) { this.onmessage({ data: JSON.stringify(m) }); }
  }
  const lobby = { join: async () => ({ roomId: 'abcdefabcdef', ticket: 'T1', wsUrl: 'ws://x/room/abcdefabcdef', seat: 0 }) };
  const timers = { setTimeout: (fn) => { Promise.resolve().then(fn); return 1; }, clearTimeout() {} };
  const s = createRoomSession({ track: threshold(), feel: makeFeel(), platform: 'web', lobby, loads, WS: FakeWS, now: () => 200_000, timers });
  const welcome = (ws) => ws.recv({ t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat: 0, t0: 100_000, serverMs: 200_000, phase: 'lobby', cfg: CFG, seed: 7, seats: [seatRow(0), seatRow(1)], keep: [] });
  const sent = (ws, t) => ws.sent.filter((d) => typeof d === 'string').map((d) => JSON.parse(d)).filter((m) => m.t === t);
  return { s, sockets, welcome, sent };
}

async function connected(loads) {
  const e = make(loads);
  const p = e.s.connect({ roomId: 'abcdefabcdef', ticket: 'T0', wsUrl: 'ws://x/room/abcdefabcdef', seat: 0 });
  e.sockets[0].onopen();
  e.welcome(e.sockets[0]);
  await p;
  return e;
}

test('a loading page says so in hello, and sends loaded only when it has', async () => {
  const { s, sockets, sent } = await connected(true);
  assert.equal(sent(sockets[0], 'hello')[0].loads, true);
  assert.equal(sent(sockets[0], 'loaded').length, 0);
  s.markLoaded();
  assert.equal(sent(sockets[0], 'loaded').length, 1);
});

test('a page loaded before its welcome is not lost, and a new socket says it again', async () => {
  const { s, sockets, sent, welcome } = await connected(true);
  s.markLoaded();
  sockets[0].close(1006);
  await new Promise((r) => setTimeout(r, 5));
  assert.equal(sockets.length, 2, 'it rejoined');
  sockets[1].onopen();
  welcome(sockets[1]);
  assert.equal(sent(sockets[1], 'loaded').length, 1);
});

test('a session that does not load (node players) never mentions it', async () => {
  const { s, sockets, sent } = await connected(false);
  s.markLoaded();
  assert.equal('loads' in sent(sockets[0], 'hello')[0], false);
  assert.equal(sent(sockets[0], 'loaded').length, 0);
});

test('hold is a message the client understands', () => {
  assert.deepEqual(decodeMsg(JSON.stringify({ t: 'hold', seats: [1, 2] }), 'r2c').seats, [1, 2]);
});
