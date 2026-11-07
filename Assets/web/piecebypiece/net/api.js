/* ============================================================================
 * net/api.js - every URL, every request shape, every response shape the online
 * game knows about. ONE FILE, on purpose.
 *
 * Written against `pbp-online-api.md` v1 (the server's own spec). That is the
 * whole reason this file exists as a layer rather than as fetch calls scattered
 * through lobby.js and match.js: when the contract moves, adapting is an edit
 * here and nowhere else. Nothing above this file may build a URL, name a
 * header, or read a field off a raw response body.
 *
 * TRANSPORT is bridge.netRequest(method, path, body) -> {status, body}. It
 * never rejects (status 0 = no answer at all) and it owns the auth header and
 * the route-through-the-host decision, so what is layered here is only the
 * shape work: query strings, JSON parsing, and a small result envelope.
 *
 * THE ENVELOPE. Every call resolves
 *
 *     { ok, status, data, error }
 *
 * and never throws for a server outcome. `ok` is a 2xx with parseable JSON;
 * `error` is a machine-readable string on anything else, so a caller can tell
 * "you are offline" (`offline`) from "that match moved on without you"
 * (`stale`) from "the route is not deployed yet" (`not_deployed`) without
 * pattern-matching prose. Sentences are the UI's job, never this file's.
 * ==========================================================================*/

import { netRequest, netInfo, identity } from '../bridge.js';
import { wireLook } from '../skins/look.js';

/** The one place the base path is written down. */
export const BASE = '/v2/pbp';

/**
 * Every route, relative to BASE. Functions where the path carries an id, so a
 * caller never concatenates one itself (and never forgets to encode it).
 */
export const ROUTES = Object.freeze({
  lobbyEnter: () => `${BASE}/lobby/enter`,
  lobbyLeave: () => `${BASE}/lobby/leave`,
  lobby: () => `${BASE}/lobby`,
  join: () => `${BASE}/join`,
  quick: () => `${BASE}/quick`,
  challenge: () => `${BASE}/challenge`,
  challengeAccept: (id) => `${BASE}/challenge/${encodeURIComponent(id)}/accept`,
  challengeDecline: (id) => `${BASE}/challenge/${encodeURIComponent(id)}/decline`,
  challenges: () => `${BASE}/challenges`,
  match: (id) => `${BASE}/match/${encodeURIComponent(id)}`,
  move: (id) => `${BASE}/match/${encodeURIComponent(id)}/move`,
  events: (id) => `${BASE}/match/${encodeURIComponent(id)}/events`,
  resign: (id) => `${BASE}/match/${encodeURIComponent(id)}/resign`,
  draw: (id) => `${BASE}/match/${encodeURIComponent(id)}/draw`,
  claimTimeout: (id) => `${BASE}/match/${encodeURIComponent(id)}/claim_timeout`,
  heartbeat: (id) => `${BASE}/match/${encodeURIComponent(id)}/heartbeat`,
  history: () => `${BASE}/history`,
  watch: (id) => `${BASE}/watch/${encodeURIComponent(id)}`,
  watchUpto: (id, n) => `${BASE}/watch/${encodeURIComponent(id)}/upto/${Math.max(0, Math.trunc(Number(n) || 0))}`,
  watchChunk: (id, n) => `${BASE}/watch/${encodeURIComponent(id)}/chunk/${Math.max(0, Math.trunc(Number(n) || 0))}`,
  react: (id) => `${BASE}/watch/${encodeURIComponent(id)}/react`,
  reactions: (id, b) => `${BASE}/watch/${encodeURIComponent(id)}/reactions/${Math.max(0, Math.trunc(Number(b) || 0))}`,
  fx: (id) => `${BASE}/match/${encodeURIComponent(id)}/fx`,
  ladder: () => `${BASE}/ladder`,
  ladderMe: () => `${BASE}/ladder/me`,
});

