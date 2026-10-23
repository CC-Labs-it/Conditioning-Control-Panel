/* hud/lobbyMath.js - the pure half of the online lobby (hud/lobby.js draws it). No DOM, no three, no clock:
 * every time comes in as an argument, in SERVER ms (NET.md section 2).
 *
 * Open tables rows, the host form and its POST body, seats from `seats` / `seat` messages, badges (host, ready,
 * paused, away, left), looks (ids from Astra's wardrobe and vehicle files, unknown ids fall back), the countdown,
 * results lines, the copy for lobby errors, aborts and close codes, and the race link (token, note, claim step).
 * Copy is dry and short.
 */
import { LOOK_OUTFITS, LOOK_VEHICLES, DEFAULT_LOOK, normLook } from '../net/protocol.js';
import { formatTime, ordinal, BOT_NAMES } from './format.js';
import { LINK_TOKEN } from '../net/lobbyApi.js';

export const POLL_MS = 15000;
export const PRIME_TIER = 2;
export const TRACKS = Object.freeze({ threshold: 'Threshold' });
export const LOOK_KEY = 'glassroad.look.v1';

const int = (v, lo, hi, d) => { const n = Math.round(Number(v)); return Number.isFinite(n) ? Math.min(hi, Math.max(lo, n)) : d; };
const ROOM_ID = /^[a-z0-9]{12}$/;

// ---- who may do what ---------------------------------------------------------------------------

export const isPrime = (identity) => !!identity && Number(identity.tier) >= PRIME_TIER;
export const signedIn = (identity) => !!identity && typeof identity.displayName === 'string' && identity.displayName.length > 0;

/** Host doors: an open table needs Prime (shown locked, never hidden); a friends room needs a sign in. */
export function hostGate(identity) {
  if (!signedIn(identity)) return { open: 'signin', friends: 'signin' };
  return { open: isPrime(identity) ? 'ok' : 'locked', friends: 'ok' };
}

// ---- open tables -------------------------------------------------------------------------------

export const trackName = (id) => TRACKS[id] || (typeof id === 'string' && id ? id.charAt(0).toUpperCase() + id.slice(1) : 'Track');

/** Seconds left of a row's auto start, counted from the list's serverMs to now. null = no auto start. */
export function rowStartsIn(row, listMs, nowMs) {
  if (!row || row.startsIn == null || !(row.startsIn >= 0)) return null;
  return Math.max(0, Math.round(row.startsIn - Math.max(0, (nowMs - listMs) / 1000)));
}
export const startsLabel = (s) => (s == null ? 'Host starts' : s <= 0 ? 'Starting' : s < 60 ? 'Starts in ' + s + ' s' : 'Starts in ' + Math.ceil(s / 60) + ' min');

/**
 * GET /v2/race/open rows to display rows: bad rows dropped, numbers clamped, sorted friend first, then open seats,
 * then the soonest start, then the fullest. Never more than 20.
 */
export function tableRows(list, nowMs) {
  const rows = (list && Array.isArray(list.rows) ? list.rows : []).filter((r) => r && ROOM_ID.test(r.roomId));
  const listMs = Number(list && list.serverMs) || nowMs;
  const out = rows.map((r) => {
    const seats = int(r.seats, 2, 8, 8), filled = int(r.filled, 0, seats, 0);
    const left = rowStartsIn(r, listMs, nowMs);
    return {
      roomId: r.roomId, track: trackName(r.track), host: String((r.host && r.host.name) || 'Someone').slice(0, 32),
      seats, filled, full: filled >= seats, laps: int(r.laps, 1, 9, 3), staked: !!r.staked, friend: !!r.friend,
      startsIn: left, seatsLabel: filled + '/' + seats, startsLabel: startsLabel(left),
    };
  });
  const soon = (s) => (s == null ? 1e9 : s);
  out.sort((a, b) => (b.friend - a.friend) || (a.full - b.full) || (soon(a.startsIn) - soon(b.startsIn)) || (b.filled - a.filled));
  return out.slice(0, 20);
}

/** True when the list is due again (15 s, like Goon). */
export const pollDue = (lastAt, now, every = POLL_MS) => !(now - lastAt < every);
/** Quick match retry delay: the lobby's retryMs, clamped to 1..10 s. */
export const quickDelay = (retryMs) => int(retryMs, 1000, 10000, 3000);

// ---- host form ---------------------------------------------------------------------------------

export const HOST_DEFAULTS = Object.freeze({ mode: 'open', laps: 3, seats: 8, botFill: true, laneKeeper: false });
const HOST_STEPS = { laps: [1, 9], seats: [2, 8] };

