/* hud/cards.js - the cards in #card: menu, pause, options, controls, results (B1), plus the
 * Phase A start and finish cards.
 *
 * Paper and ink: one card at a time, built from plain buttons so the keyboard (arrows, Tab,
 * Enter, Space) and the pad (nav()) reach every control. Escape on options or controls goes
 * back to the card that opened it; on any other card Escape does nothing here and is never
 * swallowed (the host hears it). Options and Controls open themselves and Back returns by
 * itself; the hud still emits each name so boot can follow. Copy is dry and short.
 */
import { formatTime, ordinal, pauseLine, resultRows } from './format.js';
import { cleanOptions, loadOptions, saveOptions, stepVolume } from './options.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const btn = (act, label, cls = '', extra = '') => '<button type="button" class="c-btn ' + cls + '" data-act="' + act + '"' + extra + '>' + label + '</button>';

const CONTROLS = [
  ['Steer', 'A D or arrows', 'Left stick', 'Drag on the left half'],
  ['Throttle', 'W or Up', 'RT', 'Automatic'],
  ['Brake', 'S or Down', 'LT', ''],
  ['Hop and drift', 'Space or Shift, hold', 'A, hold', 'Hold the right half'],
  ['Use item', 'E', 'X', 'Tap the item'],
  ['Drop it behind', 'Hold S, then E', 'Stick down, then X', ''],
  ['Pause', 'Esc', 'Start', 'Pause button'],
];
const VOL_ROWS = [['volMaster', 'Volume'], ['volMusic', 'Music'], ['volSfx', 'Effects']];

