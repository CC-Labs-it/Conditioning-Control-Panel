/* hud/cards.js - the cards in #card: menu, pause, options, controls, results (B1), plus the
 * Phase A start and finish cards.
 *
 * Paper and ink: one card at a time, built from plain buttons so the keyboard (arrows, Tab,
 * Enter, Space) and the pad (nav()) reach every control. Escape on options or controls goes
 * back to the card that opened it; on any other card Escape does nothing here and is never
 * swallowed (the host hears it). Options and Controls open themselves and Back returns by
 * itself; the hud still emits each name so boot can follow. Copy is dry and short.
 * Polish 1: the Controls card and the pause hint name the buttons of the device used last (hud/
 * labels.js); a pad without the standard mapping gets a layout button there. The menu doors and
 * the results wear the SVG kit when it has loaded (the `kit-*` classes on #card).
 * Music road pass 4: the 1-2-3 how-to card (hud/howto.js) shows before the first race of a session (the Race
 * door opens it, unless "Don't show again" is on) and from the menu's and the Controls card's How to play link.
 * Throttle dismisses it (boot.js passes a throttle press as nav('throttle')), so the card teaches the launch;
 * the Go button does the same for a tap or Enter. ?howto=0 skips it, and so does ?bot=1 unless ?howto=1.
 * Options gains Steer assist (Light, Strong, Off; sim/assist.js); the old assist toggle reads Soft rails.
 * Polish 5 (S4): every move, confirm, back, toggle and refusal emits `sound` { name } (hud/sounds.js); boot plays it.
 */
import { formatTime, ordinal, pauseLine, resultRows } from './format.js';
import { howtoHtml } from './howto.js';
import { cleanOptions, loadOptions, saveOptions, stepOption, cycleOption, choiceLabel, STEER_SENS } from './options.js';
import { controlRows, deviceView, pauseHint } from './labels.js';
import { PROFILES, nextProfile } from '../input/padmap.js';
import { createLobby } from './lobby.js';
import { actSound } from './sounds.js';
import { recapState, dotsHtml, runPageHtml, stampsPageHtml, PAGE_TITLES, RECAP_FIXTURE } from './recap.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const btn = (act, label, cls = '', extra = '') => '<button type="button" class="c-btn ' + cls + '" data-act="' + act + '"' + extra + '>' + label + '</button>';

const VOL_ROWS = [['volMaster', 'Volume'], ['volMusic', 'Music'], ['volSfx', 'Effects']];
/** The warming card's bar (pass 3): fill, a moving sheen and a turning ring. */
const LOAD_HTML = '<div class="c-load" role="progressbar" aria-label="Loading" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0">' +
  '<div class="c-load-bar"><i class="c-load-fill"></i><i class="c-load-sheen"></i></div><span class="c-load-ring" aria-hidden="true"></span></div>';
/** Display rows (polish 2): a button that cycles its values, with a short hint. */
const CYCLE_ROWS = [['frameCap', 'Frame cap', 'Lower keeps a slow PC steady'], ['renderScale', 'Render scale', 'Auto drops pixels to hold the frame rate'],
  ['quality', 'Quality', 'Auto follows your PC']];

