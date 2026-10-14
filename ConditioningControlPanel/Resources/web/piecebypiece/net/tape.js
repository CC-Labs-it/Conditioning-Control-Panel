/* ============================================================================
 * net/tape.js - the stands' tape: a watched game served in 2 s chunks and
 * played back on a delayed timeline (spectate v2, 2026-10-14).
 *
 * WHY CHUNKS. The server holds every match `delay_ms` (12 s) behind the live
 * one, so everything older than that cutoff can never change. It cuts that past
 * into CHUNK_MS slices, chunk n covering [t0 + n*C, t0 + (n+1)*C), and serves a
 * chunk only once its END is older than the delay; from then on the answer is
 * immutable and the CDN caches it for good. One function call per chunk per
 * match, however many people watch. Nothing here polls Redis.
 *
 * THE TIMELINE. serverNow() = our monotonic clock plus an offset measured off
 * every GET watch answer (server_now_ms is the server's REAL now in v2). The
 * playhead is serverNow() - lagMs(), lag = delay + one chunk + LEAD_MS: chunk n
 * is ready at t0 + (n+1)*C + delay, the playhead reaches its start at
 * t0 + n*C + lag, so the next chunk is always on hand LEAD_MS before it is
 * needed. The honest consequence: the stands sit delay + C + LEAD behind the
 * live board (16 s at 12 s / 2 s / 2 s), not delay alone. lagMs() says so.
 * Each session fetches a chunk a random 0..JITTER_MS after it turns ready, so a
 * crowd does not hit the CDN in the same millisecond; LEAD_MS covers it. The
 * offset errs LATE (server_now_ms minus half the round trip): a playhead a
 * little behind costs nothing, one ahead of the server asks for chunks early.
 *
 * JOINING MID-GAME. The chunk the playhead is in (P) is fetched whole, and
 * everything before it comes from GET upto/P-1 (every move, the clocks at that
 * instant, the end if any). Events of chunk P already behind the playhead are
 * folded into that snapshot; the rest are queued. A playhead before t0 starts
 * from the opening position and waits for chunk 0.
 *
 * PLAYBACK. A tick (100 ms) dispatches every queued item whose time has come,
 * in order: moves (onMove; a move the rules refuse is a resync; seq only dedupes), fx heat
 * samples and fired effects (onHeat / onFire, only for the turn in progress,
 * and a fire that is already STALE_FIRE_MS late is dropped rather than dumped
 * in a heap after a stall), and the end. A chunk that lands with effects still
 * ahead says so through onSoon (prefetch). A tab that slept far behind the
 * playhead re-snaps (catch-up again) instead of fast-forwarding.
 *
 * ALSO HERE. The 30 s heartbeat (GET watch: what counts this watcher, and the
 * offset and watcher count refresh), the reaction poll (the PREVIOUS 3 s
 * bucket, every 3 s; public, cached) and the reaction sender (clicks batched,
 * at most one POST per 2.5 s, a 429 keeps the batch, counts clamped 1..10).
 *
 * THE CLOCK. createTapeClock reads the balances on the delayed timeline: the
 * side to move loses (playhead - stamp) off the balance stamped at its turn's
 * start. Same surface as net/match.js createRemoteClock, so the HUD and the
 * sound read it unchanged.
 * ==========================================================================*/

import { endingFrom } from './match.js';
import * as defaultApi from './api.js';

/** The server's WATCH_CHUNK_MS (CCP-Server proxy/pbp.js, fe22bb5). The answer's chunk_ms wins. */
export const CHUNK_MS = 2000;
/** The server's PBP_WATCH_DELAY_MS default. The answer's delay_ms wins. */
export const WATCH_DELAY_MS = 12000;
/** How long before the playhead needs a chunk it must already be fetchable: the jitter plus a fetch. */
export const LEAD_MS = 2000;
/** Each session's fetch moment is late by a random 0..JITTER_MS (spreads a crowd's CDN misses). */
export const JITTER_MS = 800;
export const HEAD_EVERY_MS = 30000;
export const BUCKET_MS = 3000;
/** Server lane, 2026-10-14: one react POST per 2.5 s per spectator (a 429 keeps the batch). */
export const REACT_GAP_MS = 2500;
export const STALE_FIRE_MS = 2500;
export const TAPE_TICK_MS = 100;
/** Behind by more than this many chunks (a tab that slept): re-snap instead of streaming. */
export const RESNAP_CHUNKS = 2;
const NOT_READY_RETRY_MS = 400;
const RETRY_MS = [500, 1000, 2000, 4000, 8000, 15000];

