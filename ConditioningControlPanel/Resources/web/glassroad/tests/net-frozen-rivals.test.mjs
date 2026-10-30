/* tests/net-frozen-rivals.test.mjs - owner's phone vs PC race (2026-10-30): "I'm always first; after a few seconds
 * the other players, bots included, get stuck" (screenshot from the PC).
 *  - Not the cause: records are stamped with the sender's server-clock tick at upload (snapshot build gets
 *    clock.tick(now), not the sim tick), and a steadily late stream keeps moving (dead reckoning + the 150 ms blend).
 *    Kept as a guard so nobody "fixes" interp.js for it.
 *  - A fresh session rejoining mid-race restarted the uplink seq at 0 and the room dropped all its records until the
 *    seq passed the old one: welcome.resume now continues it (net/room.js).
 *  - A phone says 'phone' to the lobby, so the room never makes it the bot keeper over a desktop (net/lobbyApi.js). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createInterp } from '../net/interp.js';
import { createRoomSession } from '../net/room.js';
import { createLobbyApi } from '../net/lobbyApi.js';
import { BIN, NET_V, HEADER_BYTES, KART_BYTES, createKart, encodeFrame, toHex } from '../net/protocol.js';
import { S, createState } from '../sim/state.js';
import { threshold } from './sim-racers-harness.mjs';
import { makeFeel } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const SPEED = 30, GAP_MS = 1000 / 15;   // a phone uploads at 15 Hz

/** A sender driving at SPEED whose stamps are `lateMs` older than their arrival; frames drawn every 16 ms for 3 s.
 * A new record shows the previous pose for one frame (the blend starts where the kart was shown), so a healthy seat
 * stands still for one 16 ms frame at most; a held seat stands for whole record gaps.
 * @returns the longest time (ms) after the first second that the drawn kart did not move */
function drive(lateMs) {
  const interp = createInterp(track), out = createState();
  let next = 0, seq = 0, prevS = null, run = 0, still = 0;
  for (let now = 0; now <= 3000; now += 16) {
    while (next <= now) {                                   // the record sent at sender time next - lateMs arrives now
      const sent = next - lateMs;
      const k = Object.assign(createKart(), { seat: 1, seq: seq++ & 0xffff, s: 100 + SPEED * sent / 1000, x: 0, h: 0, grounded: true });
      interp.push(k, sent);
      next += GAP_MS;
    }
    interp.sample(1, now, out);
    if (now >= 1000) { run = prevS !== null && Math.abs(out[S.s] - prevS) < 1e-6 ? run + 16 : 0; still = Math.max(still, run); }
    prevS = out[S.s];
  }
  return still;
}

test('a sender stamped 800 ms late, or 30 ms late, still moves on every frame (not the freeze)', () => {
  for (const late of [30, 800]) assert.ok(drive(late) <= 16, `${late} ms late: stood still ${drive(late)} ms`);
});

// ---- the room session --------------------------------------------------------------------------------------------
const look = { outfit: null, vehicle: 'emi-teacup-kart' };
const CFG = { track: 'threshold', laps: 3, seats: 2, mode: 'friends', laneKeeper: false, staked: false, botFill: false, autoStartS: 0 };
const seatRow = (seat) => ({ seat, kind: 'human', name: seat ? 'Friend' : 'Owner', platform: 'desktop', look, host: !seat,
  ready: true, idle: false, left: false });

async function session(welcomeMore = {}) {
  let t = 200_000;
  const sockets = [];
  class FakeWS {
    constructor(url) { this.url = url; this.sent = []; sockets.push(this); }
    send(d) { this.sent.push(d); }
    close(code = 1000) { this.onclose && this.onclose({ code }); }
    recv(m) { this.onmessage({ data: JSON.stringify(m) }); }
    bin(u8) { this.onmessage({ data: u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength) }); }
  }
  const s = createRoomSession({ track: threshold(), feel: makeFeel(), platform: 'desktop', WS: FakeWS, now: () => t,
    timers: { setTimeout: () => 1, clearTimeout() {} } });
  const p = s.connect({ roomId: 'abcdefabcdef', ticket: 'T0', wsUrl: 'ws://x/room/abcdefabcdef', seat: 0 });
  const ws = sockets[0];
  ws.onopen();
  ws.recv({ t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat: 0, t0: t - 60_000, serverMs: t, phase: 'racing', cfg: CFG, seed: 7,
    seats: [seatRow(0), seatRow(1)], keep: [], countAt: t - 40_000, startAt: t - 37_000, goTick: 1380, ...welcomeMore });
  await p;
  return { s, ws, get t() { return t; }, set t(v) { t = v; } };
}

test('a fresh session rejoining mid-race continues its seq past the room\'s last record (welcome.resume)', async () => {
  const frame = encodeFrame(BIN.UPLOAD, 0, [{ ...createKart(), seat: 0, seq: 900, tick: 3000, s: 120, grounded: true }]);
  const rec = frame.slice(HEADER_BYTES, HEADER_BYTES + KART_BYTES);
  const r = await session({ resume: toHex(rec) });
  assert.equal(r.s.uplink.seqOf(0), 901, 'the room drops any seq not newer than 900: the kart froze for the whole gap');
});

test('a reconnect in the same session never moves its seq back', async () => {
  const frame = encodeFrame(BIN.UPLOAD, 0, [{ ...createKart(), seat: 0, seq: 5, tick: 3000, s: 120, grounded: true }]);
  const r = await session({ resume: toHex(frame.slice(HEADER_BYTES, HEADER_BYTES + KART_BYTES)) });
  r.s.uplink.seqFrom(0, 40);   // the session sent 41 records since
  r.ws.recv({ t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat: 0, t0: r.t - 60_000, serverMs: r.t, phase: 'racing', cfg: CFG, seed: 7,
    seats: [seatRow(0), seatRow(1)], keep: [], countAt: r.t - 40_000, startAt: r.t - 37_000, goTick: 1380, resume: toHex(frame.slice(HEADER_BYTES, HEADER_BYTES + KART_BYTES)) });
  assert.equal(r.s.uplink.seqOf(0), 41);
});

// ---- the lobby ---------------------------------------------------------------------------------------------------
test('a phone tells the lobby it is a phone on open, join and quick; desktop and web bodies are unchanged', async () => {
  const seen = [];
  const transport = async (method, path, body) => { seen.push({ path, body }); return { status: 200, body: {} }; };
  const phone = createLobbyApi(transport, { platform: 'phone' });
  await phone.join({ roomId: 'abcdefabcdef', look });
  await phone.quick({ track: 'threshold', look });
  await phone.open({ track: 'threshold', mode: 'open', seats: 4 });
  for (const x of seen) assert.equal(x.body.platform, 'phone', x.path);
  seen.length = 0;
  for (const platform of ['desktop', 'web', null]) {
    const api = createLobbyApi(transport, { platform });
    await api.join({ roomId: 'abcdefabcdef', look });
    await api.quick({ look });
  }
  for (const x of seen) assert.equal(x.body.platform, undefined, x.path);
});