/** Machine-readable `error` values. The UI maps these to sentences, not the reverse. */
export const ApiError = Object.freeze({
  /** No answer at all: offline, host gone, or the call timed out. */
  Offline: 'offline',
  /** The route answered 404 with nothing match-shaped in it - server not deployed. */
  NotDeployed: 'not_deployed',
  /**
   * 409. Somebody else's ply landed first (`stale_seq`), or it is not your turn,
   * or the match is over, or your flag fell on the way. Every one of them means
   * the same thing to a client: your idea of this match is wrong. Resync; never
   * retry. The server helpfully sends the whole current state in the body of a
   * `stale_seq`, and `data` carries it.
   */
  Stale: 'stale',
  /** 401/403. The token is missing, expired, or not for this match. */
  Unauthorized: 'unauthorized',
  /** 404 for a match/challenge id that the server does not know. */
  NotFound: 'not_found',
  /** 429. `retryAfterSeconds` on the envelope, when the server said. */
  RateLimited: 'rate_limited',
  /** 5xx. */
  Server: 'server_error',
  /** A 2xx whose body was not the JSON this file expects. */
  Malformed: 'malformed_response',
  /** 403 on a watch route: a private game, a player who said no, or a friend's invite. */
  NotWatchable: 'not_watchable',
  /** 403 on a watch route: the caller is one of the two players. */
  YourMatch: 'your_match',
  /** 425 on a public watch route: that chunk (or bucket) is not old enough yet. Ask again shortly. */
  NotReady: 'not_ready',
  /** 410 on upto: the event list was trimmed past that instant (a long game's opening). Ask later. */
  Gone: 'gone',
});

/**
 * How the caller is named to the server: `unified_id` in the BODY for a POST,
 * in the QUERY STRING for a GET. The contract's §0, and the same split
 * /v2/goon/* and /v2/user/profile already use.
 *
 * The AUTH TOKEN is not here and must never be: the bridge attaches it (or the
 * host does, on its behalf), and nothing above the bridge ever holds it.
 */
function whoami() { return identity().unifiedId || ''; }

function withUid(path) {
  const uid = whoami();
  if (!uid) return path;
  const sep = path.includes('?') ? '&' : '?';
  return `${path}${sep}unified_id=${encodeURIComponent(uid)}`;
}

function withIdentity(body) {
  const uid = whoami();
  const out = Object.assign({}, body || {});
  if (uid) out.unified_id = uid;
  return out;
}

/**
 * The transport, swappable for tests. The smoke run points this at an
 * in-process fake server; nothing else ever calls it.
 * @type {(method:string, path:string, body:object|null) => Promise<{status:number, body:string}>}
 */
let transport = netRequest;

/** Swap the HTTP layer out (smoke tests, offline harnesses). Pass null to restore. */
export function setTransport(fn) { transport = (typeof fn === 'function') ? fn : netRequest; }

function envelope(ok, status, data, error, extra) {
  return Object.assign({ ok, status, data, error: error || null }, extra || {});
}

function classify(status, data) {
  if (status === 0) return ApiError.Offline;
  if (status === 409) return ApiError.Stale;
  // the watch routes word their refusals; the server's word is the one the UI needs there
  if (status === 403 && data && (data.error === ApiError.NotWatchable || data.error === ApiError.YourMatch)) return data.error;
  if (status === 401 || status === 403) return ApiError.Unauthorized;
  if (status === 429) return ApiError.RateLimited;
  if (status === 425) return ApiError.NotReady;
  if (status === 410) return ApiError.Gone;
  if (status === 404) {
    // A route behind a server flag says so in words (`PBP_WATCH` off).
    if (data && data.error === ApiError.NotDeployed) return ApiError.NotDeployed;
    // A 404 that came back with a body the server clearly wrote is "no such
    // match"; a bare one is the route not being there at all. The difference
    // matters while the server is still being deployed piece by piece.
    return (data && typeof data === 'object') ? ApiError.NotFound : ApiError.NotDeployed;
  }
  if (status >= 500) return ApiError.Server;
  return ApiError.Server;
}

/** One call. Resolves an envelope; never rejects. */
async function call(method, path, body, via) {
  let res;
  try { res = await (via || transport)(method, path, body === undefined ? null : body); }
  catch (err) { return envelope(false, 0, null, ApiError.Offline); }
  const status = (res && res.status) | 0;
  let data = null;
  const raw = (res && typeof res.body === 'string') ? res.body : '';
  if (raw) { try { data = JSON.parse(raw); } catch (err) { data = null; } }

  if (status >= 200 && status < 300) {
    if (data === null && raw.trim() !== '') return envelope(false, status, null, ApiError.Malformed);
    return envelope(true, status, data === null ? {} : data, null);
  }
  const extra = {};
  const retry = data && (data.retry_after_seconds !== undefined ? data.retry_after_seconds
    : (data.retry_after !== undefined ? data.retry_after : data.retryAfter));
  if (Number.isFinite(Number(retry))) extra.retryAfterSeconds = Number(retry);
  // The server's own code, when it sent one, kept beside ours rather than
  // instead of it: ours is the one the UI branches on, theirs is the one a log
  // line needs.
  if (data && typeof data.error === 'string') extra.serverError = data.error;
  return envelope(false, status, data, classify(status, data), extra);
}

