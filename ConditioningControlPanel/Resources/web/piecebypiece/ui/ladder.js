/* ============================================================================
 * ui/ladder.js - your rating where it matters, and the ladder (top 50).
 *
 *   createLadder({ fetchTop, fetchMe }) -> {
 *     refreshMe(), loadTop(force), me(), snapshot(),
 *     chipHtml(), paintChips(root), screenHtml(myName),
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
 * Numbers and words: ui/ladder-plan.js. Wire: net/api.js ladder / ladderMe.
 * ==========================================================================*/

import { ladder as apiTop, ladderMe as apiMe } from '../net/api.js';
import { LADDER_TUNING as L, normMe, normTop, chipText, rankText, markMine, rollValue, tickTimes } from './ladder-plan.js';

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

export function createLadder(opts = {}) {
  let fetchTop = opts.fetchTop || apiTop;
  let fetchMe = opts.fetchMe || apiMe;
  const now = opts.now || (() => Date.now());
  let me = null;               // normMe, or null before the server answered
  let top = null;              // normTop
  let topAt = 0;
  let topState = 'idle';       // 'idle' | 'loading' | 'ok' | 'offline'
  let mePending = null;

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

  /** A refresh landed: repaint every chip under root in place. */
  function paintChips(root) {
    if (!root || !root.querySelectorAll) return;
    for (const el of root.querySelectorAll('.ladder-chip')) {
      el.textContent = chipText(me);
      el.classList.toggle('rated', !!(me && me.rated));
    }
    for (const el of root.querySelectorAll('.ladder-rank')) el.textContent = rankText(me);
  }

  function screenHtml(myName) {
    const stat = (n, l) => `<div class="door-stat"><span class="n">${esc(n)}</span><span class="l">${esc(l)}</span></div>`;
    const mine = me && me.rated
      ? `<div class="door-stats ladder-mine">${stat(me.rating, 'rating')}${stat(me.rank ?? '-', 'rank')}${stat(me.best ?? '-', 'best')}</div>`
      : `<p class="door-sub">${me ? 'unrated. finish an online game of 5 moves each to get on it' : 'your place shows once you are signed in'}</p>`;
    let list;
    if (!top) list = `<div class="door-empty">${topState === 'offline' ? 'the ladder is out of reach right now' : 'reading the ladder <span class="door-dot"></span>'}</div>`;
    else if (!top.rows.length) list = '<div class="door-empty">nobody rated yet. be first</div>';
    else {
      list = `<ol class="ladder-list" aria-label="Top rated players">${markMine(top.rows, me, myName).map((r, i) => `
        <li class="ladder-row${r.mine ? ' mine' : ''}" style="--i:${Math.min(i, 10)}"><span class="ladder-rk">${r.rank}</span>
          <span class="ladder-name">${esc(r.name)}</span><span class="ladder-games">${r.games} ${r.games === 1 ? 'game' : 'games'}</span>
          <span class="ladder-rating">${r.rating}</span></li>`).join('')}</ol>`;
    }
    return `
      <h1 class="door-title">ladder</h1>
      ${mine}
      <div class="ladder-scroll" tabindex="0">${list}</div>
      <div class="door-foot"><span>esc - back</span><span>${top ? `top ${top.rows.length} of ${top.total}` : ''}</span></div>`;
  }

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
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : (fn) => setTimeout(() => fn(now()), 16);
    const step = () => {
      if (!num.isConnected) return;   // the card moved on; the next paint draws it landed
      const t = Math.min(1, (now() - t0) / L.rollMs);
      num.textContent = String(rollValue(p.from, p.to, t));
      while (ticks.length && ticks[0] <= t) { ticks.shift(); sfx('tick'); }
      if (t < 1) { raf(step); return; }
      setTimeout(() => {
        if (!box.isConnected) return;
        box.classList.add('landed');
        sfx('land');
        if (p.newBest) setTimeout(() => { if (box.isConnected) sfx('promote'); }, L.bestDelayMs);
      }, L.stampDelayMs);
    };
    raf(step);
  }

  return {
    refreshMe, loadTop, paintChips, chipHtml, screenHtml, endSlotHtml, mountEnd,
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
