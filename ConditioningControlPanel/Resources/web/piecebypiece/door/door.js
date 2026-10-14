/* ============================================================================
 * door/door.js - the front door: menu, lobby, past games, profile, the end card.
 *
 * One glass card over the live board. The board underneath is the scenery and
 * keeps drifting (Law III); the card is the subject, and every screen is the
 * same card with different contents so the eye learns one place. Rules the
 * door keeps for you:
 *   - two clicks from the Play wall to a game: the strip, then one button
 *   - nothing is hostage: Esc always goes back, then out; a countdown is a
 *     tap away from "now"; a challenge you ignore ignores itself
 *   - ONE foreground BREATH per screen (the primary button's glow, or the
 *     waiting dot while you look for a game), everything else lands once
 *   - reduced motion takes the state and not the travel (.still)
 *
 *   createDoor({ bus, game, board, lobby, root, params, startGame, settings })
 *     -> { show(screen), hide(), isUp, debug, dispose }
 *
 * startGame({ mode, match }) is boot's: it deals the game and hides the door.
 * The door never touches the referee mid-game; it reads the record at the end.
 *
 * THE LOBBY IS LEFT WHEN THE GAME DEALS, not when the lobby screen goes. The
 * found screen keeps the lobby entered on purpose (the pairing has to land
 * through its poll), so the one place the door can stand up is go(): a lobby
 * still entered during a game keeps re-advertising a player who is mid-game,
 * and the server would pair a third player against a ghost.
 *
 * THE HOST NAMES THE PLAYER. Hosted, the account's display name is what the
 * server lists and what "you are visible as" has to say (boot.js copies it
 * into settings.playerName off pbp:identity), so the profile's name box is
 * read-only there. And a hosted page whose account is signed out cannot play
 * anybody: the lobby says so in one line, with quick match off, rather than
 * showing an empty room and a quick match that fails - and never the mock,
 * whose invented names a desktop player would try to join.
 *
 * OPEN TABLES. The lobby screen is a list you browse, not a room you stand in:
 * watching it (lobby.onTables) never lists the player. One tap on a row's
 * Join sits at that table; Host a table is the only thing that puts the
 * player on the list, and the list's own poll hears somebody sit down. A
 * poll repaints the list slot alone, keeping its scroll and focus, so a
 * three-second tick never throws the reader back to the top.
 *
 * PICTURES FIRST, ONCE. Every start (solo, two here, continue, quick match,
 * a table, a join, a challenge, a friend's game) goes through gate(): with
 * Distraction on and no picture choice saved, the shared picker asks first
 * (ui/pictures.js), and the pick starts the game. Saved, it is never asked
 * again; the menu's Pictures link opens the manager.
 * ==========================================================================*/

import { buildReplay, showReplayStep, resultLine } from './replay.js';
import { readSolo, soloOptions } from '../game/save.js';
import { LEVELS, LEVEL_ORDER } from '../game/search.js';
import { requestRematch } from '../net/rematch.js';
import { isHosted, identity, whenIdentity, postToHost, onHostMessage, exitToHost } from '../bridge.js';
import { createStake, pills as stakePills, stakeLabel, refusalText, sameStake, isNone } from '../net/stake.js';
import { listGames, getGame, saveGame, playerName, setPlayerName, profileStats, outcome, fmtDuration, fmtMoves, fmtWhen, finalIq } from './store.js';
import { recapHtml, keptIq, plainIq, fallPlan, goneLine } from './fall.js';
import { saveFallCard, copyFallCard } from './fall-card.js';
import { pictureChoice } from '../ui/pictures.js';
import { watchHead as watchRoute } from '../net/api.js';
import { watchLine } from '../net/watch.js';
import { createLadder } from '../ui/ladder.js';
import { endPlan } from '../ui/ladder-plan.js';
import { ui } from '../audio/ui-cues.js';
import { backButton, wireBack } from './back-button.js';
import { screenIn, leaveGhost, countUp, kick } from './screen-fx.js';

/** Every number the door decides with. */
export const TUNING = Object.freeze({
  endCardDelayMs: 1400,     // the board's own end beat lands first (thud, status line), then the card
  countdownStepMs: 700,     // 3 2 1, CHIME LADDER
  askTimeoutMs: 8000,       // an ignored challenge ignores itself
  replayStepMs: 900,        // auto-play cadence in a replay
  iqSettleMs: 2500,         // the last move's IQ grade may still be in the worker at the end; wait this long for it
  menuSway: 0.3,            // camera drift while the door is up
  stampLandMs: 350,         // a stamp's knock and cue: its THUD (door.css .door-stamp, 180 ms in + 320 ms) reaching full size
  watchEndMs: 4000,         // a watched game's last position stays up this long, then the tables
});
const T = TUNING;

const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };

/** The clocks a hosted table can carry. */
const HOST_TCS = Object.freeze([
  { label: '5+3', initial_ms: 300000, increment_ms: 3000 },
  { label: '10+0', initial_ms: 600000, increment_ms: 0 },
  { label: '15+10', initial_ms: 900000, increment_ms: 10000 },
]);

/** What a refused seat says, in one short line under the buttons. */
const SEAT_NOTES = Object.freeze({
  gone: 'that table just closed',
  blocked: 'that table is not open to you',
  self: 'that is your own table',
  left: 'nobody answered',
});

/** Why a Watch did not open, in the same one line. An empty line says nothing. */
const WATCH_NOTES = Object.freeze({
  not_watchable: 'that game is not open to watch',
  your_match: 'that is your own game',
  not_found: 'that game just ended',
  not_deployed: '',
  offline: 'could not reach that game',
});

/** 600000 + 5000 -> "10+5". Minutes that are not whole read as m:ss. */
export function tcWord(tc) {
  if (!tc || !Number.isFinite(Number(tc.initial_ms))) return '';
  const sec = Math.round(Number(tc.initial_ms) / 1000);
  const min = sec % 60 ? `${Math.floor(sec / 60)}:${String(sec % 60).padStart(2, '0')}` : String(sec / 60);
  return `${min}+${Math.round((Number(tc.increment_ms) || 0) / 1000)}`;
}

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

/**
 * The door's pictures: line icons drawn in currentColor, so a button's hover
 * and press colour them for free. One stroke weight, round ends.
 */
const ICON_PATHS = Object.freeze({
  solo: '<path d="M12 3.5a2.7 2.7 0 0 1 1.7 4.8c1.5.9 2.4 2.3 2.4 3.7H7.9c0-1.4.9-2.8 2.4-3.7A2.7 2.7 0 0 1 12 3.5zM9.4 12l-1.1 5.2h7.4L14.6 12M6.5 20.5h11"/>',
  tables: '<circle cx="8" cy="8" r="3"/><circle cx="16.5" cy="9" r="2.4"/><path d="M2.8 19.5c.3-3.2 2.5-5.2 5.2-5.2s4.9 2 5.2 5.2M14.3 14.6c2.9-.7 6.3.9 6.6 4.9"/>',
  board: '<rect x="4" y="4" width="16" height="16" rx="2.5"/><path d="M4 12h16M12 4v16"/><path d="M4.6 4.6l7 7M12.4 12.4l7 7" opacity=".55"/>',
  games: '<path d="M4.2 12a7.8 7.8 0 1 0 2.3-5.5M4.2 3.8v3.6h3.6"/><path d="M12 8v4.4l3 2"/>',
  profile: '<circle cx="12" cy="8.4" r="3.6"/><path d="M4.8 20.2c.7-3.7 3.5-5.7 7.2-5.7s6.5 2 7.2 5.7"/>',
  ladder: '<path d="M3 20.5h18M4.5 20.5v-6h4.5v6M9.8 20.5V8.5h4.4v12M15 20.5v-8.2h4.5v8.2"/><path d="M12 3.4l.8 1.7 1.8.2-1.3 1.2.4 1.8-1.7-.9-1.7.9.4-1.8-1.3-1.2 1.8-.2z" fill="currentColor" stroke="none"/>',
  pictures: '<rect x="3.5" y="5" width="17" height="14" rx="2.2"/><circle cx="9" cy="10" r="1.8"/><path d="M4.2 17.3l5-4.5 3.4 3 3-2.5 4.4 4"/>',
  play: '<path d="M8 5.5v13l10.5-6.5z" fill="currentColor"/>',
  go: '<path d="M5 12h12.5M13 7l5 5-5 5"/>',
  host: '<path d="M4 11.5h16M6 11.5v8M18 11.5v8M8.5 7.5h7"/><circle cx="12" cy="5" r="1.6"/>',
  replay: '<path d="M4.5 12a7.5 7.5 0 1 1 2.2 5.3"/><path d="M8.5 10.2v3.6l3.2-1.8z" fill="currentColor"/>',
});
function ico(name) {
  return `<svg class="ico" viewBox="0 0 24 24" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">${ICON_PATHS[name] || ''}</svg>`;
}