/* --------------------------------------------------------------- the lobby */

/** @typedef {{initial_ms:number, increment_ms:number}} TimeControl */

/**
 * Normalize a time control to the wire shape, milliseconds both. Clamped to the
 * band the server accepts (contract §0), because a 400 for a number the menu
 * could have rounded is a worse answer than the nearest legal game.
 *
 * QUICK MATCH PAIRS ON AN EXACT PAIR, so a menu and this function disagreeing
 * about what "5+3" means would put two willing players in the same lobby and
 * never introduce them. Everything that builds a time control goes through
 * here for that reason.
 */
export const TC_LIMITS = Object.freeze({ minInitialMs: 60000, maxInitialMs: 3600000, maxIncrementMs: 60000 });

export function timeControl(initialMs, incrementMs) {
  const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, Math.trunc(Number(v) || 0)));
  return {
    initial_ms: clamp(initialMs, TC_LIMITS.minInitialMs, TC_LIMITS.maxInitialMs),
    increment_ms: clamp(incrementMs, 0, TC_LIMITS.maxIncrementMs),
  };
}

/**
 * "Let people watch my games" (Options, default on). Asked at every call that
 * can put this player in a public match; only a NO travels (`watchable: false`),
 * so a server without spectating never sees a field it does not know.
 */
let letPeopleWatch = () => true;
export function setWatchPolicy(fn) { letPeopleWatch = (typeof fn === 'function') ? fn : () => true; }
function withWatch(body) {
  let yes = true;
  try { yes = letPeopleWatch() !== false; } catch { yes = true; }
  return yes ? body : Object.assign(body, { watchable: false });
}

/**
 * What the player's men wear (skins/wear.js), on every call that seats them:
 * `look: { set, banner, insignia }` (Prize Counter contract 4). The server
 * clamps it to the account's grants and hands both seats' looks back in the
 * match state. A bare look sends no field, so an old server never sees one.
 */
let lookSource = () => null;
export function setLookSource(fn) { lookSource = (typeof fn === 'function') ? fn : () => null; }
function withLook(body) {
  let look = null;
  try { look = wireLook(lookSource()); } catch { look = null; }
  return look ? Object.assign(body, { look }) : body;
}

/** Sit down in the lobby, advertising a time control. */
export function lobbyEnter(tc) {
  return call('POST', ROUTES.lobbyEnter(), withLook(withWatch(withIdentity({ time_control: tc }))));
}

/** Stand up again. Best-effort at every call site: a lost goodbye must not wedge anything. */
export function lobbyLeave() {
  return call('POST', ROUTES.lobbyLeave(), withIdentity({}));
}

/** Who else is sitting there. `data.players` is an array; it may be empty. */
export function lobbyList() {
  return call('GET', withUid(ROUTES.lobby()), undefined);
}

/**
 * Sit at one open table. `target` is the `p_...` id off a lobby row; the listing
 * is the host's consent, so this pairs at once with the TABLE's time control:
 * `{ match_id, color }`, or 409 `table_gone`, 400 `self`, 403 `blocked` (the
 * server's word rides on `serverError`).
 */
export function join(target) {
  return call('POST', ROUTES.join(), withLook(withWatch(withIdentity({ target }))));
}

/**
 * Ask for anyone. Answers either `{ match_id, color }` (you have an opponent)
 * or `{ waiting: true }` (you are in the queue; ask again).
 */
export function quick(tc) {
  return call('POST', ROUTES.quick(), withLook(withWatch(withIdentity({ time_control: tc }))));
}

/**
 * Challenge one named player. `target` is an opaque `p_...` id off a lobby row
 * or a display name; the server resolves either, and a client never learns
 * anybody else's unified id to send instead. `color` is 'w', 'b' or 'random'.
 */
export function challenge(target, tc, color) {
  const body = { target };
  if (tc) body.time_control = tc;
  if (color === 'w' || color === 'b' || color === 'random') body.color = color;
  // a challenge to a lobby row is a public, listed game; the server ignores it on a friend's invite
  return call('POST', ROUTES.challenge(), withLook(withWatch(withIdentity(body))));
}

/** Take one up. Answers `{ match_id, color }`. */
export function acceptChallenge(id) {
  return call('POST', ROUTES.challengeAccept(id), withLook(withWatch(withIdentity({}))));
}

/** Turn one down. Either side may: the challenger declining his own is a cancel. */
export function declineChallenge(id) {
  return call('POST', ROUTES.challengeDecline(id), withIdentity({}));
}

/** Challenges pointed at you, and yours pointed at other people. */
export function challenges() {
  return call('GET', withUid(ROUTES.challenges()), undefined);
}

