/* hud/record.js - the garage's Record panel (CONTRACT-LOGBOOK.md section 1, RECORD lane).
 *
 * Four tabs in one native dialog over the carousel: Courses (medal, bests and counts per course),
 * Totals (lifetime counters with plain labels), Recent (the last 20 races and a small place line) and
 * Achievements (a wall of stamps: earned ones inked with their date, the rest faint with their line,
 * hidden ones read "Hidden" until earned). The row builders above createRecordPanel are pure and
 * tested in tests/logbook-record-ui.test.mjs; only createRecordPanel touches the DOM.
 * Keys and pad: left / right change tab, up / down scroll, ok presses, back closes (like the track picker).
 */
import { formatTime } from './format.js';
import { ACHIEVEMENTS } from '../meta/achievements.js';
import { UNLOCKS } from '../meta/unlocks.js';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const MEDAL_NAMES = { bronze: 'Bronze', silver: 'Silver', gold: 'Gold', glass: 'Glass' };
export const TABS = Object.freeze([['courses', 'Courses'], ['totals', 'Totals'], ['recent', 'Recent'], ['stamps', 'Achievements']]);

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const best = (s) => (s > 0 ? formatTime(s) : '-');
const count = (n) => String(Math.max(0, Math.floor(n) || 0));

/** '2026-10-26T12:00:00Z' -> '26 Oct 2026' (UTC, no locale). Bad input reads ''. */
export function formatDate(at) {
  const t = Date.parse(at);
  if (!Number.isFinite(t)) return '';
  const d = new Date(t);
  return d.getUTCDate() + ' ' + MONTHS[d.getUTCMonth()] + ' ' + d.getUTCFullYear();
}

/** Seconds as a plain span: '41 s', '12 min 5 s', '2 h 14 min'. */
export function formatSpan(s) {
  s = Math.max(0, Math.round(Number(s) || 0));
  if (s < 60) return s + ' s';
  if (s < 3600) return Math.floor(s / 60) + ' min' + (s % 60 ? ' ' + (s % 60) + ' s' : '');
  return Math.floor(s / 3600) + ' h ' + Math.floor((s % 3600) / 60) + ' min';
}

/** One row per course, every value a display string. */
export function courseRows(record, courses) {
  return courses.map(({ id, name }) => {
    const c = record.courses[id] || {};
    return { id, name, medal: MEDAL_NAMES[c.medal] || '-', medalId: c.medal || '', bestRace: best(c.bestRaceS), bestLap: best(c.bestLapS),
      races: count(c.races), wins: count(c.wins), podiums: count(c.podiums), trial: c.trialBestS > 0 ? formatTime(c.trialBestS) : null };
  });
}

/** Lifetime totals with plain labels. */
export function totalRows(record) {
  const t = record.totals;
  return [['Races', count(t.races)], ['Wins', count(t.wins)], ['Km driven', (t.distanceM / 1000).toFixed(1)],
    ['Drift boosts', count(t.driftBoosts)], ['Time in the air', formatSpan(t.airS)], ['Falls', count(t.falls)],
    ['Rivals Tethered', count(t.tetherBreaks)], ['Words caught', count(t.wordsCaught)]].map(([label, value]) => ({ label, value }));
}

/** The last races, newest first, ready to print. */
export function recentRows(record, courses) {
  return record.recent.map((r) => ({
    course: courses.find((c) => c.id === r.track)?.name || r.track,
    place: r.mode === 'trial' ? 'Trial' : r.place > 0 ? r.place + ' of ' + r.of : 'Out',
    time: r.timeS > 0 ? formatTime(r.timeS) : '-', date: formatDate(r.at),
  }));
}

/** The place line in a w x h box: oldest race on the left, 1st at the top. Null under two races. */
export function placeLine(recent, w = 200, h = 44, pad = 5) {
  const list = [...recent].reverse().filter((r) => r.place > 0);
  if (list.length < 2) return null;
  const points = list.map((r, i) => {
    const share = r.of > 1 ? (Math.min(r.place, r.of) - 1) / (r.of - 1) : 0;
    return [+(pad + (i * (w - pad * 2)) / (list.length - 1)).toFixed(1), +(pad + share * (h - pad * 2)).toFixed(1)];
  });
  return { d: 'M' + points.map((p) => p.join(' ')).join(' L'), points };
}

/** One stamp per achievement. Hidden and unearned reads "Hidden" with no line. */
export function achievementStamps(record, list = ACHIEVEMENTS) {
  return list.map((a) => {
    const at = record.achievements[a.id], earned = !!at, veiled = a.hidden && !earned;
    const reward = !veiled && a.reward && UNLOCKS[a.reward] ? 'Unlocks ' + UNLOCKS[a.reward].label : '';
    return { id: a.id, earned, hidden: veiled, name: veiled ? 'Hidden' : a.name, line: veiled ? 'Keep racing' : a.line,
      date: earned ? formatDate(at) : '', reward };
  });
}

