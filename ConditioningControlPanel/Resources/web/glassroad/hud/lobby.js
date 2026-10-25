/* hud/lobby.js - the online lobby, drawn in the #card (hud/cards.js kind 'lobby'). NET.md sections 4, 8, 10, 12, 13.
 *
 * Views: home (open tables, quick match, host a table, friends room), host (the table form), look (outfit and
 * vehicle from Astra's wardrobe and vehicle files), quick (waiting), room (8 seats, ready, start, preset chat,
 * invite, Invite link and Revoke for a friends room host, leave, the server-time countdown), invite (friends), results
 * (times and pops), msg (an error or abort), link (a race link claim: waiting for the host, or why it was refused).
 * Pure logic lives in hud/lobbyMath.js and hud/presets.js. Keyboard and pad come through hud/cards.js: up and down
 * move focus, left and right step a form row, Escape and pad back go up one view (in a room it only moves focus to
 * Leave: leaving is always a deliberate press). No free text anywhere; names come from tickets, uids never reach the
 * DOM. Gentle (the card's c-gentle class) drops every pulse; bubbles still fade in and out.
 *
 * Dependencies (configureLobby, once at boot, or lobby.use):
 *   api: lobbyApi (B2-c): list() -> { rows, serverMs }; open(body); join(roomId, look); quick({ track, look })
 *        -> join | { waiting, retryMs }; invite(friendUid, roomId); leave(roomId). A join is { roomId, ticket,
 *        wsUrl, seat, expiresAt }; an error is a thrown Error with .code or a resolved { error }.
 *   connect(join): -> room (or a promise of one): room.on(t, fn) for the R2C message names plus 'close'
 *        ({ code }), room.send(msg), room.close(), room.now() (server ms).
 *   identity: { displayName, tier } or a function returning it. friends?: () -> [{ uid, name }].
 *   track: 'threshold'. storage: localStorage-like. looks?: () -> { wardrobe, vehicles } (default: fetch the files).
 *   link (section 13): api.makeLink(), revokeLink(), claimLink(token, look, platform), access(); platform: 'web' | ...;
 *        linkClaimed(): after the first claim answer (boot drops ?r= from the address bar).
 * Events (through the hud's emit): online-start, online-race (at countAt), online-seat, online-emote,
 * online-finish, online-left, online-abort, online-closed.
 */
import {
  hostGate, signedIn, tableRows, quickDelay, cleanHost, stepHost, hostBody, HOST_DEFAULTS, applySeats, applySeat,
  seatRows, raceTags, readyCount, humans, canStart, lookChoices, cycleLook, lookLabel, loadLook, saveLook, countdown,
  serverNow, resultLines, popsLabel, errorCopy, abortCopy, closeCopy, trackName, startsLabel, rowStartsIn, BADGE_COPY,
  linkButton, linkNote, claimStep, pollNow, pollStep, pollReset, listKey,
} from './lobbyMath.js';
import { PRESETS, presetOf, poseOf, poseHoldS, emoteAllowed, EMOTE_GAP_MS, EMOTE_SHOW_MS } from './presets.js';
import { normLook } from '../net/protocol.js';
import { actSound, seatSound, nextGridBell } from './sounds.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const btn = (act, label, cls = '', extra = '') => '<button type="button" class="c-btn ' + cls + '" data-act="' + act + '"' + extra + '>' + label + '</button>';

let shared = {};
/** Boot wires the lobby once: api, connect, identity, friends, track, storage. */
export function configureLobby(deps) { shared = { ...shared, ...(deps || {}) }; }

const CSS = [
  '#card[data-kind="lobby"] .card-body { width: min(440px, calc(100vw - 32px)); text-align: left; }',
  '#card[data-kind="lobby"] .card-title { font-size: 24px; }',
  '.lb-tables, .lb-seats { list-style: none; margin: 0 0 12px; padding: 0; display: flex; flex-direction: column; gap: 6px; }',
  '#card .lb-row { width: 100%; display: grid; grid-template-columns: 1fr auto; gap: 2px 10px; text-align: left; padding: 8px 10px; border-radius: 6px; }',
  '.lb-row b { font-weight: 800; } .lb-row small { grid-column: 1 / -1; font: 12px/1.2 var(--mono); color: var(--paper-dim); }',
  '.lb-row .n { font: 700 13px var(--mono); font-variant-numeric: tabular-nums; }',
  '.lb-seats li { position: relative; display: grid; grid-template-columns: 22px 1fr auto; gap: 8px; align-items: center; padding: 3px 8px; font-size: 14px; border-bottom: 1px solid rgba(28, 23, 34, 0.12); }',
  '.lb-seats li.me { background: rgba(212, 48, 124, 0.1); box-shadow: inset 3px 0 0 var(--paper-pink); font-weight: 700; }',
  '.lb-seats li.empty { color: var(--paper-dim); font-style: italic; }',
  '.lb-seats .p { font: 800 13px var(--mono); } .lb-bs { display: flex; gap: 4px; }',
  '.lb-b { padding: 0 5px; border: 1.5px solid var(--paper-ink); border-radius: 3px; font: 700 10px/1.5 var(--mono); text-transform: uppercase; letter-spacing: 0.08em; }',
  '.lb-b.ready { background: var(--paper-ink); color: var(--paper); } .lb-b.paused, .lb-b.away { border-color: var(--paper-pink); color: var(--paper-pink); }',
  '.lb-b.left, .lb-b.bot { opacity: 0.6; }',
  '.lb-bub { position: absolute; left: 42%; top: -8px; padding: 2px 8px; background: var(--paper-pink); color: #fff; border-radius: 10px; font: 700 12px var(--font);',
  '  animation: lb-bub 3s ease-in-out both; pointer-events: none; }',
  '@keyframes lb-bub { 0% { opacity: 0; transform: translateY(4px) scale(0.9); } 10%, 85% { opacity: 1; transform: none; } 100% { opacity: 0; transform: translateY(-4px); } }',
  '#card.c-gentle .lb-bub { animation-name: lb-bub-soft; } @keyframes lb-bub-soft { 0% { opacity: 0; } 15%, 80% { opacity: 1; } 100% { opacity: 0; } }',
  '.lb-chat { display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; margin: 10px 0; }',
  '#card .lb-chat button { padding: 5px 2px; min-height: 34px; font-size: 12px; }',
  '.lb-count { font: 800 44px/1 var(--mono); text-align: center; margin: 6px 0 12px; font-variant-numeric: tabular-nums; }',
  '.lb-count.pop { animation: lb-pop 0.32s ease-out; } @keyframes lb-pop { from { transform: scale(1.18); } to { transform: none; } }',
  '#card.c-gentle .lb-count.pop { animation: none; }',
  '#card .lb-locked { position: relative; color: var(--paper-dim); border-style: dashed; }',
  '.lb-hostrow { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin: 4px 0 10px; }',
  '.lb-head { margin: 10px 0 6px; font: 700 11px var(--mono); letter-spacing: 0.12em; text-transform: uppercase; color: var(--paper-dim); }',
  '.lb-res .k { font: 12px var(--mono); color: var(--paper-dim); }',
  '#card .lb-url { margin: 0 0 8px; padding: 6px 8px; font: 12px var(--mono); word-break: break-all; user-select: all; -webkit-user-select: all; border: 1.5px dashed var(--paper-ink); border-radius: 4px; }',
  '.c-rows.lb-res li { grid-template-columns: 26px 1fr auto auto; }',
  '@media (prefers-reduced-motion: reduce) { .lb-bub { animation-name: lb-bub-soft; } .lb-count.pop { animation: none; } }',
].join('\n');

