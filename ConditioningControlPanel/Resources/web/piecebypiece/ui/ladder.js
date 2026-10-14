/* ============================================================================
 * ui/ladder.js - your rating where it matters, and the ladder (top 50).
 *
 *   createLadder({ fetchTop, fetchMe }) -> {
 *     refreshMe(), loadTop(force), me(), snapshot(),
 *     chipHtml(), paintChips(root), screenHtml(myName), mountScreen(root, { still }),
 *     endSlotHtml(), mountEnd(slot, end, { still, sfx }), fake({ top, me })
 *   }
 *
 * The door (door/door.js) owns the screens and calls in: the chip beside your
 * name on the menu and the tables, a Ladder screen off both, and the end
 * card's rating beat. The beat is: the number ROLLS from the old rating to
 * the new one (a few ticks), lands with a glow that breathes out, then the
 * +N stamp (green) or -N (red) slams in, and a "new best" stamp follows on a
 * personal best. Reduced motion takes the state and not the travel: the final
 * number and the stamps, no roll, no slam. A beat plays once; a repaint of the
 * card draws its landed state.
 *
 * The Ladder screen is a board (2026-10-14 polish pass; owner: "the ui is
 * awful"): a Back button top-left (door/back-button.js; the door wires every
 * [data-back]), your own card with the rating rolling up to your number, the
 * top three on a podium that rises third, second, first, the rest as rows that
 * stagger in under a thin pink scrollbar, and your row found and scrolled to
 * with a ring that comes and goes. paintChips(), which the door already calls
 * after every render, mounts it (mountScreen), so the door needs no new hook;
 * a repaint right after a mount continues the same arrival instead of
 * restarting it. Sounds: audio/ui-cues.js (ticks on the roll, the podium's
 * ticks and stamp on the first visit of a session only).
 *
 * Numbers and words: ui/ladder-plan.js. Wire: net/api.js ladder / ladderMe.
 * ==========================================================================*/

import { ladder as apiTop, ladderMe as apiMe } from '../net/api.js';
import { LADDER_TUNING as L, BOARD_TUNING as B, normMe, normTop, chipText, rankText, markMine, rollValue, tickTimes,
  podiumOf, podiumAt, rowAt, youRoll } from './ladder-plan.js';
import { backButton } from '../door/back-button.js';
import { ui } from '../audio/ui-cues.js';

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
const cue = (name) => { try { ui[name](); } catch { /* sound is optional */ } };

