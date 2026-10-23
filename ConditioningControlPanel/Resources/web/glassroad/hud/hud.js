/* hud/hud.js - the race HUD and the cards (CONTRACT-B1.md section 3, hud).
 *
 * Four corners, nothing in the middle: lap and time top left, the round item slot top centre,
 * the minimap top right, the place numeral bottom left (phone: top right, clear of the thumbs),
 * speed bottom right, drift pips under the kart. The credit slot stays empty until media shows.
 * Toasts are rare and short. Cards live in hud/cards.js. Writes touch the DOM only on a change.
 * Phase A API kept: setCard, setReady, update, onStart, onResume, onRestart, credit, dispose.
 * Polish 1: Astra's SVG kit (hud/kit.js) dresses the place numeral, item slot, drift pips, lap
 * divider, minimap frame and the countdown flag once it loads; the DOM look is the fallback and
 * all text stays in the DOM. A short note says when a controller connects or leaves.
 * Polish 5 (J2): each bell stamps its digit in big and shrinks it out over one beat (IN and OUT); Go bursts at
 * the player's kart (update's kartAt, view fractions from boot) with two rings, then lifts away. Gentle and
 * reduced motion keep only the fades.
 * Polish 6 (JUICE): the item slot is a solid ink socket with a paper rim and the bubble sits on an opaque
 * core, so the held item reads over any scene and over a Flash; its states (empty, rolling, ready, used) and
 * the moment each change plays come from hud/slotMath.js (catch, land, use, drop). A pickup bubble answers
 * with a ring at the kart and a speed punch; a drift tier pops its pip in the tier colour and the release
 * flashes the pips out. Gentle and reduced motion keep the fades only.
 */
import { formatTime, ordinal, iconOf, itemName, rollFrame, racerLabels, ICON_IDS } from './format.js';
import { slotState, slotMoment, gentleMoment, pipMoment, USED_S, PIP_OUT_S } from './slotMath.js';
import { createMinimap } from './minimap.js';
import { createCards } from './cards.js';
import { createKit, placeArt } from './kit.js';
import { devices as sharedDevices } from '../input/device.js';

export { formatTime };

const NAMES = ['race', 'online', 'options', 'controls', 'back', 'resume', 'restart', 'quit', 'again', 'menu', 'option', 'item', 'start'];
const TOAST_S = 1.6, TOAST_GAP_S = 0.6;