/** The refusals that end a watch: no retry will change them. */
export const CLOSED = new Set(['not_watchable', 'your_match', 'not_found', 'not_deployed', 'unauthorized']);
const REACTION_IDS = ['fire', 'wow', 'laugh', 'clap', 'heart', 'eyes'];

const num = (v, d = null) => (Number.isFinite(Number(v)) && v !== null && v !== '' ? Number(v) : d);
const clamp01 = (v) => Math.min(1, Math.max(0, Number.isFinite(v) ? v : 0));
const sideOfPly = (ply) => (ply % 2 === 0 ? 'w' : 'b');

/** Which chunk holds server time t. */
export const chunkOf = (t, t0, c = CHUNK_MS) => Math.floor((t - t0) / c);
/** The last chunk that is ready at server time `serverNow`: its end is older than the delay. */
export const readyThrough = (serverNow, t0, c = CHUNK_MS, delay = WATCH_DELAY_MS) => Math.floor((serverNow - delay - t0) / c) - 1;
/** How far behind the live board the stands play. */
export const lagOf = (delay = WATCH_DELAY_MS, c = CHUNK_MS, lead = LEAD_MS) => delay + c + lead;

/** An end off the wire ('1-0' | '0-1' | '1/2-1/2' + reason) in hotseat's ending shape. */
export function endOf(ev) {
  if (!ev || typeof ev !== 'object') return null;
  const r = String(ev.result || '');
  const winner = r === '1-0' ? 'w' : r === '0-1' ? 'b' : null;
  const reason = String(ev.reason || (winner ? 'resign' : 'draw'));
  return endingFrom({ status: 'done', result: { winner, reason, ended_ms: num(ev.at) } });
}

/** Time order; at the same instant a move comes before the fx and the end it carries (a mate rides its move). */
const KIND_ORDER = { move: 0, heat: 1, fire: 1, end: 2 };
const byTime = (a, b) => (a.at - b.at) || (KIND_ORDER[a.k] - KIND_ORDER[b.k]) || (a.i - b.i);

/** A chunk's events -> one flat, time-ordered list of items the tick can dispatch. */
export function expand(events) {
  const out = [];
  let i = 0;
  for (const ev of Array.isArray(events) ? events : []) {
    if (!ev || typeof ev !== 'object') continue;
    const at = num(ev.at);
    if (at === null) continue;
    if (ev.k === 'move' && typeof ev.uci === 'string') {
      out.push({ at, k: 'move', seq: num(ev.seq), uci: ev.uci, clk: Array.isArray(ev.clk) ? ev.clk : null, i: i++ });
    } else if (ev.k === 'fx' && (ev.side === 'w' || ev.side === 'b')) {
      const ply = num(ev.ply);
      for (const h of Array.isArray(ev.heat) ? ev.heat : []) {
        if (!Array.isArray(h) || num(h[0]) === null || num(h[1]) === null) continue;
        out.push({ at: at + Math.max(0, num(h[0])), k: 'heat', side: ev.side, ply, heat: clamp01(num(h[1]) / 99), i: i++ });
      }
      for (const f of Array.isArray(ev.fired) ? ev.fired : []) {
        if (!Array.isArray(f) || num(f[0]) === null || typeof f[1] !== 'string') continue;
        const hold = num(f[2], 0);
        out.push({ at: at + Math.max(0, num(f[0])), k: 'fire', side: ev.side, ply, kind: f[1], holdMs: hold > 0 ? hold : 0, i: i++ });
      }
    } else if (ev.k === 'end') {
      const end = endOf(ev);
      if (end) out.push({ at, k: 'end', end, i: i++ });
    }
  }
  return out.sort(byTime);
}

/**
 * The clocks on the delayed timeline. `playhead()` is the tape's; `set` stamps
 * the balances true at server time `stamp` with `turn` to move.
 */
