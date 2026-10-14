/* ============================================================================
 * ui/watch-hud.js - the stands' furniture, kept small (owner, 2026-10-14).
 *
 * hud.js keeps the two player plates (names, ratings, clocks, IQ); this adds
 * what only watching needs, plus the two lines every seat shares:
 *
 *   the plates' Distraction bars   `fx-heat { side, heat }` off the delayed
 *                                  tape; the turn player's bar is the lit one,
 *                                  and a move drops the mover's to nothing (the
 *                                  breath, as on the player's own screen)
 *   "15 s behind"                  `watch-info.behindMs`, the true lag
 *   "N watching"                   `watch-info.watchers` (stands) or `watchers`
 *                                  (seats, net/match.js); everyone sees it
 *   Watch from Black / White       the seat swap: `watch-seat { seat }`; the session
 *                                  turns the camera and answers with watch-info
 *   Leave                          `watch-leave`, which the door answers
 *   the crowd                      ui/reactions.js (bar for the stands,
 *                                  floats for everyone; sounds, anti-spam,
 *                                  mute and a seat's "Play watchers'
 *                                  reactions" live there)
 *   the effects switch             `watch-effects { on, amount, media }` on
 *                                  every watch deal and every change of the
 *                                  watcher's own choice (game/preferences.js
 *                                  watchEffects / watchAmount)
 *   the no-pictures card           effects on and no picture source: say so
 *                                  and offer the picker (ui/pictures.js). The
 *                                  media layers stay silent until then; only
 *                                  the media-free layers run.
 *
 * WHERE "ANY PICTURES" COMES FROM. A host that knows says so: `mediaAvailable`
 * (boolean) on pbp:settings or pbp:media-state, and its latest word wins. A
 * host that has not said falls back to the picture choice it sent; a page with
 * no host at all says nothing (null) and never nags.
 *
 *   createWatchHud({ bus, game, board, el, pictures, prefs, still, onHost })
 *     -> { dispose, debug }
 * ==========================================================================*/

import { createReactions } from './reactions.js';
import { setShown, isShown } from './fade.js';

export const DEFAULT_DELAY_MS = 15000;
const SIDES = ['w', 'b'];
const other = (s) => (s === 'w' ? 'b' : 'w');
const clamp01 = (v) => Math.min(1, Math.max(0, Number(v) || 0));

/** "15 s behind". Pure. */
export function delayWord(ms) {
  const s = Math.max(0, Math.round((Number.isFinite(Number(ms)) ? Number(ms) : DEFAULT_DELAY_MS) / 1000));
  return s + ' s behind';
}

/** "3 watching", or '' for nobody. Pure. */
export function watchersWord(n) {
  const c = Math.max(0, Math.trunc(Number(n) || 0));
  return c > 0 ? c + ' watching' : '';
}

/** The seat swap's words: the side it turns the camera to. Pure. */
export function seatWord(viewSide) {
  return viewSide === 'b' ? 'Watch from White' : 'Watch from Black';
}

/** A pbp:online-media answer that brought nothing (off, empty, error, or ready with none). Pure. */
export function onlineEmpty(m) {
  const s = String((m && m.state) || '');
  return s === 'off' || s === 'empty' || s === 'error' || (s === 'ready' && !(Number(m.have) > 0));
}

/**
 * Is there any picture source for the stands' effects? true / false, or null
 * when nobody has said (no host). Pure.
 * @param {boolean|null} hostFlag  the host's `mediaAvailable`, when it sent one
 * @param {object|null} choice     ui/pictures.js state (readChoice shape)
 * @param {boolean} [onlineOff]    the online answer came back off or empty: niches alone are no source
 */
export function mediaReady(hostFlag, choice, onlineOff = false) {
  if (typeof hostFlag === 'boolean') return hostFlag;
  if (!choice) return null;
  const online = !onlineOff && choice.online !== false && choice.canOnline !== false && (!!choice.flavour || !!choice.appWide);
  return online || choice.library !== false;
}

/** The no-pictures card's line. Pure. */
export function noMediaLine(canPick) {
  return canPick ? 'No pictures picked yet. The effects run without them.' : 'No pictures on this device. The effects run without them.';
}

