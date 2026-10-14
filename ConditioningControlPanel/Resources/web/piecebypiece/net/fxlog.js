/* ============================================================================
 * net/fxlog.js - a player's Distraction, written down for the stands.
 *
 * Spectators see the turn player's effects at the moments they fired (owner,
 * 2026-10-14), so a seat sends a short record of its own Distraction while it
 * is on the move: the heat (sampled once a second by the ramp, `ramp-heat`)
 * and every effect the ramp fired (`ramp-fired`). Nothing about media travels:
 * no url, no niche, no id, only the layer kind and how long it held. Each
 * spectator's page fetches its own pictures.
 *
 * ONLY WHILE SOMEBODY WATCHES. The seat routes report `watchers` (net/match.js
 * emits it on the bus); at 0 nothing is buffered and nothing is posted. The
 * record is a window: it starts when the turn starts (or when the first watcher
 * arrives mid-turn) and is posted every ~5 s during the turn and once on the
 * move, then a new window starts. The server allows one post per 3 s per seat
 * (3.3 s here): a move inside that gap waits out the rest of it, for the match
 * it was made in; a new deal drops it.
 *
 * Body (POST /v2/pbp/match/:id/fx, the brief's wire 4):
 *   { ply, from_ms_ago, heat: [[dt_ms, h0_99], ...], fired: [[dt_ms, kind, hold_ms?], ...] }
 * `ply` is the ply the turn is for (plies played when it started); dt is from
 * the window start; from_ms_ago is how long before the request that was.
 *
 *   createFxLog({ bus, game, api, now, timers }) -> { dispose, flush, debug }
 *
 * api.postFx(matchId, body) belongs to net/api.js (lane WATCH); until it lands
 * this posts nothing and says nothing.
 * ==========================================================================*/

import * as defaultApi from './api.js';

/** The ramp's layer names the server accepts; anything else is dropped here first. */
export const FX_KINDS = Object.freeze(['flash', 'gifRain', 'video', 'glitch', 'melt', 'spiral', 'blur', 'overlay']);

export const FX_TUNING = Object.freeze({
  postEveryMs: 5000,   // a window during the turn
  minGapMs: 3300,      // the server's rate is one post per 3 s per seat; 300 ms spare for clock skew
  capBytes: 4000,      // under the server's 4 KB body cap, with room to spare
  maxHeat: 60,         // samples kept per window (one a second)
  maxFired: 40,
});

const clamp01 = (v) => Math.min(1, Math.max(0, Number(v) || 0));

/**
 * The body for one window, trimmed to fit the cap. Pure.
 * @param {{ ply:number, startMs:number, heat:Array, fired:Array }} win
 * @param {number} nowMs the send time
 */
export function fxBody(win, nowMs, capBytes = FX_TUNING.capBytes) {
  let heat = win.heat.slice(-FX_TUNING.maxHeat);
  let fired = win.fired.slice(-FX_TUNING.maxFired);
  const make = () => ({ ply: win.ply, from_ms_ago: Math.max(0, Math.round(nowMs - win.startMs)), heat, fired });
  let body = make();
  // too big: thin the heat first (every other sample), then drop the oldest fires
  while (JSON.stringify(body).length > capBytes && (heat.length > 1 || fired.length)) {
    if (heat.length > 1) heat = heat.filter((_, i) => i % 2 === 1 || i === heat.length - 1);
    else fired = fired.slice(1);
    body = make();
  }
  return body;
}

/** One ramp-fired payload as a record entry, or null when the server would drop it. Pure. */
export function firedEntry(p, dt) {
  const kind = p && typeof p.kind === 'string' ? p.kind : '';
  if (!FX_KINDS.includes(kind)) return null;
  const hold = Number(p.holdMs);
  return Number.isFinite(hold) && hold > 0 ? [Math.max(0, Math.round(dt)), kind, Math.round(hold)] : [Math.max(0, Math.round(dt)), kind];
}