export function createTapeClock({ playhead, perSideMs = 300000 } = {}) {
  const base = { w: perSideMs, b: perSideMs };
  let total = perSideMs;
  let turn = null;
  let stamp = 0;
  let running = false;
  let synced = false;
  const ph = () => { try { return Number(playhead()) || 0; } catch { return 0; } };

  function remaining(side) {
    const b = base[side] === undefined ? 0 : base[side];
    if (!running || side !== turn) return b;
    return Math.max(0, b - Math.max(0, ph() - stamp));
  }
  function set(w, b, t, at) {
    if (num(w) !== null) base.w = Math.max(0, num(w));
    if (num(b) !== null) base.b = Math.max(0, num(b));
    turn = (t === 'w' || t === 'b') ? t : turn;
    if (num(at) !== null) stamp = num(at);
    total = Math.max(total, base.w, base.b);
    running = true;
    synced = true;
  }
  function stop() {
    if (running) { base.w = remaining('w'); base.b = remaining('b'); }
    running = false;
    turn = null;
  }
  return {
    set,
    setTotal(ms) { if (Number.isFinite(Number(ms)) && Number(ms) > 0) total = Number(ms); },
    remaining,
    stop,
    serverNow: ph,
    turnElapsedMs: () => (running && turn ? Math.max(0, ph() - stamp) : 0),
    snapshot: () => ({ w: remaining('w'), b: remaining('b'), total, active: running ? turn : null }),
    isRunning: () => running,
    hasSynced: () => synced,
    flagged: () => (running && turn && remaining(turn) === 0 ? turn : null),
    offset: () => 0,
    discountMode: () => ({ mode: 'tape', measured: true }),
    // the surface of game/clock.js, as no-ops: nobody in the stands presses a clock
    sync() {}, noteServerNow() {}, start() {}, press() {}, credit() {}, debit() {}, reset() {},
  };
}

/**
 * @param {object} o
 * @param {string} o.matchId
 * @param {object} [o.api]        net/api.js, swappable for tests
 * @param {() => number} [o.now]  monotonic clock
 * @param {boolean} [o.timers=true] run the tick on an interval (tests drive tick() by hand)
 * @param {object} [o.on]         callbacks: state, move, end, heat, fire, soon, head, closed, reactions, net
 */
