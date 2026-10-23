/* tests/net-room.test.mjs - the room session against a fake socket (NET.md sections 2-7): hello and welcome,
 * uploads at 20 / 15 Hz, remote karts written by the remote racer kind, reconnect through the lobby, the bot
 * keeper hand-off, item verdicts applied locally, pause flag, lobby transports. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRoomSession } from '../net/room.js';
import { createLobbyApi, hostTransport, fetchTransport } from '../net/lobbyApi.js';
import { encodeFrame, decodeFrame, createKart, BIN, NET_V } from '../net/protocol.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { createStandings } from '../sim/standings.js';
import { S } from '../sim/state.js';
import { threshold, DT } from './sim-racers-harness.mjs';
import { makeFeel } from './sim-fake.mjs';

const track = threshold();
const look = { outfit: null, vehicle: 'emi-teacup-kart' };
const CFG = { track: 'threshold', laps: 3, seats: 4, mode: 'open', laneKeeper: false, staked: false, botFill: true, autoStartS: 45 };
const SEATS = [
  { seat: 0, kind: 'human', name: 'Ana', platform: 'web', look, host: true, ready: true, idle: false, left: false },
  { seat: 1, kind: 'human', name: 'Bo', platform: 'desktop', look, host: false, ready: true, idle: false, left: false },
  { seat: 2, kind: 'bot', name: 'EMI bot', platform: null, look, host: false, ready: true, idle: false, left: false },
  { seat: 3, kind: 'bot', name: 'EMI bot', platform: null, look, host: false, ready: true, idle: false, left: false },
];

function rig({ platform = 'web', seat = 0 } = {}) {
  let t = 100_000;
  const timers = [], sockets = [];
  class FakeWS {
    constructor(url) { this.url = url; this.sent = []; sockets.push(this); }
    send(d) { this.sent.push(d); }
    close(code = 1000) { this.onclose && this.onclose({ code }); }
    up() { this.onopen(); }
    recv(m) { this.onmessage({ data: JSON.stringify(m) }); }
    bin(u8) { this.onmessage({ data: u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength) }); }
    drop(code = 1006) { this.onclose({ code }); }
    texts() { return this.sent.filter((d) => typeof d === 'string').map((d) => JSON.parse(d)); }
    frames() { return this.sent.filter((d) => typeof d !== 'string').map((d) => decodeFrame(d)); }
  }
  const joins = [];
  const lobby = { join: async (b) => { joins.push(b); return { roomId: 'abcdefabcdef', ticket: 'T' + joins.length, wsUrl: 'ws://x/room/abcdefabcdef', seat }; } };
  const session = createRoomSession({ track, feel: makeFeel(), lobby, platform, WS: FakeWS, now: () => t,
    timers: { setTimeout: (fn, ms) => { timers.push({ fn, at: t + ms }); return timers.length; }, clearTimeout: () => {} } });
  const log = [];
  const queue = { emit: (name, p) => log.push({ name, p }) };
  return {
    session, sockets, joins, log, queue,
    get t() { return t; }, set t(v) { t = v; },
    async runTimers() { for (const x of timers.splice(0)) { t = Math.max(t, x.at); await x.fn(); } },
    welcome(ws, more = {}) {
      ws.recv({ t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat, t0: t - 10_000, serverMs: t, phase: 'lobby', cfg: CFG, seed: 7,
        seats: SEATS.map((x) => ({ ...x })), keep: [], ...more });
    },
  };
}

async function racing(r, { keep = [] } = {}) {
  const p = r.session.connect({ roomId: 'abcdefabcdef', ticket: 'T0', wsUrl: 'ws://x/room/abcdefabcdef', seat: 0 });
  const ws = r.sockets[0];
  ws.up();
  r.welcome(ws, { keep });
  await p;
  ws.recv({ t: 'start', countAt: r.t + 100, startAt: r.t + 3100, goTick: 780, seed: 7 });
  const feel = makeFeel();
  const q = r.session.tap(r.queue);
  const racers = createRacers(track, feel, q, { net: r.session.layout(), seed: 7 });
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() {} };
  const items = r.session.mount(racers, race, q);
  r.t += 3200;
  return { ws, feel, racers, race, items, q };
}

test('hello carries the ticket; welcome resolves connect and sets the room', async () => {
  const r = rig();
  const p = r.session.connect({ roomId: 'abcdefabcdef', ticket: 'TK', wsUrl: 'ws://x/room/abcdefabcdef', seat: 0 });
  const ws = r.sockets[0];
  ws.up();
  const [hello, ping] = ws.texts();
  assert.deepEqual(Object.keys(hello).sort(), ['clientMs', 't', 'ticket', 'v']);
  assert.equal(hello.ticket, 'TK');
  assert.equal(ping.t, 'ping');
  r.welcome(ws);
  const w = await p;
  assert.equal(w.seat, 0);
  assert.equal(r.session.cfg.laps, 3);
  ws.recv({ t: 'pong', c: ping.c, s: r.t + 5000 });
  assert.ok(Math.abs(r.session.serverNow() - (r.t + 5000)) < 1);
  const lay = r.session.layout();
  assert.deepEqual(lay.map((x) => x.seat), [0, 1, 2, 3]);
  assert.equal(lay[2].kind, 'bot');
});

test('uploads at 20 Hz from web and 15 Hz from phones, own kart only, after countAt', async () => {
  for (const [platform, want] of [['web', 20], ['phone', 15]]) {
    const r = rig({ platform });
    const { ws } = await racing(r);
    const before = ws.frames().length;
    for (let i = 0; i < 100; i++) { r.t += 10; r.session.update({ paused: false }); }   // one second
    const fr = ws.frames().slice(before);
    assert.ok(Math.abs(fr.length - want) <= 1, `${platform}: ${fr.length}`);
    assert.equal(fr[0].type, BIN.UPLOAD);
    assert.deepEqual(fr[0].karts.map((k) => k.seat), [0]);
    assert.equal(fr[1].karts[0].seq, (fr[0].karts[0].seq + 1) & 0xffff);
  }
});

test('pause only pauses you: records keep flowing with the paused and assist flags', async () => {
  const r = rig();
  const { ws } = await racing(r);
  r.t += 100; r.session.update({ paused: true });
  const k = ws.frames().at(-1).karts[0];
  assert.equal(k.paused, true);
  assert.equal(k.assist, true);
});

test('remote karts are written by the remote racer kind from batches, 100 ms in the past', async () => {
  const r = rig();
  const { ws, racers } = await racing(r);
  const bo = racers.list.find((x) => x.meta.seat === 1);
  assert.equal(bo.kind, 'remote');
  const c = r.session.clock, k0 = c.tick() - 12;
  const mk = (seq, tick, s) => Object.assign(createKart(), { seat: 1, seq, tick, s, q: [0, 0, 0, 1], grounded: true });
  ws.bin(encodeFrame(BIN.BATCH, 0, [mk(1, k0, 300)]));
  ws.bin(encodeFrame(BIN.BATCH, 0, [mk(2, k0 + 6, 302)]));
  ws.bin(encodeFrame(BIN.BATCH, 0, [mk(3, k0 + 12, 304)]));
  racers.step(DT, {}, { phase: 'racing', timeS: 1 }, {});
  const now = c.serverNow(), drawn = now - 100, want = 300 + 4 * (drawn - c.tickMs(k0)) / (c.tickMs(k0 + 12) - c.tickMs(k0));
  assert.ok(Math.abs(bo.st[S.s] - want) < 0.05, `${bo.st[S.s]} vs ${want}`);
});

test('a dropped socket reconnects through the lobby with a fresh ticket; a replaced one does not', async () => {
  const r = rig();
  const { ws } = await racing(r);
  ws.drop(1006);
  await r.runTimers();
  assert.equal(r.joins.length, 1);
  assert.equal(r.joins[0].roomId, 'abcdefabcdef');
  const ws2 = r.sockets[1];
  ws2.up();
  assert.equal(ws2.texts()[0].ticket, 'T1');
  let closed = null;
  r.session.on('closed', (m) => { closed = m; });
  ws2.drop(4009);
  await r.runTimers();
  assert.equal(r.joins.length, 1);
  assert.equal(closed.code, 4009);
});

test('reconnect gives up 30 s after the drop', async () => {
  const r = rig();
  r.session.connect({ roomId: 'abcdefabcdef', ticket: 'T0', wsUrl: 'ws://x', seat: 0 }).catch(() => {});
  const ws = r.sockets[0]; ws.up(); r.welcome(ws);
  let closed = null;
  r.session.on('closed', (m) => { closed = m; });
  ws.drop(1006);
  for (let i = 0; i < 20 && !closed; i++) { await r.runTimers(); const s = r.sockets.at(-1); if (s !== ws) s.drop(1006); r.t += 4000; }
  assert.equal(closed.code, 'gone');
});

test('bot keeper: keep seeds the bot from its newest record, continues seq, uploads it; keep [] gives it back', async () => {
  const r = rig();
  const { ws, racers, feel } = await racing(r);
  const bot = racers.list.find((x) => x.meta.seat === 3);
  assert.equal(bot.kind, 'remote');
  const c = r.session.clock;
  ws.bin(encodeFrame(BIN.BATCH, 0, [Object.assign(createKart(), { seat: 3, seq: 41, tick: c.tick() - 3, s: 640, x: 1.2, lap: 1, checkpoint: 2, bot: true, q: [0, 0, 0, 1], grounded: true, speedPct: 0.8 })]));
  ws.recv({ t: 'keep', seats: [3] });
  assert.equal(bot.kind, 'local');
  assert.ok(Math.abs(bot.st[S.s] - 640) < 1e-3);
  assert.equal(bot.st[S.lap], 1);
  assert.equal(bot.st[S.checkpoint], 2);
  // it drives under the AI now
  const ai = createAi(track, feel, { seed: 7 }), standings = createStandings(track, feel, r.queue);
  const race = { phase: 'racing', timeS: 1, lapStartS: 0, finish() {} };
  for (let i = 0; i < 30; i++) { racers.step(DT, {}, race, { ai, standings }); standings.update(racers.list, race, DT); }
  assert.ok(bot.st[S.s] > 640, 'moved');
  r.t += 100; r.session.update();
  const k = ws.frames().at(-1).karts;
  assert.deepEqual(k.map((x) => x.seat), [0, 3]);
  assert.equal(k[1].seq, 42);
  assert.equal(k[1].bot, true);
  ws.recv({ t: 'keep', seats: [] });
  assert.equal(bot.kind, 'remote');
  r.t += 100; r.session.update();
  assert.deepEqual(ws.frames().at(-1).karts.map((x) => x.seat), [0]);
});

test('a kept bot sends its own cp with its seat', async () => {
  const r = rig();
  const { ws, racers, q } = await racing(r, { keep: [2] });
  const bot = racers.list.find((x) => x.meta.seat === 2);
  assert.equal(bot.kind, 'local');
  q.emit('racer-event', { racer: bot.i, name: 'checkpoint', p: { idx: 1 } });
  q.emit('lap', { lap: 1, lapTimeS: 60, timeS: 60 });
  const cps = ws.texts().filter((m) => m.t === 'cp');
  assert.deepEqual(cps.map((m) => [m.idx, m.lap, m.seat]), [[1, 0, 2], [0, 1, undefined]]);
  q.emit('finish', { timeS: 120.5 });
  const fin = ws.texts().find((m) => m.t === 'finish');
  assert.equal(fin.tick, 780 + 7230);
  assert.ok(Math.abs(fin.frac) < 1e-6);
});

test('items online: pickup goes up, roll fills the slot, fire goes up, a hit lands on the local victim only', async () => {
  const r = rig();
  const { ws, racers, items, feel } = await racing(r);
  const me = racers.player, race = { phase: 'racing', timeS: 1 }, standings = createStandings(track, feel, r.queue);
  standings.update(racers.list, race, DT);
  const box = track.items.findIndex((x) => x.kind === 'box'), b = track.items[box];
  me.st[S.s] = b.s; me.st[S.lateral] = b.x; me.st[S.grounded] = 1;
  items.update(DT, racers.list, standings, race);
  const pick = ws.texts().filter((m) => m.t === 'pickup');
  assert.deepEqual(pick.map((m) => [m.box, m.seat]), [[box, undefined]]);
  assert.equal(me.item.id, null);
  ws.recv({ t: 'roll', seat: 0, box, item: 'flash' });
  assert.equal(me.item.id, 'flash');
  assert.equal(me.item.rolling, true);
  for (let i = 0; i < 70; i++) items.update(DT, racers.list, standings, race);
  assert.equal(me.item.rolling, false);
  me.input.item = true;
  items.update(DT, racers.list, standings, race);
  me.input.item = false;
  const fire = ws.texts().find((m) => m.t === 'fire');
  assert.equal(fire.item, 'flash');
  assert.equal(fire.mode, 'throw');
  assert.equal(me.item.id, null);
  // the room's verdicts: a hit on a remote kart changes nothing here but the event; a hit on me lands
  ws.recv({ t: 'fired', sid: 9, seat: 1, id: 4, item: 'spiral', mode: 'throw', tick: 1, s: me.st[S.s] - 10, x: 0, target: 0 });
  assert.equal(items.view().projectiles.length, 1);
  ws.recv({ t: 'hit', sid: 9, victim: 0, by: 1, item: 'spiral', verdict: 'hit', durationS: 1.5, tick: 2 });
  assert.equal(items.view().projectiles.length, 0);
  items.update(DT, racers.list, standings, race);
  assert.equal(me.fxv.effect, 'spiral');
  assert.notEqual(me.mods.steerAdd, 0);
  const hit = r.log.find((e) => e.name === 'item-hit');
  assert.equal(hit.p.victim, 0);
  assert.equal(hit.p.durationS, 1.5);
  ws.recv({ t: 'hit', sid: 10, victim: 1, by: 0, item: 'flash', verdict: 'mercy', durationS: 0, tick: 3 });
  assert.equal(r.log.filter((e) => e.name === 'item-hit').length, 2);
});

test('lobby api: fetch transport and the desktop host transport, /v2/race/ only', async () => {
  const calls = [];
  const fetchImpl = async (url, o) => { calls.push([url, o.method, o.headers.authorization, o.body]); return { status: 200, json: async () => ({ roomId: 'r', ticket: 't' }) }; };
  const api = createLobbyApi(fetchTransport({ base: 'http://127.0.0.1:8811', headers: { authorization: 'Bearer jwt-u1' }, fetchImpl }));
  const j = await api.join({ roomId: 'abcdefabcdef', look });
  assert.equal(j.ticket, 't');
  assert.deepEqual(calls[0].slice(0, 3), ['http://127.0.0.1:8811/v2/race/join', 'POST', 'Bearer jwt-u1']);
  const handlers = {}, sent = [];
  const bridge = { on: (type, fn) => { handlers[type] = fn; }, send: (m) => sent.push(m) };
  const host = createLobbyApi(hostTransport(bridge, { timers: { setTimeout: () => 1, clearTimeout: () => {} } }));
  const p = host.quick({ look });
  assert.equal(sent[0].type, 'net');
  assert.equal(sent[0].path, '/v2/race/quick');
  handlers['net-reply']({ type: 'net-reply', id: sent[0].id, status: 409, body: JSON.stringify({ error: 'full' }) });
  await assert.rejects(p, (e) => e.code === 'full' && e.status === 409);
});