export function createCards(card, { doc = globalThis.document, emit, devices = null, kit = null } = {}) {
  const body = card.querySelector('.card-body');
  const title = card.querySelector('.card-title');
  const note = card.querySelector('.card-note');
  const go = card.querySelector('#card-go') || card.querySelector('button');
  let extra = card.querySelector('.card-extra');
  if (!extra) { extra = doc.createElement('div'); extra.className = 'card-extra'; go.before(extra); }
  const old = card.querySelector('#card-restart');
  if (old) old.remove();

  let kind = 'start', data = {}, ready = false, waitNote = note?.textContent || '';
  const sound = (name) => { if (name) emit('sound', { name }); };
  let opts = loadOptions(), host = { gentle: false };
  const load = { p: 0 };
  function paintLoad() {
    if (kind !== 'start' || ready || card.hidden) return;
    note.textContent = waitNote;
    const bar = extra.querySelector('.c-load'), fill = extra.querySelector('.c-load-fill');
    if (fill) fill.style.transform = 'scaleX(' + load.p.toFixed(3) + ')';
    if (bar) bar.setAttribute('aria-valuenow', String(Math.round(load.p * 100)));
  }
  const stack = { options: null, controls: null, howto: null };
  // the how-to card: once a session before the first race, never in a dev bot run unless asked for
  let howtoSeen = false, leaving = 0;
  const howtoSkip = (() => {
    try { const q = new URLSearchParams(doc.defaultView.location.search); return q.get('howto') === '0' || (q.get('bot') === '1' && q.get('howto') !== '1'); }
    catch (e) { return false; }
  })();
  // the recap pages (Logbook): rc = { state, recap, at } on a results card; the loop comes from hud.setTrack
  let rc = null, trackLoop = null;
  const devFixture = (() => {
    try { const q = new URLSearchParams(doc.defaultView.location.search); return q.get('dev') === '1' && q.get('recap') === 'fixture'; }
    catch (e) { return false; }
  })();
  const howtoDue = () => !howtoSeen && !opts.howtoOff && !howtoSkip;
  const BACKS = ['options', 'controls', 'howto'];
  // B2-d: the online lobby draws in this card as kind 'lobby' (hud/lobby.js); its Back returns to the opener
  const lobby = createLobby({ doc, card, title, note, extra, emit, focusFirst: () => focusFirst(), exit: () => {
    const p = stack.lobby || { kind: 'menu', data: {} };
    stack.lobby = null; show(p.kind, p.data); emit('back', { from: 'lobby', to: p.kind });
  } });

  function show(k, d = {}) {
    kind = k; data = d || {};
    if (leaving) { clearTimeout(leaving); leaving = 0; }
    body.classList.remove('out');
    card.hidden = !k;
    delete card.dataset.page;
    if (k !== 'results') rc = null;
    if (!k) return;
    card.dataset.kind = k;
    card.classList.toggle('c-gentle', !!(host.gentle || opts.gentlePreview));
    body.classList.remove('in'); void body.offsetWidth; body.classList.add('in');
    go.hidden = !(k === 'start' || k === 'finish');
    extra.innerHTML = '';
    note.hidden = false;
    if (k === 'start') {
      title.textContent = data.title || 'Threshold';
      note.textContent = ready ? 'Press any key' : waitNote;
      go.textContent = 'Start'; go.disabled = !ready;
      // warming: a bar that fills with the real stages, a sheen and a ring that keep turning even while a
      // shader link holds the page (transform-only animations run on the compositor)
      if (!ready) { extra.innerHTML = LOAD_HTML; paintLoad(); }
    } else if (k === 'finish') {
      title.textContent = 'Finished';
      note.textContent = formatTime(data.timeS) + (data.best ? ', best lap ' + formatTime(data.best) : '');
      go.textContent = 'Again'; go.disabled = false;
    } else if (k === 'menu') {
      title.innerHTML = esc(data.title || 'Racing Thoughts') + '<small>A CC Labs game</small>';
      note.textContent = (data.track || 'Threshold') + ', ' + (data.laps || 3) + ' laps';
      extra.innerHTML = '<div class="c-doors">' +
        btn('race', '<b>Race</b><em>You and 7 bots</em>', 'c-door hot') +
        (data.online === false ? '' : btn('online', '<b>Race online</b><em>Tables and friends</em>', 'c-door')) +
        '</div><div class="c-links">' + btn('options', 'Options', 'c-link') + btn('controls', 'Controls', 'c-link') +
        btn('howto', 'How to play', 'c-link') + btn('back', 'Back', 'c-link') + '</div>' +
        (data.courses ? '<nav class="c-links" aria-label="Track">' + data.courses.map(c => '<a class="c-btn c-link" href="' + esc(c.url) + '"' + (c.selected ? ' aria-current="page"' : '') + '>' + esc(c.name) + '</a>').join('') + '</nav>' : '');
    } else if (k === 'pause') {
      title.textContent = 'Paused';
      note.textContent = pauseLine(data) || 'The race waits';
      extra.innerHTML = '<div class="c-list">' + btn('resume', 'Resume', 'hot') + btn('restart', 'Restart') +
        btn('options', 'Options') + btn('quit', 'Quit to menu', 'quiet') + '</div><p class="c-hint"></p>';
      hint();
    } else if (k === 'options') {
      if (typeof data.hostGentle === 'boolean') host.gentle = data.hostGentle;
      title.textContent = 'Options';
      note.hidden = true;
      extra.innerHTML = optionsHtml() + '<div class="c-list">' + btn('back', 'Back', 'quiet') + '</div>';
    } else if (k === 'controls') {
      title.textContent = 'Controls';
      note.hidden = true;
      extra.innerHTML = '<div class="c-keyswrap">' + keysHtml() + '</div><div class="c-list">' + btn('howto', 'How to play') + btn('back', 'Back', 'quiet') + '</div>';
    } else if (k === 'howto') {
      title.textContent = 'How to race';
      note.hidden = true;
      extra.innerHTML = '<div class="hw-wrap">' + howtoHtml(devices || {}, { off: opts.howtoOff }) + '</div>';
    } else if (k === 'lobby') {
      lobby.show(data);                              // draws and focuses itself
      return;
    } else if (k === 'results') {
      // Logbook recap (hud/recap.js): page 1 is the podium as before; a recap adds pages 2 and 3
      const recap = data.recap || (devFixture ? RECAP_FIXTURE : null);
      rc = recap ? { state: recapState(recap), recap, at: Date.now() } : null;
      paintResults();
      return;
    }
    focusFirst();
  }

  /** The results card on its current recap page. Again and Menu stay on every page; the last page has no Next. */
  function paintResults() {
    const page = rc ? rc.state.page : 'podium', multi = !!rc && rc.state.pages.length > 1, last = !rc || rc.state.last;
    const me = data.me || {}, o = ordinal(me.place || 0);
    card.dataset.page = page;
    note.textContent = formatTime(me.timeS) + (me.bestLapS > 0 ? ', best lap ' + formatTime(me.bestLapS) : '');
    let html;
    if (page === 'podium') {
      const flag = kit && kit.ok('flag-finish') ? '<img class="c-flag" src="' + kit.url('flag-finish') + '" alt="" draggable="false">' : '';
      title.innerHTML = flag + '<span class="c-place">' + o.n + '<sup>' + o.suf + '</sup></span>';
      const rows = resultRows(data, data.metas || null);
      html = '<ol class="c-rows">' + rows.map((r) =>
        '<li class="' + (r.me ? 'me' : '') + '"><span class="p">' + r.place + '</span><span class="n">' + esc(r.name) +
        '</span><span class="t' + (r.est ? ' est' : '') + '">' + (r.est ? '~' : '') + r.time + '</span></li>').join('') + '</ol>';
    } else {
      title.textContent = PAGE_TITLES[page] || '';
      html = page === 'run' ? runPageHtml(rc.recap, trackLoop) : stampsPageHtml(rc.recap);
    }
    if (multi) html = '<div class="rc-page in">' + html + '</div>' + dotsHtml(rc.state.pages, rc.state.i);
    extra.innerHTML = html + '<div class="c-list row">' + (last ? '' : btn('rc-next', 'Next', 'hot')) +
      btn('again', 'Again', last ? 'hot' : '') + btn('menu', 'Menu') + '</div>';
    if (page === 'stamps' && extra.querySelector('.rc-stamp')) sound('ready-on');
    focusFirst();
  }
  /** Next, Throttle, Enter or pad A: the page slides (fades under Gentle) out and the next one comes in. */
  function recapNext() {
    if (kind !== 'results' || card.hidden || !rc || rc.state.last || leaving) return;
    if (Date.now() - rc.at < 700) return;            // a throttle still held from the finish line never skips page 1
    sound('menu-move');
    const page = extra.querySelector('.rc-page'), gentle = card.classList.contains('c-gentle');
    if (page) { page.classList.remove('in'); page.classList.add('out'); }
    leaving = setTimeout(() => { leaving = 0; if (kind !== 'results' || !rc) return; rc.state.next(); rc.at = 0; paintResults(); }, gentle ? 260 : 170);
  }

  function optionsHtml() {
    const g = host.gentle;
    const tog = (key, label, on, hint, lock) => '<div class="c-opt"><span>' + label + '<small>' + hint + '</small></span>' +
      btn('toggle', lock ? 'On' : on ? 'On' : 'Off', 'c-tog' + (on || lock ? ' on' : ''), ' data-key="' + key + '" aria-pressed="' + !!(on || lock) + '"' + (lock ? ' disabled' : '')) + '</div>';
    return '<div class="c-opts">' +
      tog('gentlePreview', 'Gentle preview', opts.gentlePreview, g ? 'Set in CCP, stays on' : 'Softer effects, no flicker', g) +
      '<div class="c-opt"><span>Steer assist<small>Leans to the middle, drives hands off</small></span>' +
        btn('cycle', choiceLabel('steerAssist', opts.steerAssist), 'c-tog', ' data-key="steerAssist" aria-label="Steer assist"') + '</div>' +
      tog('assist', 'Soft rails', opts.assist, 'Open edges hold you in', false) +
      '<label class="c-opt"><span>Steering<small>How hard the wheel turns</small></span><input type="range" min="' + STEER_SENS.min + '" max="' +
        STEER_SENS.max + '" step="' + STEER_SENS.step + '" data-key="steerSens" value="' + opts.steerSens + '" aria-label="Steering"></label>' +
      VOL_ROWS.map(([key, label]) => '<label class="c-opt"><span>' + label + '</span><input type="range" min="0" max="1" step="0.05" data-key="' +
        key + '" value="' + opts[key] + '" aria-label="' + label + '"></label>').join('') +
      CYCLE_ROWS.map(([key, label, hint]) => '<div class="c-opt"><span>' + label + '<small>' + hint + '</small></span>' +
        btn('cycle', choiceLabel(key, opts[key]), 'c-tog', ' data-key="' + key + '" aria-label="' + label + '"') + '</div>').join('') +
      tog('shadows', 'Shadows', opts.shadows, 'Off is lighter on the GPU', false) +
      tog('showFps', 'Show fps', opts.showFps, 'A small counter in the corner', false) + '</div>';
  }

  /** The controls table for the device used last, plus the layout button for a non-standard pad. */
  function keysHtml() {
    const v = deviceView(devices || {}), pad = devices && devices.pad;
    const rows = controlRows(v.family, v.profile);
    const lay = pad && !pad.standard
      ? '<div class="c-opt c-layout"><span>Pad layout<small>Try the next one if the buttons feel wrong</small></span>' +
        btn('layout', esc((PROFILES[pad.profile] || PROFILES.standard).label), 'c-tog') + '</div>'
      : '';
    return '<table class="c-keys" data-last="' + v.last + '"><thead><tr><th></th><th class="k">Keys</th><th class="p">' + esc(v.padName) +
      '</th><th class="t">Touch</th></tr></thead><tbody>' +
      rows.map((r) => '<tr><th>' + r[0] + '</th><td class="k">' + esc(r[1]) + '</td><td class="p">' + esc(r[2]) + '</td><td class="t">' + esc(r[3]) + '</td></tr>').join('') +
      '</tbody></table>' + lay;
  }
  function hint() {
    const h = extra.querySelector('.c-hint');
    if (!h) return;
    const text = pauseHint(devices || {});
    h.textContent = text; h.hidden = !text;
  }
  /** The device in use changed: rename the buttons on the open card, keeping focus where it was. */
  function refreshDevice() {
    if (card.hidden) return;
    if (kind === 'pause') { hint(); return; }
    const hw = kind === 'howto' && extra.querySelector('.hw-wrap');
    if (hw) { hw.innerHTML = howtoHtml(devices || {}, { off: opts.howtoOff }); focusFirst(); return; }
    const wrap = kind === 'controls' && extra.querySelector('.c-keyswrap');
    if (!wrap) return;
    const focusAct = wrap.contains(doc.activeElement) && doc.activeElement.dataset ? doc.activeElement.dataset.act : null;
    wrap.innerHTML = keysHtml();
    const back = focusAct && wrap.querySelector('[data-act="' + focusAct + '"]');
    if (back) back.focus?.({ preventScroll: true });
    else if (focusAct) focusFirst();
  }

  function setOption(key, value) {
    opts = cleanOptions({ ...opts, [key]: value });
    saveOptions(opts);
    emit('option', { key, value: opts[key], options: { ...opts } });
  }

  const focusables = () => [...card.querySelectorAll('button, input, a[href]')].filter((e) => !e.hidden && !e.disabled && e.offsetParent !== null);
  function focusFirst() {
    if (kind === 'start' && go.disabled) return;
    const list = focusables();
    const first = list.find((e) => e.classList.contains('hot')) || list[0];
    first?.focus?.({ preventScroll: true });
  }

  function act(el) {
    if (!el) return;
    const a = el.dataset.act;
    if (a.startsWith('lb-')) { lobby.act(el); return; }   // the lobby picks its own sound (it knows a refusal)
    if (a === 'rc-next') { if (rc) rc.at = 0; recapNext(); return; }   // a click is never a held throttle
    if (!(a === 'back' && BACKS.includes(kind))) sound(actSound(a));   // back() plays its own
    if (a === 'toggle') {
      const key = el.dataset.key, on = !(opts[key]);
      setOption(key, on);
      el.textContent = on ? 'On' : 'Off'; el.classList.toggle('on', on); el.setAttribute('aria-pressed', String(on));
      return;
    }
    if (a === 'cycle') { cycle(el, 1); return; }
    if (a === 'race' && kind === 'menu' && howtoDue()) { openHowto({ then: 'race' }); return; }
    if (a === 'howto') { openHowto({}); return; }
    if (a === 'howto-go') { howtoGo(); return; }
    if (a === 'howto-off') {
      const on = !opts.howtoOff;
      setOption('howtoOff', on);
      el.classList.toggle('on', on); el.setAttribute('aria-pressed', String(on));
      return;
    }
    if (a === 'online') {
      if (data.online === false) return;
      if (kind !== 'lobby') stack.lobby = { kind, data };
      show('lobby', { view: 'home' });
      emit('online', { lobby });
      return;
    }
    if (a === 'layout') {
      const pad = devices && devices.pad;
      if (pad) devices.setLayout(pad.id, nextProfile(pad.profile));   // devices tells the hud, which refreshes this card
      return;
    }
    if (a === 'options' || a === 'controls') {
      stack[a] = { kind, data };
      show(a, a === 'options' ? { from: kind, hostGentle: host.gentle } : { from: kind });
      emit(a, { from: stack[a].kind });
      return;
    }
    if (a === 'back' && BACKS.includes(kind)) { back(); return; }
    emit(a, a === 'back' ? { from: kind } : {});
  }

  /** The how-to card over the card that opened it (then: 'race' starts the race when it is dismissed). */
  function openHowto(d) {
    stack.howto = { kind, data };
    show('howto', { ...d, from: kind });
    emit('howto', { from: stack.howto.kind });
  }
  /** Throttle or Go: the card leaves (its OUT), then the race starts or the opener comes back. */
  function howtoGo() {
    if (kind !== 'howto' || card.hidden || leaving) return;
    howtoSeen = true;
    const then = data.then, gentle = card.classList.contains('c-gentle');
    body.classList.remove('in'); body.classList.add('out');
    leaving = setTimeout(() => {
      leaving = 0;                                   // show() takes the out class off the next card
      if (kind !== 'howto') return;
      if (then === 'race') { const prev = stack.howto; stack.howto = null; kind = prev ? prev.kind : 'menu'; emit('race', {}); }
      else back();
    }, gentle ? 320 : 200);
  }

  function cycle(el, dir) {
    const key = el.dataset.key;
    setOption(key, cycleOption(key, opts[key], dir));
    el.textContent = choiceLabel(key, opts[key]);
  }

  function back() {
    sound('menu-back');
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
    if (kind === 'lobby' && lobby.nav(dir)) return;   // back, and left / right on the lobby's form rows
    if (dir === 'throttle') { if (kind === 'howto' && !leaving) { sound('menu-ok'); howtoGo(); } else if (kind === 'results') recapNext(); return; }   // boot.js: a throttle press on a card
    if (dir === 'back') { if (BACKS.includes(kind)) back(); return; }
    const list = focusables();
    if (!list.length) return;
    const cur = doc.activeElement, i = list.indexOf(cur);
    if (dir === 'ok') {
      if (i < 0) { (list.find((e) => e.classList.contains('hot')) || list[0]).click?.(); return; }
      if (cur.tagName === 'BUTTON') cur.click();
      return;
    }
    if ((dir === 'left' || dir === 'right') && cur && cur.type === 'range') {
      const was = cur.value;
      cur.value = String(stepOption(cur.dataset.key, Number(cur.value), dir === 'left' ? -1 : 1));
      sound(cur.value === was ? 'menu-deny' : 'menu-move');   // a slider at its end says no
      setOption(cur.dataset.key, Number(cur.value));
      return;
    }
    if ((dir === 'left' || dir === 'right') && cur && cur.dataset && cur.dataset.act === 'cycle') { sound('menu-toggle'); cycle(cur, dir === 'left' ? -1 : 1); return; }
    const step = dir === 'up' || dir === 'left' ? -1 : 1;
    const next = list[i < 0 ? 0 : (i + step + list.length) % list.length];
    if (next !== cur) sound('menu-move');
    next.focus?.({ preventScroll: true });
    if (kind === 'options') next.scrollIntoView?.({ block: 'nearest' });   // the options list scrolls in the card
  }

  const onKeydown = (e) => {
    if (card.hidden || e.altKey || e.ctrlKey || e.metaKey) return;
    if(kind==='menu'&&doc.body.classList.contains('garage-open'))return;
    if (e.key === 'Escape') { if (BACKS.includes(kind)) back(); else if (kind === 'lobby') lobby.back(); return; }   // never swallowed
    if (kind === 'start') {
      if (e.repeat || e.key === 'Shift' || e.key === 'Tab' || e.key === 'CapsLock') return;
      e.preventDefault(); if (ready) { sound('menu-ok'); emit('start', {}); } else sound('menu-deny'); return;
    }
    if (kind === 'finish') { if (e.key === 'Enter' && !e.repeat) { e.preventDefault(); emit('restart', {}); } return; }
    const onRange = doc.activeElement && doc.activeElement.type === 'range';
    const dir = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' }[e.key];
    if (dir) {
      if (onRange && (dir === 'left' || dir === 'right')) return;   // the slider moves itself
      e.preventDefault(); nav(dir); return;
    }
    if ((e.key === 'Enter' || e.key === ' ') && !card.contains(doc.activeElement)) { e.preventDefault(); if (!e.repeat) nav('ok'); }
  };
  doc.addEventListener('keydown', onKeydown);

  return {
    dispose(){clearTimeout(leaving);doc.removeEventListener('keydown',onKeydown);lobby.dispose();},
    show, nav, refreshDevice,
    menuAction(name){if(kind==='menu'&&['race','online','options','controls','howto','back'].includes(name))act({dataset:{act:name}});},
    get kind() { return card.hidden ? null : kind; },
    /** The online lobby (hud/lobby.js): boot calls lobby.use({ api, connect, identity, friends }) or configureLobby. */
    get lobby() { return lobby; },
    get options() { return { ...opts }; },
    setReady(r, n) { ready = !!r; if (n) waitNote = n; if (kind === 'start' && !card.hidden) show('start', data); },
    /** The warming card's stage line and bar (p 0..1), updated in place: no re-show, no card-in replay. */
    setProgress(stage, p) {
      if (stage) waitNote = String(stage);
      load.p = Math.max(load.p, Math.min(1, Math.max(0, Number(p) || 0)));   // the bar never runs backwards
      paintLoad();
    },
    setHostGentle(on) { host.gentle = !!on; },
    /** The course loop for the recap sketch ({ xz } from boot's centreline, evenly spaced along s). */
    setTrack(t) { trackLoop = t && t.xz ? t : null; },
  };
}