export function createTape({ matchId, api = defaultApi, now = null, timers = true, on = {}, random = Math.random }) {
  if (!matchId) throw new Error('createTape: matchId is required');
  const mono = now || ((typeof performance !== 'undefined' && performance.now) ? () => performance.now() : () => Date.now());
  const call = (name, p) => { const fn = on[name]; if (typeof fn !== 'function') return undefined; try { return fn(p); } catch (e) { try { console.warn('[pbp/tape] ' + name + ': ' + (e && e.message)); } catch { /* no console */ } return undefined; } };

  let t0 = null;
  let chunkMs = CHUNK_MS;
  let delayMs = WATCH_DELAY_MS;
  let offset = 0;
  let head = null;              // the last GET watch answer we adopted

  let queue = [];
  let nextChunk = 0;
  let caught = false;
  let catching = false;
  let fetching = false;
  let retryAt = 0;
  let miss = 0;
  let notReady = 0;
  let lastSeq = 0;
  let moveCount = 0;
  let over = false;
  let closed = null;
  let disposed = false;
  let ticker = null;
  let lastHead = -Infinity;
  let heading = false;
  let lastReactPoll = -Infinity;
  let lastBucket = null;
  let pending = {};
  let lastSent = -Infinity;
  let sending = false;
  let flushTimer = 0;
  const jitterMs = (() => { try { return Math.max(0, Math.min(JITTER_MS, Math.floor(Number(random()) * JITTER_MS))); } catch { return 0; } })();

  const serverNow = () => mono() + offset;
  const lagMs = () => lagOf(delayMs, chunkMs);
  const playhead = () => serverNow() - lagMs();
  const clock = createTapeClock({ playhead });

  /* -------------------------------------------------------------- the head */

  function adoptHead(data, sentAt, gotAt) {
    if (!data || typeof data !== 'object') return false;
    const start = num(data.t0_ms);
    if (start === null) return false;
    t0 = start;
    chunkMs = Math.max(1000, num(data.chunk_ms, CHUNK_MS));
    delayMs = Math.max(0, num(data.delay_ms, WATCH_DELAY_MS));
    const sn = num(data.server_now_ms);
    // erring late: read the server as half a round trip BEHIND what it said when the answer landed
    if (sn !== null) offset = (sn - Math.max(0, gotAt - sentAt) / 2) - gotAt;
    const tc = data.time_control && typeof data.time_control === 'object' ? data.time_control : null;
    if (tc && num(tc.initial_ms)) clock.setTotal(num(tc.initial_ms));
    head = data;
    call('head', data);
    return true;
  }

  async function fetchHead() {
    if (heading || disposed || closed) return false;
    heading = true;
    lastHead = mono();
    const sentAt = mono();
    let res;
    try { res = await api.watchHead(matchId); } finally { heading = false; }
    if (disposed || closed) return false;
    if (!res || !res.ok) {
      const why = res && res.error;
      // a game that is gone once its end has played is not news; before that it is
      if (CLOSED.has(why) && !(why === 'not_found' && over)) close(why);
      return false;
    }
    if (!adoptHead(res.data, sentAt, mono())) { close('not_deployed'); return false; }
    return true;
  }

  /* ------------------------------------------------------------- catch-up */

  function initialState() {
    const tc = head && head.time_control && typeof head.time_control === 'object' ? head.time_control : {};
    const init = num(tc.initial_ms, clock.snapshot().total);
    return { w: init, b: init, turn: 'w', stamp: t0 };
  }

  function failed(res, what) {
    const why = (res && res.error) || 'offline';
    if (CLOSED.has(why)) { close(why); return; }
    // `gone` (410 on upto: a long game's trimmed opening) clears as the playhead moves on: retried like an outage
    const wait = why === 'not_ready' ? NOT_READY_RETRY_MS : RETRY_MS[Math.min(miss, RETRY_MS.length - 1)];
    if (why === 'not_ready') {
      // a chunk the server calls early more than a few times running: our offset is off, ask the head again
      if (++notReady >= 5) { notReady = 0; fetchHead(); }
    } else {
      miss++;
      call('net', { state: 'retrying', error: why, attempt: miss, inMs: wait, what });
    }
    retryAt = mono() + wait;
  }

  /** Snap to the playhead: upto the chunk before it, that chunk itself, fold the past, queue the rest. */
  async function catchUp() {
    if (catching || disposed || closed || t0 === null) return false;
    catching = true;
    caught = false;
    try {
      const P = chunkOf(playhead(), t0, chunkMs);
      let snap = null;
      if (P >= 1) {
        const res = await api.watchUpto(matchId, P - 1);
        if (disposed || closed) return false;
        if (!res || !res.ok || !res.data) { failed(res, 'upto'); return false; }
        snap = res.data;
      }
      let items = [];
      let fetched = P - 1;
      if (P >= 0) {
        const res = await api.watchChunk(matchId, P);
        if (disposed || closed) return false;
        if (res && res.ok && res.data) { items = expand(res.data.events); fetched = P; }
        else if (!res || res.error !== 'not_ready') { failed(res, 'chunk'); return false; }
      }
      // the snapshot, then chunk P's past folded on top of it
      const moves = [];
      const seqs = [];
      for (const m of Array.isArray(snap && snap.moves) ? snap.moves : []) {
        if (m && typeof m.uci === 'string') { moves.push(m.uci); seqs.push(num(m.seq)); }
      }
      let clk = null;
      const sc = snap && snap.clocks;
      if (sc && num(sc.w_ms) !== null) clk = { w: num(sc.w_ms), b: num(sc.b_ms), turn: sc.turn === 'b' ? 'b' : (sc.turn === 'w' ? 'w' : sideOfPly(moves.length)), stamp: num(snap.to_ms, t0) };
      let end = snap && snap.end ? endOf(snap.end) : null;
      // seq is the match's EVENT seq (draw offers count too) and a trimmed opening sends it null:
      // it only dedupes here; a missed move shows up as one the rules refuse
      lastSeq = seqs.reduce((m, s) => (s !== null && s > m ? s : m), 0);
      const ph = playhead();
      let heat = null;
      const rest = [];
      for (const it of items) {
        if (it.at > ph || end) { if (!end) rest.push(it); continue; }
        if (it.k === 'move') {
          if (it.seq !== null && it.seq <= lastSeq) continue;
          moves.push(it.uci);
          if (it.seq !== null) lastSeq = it.seq;
          const c = it.clk || [];
          clk = { w: num(c[0], clk ? clk.w : null), b: num(c[1], clk ? clk.b : null), turn: sideOfPly(moves.length), stamp: it.at };
          heat = null;
        } else if (it.k === 'heat') {
          if (it.ply === null || it.ply === moves.length) heat = { side: it.side, heat: it.heat };
        } else if (it.k === 'end') end = it.end;
        // a fire behind the playhead is history: it is not replayed at a join
      }
      if (!clk) clk = initialState();
      moveCount = moves.length;
      clock.set(clk.w, clk.b, clk.turn, clk.stamp);
      queue = rest;
      nextChunk = Math.max(0, fetched + 1);
      retryAt = 0;
      const okState = call('state', { moves, seq: lastSeq, end, heat, clocks: clk });
      if (end && okState !== false) { miss = 0; notReady = 0; finish(end); return true; }
      if (okState === false) {
        // a snapshot that will not replay is the server's to fix; try again a little later, never spin
        retryAt = mono() + RETRY_MS[Math.min(miss++, RETRY_MS.length - 1)];
        return false;
      }
      // only a snapshot that really applied ends the backoff
      miss = 0;
      notReady = 0;
      caught = true;
      announceSoon(rest, ph);
      return true;
    } finally { catching = false; }
  }

  function announceSoon(items, ph) {
    for (const it of items) if (it.k === 'fire' && it.at > ph && (it.ply === null || it.ply >= moveCount)) call('soon', { side: it.side, kind: it.kind, inMs: Math.round(it.at - ph) });
  }

  /* --------------------------------------------------------------- chunks */

  async function fetchChunk(n) {
    fetching = true;
    let res;
    try { res = await api.watchChunk(matchId, n); } finally { fetching = false; }
    if (disposed || closed || !caught || n !== nextChunk) return;
    if (!res || !res.ok || !res.data) { failed(res, 'chunk'); return; }
    miss = 0;
    notReady = 0;
    const items = expand(res.data.events);
    queue = queue.concat(items).sort(byTime);
    nextChunk = n + 1;
    announceSoon(items, playhead());
  }

  function finish(end) {
    if (over) return;
    over = true;
    clock.stop();
    queue = [];
    stopTicker();
    call('end', end);
    // a cheer still waiting for its gap goes out after the end too
    flushReactions();
    laterFlush();
  }

  function close(reason) {
    if (closed || disposed) return;
    closed = reason || 'closed';
    stopTicker();
    call('closed', closed);
  }

  /* ------------------------------------------------------------ reactions */

  async function pollReactions() {
    lastReactPoll = mono();
    const b = Math.floor(serverNow() / BUCKET_MS) - 1;
    if (b === lastBucket || b < 0) return;
    lastBucket = b;
    let res;
    try { res = await api.reactions(matchId, b); } catch { res = null; }
    if (disposed || closed || !res || !res.ok || !res.data) return;
    const r = res.data.r && typeof res.data.r === 'object' ? res.data.r : {};
    const counts = {};
    for (const k of REACTION_IDS) { const n = Math.trunc(Number(r[k]) || 0); if (n > 0) counts[k] = n; }
    if (Object.keys(counts).length) call('reactions', counts);
  }

  /** A click in the stands. Batched: at most one POST per REACT_GAP_MS. Still taken after the end. */
  function react(id) {
    if (!REACTION_IDS.includes(id) || closed || disposed) return false;
    pending[id] = Math.min(10, (pending[id] || 0) + 1);
    flushReactions();
    laterFlush();
    return true;
  }

  /** Once the tick has stopped (the game is over), a timer of its own sends what is waiting. */
  function laterFlush() {
    if (!timers || flushTimer || ticker || disposed || !Object.keys(pending).length) return;
    const wait = Math.max(0, lastSent + REACT_GAP_MS - mono()) + 10;
    flushTimer = setTimeout(() => { flushTimer = 0; flushReactions().then(laterFlush, laterFlush); }, wait);
    if (flushTimer.unref) flushTimer.unref();
  }

  async function flushReactions() {
    if (sending || !Object.keys(pending).length || mono() - lastSent < REACT_GAP_MS) return;
    sending = true;
    const counts = pending;
    pending = {};
    lastSent = mono();
    let res = null;
    try { res = await api.react(matchId, counts); } catch { res = null; } finally { sending = false; }
    // rate limited: keep the batch and send it with the next one (clamped), never drop a cheer for a 429
    if (res && res.error === 'rate_limited') {
      for (const k of Object.keys(counts)) pending[k] = Math.min(10, (pending[k] || 0) + counts[k]);
    }
    laterFlush();
  }

  /* ----------------------------------------------------------------- tick */

  function dispatch(ph) {
    while (queue.length && queue[0].at <= ph && !over && !closed && !disposed) {
      const it = queue.shift();
      if (it.k === 'move') {
        if (it.seq !== null && it.seq <= lastSeq) continue;
        const okMove = call('move', it);
        if (okMove === false) { resnap(); return; }
        moveCount++;
        if (it.seq !== null) lastSeq = it.seq;
        const c = it.clk || [];
        clock.set(num(c[0]), num(c[1]), sideOfPly(moveCount), it.at);
      } else if (it.k === 'heat') {
        if (it.ply === null || it.ply === moveCount) call('heat', { side: it.side, heat: it.heat });
      } else if (it.k === 'fire') {
        if ((it.ply === null || it.ply === moveCount) && ph - it.at <= STALE_FIRE_MS) call('fire', { side: it.side, kind: it.kind, holdMs: it.holdMs });
      } else if (it.k === 'end') {
        finish(it.end);
        return;
      }
    }
  }

  function resnap() {
    caught = false;
    queue = [];
    catchUp();
  }

  function tick() {
    if (disposed || closed) return;
    if (over) { if (Object.keys(pending).length) flushReactions(); return; }
    const t = mono();
    if (t - lastHead >= HEAD_EVERY_MS) fetchHead();
    if (t - lastReactPoll >= BUCKET_MS) pollReactions();
    if (Object.keys(pending).length) flushReactions();
    if (t0 === null) return;
    if (!caught) { if (!catching && t >= retryAt) catchUp(); return; }
    const ph = playhead();
    // asleep far behind the playhead (a hidden tab): snap, do not stream a minute of chess at once
    if (nextChunk < chunkOf(ph, t0, chunkMs) - RESNAP_CHUNKS) { resnap(); return; }
    dispatch(ph);
    if (!caught || over || closed) return;
    if (!fetching && t >= retryAt && nextChunk <= readyThrough(serverNow() - jitterMs, t0, chunkMs, delayMs)) fetchChunk(nextChunk);
  }

  function stopTicker() { if (ticker) { clearInterval(ticker); ticker = null; } }

  /**
   * Join: adopt the GET watch answer the door already has (else ask), then snap.
   * Answers whether the tape is running.
   */
  async function start(initial = null) {
    if (disposed) return false;
    if (timers && !ticker) { ticker = setInterval(tick, TAPE_TICK_MS); if (ticker.unref) ticker.unref(); }
    if (initial && num(initial.t0_ms) !== null) {
      const t = mono();
      adoptHead(initial, t, t);
      lastHead = t;
    } else if (!(await fetchHead())) return false;
    if (disposed || closed) return false;
    return catchUp();
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    stopTicker();
    if (flushTimer) { clearTimeout(flushTimer); flushTimer = 0; }
    clock.stop();
    queue = [];
  }

  return {
    start, tick, dispose, react, clock,
    serverNow, playhead, lagMs,
    jitterMs: () => jitterMs,
    pendingReactions: () => ({ ...pending }),
    delayMs: () => delayMs,
    watchers: () => Math.max(0, Math.trunc(num(head && head.watchers, 0))),
    head: () => head,
    over: () => over,
    closed: () => closed,
    caught: () => caught,
    queued: () => queue.length,
    nextChunk: () => nextChunk,
    moveCount: () => moveCount,
    _catchUp: catchUp,
    _fetchHead: fetchHead,
    _pollReactions: pollReactions,
    _flushReactions: flushReactions,
  };
}
