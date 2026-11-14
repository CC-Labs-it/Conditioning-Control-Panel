/* tests/net-frozen-rivals.test.mjs - owner's phone vs PC race (2026-10-30): "I'm always first; after a few seconds
 * the other players, bots included, get stuck" (screenshot from the PC).
 *  - Not the cause: a steadily late stream keeps moving (dead reckoning + the 150 ms blend). Kept as a guard so nobody
 *    "fixes" interp.js for it. A sim-step stamp that lost the room clock WOULD be such a stream: recordTick re-anchors.
 *  - A fresh session rejoining mid-race restarted the uplink seq at 0 and the room dropped all its records until the
 *    seq passed the old one: welcome.resume now continues it (net/room.js).
 *  - A fresh keeper with no record of a bot leaves it remote until a batch carries one (it drove it from the grid).
 *  - A phone says 'phone' to the lobby, so the room never makes it the bot keeper over a desktop (net/lobbyApi.js). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createInterp } from '../net/interp.js';
import { createRoomSession } from '../net/room.js';
import { createLobbyApi } from '../net/lobbyApi.js';
import { BIN, NET_V, HEADER_BYTES, KART_BYTES, createKart, encodeFrame, decodeFrame, toHex } from '../net/protocol.js';
import { S, createState } from '../sim/state.js';
import { createRacers } from '../sim/racers.js';
import { threshold } from './sim-racers-harness.mjs';
import { makeFeel } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const SPEED = 30, GAP_MS = 1000 / 15;   // a phone uploads at 15 Hz

/** A sender driving at SPEED whose stamps are `lateMs` older than their arrival; frames drawn every 16 ms for 3 s.
 * A healthy seat stands still for one 16 ms frame at most; a held seat stands for whole record gaps.
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

test('a fresh session resumes past seq 0x8000 (a race longer than ~27 min at 20 Hz)', async () => {
  const frame = encodeFrame(BIN.UPLOAD, 0, [{ ...createKart(), seat: 0, seq: 40000, tick: 3000, s: 120, grounded: true }]);
  const r = await session({ resume: toHex(frame.slice(HEADER_BYTES, HEADER_BYTES + KART_BYTES)) });
  assert.equal(r.s.uplink.seqOf(0), 40001, 'seqNewer(0, 40000) is true past half the ring: the old check never resumed');
});

// ---- the bot keeper after a rejoin -------------------------------------------------------------------------------
const botRow = (seat) => ({ seat, kind: 'bot', name: 'EMI bot', platform: null, look, host: false, ready: true, idle: false, left: false });

/** A mid-race session with one human seat and bot seat 2, racers mounted. */
async function botSession(keep) {
  const r = await session({ seats: [seatRow(0), botRow(2)], keep });
  const q = r.s.tap({ emit() {} });
  const racers = createRacers(threshold(), makeFeel(), q, { net: r.s.layout(), seed: 7 });
  r.s.mount(racers, { phase: 'racing', timeS: 40, lapStartS: 0, finish() {} }, q);
  return Object.assign(r, { racers, bot: racers.list.find((x) => x.meta.seat === 2) });   // keeps the t accessor
}
const botRecord = (s, seq) => encodeFrame(BIN.BATCH, 0, [Object.assign(createKart(), { seat: 2, seq, tick: s.clock.tick() - 3, s: 640, x: 1.2,
  lap: 1, checkpoint: 2, bot: true, q: [0, 0, 0, 1], grounded: true, speedPct: 0.8 })]);
const upSeats = (ws) => decodeFrame(ws.sent.filter((d) => typeof d !== 'string').at(-1)).karts.map((k) => [k.seat, k.seq]);

// ---- record stamps: "they race but you see them teleporting 10cm at the time" (owner 2026-10-30) -------------------
/** A racing session; upload() steps the wall clock and the sim, and returns the tick the record carried. */
async function stamping() {
  const r = await session();
  const q = r.s.tap({ emit() {} }), race = { phase: 'racing', timeS: 40, lapStartS: 0, finish() {} };
  const racers = createRacers(threshold(), makeFeel(), q, { net: r.s.layout(), seed: 7 });
  r.s.mount(racers, race, q);
  const upload = (wallMs, steps) => { r.t += wallMs; race.timeS += steps / 60; r.s.update(); return decodeFrame(r.ws.sent.filter((d) => typeof d !== 'string').at(-1)).karts[0].tick; };
  return Object.assign(r, { race, upload });
}

test('while racing a record is stamped by sim steps: frame phase on the wall clock never reaches a rival', async () => {
  const r = await stamping();
  let last = r.upload(60, 3);
  for (const [wall, steps] of [[52, 3], [50.5, 3], [51, 3], [55, 3], [50, 3], [53, 3], [50, 3]]) {   // equal travel, uneven wall
    const tick = r.upload(wall, steps);
    assert.equal(tick - last, steps, 'one tick here is 67 cm at 40 m/s on every rival\'s screen');
    last = tick;
  }
});

test('dropped sim time re-anchors the stamp to the room clock, so rivals never run out of records', async () => {
  const r = await stamping();
  r.upload(60, 3);
  const tick = r.upload(600, 6);                            // 600 ms of wall, 100 ms of sim
  assert.ok(Math.abs(tick - r.s.clock.tick(r.t)) <= 2, `stamp ${tick} against room tick ${r.s.clock.tick(r.t)}`);
});

test('outside the race the stamp is the room clock (a frozen timeS at the finish never freezes the stamp)', async () => {
  const r = await stamping();
  r.upload(60, 3);
  r.race.phase = 'finished';
  const a = r.upload(100, 0), b = r.upload(100, 0);
  assert.equal(b - a, 6);
  assert.equal(b, r.s.clock.tick(r.t));
});

test('the room sends the bot record before keep: the new keeper seeds position, lap and seq + 1', async () => {
  const r = await botSession([]);
  assert.equal(r.bot.kind, 'remote');
  r.ws.bin(botRecord(r.s, 300));
  r.ws.recv({ t: 'keep', seats: [2] });
  assert.equal(r.bot.kind, 'local');
  assert.ok(Math.abs(r.bot.st[S.s] - 640) < 1e-3, 'seeded where the room last saw it, not on the grid');
  assert.equal(r.bot.st[S.lap], 1);
  assert.equal(r.s.uplink.seqOf(2), 301);
  r.t += 100; r.s.update();
  assert.deepEqual(upSeats(r.ws).find((x) => x[0] === 2), [2, 301]);
});

test('keep with no record leaves the bot remote until a batch carries it, then seeds it', async () => {
  const r = await botSession([2]);
  assert.equal(r.bot.kind, 'remote', 'driving it from the grid froze it at the start line on every screen');
  r.t += 100; r.s.update();
  assert.deepEqual(upSeats(r.ws).map((x) => x[0]), [0], 'nothing uploaded for a bot it cannot place');
  r.ws.bin(botRecord(r.s, 1200));
  assert.equal(r.bot.kind, 'local');
  assert.ok(Math.abs(r.bot.st[S.s] - 640) < 1e-3);
  assert.equal(r.bot.st[S.lap], 1);
  assert.equal(r.s.uplink.seqOf(2), 1201);
  r.t += 100; r.s.update();
  assert.deepEqual(upSeats(r.ws).find((x) => x[0] === 2), [2, 1201]);
});

test('a kept bot whose record never comes is driven from where it stands after a second (an older room)', async () => {
  const r = await botSession([2]);
  r.t += 500; r.s.update();
  assert.equal(r.bot.kind, 'remote');
  r.t += 600; r.s.update();
  assert.equal(r.bot.kind, 'local');
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
