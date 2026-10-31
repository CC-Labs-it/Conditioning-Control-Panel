/* net/room.js - one online race from the client's side (NET.md sections 1-8).
 *
 *   const s = createRoomSession({ track, feel, lobby, platform });
 *   await s.connect(join);                    // join = lobby answer { roomId, ticket, wsUrl, seat }
 *   s.on('start', ...)                         // then, before the countdown:
 *   const q = s.tap(queue);                    // the queue the sim emits into (local events go up as cp, finish, drop, pulses)
 *   const racers = createRacers(track, feel, q, { net: s.layout(), seed: s.seed });
 *   const items = s.mount(racers, race, q);    // remote driver + online items (the sim/items.js API)
 *   per frame: s.update({ paused, assist });   // clock slew, pings, uploads at 20 / 15 Hz
 *   after racers.interpolate(alpha): s.drawRemotes();   // remote karts at this frame's server time
 *   race.start() when s.serverNow() >= s.raceStartAt(goS)   // go lands on the room's startAt
 *
 * The room is never waited on: pause pauses only you (records keep flowing with the paused flag). A dropped
 * socket reconnects within 30 s through the lobby (a fresh ticket for the same seat). The bot keeper: `keep`
 * hands bot seats to this client, which seeds each from the newest record it drew and simulates it with
 * sim/ai.js (kind 'local'); losing them makes them remote again. Pure apart from the injected WebSocket and timers.
 */
import { S, RESPAWN } from '../sim/state.js';
import { NET_V, COUNT_MS, RATES, PLATFORMS, normLook, roomLook, fromHex, unpackKart, seqNewer, KART_BYTES } from './protocol.js';
import { openSocket, FINAL_CLOSE } from './socket.js';
import { createServerClock } from './clock.js';
import { createInterp } from './interp.js';
import { createUplink } from './snapshot.js';
import { createRemoteDriver } from './remote.js';
import { createNetItems } from './items.js';

const KEEP_WAIT_MS = 1000;   // a kept bot with no record: wait this long for one before driving it from where it stands
const SIM_EVENTS = new Set(['checkpoint', 'lap', 'finish', 'drop', 'hop', 'land', 'boost-start', 'airborne', 'trick']);

