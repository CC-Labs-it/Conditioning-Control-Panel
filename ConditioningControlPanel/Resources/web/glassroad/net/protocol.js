/* net/protocol.js - the Glassroad wire (NET.md, frozen in B2-0). Pure: no three, no DOM, no clock.
 *
 * Binary frames: an 8 B header + n x 40 B kart records (NET.md section 3). JSON messages: `{ t, ...fields }`
 * checked against the C2R / R2C tables (section 4). The room (`rooms/src/contracts.js`) and the lobby
 * (`proxy/race-contracts.js`) implement the same spec; `net/vectors.json` pins all three to the same bytes.
 * Decoded karts are in game units (metres, m/s, ids), quantised by the encoding.
 */
import { S } from '../sim/state.js';

export const NET_V = 1;
export const HEADER_BYTES = 8;
export const KART_BYTES = 40;
export const MAX_KARTS = 8;
export const BIN = Object.freeze({ BATCH: 1, UPLOAD: 2 });
export const TICK_HZ = 60;
export const COUNT_MS = 3000;
export const MSG_MAX_BYTES = 2048;
export const ROOM_MSG_MAX_CHARS = 4096; // eight complete appearances plus lobby seat metadata

/** Rates and timings (NET.md sections 2, 3, 8). */
export const RATES = Object.freeze({
  upHz: 20, upHzPhone: 15, batchMs: 50, standingsMs: 500, maxUploadsPerS: 25, maxMsgsPerS: 20,
  emoteGapMs: 2000, pingsPerS: 2, helloMs: 5000, leadMs: 3000, ticketTtlS: 120, ticketMaxAheadS: 300,
  reconnectS: 30, idleS: 10, keeperStallMs: 1500, resultsWaitS: 15, raceCapS: 600, closeAfterS: 60,
  emptyCloseS: 30, renderDelayMs: 100, deadReckonMs: 250, blendMs: 150, autoStartS: 45, pingSamples: 8,
});

/** Item ids in wire order (must equal sim/itemTable.js ITEM_IDS; a test checks). The first 8 are the hit kinds. */
export const WIRE_ITEMS = Object.freeze(['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter',
  'surge', 'glass', 'echo', 'golden']);
export const EFFECT_COUNT = 8;
export const SHIELDS = Object.freeze([null, 'glass', 'golden', 'echo']);
export const MODES = Object.freeze(['throw', 'lob', 'wide', 'drop', 'self']);
export const VERDICTS = Object.freeze(['hit', 'glass', 'golden', 'mercy', 'decoy']);
export const PHASES = Object.freeze(['lobby', 'countdown', 'racing', 'done']);
export const ERRORS = Object.freeze(['bad', 'phase', 'seat', 'item', 'host', 'rate']);
export const ABORTS = Object.freeze(['hello', 'ticket', 'expired', 'replay', 'version', 'config', 'full', 'started', 'closed', 'server']);
export const CLOSE = Object.freeze({ over: 4000, hello: 4001, bad: 4002, ticket: 4003, full: 4004, replaced: 4009, closed: 4010, server: 4011 });
export const FLAGS = Object.freeze(['speed', 'order', 'lap', 'finish', 'rate', 'assist']);
export const PLATFORMS = Object.freeze(['desktop', 'web', 'phone']);

/** Pulse bits (record byte 37): one-shot edges since the seat's previous record. */
export const PULSE = Object.freeze({ HOP: 1, LAND: 2, LAND_HEAVY: 4, BOOST: 8, LIP: 16, TRICK: 32, WORD: 64, BUBBLE: 128 });

/** Looks (Astra wardrobe r4 #2623, vehicle variants #2624). */
export const LOOK_OUTFITS = Object.freeze(['emi-outfit-croupier', 'emi-outfit-pool', 'emi-outfit-varsity', 'emi-outfit-builder',
  'emi-outfit-cheer', 'emi-outfit-swim', 'emi-outfit-drone-protocol', 'emi-outfit-snow-day']);