export function createCards(card, { doc = globalThis.document, emit } = {}) {
  const body = card.querySelector('.card-body');
  const title = card.querySelector('.card-title');
  const note = card.querySelector('.card-note');
  const go = card.querySelector('#card-go') || card.querySelector('button');
  let extra = card.querySelector('.card-extra');
  if (!extra) { extra = doc.createElement('div'); extra.className = 'card-extra'; go.before(extra); }
  const old = card.querySelector('#card-restart');
  if (old) old.remove();

  let kind = 'start', data = {}, ready = false, waitNote = note?.textContent || '';
  let opts = loadOptions(), host = { gentle: false };
  const stack = { options: null, controls: null };

  function show(k, d = {}) {
    kind = k; data = d || {};
    card.hidden = !k;
    if (!k) return;
    card.dataset.kind = k;
    body.classList.remove('in'); void body.offsetWidth; body.classList.add('in');
    go.hidden = !(k === 'start' || k === 'finish');
    extra.innerHTML = '';
    note.hidden = false;
    if (k === 'start') {
      title.textContent = data.title || 'Threshold';
      note.textContent = ready ? 'Press any key' : waitNote;
      go.textContent = 'Start'; go.disabled = !ready;
    } else if (k === 'finish') {
      title.textContent = 'Finished';
      note.textContent = formatTime(data.timeS) + (data.best ? ', best lap ' + formatTime(data.best) : '');
      go.textContent = 'Again'; go.disabled = false;
    } else if (k === 'menu') {
      title.innerHTML = esc(data.title || 'Racing Thoughts') + '<small>A CC Labs game</small>';
      note.textContent = (data.track || 'Threshold') + ', ' + (data.laps || 3) + ' laps';
      extra.innerHTML = '<div class="c-doors">' +
        btn('race', '<b>Race</b><em>You and 7 bots</em>', 'c-door hot') +
        btn('online', '<b>Online</b><em>Rooms and friends</em><span class="c-stamp">soon</span>', 'c-door soon', ' aria-disabled="true"') +
        '</div><div class="c-links">' + btn('options', 'Options', 'c-link') + btn('controls', 'Controls', 'c-link') +
        btn('back', 'Back', 'c-link') + '</div>';
    } else if (k === 'pause') {
      title.textContent = 'Paused';
      note.textContent = pauseLine(data) || 'The race waits';
      extra.innerHTML = '<div class="c-list">' + btn('resume', 'Resume', 'hot') + btn('restart', 'Restart') +
        btn('options', 'Options') + btn('quit', 'Quit to menu', 'quiet') + '</div>';
    } else if (k === 'options') {
      if (typeof data.hostGentle === 'boolean') host.gentle = data.hostGentle;
      title.textContent = 'Options';
      note.hidden = true;
      extra.innerHTML = optionsHtml() + '<div class="c-list">' + btn('back', 'Back', 'quiet') + '</div>';
    } else if (k === 'controls') {
      title.textContent = 'Controls';
      note.hidden = true;
      extra.innerHTML = '<table class="c-keys"><thead><tr><th></th><th>Keys</th><th>Pad</th><th>Touch</th></tr></thead><tbody>' +
        CONTROLS.map((r) => '<tr><th>' + r[0] + '</th><td>' + r[1] + '</td><td>' + r[2] + '</td><td>' + (r[3] || '') + '</td></tr>').join('') +
        '</tbody></table><div class="c-list">' + btn('back', 'Back', 'quiet') + '</div>';
    } else if (k === 'results') {
      const me = data.me || {}, o = ordinal(me.place || 0);
      title.innerHTML = '<span class="c-place">' + o.n + '<sup>' + o.suf + '</sup></span>';
      note.textContent = formatTime(me.timeS) + (me.bestLapS > 0 ? ', best lap ' + formatTime(me.bestLapS) : '');
      const rows = resultRows(data, data.metas || null);
      extra.innerHTML = '<ol class="c-rows">' + rows.map((r) =>
        '<li class="' + (r.me ? 'me' : '') + '"><span class="p">' + r.place + '</span><span class="n">' + esc(r.name) +
        '</span><span class="t' + (r.est ? ' est' : '') + '">' + (r.est ? '~' : '') + r.time + '</span></li>').join('') +
        '</ol><div class="c-list row">' + btn('again', 'Again', 'hot') + btn('menu', 'Menu') + '</div>';
    }
    focusFirst();
  }

  function optionsHtml() {
    const g = host.gentle;
    const tog = (key, label, on, hint, lock) => '<div class="c-opt"><span>' + label + '<small>' + hint + '</small></span>' +
      btn('toggle', lock ? 'On' : on ? 'On' : 'Off', 'c-tog' + (on || lock ? ' on' : ''), ' data-key="' + key + '" aria-pressed="' + !!(on || lock) + '"' + (lock ? ' disabled' : '')) + '</div>';
    return '<div class="c-opts">' +
      tog('gentlePreview', 'Gentle preview', opts.gentlePreview, g ? 'Set in CCP, stays on' : 'Softer effects, no flicker', g) +
      tog('assist', 'Steering assist', opts.assist, 'Soft rails keep you on', false) +
      VOL_ROWS.map(([key, label]) => '<label class="c-opt"><span>' + label + '</span><input type="range" min="0" max="1" step="0.05" data-key="' +
        key + '" value="' + opts[key] + '" aria-label="' + label + '"></label>').join('') + '</div>';
  }

  function setOption(key, value) {
    opts = cleanOptions({ ...opts, [key]: value });
    saveOptions(opts);
    emit('option', { key, value: opts[key], options: { ...opts } });
  }

  const focusables = () => [...card.querySelectorAll('button, input')].filter((e) => !e.hidden && !e.disabled && e.offsetParent !== null);
  function focusFirst() {
    if (kind === 'start' && go.disabled) return;
    const list = focusables();
    const first = list.find((e) => e.classList.contains('hot')) || list[0];
    first?.focus?.({ preventScroll: true });
  }

  function act(el) {
    if (!el) return;
    const a = el.dataset.act;
    if (a === 'toggle') {
      const key = el.dataset.key, on = !(opts[key]);
      setOption(key, on);
      el.textContent = on ? 'On' : 'Off'; el.classList.toggle('on', on); el.setAttribute('aria-pressed', String(on));
      return;
    }
    if (a === 'online') { note.textContent = 'Online opens later'; emit('online', {}); return; }
    if (a === 'options' || a === 'controls') {
      stack[a] = { kind, data };
      show(a, a === 'options' ? { from: kind, hostGentle: host.gentle } : { from: kind });
      emit(a, { from: stack[a].kind });
      return;
    }
    if (a === 'back' && (kind === 'options' || kind === 'controls')) { back(); return; }
    emit(a, a === 'back' ? { from: kind } : {});
  }

  function back() {
    const prev = stack[kind] || { kind: data.from || 'menu', data: {} };
    const from = kind;
    show(prev.kind, prev.data);
    emit('back', { from, to: prev.kind });
  }

  body.addEventListener('click', (e) => {
    const el = e.target.closest && e.target.closest('button[data-act]');
    if (el && !el.disabled) act(el);
  });
  body.addEventListener('input', (e) => {
    const el = e.target;
    if (el && el.dataset && el.dataset.key && el.type === 'range') setOption(el.dataset.key, Number(el.value));
  });
  go.addEventListener('click', () => {
    if (kind === 'start' && ready) emit('start', {});
    else if (kind === 'finish') emit('restart', {});
  });

  /** Pad or arrow navigation on the visible card. */
  function nav(dir) {
    if (card.hidden) return;
    if (dir === 'back') { if (kind === 'options' || kind === 'controls') back(); return; }
    const list = focusables();
    if (!list.length) return;
    const cur = doc.activeElement, i = list.indexOf(cur);
    if (dir === 'ok') {
      if (i < 0) { (list.find((e) => e.classList.contains('hot')) || list[0]).click?.(); return; }
      if (cur.tagName === 'BUTTON') cur.click();
      return;
    }
    if ((dir === 'left' || dir === 'right') && cur && cur.type === 'range') {
      cur.value = String(stepVolume(Number(cur.value), dir === 'left' ? -1 : 1));
      setOption(cur.dataset.key, Number(cur.value));
      return;
    }
    const step = dir === 'up' || dir === 'left' ? -1 : 1;
    const next = list[i < 0 ? 0 : (i + step + list.length) % list.length];
    next.focus?.({ preventScroll: true });
  }

  doc.addEventListener('keydown', (e) => {
    if (card.hidden || e.altKey || e.ctrlKey || e.metaKey) return;
    if (e.key === 'Escape') { if (kind === 'options' || kind === 'controls') back(); return; }   // never swallowed
    if (kind === 'start') {
      if (e.repeat || e.key === 'Shift' || e.key === 'Tab' || e.key === 'CapsLock') return;
      e.preventDefault(); if (ready) emit('start', {}); return;
    }
    if (kind === 'finish') { if (e.key === 'Enter' && !e.repeat) { e.preventDefault(); emit('restart', {}); } return; }
    const onRange = doc.activeElement && doc.activeElement.type === 'range';
    const dir = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' }[e.key];
    if (dir) {
      if (onRange && (dir === 'left' || dir === 'right')) return;   // the slider moves itself
      e.preventDefault(); nav(dir); return;
    }
    if ((e.key === 'Enter' || e.key === ' ') && !card.contains(doc.activeElement)) { e.preventDefault(); if (!e.repeat) nav('ok'); }
  });

  return {
    show, nav,
    get kind() { return card.hidden ? null : kind; },
    get options() { return { ...opts }; },
    setReady(r, n) { ready = !!r; if (n) waitNote = n; if (kind === 'start' && !card.hidden) show('start', data); },
    setHostGentle(on) { host.gentle = !!on; },
  };
}