/**
 * @param {{ doc: Document, card: HTMLElement, title: HTMLElement, note: HTMLElement, extra: HTMLElement,
 *   emit: (name: string, payload?: object) => void, focusFirst: () => void, exit: () => void }} parts
 */
export function createLobby({ doc = globalThis.document, card, title, note, extra, emit, focusFirst, exit }) {
  const win = doc.defaultView || globalThis;
  let disposed = false, handedOff = false, transferredRoomId = null;
  const alive = () => !disposed && !handedOff, deliver = emit;
  emit = (...args) => { if (alive()) deliver(...args); };
  let local = {};
  const deps = () => ({ ...shared, ...local });
  const ident = () => { const i = deps().identity; return typeof i === 'function' ? i() : i || null; };
  const storage = () => { const d = deps(); if (d.storage) return d.storage; try { return win.localStorage; } catch (e) { return null; } };
  const track = () => deps().track || 'threshold';

  const st = {
    view: 'home', msg: '', busy: false,
    rows: [], listAt: -1e9, listMs: 0, listSeen: 0, poll: { key: null, same: 0 },
    host: cleanHost({ ...HOST_DEFAULTS, laps: deps().trackLaps || HOST_DEFAULTS.laps }), look: loadLook(storage()), choices: lookChoices(), looksAsked: false,
    join: null, room: null, offs: [], welcome: null, seenAt: 0, seat: -1, phase: 'lobby', cfg: null, seed: 0, keep: [],
    seats: applySeats([]), start: null, raced: false, results: null, leaving: false,
    lastEmote: -Infinity, myPose: null, bubbles: {}, friends: null, sent: new Set(),
    quickT: 0, quickOn: false, autoAt: 0, gen: 0,
    // the race link (NET.md section 13): preview refusal, access().host, our live link, a claim in flight
    preview: false, access: null, accessAsked: false, link: null, linkCopied: false, linkMsg: '', linkBusy: false, claim: null,
  };
  let ticker = 0, lastCount = '', bellT = 0;
  // the tab coming back is a user action: the poll gap drops to 15 s again
  const onVis = () => { if (alive() && !doc.hidden) st.poll = pollReset(st.poll); };
  try { doc.addEventListener?.('visibilitychange', onVis); } catch (e) { /* a page without the event polls as before */ }
  // polish 5 (S4): menu sounds through the hud's `sound` event (hud/sounds.js); refusals say no
  const say = (name) => { if (name) emit('sound', { name }); };

  function injectCss() {
    if (doc.getElementById('gr-lobby-css')) return;
    const s = doc.createElement('style'); s.id = 'gr-lobby-css'; s.textContent = CSS;
    (doc.head || doc.body).appendChild(s);
  }
  const shown = () => alive() && !card.hidden && card.dataset.kind === 'lobby';
  const localNow = () => Date.now();
  const srv = () => serverNow(localNow(), st.room, st.welcome, st.seenAt);
  const listNow = () => st.listMs + (localNow() - st.listSeen);
  const gentle = () => card.classList.contains('c-gentle');

  // ---- drawing -------------------------------------------------------------------------------

  function paint() {
    if (!shown()) return;
    const had = card.contains(doc.activeElement) && doc.activeElement.dataset ? doc.activeElement.dataset : null;
    const key = had ? had.act + '|' + (had.id || had.key || '') : '';
    const v = VIEWS[st.view] || VIEWS.home;
    v();
    lastCount = '';
    paintCount();
    if (key) {
      const [a, id] = key.split('|');
      const el = [...extra.querySelectorAll('[data-act="' + a + '"]')].find((e) => (e.dataset.id || e.dataset.key || '') === id);
      if (el && !el.disabled) { el.focus?.({ preventScroll: true }); return; }
    }
    focusFirst();
  }
  const setNote = (t) => { note.hidden = !t; note.textContent = t || ''; };

  const lookRows = () => ['outfit', 'vehicle'].map((k) => '<div class="c-opt"><span>' + (k === 'outfit' ? 'Outfit' : 'Ride') +
    '</span>' + btn('lb-lstep', esc(lookLabel(st.look, k, st.choices)), 'c-tog', ' data-key="' + k + '" aria-label="' + (k === 'outfit' ? 'Outfit' : 'Ride') + '"') + '</div>').join('');

  const VIEWS = {
    home() {
      title.textContent = 'Race online';
      const who = ident(), gate = hostGate(who);
      if (!deps().api) { setNote(errorCopy('off')); extra.innerHTML = '<div class="c-list">' + btn('lb-exit', 'Back', 'quiet') + '</div>'; return; }
      if (!signedIn(who)) { setNote('Sign in to race online'); extra.innerHTML = '<div class="c-list">' + btn('lb-exit', 'Back', 'quiet') + '</div>'; return; }
      if (st.preview) { setNote(errorCopy('preview')); extra.innerHTML = '<div class="c-list">' + btn('lb-exit', 'Back', 'quiet') + '</div>'; return; }
      const now = listNow();
      setNote(st.msg || (st.rows.length ? st.rows.length + ' open' + (st.poll.same < 2 ? ', refreshed every 15 s' : ', checked less often while quiet') : st.listAt > 0 ? 'No open tables. Host one, or quick match' : 'Looking'));
      const rows = st.rows.map((r) => '<li>' + btn('lb-join', '<b>' + esc(r.host) + (r.friend ? ' (friend)' : '') + '</b><span class="n">' + r.seatsLabel +
        '</span><small data-starts="' + r.roomId + '">' + esc(r.track) + ', ' + r.laps + ' laps, ' + esc(startsLabel(rowStartsIn(r.raw, st.listMs, now))) + '</small>',
        'lb-row', ' data-id="' + r.roomId + '"' + (r.full ? ' disabled' : '')) + '</li>').join('');
      extra.innerHTML = '<div class="c-list">' + btn('lb-quick', 'Quick match', 'hot') + '</div>' +
        (rows ? '<div class="lb-head">Open tables</div><ol class="lb-tables">' + rows + '</ol>' : '') +
        '<div class="lb-hostrow">' +
          btn('lb-host', 'Host a table' + (gate.open === 'locked' ? '<span class="c-stamp">Prime</span>' : ''), gate.open === 'locked' ? 'lb-locked' : '',
            ' data-id="open"' + (gate.open === 'locked' ? ' aria-disabled="true"' : '')) +
          btn('lb-host', 'Friends room', '', ' data-id="friends"') + '</div>' +
        '<div class="c-links">' + btn('lb-look', 'Your look', 'c-link') + btn('lb-refresh', 'Refresh', 'c-link') + btn('lb-exit', 'Back', 'c-link') + '</div>';
    },
    host() {
      const f = st.host, open = f.mode === 'open';
      title.textContent = open ? 'Host a table' : 'Friends room';
      setNote(st.msg || (open ? 'Anyone can join. Starts by itself' : 'Friends you invite. You start it'));
      const row = (key, label, value, hint, dis) => '<div class="c-opt"><span>' + label + (hint ? '<small>' + hint + '</small>' : '') + '</span>' +
        btn('lb-hstep', value, 'c-tog', ' data-key="' + key + '" aria-label="' + label + '"' + (dis ? ' disabled' : '')) + '</div>';
      extra.innerHTML = '<div class="c-opts">' + row('laps', 'Laps', String(f.laps)) + row('seats', 'Seats', String(f.seats)) +
        row('botFill', 'Bots fill', f.botFill ? 'On' : 'Off', 'Empty seats race as bots') +
        row('laneKeeper', 'Lane keeper', f.laneKeeper ? 'On' : 'Off', open ? 'Off on open tables' : 'Steer assist allowed', open) + '</div>' +
        '<div class="c-list row">' + btn('lb-open', 'Open it', 'hot') + btn('lb-back', 'Back') + '</div>';
    },
    look() {
      title.textContent = 'Your look';
      setNote('Others see this on your kart');
      if (!st.looksAsked) loadLooks();
      extra.innerHTML = '<div class="c-opts">' + lookRows() + '</div><div class="c-list row">' + btn('lb-back', 'Done', 'hot') + '</div>';
    },
    quick() {
      title.textContent = 'Quick match';
      setNote(st.msg || 'Looking for a table');
      extra.innerHTML = '<div class="c-list">' + btn('lb-back', 'Cancel', 'quiet') + '</div>';
    },
    joining() {
      title.textContent = 'Joining';
      setNote(st.msg || 'Taking a seat');
      extra.innerHTML = '<div class="c-list">' + btn('lb-leave', 'Cancel', 'quiet') + '</div>';
    },
    room() {
      const cfg = st.cfg || {}, me = st.seats[st.seat] || {};
      title.textContent = trackName(cfg.track) + ', ' + (cfg.laps || 3) + ' laps';
      const hs = humans(st.seats).length;
      setNote(st.msg || st.linkMsg || (st.phase === 'lobby' ? (cfg.mode === 'friends' ? 'Friends room' : 'Open table') + ', ' + readyCount(st.seats) + ' of ' + hs + ' ready'
        : st.phase === 'done' ? 'Race over' : 'Racing'));
      if (cfg.mode === 'friends' && me.host && !st.accessAsked) askAccess();
      const linkOn = linkButton(cfg, me, st.access, st.phase);
      const now = localNow();
      const seats = seatRows(st.seats, st.seat, st.phase, cfg.seats || 8).map((r) => {
        const b = st.bubbles[r.seat], p = b && b.until > now ? presetOf(b.code) : null;
        return '<li class="' + (r.me ? 'me ' : '') + r.kind + '"><span class="p">' + r.n + '</span><span class="n">' + esc(r.me ? r.name + ' (you)' : r.name) +
          '</span><span class="lb-bs">' + r.badges.map((x) => '<i class="lb-b ' + x + '">' + BADGE_COPY[x] + '</i>').join('') + '</span>' +
          (p ? '<span class="lb-bub">' + esc(p.text) + '</span>' : '') + '</li>';
      }).join('');
      const lobby = st.phase === 'lobby', counting = !!st.start && st.phase !== 'done';
      const chatOff = !emoteAllowed(st.lastEmote, now);
      extra.innerHTML = (counting ? '<div class="lb-count" aria-live="polite"></div>' : lobby && st.autoAt ? '<p class="lb-head lb-auto"></p>' : '') +
        '<ol class="lb-seats">' + seats + '</ol>' +
        (lobby ? '<div class="c-opts">' + lookRows() + '</div><div class="c-list row">' +
          btn('lb-ready', me.ready ? 'Ready' : 'Not ready', 'c-tog' + (me.ready ? ' on' : ''), ' aria-pressed="' + !!me.ready + '"') +
          (me.host ? btn('lb-start', 'Start', 'hot', canStart(st.seats, st.seat, st.phase) ? '' : ' disabled') : '') + '</div>' : '') +
        '<div class="lb-chat" role="group" aria-label="Say">' + PRESETS.map((p) => btn('lb-emote', esc(p.text), '', ' data-id="' + p.code + '"' + (chatOff ? ' disabled' : ''))).join('') + '</div>' +
        (linkOn && st.link && !st.linkCopied ? '<p class="lb-url" aria-label="Race link">' + esc(st.link.url) + '</p>' : '') +
        '<div class="c-links">' + (lobby && deps().friends && st.join ? btn('lb-invite', 'Invite a friend', 'c-link') : '') +
        (linkOn ? btn('lb-link', 'Invite link', 'c-link', st.linkBusy ? ' disabled' : '') + (st.link ? btn('lb-unlink', 'Revoke', 'c-link') : '') : '') +
        btn('lb-leave', 'Leave', 'c-link') + '</div>';
    },
    link() {
      title.textContent = 'Race a friend';
      setNote(st.msg || 'Checking the link');
      extra.innerHTML = '<div class="c-list row">' + (st.claim ? btn('lb-back', 'Cancel', 'quiet') : btn('lb-home', 'Tables', 'hot') + btn('lb-exit', 'Menu')) + '</div>';
    },
    invite() {
      title.textContent = 'Invite a friend';
      const list = st.friends;
      setNote(st.msg || (!list ? 'Loading friends' : list.length ? 'They get a ping in CCP' : 'No friends to ask yet'));
      extra.innerHTML = '<ol class="lb-tables">' + (list || []).map((f, i) => '<li>' + btn('lb-send', '<b>' + esc(f.name) + '</b><span class="n">' +
        (st.sent.has(i) ? 'Sent' : 'Ask') + '</span>', 'lb-row', ' data-id="' + i + '"' + (st.sent.has(i) ? ' disabled' : '')) + '</li>').join('') + '</ol>' +
        '<div class="c-list row">' + btn('lb-back', 'Back', 'hot') + '</div>';
    },
    results() {
      const lines = resultLines(st.results, st.seat), mine = lines.find((l) => l.me);
      title.textContent = mine ? mine.place + mine.suf : 'Results';
      setNote(mine ? (mine.time === 'no time' ? 'Did not finish' : mine.time) + ', ' + popsLabel(mine.pops) : 'Race over');
      extra.innerHTML = '<ol class="c-rows lb-res">' + lines.map((l) => '<li class="' + (l.me ? 'me' : '') + '"><span class="p">' + l.place +
        '</span><span class="n">' + esc(l.name) + '</span><span class="k">' + popsLabel(l.pops) + '</span><span class="t">' + l.time + '</span></li>').join('') +
        '</ol><div class="c-list row">' + btn('lb-again', 'Again', 'hot') + btn('lb-home', 'Tables') + btn('lb-exit', 'Menu') + '</div>';
    },
    msg() {
      title.textContent = 'Race online';
      setNote(st.msg || 'The room closed');
      extra.innerHTML = '<div class="c-list row">' + btn('lb-home', 'Tables', 'hot') + btn('lb-exit', 'Menu') + '</div>';
    },
  };

  /** The countdown and auto-start lines, updated in place (never a re-render, so focus stays put). */
  function paintCount() {
    if (!shown()) return;
    const c = extra.querySelector('.lb-count');
    if (c && st.start) {
      const cd = countdown(srv(), st.start);
      if (cd.label !== lastCount) {
        lastCount = cd.label; c.textContent = cd.label;
        if (!gentle()) { c.classList.remove('pop'); void c.offsetWidth; c.classList.add('pop'); }
      }
    }
    const a = extra.querySelector('.lb-auto');
    if (a && st.autoAt) a.textContent = startsLabel(Math.max(0, Math.ceil((st.autoAt - srv()) / 1000)));
    if (st.view === 'home') {
      const now = listNow();
      for (const r of st.rows) {
        const sm = extra.querySelector('[data-starts="' + r.roomId + '"]');
        if (sm) sm.textContent = r.track + ', ' + r.laps + ' laps, ' + startsLabel(rowStartsIn(r.raw, st.listMs, now));
      }
    }
  }

  // ---- the clock -----------------------------------------------------------------------------

  function tick() {
    const now = localNow();
    // the list poll stops while the page is hidden and backs off while answers stay the same (lobbyMath pollNow)
    if (shown() && st.view === 'home' && !st.busy && !st.preview && signedIn(ident()) && deps().api
      && pollNow({ hidden: !!doc.hidden, lastAt: st.listAt, now, poll: st.poll })) refresh();
    if (st.start && !st.raced && st.phase !== 'done' && srv() >= st.start.countAt) { st.raced = true; emit('online-race', ctx()); }
    let expired = false;
    for (const k of Object.keys(st.bubbles)) if (st.bubbles[k].until <= now) { delete st.bubbles[k]; expired = true; }
    if (st.myPose && st.myPose.until <= now) st.myPose = null;
    const chatBack = st.view === 'room' && st.chatOff && emoteAllowed(st.lastEmote, now);
    if (chatBack) st.chatOff = false;
    if ((expired || chatBack) && st.view === 'room') paint();
    else paintCount();
    if (!shown() && !st.room && !st.quickOn) stopTicker();
  }
  const startTicker = () => { if (alive() && !ticker) ticker = win.setInterval(tick, 250); };
  function stopTicker() { if (ticker) { win.clearInterval(ticker); ticker = 0; } }

  // ---- lobby calls ---------------------------------------------------------------------------

  /** One lobby call: resolves the body, or null with st.msg set. */
  async function call(fn) {
    try {
      const r = await fn();
      if (r && r.error) { st.preview = r.error === 'preview'; st.msg = errorCopy(r.error); return null; }
      return r;
    } catch (e) { st.preview = !!e && e.code === 'preview'; st.msg = errorCopy((e && e.code) || 'net'); return null; }
  }

  // ---- the race link (NET.md section 13) -----------------------------------------------------

  /** May we mint a link? Asked once, the first time we host a friends room. */
  async function askAccess() {
    const api = deps().api;
    st.accessAsked = true;
    if (!api || !api.access) return;
    try { st.access = (await api.access()) || null; } catch (e) { st.access = null; }
    if (st.view === 'room') paint();
  }
  /** Invite link: mint (or reuse) the link, copy its url, say how many seats and how long. */
  async function makeLink() {
    const api = deps().api;
    st.linkBusy = true; paint();
    const r = await call(() => api.makeLink());
    if (!alive()) return;
    st.linkBusy = false;
    if (!r || typeof r.url !== 'string') { st.linkMsg = st.msg; st.msg = ''; if (st.view === 'room') paint(); return; }
    let copied = false;
    try { await win.navigator.clipboard.writeText(r.url); copied = true; } catch (e) { copied = false; }
    st.link = r; st.linkCopied = copied; st.linkMsg = linkNote(r, srv(), copied);
    if (st.view === 'room') paint();
  }
  async function revokeLink() {
    const r = await call(() => deps().api.revokeLink());
    if (r) { st.link = null; st.linkCopied = false; st.linkMsg = 'Link revoked.'; } else { st.linkMsg = st.msg; st.msg = ''; }
    if (st.view === 'room') paint();
  }
  /** Claim a link (boot's ?r=): seated -> the room; waiting -> poll on retryMs until seated or cancelled; refused -> its card. */
  async function claimLink() {
    const c = st.claim, api = deps().api;
    if (!c) return;
    let r;
    try { r = await api.claimLink(c.token, normLook(st.look), deps().platform); } catch (e) { r = { error: (e && e.code) || 'net' }; }
    if (st.claim !== c) { if (r && r.roomId) late(r); return; }
    if (!c.said) { c.said = true; try { deps().linkClaimed?.(); } catch (e) { /* the address bar stays */ } }
    const s = claimStep(r);
    if (s.kind === 'seat') { st.claim = null; seat(s.join, null); return; }
    st.msg = s.msg;
    if (s.kind === 'wait') { c.t = win.setTimeout(claimLink, s.delayMs); if (st.view === 'link') paint(); return; }
    st.claim = null; say('menu-deny'); st.view = 'link'; paint();
  }
  function startClaim(token) {
    stopClaim();
    if (!deps().api || !deps().api.claimLink) { st.msg = errorCopy('off'); st.view = 'link'; return; }
    st.claim = { token, t: 0, said: false }; st.msg = ''; st.view = 'link';
    claimLink();
  }
  function stopClaim() { const c = st.claim; st.claim = null; if (c && c.t) win.clearTimeout(c.t); }

  async function refresh() {
    const api = deps().api;
    if (!api || st.busy) return;
    st.listAt = localNow();
    const r = await call(() => api.list());
    if (!alive()) return;
    if (!r) { paint(); return; }
    st.listMs = Number(r.serverMs) || localNow(); st.listSeen = localNow();
    st.poll = pollStep(st.poll, listKey(r));
    const raw = new Map((r.rows || []).map((x) => [x && x.roomId, x]));
    st.rows = tableRows(r, st.listMs).filter(row => raw.get(row.roomId)?.track === track()).map((x) => ({ ...x, raw: raw.get(x.roomId) }));
    if (st.view === 'home') { st.msg = ''; paint(); }
  }

  async function loadLooks() {
    st.looksAsked = true;
    try {
      const d = deps();
      let w = null, v = null;
      if (d.looks) ({ wardrobe: w, vehicles: v } = (await d.looks()) || {});
      else if (typeof win.fetch === 'function') {
        const get = async (f) => { const res = await win.fetch(new URL('../art/models/' + f, import.meta.url)); return res.ok ? res.json() : null; };
        [w, v] = await Promise.all([get('emi-bot-wardrobe.json'), get('emi-vehicle-variants.json')]);
      }
      st.choices = lookChoices(w, v);
    } catch (e) { st.choices = lookChoices(); }
    if (st.view === 'look' || st.view === 'room') paint();
  }

  async function quick() {
    const api = deps().api;
    if (!api) return;
    st.quickOn = true; st.view = 'quick'; st.msg = ''; paint(); startTicker();
    const r = await call(() => api.quick({ track: track(), look: normLook(st.look) }));
    if (!st.quickOn) { if (r && !r.waiting) late(r); return; }
    if (!r) { st.quickOn = false; st.view = 'home'; paint(); return; }
    if (r.waiting) { st.quickT = win.setTimeout(quick, quickDelay(r.retryMs)); return; }
    st.quickOn = false;
    seat(r, null);
  }
  function stopQuick() { st.quickOn = false; if (st.quickT) { win.clearTimeout(st.quickT); st.quickT = 0; } }

  async function seat(join, row) {
    if (!alive()) { late(join); return; }
    if (!join || typeof join.roomId !== 'string') { st.msg = errorCopy('bad'); st.view = 'home'; paint(); return; }
    st.join = join; st.view = 'joining'; st.msg = ''; st.results = null; st.start = null; st.raced = false; st.phase = 'lobby';
    st.sent = new Set(); st.friends = null; st.leaving = false;
    st.autoAt = row && row.startsIn != null ? st.listMs + row.startsIn * 1000 : 0;
    paint(); startTicker();
    const connect = deps().connect;
    if (typeof connect !== 'function') { st.msg = errorCopy('off'); st.view = 'msg'; st.join = null; paint(); return; }
    let room;
    try { room = await connect({ ...join, look: normLook(st.look) }); } catch (e) { room = null; }
    if (!room || st.join !== join) { if (room && st.join !== join) room.close?.(); if (st.join === join) { st.msg = errorCopy('net'); st.view = 'msg'; st.join = null; paint(); } return; }
    st.room = room;
    const on = (t, fn) => { const off = room.on(t, m => { if(alive() && st.room===room)fn(m); }); if (typeof off === 'function') st.offs.push(off); };
    on('welcome', onWelcome); on('seats', (m) => { const was = st.seats; st.seats = applySeats(m.seats, st.seats); seatSay(was); seatsChanged(); });
    on('seat', (m) => { const was = st.seats; st.seats = applySeat(st.seats, m); seatSay(was); seatsChanged(m.seat); });
    on('start', onStart); on('emote', onEmote); on('finish', onFinish); on('abort', onAbort); on('error', onError); on('close', onClose);
  }

  function ctx() {
    return { room: st.room, roomId: st.join && st.join.roomId, seat: st.seat, seats: st.seats, cfg: st.cfg, seed: st.seed, keep: st.keep,
      start: st.start, welcome: st.welcome, look: normLook(st.look), tags: raceTags(st.seats) };
  }

  function onWelcome(m) {
    st.welcome = m; st.seenAt = localNow(); st.seat = m.seat; st.phase = m.phase; st.cfg = m.cfg; st.seed = m.seed; st.keep = m.keep || [];
    st.seats = applySeats(m.seats, null);
    if (m.countAt > 0 && m.startAt > 0) { st.start = { countAt: m.countAt, startAt: m.startAt, goTick: m.goTick, seed: m.seed }; armBell(); }
    st.view = 'room'; st.msg = ''; paint();
    if (m.phase === 'countdown' || m.phase === 'racing') {
      emit('online-start', { ...ctx(), resume: m.resume || null });
      if (srv() >= st.start.countAt) { st.raced = true; emit('online-race', ctx()); }
    }
  }
  /** Someone sat down, left or got ready while the room waits: one sound (never in a race). */
  function seatSay(was) { if (st.phase === 'lobby') say(seatSound(was, st.seats)); }
  /** The grid count's bells on server time: each whole second before countAt (the race bells take over after). */
  function armBell() {
    if (bellT) { win.clearTimeout(bellT); bellT = 0; }
    if (!alive() || !st.start || st.phase === 'done') return;
    const b = nextGridBell(srv(), st.start.countAt);
    if (!b) return;
    bellT = win.setTimeout(() => { bellT = 0; if (!st.start) return; say('lobby-tick'); paintCount(); armBell(); }, b.waitMs);
  }
  function seatsChanged(seatNo) {
    if (st.view === 'room') paint();
    emit('online-seat', { seat: seatNo ?? null, seats: st.seats, tags: raceTags(st.seats) });
  }
  function onStart(m) {
    st.start = { countAt: m.countAt, startAt: m.startAt, goTick: m.goTick, seed: m.seed };
    st.seed = m.seed; st.phase = 'countdown'; st.raced = false; st.autoAt = 0;
    if (srv() < st.start.countAt) say('lobby-tick');
    armBell();
    emit('online-start', ctx());
    if (st.view === 'room') paint();
  }
  function onEmote(m) {
    const p = presetOf(m.code);
    if (!p) return;                                         // an unknown code is never printed
    st.bubbles[m.seat] = { code: m.code, until: localNow() + EMOTE_SHOW_MS };
    emit('online-emote', { seat: m.seat, code: m.code, preset: p, pose: poseOf(m.code) });
    if (st.view === 'room') paint();
  }
  function onFinish(m) {
    st.results = m.rows; st.phase = 'done'; st.start = null; st.view = 'results';
    emit('online-finish', { rows: m.rows, seat: st.seat, lines: resultLines(m.rows, st.seat) });
    paint();
  }
  function onAbort(m) {
    st.msg = abortCopy(m.reason);
    emit('online-abort', { reason: m.reason });
    dropRoom(); st.view = 'msg'; paint();
  }
  function onError(m) {
    st.msg = m.code === 'rate' ? errorCopy('rate') : m.code === 'host' ? 'Only the host starts' : m.code === 'phase' ? 'Not now' : '';
    if (st.msg && st.view === 'room') { setNote(st.msg); win.setTimeout(() => { st.msg = ''; if (st.view === 'room') paint(); }, 2400); }
  }
  function onClose(e) {
    if (st.leaving || !st.room) return;
    const done = st.phase === 'done';
    emit('online-closed', { code: e && e.code });
    dropRoom();
    if (done) return;                                        // the results stay up
    st.msg = closeCopy(e && e.code); st.view = 'msg'; paint();
  }

  function dropRoom() {
    for (const off of st.offs) { try { off(); } catch (e) { /* already gone */ } }
    st.offs = [];
    const r = st.room; st.room = null;
    if (r) { st.leaving = true; try { r.close?.(); } catch (e) { /* already closed */ } }
    st.join = null; st.welcome = null; st.start = null; st.autoAt = 0; st.bubbles = {}; st.myPose = null;
    st.link = null; st.linkCopied = false; st.linkMsg = '';             // the link lives on server side; Invite link fetches it again
    if (bellT) { win.clearTimeout(bellT); bellT = 0; }
  }

  /** Leave on purpose: tell the room and the lobby, then back to the tables. */
  function leave() {
    stopQuick(); stopClaim(); st.gen++;
    const r = st.room, j = st.join, api = deps().api;
    if (r) { try { r.send({ t: 'leave' }); } catch (e) { /* closing anyway */ } }
    if (j && api && api.leave) call(() => api.leave(j.roomId));
    const had = !!(r || j);
    dropRoom();
    st.phase = 'lobby'; st.seat = -1; st.seats = applySeats([]);
    if (had) emit('online-left', {});
  }

  /** A seat granted after the player cancelled: hand it straight back. */
  function late(j) { const api = deps().api; if (j && j.roomId && j.roomId !== transferredRoomId && api && api.leave) call(() => api.leave(j.roomId)); }

  /** Transfer the granted seat to another local course without releasing it on the server. */
  function handoff() {
    if (!alive()) return null;
    local = deps(); // Late answers still belong to this course's transport.
    const join = st.join ? {...st.join,look:normLook(st.look)} : null;
    transferredRoomId = join?.roomId || null;handedOff = true;st.gen++;
    stopQuick();stopClaim();dropRoom();stopTicker();
    return join;
  }

  function send(m) { if (st.room) { try { st.room.send(m); } catch (e) { /* the close handler reports it */ } } }

  // ---- input ---------------------------------------------------------------------------------

  /** A lobby button: its sound (hud/sounds.js), or a no when the press is refused. */
  function act(el) {
    if (!alive()) return;
    st.poll = pollReset(st.poll);
    const r = actOn(el);
    say(r === false ? 'menu-deny' : actSound(el.dataset.act));
  }
  function actOn(el) {
    const a = el.dataset.act, id = el.dataset.id, key = el.dataset.key;
    if (a === 'lb-quick') { quick(); return; }
    if (a === 'lb-refresh') { st.listAt = -1e9; refresh(); return; }
    if (a === 'lb-join') {
      const row = st.rows.find((r) => r.roomId === id), api = deps().api;
      if (!row || row.full || !api) return false;
      const g = ++st.gen;
      st.busy = true; st.view = 'joining'; paint();
      call(() => api.join(row.roomId, normLook(st.look))).then((j) => { st.busy = false; if (g !== st.gen) { late(j); return; } if (j) seat(j, row.raw); else { st.view = 'home'; paint(); } });
      return;
    }
    if (a === 'lb-host') {
      const gate = hostGate(ident());
      if (gate[id] !== 'ok') { st.msg = errorCopy(gate[id] === 'locked' ? 'tier' : 'auth'); paint(); return false; }
      st.host = cleanHost({ ...st.host, mode: id }); st.msg = ''; st.view = 'host'; paint(); return;
    }
    if (a === 'lb-hstep') { step(el, 1); return; }
    if (a === 'lb-lstep') { step(el, 1); return; }
    if (a === 'lb-open') {
      const api = deps().api;
      if (!api) return false;
      const g = ++st.gen;
      st.busy = true; st.view = 'joining'; paint();
      call(() => api.open(hostBody(st.host, st.look, track()))).then((j) => { st.busy = false; if (g !== st.gen) { late(j); return; } if (j) seat(j, null); else { st.view = 'host'; paint(); } });
      return;
    }
    if (a === 'lb-look') { st.view = 'look'; st.msg = ''; paint(); return; }
    if (a === 'lb-ready') { const me = st.seats[st.seat]; send({ t: 'ready', on: !(me && me.ready) }); return; }
    if (a === 'lb-start') { if (!canStart(st.seats, st.seat, st.phase)) return false; send({ t: 'start' }); return; }
    if (a === 'lb-emote') {
      const now = localNow();
      if (!presetOf(id) || !emoteAllowed(st.lastEmote, now)) return false;
      st.lastEmote = now; st.chatOff = true;
      st.myPose = { pose: poseOf(id), until: now + poseHoldS(id) * 1000 };
      send({ t: 'emote', code: id });
      paint(); return;
    }
    if (a === 'lb-invite') { openInvite(); return; }
    if (a === 'lb-link') { const api = deps().api; if (!api || !api.makeLink || st.linkBusy) return false; makeLink(); return; }
    if (a === 'lb-unlink') { const api = deps().api; if (!api || !api.revokeLink || !st.link) return false; revokeLink(); return; }
    if (a === 'lb-send') {
      const i = Number(id), f = st.friends && st.friends[i], api = deps().api;
      if (!f || !st.join || !api) return false;
      st.sent.add(i); paint();
      call(() => api.invite(f.uid, st.join.roomId)).then((r) => { if (!r) { st.sent.delete(i); } if (st.view === 'invite') paint(); });
      return;
    }
    if (a === 'lb-leave') { leave(); st.view = 'home'; st.msg = ''; st.listAt = -1e9; paint(); return; }
    if (a === 'lb-again') { leave(); quick(); return; }
    if (a === 'lb-home') { leave(); st.view = 'home'; st.msg = ''; st.listAt = -1e9; paint(); return; }
    if (a === 'lb-back') { back(true); return; }
    if (a === 'lb-exit') { leave(); stopTicker(); exit(); }
  }

  /** A form row one step (dir +1 / -1). */
  function step(el, dir) {
    const key = el.dataset.key;
    if (el.dataset.act === 'lb-hstep') { st.host = stepHost(st.host, key, dir); paint(); return; }
    st.look = cycleLook(st.look, key, dir, st.choices);
    saveLook(storage(), st.look);
    if (st.room && st.phase === 'lobby') send({ t: 'look', outfit: st.look.outfit, vehicle: st.look.vehicle });
    paint();
  }

  async function openInvite() {
    st.view = 'invite'; st.msg = ''; st.friends = null; paint();
    try { const list = await deps().friends(); st.friends = (Array.isArray(list) ? list : []).filter((f) => f && f.uid && f.name).slice(0, 50); }
    catch (e) { st.friends = []; st.msg = errorCopy('net'); }
    if (st.view === 'invite') paint();
  }

  /** Escape or pad back: up one view. In a room it moves focus to Leave and never leaves by itself. */
  function back(quiet = false) {
    if (!alive()) return;
    const v = st.view;
    if (!quiet) say(v === 'room' || v === 'joining' ? 'menu-move' : 'menu-back');
    if (v === 'quick') { stopQuick(); st.view = 'home'; st.msg = ''; paint(); return; }
    if (v === 'link' && st.claim) { stopClaim(); st.view = 'home'; st.msg = ''; st.listAt = -1e9; paint(); return; }
    if (v === 'host' || v === 'look' || v === 'invite') { st.view = st.room ? 'room' : 'home'; st.msg = ''; paint(); return; }
    if (v === 'room' || v === 'joining') { extra.querySelector('[data-act="lb-leave"]')?.focus?.({ preventScroll: true }); return; }
    if (v === 'results' || v === 'msg' || v === 'link') { leave(); st.view = 'home'; st.msg = ''; st.listAt = -1e9; paint(); return; }
    leave(); stopTicker(); exit();
  }

  /** Pad or arrows from hud/cards.js. True when handled here. */
  function nav(dir) {
    if (!alive()) return false;
    st.poll = pollReset(st.poll);
    if (dir === 'back') { back(); return true; }
    const cur = doc.activeElement;
    if ((dir === 'left' || dir === 'right') && cur && card.contains(cur) && (cur.dataset.act === 'lb-hstep' || cur.dataset.act === 'lb-lstep')) {
      if (!cur.disabled) { say('menu-toggle'); step(cur, dir === 'left' ? -1 : 1); }
      return true;
    }
    return false;
  }

  return {
    /** hud/cards.js calls this from show('lobby', data). data.view: 'results' or 'home' to jump there; data.claim: a link token (boot's ?r=). */
    show(data = {}) {
      if (!alive()) return;
      if(!st.room){const selected=deps().garageLook?.();if(selected)st.look=normLook(selected);}
      injectCss();
      if (data.join && !st.room) {
        if(data.join.look)st.look=normLook(data.join.look);
        seat(data.join,null);
      }
      else if (data.roomId && !st.room) {
        const g=++st.gen;
        st.view = 'joining';
        call(() => deps().api.join(data.roomId, normLook(st.look))).then(j => { if(!alive()||g!==st.gen){late(j);return;}if (j) seat(j, null); else { st.view = 'home'; paint(); } });
      }
      else if (data.claim && !st.room) startClaim(data.claim);
      else if (data.view === 'home' && !st.room) { st.view = 'home'; st.msg = ''; }
      else if (data.view === 'results' && st.results) st.view = 'results';
      startTicker();
      paint();
      if (st.view === 'home' && !st.looksAsked) loadLooks();
    },
    act, nav, back, leave, handoff,
    use(d) { local = { ...local, ...(d || {}) }; },
    get view() { return st.view; },
    get phase() { return st.room ? st.phase : null; },
    /** In-race tags by seat ('paused', 'away', 'left') for the kart labels. */
    tags() { return raceTags(st.seats); },
    /** The pose byte for our own record while an emote plays (B2-c snapshot), else 0. */
    pose(now = localNow()) { return st.myPose && st.myPose.until > now ? st.myPose.pose : 0; },
    /** Emote from the race (pause card or a pad chord): same rules as the lobby chat. */
    emote(code) { const el = { dataset: { act: 'lb-emote', id: code } }; act(el); },
    dispose() { if(disposed)return;local=deps();disposed=true;stopQuick(); leave(); stopTicker(); try { doc.removeEventListener?.('visibilitychange', onVis); } catch (e) { /* gone */ } },
  };
}

export { EMOTE_GAP_MS };