export const LOOK_VEHICLES = Object.freeze(['emi-teacup-kart', 'emi-vehicle-petal', 'emi-vehicle-trinket', 'emi-vehicle-pendulum', 'emi-vehicle-reverie', 'emi-vehicle-house-edge', 'emi-vehicle-checkmate', 'emi-vehicle-lovebomb', 'emi-vehicle-feedback', 'emi-vehicle-velvet-lock', 'emi-vehicle-undertow']);
export const DEFAULT_LOOK = Object.freeze({ outfit: null, vehicle: 'emi-teacup-kart' });

/** A look with unknown ids replaced by the defaults. */
export function normLook(look) {
  const o = look && typeof look === 'object' ? look : {};
  const outfit=o.outfit==='emi-outfit-labcoat'?'emi-outfit-builder':o.outfit;
  return {outfit: LOOK_OUTFITS.includes(outfit) ? outfit : null,
    vehicle: LOOK_VEHICLES.includes(o.vehicle) ? o.vehicle : DEFAULT_LOOK.vehicle,
    ...(typeof o.skin === 'string' && /^[0-9a-f]{36}[0-3]$/.test(o.skin) ? {skin:o.skin} : {})};
}

/** The look a bot wears in a seat (NET.md section 10). */
export function botLook(seat, seed) {
  const sd = seed >>> 0;
  return { outfit: LOOK_OUTFITS[(sd + seat * 3) % LOOK_OUTFITS.length], vehicle: LOOK_VEHICLES[(sd + seat) % LOOK_VEHICLES.length] };
}

/** True when seq a is newer than b (u16 serial arithmetic). */
export const seqNewer = (a, b) => { const d = (a - b) & 0xffff; return d > 0 && d < 0x8000; };

// ---- binary ------------------------------------------------------------------------------------

const clampI = (v, lo, hi) => { const r = Math.round(Number(v) || 0); return r < lo ? lo : r > hi ? hi : r; };
const U32 = 0xffffffff;

/** A kart record with every field at rest. */
export function createKart() {
  return { tick: 0, seq: 0, seat: 0, paused: false, bot: false, finished: false, grounded: false, airborne: false,
    lap: 0, checkpoint: 0, respawn: 0, stalled: false, assist: false, s: 0, x: 0, h: 0, q: [0, 0, 0, 1], v: [0, 0, 0],
    speedPct: 0, driftDir: 0, driftStage: 0, boostKind: 0, item: null, rolling: false, effect: null, shield: null, echo: 0,
    pose: 0, pulses: 0, pops: 0 };
}

