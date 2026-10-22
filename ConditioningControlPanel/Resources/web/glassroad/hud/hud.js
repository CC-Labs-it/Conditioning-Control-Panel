/* hud/hud.js - the race HUD and the cards (CONTRACT-B1.md section 3, hud).
 *
 * Four corners, nothing in the middle: lap and time top left, the round item slot top centre,
 * the minimap top right, the place numeral bottom left (phone: top right, clear of the thumbs),
 * speed bottom right, drift pips under the kart. The credit slot stays empty until media shows.
 * Toasts are rare and short. Cards live in hud/cards.js. Writes touch the DOM only on a change.
 * Phase A API kept: setCard, setReady, update, onStart, onResume, onRestart, credit, dispose.
 */
import { formatTime, ordinal, iconOf, itemName, rollFrame, racerLabels, ICON_IDS } from './format.js';
import { createMinimap } from './minimap.js';
import { createCards } from './cards.js';

export { formatTime };

const NAMES = ['race', 'online', 'options', 'controls', 'back', 'resume', 'restart', 'quit', 'again', 'menu', 'option', 'item', 'start'];
const TOAST_S = 1.6, TOAST_GAP_S = 0.6;

/** @returns {Hud} */
export function createHud({ root, card }, feel, bus, { doc = globalThis.document, now = () => performance.now() } = {}) {
  root.classList.add('hud');
  root.dataset.screen = 'race';
  root.innerHTML =
    '<div class="hud-race">' +
      '<div class="hud-tl"><span class="hud-lap"></span><span class="hud-time"></span><span class="hud-best"></span></div>' +
      '<button type="button" class="hud-item empty" tabindex="-1" aria-label="Item"><i class="hud-item-ring"></i><img alt="" draggable="false" hidden></button>' +
      '<canvas class="hud-map" aria-hidden="true"></canvas>' +
      '<div class="hud-place" aria-live="polite" hidden><b></b><sup></sup><small></small></div>' +
      '<span class="hud-pips"><i></i><i></i><i></i></span>' +
      '<span class="hud-speed"><b>0</b> km/h</span>' +
      '<div class="hud-toast" role="status"></div>' +
    '</div>' +
    '<div class="hud-count" aria-hidden="true"></div>' +
    '<div class="hud-credit" data-slot="credit"></div>';
  const $ = (sel) => root.querySelector(sel);
  const el = {
    lap: $('.hud-lap'), time: $('.hud-time'), best: $('.hud-best'), count: $('.hud-count'), speed: $('.hud-speed b'),
    pips: [...root.querySelectorAll('.hud-pips i')], item: $('.hud-item'), icon: $('.hud-item img'),
    place: $('.hud-place'), placeN: $('.hud-place b'), placeSuf: $('.hud-place sup'), placeOf: $('.hud-place small'),
    toast: $('.hud-toast'),
  };
  const map = createMinimap($('.hud-map'), { doc });
  // warm the icon cache so the roll never waits on a load
  const warm = ICON_IDS.map((id) => { const im = doc.createElement('img'); im.src = iconOf(id); return im; });

  const listeners = Object.fromEntries(NAMES.map((n) => [n, []]));
  const fns = { start: [], resume: [], restart: [] };
  /** One door for every action: on() listeners, else the Phase A onStart / onResume / onRestart. */
  function fire(name, payload = {}) {
    const list = (listeners[name] || []).length ? listeners[name] : fns[name] || [];
    for (const fn of list.slice()) { try { fn(payload); } catch (e) { console.warn('[hud]', name, e); } }
  }
  const cards = createCards(card, { doc, emit: fire });
  el.item.addEventListener('pointerdown', (e) => { e.preventDefault(); e.stopPropagation(); fire('item', {}); });

  let labels = [], metas = null, laps = 1, leader = -1, goUntil = 0, countN = 0;
  let toastUntil = 0, toastAt = -1e9, pending = null;
  const last = {};
  const put = (key, node, text) => { if (last[key] !== text) { last[key] = text; node.textContent = text; } };
  const gentleOn = () => !!feel.get('gentle.on');
  const retrigger = (node, cls) => { node.classList.remove('pop', 'pop-soft', 'land'); void node.offsetWidth; node.classList.add(cls); };

  function toast(text) {
    const t = now() / 1000;
    if (t - toastAt < TOAST_GAP_S && t < toastUntil) { pending = text; return; }
    toastAt = t; toastUntil = t + TOAST_S; pending = null;
    el.toast.textContent = text;
    el.toast.classList.add('on');
  }
  const nameOf = (i) => labels[i] || (i === 0 ? 'You' : 'Bot ' + i);

  const offs = [
    bus.on('countdown', ({ n }) => {
      countN = n;
      if (!gentleOn()) { el.count.classList.remove('pulse'); void el.count.offsetWidth; el.count.classList.add('pulse'); }
    }),
    bus.on('go', () => { countN = 0; goUntil = now() + feel.get('juice.goReleaseS') * 1000; }),
    bus.on('drift-tier', ({ tier }) => pips(tier)),
    bus.on('drift-release', () => pips(0)),
    bus.on('drift-cancel', () => pips(0)),
    bus.on('lap', ({ lap }) => { if (lap > 0 && lap < laps) toast(lap === laps - 1 ? 'Final lap' : 'Lap ' + (lap + 1)); }),
    bus.on('slipstream', ({ racer, state }) => { if (racer === 0 && state === 'fire') toast('Slipstream'); }),
    bus.on('race-standings', ({ order }) => { leader = order && order.length ? order[0] : -1; }),
    bus.on('item-refused', ({ racer }) => { if (racer === 0) toast('Not by the edge'); }),
    bus.on('item-hit', ({ victim, by, kind, verdict }) => {
      if (victim === 0 && verdict === 'hit') toast(itemName(kind) + ' from ' + nameOf(by));
      else if (victim === 0 && verdict === 'glass') toast('Glass held');
      else if (by === 0 && victim !== 0 && verdict === 'hit') toast(itemName(kind) + ' on ' + nameOf(victim));
    }),
  ];
  const colours = () => feel.get('juice.sparkColours');
  function pips(tier) {
    const c = colours();
    el.pips.forEach((p, i) => { p.classList.toggle('on', i < tier); p.style.setProperty('--pip', c[i] || '#fff'); });
  }

  function slot(item, gentle) {
    let id = null, state = 'empty';
    if (item && (item.id || item.rolling)) {
      if (item.rolling) {
        const rollS = item.rollS > 0 ? item.rollS : feel.get('items.rollS') || 1;
        const f = rollFrame(item.rollT || 0, rollS, item.id, { gentle });
        id = f.id; state = f.tick < 0 ? 'held' : 'rolling';
      } else { id = item.id; state = 'held'; }
    }
    if (state !== last.slot) {
      const wasRolling = last.slot === 'rolling';
      last.slot = state;
      el.item.classList.toggle('empty', state === 'empty');
      el.item.classList.toggle('rolling', state === 'rolling');
      el.item.classList.toggle('held', state === 'held');
      if (wasRolling && state === 'held') retrigger(el.item, gentle ? 'pop-soft' : 'land');
    }
    if (id !== last.icon) {
      last.icon = id;
      if (id) el.icon.src = iconOf(id);
      el.icon.hidden = !id;
      el.item.setAttribute('aria-label', state === 'held' && id ? 'Use ' + itemName(id) : 'Item');
    }
  }

  function setPlace(place, count) {
    if (!(place > 0)) { el.place.hidden = true; return; }
    el.place.hidden = false;
    if (place !== last.place) {
      const moved = last.place > 0, up = moved && place < last.place;
      last.place = place;
      const o = ordinal(place);
      el.placeN.textContent = o.n; el.placeSuf.textContent = o.suf;
      el.place.classList.toggle('first', place === 1);
      if (moved) { el.place.classList.toggle('up', up); retrigger(el.place, gentleOn() ? 'pop-soft' : 'pop'); }
    }
    put('of', el.placeOf, count > 0 ? '/' + count : '');
  }

  /**
   * Phase A fields plus B1: place, count, item { id, rolling, rollT, rollS } | null, map (x, z per
   * racer), finished, bestLapS (optional).
   */
  function update({ lap = 0, laps: L = 1, timeS = 0, speedPct = 0, countdown = null, paused = false,
    place = 0, count = 0, item = null, map: xz = null, finished = false, bestLapS = 0 } = {}) {
    laps = L;
    const gentle = gentleOn();
    root.classList.toggle('gentle', gentle);
    put('lap', el.lap, 'Lap ' + Math.min(lap + 1, L) + '/' + L);
    put('time', el.time, formatTime(timeS));
    put('best', el.best, bestLapS > 0 ? 'best ' + formatTime(bestLapS) : '');
    put('speed', el.speed, String(Math.round(Math.max(0, speedPct) * feel.get('speed.topMps') * 3.6)));
    const n = countdown || countN;
    put('count', el.count, n > 0 ? String(n) : now() < goUntil ? 'Go' : '');
    root.classList.toggle('paused', !!paused);
    root.classList.toggle('finished', !!finished);
    setPlace(place, count);
    slot(finished ? null : item, gentle);
    if (xz && root.dataset.screen === 'race') map.draw(xz, leader);
    const t = now() / 1000;
    if (t >= toastUntil && el.toast.classList.contains('on')) {
      el.toast.classList.remove('on');
      if (pending) { const p = pending; pending = null; toast(p); }
    }
  }

  function setCard(kind, data = {}) {
    if (kind === 'results') data = { ...data, metas };
    if (kind === 'menu') root.dataset.screen = 'menu';
    else if (kind === 'results') root.dataset.screen = 'results';
    else if (!kind || kind === 'start') root.dataset.screen = 'race';
    if (kind === 'menu' || kind === 'start') { last.place = 0; leader = -1; el.place.hidden = true; }
    cards.show(kind, data);
  }

  return {
    setCard, update, nav: cards.nav,
    setReady: (r, n) => cards.setReady(r, n),
    on(name, fn) { (listeners[name] || (listeners[name] = [])).push(fn); return () => { const l = listeners[name], i = l.indexOf(fn); if (i >= 0) l.splice(i, 1); }; },
    onStart: (fn) => fns.start.push(fn), onResume: (fn) => fns.resume.push(fn), onRestart: (fn) => fns.restart.push(fn),
    setRacers(meta) { metas = meta || null; labels = racerLabels(meta || []); },
    setTrack(t) { map.setTrack(t || {}); },
    /** The host's Gentle switch: the options card shows Gentle preview locked on. */
    setHostGentle(on) { cards.setHostGentle(on); },
    /** The saved options, for boot to apply at load. */
    get options() { return cards.options; },
    get card() { return cards.kind; },
    toast,
    item: el.item,
    credit: root.querySelector('.hud-credit'),
    dispose() { offs.forEach((off) => off()); warm.length = 0; },
  };
}
