/* tests/net-race-fake.test.mjs - two clients race the real sim on Threshold through an in-memory fake room
 * (relay at 50 ms, start, keep, rolls, a hit), in virtual time: each sees the other's kart drawn smoothly about
 * 100 ms behind, the bot seat is kept by one client and handed to the other when its keeper leaves, a hit lands
 * on the victim's own client. The fake room is a test double of NET.md, not the B2-a room. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRoomSession } from '../net/room.js';
import { decodeFrame, encodeFrame, decodeMsg, BIN, NET_V } from '../net/protocol.js';
import { createRace } from '../sim/race.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { createContact } from '../sim/contact.js';
import { createStandings } from '../sim/standings.js';
import { createActions } from '../core/actions.js';
import { S } from '../sim/state.js';
import { wrap } from '../core/math.js';
import { threshold, DT } from './sim-racers-harness.mjs';
import { makeFeel } from './sim-fake.mjs';

const track = threshold();
const look = { outfit: null, vehicle: 'emi-teacup-kart' };
const CFG = { track: 'threshold', laps: 3, seats: 3, mode: 'friends', laneKeeper: false, staked: false, botFill: true, autoStartS: 0 };

function fakeRoom(clock) {
  const socks = new Map(), newest = new Map();   // ws -> { seat, gone }, seat -> { k, pulses, from }
  const seats = [0, 1, 2].map((seat) => ({ seat, kind: seat === 2 ? 'bot' : 'human', name: seat === 2 ? 'EMI bot' : 'P' + seat,
    platform: seat === 2 ? null : 'web', look, host: seat === 0, ready: true, idle: false, left: false }));
  let keeper = null, lastBatch = 0, sid = 1;
  const out = (ws, m) => { assert.ok(decodeMsg(JSON.stringify(m), 'r2c'), 'room sends a valid ' + m.t); ws.onmessage({ data: JSON.stringify(m) }); };
  const all = (m) => { for (const [ws, s] of socks) if (!s.gone) out(ws, m); };
  const room = {
    t0: clock() - 5000, hits: [],
    connect(ws) { socks.set(ws, { seat: -1, gone: false }); queueMicrotask(() => ws.onopen()); },
    text(ws, txt) {
      const m = decodeMsg(txt, 'c2r'), me = socks.get(ws);
      assert.ok(m, 'client sends a valid message: ' + txt);
      if (m.t === 'hello') {
        me.seat = Number(m.ticket.split(':')[1]);
        out(ws, { t: 'welcome', v: NET_V, roomId: 'abcdefabcdef', seat: me.seat, t0: room.t0, serverMs: clock(), phase: 'lobby', cfg: CFG, seed: 3, seats, keep: [] });
      } else if (m.t === 'ping') out(ws, { t: 'pong', c: m.c, s: clock() });
      else if (m.t === 'pickup') all({ t: 'roll', seat: m.seat ?? me.seat, box: m.box, item: 'spiral' });
      else if (m.t === 'fire') {
        const by = m.seat ?? me.seat, target = by === 0 ? 1 : 0, s = sid++;
        all({ t: 'fired', sid: s, seat: by, id: m.id, item: m.item, mode: m.mode, tick: m.tick, s: m.s, x: m.x, target });
        all({ t: 'hit', sid: s, victim: target, by, item: m.item, verdict: 'hit', durationS: 1, tick: m.tick + 20 });
        room.hits.push({ by, target });
      } else if (m.t === 'leave') { me.gone = true; room.handOff(ws); }
    },
    bin(ws, buf) {
      const f = decodeFrame(buf), me = socks.get(ws);
      assert.ok(f && f.type === BIN.UPLOAD);
      for (const k of f.karts) {
        assert.ok(k.seat === me.seat || (ws === keeper && k.seat === 2), 'only own or kept seats');
        const o = newest.get(k.seat);
        newest.set(k.seat, { k, pulses: (o ? o.pulses : 0) | k.pulses, from: ws });
      }
    },
    close(ws) { const me = socks.get(ws); if (me) { me.gone = true; room.handOff(ws); } },
    start() {
      const countAt = clock() + 200, startAt = countAt + 3000;
      all({ t: 'start', countAt, startAt, goTick: Math.round((startAt - room.t0) * 60 / 1000), seed: 3 });
      keeper = [...socks.keys()][0];
      out(keeper, { t: 'keep', seats: [2] });
    },
    handOff(ws) {
      if (ws !== keeper) return;
      keeper = [...socks].find(([w, s]) => !s.gone && w !== ws)?.[0] || null;
      if (keeper) out(keeper, { t: 'keep', seats: [2] });
    },
    tick() {
      if (clock() - lastBatch < 50) return;
      lastBatch = clock();
      for (const [ws, s] of socks) {
        if (s.gone) continue;
        const ks = [...newest.values()].filter((x) => x.from !== ws).map((x) => Object.assign({}, x.k, { pulses: x.pulses }));
        if (ks.length) ws.onmessage({ data: encodeFrame(BIN.BATCH, clock() - room.t0, ks).buffer });
      }
      for (const x of newest.values()) x.pulses = 0;
    },
  };
  return room;
}

test('two clients and a kept bot race through a fake room', async (t0) => {
  let t = 1_000_000;
  const clock = () => t;
  const room = fakeRoom(clock);
  class WS {
    constructor(url) { this.url = url; room.connect(this); }
    send(d) { if (typeof d === 'string') room.text(this, d); else room.bin(this, d); }
    close() { room.close(this); this.onclose && this.onclose({ code: 1000 }); }
  }
  const timers = { setTimeout: (fn) => { queueMicrotask(fn); return 1; }, clearTimeout() {} };
  const feel = makeFeel();
  const goS = feel.get('juice.countdownPulses') * 60 / feel.get('clock.bpm');
  const clients = [];
  for (const seat of [0, 1]) {
    const s = createRoomSession({ track, feel: makeFeel(), WS, now: clock, timers });
    await s.connect({ roomId: 'abcdefabcdef', ticket: 'seat:' + seat, wsUrl: 'ws://fake', seat });
    clients.push({ s, seat, log: [] });
  }
  room.start();
  for (const c of clients) {
    const f = makeFeel(), queue = { emit: (name, p) => c.log.push({ name, p }) }, q = c.s.tap(queue);
    c.race = createRace(track, f, q);
    c.racers = createRacers(track, f, q, { net: c.s.layout(), seed: 3 });
    c.ai = createAi(track, f, { seed: 3 + c.seat });
    c.contact = createContact(track, f, q);
    c.standings = createStandings(track, f, q);
    c.items = c.s.mount(c.racers, c.race, q);
    c.a = Object.assign(createActions(), { item: false, back: false });
  }
  assert.equal(clients[0].racers.list.find((r) => r.meta.seat === 2).kind, 'local');
  assert.equal(clients[1].racers.list.find((r) => r.meta.seat === 2).kind, 'remote');

  let maxErr = 0, maxJump = 0, gave = false, fired = false, sawEffect = false;
  const lastShown = [null, null];
  for (let step = 0; step < 60 * 40; step++) {
    t += 1000 / 60;
    for (const c of clients) {
      if (c.gone) continue;
      if (!c.started && c.s.serverNow() >= c.s.raceStartAt(goS)) { c.started = true; c.race.start(); }
      if (!c.started) { c.s.update(); continue; }
      const P = c.racers.player, ctx = { racers: c.racers.list, items: c.items, standings: c.standings, race: c.race };
      c.ai.drive(P, ctx, DT, c.a);
      if (!fired && c.seat === 0 && c.race.timeS > 6) { c.racers.player.item.id = 'spiral'; c.racers.player.item.rolling = false; c.a.item = true; fired = true; }
      c.race.update(DT, c.a, P.st);
      c.racers.step(DT, c.a, c.race, { ai: c.ai, items: c.items, standings: c.standings });
      c.contact.update(DT, c.racers.list, c.race);
      c.standings.update(c.racers.list, c.race, DT);
      c.items.update(DT, c.racers.list, c.standings, c.race);
      c.s.update({ paused: false });
    }
    room.tick();
    await Promise.resolve();
    // client 1 draws client 0's kart: close to where it truly was ~100 ms ago, and never jumping
    const A = clients[0], B = clients[1];
    if (A.started && B.started && A.race.timeS > 2 && !A.gone) {
      const mine = A.racers.player.st[S.s], drawn = B.racers.list.find((r) => r.meta.seat === 0).st[S.s];
      const lag = wrap(mine - drawn + track.lengthM / 2, track.lengthM) - track.lengthM / 2;
      maxErr = Math.max(maxErr, Math.abs(lag));
      if (lastShown[0] !== null) maxJump = Math.max(maxJump, Math.abs(wrap(drawn - lastShown[0] + track.lengthM / 2, track.lengthM) - track.lengthM / 2));
      lastShown[0] = drawn;
    }
    if (B.racers && B.racers.player.fxv.effect) sawEffect = true;
    if (!gave && A.race.timeS > 20) { gave = true; A.s.leave(); A.gone = true; }
  }
  const A = clients[0], B = clients[1];
  // drawn about 100 ms (plus one upload gap) behind at race speed, never teleporting
  assert.ok(maxErr < 12, `remote lag ${maxErr.toFixed(2)} m`);
  assert.ok(maxJump < 1.5, `remote frame jump ${maxJump.toFixed(2)} m`);
  // the bot moved on B's screen while A kept it, then B took it over and drives it
  const botB = B.racers.list.find((r) => r.meta.seat === 2);
  assert.equal(botB.kind, 'local');
  assert.ok(B.s.keep.includes(2));
  assert.ok(botB.st[S.lap] >= 1 || botB.st[S.s] > 100, 'bot raced');
  // items crossed clients: A threw at B, the kept bot threw too (with its seat), and B applied the hits itself
  assert.ok(room.hits.some((h) => h.by === 0 && h.target === 1), JSON.stringify(room.hits));
  assert.ok(room.hits.some((h) => h.by === 2), 'the kept bot fires through its keeper');
  const onB = room.hits.filter((h) => h.target === 1).length;
  assert.equal(B.log.filter((e) => e.name === 'item-hit' && e.p.victim === 0).length, onB);
  assert.ok(sawEffect, 'B ran the screen effect locally');
  t0.diagnostic(`remote lag max ${maxErr.toFixed(2)} m, frame jump max ${maxJump.toFixed(2)} m, hits ${room.hits.length}`);
});