/** Write one kart record at byte offset `o` of a DataView. */
export function packKart(dv, o, k) {
  dv.setUint32(o, clampI(k.tick, 0, U32), true);
  dv.setUint16(o + 4, (Math.round(Number(k.seq) || 0) & 0xffff), true);
  dv.setUint8(o + 6, (clampI(k.seat, 0, 7)) | (k.paused ? 8 : 0) | (k.bot ? 16 : 0) | (k.finished ? 32 : 0)
    | (k.grounded ? 64 : 0) | (k.airborne ? 128 : 0));
  dv.setUint8(o + 7, clampI(k.lap, 0, 255));
  dv.setUint8(o + 8, clampI(k.checkpoint, 0, 255));
  dv.setUint8(o + 9, clampI(k.respawn, 0, 7) | (k.stalled ? 8 : 0) | (k.assist ? 16 : 0) | (clampI(k.fork, 0, 7) << 5));
  dv.setUint32(o + 10, clampI(k.s * 100, 0, U32), true);
  dv.setInt16(o + 14, clampI(k.x * 100, -32768, 32767), true);
  dv.setInt16(o + 16, clampI(k.h * 100, -32768, 32767), true);
  const q = k.q || [0, 0, 0, 1], sg = (Number(q[3]) || 0) < 0 ? -1 : 1;
  for (let i = 0; i < 4; i++) dv.setInt16(o + 18 + i * 2, clampI(sg * q[i] * 32767, -32767, 32767), true);
  const v = k.v || [0, 0, 0];
  for (let i = 0; i < 3; i++) dv.setInt16(o + 26 + i * 2, clampI(v[i] * 100, -32768, 32767), true);
  dv.setUint8(o + 32, clampI(Math.min(Math.max(Number(k.speedPct) || 0, 0), 1.5) / 1.5 * 255, 0, 255));
  const dd = k.driftDir < 0 ? 1 : k.driftDir > 0 ? 2 : 0;
  dv.setUint8(o + 33, dd | (clampI(k.driftStage, 0, 3) << 2) | (clampI(k.boostKind, 0, 7) << 4));
  const it = k.item ? WIRE_ITEMS.indexOf(k.item) + 1 : 0;
  dv.setUint8(o + 34, it | (k.rolling && it ? 32 : 0));
  const ef = k.effect ? WIRE_ITEMS.indexOf(k.effect) + 1 : 0, sh = Math.max(0, SHIELDS.indexOf(k.shield || null));
  dv.setUint8(o + 35, (ef > EFFECT_COUNT ? 0 : ef) | (sh << 4) | (clampI(k.echo, 0, 3) << 6));
  dv.setUint8(o + 36, clampI(k.pose, 0, 255));
  dv.setUint8(o + 37, clampI(k.pulses, 0, 255));
  dv.setUint16(o + 38, clampI(k.pops, 0, 65535), true);
}

/** Read one kart record at byte offset `o`. @returns the kart (`out` reused when given) */
export function unpackKart(dv, o, out = createKart()) {
  out.tick = dv.getUint32(o, true);
  out.seq = dv.getUint16(o + 4, true);
  const sf = dv.getUint8(o + 6);
  out.seat = sf & 7; out.paused = !!(sf & 8); out.bot = !!(sf & 16); out.finished = !!(sf & 32);
  out.grounded = !!(sf & 64); out.airborne = !!(sf & 128);
  out.lap = dv.getUint8(o + 7);
  out.checkpoint = dv.getUint8(o + 8);
  const st = dv.getUint8(o + 9);
  out.respawn = st & 7; out.stalled = !!(st & 8); out.assist = !!(st & 16);
  if (st >> 5) out.fork = st >> 5; else delete out.fork;
  out.s = dv.getUint32(o + 10, true) / 100;
  out.x = dv.getInt16(o + 14, true) / 100;
  out.h = dv.getInt16(o + 16, true) / 100;
  const q = out.q || (out.q = [0, 0, 0, 1]);
  let n = 0;
  for (let i = 0; i < 4; i++) { q[i] = dv.getInt16(o + 18 + i * 2, true) / 32767; n += q[i] * q[i]; }
  if (n === 0) q[3] = 1; // not renormalised (re-encodes to the same bytes); the reader normalises before use
  const v = out.v || (out.v = [0, 0, 0]);
  for (let i = 0; i < 3; i++) v[i] = dv.getInt16(o + 26 + i * 2, true) / 100;
  out.speedPct = dv.getUint8(o + 32) * 1.5 / 255;
  const d = dv.getUint8(o + 33);
  out.driftDir = (d & 3) === 1 ? -1 : (d & 3) === 2 ? 1 : 0; out.driftStage = (d >> 2) & 3; out.boostKind = (d >> 4) & 7;
  const it = dv.getUint8(o + 34) & 31;
  out.item = it >= 1 && it <= WIRE_ITEMS.length ? WIRE_ITEMS[it - 1] : null;
  out.rolling = !!(dv.getUint8(o + 34) & 32) && !!out.item;
  const a = dv.getUint8(o + 35), ef = a & 15;
  out.effect = ef >= 1 && ef <= EFFECT_COUNT ? WIRE_ITEMS[ef - 1] : null;
  out.shield = SHIELDS[(a >> 4) & 3]; out.echo = (a >> 6) & 3;
  out.pose = dv.getUint8(o + 36);
  out.pulses = dv.getUint8(o + 37);
  out.pops = dv.getUint16(o + 38, true);
  return out;
}