export function createRoomSession({ track, feel, courseHash = '', lobby = null, platform = 'web', loads = false, WS = globalThis.WebSocket, now = () => Date.now(),
  timers = globalThis, log = () => {} } = {}) {
  const plat = PLATFORMS.includes(platform) ? platform : 'web';
  const clock = createServerClock({ now });
  const interp = createInterp(track);
  const uplink = createUplink({ phone: plat === 'phone', track });
  const handlers = new Map();
  const S0 = { roomId: null, seat: -1, cfg: null, seed: 0, seats: [], keep: [], phase: 'lobby', countAt: 0, startAt: 0, goTick: 0 };
  const st = { ...S0 };
  let sock = null, joinInfo = null, look = normLook(null), leaving = false, downAt = 0, tries = 0, retryT = 0;
  let racers = null, race = null, items = null, connectWait = null, isLoaded = false;
  const keepWait = new Map();   // kept bot seat -> ms after which it is driven without a record
  const STAMP_SLACK_TICKS = 2, VIEW_LEAN = 0.02, VIEW_SNAP_MS = 100;
  let stampLag = null, viewAt = null, viewFrame = null;

  const emit = (name, m) => { const set = handlers.get(name); if (set) for (const fn of [...set]) { try { fn(m); } catch (e) { log('warn', `net ${name}: ${e}`); } } };
  function on(name, fn) { let set = handlers.get(name); if (!set) handlers.set(name, (set = new Set())); set.add(fn); return () => set.delete(fn); }
  const send = (m) => !!(sock && sock.send(m));
  const racerOf = (seat) => (racers ? racers.list.find((r) => r.meta.seat === seat) || null : null);
  const raceTick = () => (race && (race.phase === 'racing' || race.phase === 'finished') ? st.goTick + Math.floor(race.timeS * 60) : clock.tick());

  // ---- socket ---------------------------------------------------------------------------------------
  function open(j) {
    joinInfo = j;
    sock = openSocket(j.wsUrl, {
      WS,
      onOpen() { clock.restartPings(); send({ t: 'hello', v: NET_V, ticket: j.ticket, clientMs: now(), ...(courseHash ? { courseHash } : {}), ...(loads ? { loads: true } : {}) }); pingNow(); },
      onMsg,
      onBatch(f) {
        for (const k of f.karts) {
          if (k.seat === st.seat) continue;
          const r = racerOf(k.seat), kept = st.keep.includes(k.seat);
          if (kept && r && r.kind !== 'remote') continue;
          interp.push(k, clock.tickMs(k.tick));
          // a kept bot handed over before this client had any record of it is taken on its first record
          if (kept) { if (r) takeBot(k.seat); continue; }
          // the room relays records only for seats it holds as present: a kart marked left that sends again is back
          if (r && r.meta.left) seatBack(r);
        }
      },
      onClose,
    });
  }
  function pingNow() { if (sock && sock.open && clock.pingDue()) send({ t: 'ping', c: now() }); }

  function onClose({ code }) {
    sock = null;
    if (leaving || FINAL_CLOSE.includes(code) || st.phase === 'done') { emit('closed', { code }); return; }
    if (!downAt) { downAt = now(); tries = 0; emit('reconnecting', { code }); }
    retry();
  }
  function retry() {
    if (now() - downAt > RATES.reconnectS * 1000 || !lobby) { emit('closed', { code: 'gone' }); return; }
    const wait = Math.min(4000, 500 * 2 ** tries++);
    retryT = timers.setTimeout(async () => {
      retryT = 0;
      if (leaving) return;
      try {
        const j = await lobby.join({ roomId: st.roomId, look });
        if (j && j.ticket && !leaving) { open(j); return; }
      } catch (e) { log('warn', `net rejoin: ${e && e.message}`); }
      retry();
    }, wait);
  }

  // ---- room messages --------------------------------------------------------------------------------
  function onMsg(m) {
    switch (m.t) {
      case 'welcome':
        Object.assign(st, { roomId: m.roomId, seat: m.seat, cfg: m.cfg, seed: m.seed, seats: m.seats, phase: m.phase });
        if (m.countAt !== undefined) Object.assign(st, { countAt: m.countAt, startAt: m.startAt, goTick: m.goTick });
        clock.setRoom(m.t0, m.serverMs);
        resumeSeq(m.resume);
        downAt = 0;
        applyKeep(m.keep);
        syncRacers();
        if (loads && isLoaded) send({ t: 'loaded' });   // a new socket is a new seat to the room's load hold
        if (connectWait) { connectWait.res(m); connectWait = null; }
        break;
      case 'pong': clock.pong(m.c, m.s); break;
      case 'seats': st.seats = m.seats; syncRacers(); break;
      case 'seat': seatChange(m); break;
      case 'start': Object.assign(st, { countAt: m.countAt, startAt: m.startAt, goTick: m.goTick, seed: m.seed, phase: 'countdown' }); break;
      case 'keep': applyKeep(m.seats); break;
      case 'roll': if (items) items.onRoll(m); break;
      case 'fired': if (items) items.onFired(m); break;
      case 'hit': if (items) items.onHit(m); break;
      case 'fizzle': if (items) items.onFizzle(m); break;
      case 'finish': st.phase = 'done'; break;
      case 'error': if (m.code === 'item' && m.re === 'pickup' && items) items.onPickupRefused(); break;
      case 'abort': leaving = leaving || ['ticket', 'expired', 'replay', 'version', 'config', 'full', 'started', 'closed'].includes(m.reason); break;
      default: break;
    }
    emit(m.t, m);
  }

  function seatChange(m) {
    const row = st.seats.find((x) => x.seat === m.seat);
    if (row) for (const k of ['ready', 'idle', 'left', 'look', 'paused']) if (m[k] !== undefined) row[k] = m[k];
    if (m.idle !== undefined) interp.hold(m.seat, m.idle);
    const r = racerOf(m.seat);
    if (r && m.left !== undefined) { if (m.left) seatGone(r); else seatBack(r); }
    if (r && m.paused !== undefined) r.meta.paused = m.paused;
  }

  /* A seat's racer follows the room's seat list. The room clears `left` on a reconnect (and blanks a seat past the
   * grace) with a whole `seats` list, not a `seat` message: without this a kart hidden by a drop stayed hidden for
   * the rest of the race on every other client (owner's first phone race, 2026-10-30). */
  function syncRacers() {
    if (!racers) return;
    for (const r of racers.list) {
      if (r.i === 0) continue;
      const row = st.seats.find((x) => x.seat === r.meta.seat);
      const gone = !row || row.kind === 'empty' || !!row.left;
      if (gone && !r.meta.left) seatGone(r);
      else if (!gone && r.meta.left) seatBack(r);
      if (row && !gone) { interp.hold(r.meta.seat, !!row.idle); r.meta.paused = !!row.paused; }
    }
  }
  /* A rejoin mid-race in a fresh session (the page or the phone app rebuilt it) starts the uplink seq at 0, and the
   * room drops every record whose seq is not newer than the seat's last one: the kart froze on every other screen
   * for as long as it had raced. welcome.resume is the room's last record of this seat: continue past its seq. */
  function resumeSeq(hex) {
    if (!hex || st.seat < 0) return;
    const u8 = fromHex(hex);
    if (u8.length < KART_BYTES) return;
    const k = unpackKart(new DataView(u8.buffer, u8.byteOffset, u8.byteLength), 0);
    // fresh session (seq still the untouched 0): continue past the room's record whatever its value (a plain
    // seqNewer against 0 fails past 0x8000, ~27 min at 20 Hz); a same-session reconnect only ever moves forward
    if (!uplink.seqMoved(st.seat) || seqNewer(k.seq, uplink.seqOf(st.seat))) uplink.seqFrom(st.seat, k.seq);
  }
  function seatGone(r) { r.meta.left = true; }
  function seatBack(r) { r.meta.left = false; }

  /** The bot keeper hand-off (NET.md section 6). */
  function applyKeep(seats) {
    const was = st.keep;
    st.keep = (seats || []).filter((s) => s !== st.seat);
    if (!racers) return;
    for (const s of was) {
      if (st.keep.includes(s)) continue;
      keepWait.delete(s);
      const r = racerOf(s);
      interp.drop(s);                       // records from before this client kept it would bridge a long gap
      if (r) racers.setKind(r.i, 'remote');
    }
    for (const s of st.keep) { const r = racerOf(s); if (r && (!was.includes(s) || r.kind === 'remote')) takeBot(s); }
  }
  /* Mid-race, a bot handed to a client that has no record of it (a fresh session after a rejoin) stays a remote
   * racer until the first batch carrying its record: driving it from the grid froze it at the start line on every
   * screen. Before GO the grid is its true place. No record within KEEP_WAIT_MS (a room that never sends one): drive
   * it from where it stands. */
  function takeBot(seat, force = false) {
    const r = racerOf(seat);
    if (!r) return;
    const top = interp.newest(seat);
    if (!top && !force && (st.phase === 'racing' || st.phase === 'done')) {
      if (!keepWait.has(seat)) keepWait.set(seat, now() + KEEP_WAIT_MS);
      if (r.kind !== 'remote') racers.setKind(r.i, 'remote');   // layout() built it local
      return;
    }
    keepWait.delete(seat);
    if (top) {
      interp.seed(seat, r.st);
      r.st[S.respawnPhase] = top.k.respawn === RESPAWN.NONE ? RESPAWN.NONE : RESPAWN.GRACE;
      const lap = r.st[S.lap], cp = r.st[S.checkpoint];
      r.progress.reset(r.st);
      r.st[S.lap] = lap; r.st[S.checkpoint] = cp;
      r.prev.set(r.st); r.view.set(r.st);
      r.finished = top.k.finished;
      uplink.seqFrom(seat, top.k.seq);
    }
    racers.setKind(r.i, 'local');
  }

  // ---- local events up --------------------------------------------------------------------------------
  function observe(i, name, p) {
    const r = racers && racers.list[i];
    if (!r || r.kind === 'remote' || !(st.phase === 'countdown' || st.phase === 'racing')) return;
    const seat = r.meta.seat, mine = (m) => (i === 0 ? m : Object.assign(m, { seat }));
    uplink.event(seat, name, p, now());
    if (name === 'checkpoint' && p.idx > 0) send(mine({ t: 'cp', idx: p.idx, lap: r.st[S.lap] | 0, tick: raceTick() }));
    else if (name === 'lap') send(mine({ t: 'cp', idx: 0, lap: p.lap | 0, tick: raceTick() }));
    else if (name === 'finish') {
      const f = st.goTick + Math.max(0, p.timeS) * 60, k = Math.floor(f);
      send(mine({ t: 'finish', tick: k, frac: Math.min(1, Math.max(0, f - k)) }));
    } else if (name === 'drop') send(mine({ t: 'drop', tick: raceTick(), s: r.st[S.s] }));
  }
  function tap(queue) {
    return {
      emit(name, p = {}) {
        queue.emit(name, p);
        if (name === 'racer-event') { if (p && SIM_EVENTS.has(p.name)) observe(p.racer, p.name, p.p || {}); }
        else if (name === 'bubble-pop') observe(p.racer | 0, name, p);
        else if (SIM_EVENTS.has(name)) observe(0, name, p);
      },
      clear() { if (queue.clear) queue.clear(); }, flush(bus) { if (queue.flush) queue.flush(bus); },
    };
  }

  /** Wire the racers in: remote driver, kept bots, online items. @returns the items object for boot.js */
  function mount(rs, rc, q) {
    racers = rs; race = rc;
    racers.remote = createRemoteDriver(interp, viewNow, q).drive;
    items = createNetItems(track, feel, q, { send, tick: raceTick });
    const keep = st.keep; st.keep = []; applyKeep(keep);
    syncRacers();
    return items;
  }

  /** Per frame: clock slew, pings, uploads. */
  function update({ paused = false, assist = false } = {}) {
    const t = now();
    clock.slew(t);
    pingNow();
    if (st.phase === 'countdown' && clock.serverNow(t) >= st.startAt) st.phase = 'racing';
    for (const [s, at] of keepWait) if (t >= at) { if (st.keep.includes(s)) takeBot(s, true); else keepWait.delete(s); }
    if (!racers || !sock || !sock.open || (st.phase !== 'countdown' && st.phase !== 'racing')) return;
    if (clock.serverNow(t) < st.countAt || !uplink.due(t)) return;
    const rows = [{ seat: st.seat, r: racers.player, bot: false, paused, assist: !!assist || paused }];
    for (const s of st.keep) { const r = racerOf(s); if (r && r.kind !== 'remote' && !r.meta.left) rows.push({ seat: s, r, bot: true, paused: false, assist: false }); }
    sock.upload(Math.max(0, Math.round(clock.roomMs(t))), uplink.build(rows, recordTick(t), t));
  }

  /** The tick a record is stamped with. While racing it counts the sim's own steps, so equal travel carries equal
   * time: the wall clock is a frame's phase off at each upload, and one tick is 67 cm at 40 m/s on every rival's
   * screen. The wall clock only re-anchors it when they part (dropped sim time, a pause). */
  function recordTick(t) {
    const wall = clock.tick(t);
    if (!race || race.phase !== 'racing') { stampLag = null; return wall; }
    const sim = st.goTick + Math.round(race.timeS * 60);
    if (stampLag === null || Math.abs(wall - sim - stampLag) > STAMP_SLACK_TICKS) stampLag = wall - sim;
    return Math.max(0, sim + stampLag);
  }

  /** The server time remote karts are drawn at: it runs on the frame clock and leans toward serverNow, which reads
   * Date.now wherever the frame's work has got to (2 ms of that is 8 cm at 40 m/s). */
  function viewNow() {
    const server = clock.serverNow();
    return viewAt === null || Math.abs(server - viewAt) > VIEW_SNAP_MS ? server : viewAt;
  }
  function stepView(frameMs) {
    const server = clock.serverNow();
    const next = viewAt === null || frameMs === undefined || viewFrame === null ? server : viewAt + Math.max(0, frameMs - viewFrame);
    viewAt = Math.abs(server - next) > VIEW_SNAP_MS ? server : next + (server - next) * VIEW_LEAN;
    viewFrame = frameMs === undefined ? null : frameMs;
  }

  /** After racers.interpolate(alpha): remote karts drawn at this frame's server time (smooth at any frame rate,
   * whatever number of sim steps the frame ran). `frameMs` = the frame's requestAnimationFrame stamp. */
  function drawRemotes(frameMs) {
    if (!racers) return;
    stepView(frameMs);
    for (const r of racers.list) {
      if (r.kind !== 'remote' || !interp.has(r.meta.seat)) continue;
      r.view.set(r.st);
      interp.sample(r.meta.seat, viewAt, r.view);
    }
  }

  return {
    on, update, tap, mount, drawRemotes, interp, clock, uplink,
    /** Open the room socket with a lobby answer; resolves on welcome. */
    connect(j, { lookNow = null } = {}) {
      if (lookNow) look = normLook(lookNow);
      leaving = false;
      return new Promise((res, rej) => {
        connectWait = { res, rej };
        // the room's abort comes just before its close: keep the reason so the lobby can say why, not "No connection"
        let reason = null;
        const offAbort = on('abort', (m) => { reason = m.reason || null; });
        const stop = on('closed', (m) => {
          stop(); offAbort();
          if (connectWait) { connectWait = null; rej(Object.assign(new Error('net: closed ' + m.code), { reason, closeCode: m.code })); }
        });
        open(j);
      });
    },
    /** One racer per non-empty seat at start, this client's first (createRacers `net`). */
    layout() {
      const rows = st.seats.filter((x) => x.kind !== 'empty' && !(x.left && x.seat !== st.seat));
      rows.sort((a, b) => (a.seat === st.seat ? -1 : b.seat === st.seat ? 1 : a.seat - b.seat));
      return rows.map((x) => ({ seat: x.seat, kind: x.kind, name: x.name, look: normLook(x.look), local: x.seat === st.seat || st.keep.includes(x.seat) }));
    },
    /** This page can race now: the room's start waits for it (NET.md, the load hold). */
    markLoaded() { isLoaded = true; if (loads) send({ t: 'loaded' }); },
    ready: (on2 = true) => send({ t: 'ready', on: !!on2 }),
    start: () => send({ t: 'start' }),
    setLook(l) { look = normLook(l); return send({ t: 'look', ...roomLook(look) }); },
    emote(code, poseId = 0) { if (poseId) uplink.pose(st.seat, poseId, now()); return send({ t: 'emote', code }); },
    /** One collected lyric floor word, bubble or song gate: shared pulse and cumulative race total. */
    wordPop(total) { uplink.event(st.seat, 'word-pop', {}); if (Number.isFinite(total)) uplink.setPops(st.seat, total); },
    leave() { leaving = true; if (retryT) timers.clearTimeout(retryT); send({ t: 'leave' }); if (sock) sock.close(1000, 'leave'); },
    serverNow: () => clock.serverNow(),
    /** Server ms at which race.start() puts go on startAt, for a local countdown of goS seconds. */
    raceStartAt: (goS) => st.startAt - goS * 1000,
    /** Seconds since go on server time (negative before), for the race clock and a late joiner's song seek. */
    raceTimeS: () => (clock.serverNow() - st.startAt) / 1000,
    songS: () => (clock.serverNow() - st.countAt) / 1000,
    get seat() { return st.seat; }, get seats() { return st.seats; }, get cfg() { return st.cfg; }, get seed() { return st.seed; },
    get phase() { return st.phase; }, get keep() { return st.keep.slice(); }, get roomId() { return st.roomId; },
    get countAt() { return st.countAt; }, get startAt() { return st.startAt; }, get goTick() { return st.goTick; },
    get connected() { return !!(sock && sock.open); }, get join() { return joinInfo; }, COUNT_MS,
  };
}