export function cleanHost(f = {}) {
  const mode = f.mode === 'friends' ? 'friends' : 'open';
  return {
    mode, laps: int(f.laps, 1, 9, 3), seats: int(f.seats, 2, 8, 8), botFill: f.botFill !== false,
    laneKeeper: mode === 'friends' ? !!f.laneKeeper : false,
  };
}
/** One step of a host form row (dir +1 / -1). Numbers wrap; switches flip; lane keeper stays off on open tables. */
export function stepHost(form, key, dir = 1) {
  const f = cleanHost(form);
  if (key === 'mode') f.mode = f.mode === 'open' ? 'friends' : 'open';
  else if (HOST_STEPS[key]) { const [lo, hi] = HOST_STEPS[key], span = hi - lo + 1; f[key] = lo + (((f[key] - lo + (dir < 0 ? -1 : 1)) % span) + span) % span; }
  else if (key === 'botFill' || key === 'laneKeeper') f[key] = !f[key];
  return cleanHost(f);
}
/** POST /v2/race/open body (NET.md section 12). No stakes here: they wait for RACE_STAKES. */
export function hostBody(form, look, track = 'threshold') {
  const f = cleanHost(form);
  return { track, mode: f.mode, seats: f.seats, laps: f.laps, botFill: f.botFill, laneKeeper: f.laneKeeper, look: normLook(look) };
}

// ---- seats -------------------------------------------------------------------------------------

const SEAT_FIELDS = ['ready', 'idle', 'left', 'paused'];
const blankSeat = (seat) => ({ seat, kind: 'empty', name: '', platform: null, look: { ...DEFAULT_LOOK }, host: false, ready: false, idle: false, left: false, paused: false });

/** Eight seats from a `seats` list (missing seats are empty). Paused flags carry over from `prev`. */
export function applySeats(list, prev = null) {
  const out = Array.from({ length: 8 }, (_, i) => blankSeat(i));
  for (const s of Array.isArray(list) ? list : []) {
    if (!s || !(s.seat >= 0 && s.seat < 8)) continue;
    const kind = s.kind === 'human' || s.kind === 'bot' ? s.kind : 'empty';
    out[s.seat] = {
      seat: s.seat, kind, name: String(s.name || '').slice(0, 32), platform: s.platform || null, look: normLook(s.look),
      host: !!s.host, ready: !!s.ready, idle: !!s.idle, left: !!s.left, paused: !!(prev && prev[s.seat] && prev[s.seat].paused),
    };
  }
  return out;
}
/** One `seat` message applied (a new array, the old one untouched). */
export function applySeat(seats, msg) {
  if (!msg || !(msg.seat >= 0 && msg.seat < 8)) return seats;
  const out = seats.slice(), s = { ...(out[msg.seat] || blankSeat(msg.seat)) };
  for (const k of SEAT_FIELDS) if (typeof msg[k] === 'boolean') s[k] = msg[k];
  if (msg.look) s.look = normLook(msg.look);
  if (msg.left) { s.ready = false; s.paused = false; }
  out[msg.seat] = s;
  return out;
}

/** The name a seat shows: its ticket name, our short names for bots, 'Open seat' when empty. */
export function seatName(s) {
  if (!s || s.kind === 'empty') return 'Open seat';
  if (s.kind === 'bot') return BOT_NAMES[s.seat % BOT_NAMES.length];
  return s.name || 'Racer';
}
/** Badges in a fixed order. `phase` racing hides ready. */
export function seatBadges(s, phase = 'lobby') {
  if (!s || s.kind === 'empty') return [];
  const b = [];
  if (s.host) b.push('host');
  if (s.kind === 'bot') b.push('bot');
  if (s.left) { b.push('left'); return b; }
  if (s.paused) b.push('paused');
  else if (s.idle) b.push('away');
  if (s.ready && phase === 'lobby' && s.kind === 'human') b.push('ready');
  return b;
}
/** The one in-race tag a remote kart wears ('paused', 'away', 'left'), by seat. Boot feeds it to the kart labels. */
export function raceTags(seats) {
  const out = {};
  for (const s of seats || []) {
    if (!s || s.kind === 'empty') continue;
    const t = s.left ? 'left' : s.paused ? 'paused' : s.idle ? 'away' : null;
    if (t) out[s.seat] = t;
  }
  return out;
}
export function seatRows(seats, mySeat, phase = 'lobby', cap = 8) {
  return (seats || []).slice(0, cap).map((s) => ({ seat: s.seat, n: s.seat + 1, name: seatName(s), kind: s.kind, me: s.seat === mySeat, badges: seatBadges(s, phase) }));
}
export const humans = (seats) => (seats || []).filter((s) => s.kind === 'human' && !s.left);
export const readyCount = (seats) => humans(seats).filter((s) => s.ready).length;
/** The host may press Start in the lobby phase with at least one human seated. */
export const canStart = (seats, mySeat, phase) => phase === 'lobby' && !!(seats && seats[mySeat] && seats[mySeat].host) && humans(seats).length > 0;