/** A whole frame: header + karts. @returns {Uint8Array} */
export function encodeFrame(type, ms, karts) {
  const n = Math.min(karts.length, MAX_KARTS), buf = new Uint8Array(HEADER_BYTES + n * KART_BYTES);
  const dv = new DataView(buf.buffer);
  dv.setUint8(0, type); dv.setUint8(1, NET_V); dv.setUint8(2, n); dv.setUint8(3, 0);
  dv.setUint32(4, clampI(ms, 0, U32), true);
  for (let i = 0; i < n; i++) packKart(dv, HEADER_BYTES + i * KART_BYTES, karts[i]);
  return buf;
}

/** Parse a frame. @returns {{ type, ver, n, flags, ms, karts }|null} null when malformed */
export function decodeFrame(data) {
  const u8 = data instanceof Uint8Array ? data : ArrayBuffer.isView(data) ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength)
    : data instanceof ArrayBuffer ? new Uint8Array(data) : null;
  if (!u8 || u8.length < HEADER_BYTES) return null;
  const dv = new DataView(u8.buffer, u8.byteOffset, u8.byteLength);
  const type = dv.getUint8(0), ver = dv.getUint8(1), n = dv.getUint8(2);
  if ((type !== BIN.BATCH && type !== BIN.UPLOAD) || ver !== NET_V || n > MAX_KARTS || u8.length !== HEADER_BYTES + n * KART_BYTES) return null;
  const karts = [];
  for (let i = 0; i < n; i++) karts.push(unpackKart(dv, HEADER_BYTES + i * KART_BYTES));
  return { type, ver, n, flags: dv.getUint8(3), ms: dv.getUint32(4, true), karts };
}

/** Fill a wire kart from a sim KartState plus the per-racer extras (`tick, seq, seat, paused, bot, finished,
 * assist, item, rolling, effect, shield, echo, pose, pulses, pops`). */
export function kartFromState(out, st, extra = {}) {
  out.tick = extra.tick || 0; out.seq = extra.seq || 0; out.seat = extra.seat || 0;
  out.paused = !!extra.paused; out.bot = !!extra.bot; out.finished = !!extra.finished; out.assist = !!extra.assist;
  out.grounded = st[S.grounded] > 0; out.airborne = st[S.airborne] > 0;
  out.lap = st[S.lap]; out.checkpoint = st[S.checkpoint]; out.respawn = st[S.respawnPhase]; out.stalled = st[S.stalled] > 0;
  out.s = st[S.s]; out.x = st[S.lateral]; out.h = st[S.height];
  out.q = [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]]; out.v = [st[S.velX], st[S.velY], st[S.velZ]];
  out.speedPct = st[S.speedPct]; out.driftDir = Math.sign(st[S.driftDir]); out.driftStage = st[S.driftStage]; out.boostKind = st[S.boostKind];
  out.item = extra.item || null; out.rolling = !!extra.rolling; out.effect = extra.effect || null;
  out.shield = extra.shield || null; out.echo = extra.echo || 0; out.pose = extra.pose || 0; out.pulses = extra.pulses || 0; out.pops = extra.pops || 0;
  return out;
}

/** Hex helpers (vectors, `welcome.resume`). */
export const toHex = (u8) => Array.from(u8, (b) => b.toString(16).padStart(2, '0')).join('');
export function fromHex(hex) {
  const s = String(hex || ''), out = new Uint8Array(s.length >> 1);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(s.substr(i * 2, 2), 16);
  return out;
}

// ---- JSON messages -------------------------------------------------------------------------------