export function createFxLog({
  bus, game, api = defaultApi, now = () => Date.now(),
  timers = { setTimeout: (f, ms) => setTimeout(f, ms), clearTimeout: (id) => clearTimeout(id), setInterval: (f, ms) => setInterval(f, ms), clearInterval: (id) => clearInterval(id) },
} = {}) {
  if (!bus || typeof bus.on !== 'function') return { dispose() {}, flush() {}, debug: () => ({ ok: false }) };
  const unbind = [];
  let watchers = 0;
  let mine = false;          // this seat is on the move
  let over = false;
  let win = null;            // { ply, startMs, heat, fired }
  let lastPost = -Infinity;
  let deferred = null;       // a move's window waiting out the rate gap
  let posts = 0;
  let disposed = false;

  const mySide = () => {
    try { const s = game && game.seats; return game && game.isOnline && !game.isWatch && Array.isArray(s) && s.length === 1 ? s[0] : null; } catch { return null; }
  };
  const matchId = () => {
    try { return (game && game.current && game.current.matchId) || (typeof window !== 'undefined' && window.PBP && window.PBP.match && window.PBP.match.id) || null; } catch { return null; }
  };
  const live = () => !disposed && !over && mine && watchers > 0 && !!mySide();
  const open = (ply) => { win = { ply: Number.isFinite(Number(ply)) ? Number(ply) : 0, startMs: now(), heat: [], fired: [] }; };
  const empty = (w) => !w || (!w.heat.length && !w.fired.length);

  function send(w, at = null) {
    const id = at || matchId();
    if (!id || empty(w) || typeof api.postFx !== 'function') return;
    const t = now();
    lastPost = t;
    posts++;
    try { Promise.resolve(api.postFx(id, fxBody(w, t))).catch(() => {}); } catch { /* the stands miss one window */ }
  }

  /** Post the window now if the rate allows, else once it does. A move always closes the window. */
  function flush(reason = 'tick') {
    if (empty(win)) { if (reason === 'move') win = null; return; }
    const w = win;
    const wait = lastPost + FX_TUNING.minGapMs - now();
    if (reason === 'move') win = null; else open(w.ply);
    if (wait <= 0) { send(w); return; }
    if (reason !== 'move') { win = w; return; }   // a tick inside the gap just keeps the window open
    if (deferred) timers.clearTimeout(deferred.id);
    // the match id is taken now: by the time the gap is over the page may be dealing another game
    const match = matchId();
    deferred = { w, id: timers.setTimeout(() => { const d = deferred; deferred = null; if (d && !disposed) send(d.w, d.match); }, wait), match };
  }

  const on = (type, fn) => {
    const safe = (p) => { try { fn(p); } catch (e) { console.warn('[pbp/fxlog] ' + type + ': ' + (e && e.message)); } };
    try { unbind.push(bus.on(type, safe)); } catch { /* a bus that will not take listeners */ }
  };

  on('local', () => { if (deferred) { timers.clearTimeout(deferred.id); deferred = null; } mine = false; over = false; win = null; watchers = 0; try { watchers = Math.max(0, Number(game.watchers && game.watchers()) || 0); } catch { watchers = 0; } });
  on('turn', (p) => {
    const me = mySide();
    const side = p && (p.side === 'w' || p.side === 'b') ? p.side : null;
    if (mine && side !== me) { flush('move'); mine = false; return; }
    if (!mine && me && side === me) { mine = true; if (watchers > 0) open(p && p.ply); }
  });
  on('gameover', () => { if (mine) flush('move'); mine = false; over = true; win = null; });
  on('watchers', (p) => {
    const n = Math.max(0, Math.trunc(Number(p && p.count) || 0));
    const was = watchers;
    watchers = n;
    if (n === 0) { win = null; return; }
    if (was === 0 && mine && !win) { let ply = 0; try { ply = game.plies(); } catch { ply = 0; } open(ply); }
  });
  on('ramp-heat', (p) => {
    if (!live() || !win) return;
    win.heat.push([Math.max(0, Math.round(now() - win.startMs)), Math.round(clamp01(p && p.heat) * 99)]);
    if (win.heat.length > FX_TUNING.maxHeat * 2) win.heat.splice(0, win.heat.length - FX_TUNING.maxHeat);
  });
  on('ramp-fired', (p) => {
    if (!live() || !win) return;
    const e = firedEntry(p, now() - win.startMs);
    if (e) win.fired.push(e);
    if (win.fired.length > FX_TUNING.maxFired * 2) win.fired.splice(0, win.fired.length - FX_TUNING.maxFired);
  });

  const ticker = timers.setInterval(() => {
    if (!live() || !win) return;
    if (now() - win.startMs >= FX_TUNING.postEveryMs) flush('tick');
  }, 1000);

  return {
    flush,
    dispose() {
      if (disposed) return;
      disposed = true;
      timers.clearInterval(ticker);
      if (deferred) timers.clearTimeout(deferred.id);
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      unbind.length = 0;
    },
    debug: () => ({ ok: true, watchers, mine, side: mySide(), posts, window: win ? { ply: win.ply, heat: win.heat.length, fired: win.fired.length } : null, deferred: !!deferred }),
  };
}

export default createFxLog;