/** @returns {Hud} */
export function createHud({ root, card }, feel, bus, { doc = globalThis.document, now = () => performance.now(), devices = sharedDevices } = {}) {
  root.classList.add('hud');
  root.dataset.screen = 'race';
  root.innerHTML =
    '<div class="hud-race">' +
      '<div class="hud-tl"><span class="hud-lap"></span><i class="hud-div" aria-hidden="true"></i><span class="hud-time"></span><span class="hud-best"></span></div>' +
      '<button type="button" class="hud-item empty" data-state="empty" tabindex="-1" aria-label="Item"><i class="hud-item-core"></i><i class="hud-item-ring"></i>' +
        '<img class="hud-item-icon" alt="" draggable="false" hidden><img class="hud-item-ghost" alt="" draggable="false" hidden><i class="hud-item-flare"></i></button>' +
      '<div class="hud-pop" aria-hidden="true"><i></i><i class="r2"></i></div>' +
      '<div class="hud-mapbox"><canvas class="hud-map" aria-hidden="true"></canvas></div>' +
      '<div class="hud-place" aria-live="polite" hidden><img class="hud-place-art" alt="" draggable="false" hidden><b></b><sup></sup><small></small></div>' +
      '<span class="hud-pips"><i></i><i></i><i></i></span>' +
      '<span class="hud-speed"><b>0</b> km/h</span>' +
      '<div class="hud-toast" role="status"></div>' +
    '</div>' +
    '<div class="hud-count" aria-hidden="true"><img class="hud-count-flag" alt="" draggable="false" hidden><span></span></div>' +
    '<div class="hud-go" aria-hidden="true"><i class="hud-go-ring"></i><i class="hud-go-ring r2"></i><span>Go</span></div>' +
    '<div class="hud-note" role="status"></div>' +
    '<div class="hud-credit" data-slot="credit"></div>';
  const $ = (sel) => root.querySelector(sel);
  const el = {
    lap: $('.hud-lap'), time: $('.hud-time'), best: $('.hud-best'), count: $('.hud-count'), countN: $('.hud-count span'),
    flag: $('.hud-count-flag'), speed: $('.hud-speed b'), go: $('.hud-go'),
    pips: [...root.querySelectorAll('.hud-pips i')], pipRow: $('.hud-pips'), item: $('.hud-item'), icon: $('.hud-item-icon'), ghost: $('.hud-item-ghost'),
    pop: $('.hud-pop'), speedBox: $('.hud-speed'),
    place: $('.hud-place'), placeArt: $('.hud-place-art'), placeN: $('.hud-place b'), placeSuf: $('.hud-place sup'), placeOf: $('.hud-place small'),
    toast: $('.hud-toast'), note: $('.hud-note'),
  };
  const map = createMinimap($('.hud-map'), { doc });
  // warm the icon cache so the roll never waits on a load
  const warm = ICON_IDS.map((id) => { const im = doc.createElement('img'); im.src = iconOf(id); return im; });
  const kit = createKit({ doc, onGroup: (g) => { root.classList.add('kit-' + g); card.classList.add('kit-' + g); } });

  const listeners = Object.fromEntries(NAMES.map((n) => [n, []]));
  const fns = { start: [], resume: [], restart: [] };
  /** One door for every action: on() listeners, else the Phase A onStart / onResume / onRestart. */
  function fire(name, payload = {}) {
    const list = (listeners[name] || []).length ? listeners[name] : fns[name] || [];
    for (const fn of list.slice()) { try { fn(payload); } catch (e) { console.warn('[hud]', name, e); } }
  }
  const cards = createCards(card, { doc, emit: fire, devices, kit });
  let noteTimer = 0;
  function note(text) {
    el.note.textContent = text;
    el.note.classList.add('on');
    clearTimeout(noteTimer);
    noteTimer = setTimeout(() => el.note.classList.remove('on'), 2400);
  }
  const offDev = devices.on((type, info) => {
    if (type === 'connect') note('Controller connected');
    else if (type === 'lost') note('Controller disconnected');
    cards.refreshDevice();
  });
  el.item.addEventListener('pointerdown', (e) => { e.preventDefault(); e.stopPropagation(); fire('item', {}); });

  let labels = [], metas = null, laps = 1, leader = -1, goUntil = 0, countN = 0;
  let toastUntil = 0, toastAt = -1e9, pending = null;
  const last = {};
  const put = (key, node, text) => { if (last[key] !== text) { last[key] = text; node.textContent = text; } };
  const gentleOn = () => !!feel.get('gentle.on');
  const retrigger = (node, cls) => { node.classList.remove('pop', 'pop-soft', 'land'); void node.offsetWidth; node.classList.add(cls); };
  // a one-shot class (polish 6): removed and re-added so the same moment can play twice in a row
  const MOMENTS = ['m-catch', 'm-land', 'm-use', 'm-drop', 'm-soft', 'm-use-soft'];
  const play = (node, cls, all = [cls]) => { node.classList.remove(...all); void node.offsetWidth; node.classList.add(cls); };
  let usedUntil = 0, usedId = null, pipTier = 0, pipsOutUntil = 0;

  function toast(text) {
    const t = now() / 1000;
    if (t - toastAt < TOAST_GAP_S && t < toastUntil) { pending = text; return; }
    toastAt = t; toastUntil = t + TOAST_S; pending = null;
    el.toast.textContent = text;
    el.toast.classList.add('on');
  }
  const nameOf = (i) => labels[i] || (i === 0 ? 'You' : 'Bot ' + i);

  const offs = [
    // one stamp per bell, one beat long (in, hold, shrink out); Gentle swaps the keyframes for a plain fade
    bus.on('countdown', ({ n }) => {
      countN = n;
      el.count.style.setProperty('--beat', (60 / feel.get('clock.bpm')).toFixed(3) + 's');
      put('count', el.countN, n > 0 ? String(n) : '');
      el.count.classList.remove('stamp'); void el.count.offsetWidth; el.count.classList.add('stamp');
    }),
    bus.on('go', () => {
      countN = 0;
      const s = Math.max(0.3, feel.get('juice.goReleaseS') * (gentleOn() ? 1.6 : 1.2));
      goUntil = now() + s * 1000;
      el.go.style.setProperty('--go-s', s.toFixed(3) + 's');
      el.go.classList.remove('on'); void el.go.offsetWidth; el.go.classList.add('on');
    }),
    bus.on('drift-tier', ({ tier }) => pips(tier)),
    // the release flashes the lit pips out (IN: a swell, OUT: a fade), then they go dark; a cancel just fades
    bus.on('drift-release', ({ tier = 0 } = {}) => {
      if (pipMoment(pipTier, 0, { released: true }).out || tier > 0) {
        play(el.pipRow, gentleOn() ? 'out-soft' : 'out', ['out', 'out-soft']);
        pipsOutUntil = now() + PIP_OUT_S * 1000;
      } else pips(0);
    }),
    bus.on('drift-cancel', () => { pipsOutUntil = 0; el.pipRow.classList.remove('out', 'out-soft'); pips(0); }),
    // the player used the held item: the slot shows `used` for USED_S (the icon shrinks out, a ring flares)
    bus.on('item-fire', ({ racer }) => { if (racer === 0) { usedUntil = now() / 1000 + USED_S; usedId = last.icon || null; } }),
    // a pickup bubble: a ring at the kart and a speed punch (IN fast, OUT over the keyframes)
    bus.on('bubble-pop', ({ racer }) => {
      if (racer !== 0) return;
      play(el.pop, gentleOn() ? 'on-soft' : 'on', ['on', 'on-soft']);
      play(el.speedBox, gentleOn() ? 'punch-soft' : 'punch', ['punch', 'punch-soft']);
    }),
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
    const c = colours(), t = Math.max(0, Math.min(3, tier | 0));
    const m = pipMoment(pipTier, t);
    pipTier = t;
    if (t > 0) { pipsOutUntil = 0; el.pipRow.classList.remove('out', 'out-soft'); }
    // every lit pip wears the CURRENT tier colour (the spark colour), so the row reads blue, orange, pink
    el.pipRow.style.setProperty('--tier', c[t - 1] || '#fff');
    el.pips.forEach((p, i) => { p.classList.toggle('on', i < t); p.style.setProperty('--pip', c[i] || '#fff'); });
    if (m.pop >= 0 && el.pips[m.pop]) play(el.pips[m.pop], gentleOn() ? 'pop-soft' : 'pop', ['pop', 'pop-soft']);
  }

  function slot(item, gentle) {
    let frame = null;
    if (item && item.rolling) {
      const rollS = item.rollS > 0 ? item.rollS : feel.get('items.rollS') || 1;
      frame = rollFrame(item.rollT || 0, rollS, item.id, { gentle });
    }
    const { state, id: shown } = slotState(item, frame, { nowS: now() / 1000, usedUntil, usedId });
    const id = state === 'used' ? null : shown;
    if (state !== last.slot) {
      const from = last.slot || 'empty';
      last.slot = state;
      el.item.dataset.state = state;
      el.item.classList.toggle('empty', state === 'empty');
      el.item.classList.toggle('rolling', state === 'rolling');
      el.item.classList.toggle('held', state === 'ready');
      el.item.classList.toggle('used', state === 'used');
      // the used item's icon stays on a ghost that shrinks out under the flare
      if (state === 'used' && shown) { el.ghost.src = iconOf(shown); el.ghost.hidden = false; } else el.ghost.hidden = true;
      const cls = gentleMoment(slotMoment(from, state), gentle);
      if (cls) play(el.item, cls, MOMENTS);
    }
    if (id !== last.icon) {
      last.icon = id;
      if (id) el.icon.src = iconOf(id);
      el.icon.hidden = !id;
      el.item.setAttribute('aria-label', state === 'ready' && id ? 'Use ' + itemName(id) : 'Item');
    }
  }

  function setPlace(place, count) {
    if (!(place > 0)) { el.place.hidden = true; return; }
    el.place.hidden = false;
    const art = kit.group('place') ? placeArt(place) : null;
    if (art !== last.art) {
      last.art = art;
      if (art) el.placeArt.src = kit.url(art);
      el.placeArt.hidden = !art;
      el.place.classList.toggle('art', !!art);
    }
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
    place = 0, count = 0, item = null, map: xz = null, finished = false, bestLapS = 0, kartAt = null } = {}) {
    laps = L;
    const gentle = gentleOn();
    root.classList.toggle('gentle', gentle);
    put('lap', el.lap, 'Lap ' + Math.min(lap + 1, L) + '/' + L);
    put('time', el.time, formatTime(timeS));
    put('best', el.best, bestLapS > 0 ? 'best ' + formatTime(bestLapS) : '');
    put('speed', el.speed, String(Math.round(Math.max(0, speedPct) * feel.get('speed.topMps') * 3.6)));
    const n = countdown || countN;
    const countText = n > 0 ? String(n) : '';
    put('count', el.countN, countText);
    // the Go burst sits on the player's kart (polish 5): view fractions, written only on a change
    if (kartAt) {
      const kx = (Math.round(kartAt.x * 400) / 4).toFixed(2) + '%', ky = (Math.round(kartAt.y * 400) / 4).toFixed(2) + '%';
      if (last.kx !== kx || last.ky !== ky) { last.kx = kx; last.ky = ky; el.go.style.left = kx; el.go.style.top = ky; }
    }
    if (goUntil && now() >= goUntil) { goUntil = 0; el.go.classList.remove('on'); }
    // the bubble ring follows the kart the same way (last.kx / ky are the Go burst's fractions)
    if (kartAt && (last.px !== last.kx || last.py !== last.ky)) { last.px = last.kx; last.py = last.ky; el.pop.style.left = last.kx; el.pop.style.top = last.ky; }
    if (pipsOutUntil && now() >= pipsOutUntil) { pipsOutUntil = 0; el.pipRow.classList.remove('out', 'out-soft'); pips(0); }
    const flagOn = !!countText && kit.ok('flag-start');
    if (flagOn !== last.flag) { last.flag = flagOn; if (flagOn && !el.flag.src) el.flag.src = kit.url('flag-start'); el.flag.hidden = !flagOn; }
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
    /** The warming card's stage line and bar, p 0..1 (pass 3: boot names each stage). */
    setLoading: (stage, p) => cards.setProgress(stage, p),
    on(name, fn) { (listeners[name] || (listeners[name] = [])).push(fn); return () => { const l = listeners[name], i = l.indexOf(fn); if (i >= 0) l.splice(i, 1); }; },
    onStart: (fn) => fns.start.push(fn), onResume: (fn) => fns.resume.push(fn), onRestart: (fn) => fns.restart.push(fn),
    setRacers(meta) { metas = meta || null; labels = racerLabels(meta || []); },
    setTrack(t) { map.setTrack(t || {}); },
    /** The host's Gentle switch: the options card shows Gentle preview locked on. */
    setHostGentle(on) { cards.setHostGentle(on); },
    /** The saved options, for boot to apply at load. */
    get options() { return cards.options; },
    get card() { return cards.kind; },
    /** The online lobby handle (hud/lobby.js), for boot's race link claim. */
    get lobby() { return cards.lobby; },
    toast,
    item: el.item,
    credit: root.querySelector('.hud-credit'),
    /** A short note at the bottom, on any screen (the controller notes use it). */
    note,
    dispose() { offs.forEach((off) => off()); offDev(); clearTimeout(noteTimer); kit.dispose(); warm.length = 0; },
  };
}