const isInt = (v, lo, hi) => Number.isInteger(v) && v >= lo && v <= hi;
const LOOK_ID = /^[a-z0-9-]{1,48}$/;
const SEATS_OK = (v) => Array.isArray(v) && v.length <= 8 && v.every((s) => isInt(s, 0, 7));
const isLook = (v) => !!v && typeof v === 'object' && !Array.isArray(v) && Object.keys(v).every(k=>['outfit','vehicle','skin'].includes(k)) && Object.keys(v).length >= 2 && Object.keys(v).length <= 3 && (v.skin === undefined || (typeof v.skin === 'string' && /^[0-9a-f]{36}[0-3]$/.test(v.skin)))
  && (v.outfit === null || (typeof v.outfit === 'string' && LOOK_ID.test(v.outfit)))
  && typeof v.vehicle === 'string' && LOOK_ID.test(v.vehicle);
const isObj = (v) => !!v && typeof v === 'object' && !Array.isArray(v);
const numOrNull = (v) => v === null || (typeof v === 'number' && Number.isFinite(v));

/** Field validators by type name. */
export const TYPES = Object.freeze({
  seat: (v) => isInt(v, 0, 7),
  u8: (v) => isInt(v, 0, 255),
  u16: (v) => isInt(v, 0, 65535),
  u32: (v) => isInt(v, 0, U32),
  num: (v) => typeof v === 'number' && Number.isFinite(v),
  unit: (v) => typeof v === 'number' && v >= 0 && v <= 1,
  bool: (v) => typeof v === 'boolean',
  str: (v) => typeof v === 'string' && v.length >= 1 && v.length <= 1024,
  item: (v) => WIRE_ITEMS.includes(v),
  mode: (v) => MODES.includes(v),
  code: (v) => typeof v === 'string' && /^[a-z0-9-]{1,24}$/.test(v),
  lookId: (v) => v === null || (typeof v === 'string' && LOOK_ID.test(v)),
  skin: (v) => typeof v === 'string' && /^[0-9a-f]{36}[0-3]$/.test(v),
  look: isLook,
  phase: (v) => PHASES.includes(v),
  verdict: (v) => VERDICTS.includes(v),
  err: (v) => ERRORS.includes(v),
  abort: (v) => ABORTS.includes(v),
  leftWhy: (v) => v === 'leave' || v === 'gone',
  roomId: (v) => typeof v === 'string' && /^[a-z0-9]{12}$/.test(v),
  hex: (v) => typeof v === 'string' && /^[0-9a-f]{80}$/.test(v),
  seats: SEATS_OK,
  seatOrNull: (v) => v === null || isInt(v, 0, 7),
  numOrNull,
  cfg: (v) => isObj(v) && typeof v.track === 'string' && isInt(v.laps, 1, 9) && isInt(v.seats, 2, 8)
    && (v.mode === 'open' || v.mode === 'friends') && typeof v.laneKeeper === 'boolean' && typeof v.staked === 'boolean'
    && typeof v.botFill === 'boolean' && isInt(v.autoStartS, 0, 65535),
  seatList: (v) => Array.isArray(v) && v.length <= 8 && v.every((s) => isObj(s) && isInt(s.seat, 0, 7)
    && ['human', 'bot', 'empty'].includes(s.kind) && typeof s.name === 'string' && isLook(s.look)),
  standRows: (v) => Array.isArray(v) && v.length <= 8 && v.every((r) => isObj(r) && isInt(r.seat, 0, 7) && isInt(r.place, 1, 8)
    && isInt(r.lap, 0, 255) && typeof r.progM === 'number' && isInt(r.pops, 0, 65535) && typeof r.finished === 'boolean' && numOrNull(r.timeMs)),
  finishRows: (v) => Array.isArray(v) && v.length <= 8 && v.every((r) => isObj(r) && isInt(r.seat, 0, 7) && isInt(r.place, 1, 8)
    && typeof r.name === 'string' && typeof r.bot === 'boolean' && typeof r.finished === 'boolean' && numOrNull(r.timeMs)
    && numOrNull(r.bestLapMs) && isInt(r.pops, 0, 65535)),
});