/** The brass wheel of the opening card (ui/open-intro.js), small, in the door's header. */
const GEAR = (() => {
  const teeth = 10, r0 = 8.2, r1 = 10.6, step = (Math.PI * 2) / teeth, pts = [];
  for (let i = 0; i < teeth; i++) {
    const a = i * step;
    for (const [r, an] of [[r0, a], [r0, a + step * 0.22], [r1, a + step * 0.34], [r1, a + step * 0.66], [r0, a + step * 0.78]]) pts.push(`${(12 + Math.cos(an) * r).toFixed(2)},${(12 + Math.sin(an) * r).toFixed(2)}`);
  }
  return `<svg class="door-gear" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><defs><linearGradient id="door-brass" x1="0" y1="0" x2="1" y2="1">`
    + '<stop offset="0" stop-color="#ffe29a"/><stop offset=".45" stop-color="#e2a544"/><stop offset="1" stop-color="#8a5a1c"/></linearGradient></defs>'
    + `<polygon points="${pts.join(' ')}" fill="url(#door-brass)" stroke="#3a2208" stroke-width=".8" stroke-linejoin="round"/>`
    + '<circle cx="12" cy="12" r="4.4" fill="#2a1608"/><circle cx="12" cy="12" r="1.6" fill="#e2a544"/></svg>';
})();

/** A big door button: icon, the word, and the small print under it. */
function bigBtn(act, icon, text, small, cls = '', extra = '') {
  return `<button type="button" class="door-btn ${cls}${small === null ? ' center' : ''}" data-act="${act}"${extra}>${ico(icon)}<span class="lbl"><span class="t">${text}</span>${small !== null ? `<span class="k">${small}</span>` : ''}</span></button>`;
}

/** The top of every panel: Back on the left, the screen's name, anything small on the right. */
function bar(title, back = 'Back', right = '') {
  return `<div class="door-top">${back ? backButton(back) : '<span class="door-top-gap"></span>'}${title ? `<h1 class="door-title">${esc(title)}</h1>` : ''}<span class="door-top-r">${right}</span></div>`;
}

/** The stamp language of the opening card's "Mostly.": a word that lands, tilted, inked. */
function stamp(word, tone = '', quiet = false) {
  return `<span class="door-stamp ${tone}"${quiet ? '' : ' data-stamp'}>${esc(word)}</span>`;
}