/* --------------------------------------------------------------- one match */

/**
 * The whole match, as the server sees it. This is the resync call and the
 * source of truth for everything: fen, moves, clocks, status, result, seq.
 */
export function match(id) {
  return call('GET', withUid(ROUTES.match(id)), undefined);
}

/**
 * Play a ply. `seqExpected` is the seq this client believes the match is at;
 * a 409 means it was wrong and the answer is to resync, never to retry.
 * On success: `{ ok, seq, fen, clocks }`.
 */
export function move(id, seqExpected, uci) {
  return call('POST', ROUTES.move(id), withIdentity({ seq_expected: seqExpected, uci }));
}

/**
 * How long the server may hold a long poll open. The contract caps it at 8000ms
 * and explains why: proxy/vercel.json declares no `maxDuration`, so these
 * functions die at Vercel's default 10s ceiling, and a longer hold would be
 * killed mid-flight and read here as a network error. If that ceiling is ever
 * raised server-side, this is the number that moves with it and nothing else
 * changes.
 */
export const EVENT_WAIT_MS = 8000;

/**
 * The long poll. Comes back the moment anything happens, or empty-handed when
 * the budget runs out - an empty `events` array is a normal answer, not a
 * failure. `server_now_ms` rides along on every one, which is what keeps the
 * clock offset honest even through a long think, and the call stamps the
 * caller's heartbeat server-side so a client that polls need not also beat.
 */
export function events(id, since, waitMs) {
  const n = Math.max(0, Math.trunc(Number(since) || 0));
  const w = Math.min(EVENT_WAIT_MS, Math.max(0, Math.trunc(Number(waitMs === undefined ? EVENT_WAIT_MS : waitMs) || 0)));
  return call('GET', withUid(`${ROUTES.events(id)}?since=${n}&wait_ms=${w}`), undefined);
}

export function resign(id) { return call('POST', ROUTES.resign(id), withIdentity({})); }

/**
 * `action` is one of 'offer' | 'accept' | 'decline'. Sent BOTH as a flag and as
 * an `action` field, for the same reason whoami() sends the id three ways:
 * whichever the finished server reads, it is there.
 */
export function draw(id, action) {
  const body = { action };
  body[action] = true;
  return call('POST', ROUTES.draw(id), withIdentity(body));
}

/** "His flag fell." The server is the judge; this only asks it to look. */
export function claimTimeout(id) { return call('POST', ROUTES.claimTimeout(id), withIdentity({})); }

/** Keep the seat warm. Best-effort; a missed beat is not an event. */
export function heartbeat(id) { return call('POST', ROUTES.heartbeat(id), withIdentity({})); }

/** Games already finished, newest first. `limit` is clamped to 1..50 server-side. */
export function history(limit) {
  const n = Math.min(50, Math.max(1, Math.trunc(Number(limit) || 50)));
  return call('GET', withUid(`${ROUTES.history()}?limit=${n}`), undefined);
}

/**
 * The ladder (contract section 5): the top 50 rated accounts, PUBLIC and
 * CDN-cached a minute, `{ rows: [{ rank, display_name, rating, games }], total }`,
 * names only. Rides the public road like the tape.
 */
export function ladder() { return call('GET', ROUTES.ladder(), undefined, publicTransport); }

/** Your own place: `{ rated, rating, rank, games, best, total }`; rank null until you are on it. */
export function ladderMe() { return call('GET', withUid(ROUTES.ladderMe()), undefined); }

/* ------------------------------------------------------------- spectating */

/*
 * SPECTATE v2 (2026-10-14): THE TAPE. Everything older than the 15 s delay is
 * immutable, so the game is served in 5 s chunks the CDN caches for good. Two
 * kinds of route, and the difference matters:
 *
 *   AUTH   GET watch/:id (join + 30 s heartbeat, counts the watcher), POST
 *          watch/:id/react, POST match/:id/fx (a seat). Same identity and
 *          transport as every other pbp call.
 *   PUBLIC GET watch/:id/upto/:n, watch/:id/chunk/:n, watch/:id/reactions/:b.
 *          NO auth header, NO unified_id in the query, nothing that varies by
 *          caller: one cached answer serves everybody, which is the whole point.
 *          425 `not_ready` is a normal answer (ask again shortly).
 */

/**
 * The public road. Direct to the server with no credentials when the page knows
 * the server base (a standalone page, or a host that hands one over); else
 * through the bridge, which still works (the server ignores auth on these
 * routes) but cannot share the browser cache. Swappable for tests.
 */