/** Client -> room messages: field -> type ('?' suffix = optional). */
export const C2R = Object.freeze({
  hello: { v: 'u8', ticket: 'str', clientMs: 'num', courseHash: 'str?' },
  ping: { c: 'num' },
  ready: { on: 'bool' },
  start: {},
  look: { outfit: 'lookId', vehicle: 'lookId', skin: 'skin?' },
  pickup: { box: 'u16', tick: 'u32', seat: 'seat?' },
  fire: { id: 'u16', item: 'item', mode: 'mode', tick: 'u32', s: 'num', x: 'num', seat: 'seat?' },
  cp: { idx: 'u8', lap: 'u8', tick: 'u32', seat: 'seat?' },
  finish: { tick: 'u32', frac: 'unit', seat: 'seat?' },
  drop: { tick: 'u32', s: 'num', seat: 'seat?' },
  emote: { code: 'code' },
  leave: {},
});

/** Room -> client messages. */
export const R2C = Object.freeze({
  welcome: { v: 'u8', roomId: 'roomId', seat: 'seat', t0: 'num', serverMs: 'num', phase: 'phase', cfg: 'cfg', seed: 'u32',
    seats: 'seatList', keep: 'seats', countAt: 'num?', startAt: 'num?', goTick: 'u32?', resume: 'hex?' },
  pong: { c: 'num', s: 'num' },
  seats: { seats: 'seatList' },
  seat: { seat: 'seat', ready: 'bool?', idle: 'bool?', left: 'bool?', reason: 'leftWhy?', look: 'look?', paused: 'bool?' },
  start: { countAt: 'num', startAt: 'num', goTick: 'u32', seed: 'u32' },
  keep: { seats: 'seats' },
  roll: { seat: 'seat', box: 'u16', item: 'item' },
  fired: { sid: 'u32', seat: 'seat', id: 'u16', item: 'item', mode: 'mode', tick: 'u32', s: 'num', x: 'num', target: 'seatOrNull', fork: 'u8?', targetFork: 'u8?' },
  hit: { sid: 'u32', victim: 'seat', by: 'seat', item: 'item', verdict: 'verdict', durationS: 'num', tick: 'u32' },
  fizzle: { sid: 'u32', tick: 'u32' },
  standings: { tick: 'u32', rows: 'standRows' },
  finish: { rows: 'finishRows' },
  emote: { seat: 'seat', code: 'code' },
  error: { code: 'err', re: 'code?' },
  abort: { reason: 'abort' },
});

/** Check a message object against a table. @returns {boolean} */
export function checkMsg(table, m) {
  if (!isObj(m) || typeof m.t !== 'string' || !Object.prototype.hasOwnProperty.call(table, m.t)) return false;
  const spec = table[m.t];
  for (const k of Object.keys(m)) if (k !== 't' && !Object.prototype.hasOwnProperty.call(spec, k)) return false;
  for (const [k, ty] of Object.entries(spec)) {
    const opt = ty.endsWith('?'), name = opt ? ty.slice(0, -1) : ty;
    if (!(k in m) || m[k] === undefined) { if (opt) continue; return false; }
    if (!TYPES[name](m[k])) return false;
  }
  return true;
}

/** Parse a text frame for one direction ('c2r' or 'r2c'). @returns {object|null} */
export function decodeMsg(text, dir) {
  if (typeof text !== 'string' || text.length > (dir==='r2c'?ROOM_MSG_MAX_CHARS:MSG_MAX_BYTES)) return null;
  let m;
  try { m = JSON.parse(text); } catch { return null; }
  return checkMsg(dir === 'c2r' ? C2R : R2C, m) ? m : null;
}

/** Build a text frame; throws on a shape the table refuses (a bug on the sending side). */
export function encodeMsg(m, dir = 'c2r') {
  if (!checkMsg(dir === 'c2r' ? C2R : R2C, m)) throw new Error(`net: bad ${dir} message ${m && m.t}`);
  return JSON.stringify(m);
}