function reducedMotion(settings) {
  try {
    if (settings && settings().reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

export function createDoor(opts = {}) {
  const { bus, game, board, params } = opts;
  const root = opts.root || (typeof document !== 'undefined' ? document.getElementById('door') : null);
  // A hosted page never gets the mock (see the header). No lobby at all reads
  // as "not available", which is at least true.
  const lobby = (opts.lobby && !(isHosted && opts.lobby.debug && opts.lobby.debug.isMock)) ? opts.lobby : null;
  const settings = () => (opts.settings ? opts.settings() : ((typeof window !== 'undefined' && window.PBP && window.PBP.settings) || {}));
  const startGame = typeof opts.startGame === 'function' ? opts.startGame : () => {};
  if (!root) return { show() {}, hide() {}, isUp: () => false, debug: () => ({ ok: false, why: 'no #door' }), dispose() {} };

  const veil = document.createElement('div'); veil.className = 'door-veil';
  const card = document.createElement('div'); card.className = 'door-card'; card.setAttribute('role', 'dialog');
  root.append(veil, card);

  let screen = null;            // 'menu' | 'lobby' | 'found' | 'games' | 'profile' | 'replay' | 'end' | null (in game)
  let drawn = null;             // the screen the card last drew: a render of another one is an arrival, not a repaint
  let swapping = false;         // show() swapped screens inside a card that was already up: the old one leaves as a ghost
  let stopRolls = null;         // the profile's number rolls, stopped (landed) when the screen goes
  let setup = soloOptions();
  let current = null;           // the game in progress: { mode, match, startedAt, captures }
  let lastEnd = null;           // the record of the last finished game, for the end card + rematch
  let looking = false;
  let ask = null;               // an incoming challenge { id, name, accept, decline, timer }
  let people = [];
  let playingNow = [];          // [{ white, black, timeControl, moves, matchId?, watchable, watchers }], names only
  const watchApi = opts.watchApi || watchRoute;   // GET /v2/pbp/watch/:id (the tape head, spectate v2), swappable for a harness
  let lookKind = null;          // 'quick' | 'join' | 'host' | 'challenge' while looking
  let joiningId = null;         // the table a Join is sitting down at
  let note = '';                // one line after a refused seat
  let tableCount = null;        // for the menu's button, once asked
  let hostTc = HOST_TCS[1];
  const seenTables = new Set(); // rows already drawn once land without their entrance
  let replay = null;            // { moves, positions, marks, i, timer }
  const timers = new Set();
  const unbind = [];
  // PvP stakes (net/stake.js): online games only. Disabled, and drawn as nothing, until the
  // host says stakes are on for this account.
  const stake = opts.stake || createStake({ post: postToHost, onMessage: onHostMessage });
  let stakeChip = null;
  // Where Distraction's pictures come from (ui/pictures.js). Every start asks first while no
  // choice is saved, once (owner, 2026-09-30); after that the manager is the only way in.
  const pictures = opts.pictures || pictureChoice;
  // The rating ladder (ui/ladder.js): the chip, the Ladder screen, the end card's beat.
  const ladder = opts.ladder || createLadder();
  let ladderFrom = 'menu';      // where Esc goes back to from the Ladder screen
  let ratingBefore = null;      // ladder/me when the online game was dealt
  let endRating = null;         // { plan, played } for the end card of the last online game
  function freshMe() {
    return askHost().then(() => ((host.isHosted && !signedOut) ? ladder.refreshMe() : null))
      .then(() => ladder.paintChips(card)).catch(() => {});
  }
  /** Run a start, after the one-time picture ask if it is still owed. Backing out starts nothing. */
  function gate(start, backedOut = () => {}) {
    let owed = false;
    try { owed = pictures.needsChoice(); } catch { owed = false; }
    if (!owed) { start(); return; }
    Promise.resolve(pictures.ask({ still: still() })).then((go) => {
      if (go) start(); else backedOut();
    }).catch(() => start());
  }

  /**
   * Who the host says we are, behind one object so a harness can stand in for
   * the desktop (debug.host). Unhosted, whenIdentity resolves at once with the
   * empty identity and none of the account states below can happen.
   */
  let host = { isHosted, identity, whenIdentity };
  let signedOut = false;        // hosted, and there is no signed-in account behind the page

  /** Wait for the host's word, then remember whether there is anyone to play as. */
  async function askHost() {
    let id = null;
    try { id = await host.whenIdentity(); } catch { id = null; }
    if (!id) { try { id = host.identity(); } catch { id = null; } }
    signedOut = !!host.isHosted && !(id && id.online !== false && id.unifiedId);
    return id;
  }

  /** Nothing is asked of the lobby before the host has spoken, and nothing at all for nobody. */
  async function afterHost(fn) {
    await askHost();
    if (signedOut) throw new Error('cancelled');    // the quiet rejection: look() re-renders, no squelch
    return fn();
  }

  function later(fn, ms) { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); return t; }
  function hudEl() { return document.getElementById('hud'); }
  function camEl() { return document.getElementById('cam-views'); }
  function sfx(name) { try { if (board.sfx && board.sfx.play) board.sfx.play(name); } catch { /* quiet */ } }
  function still() { return reducedMotion(settings); }

  // ---------------------------------------------------------------- show / hide
  function show(name) {
    if (current && !game.isOver() && current.startedAt) {
      if (current.mode === 'online') return;
      toMenu();
      if (name === 'menu') return;
    }
    if (name === 'replay' && !replay) name = 'games';
    if (screen === 'replay' && name !== 'replay') closeReplay();
    if (['lobby', 'end'].includes(screen) && !['lobby', 'found', 'end'].includes(name)) leaveLobby();
    // a new screen inside a card that is already up: the old one leaves as the new one lands
    swapping = !root.hidden && screen !== null && screen !== name;
    if (screen !== name) { try { ui.open(); } catch { /* sound is optional */ } }
    screen = name;
    if ((name === 'lobby' || (name === 'end' && lastEnd?.mode === 'online')) && !offList) enterLobby();
    board.drag?.suspend?.(true);
    root.hidden = false;
    root.className = 'door up screen-' + name + (still() ? ' still' : '') + (looking ? ' looking' : '');
    if (name === 'menu' || name === 'lobby') freshMe();
    for (const el of [hudEl(), camEl()]) if (el) el.classList.add('parked');
    try { board.setCameraSway(name === 'replay' ? 0 : T.menuSway); } catch { /* no rig */ }
    render();
    paintStake();
  }

  /**
   * The OUT of the whole door: a frozen copy of it fades and sinks over the
   * board while the real one is already gone, so the game underneath starts
   * at once (deal() hides and starts in one go) and the card still goes
   * somewhere instead of vanishing in one frame.
   */
  function leaveCard() {
    if (root.hidden || screen === null || still()) return;
    try {
      const g = root.cloneNode(true);
      g.removeAttribute('id');
      for (const n of g.querySelectorAll('[id]')) n.removeAttribute('id');
      for (const n of g.querySelectorAll('.fx-ghost')) n.remove();
      g.classList.add('door-out');
      g.setAttribute('aria-hidden', 'true');
      g.inert = true;
      document.body.append(g);
      later(() => g.remove(), 320);
    } catch { /* the door just goes */ }
  }

  function hide() {
    leaveCard();
    if (stopRolls) { stopRolls(); stopRolls = null; }
    if (screen === 'lobby' || screen === 'end') leaveLobby();
    if (screen === 'replay') closeReplay();
    screen = null;
    drawn = null;
    root.hidden = true;
    board.drag?.suspend?.(false);
    root.className = 'door';
    for (const el of [hudEl(), camEl()]) if (el) el.classList.remove('parked');
    try { board.setCameraSway(0); } catch { /* no rig */ }
    paintStake();
  }

  function render() {
    const old = card.querySelector('.door-body:not(.fx-ghost)');
    const kept = old && old.querySelector('.tables-scroll');
    const top = kept ? kept.scrollTop : 0;
    // A new screen, not a repaint of this one: it lands (stagger), the old one leaves (ghost).
    const fresh = drawn !== screen;
    drawn = screen;
    if (fresh && stopRolls) { stopRolls(); stopRolls = null; }
    if (fresh && swapping && old) leaveGhost(old, { still: still() });
    swapping = false;
    // the ghost stays (it removes itself); everything else goes
    for (const n of Array.from(card.children)) if (!n.classList.contains('fx-ghost')) n.remove();
    const body = document.createElement('div'); body.className = 'door-body screen-' + screen;
    body.innerHTML = ({ menu, lobby: lobbyScreen, found, games, profile, replay: replayScreen, end, ladder: () => ladder.screenHtml(me()) }[screen] || menu)();
    card.prepend(body);   // first, so every card.querySelector finds the live screen before a ghost
    if (fresh) land(body);
    if (screen === 'end' && endRating) ladder.mountEnd(body.querySelector('.rating-slot'), endRating, { still: still(), sfx });
    ladder.paintChips(body);
    const sc = body.querySelector('.tables-scroll'); if (sc && top) sc.scrollTop = top;
    if (screen === 'menu') countTables();
    if (screen === 'profile') { const inp = body.querySelector('input'); if (inp && !inp.readOnly) inp.addEventListener('change', () => setPlayerName(inp.value)); }
    if (screen === 'replay') { const r = body.querySelector('.door-scrub'); if (r) r.addEventListener('input', () => stepReplay(Number(r.value))); }
    const focus = body.querySelector('.door-btn.primary') || body.querySelector('.door-btn') || body.querySelector('button');
    if (focus && !still()) later(() => { try { focus.focus({ preventScroll: true }); } catch { /* fine */ } }, 60);
  }

  /**
   * A screen's arrival, once per screen (never on a poll's repaint): the
   * children stagger in, the header gear turns a tooth, the profile's
   * numbers roll, and a stamp (a result, "Matched.") lands with a knock on
   * the card and its cue a beat after the line it sits on.
   */
  function land(body) {
    const calm = still();
    screenIn(body, { still: calm });
    const rolls = [];
    let i = 0;
    for (const n of body.querySelectorAll('[data-roll]')) {
      rolls.push(countUp(n, { still: calm, delay: 160 + 45 * i++, onTick: () => { try { ui.tick(); } catch { /* quiet */ } } }));
    }
    if (rolls.length) stopRolls = () => { for (const s of rolls) s(); };
    const st = body.querySelector('[data-stamp]');
    if (st) later(() => { if (!st.isConnected) return; try { ui.stamp(); } catch { /* quiet */ } kick(card, 'knock', { still: calm }); }, calm ? 0 : T.stampLandMs);
  }

  /** The solo button and the level row say the same level; a pick repaints both in place. */
  const levelWord = () => LEVELS[setup.level].label.toLowerCase() + ' computer';
  function levelHtml() {
    return `<div class="level-row" role="group" aria-label="Computer strength">${LEVEL_ORDER.map(id => `<button type="button" class="level-pill${id === setup.level ? ' on' : ''}" data-act="level" data-id="${id}" aria-pressed="${id === setup.level}" title="${esc(LEVELS[id].blurb)}">${esc(LEVELS[id].label)}</button>`).join('')}</div>`
      + `<p class="level-blurb">${esc(LEVELS[setup.level].blurb)}</p>`;
  }
  function paintLevel() {
    const label = card.querySelector('[data-act=solo] .k'); if (label) label.textContent = levelWord();
    const slot = card.querySelector('.level-slot'); if (slot) slot.innerHTML = levelHtml();
  }

  /** Solo setup: two short chip rows, the same pills as the level, behind one disclosure. */
  const SIDES = [['w', 'White'], ['b', 'Black'], ['random', 'Surprise me']];
  const CLOCKS = [['0', 'Untimed'], ['300000', '5 min'], ['900000', '15 min']];
  const setupWord = () => `${(SIDES.find(([v]) => v === setup.side) || SIDES[0])[1].toLowerCase()}, ${(CLOCKS.find(([v]) => v === String(setup.clockMs)) || CLOCKS[0])[1].toLowerCase()}`;
  function setupHtml() {
    const row = (key, label, opts, cur) => `<div class="setup-row"><span class="setup-k">${label}</span><div class="level-row" role="group" aria-label="${label}">${opts.map(([v, t]) => `<button type="button" class="level-pill${v === cur ? ' on' : ''}" data-act="setup" data-key="${key}" data-id="${key}:${v}" aria-pressed="${v === cur}">${t}</button>`).join('')}</div></div>`;
    return row('side', 'Your pieces', SIDES, setup.side) + row('clockMs', 'Clock', CLOCKS, String(setup.clockMs));
  }
  function paintSetup() {
    const slot = card.querySelector('.setup-slot'); if (slot) slot.innerHTML = setupHtml();
    const word = card.querySelector('.door-setup summary .k'); if (word) word.textContent = setupWord();
  }
  /** A chip turned on: its cue, and the new pick pops (the old one simply lets go). */
  function picked(sel) {
    try { ui.select(); } catch { /* quiet */ }
    const on = card.querySelector(sel);
    if (on) kick(on, 'pop', { still: still() });
  }
  /** A refused press: the one quiet cue (it replaces the board's squelch here: failure subtracts). */
  function nope() {
    try { ui.error(); } catch { /* quiet */ }
  }

  // ---------------------------------------------------------------- stakes
  /** The pill row: Off, 15 min, 30 min (only while the lock could take it), 5, 10, 25 sparkles. */
  function stakeHtml() {
    const s = stake.state;
    if (!s.enabled) return '';
    const L = s.labels;
    const lock = !!s.locked;
    const row = stakePills(s.options, s.timeOk).map((p) => `<button type="button" class="stake-pill${sameStake(p, s.pick) ? ' on' : ''}" data-act="stake" data-id="${esc(p.kind + ':' + p.amount)}"${(lock || s.busy) ? ' disabled' : ''}>${esc(stakeLabel(p, L))}</button>`).join('');
    const them = (s.match && s.them && !isNone(s.them)) ? `<span class="stake-chip">${esc(L.them)} <b>${esc(stakeLabel(s.them, L))}</b></span>` : '';
    const note = s.refusal ? esc(refusalText(s.refusal, L)) : (lock ? esc(L.locked) : '');
    return `<div class="stake-row" role="group" aria-label="${esc(L.title)}"><span class="stake-k">${esc(L.title)}</span>${row}</div>`
      + ((them || note) ? `<div class="stake-meta">${them}${note ? `<span class="stake-note">${note}</span>` : ''}</div>` : '');
  }

  /** Stakes changed: repaint only the stake bits (a full render would restart the count). */
  function paintStake() {
    const slot = card.querySelector('.stake-slot');
    if (slot) slot.innerHTML = stakeHtml();
    const res = card.querySelector('.stake-result');
    if (res) res.textContent = stake.resultLine() || '';
    // over the board during the game: you and them, until it is over
    const s = stake.state;
    const show = screen === null && s.enabled && s.match && !s.ended && (!isNone(s.you) || !isNone(s.them));
    if (!show) { if (stakeChip) stakeChip.hidden = true; return; }
    if (!stakeChip) { stakeChip = document.createElement('div'); stakeChip.className = 'stake-live'; document.body.append(stakeChip); }
    const L = s.labels;
    const part = (who, st) => isNone(st) ? '' : `<span>${esc(who)} <b>${esc(stakeLabel(st, L))}</b></span>`;
    stakeChip.innerHTML = part(L.you, s.you) + part(L.them, s.them) + (s.locked ? `<span class="lock">${esc(L.locked)}</span>` : '');
    stakeChip.hidden = false;
  }
  unbind.push(stake.subscribe(paintStake));

  // ---------------------------------------------------------------- screens
  const me = () => playerName(settings());

  function menu() {
    const saved = readSolo();
    const tile = (act, icon, word) => `<button type="button" class="door-tile" data-act="${act}">${ico(icon)}<span>${word}</span></button>`;
    // Leave closes the board (what Esc does here); only a host has a window to close.
    const leave = host.isHosted ? backButton('Leave') : '';
    return `
      <div class="door-top">${leave || '<span class="door-top-gap"></span>'}<span></span><span class="door-top-r door-tag">${ico('profile')}<b>${esc(me())}</b>${ladder.chipHtml()}</span></div>
      <div class="door-brand">${GEAR}<div class="door-brand-t"><h1 class="door-logo" data-text="Piece by Piece"><span>Piece by Piece</span></h1><p class="door-h">Your next move.</p></div></div>
      ${saved ? bigBtn('continue', 'go', 'Continue game', esc(fmtMoves(saved.moves.length)), 'primary big') : ''}
      ${bigBtn('solo', 'solo', 'Play solo', esc(levelWord()), saved ? 'big' : 'primary big')}
      <div class="level-slot">${levelHtml()}</div>
      <div class="door-pair">
        ${bigBtn('lobby', 'tables', 'Open tables', `<span class="tables-count">${esc(countWord())}</span>`, 'tables-btn')}
        ${bigBtn('hotseat', 'board', 'Two here', 'one board')}
      </div>
      <details class="door-setup"><summary><span class="chev" aria-hidden="true"></span><span>Solo setup</span><span class="k">${esc(setupWord())}</span></summary>
        <div class="door-settings"><div class="setup-slot">${setupHtml()}</div>
        ${saved ? '<p class="door-sub">A new solo game replaces your unfinished one.</p>' : ''}</div></details>
      <div class="door-tiles${pictures.available() ? '' : ' three'}">
        ${tile('games', 'games', 'Past games')}${tile('profile', 'profile', 'Profile')}${tile('ladder', 'ladder', 'Ladder')}
        ${pictures.available() ? tile('pictures', 'pictures', 'Pictures') : ''}
      </div>`;
  }

  const countWord = () => (tableCount ? `${tableCount} open` : 'online');

  /** The menu's button says how many tables are open: one read, never a listing. */
  function countTables() {
    if (!lobby || typeof lobby.tables !== 'function') return;
    askHost().then(() => (signedOut ? null : lobby.tables())).then((t) => {
      if (!t) return;
      tableCount = (t.open || []).length;
      const el = screen === 'menu' && card.querySelector('.tables-count');
      if (el) el.textContent = countWord();
    }).catch(() => {});
  }

  function lobbyScreen() {
    // Three rooms: the one with tables in it, the one with nobody to play as
    // (hosted, signed out), and the one with no lobby behind it at all. The
    // last two are one line each and every button is off, never a dead one.
    const canPlay = !!lobby && !signedOut;
    const hosting = looking && lookKind === 'host';
    const askRow = ask ? `
      <div class="door-ask"><span><b>${esc(ask.name)}</b> wants a game</span>
        <button type="button" class="door-pill hot" data-act="accept">Play</button>
        <button type="button" class="door-pill" data-act="decline">Ignore</button></div>` : '';
    const dot = '<span class="door-dot"></span>';
    const quickLabel = lookKind === 'quick' ? `Looking for a game ${dot}`
      : lookKind === 'challenge' ? `Waiting for an answer ${dot}` : `${ico('play')}<span class="t">Quick match</span>`;
    const hostLabel = hosting ? `At your table ${dot}` : `${ico('host')}<span class="t">Host a table</span> <span class="k">${esc(hostTc.label)}</span>`;
    const clocks = (canPlay && !looking) ? `<div class="level-row tc-row" role="group" aria-label="Your table's clock">${HOST_TCS.map((t) => `<button type="button" class="level-pill${t === hostTc ? ' on' : ''}" data-act="hosttc" data-id="${esc(t.label)}" aria-pressed="${t === hostTc}">${esc(t.label)}</button>`).join('')}</div>` : '';
    const stop = hosting ? 'Leave the table' : lookKind === 'join' ? 'Stand up' : 'Stop looking';
    const ladderBtn = canPlay ? `<button type="button" class="door-pill ladder-me" data-act="ladder">${ico('ladder')}<span>Ladder</span>${ladder.chipHtml()}<span class="ladder-rank"></span></button>` : '';
    return `
      ${bar('Open tables', 'Back', ladderBtn)}
      <div class="tables-panel">
        <div class="tables-side">
          <p class="door-sub tables-sub">${subHtml()}</p>
          ${canPlay ? '<div class="stake-slot">' + stakeHtml() + '</div>' : ''}
          ${askRow}
          <div class="door-row tables-actions">
            <button type="button" class="door-btn primary" data-act="quick" title="whoever waited longest" ${((looking && !hosting) || !canPlay) ? 'disabled' : ''}>${quickLabel}</button>
            <button type="button" class="door-btn host-btn${hosting ? ' on' : ''}" data-act="host" ${(looking || !canPlay || typeof lobby.host !== 'function') ? 'disabled' : ''}>${hostLabel}</button>
          </div>
          ${clocks}
          <p class="tables-note" role="status">${esc(note)}</p>
        </div>
        <div class="tables-slot">${tablesHtml()}</div>
      </div>
      ${looking ? `<button type="button" class="door-btn slim" data-act="cancel">${stop}</button>` : ''}`;
  }

  function subHtml() {
    if (signedOut) return 'not signed in';
    if (!lobby) return 'online play is not available right now';
    if (looking && lookKind === 'host') return `your table is open as <b>${esc(me())}</b>`;
    return `<b>${people.length}</b> open &middot; <b>${playingNow.length}</b> playing now`;
  }

  /** The scroll region: the open tables, then the games on right now. */
  function tablesHtml() {
    if (signedOut) return '<div class="door-empty">sign in to see open tables</div>';
    if (!lobby) return '<div class="door-empty">the board here still works - play here from the menu</div>';
    const hosting = looking && lookKind === 'host';
    const mine = hosting ? `
      <li class="table-row mine"><span class="table-name">${esc(me())}</span>
        <span class="table-meta"><b>${esc(hostTc.label)}</b><span>your table</span></span><span class="door-dot"></span></li>` : '';
    const rows = people.map((p, i) => {
      const fresh = !seenTables.has(p.id);
      const sitting = lookKind === 'join' && joiningId === p.id;
      const meta = [tcWord(p.timeControl) ? `<b>${esc(tcWord(p.timeControl))}</b>` : '', `<span>${esc(waitWord(p.waitingSince))}</span>`,
        (p.rating !== null && p.rating !== undefined && Number.isFinite(Number(p.rating))) ? `<span class="rating">${esc(Math.round(Number(p.rating)))}</span>` : ''].join('');
      return `
      <li class="table-row${fresh ? ' fresh' : ''}${sitting ? ' sitting' : ''}" style="--i:${Math.min(i, 8)}" data-id="${esc(p.id)}">
        <span class="table-name">${esc(p.name)}</span>
        <span class="table-meta">${meta}</span>
        <button type="button" class="table-join" data-act="join" data-id="${esc(p.id)}" aria-label="join ${esc(p.name)}" ${(looking && !hosting) ? 'disabled' : ''}>${sitting ? 'sitting <span class="door-dot"></span>' : 'join'}</button>
      </li>`;
    }).join('');
    for (const p of people) seenTables.add(p.id);
    const open = (mine || rows) ? `<ul class="tables-list" aria-label="Open tables">${mine}${rows}</ul>`
      : '<div class="door-empty tables-empty">no open tables. host one</div>';
    // A watchable game (spectating on, both players said yes) gets a Watch button; `spectate`,
    // because `watch` is already the shelf's review of a past game.
    const games = playingNow.map((g) => `
      <li class="playing-row"><span class="who">${esc(g.white)} <i>vs</i> ${esc(g.black)}</span>
        <span class="meta">${esc([tcWord(g.timeControl), fmtMoves(g.moves), g.watchers > 0 ? g.watchers + ' watching' : ''].filter(Boolean).join(' - '))}</span>${g.watchable && g.matchId ? `
        <button type="button" class="table-join" data-act="spectate" data-id="${esc(g.matchId)}" aria-label="watch ${esc(g.white)} against ${esc(g.black)}">watch</button>` : ''}</li>`).join('');
    return `<div class="tables-scroll" tabindex="0" aria-label="Open tables and games on now">${open}
      <h2 class="tables-h">playing now</h2>
      ${games ? `<ul class="playing-list">${games}</ul>` : '<p class="tables-none">no games on right now</p>'}</div>`;
  }

  /**
   * A poll landed: repaint the list slot and the count line only, keeping the
   * scroll and the focused Join where they were.
   */
  function paintTables() {
    if (screen !== 'lobby') return;
    const slot = card.querySelector('.tables-slot');
    if (!slot) { render(); return; }
    const sc = slot.querySelector('.tables-scroll');
    const top = sc ? sc.scrollTop : 0;
    const a = document.activeElement;
    const inSlot = !!(a && slot.contains(a));
    const focusId = inSlot && a.dataset ? a.dataset.id : null;
    slot.innerHTML = tablesHtml();
    const sc2 = slot.querySelector('.tables-scroll');
    if (sc2) sc2.scrollTop = top;
    if (inSlot) {
      const back = (focusId && slot.querySelector(`[data-act=join][data-id="${CSS.escape(focusId)}"]`)) || sc2;
      try { back && back.focus({ preventScroll: true }); } catch { /* fine */ }
    }
    const sub = card.querySelector('.tables-sub');
    if (sub) sub.innerHTML = subHtml();
  }

  function found() {
    const m = current && current.match;
    const side = m ? m.side : 'w';
    return `
      ${bar('', 'Cancel')}
      <div class="door-found">
        ${stamp('Matched.')}
        <p class="line">with <b>${esc(m ? m.opponent.name : '')}</b></p>
        <p class="side">you play <b class="${side}">${side === 'w' ? 'white' : 'black'}</b></p>
        <div class="stake-slot">${stakeHtml()}</div>
        <div class="door-count" id="door-count"></div>
      </div>
      ${bigBtn('go', 'go', 'Start now', null, 'primary')}`;
  }

  function games() {
    const list = listGames();
    const rows = list.map((g, i) => {
      const o = outcome(g);
      // the result is a small stamp on the row, in the opening card's language
      const word = o === 'win' ? 'Won' : o === 'loss' ? 'Lost' : o === 'draw' ? 'Draw'
        : (g.result ? (g.result.winner ? (g.result.winner === 'w' ? 'White' : 'Black') : 'Draw') : 'Open');
      const iq = finalIq(g);
      return `
      <li class="door-item" style="--i:${Math.min(i, 8)}">
        <span class="res ${o || 'none'}">${esc(word)}</span>
        <span class="who">${esc(g.opponent || 'a friend here')}<span class="meta">${esc(fmtWhen(g.at))} &middot; ${esc(fmtMoves(g.plies))}${iq !== null ? ` &middot; iq ${iq}` : ''}</span></span>
        <button type="button" class="door-pill" data-act="watch" data-id="${esc(g.id)}" aria-label="watch the game against ${esc(g.opponent || 'a friend here')}">${ico('replay')}Watch</button>
      </li>`;
    }).join('');
    return `
      ${bar('Past games', 'Back', list.length ? `<span class="door-count-k">${list.length} kept</span>` : '')}
      ${list.length ? `<ul class="door-list">${rows}</ul>` : `<div class="door-empty big">${ico('games')}<span>No games on the shelf yet. The first one you finish lands here.</span></div>`}`;
  }

  function profile() {
    const s = profileStats();
    // Every number rolls up from nothing as the screen lands (data-roll, see land()).
    const stat = (n, l, cls = '') => `<div class="door-stat ${cls}"><span class="n" data-roll>${esc(n)}</span><span class="l">${l}</span></div>`;
    // Hosted, the name is the account's: the server lists it, so it is shown
    // here and not typed. Unhosted (a plain browser), typed and kept locally.
    const hosted = !!host.isHosted;
    const decided = s.wins + s.losses + s.draws;
    const pc = (n) => (decided ? (100 * n / decided).toFixed(1) : 0);
    const initial = (me() || '?').trim().charAt(0).toUpperCase() || '?';
    return `
      ${bar('Profile', 'Back', '<span class="door-count-k">this device</span>')}
      <div class="door-plate">
        <span class="door-mono" aria-hidden="true">${esc(initial)}</span>
        <div class="door-name"><input type="text" maxlength="24" value="${esc(me())}" aria-label="your name at the board" spellcheck="false"${hosted ? ' readonly' : ''}>
          <span class="door-name-k">${hosted ? 'from your account' : 'your name at the board'}${ladder.chipHtml()}</span></div>
        ${s.streak > 1 ? stamp(`${s.streak} in a row`, 'small', true) : ''}
      </div>
      <div class="door-record">
        <div class="rec win"><span class="n" data-roll>${s.wins}</span><span class="l">won</span></div>
        <div class="rec loss"><span class="n" data-roll>${s.losses}</span><span class="l">lost</span></div>
        <div class="rec draw"><span class="n" data-roll>${s.draws}</span><span class="l">drawn</span></div>
        <div class="rec-bar" role="img" aria-label="${s.wins} won, ${s.losses} lost, ${s.draws} drawn of ${s.games} games">
          <i class="win" style="--w:${pc(s.wins)}%"></i><i class="loss" style="--w:${pc(s.losses)}%"></i><i class="draw" style="--w:${pc(s.draws)}%"></i>
        </div>
      </div>
      <div class="door-stats">
        ${stat(s.games, 'games')}${stat(s.captures, 'taken')}${stat(fmtMoves(s.plies), 'played')}
        ${stat(s.favourite || '-', 'favourite')}${stat(fmtDuration(s.ms), 'at the board')}
        ${s.lowIq !== null && s.lowIq !== undefined ? stat(s.lowIq, 'lowest iq', 'pink') : stat('-', 'lowest iq')}
      </div>`;
  }

  function replayScreen() {
    const r = replay;
    const n = r.i;
    const san = n === 0 ? 'start' : `<i>${Math.ceil(n / 2)}${n % 2 ? '.' : '...'}</i>${esc(r.moves[n - 1])}`;
    // The strip sits at the bottom so the board is the subject; Back leads it like every other panel.
    return `
      ${bar('', 'Back', `<span class="replay-title">${esc(r.title)}</span>`)}
      <div class="door-strip">
        <button type="button" class="door-btn door-ico" data-act="rstart" title="to the start" aria-label="to the start">&#9198;</button>
        <button type="button" class="door-btn door-ico" data-act="rprev" title="back one" aria-label="back one">&#9664;</button>
        <span class="san">${san}</span>
        <button type="button" class="door-btn door-ico" data-act="rnext" title="on one" aria-label="on one">&#9654;</button>
        <button type="button" class="door-btn door-ico ${r.timer ? 'primary' : ''}" data-act="rplay" title="play" aria-label="play">${r.timer ? '&#10074;&#10074;' : '&#9654;&#9654;'}</button>
      </div>
      <input class="door-scrub" type="range" min="0" max="${r.moves.length}" value="${n}" aria-label="move" style="--p:${r.moves.length ? (100 * n / r.moves.length).toFixed(1) : 0}%">`;
  }

  function end() {
    const g = lastEnd || {};
    const o = outcome(g);
    const line = resultLine(g);
    // The result is a stamp, the way the opening card says "Mostly."; the line under it says how.
    // A loss gets its stamp quietly (no knock, no cue): failure subtracts, it never punishes.
    const winner = g.result && g.result.winner;
    const word = o === 'win' ? 'Won.' : o === 'loss' ? 'Lost.' : (o === 'draw' || !winner) ? 'Drawn.' : (winner === 'w' ? 'White wins.' : 'Black wins.');
    const tone = o === 'loss' ? 'loss' : o === 'draw' || !winner ? 'draw' : 'win';
    const how = line.replace(/^(You won|You lost|White won|Black won|Draw)\s*/, '');
    return `
      ${bar('', 'Menu')}
      <div class="door-end">
        ${stamp(word, 'big ' + tone, tone === 'loss')}
        <p class="result ${o || ''}">${esc(how)}</p>
        <p class="tally">${esc(fmtMoves(g.plies))} &middot; ${esc(fmtDuration(g.durationMs))}</p>
        ${g.mode === 'online' ? `<div class="end-rating">${ladder.endSlotHtml()}</div>` : ''}
        ${g.mode === 'online' ? '<p class="stake-result">' + esc(stake.resultLine() || '') + '</p>' : ''}
      </div>
      ${ask ? `<div class="door-ask"><span><b>${esc(ask.name)}</b> wants a rematch</span><button type="button" class="door-pill hot" data-act="accept">Play</button><button type="button" class="door-pill" data-act="decline">Decline</button></div>` : ''}
      <div class="door-pair end-acts">
        ${bigBtn('rematch', 'go', 'Rematch', null, 'primary')}
        ${bigBtn('watch', 'replay', 'Review', null, '', ` data-id="${esc(g.id)}"`)}
      </div>
      <div id="door-recap">${recap()}</div>`;
  }

  function waitWord(since) {
    const s = Math.max(0, Math.round((Date.now() - (since || Date.now())) / 1000));
    if (s < 60) return 'just sat down';
    return 'waiting ' + Math.floor(s / 60) + ' min';
  }

  // ---------------------------------------------------------------- lobby
  let offList = null, offAsk = null;
  async function enterLobby() {
    askHost().then(() => { if (host.isHosted && !signedOut) stake.limits(); }).catch(() => {});
    // The host first: a signed-out account has nothing to enter with, and
    // the screen has to say so rather than sit on an empty list.
    await askHost();
    if (!['lobby', 'end'].includes(screen)) return;
    if (signedOut || !lobby) { if (screen === 'lobby') render(); return; }
    if (offList) offList();
    const tables = (t) => { people = (t && t.open) || []; playingNow = (t && t.playing) || []; tableCount = people.length; paintTables(); };
    if (typeof lobby.onTables === 'function') {
      // browse: the list is read, the player is not put on it
      offList = lobby.onTables(tables);
    } else {
      // an older lobby with no tables: stand in it, as before
      try { await lobby.enter({ name: me() }); } catch { /* the door still opens */ }
      if (!['lobby', 'end'].includes(screen)) { lobby.leave(); return; }
      offList = lobby.onList((l) => { people = l || []; paintTables(); });
    }
    if (offAsk) offAsk();
    offAsk = lobby.onChallenge((offer) => {
      if (!['lobby', 'end'].includes(screen) || ask) { try { offer.decline(); } catch { /* fine */ } return; }
      ask = offer;
      sfx('pop');
      ask.timer = later(() => { if (ask === offer) { try { offer.decline(); } catch { /* fine */ } ask = null; if (['lobby', 'end'].includes(screen)) render(); } }, T.askTimeoutMs);
      render();
    });
    try {
      if (typeof lobby.tables === 'function') tables(await lobby.tables());
      else { people = await lobby.list(); paintTables(); }
    } catch { /* the poll fills it in */ }
  }
  function leaveLobby() {
    looking = false;
    lookKind = null;
    joiningId = null;
    note = '';
    if (ask) { try { ask.decline(); } catch { /* fine */ } ask = null; }
    if (offList) { offList(); offList = null; }
    if (offAsk) { offAsk(); offAsk = null; }
    try { if (lobby) lobby.leave(); } catch { /* fine */ }
  }

  let lookSeq = 0;
  async function look(promise, kind = 'challenge', id = null) {
    // A new look may start while one is out (Join from your own table): the old
    // one rejects 'cancelled' later, and must not clear the new one's state.
    const mine = ++lookSeq;
    looking = true;
    lookKind = kind;
    joiningId = id;
    note = '';
    root.classList.add('looking');
    if (screen === 'lobby') render();
    const done = () => { looking = false; lookKind = null; joiningId = null; root.classList.remove('looking'); };
    try {
      const match = await promise;
      if (mine !== lookSeq) return;
      done();
      matched(match);
    } catch (err) {
      if (mine !== lookSeq) return;
      done();
      const why = err && err.message;
      note = SEAT_NOTES[why] && why !== 'left' ? SEAT_NOTES[why] : '';
      if (screen === 'lobby') render();
      if (SEAT_NOTES[why]) nope();
    }
  }

  function matched(match) {
    current = { mode: 'online', match, startedAt: 0, captures: { w: 0, b: 0 } };
    if (match && match.id) stake.begin(match.id);
    sfx('promote');
    show('found');
    countdown();
  }

  let countTimer = null;
  function countdown() {
    // with stakes on, two more beats: long enough to read what the other side put down
    let n = stake.state.enabled ? 5 : 3;
    const el = () => card.querySelector('#door-count');
    const tick = () => {
      const c = el();
      if (!c || screen !== 'found') return;
      c.textContent = String(n);
      c.classList.remove('tick'); void c.offsetWidth; c.classList.add('tick');
      sfx('tick');
      if (n === 0) { go(); return; }
      n--;
      countTimer = later(tick, T.countdownStepMs);
    };
    // "go" lands on 0: three ticks and the board
    tick();
  }
  function go() {
    if (!current) return;
    if (countTimer) { timers.delete(countTimer); clearTimeout(countTimer); countTimer = null; }
    const m = current.match;
    // An accepted challenge is a round trip on a server, and the lobby handed
    // the door its Match before the answer landed (net/lobbyServer.js,
    // offer()): the id is filled in behind it, on `ready`. Dealing a match
    // with no id throws in startOnlineMatch, so the count holds at zero until
    // the id is there - or until it is not, which is the lobby's way of
    // saying he is not playing after all.
    if (current.mode === 'online' && m && !m.id && m.ready && typeof m.ready.then === 'function') {
      const mine = current;
      m.ready.then((r) => {
        if (current !== mine) return;
        if (r && r.id) { go(); return; }
        current = null;
        nope();
        show('lobby');
      }).catch(() => { if (current === mine) { current = null; show('lobby'); } });
      return;
    }
    // The lobby was kept through the found screen so the pairing could land;
    // the game has, so stand up (see the header). The next visit to the lobby
    // screen enters again, because leaveLobby drops the subscription show()
    // keys the re-entry on.
    leaveLobby();
    if (screen === 'found') { screen = null; }
    deal(current.mode, m);
  }

  // ---------------------------------------------------------------- the game itself
  function deal(mode, match = null, restore = null) {
    current = { mode, match, options: restore?.options || setup, startedAt: Date.now() - (restore?.durationMs || 0), captures: { w: 0, b: 0 } };
    if (restore) for (const [i, san] of restore.moves.entries()) if (san.includes('x')) current.captures[i % 2 ? 'b' : 'w']++;
    if (mode === 'online' && match && match.id) { stake.begin(match.id); stake.play?.(); }
    if (mode === 'online') ratingBefore = ladder.snapshot();
    hide();
    startGame({ mode, match, options: setup, restore });
  }

  function onCapture(p) { if (current && p && p.by) current.captures[p.by] = (current.captures[p.by] || 0) + 1; }

  function onGameOver() {
    if (!current) return;
    // A watched game is not ours: nothing goes on the shelf. The last position stays up a moment, then the tables.
    if (current.mode === 'watch') {
      const watched = current;
      later(() => { if (current === watched) leaveWatch(watchLine(game.result && game.result())); }, still() ? 1500 : T.watchEndMs);
      return;
    }
    let rec = {};
    try { rec = game.record ? game.record() : {}; } catch { rec = {}; }
    const m = current.match;
    const history = game.rules.chess.history({ verbose: true });
    const captures = { w: 0, b: 0 };
    for (const move of history) if (move.captured) captures[move.color]++;
    // the seat off the record when the driver knows it (an online seat may have
    // been corrected by the server after the deal), else the lobby's word
    const seat = (rec.me === 'w' || rec.me === 'b') ? rec.me : (m ? m.side : null);
    lastEnd = saveGame({
      mode: current.mode, me: seat, opponent: rec.opponent || (m ? m.opponent.name : 'a friend here'),
      moves: rec.moves || [], plies: rec.plies || 0, result: rec.result || null, fen: rec.fen || history[0]?.before,
      durationMs: rec.durationMs ?? (Date.now() - current.startedAt), captures, clocks: rec.clocks || null,
      iq: plainIq(rec.iq),   // the fall so far; settleIq() brings in the last grade
    });
    settleIq(lastEnd, rec.iq);
    endRating = current.mode === 'online' ? rateEnd(seat, rec) : null;
    if (current.mode === 'online' && m && m.id) stake.end(m.id);
    const finished = current;
    current = null;
    // the board's own end beat first, then the card
    later(() => { if (!current && screen === null) { lastEnd.rematchOf = finished; show('end'); } }, still() ? 0 : T.endCardDelayMs);
  }

  /**
   * The end card's rating beat: the server's rating_delta for our seat off the
   * result, then ladder/me again for the new number, rank and best. A read that
   * lands after the beat played only fills in the rank line.
   */
  function rateEnd(seat, rec) {
    let res = null;
    try { res = game.result ? game.result() : null; } catch { res = null; }
    const rd = res && res.ratingDelta;
    const args = { delta: rd && seat ? rd[seat] : null, before: ratingBefore, plies: rec.plies || 0, reason: res && res.reason };
    const er = { plan: endPlan(args), played: false };
    ladder.refreshMe().then((after) => {
      if (endRating !== er || !after) return;
      const next = endPlan({ ...args, after });
      if (!er.played) { er.plan = next; return; }
      er.plan = { ...er.plan, note: next.note || er.plan.note };
      const n = screen === 'end' && card.querySelector('.rating-note.rank');
      if (n) n.textContent = er.plan.note; else if (screen === 'end' && er.plan.rated) render();
    }).catch(() => {});
    return er;
  }

  // ---------------------------------------------------------------- the fall (IQ recap)
  /** The end card's IQ recap (door/fall.js): your own seat only, nothing for a game with no grades. */
  function recap() { return lastEnd ? recapHtml(lastEnd, { extra: fallActs() }) : ''; }
  /** Under the recap: watch the fall (a loss with a worst move of yours), then the picture. */
  function fallActs() {
    const watch = outcome(lastEnd) === 'loss' && fallPlan(lastEnd)
      ? '<button type="button" class="door-pill" data-act="fallwatch">Watch the fall</button>' : '';
    return `<div class="fall-acts">${watch}<button type="button" class="door-pill" data-act="fallsave">Save picture</button>`
      + '<button type="button" class="door-pill" data-act="fallcopy">Copy</button></div><p class="fall-note" role="status"></p>';
  }
  /**
   * Watch the fall: the review from just before your worst move, through the
   * capture that punished it (door/fall.js fallPlan), with the live capture
   * choreography and, the director allowing, its replay. Esc goes back to the
   * end card rather than the shelf.
   */
  function watchFall() {
    const g = lastEnd;
    const plan = g && g.id ? fallPlan(g) : null;
    if (!plan) return;
    openReplay(g.id);
    if (!replay || screen !== 'replay') return;
    replay.fall = plan;
    replay.title = goneLine(plan.worst);
    stepReplay(plan.from);
    try { board.director?.allowUnderDoor?.(true); } catch { /* an older director: the moves still play */ }
    replay.stopAt = plan.to;
    playReplay();
  }
  /** Out of the fall, back to the end card, with the finished position under it again. */
  function backToEnd() {
    const last = replay ? replay.positions[replay.positions.length - 1] : null;
    const side = replay ? replay.side : 'w';
    show('end');   // closes the review, which puts the referee and the board back at the start
    try { if (last) { board.pieces.setPosition(last); board.setSide(side, true); } } catch { /* the board stays as it is */ }
  }
  /** Save or copy the picture (door/fall-card.js) and say how it went, in one line. */
  function shareFall(how) {
    const g = lastEnd;
    if (!g) return;
    const say = (text) => { const n = screen === 'end' && card.querySelector('.fall-note'); if (n) n.textContent = text; };
    const job = how === 'copy' ? copyFallCard(g) : saveFallCard(g);
    Promise.resolve(job).then((ok) => {
      if (how === 'copy') say(ok ? 'Copied.' : 'Copy is not allowed here. Save it instead.');
      // the page only hands the file over; the host's own download tray says whether it landed
      else say(ok ? 'Sent to your downloads.' : 'Could not make the picture.');
    }).catch(() => say('Could not make the picture.'));
  }
  function paintRecap() {
    const slot = screen === 'end' && card.querySelector('#door-recap');
    if (slot) slot.innerHTML = recap();
  }
  /**
   * The grader (window.PBP.iq) works in a worker, so the last move's grade can
   * land after the game does. Wait for it (bounded), then keep the fall on the
   * saved game and paint it into the card in place. A game dealt meanwhile owns
   * the grader by then, and so does the menu (its reset starts the trackers
   * over), so only what the record already carried is kept.
   */
  function settleIq(saved, fromRecord) {
    const api = window.PBP && window.PBP.iq;
    let epoch = null, settled = null;
    try { epoch = api && typeof api.epoch === 'function' ? api.epoch() : null; } catch { epoch = null; }
    try { settled = api && typeof api.settled === 'function' ? api.settled(T.iqSettleMs) : null; } catch { settled = null; }
    Promise.race([Promise.resolve(settled), new Promise((r) => later(r, T.iqSettleMs + 500))]).catch(() => {}).then(() => {
      const iq = keptIq(api, epoch, fromRecord, lastEnd === saved && !current);
      if (!iq) return;
      saved.iq = iq;
      const { rematchOf, ...plain } = saved;   // the finished match object is not shelf material
      try { saveGame(plain); } catch { /* the shelf is optional */ }
      if (lastEnd === saved) paintRecap();
    });
  }

  function rematch() {
    const prev = lastEnd && lastEnd.rematchOf;
    if (prev && prev.mode === 'online' && prev.match) {
      show('lobby');
      if (lobby) look(afterHost(() => requestRematch(lobby, prev.match)));
    } else if (prev?.mode === 'solo') {
      setup = prev.options || setup;
      deal('solo');
    } else {
      deal('hotseat');
    }
  }

  function toMenu() {
    if (current?.mode === 'online' && !game.isOver()) return;
    current = null;
    stake.clear();
    board.anim?.skip?.();
    game.clock?.stop?.();
    // A finished online seat stays in the chair for the end card, so the
    // board under the card is the finished position. The menu wants the
    // hotseat's fresh one, and that only exists once the switch has gone
    // back; reset() on the online seat is a no-op and would leave the mate.
    try { if (typeof game.switchBack === 'function') game.switchBack(); } catch { /* one driver only */ }
    try { game.reset(); board.pieces.setPosition(game.rules.position()); } catch { /* the board stays as it is */ }
    try { board.setSide('w', true); } catch { /* no rig */ }
    show('menu');
  }

  // ---------------------------------------------------------------- the stands
  /**
   * Watch one public game (net/watch.js): ask the server first, so a game that is
   * not open to watch says so in the tables' note line instead of dealing a board.
   * A live game of our own is never left for it.
   */
  let watchSeq = 0;
  async function spectate(id) {
    if (!id) return;
    if (current && current.mode === 'online' && !game.isOver()) return;
    const mine = ++watchSeq;
    await askHost();
    if (mine !== watchSeq) return;
    if (signedOut) { note = 'sign in to watch'; if (screen === 'lobby') render(); return; }
    let res = null;
    try { res = await watchApi(String(id)); } catch { res = null; }
    if (mine !== watchSeq || (current && current.mode === 'online' && !game.isOver())) return;
    if (!res || !res.ok || !res.data || typeof res.data !== 'object') {
      const why = (res && res.error) || 'offline';
      note = WATCH_NOTES[why] !== undefined ? WATCH_NOTES[why] : 'that game cannot be watched';
      if (note) nope();
      if (screen === 'lobby') render(); else if (note) show('lobby');
      return;
    }
    if (current) toMenu();
    leaveLobby();
    deal('watch', { id: String(id), state: res.data });
  }

  /** Out of the stands, back to the tables (the menu with no lobby), with one line about why. */
  function leaveWatch(text = '') {
    const watching = (current && current.mode === 'watch') || !!game.isWatch;
    if (!watching) return;
    current = null;
    board.anim?.skip?.();
    try { if (typeof game.switchBack === 'function') game.switchBack(); } catch { /* one driver only */ }
    try { game.reset(); board.pieces.setPosition(game.rules.position()); } catch { /* the board stays as it is */ }
    try { board.setSide('w', true); } catch { /* no rig */ }
    show(lobby ? 'lobby' : 'menu');
    if (text) { note = text; if (screen === 'lobby') render(); }
  }
  /** The watch session said the game closed to us mid-way. */
  function watchClosed(p) {
    const why = p && p.reason;
    leaveWatch(why === 'not_found' ? 'that game ended' : (WATCH_NOTES[why] || ''));
  }

  // ---------------------------------------------------------------- replay
  function openReplay(id) {
    if (current?.mode === 'online' && !game.isOver()) return;
    const g = getGame(id);
    if (!g) return;
    if (current) toMenu();
    leaveLobby();
    try { replay = buildReplay(g); } catch { return; }
    replay.title = `${g.opponent || 'a friend here'} - ${fmtWhen(g.at)}`;
    board.anim?.setClock?.(null);
    show('replay');
    stepReplay(0);
  }
  function stepReplay(i, animate = false) {
    if (!replay) return;
    showReplayStep(board, replay, i, animate && !still());
    if (replay.i === replay.moves.length && replay.timer) stopReplay();
    if (replay.stopAt != null && replay.i >= replay.stopAt && replay.timer) { stopReplay(); replay.stopAt = null; }
    if (screen === 'replay') render();
  }
  function playReplay() {
    if (!replay || replay.timer) return;
    if (replay.i >= replay.moves.length) stepReplay(0);
    const tickFn = () => {
      if (!replay || !replay.timer) return;
      // the fall also waits out the director's replay of a capture, so the next move never cuts it
      const director = replay.fall ? board.director : null;
      if (board.anim?.busy?.() || director?.holding?.() || director?.active?.()) { replay.timer = later(tickFn, 150); return; }
      stepReplay(replay.i + 1, true);
      if (replay?.timer) replay.timer = later(tickFn, T.replayStepMs);
    };
    replay.timer = later(tickFn, 200);
    render();
  }
  function stopReplay() { if (replay && replay.timer) { clearTimeout(replay.timer); timers.delete(replay.timer); replay.timer = null; } }
  function closeReplay() {
    stopReplay();
    replay = null;
    try { board.director?.allowUnderDoor?.(false); } catch { /* an older director */ }
    board.anim?.setClock?.(() => game.clock);
    board.anim?.skip?.();
    game.switchBack?.();
    try { const m = board.drag && board.drag.markers; if (m && m.setLastMove) m.setLastMove(null, null); } catch { /* fine */ }
    try { game.reset(); board.pieces.setPosition(game.rules.position()); board.setSide('w', true); } catch { /* fine */ }
  }

  // ---------------------------------------------------------------- input
  function act(name, id) {
    switch (name) {
      // the lobby is asked only once the host has said who we are, and not at
      // all for nobody (afterHost); look() reads the quiet rejection as a re-render
      // every start below goes through gate(): the picture ask, once, while nothing is saved
      case 'quick': gate(() => { if (screen !== 'lobby') show('lobby'); if (lobby) look(afterHost(() => lobby.quickMatch()), 'quick'); }); break;
      case 'join': if (lobby && id) gate(() => look(afterHost(() => (typeof lobby.join === 'function' ? lobby.join(id) : lobby.challenge(id))), 'join', id)); break;
      case 'host': if (lobby && typeof lobby.host === 'function') gate(() => look(afterHost(() => lobby.host({ timeControl: { initial_ms: hostTc.initial_ms, increment_ms: hostTc.increment_ms } })), 'host')); break;
      case 'hosttc': { const t = HOST_TCS.find((c) => c.label === id); if (t && t !== hostTc) { hostTc = t; render(); picked('.tc-row .level-pill.on'); } break; }
      case 'solo': gate(() => deal('solo')); break;
      case 'level': if (LEVELS[id] && id !== setup.level) { setup = soloOptions({ ...setup, level: id }); paintLevel(); picked('.level-slot .level-pill.on'); } break;
      case 'setup': { const [key, v] = String(id || '').split(':'); if ((key === 'side' || key === 'clockMs') && String(setup[key]) !== v) { setup = soloOptions({ ...setup, [key]: v }); paintSetup(); picked(`.setup-slot [data-key="${key}"].on`); } break; }
      case 'back': goBack(); break;
      case 'continue': gate(() => { const saved = readSolo(); if (saved) deal('solo', null, saved); else render(); }); break;
      case 'hotseat': gate(() => deal('hotseat')); break;
      case 'lobby': show('lobby'); break;
      case 'games': show('games'); break;
      case 'profile': show('profile'); break;
      case 'ladder': ladderFrom = screen === 'lobby' ? 'lobby' : 'menu'; show('ladder'); Promise.all([ladder.loadTop(), freshMe()]).then(() => { if (screen === 'ladder') render(); }); break;
      case 'pictures': Promise.resolve(pictures.manage({ still: still() })).then(() => { if (screen === 'menu') render(); }).catch(() => {}); break;
      case 'challenge': if (lobby) gate(() => look(afterHost(() => lobby.challenge(id)))); break;
      case 'cancel': try { if (lobby) lobby.cancel(); } catch { /* fine */ } break;
      // accepting is a round trip on a server; the mock answers at once and
      // Promise.resolve makes both read the same. The ask comes first; backing
      // out of it declines, as ignoring the challenge would.
      case 'accept': if (ask) {
        const a = ask; ask = null; if (a.timer) clearTimeout(a.timer);
        gate(() => Promise.resolve(a.accept()).then((m) => { if (m) matched(m); }).catch(() => { nope(); if (screen) render(); }),
          () => { try { a.decline(); } catch { /* fine */ } if (screen) render(); });
      } break;
      case 'decline': if (ask) { try { ask.decline(); } catch { /* fine */ } if (ask.timer) clearTimeout(ask.timer); ask = null; render(); } break;
      case 'go': go(); break;
      case 'stake': { const [k, a] = String(id || '').split(':'); stake.choose(k, Number(a)); try { ui.select(); } catch { /* quiet */ } later(() => { const on = card.querySelector('.stake-pill.on'); if (on) kick(on, 'pop', { still: still() }); }, 30); break; }
      case 'watch': openReplay(id); break;
      case 'spectate': spectate(id); break;
      case 'rstart': stopReplay(); stepReplay(0); break;
      case 'rprev': stopReplay(); stepReplay(replay ? replay.i - 1 : 0); break;
      case 'rnext': stopReplay(); stepReplay(replay ? replay.i + 1 : 0, true); break;
      case 'rplay': if (replay && replay.timer) { stopReplay(); render(); } else playReplay(); break;
      case 'rematch': gate(rematch); break;
      case 'menu': toMenu(); break;
      case 'fallwatch': watchFall(); break;
      case 'fallsave': shareFall('save'); break;
      case 'fallcopy': shareFall('copy'); break;
      default: break;
    }
  }

  function onClick(e) {
    const b = e.target.closest('[data-act]');
    if (!b || !card.contains(b)) return;
    e.preventDefault();
    // a keyboard press (Enter, Space) has no pointerdown before it: its click is the press
    if (e.detail === 0 && !b.matches(CHIPS)) { try { ui.press(); } catch { /* quiet */ } }
    act(b.dataset.act, b.dataset.id);
  }

  /**
   * One step back: the Back button and Escape both land here. The menu's
   * step back is out of the board (boot's own Escape does the same there).
   */
  function goBack() {
    if (screen === null) return;
    if (screen === 'menu') { exitToHost(); return; }
    if (screen === 'found') { if (countTimer) { clearTimeout(countTimer); timers.delete(countTimer); countTimer = null; } current = null; stake.withdraw(); stake.clear(); show('lobby'); return; }
    if (screen === 'end') { toMenu(); return; }
    if (screen === 'replay') { if (replay && replay.fall) backToEnd(); else show('games'); return; }
    if (screen === 'ladder') { show(ladderFrom); return; }
    show('menu');
  }
  // Esc: back, then out. Capture phase so boot's own leave-on-Escape
  // only sees it from the menu; a countdown is not a place Esc can trap you either.
  function onKey(e) {
    if (e.key !== 'Escape' || screen === null) return;
    if (screen === 'menu') return;                  // boot posts pbp:exit
    if (pictures.isOpen && pictures.isOpen()) return;   // the picture card's own Escape closes it
    e.stopImmediatePropagation();
    e.preventDefault();
    try { ui.back(); } catch { /* quiet */ }
    goBack();
  }

  /**
   * The cues under the pointer: a soft one when it lands on something you
   * can press (once per button, not per pixel), the press as it goes down.
   * Chips speak with select() instead, Back with back() (wireBack).
   */
  const PRESSABLE = 'button, summary';
  const CHIPS = '.level-pill, .stake-pill, [data-back]';
  let hovered = null;
  function onOver(e) {
    const b = e.target && e.target.closest ? e.target.closest(PRESSABLE) : null;
    if (b === hovered) return;
    hovered = b;
    if (b && card.contains(b) && !b.disabled) { try { ui.hover(); } catch { /* quiet */ } }
  }
  function onDown(e) {
    const b = e.target && e.target.closest ? e.target.closest(PRESSABLE) : null;
    if (!b || !card.contains(b) || b.disabled || b.matches(CHIPS)) return;
    try { ui.press(); } catch { /* quiet */ }
  }
  /** The disclosure opening is a small screen of its own: its rows land, it sounds like a pick. */
  function onToggle(e) {
    const d = e.target;
    if (!d || !d.classList || !d.classList.contains('door-setup')) return;
    try { ui.select(); } catch { /* quiet */ }
    if (d.open) { const s = d.querySelector('.door-settings'); if (s) screenIn(s, { still: still() }); }
  }
  // a tap on the board during the countdown means "now"
  function onStageTap() { if (screen === 'found') go(); }

  const unwireBack = wireBack(card, () => goBack());
  card.addEventListener('click', onClick);
  card.addEventListener('pointerover', onOver);
  card.addEventListener('pointerdown', onDown);
  card.addEventListener('toggle', onToggle, true);
  window.addEventListener('keydown', onKey, { capture: true });
  veil.addEventListener('pointerdown', onStageTap);
  if (bus) {
    unbind.push(bus.on('menu-request', toMenu));
    unbind.push(bus.on('capture', onCapture));
    unbind.push(bus.on('gameover', onGameOver));
    unbind.push(bus.on('watch-leave', () => leaveWatch()));
    unbind.push(bus.on('watch-closed', watchClosed));
  }

  // ---------------------------------------------------------------- friends drawer
  /**
   * An intent from the desktop's friends drawer (host frame `pbp:friend`):
   *   { mode: 'challenge', friendId }   challenge that friend; the challenge id goes back to
   *                                     the host (`pbp:friend-challenge`) so the drawer can send
   *                                     it as the invite, and the lobby waits for the yes
   *   { mode: 'accept', challengeId }   the friend's side: take it up, straight to the board
   *   { mode: 'join', target }          the desktop Lobby: sit at that open table (a p_ id)
   *   { mode: 'host' }                  the desktop Lobby: list a table and wait
   *   { mode: 'spectate', matchId }     the desktop Lobby: watch that public game (ten seconds behind)
   * A live online game is never interrupted; the host hears null and says so.
   */
  function friend(intent) {
    const m = intent || {};
    const tell = (challengeId) => { try { postToHost({ type: 'pbp:friend-challenge', friendId: m.friendId || null, challengeId: challengeId || null }); } catch { /* no host */ } };
    const busy = current && current.mode === 'online' && !game.isOver();
    if (!busy && m.mode === 'spectate' && m.matchId) { spectate(String(m.matchId)); return; }
    if (busy || !lobby) { if (m.mode === 'challenge') tell(null); return; }
    // a friend's game is a start too: the picture ask comes first while nothing is saved
    if (m.mode === 'challenge' && m.friendId) {
      gate(() => {
        if (screen !== 'lobby') show('lobby');
        let told = false;
        const said = (id) => { if (!told) { told = true; tell(id); } };
        look(afterHost(() => lobby.challenge(String(m.friendId), { onChallengeId: said }))
          .catch((err) => { said(null); throw err; }));
      }, () => tell(null));
      return;
    }
    // The desktop Lobby: sit at one open table, or list ours. The same buttons the lobby screen has.
    if (m.mode === 'join' && m.target) { if (screen !== 'lobby') show('lobby'); act('join', String(m.target)); return; }
    if (m.mode === 'host') { if (screen !== 'lobby') show('lobby'); act('host'); return; }
    if (m.mode === 'accept' && m.challengeId && typeof lobby.acceptChallenge === 'function') {
      gate(() => {
        if (screen !== 'lobby') show('lobby');
        look(afterHost(() => lobby.acceptChallenge(String(m.challengeId))));
      });
    }
  }

  return {
    show,
    hide,
    friend,
    isUp: () => screen !== null,
    screen: () => screen,
    /** For the harness: the door's state, and levers to pull. */
    debug: {
      state: () => ({ screen, looking, lookKind, note, ask: ask ? ask.name : null, people: people.length, playing: playingNow.length, current: current ? current.mode : null, replay: replay ? replay.i : null, games: listGames().length, signedOut, hosted: !!host.isHosted, inLobby: !!offList }),
      act,
      openReplay,
      matched,
      spectate,
      leaveWatch,
      stake,
      shelf: { save: saveGame, list: listGames },
      ladder,
      /** Harness: an online end card with a rating beat, without a server. */
      fakeEnd(g, rating) {
        lastEnd = Object.assign({ mode: 'online', me: 'w', opponent: 'Rose', moves: [], plies: 30, durationMs: 300000, result: { result: 'checkmate', winner: 'w', reason: 'checkmate' } }, g || {});
        endRating = { plan: endPlan(rating || {}), played: false };
        show('end');
      },
      /**
       * Stand in for the desktop host: { isHosted, identity(), whenIdentity() },
       * any subset. What lets a plain browser photograph the hosted states
       * (a read-only name, a signed-out lobby) that only a host can put it in.
       */
      host(fake) {
        host = Object.assign({}, host, fake || {});
        signedOut = false;
        if (offList) leaveLobby();
        if (screen) render();
      },
    },
    dispose() {
      card.removeEventListener('click', onClick);
      card.removeEventListener('pointerover', onOver);
      card.removeEventListener('pointerdown', onDown);
      card.removeEventListener('toggle', onToggle, true);
      unwireBack();
      window.removeEventListener('keydown', onKey, { capture: true });
      veil.removeEventListener('pointerdown', onStageTap);
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      for (const t of timers) clearTimeout(t);
      leaveLobby();
      try { stake.dispose(); } catch { /* gone */ }
      if (stakeChip) stakeChip.remove();
    },
  };
}