export function createWatchHud({
  bus, game = null, board = null, el = {}, pictures = null, prefs = null,
  still = () => false, onHost = null,
} = {}) {
  const unbind = [];
  const undom = [];
  let watching = false;
  let dealt = false;
  let over = false;
  let active = 'w';
  let viewSide = 'w';
  let behindMs = DEFAULT_DELAY_MS;   // the true lag behind the live game (watch-info behindMs)
  let watcherCount = 0;
  const heat = { w: 0, b: 0 };
  let hostMedia = null;        // the host's mediaAvailable, once it says
  let onlineOff = false;       // the host's last pbp:online-media came back off or empty
  let later = false;           // "Not now" on the no-pictures card, for this game
  let lastFx = '';             // the last watch-effects sent, so a repaint is not a resend
  const pres = () => { try { return prefs && prefs.presentation ? prefs.presentation() : {}; } catch { return {}; } };

  const on = (type, fn) => {
    if (!bus || typeof bus.on !== 'function') return;
    const safe = (p) => { try { fn(p); } catch (e) { console.warn('[pbp/watch-hud] ' + type + ': ' + (e && e.message)); } };
    try { unbind.push(bus.on(type, safe)); } catch { /* no listeners */ }
  };
  const click = (node, fn) => {
    if (!node) return;
    const h = (e) => { e.preventDefault(); try { fn(); } catch (err) { console.warn('[pbp/watch-hud] ' + (err && err.message)); } };
    node.addEventListener('click', h);
    undom.push(() => node.removeEventListener('click', h));
  };

  const crowd = createReactions({ bus, bar: el.reactBar || null, floats: el.floats || null, still });

  /* ---- what is on screen -------------------------------------------------- */

  function ready() {
    let choice = null;
    try { choice = pictures && pictures.available && pictures.available() ? pictures.state() : null; } catch { choice = null; }
    return mediaReady(hostMedia, choice, onlineOff);
  }

  function paintHeat() {
    for (const s of SIDES) {
      const bar = el.heat && el.heat[s];
      if (!bar) continue;
      bar.hidden = !watching;
      bar.classList.toggle('lit', watching && s === active && !over);
      const fill = bar.firstElementChild;
      if (fill) fill.style.width = Math.round(heat[s] * 100) + '%';
      bar.setAttribute('aria-label', (s === 'w' ? 'White' : 'Black') + ' Distraction ' + Math.round(heat[s] * 100) + '%');
    }
  }

  function paintMedia() {
    if (!el.nomedia) return;
    const p = pres();
    const canPick = !!(pictures && pictures.available && pictures.available());
    const show = watching && !over && p.watchEffects !== false && ready() === false && !later;
    setShown(el.nomedia, show, still());
    if (!show) return;
    if (el.nomediaMsg) el.nomediaMsg.textContent = noMediaLine(canPick);
    if (el.nomediaPick) el.nomediaPick.hidden = !canPick;
  }

  function paint() {
    setShown(el.block, watching, still());
    if (el.tag) el.tag.textContent = delayWord(behindMs);
    if (el.flip) el.flip.textContent = seatWord(viewSide);
    if (el.watchers) {
      const text = dealt ? watchersWord(watcherCount) : '';
      el.watchers.hidden = !text;
      el.watchers.textContent = text;
    }
    crowd.setStands(watching);
    crowd.setWatching(watching && !over);
    paintHeat();
    paintMedia();
  }

  /** watch-effects, at boot, on every change, on every watch deal and on every pbp:media-state (WATCH lane). */
  function sendEffects(force = false) {
    if (!bus) return;
    const p = pres();
    const r = ready();
    const msg = { on: p.watchEffects !== false, amount: p.watchAmount || 'normal', media: r !== false };
    const key = JSON.stringify(msg);
    if (!force && key === lastFx) return;
    lastFx = key;
    try { bus.emit('watch-effects', msg); } catch { /* no bus */ }
  }

  // A source lost after "Not now" (the online answer came back empty) asks again: it is news.
  let lastReady = null;
  function mediaChanged(force = false) {
    const r = ready();
    if (r === false && lastReady !== false) later = false;
    lastReady = r;
    paintMedia();
    sendEffects(force);
  }

  /* ---- the bus ------------------------------------------------------------ */

  on('local', (p) => {
    dealt = !!p;
    watching = !!(p && p.mode === 'watch');
    over = false;
    later = false;
    viewSide = 'w';
    heat.w = 0; heat.b = 0;
    watcherCount = 0;
    try { watcherCount = Math.max(0, Number(game && game.watchers && game.watchers()) || 0); } catch { watcherCount = 0; }
    // the tag says the true lag (watch-info behindMs), the configured delay only until the tape says
    behindMs = DEFAULT_DELAY_MS;
    try {
      const cur = game && game.current;
      const b = cur && cur.behindMs ? cur.behindMs() : cur && cur.delayMs ? cur.delayMs() : NaN;
      if (watching && Number.isFinite(b) && b > 0) behindMs = b;
    } catch { /* the default */ }
    try { active = game && game.turn ? game.turn() : 'w'; } catch { active = 'w'; }
    lastFx = '';
    paint();
    sendEffects(true);
  });
  on('watch-info', (p) => {
    if (!p) return;
    if (Number.isFinite(Number(p.behindMs))) behindMs = Math.max(0, Number(p.behindMs));
    else if (Number.isFinite(Number(p.delayMs))) behindMs = Math.max(0, Number(p.delayMs));
    if (Number.isFinite(Number(p.watchers))) watcherCount = Math.max(0, Math.trunc(Number(p.watchers)));
    if (p.seat === 'w' || p.seat === 'b') viewSide = p.seat;
    paint();
  });
  on('watchers', (p) => { watcherCount = Math.max(0, Math.trunc(Number(p && p.count) || 0)); paint(); });
  on('fx-heat', (p) => {
    if (!watching || !p || (p.side !== 'w' && p.side !== 'b')) return;
    heat[p.side] = clamp01(p.heat);
    paintHeat();
  });
  on('turn', (p) => {
    const side = p && p.side === 'b' ? 'b' : 'w';
    // the side that just moved lets go, the way the player's own screen does
    if (watching && side !== active) heat[other(side)] = 0;
    active = side;
    paintHeat();
  });
  on('gameover', () => { over = true; heat.w = 0; heat.b = 0; paint(); });

  /* ---- the buttons -------------------------------------------------------- */

  click(el.leave, () => { if (bus) bus.emit('watch-leave'); });
  // The session turns the camera on watch-seat and answers with watch-info { seat }, which relabels
  // the button: nothing here turns the board, so a swap never flips twice.
  click(el.flip, () => { if (bus) bus.emit('watch-seat', { seat: other(viewSide) }); });
  click(el.nomediaLater, () => { later = true; paintMedia(); });
  click(el.nomediaPick, () => {
    if (!pictures || !pictures.manage) return;
    Promise.resolve(pictures.manage({ still: still() })).then(() => mediaChanged()).catch(() => {});
  });

  /* ---- the host and the watcher's own choice ------------------------------ */

  if (typeof onHost === 'function') {
    try {
      unbind.push(onHost((m) => {
        if (m && m.type === 'pbp:online-media') { onlineOff = onlineEmpty(m); mediaChanged(); return; }
        if (!m || (m.type !== 'pbp:settings' && m.type !== 'pbp:media-state')) return;
        if (typeof m.mediaAvailable === 'boolean') hostMedia = m.mediaAvailable;
        mediaChanged(m.type === 'pbp:media-state');   // WATCH re-reads the effects after every niche pick
      }));
    } catch { /* no host */ }
  }
  if (prefs && typeof prefs.onPresentation === 'function') {
    try { unbind.push(prefs.onPresentation(() => mediaChanged())); } catch { /* no prefs */ }
  }

  paint();
  sendEffects();   // at boot (once: the prefs listener above may have sent it already), so the ramp holds the watcher's choice before any watch deal

  return {
    dispose() {
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      for (const off of undom) { try { off(); } catch { /* gone */ } }
      unbind.length = 0; undom.length = 0;
      crowd.dispose();
    },
    debug: () => ({
      watching,
      shown: isShown(el.block),
      tag: el.tag ? el.tag.textContent : null,
      seat: viewSide,
      watchers: el.watchers && !el.watchers.hidden ? el.watchers.textContent : null,
      heat: { w: heat.w, b: heat.b },
      lit: SIDES.find((s) => el.heat && el.heat[s] && el.heat[s].classList.contains('lit')) || null,
      media: ready(),
      noMedia: isShown(el.nomedia),
      effects: lastFx ? JSON.parse(lastFx) : null,
      crowd: crowd.debug(),
    }),
  };
}

export default createWatchHud;