function tabHtml(tab, record, courses) {
  if (tab === 'courses') {
    return '<table class="g-rec-table"><thead><tr><th>Course</th><th>Medal</th><th>Best race</th><th>Best lap</th><th>Races</th><th>Wins</th><th>Podiums</th></tr></thead><tbody>' +
      courseRows(record, courses).map((r) => '<tr><th scope="row">' + esc(r.name) + (r.trial ? '<small>Time Trial ' + r.trial + '</small>' : '') +
        '</th><td><span class="g-rec-medal" data-medal="' + esc(r.medalId) + '">' + r.medal + '</span></td><td>' + r.bestRace + '</td><td>' + r.bestLap +
        '</td><td>' + r.races + '</td><td>' + r.wins + '</td><td>' + r.podiums + '</td></tr>').join('') + '</tbody></table>';
  }
  if (tab === 'totals') return '<dl class="g-rec-totals">' + totalRows(record).map((r) => '<div><dt>' + r.label + '</dt><dd>' + r.value + '</dd></div>').join('') + '</dl>';
  if (tab === 'recent') {
    const rows = recentRows(record, courses);
    if (!rows.length) return '<p class="g-rec-empty">No races yet. The logbook waits.</p>';
    const line = placeLine(record.recent);
    const svg = line ? '<svg class="g-rec-line" viewBox="0 0 200 44" role="img" aria-label="Place in each race, oldest first"><path d="' + line.d + '"/>' +
      line.points.map(([x, y]) => '<circle cx="' + x + '" cy="' + y + '" r="2.2"/>').join('') + '</svg>' : '';
    return svg + '<ol class="g-rec-recent">' + rows.map((r) => '<li><b>' + esc(r.course) + '</b><span>' + r.place + '</span><span>' + r.time + '</span><small>' + r.date + '</small></li>').join('') + '</ol>';
  }
  const stamps = achievementStamps(record);
  return '<p class="g-rec-count">' + stamps.filter((s) => s.earned).length + ' of ' + stamps.length + ' earned</p><ul class="g-rec-stamps">' +
    stamps.map((s) => '<li class="g-stamp' + (s.earned ? ' earned' : '') + (s.hidden ? ' veiled' : '') + '"><b>' + esc(s.name) + '</b><span>' + esc(s.line) + '</span>' +
      (s.reward ? '<em>' + esc(s.reward) + '</em>' : '') + (s.date ? '<small>' + s.date + '</small>' : '') + '</li>').join('') + '</ul>';
}

/** The panel, built on first open. `load` returns the current record each time the panel opens. */
export function createRecordPanel({ root, courses, load }) {
  let dialog = null, body = null, tabs = [], back = null, opener = null, tab = 'courses', record = null;
  const paint = () => {
    for (const b of tabs) b.setAttribute('aria-selected', String(b.dataset.tab === tab));
    body.innerHTML = tabHtml(tab, record, courses); body.scrollTop = 0;
  };
  const pick = (id, focus = true) => { tab = id; paint(); if (focus) tabs.find((b) => b.dataset.tab === id)?.focus({ preventScroll: true }); };
  const close = () => { if (!dialog?.open) return; dialog.close(); opener?.focus({ preventScroll: true }); };
  function build() {
    dialog = document.createElement('dialog');
    dialog.className = 'g-record'; dialog.setAttribute('aria-labelledby', 'g-record-title');
    dialog.innerHTML = '<header><div><small>THE LOGBOOK</small><h2 id="g-record-title">Record</h2></div><button type="button" class="g-record-back">Back</button></header>' +
      '<div class="g-record-tabs" role="tablist">' + TABS.map(([id, label]) => '<button type="button" role="tab" data-tab="' + id + '">' + label + '</button>').join('') +
      '</div><div class="g-record-body" role="tabpanel" tabindex="-1"></div>';
    root.append(dialog);
    body = dialog.querySelector('.g-record-body'); tabs = [...dialog.querySelectorAll('[data-tab]')]; back = dialog.querySelector('.g-record-back');
    for (const b of tabs) b.onclick = () => pick(b.dataset.tab);
    back.onclick = close;
    dialog.addEventListener('cancel', (e) => { e.preventDefault(); close(); });
  }
  return {
    get open() { return !!dialog?.open; },
    close() { if (dialog?.open) dialog.close(); },
    show(trigger = document.activeElement) { if (dialog?.open) return; if (!dialog) build(); opener = trigger; record = load(); dialog.showModal(); pick(tab); },
    nav(dir) {
      if (!dialog?.open) return;
      if (dir === 'back') { close(); return; }
      if (dir === 'ok') { document.activeElement?.click?.(); return; }
      const i = TABS.findIndex(([id]) => id === tab);
      if (dir === 'left' || dir === 'right') { pick(TABS[(i + (dir === 'left' ? -1 : 1) + TABS.length) % TABS.length][0]); return; }
      const step = body.clientHeight * 0.6 * (dir === 'up' ? -1 : 1);
      if (dir === 'down' && body.scrollTop + body.clientHeight >= body.scrollHeight - 2) { back.focus(); return; }
      if (dir === 'up' && document.activeElement === back) { tabs.find((b) => b.dataset.tab === tab)?.focus(); return; }
      body.scrollBy({ top: step });
    },
    dispose() { dialog?.remove(); dialog = null; },
  };
}