// ---- looks -------------------------------------------------------------------------------------

const pretty = (id) => String(id).replace(/^emi-(outfit|vehicle)-/, '').split('-').map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
/**
 * The picker lists from Astra's files (art/models/emi-bot-wardrobe.json `outfits`, emi-vehicle-variants.json
 * `vehicles`). Only ids the wire knows are offered; a known id missing from a file still shows with a plain label.
 */
export function lookChoices(wardrobe = null, vehicles = null) {
  const label = (list, id) => { const e = (Array.isArray(list) ? list : []).find((x) => x && x.id === id); return e && typeof e.label === 'string' ? e.label : pretty(id); };
  const ow = wardrobe && wardrobe.outfits, vv = vehicles && vehicles.vehicles;
  return {
    outfit: [{ id: null, label: 'None' }, ...LOOK_OUTFITS.map((id) => ({ id, label: label(ow, id) }))],
    vehicle: LOOK_VEHICLES.map((id) => ({ id, label: id === DEFAULT_LOOK.vehicle ? 'Teacup' : label(vv, id) })),
  };
}
/** One step through a look row; an unknown current id starts from the default. */
export function cycleLook(look, key, dir, choices = lookChoices()) {
  const l = normLook(look), list = choices[key];
  if (!list || !list.length) return l;
  const i = Math.max(0, list.findIndex((c) => c.id === l[key]));
  const n = list.length;
  return { ...l, [key]: list[(((i + (dir < 0 ? -1 : 1)) % n) + n) % n].id };
}
export function lookLabel(look, key, choices = lookChoices()) {
  const l = normLook(look), c = (choices[key] || []).find((x) => x.id === l[key]);
  return c ? c.label : 'None';
}
export function loadLook(storage) {
  try { const raw = storage && storage.getItem(LOOK_KEY); return normLook(raw ? JSON.parse(raw) : null); } catch (e) { return normLook(null); }
}
export function saveLook(storage, look) {
  try { storage.setItem(LOOK_KEY, JSON.stringify(normLook(look))); return true; } catch (e) { return false; }
}

// ---- time --------------------------------------------------------------------------------------

/**
 * The lobby countdown on server time. Before countAt: 'Grid in N'. countAt..startAt: the race view counts 3, 2, 1
 * (the song's 0 s is countAt); the lobby only says 'Go' after startAt.
 */
export function countdown(nowMs, { countAt, startAt } = {}) {
  if (!(countAt > 0) || !(startAt > 0)) return { phase: 'none', n: 0, label: '' };
  if (nowMs < countAt) { const n = Math.ceil((countAt - nowMs) / 1000); return { phase: 'grid', n, label: 'Grid in ' + n }; }
  if (nowMs < startAt) { const n = Math.ceil((startAt - nowMs) / 1000); return { phase: 'count', n, label: String(n) }; }
  return { phase: 'go', n: 0, label: 'Go' };
}
/** Server ms now from the room clock, or from a welcome's serverMs seen at local time `seenAt`. */
export function serverNow(localNow, room = null, welcome = null, seenAt = 0) {
  if (room && typeof room.now === 'function') { const v = room.now(); if (Number.isFinite(v)) return v; }
  if (welcome && Number.isFinite(welcome.serverMs)) return welcome.serverMs + (localNow - seenAt);
  return localNow;
}

// ---- results -----------------------------------------------------------------------------------

/** `finish` rows to card lines: place, name, time or 'no time', pops. Ours reads You. */
export function resultLines(rows, mySeat) {
  return (Array.isArray(rows) ? rows : []).slice().sort((a, b) => a.place - b.place).map((r) => {
    const o = ordinal(r.place), me = r.seat === mySeat;
    const name = me ? 'You' : r.bot ? BOT_NAMES[r.seat % BOT_NAMES.length] : String(r.name || 'Racer').slice(0, 32);
    return {
      place: o.n, suf: o.suf, name, me, bot: !!r.bot,
      time: r.finished && r.timeMs != null ? formatTime(r.timeMs / 1000) : 'no time',
      best: r.bestLapMs > 0 ? formatTime(r.bestLapMs / 1000) : '',
      pops: int(r.pops, 0, 65535, 0),
    };
  });
}
export const popsLabel = (n) => (n === 1 ? '1 pop' : n + ' pops');