export function createLadder(opts = {}) {
  let fetchTop = opts.fetchTop || apiTop;
  let fetchMe = opts.fetchMe || apiMe;
  const now = opts.now || (() => Date.now());
  let me = null;               // normMe, or null before the server answered
  let top = null;              // normTop
  let topAt = 0;
  let topState = 'idle';       // 'idle' | 'loading' | 'ok' | 'offline'
  let mePending = null;
  // the board's arrival: one per visit, continued across a quick repaint
  let seen = null;             // the rating this page last showed on the board
  let arrival = null;          // the list's: { key, at, loud }
  let youArrival = null;       // your card's: { key, at, roll }
  let visits = 0;

  /** Ask the server where you stand. Keeps the last answer on a failure. */
  function refreshMe() {
    if (mePending) return mePending;
    mePending = Promise.resolve().then(() => fetchMe()).then((res) => {
      if (res && res.ok) me = normMe(res.data) || me;
      return me;
    }).catch(() => me).finally(() => { mePending = null; });
    return mePending;
  }

  function loadTop(force = false) {
    if (!force && top && now() - topAt < L.cacheMs) return Promise.resolve(top);
    topState = top ? topState : 'loading';
    return Promise.resolve().then(() => fetchTop()).then((res) => {
      if (res && res.ok) { top = normTop(res.data); topAt = now(); topState = 'ok'; } else if (!top) topState = 'offline';
      return top;
    }).catch(() => { if (!top) topState = 'offline'; return top; });
  }

  const chipHtml = () => `<span class="ladder-chip${me && me.rated ? ' rated' : ''}">${esc(chipText(me))}</span>`;

  /** A refresh landed: repaint every chip under root in place, and mount a Ladder board that is new. */
  function paintChips(root) {
    if (!root || !root.querySelectorAll) return;
    for (const el of root.querySelectorAll('.ladder-chip')) {
      el.textContent = chipText(me);
      el.classList.toggle('rated', !!(me && me.rated));
    }
    for (const el of root.querySelectorAll('.ladder-rank')) el.textContent = rankText(me);
    const board = root.querySelector('.ladder-board:not([data-mounted])');
    if (board) mountScreen(board);
  }

  /* ---- the screen ------------------------------------------------------- */

  const plural = (n) => `${n} ${n === 1 ? 'game' : 'games'}`;

  function youHtml(myName, inTop) {
    if (!me) return '<div class="ladder-you none"><p>your place shows once you are signed in</p></div>';
    if (!me.rated) {
      return `<div class="ladder-you unrated"><span class="ladder-you-stamp">unrated</span>
        <p>finish an online game of 5 moves each to get on the ladder</p></div>`;
    }
    const rank = me.rank ?? '-';
    return `<div class="ladder-you rated${inTop ? ' in-top' : ''}">
        <span class="ladder-coin you" aria-hidden="true">${esc(rank)}</span>
        <span class="ladder-you-who"><b>${esc(myName || 'you')}</b><small>${esc(rankText(me) || 'rated')}${me.best !== null ? ` - best ${esc(me.best)}` : ''}</small></span>
        <span class="ladder-you-n" data-to="${esc(me.rating)}" aria-label="rating ${esc(me.rating)}">${esc(me.rating)}</span>
      </div>`;
  }

  function podiumHtml(podium) {
    if (!podium.length) return '';
    return `<div class="ladder-podium" aria-label="Top three">${podium.map((r) => `
        <div class="ladder-step p${r.place}${r.mine ? ' mine' : ''}" style="--at:${podiumAt(r.place)}ms">
          <span class="ladder-coin p${r.place}" aria-label="rank ${esc(r.rank)}">${esc(r.rank)}</span>
          <span class="ladder-step-name">${esc(r.name)}</span>
          <span class="ladder-step-n">${esc(r.rating)}</span>
          <span class="ladder-step-g">${esc(plural(r.games))}</span>
          <span class="ladder-block" aria-hidden="true"></span>
        </div>`).join('')}</div>`;
  }

  function rowHtml(r, i) {
    return `<li class="ladder-row${r.mine ? ' mine' : ''}" style="--at:${rowAt(i)}ms"><span class="ladder-rk">${esc(r.rank)}</span>
          <span class="ladder-name">${esc(r.name)}${r.mine ? '<i class="ladder-tag">you</i>' : ''}</span><span class="ladder-games">${esc(plural(r.games))}</span>
          <span class="ladder-rating">${esc(r.rating)}</span></li>`;
  }

  function screenHtml(myName) {
    let body;
    let inTop = false;
    if (!top) body = `<div class="door-empty">${topState === 'offline' ? 'the ladder is out of reach right now' : 'reading the ladder <span class="door-dot"></span>'}</div>`;
    else if (!top.rows.length) body = '<div class="door-empty">nobody rated yet. be first</div>';
    else {
      const marked = markMine(top.rows, me, myName);
      inTop = marked.some((r) => r.mine);
      const { podium, rest } = podiumOf(marked);
      const pinned = me && me.rated && !inTop && me.rank !== null
        ? `<div class="ladder-pinned">${rowHtml({ rank: me.rank, name: myName || 'you', games: me.games, rating: me.rating, mine: true }, rest.length)}</div>` : '';
      body = `${podiumHtml(podium)}
        ${rest.length ? `<div class="ladder-scroll" tabindex="0"><ol class="ladder-list" aria-label="Top rated players" start="4">${rest.map(rowHtml).join('')}</ol></div>` : ''}
        ${pinned}`;
    }
    const count = top && top.rows.length ? `<span class="ladder-count">top ${top.rows.length} of ${top.total}</span>` : '';
    return `<div class="ladder-board${top && top.rows.length ? ' has-list' : ''}">
      <div class="ladder-head">${backButton('Back')}<h1 class="ladder-title">ladder</h1>${count}</div>
      ${youHtml(myName, inTop)}
      ${body}
    </div>`;
  }

  /* ---- the arrival ------------------------------------------------------ */

  const isStill = (el) => {
    if (opts.still !== undefined) return !!opts.still;
    try { if (el.closest && el.closest('.still')) return true; } catch { /* detached */ }
    try { return !!(globalThis.matchMedia && globalThis.matchMedia('(prefers-reduced-motion: reduce)').matches); } catch { return false; }
  };
  const raf = (fn) => ((typeof requestAnimationFrame === 'function') ? requestAnimationFrame(fn) : setTimeout(() => fn(now()), 16));
  const later = (fn, ms) => setTimeout(fn, Math.max(0, ms));

  /**
   * Bring a freshly painted board in. A board painted within B.freshMs of the last
   * one with the same contents carries on where that one was (CSS --since).
   */
  function mountScreen(board, { still = isStill(board) } = {}) {
    if (!board || board.dataset.mounted) return;
    board.dataset.mounted = '1';
    const hasList = board.classList.contains('has-list');
    const t = now();
    const fresh = (a, key) => a && a.key === key && t - a.at < B.freshMs;
    // the list (podium + rows) and your card arrive on their own clocks: the card can
    // already be rolling on the "reading the ladder" paint when the list comes in
    const listKey = hasList ? `${top.rows.length}:${top.total}:${top.rows[0] && top.rows[0].rating}` : 'wait';
    if (!fresh(arrival, listKey)) {
      if (hasList) visits += 1;
      arrival = { key: listKey, at: t, loud: hasList && visits === 1 };
      if (hasList) schedulePodiumCues(board, still);
    }
    const youKey = me && me.rated ? String(me.rating) : '-';
    if (!fresh(youArrival, youKey)) youArrival = { key: youKey, at: t, roll: youRoll(me, seen) };
    if (me && me.rated) seen = me.rating;
    if (still) { board.classList.add('landed'); findMine(board, true, 0); return; }
    board.style.setProperty('--since', `${Math.round(t - arrival.at)}ms`);
    board.style.setProperty('--you-since', `${Math.round(t - youArrival.at)}ms`);
    rollYou(board, t - youArrival.at);
    findMine(board, false, t - arrival.at);
  }

  function schedulePodiumCues(board, still) {
    if (!arrival.loud || still) return;
    const steps = board.querySelectorAll('.ladder-step').length;
    if (!steps) return;
    const land = (place) => podiumAt(place) + B.stepMs * 0.55;
    if (steps >= 3) later(() => cue('tick'), land(3));
    if (steps >= 2) later(() => cue('tick'), land(2));
    later(() => cue('stamp'), land(1));
  }

  /** Your rating rolls up to your number, ticking as it goes. */
  function rollYou(board, since) {
    const n = board.querySelector('.ladder-you-n');
    const roll = youArrival && youArrival.roll;
    if (!n || !roll) return;
    const startAt = B.youAtMs - since;
    const ticks = tickTimes(roll.from, roll.to).filter((x) => x * B.youRollMs > -startAt);
    const t0 = now() + startAt;
    n.textContent = String(startAt > 0 ? roll.from : rollValue(roll.from, roll.to, -startAt / B.youRollMs));
    n.classList.add('rolling');
    const step = () => {
      if (!n.isConnected) return;   // repainted: the new board carries on
      const k = Math.min(1, Math.max(0, (now() - t0) / B.youRollMs));
      n.textContent = String(rollValue(roll.from, roll.to, k));
      while (ticks.length && ticks[0] <= k) { ticks.shift(); cue('tick'); }
      if (k < 1) { raf(step); return; }
      n.classList.remove('rolling');
      n.classList.add('rolled');
    };
    raf(step);
  }

  /** Your row: scrolled to the middle of the list once the rows have landed, and ringed. */
  function findMine(board, still, since) {
    const scroll = board.querySelector('.ladder-scroll');
    const row = scroll && scroll.querySelector('.ladder-row.mine');
    if (!row) return;
    const go = () => {
      if (!row.isConnected) return;
      const to = row.offsetTop - (scroll.clientHeight - row.offsetHeight) / 2;
      try { scroll.scrollTo({ top: Math.max(0, to), behavior: still ? 'auto' : 'smooth' }); } catch { scroll.scrollTop = Math.max(0, to); }
      row.classList.add('found');
    };
    if (still) go(); else later(go, B.scrollAtMs - since);
  }

  /* ---- the end card's beat --------------------------------------------- */

  const endSlotHtml = () => '<div class="rating-slot" aria-live="polite"></div>';

  /**
   * The end card's beat, into the slot. `end` = { plan (ladder-plan endPlan), played }.
   * Plays the roll once; any later call (a repaint) draws the landed state.
   */
  function mountEnd(slot, end, { still = false, sfx = () => {} } = {}) {
    if (!slot || !end || !end.plan) return;
    const p = end.plan;
    if (!p.rated) {
      slot.innerHTML = p.note ? `<p class="rating-note">${esc(p.note)}</p>` : '';
      return;
    }
    const quiet = still || end.played;
    slot.innerHTML = `
      <div class="rating-end ${p.tone}${quiet ? ' landed' : ''}">
        <span class="rating-num">${quiet ? p.to : p.from}</span>
        <span class="rating-stamp">${esc(p.stamp)}</span>
        ${p.newBest ? '<span class="rating-best">new best</span>' : ''}
      </div>
      ${p.note ? `<p class="rating-note rank">${esc(p.note)}</p>` : ''}`;
    if (quiet) { end.played = true; return; }
    end.played = true;
    const box = slot.querySelector('.rating-end');
    const num = slot.querySelector('.rating-num');
    const ticks = tickTimes(p.from, p.to);
    const t0 = now();
    const step = () => {
      if (!num.isConnected) return;   // the card moved on; the next paint draws it landed
      const t = Math.min(1, (now() - t0) / L.rollMs);
      num.textContent = String(rollValue(p.from, p.to, t));
      while (ticks.length && ticks[0] <= t) { ticks.shift(); cue('tick'); }
      if (t < 1) { raf(step); return; }
      setTimeout(() => {
        if (!box.isConnected) return;
        box.classList.add('landed');
        cue('stamp');
        if (p.newBest) setTimeout(() => { if (box.isConnected) { cue('stamp'); sfx('promote'); } }, L.bestDelayMs);
      }, L.stampDelayMs);
    };
    raf(step);
  }

  return {
    refreshMe, loadTop, paintChips, chipHtml, screenHtml, mountScreen, endSlotHtml, mountEnd,
    me: () => me,
    /** A copy of where you stand now, kept as the "before" of a game being dealt. */
    snapshot: () => (me ? { ...me } : null),
    /** Harness: stand in for the server. */
    fake({ top: t, me: m } = {}) {
      if (t !== undefined) { fetchTop = () => Promise.resolve({ ok: true, status: 200, data: t }); top = null; topAt = 0; }
      if (m !== undefined) { fetchMe = () => Promise.resolve({ ok: true, status: 200, data: m }); me = normMe(m); }
    },
  };
}