function directPublic(method, path) {
  let base = '';
  try { base = netInfo().serverBase || ''; } catch { base = ''; }
  let f = null;
  try { f = (typeof fetch === 'function') ? fetch : null; } catch { f = null; }
  if (!base || !f) return netRequest(method, path, null);
  return f(base + path, { method, credentials: 'omit', cache: 'default' })
    .then((r) => r.text().then((t) => ({ status: r.status | 0, body: t }), () => ({ status: r.status | 0, body: '' })))
    .catch(() => ({ status: 0, body: '' }));
}
let publicTransport = directPublic;

/** Swap the public road out (smoke tests). Pass null to restore. */
export function setPublicTransport(fn) { publicTransport = (typeof fn === 'function') ? fn : directPublic; }

/** The reaction ids the stands may send. No free text, ever. */
export const REACTIONS = Object.freeze(['fire', 'wow', 'laugh', 'clap', 'heart', 'eyes']);

/** Fx kinds the server accepts on POST match/:id/fx (the ramp's layer names). */
export const FX_KINDS = Object.freeze(['flash', 'gifRain', 'video', 'glitch', 'melt', 'spiral', 'blur', 'overlay']);

/**
 * Join the stands, and the 30 s heartbeat that keeps this watcher counted:
 * `{ ok, match_id, t0_ms, chunk_ms, delay_ms, server_now_ms, ready_chunk,
 * watchers, status, players: { w, b }, time_control }`. server_now_ms is the
 * server's REAL now here (v1 sent the cutoff). 404 `not_deployed` while
 * PBP_WATCH is off; 403 `not_watchable` / `your_match`; 404 `not_found`.
 */
export function watchHead(id) {
  return call('GET', withUid(ROUTES.watch(id)), undefined);
}

/** v1's name for the same route, kept for any caller still using it. */
export const watch = watchHead;

/** PUBLIC. Every move before the end of chunk n, the clocks at that instant, and the end when it falls inside. */
export function watchUpto(id, n) { return call('GET', ROUTES.watchUpto(id, n), undefined, publicTransport); }

/** PUBLIC. Chunk n's events in time order: move, fx, end. */
export function watchChunk(id, n) { return call('GET', ROUTES.watchChunk(id, n), undefined, publicTransport); }

/** PUBLIC. Bucket b's reaction counts, once that 3 s bucket is in the past: `{ b, r }`. */
export function reactions(id, b) { return call('GET', ROUTES.reactions(id, b), undefined, publicTransport); }

/**
 * AUTH, spectators only. `counts` is { id: n }; unknown ids are dropped and each
 * count is clamped 1..10 here as the server will. Resolves null-shaped when
 * there was nothing worth sending.
 */
export function react(id, counts) {
  const r = {};
  for (const k of REACTIONS) {
    const n = Math.trunc(Number(counts && counts[k]) || 0);
    if (n > 0) r[k] = Math.min(10, n);
  }
  if (!Object.keys(r).length) return Promise.resolve(envelope(false, 0, null, 'empty'));
  return call('POST', ROUTES.react(id), withIdentity({ r }));
}

/**
 * SEAT auth. The turn player's Distraction record:
 * `{ ply, from_ms_ago, heat: [[dt, h0_99]], fired: [[dt, kind, hold?]] }`.
 * Only known kinds travel, and nothing about media ever does (no urls, no
 * niches, no ids): the stands fetch their own pictures. 204 on success.
 */
export function postFx(matchId, body) {
  const b = body || {};
  const heat = (Array.isArray(b.heat) ? b.heat : [])
    .filter((x) => Array.isArray(x) && Number.isFinite(Number(x[0])) && Number.isFinite(Number(x[1])))
    .map((x) => [Math.max(0, Math.round(Number(x[0]))), Math.min(99, Math.max(0, Math.round(Number(x[1]))))]);
  const fired = (Array.isArray(b.fired) ? b.fired : [])
    .filter((x) => Array.isArray(x) && Number.isFinite(Number(x[0])) && FX_KINDS.includes(x[1]))
    .map((x) => (Number.isFinite(Number(x[2])) && Number(x[2]) > 0
      ? [Math.max(0, Math.round(Number(x[0]))), x[1], Math.round(Number(x[2]))]
      : [Math.max(0, Math.round(Number(x[0]))), x[1]]));
  return call('POST', ROUTES.fx(matchId), withIdentity({
    ply: Math.max(0, Math.trunc(Number(b.ply) || 0)),
    from_ms_ago: Math.max(0, Math.round(Number(b.from_ms_ago) || 0)),
    heat, fired,
  }));
}