// ---- copy --------------------------------------------------------------------------------------

const ERR = {
  bad: 'That did not work', auth: 'Sign in first', tier: 'Open tables are a Prime thing', friends: 'Friends of the host only',
  gone: 'That table is gone', full: 'That table is full', started: 'That race already started', 'stakes-off': 'No stakes right now',
  rate: 'Slow down a little', net: 'No connection', off: 'Online is not set up here', preview: 'Online racing is in a closed preview.',
};
export const errorCopy = (code) => ERR[code] || ERR.bad;
const ABORT = {
  hello: 'The room did not hear us', ticket: 'The room refused the ticket', expired: 'The ticket ran out', replay: 'That ticket was used',
  version: 'This build is out of date', config: 'The room changed', full: 'The room is full', started: 'The race already started',
  closed: 'The room closed', server: 'The room fell over',
};
export const abortCopy = (reason) => ABORT[reason] || 'The room closed';
const CLOSE = { 4000: 'Race over', 4001: ABORT.hello, 4002: 'The room hung up', 4003: ABORT.ticket, 4004: ABORT.full, 4009: 'Opened somewhere else', 4010: ABORT.closed, 4011: ABORT.server };
export const closeCopy = (code) => CLOSE[code] || 'Lost the room';
export const BADGE_COPY = Object.freeze({ host: 'host', bot: 'bot', ready: 'ready', paused: 'paused', away: 'away', left: 'left' });

// ---- race a friend through a link (NET.md section 13) -----------------------------------------

/** The `r` token from a query string, or null when it is missing or the wrong shape. */
export function linkToken(search) {
  try { const t = new URLSearchParams(search || '').get('r'); return t && LINK_TOKEN.test(t) ? t : null; } catch (e) { return null; }
}
/** The address without its `r` (boot hands it to history.replaceState after the first claim). */
export function dropLinkParam(href) {
  try { const u = new URL(href); u.searchParams.delete('r'); return u.pathname + u.search + u.hash; } catch (e) { return null; }
}
/** Server ms of a link's `exp` (seconds are taken for seconds). */
const expMs = (exp) => { const n = Number(exp); return !(n > 0) ? 0 : n < 1e11 ? n * 1000 : n; };
/** What is left of a POST /v2/race/link answer: seats and time, as numbers and words ('8 seats, 24 h'). */
export function linkLeft(link, nowMs) {
  const max = int(link?.maxUses, 1, 64, 8), seats = Math.max(0, max - int(link?.uses, 0, 64, 0));
  const ms = Math.max(0, expMs(link?.exp) - nowMs), h = Math.round(ms / 3600000), m = Math.ceil(ms / 60000);
  const time = h >= 1 ? h + ' h' : m > 0 ? m + ' min' : 'no time';
  return { seats, ms, label: (seats === 1 ? '1 seat' : seats + ' seats') + ', ' + time };
}
/** The note after Invite link: copied to the clipboard, or shown in the field below for a manual copy. */
export const linkNote = (link, nowMs, copied) => (copied ? 'Link copied. ' : 'Copy the link below. ') + linkLeft(link, nowMs).label + '.';
/** Only the host of a friends room, in the lobby phase, with the lobby's access().host, gets the button. */
export const linkButton = (cfg, me, access, phase) => !!(cfg && cfg.mode === 'friends' && me && me.host && access && access.host && phase === 'lobby');

const LINK_ERR = {
  tier: 'Racing a friend needs Prime.', gone: 'This link has expired.', full: 'That race is full.',
  preview: 'Online racing is in a closed preview.', started: 'That race already started.',
};
export const linkCopy = (code) => LINK_ERR[code] || errorCopy(code);
export const waitingCopy = (host) => 'Waiting for ' + String(host || 'your friend').slice(0, 32) + ' to open a room.';
/**
 * One claim answer to what the lobby does next: 'seat' (the usual seat body, into the room), 'wait' (poll again
 * after delayMs) or 'refused' (code and its copy). Give it the resolved body, or `{ error }` for a thrown refusal.
 */
export function claimStep(r) {
  if (r && typeof r.roomId === 'string' && typeof r.ticket === 'string') return { kind: 'seat', join: r, host: r.host || null };
  if (r && r.waiting) return { kind: 'wait', host: r.host || null, delayMs: quickDelay(r.retryMs), msg: waitingCopy(r.host) };
  const code = (r && r.error) || 'bad';
  if (code === 'network' || code === 'timeout' || code === 'rate') return { kind: 'wait', host: null, delayMs: 3000, msg: errorCopy(code === 'rate' ? 'rate' : 'net') };
  return { kind: 'refused', code, msg: linkCopy(code) };
}
