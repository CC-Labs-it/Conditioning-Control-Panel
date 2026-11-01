/* ============================================================================
 * ui/app.js - the plain game: counter, bubble, shop window, pause card, save
 * loop. This is the "ugly start" every run begins from; widgets (ui/widgets/*)
 * add the layers an item buys, fx/ adds the juice. Nothing in here computes a
 * number: sim/ does, this file draws what sim returns.
 * ==========================================================================*/

import { ITEMS, BY_ID, ENDING_ID } from '../data/items.js';
import * as E from '../sim/economy.js';
import { fuzzLabel } from '../sim/format.js';
import { load, store, forget, SAVE_EVERY_MS } from '../sim/save.js';
import { PLUCKS } from '../audio/kit.js';

export function createApp({ doc = document, storage = globalThis.localStorage, audio, spiral, bridge, pace = 1, now = () => performance.now(), onBuy, onClick, onEnd } = {}) {
  const $ = (id) => doc.getElementById(id);
  const el = { fuzz: $('fuzz'), fps: $('fps'), bubble: $('bubble'), shop: $('shop'), fx: $('fx'), pause: $('pause'), wake: $('wake'), resume: $('resume'), mute: $('mute'), safe: $('photosafe'), exportBtn: $('export'), exportBox: $('exportbox'), forget: $('forget') };

  const saved = load(storage, { now: now() });
  const state = saved?.state || E.createState({ now: now(), pace });
  if (saved) state.pace = Math.max(state.pace, pace);
  let paused = false, shown = 0, lastSave = now(), lastTick = now(), tipEl = null;
  const widgets = new Map();   // widget key -> { mount(app), unmount? }

  /* ---- counter ---- */
  function drawCounter() {
    el.fuzz.textContent = fuzzLabel(state.fuzz);
    if (!el.fps.hidden) el.fps.textContent = `${fuzzLabel(E.fps(state, now()))} per second`;
    if (shown !== Math.floor(state.fuzz)) { shown = Math.floor(state.fuzz); }
  }

  /* ---- shop ---- */
  function drawShop() {
    const win = E.shopWindow(state);
    const ids = win.map((it) => it.id);
    for (const tile of [...el.shop.children]) if (!ids.includes(tile.dataset.id)) tile.remove();
    win.forEach((item, i) => {
      let tile = el.shop.querySelector(`[data-id="${item.id}"]`);
      if (!tile) {
        tile = doc.createElement('button');
        tile.type = 'button'; tile.className = 'tile enter'; tile.dataset.id = item.id;
        tile.style.backgroundImage = `url("${item.icon}")`;
        tile.innerHTML = '<span class="cost"></span>';
        tile.addEventListener('click', () => buy(item.id));
        tile.addEventListener('pointerenter', () => tip(tile, item));
        tile.addEventListener('pointerleave', untip);
        tile.addEventListener('focus', () => tip(tile, item));
        tile.addEventListener('blur', untip);
        el.shop.appendChild(tile);
        requestAnimationFrame(() => tile.classList.remove('enter'));
      }
      if (el.shop.children[i] !== tile) el.shop.insertBefore(tile, el.shop.children[i] || null);
      const cost = E.price(state, item.id);
      tile.querySelector('.cost').textContent = cost.toLocaleString('en-US');
      tile.classList.toggle('short', state.fuzz < cost);
    });
  }

  function tip(tile, item) {
    untip();
    const cost = E.price(state, item.id);
    tipEl = doc.createElement('div');
    tipEl.className = 'tip';
    tipEl.innerHTML = `<b></b><span class="line"></span><br><span class="costline">Cost: ${cost.toLocaleString('en-US')} Fuzz</span>`;
    tipEl.querySelector('b').textContent = item.name;
    tipEl.querySelector('.line').textContent = item.line;
    if (state.fuzz < cost) tipEl.querySelector('.costline').classList.add('short');
    doc.body.appendChild(tipEl);
    const r = tile.getBoundingClientRect();
    tipEl.style.left = `${Math.min(window.innerWidth - 250, Math.max(8, r.left))}px`;
    tipEl.style.top = `${r.bottom + 8}px`;
  }
  function untip() { tipEl?.remove(); tipEl = null; }

  /* ---- actions ---- */
  function buy(id) {
    const r = E.buy(state, id, now());
    if (!r.ok) { if (r.reason === 'short') audio?.play('assets/audio/sfx/ui_denied.mp3', { gain: 0.5 }); return r; }
    audio?.play(PLUCKS[(state.bought.length - 1) % PLUCKS.length], { gain: 0.8 });
    const w = widgets.get(r.item.widget);
    if (w) w.mount(app, r);
    onBuy?.(r, app);
    store(storage, state, extras());
    drawShop(); drawCounter();
    if (id === ENDING_ID) { onEnd?.(app); bridge?.send('fuzz-ended', { total: Math.round(state.total), minutes: E.minutesPlayed(state, now()), items: state.bought.length, achievements: state.achievements.length, dayUtc: new Date().toISOString().slice(0, 10) }); }
    return r;
  }

  function pop(ev) {
    if (paused || state.ended) return;
    audio?.arm();
    const r = E.click(state, now());
    el.bubble.classList.add('pressed');
    setTimeout(() => el.bubble.classList.remove('pressed'), 120);
    if (E.has(state, 'shiny')) audio?.play(`assets/audio/pop/Pop${['', '2', '3'][state.clicks % 3]}.mp3`, { gain: 0.7 });
    if (r.crit) audio?.play('assets/audio/sfx/chip_pop.mp3');
    if (E.has(state, 'floatnum')) floater(ev, r);
    onClick?.(r, ev, app);
    drawCounter(); drawShop();
  }

  function floater(ev, r) {
    const f = doc.createElement('div');
    f.className = 'floater' + (r.crit ? ' crit' : '');
    f.textContent = `+${Math.round(r.gain).toLocaleString('en-US')}`;
    const rect = el.bubble.getBoundingClientRect();
    f.style.left = `${ev?.clientX || rect.left + rect.width / 2}px`;
    f.style.top = `${ev?.clientY || rect.top + rect.height / 2}px`;
    el.fx.appendChild(f);
    setTimeout(() => f.remove(), 720);
  }

  /* ---- pause ---- */
  function extras() { return { muted: !!audio?.muted, safe: doc.body.classList.contains('photosafe') }; }
  function pause() {
    if (paused) return; paused = true;
    audio?.setMuted(true);
    el.pause.hidden = false;
    el.resume.focus();
  }
  function resume() {
    paused = false;
    el.pause.hidden = true;
    audio?.setMuted(el.mute.checked);
    lastTick = now();
  }
  el.wake.addEventListener('click', pause);
  el.resume.addEventListener('click', resume);
  el.mute.addEventListener('change', () => { audio?.setMuted(el.mute.checked); store(storage, state, extras()); });
  el.safe.addEventListener('change', () => { doc.body.classList.toggle('photosafe', el.safe.checked); spiral?.setStill(el.safe.checked); store(storage, state, extras()); });
  el.exportBtn.addEventListener('click', () => { el.exportBox.hidden = false; el.exportBox.value = storage?.getItem?.('fuzz.save.v1') || ''; el.exportBox.select?.(); });
  el.forget.addEventListener('click', () => {
    if (el.forget.dataset.sure) { forget(storage); location.reload(); return; }
    el.forget.dataset.sure = '1'; el.forget.textContent = 'Sure. Forget it.';
    setTimeout(() => { el.forget.dataset.sure = ''; el.forget.textContent = 'Forget everything'; }, 4000);
  });
  doc.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.preventDefault(); paused ? resume() : pause(); } });

  /* ---- loop ---- */
  function frame() {
    requestAnimationFrame(frame);
    const t = now();
    if (!paused && !state.ended) {
      const dt = t - lastTick;
      if (dt >= 100) { E.tick(state, dt, t); lastTick = t; drawCounter(); if ((state.clicks + Math.floor(t / 1000)) % 5 === 0) drawShop(); }
      for (const w of widgets.values()) w.tick?.(t, app);
    }
    if (t - lastSave >= SAVE_EVERY_MS) { lastSave = t; store(storage, state, extras()); }
  }

  /* ---- boot ---- */
  const app = {
    state, el, doc, audio, spiral, bridge, now, widgets, pause, resume, buy, pop, drawCounter, drawShop,
    register(key, widget) { widgets.set(key, widget); },
    /** Remount what a save already owns. */
    restore() {
      if (saved?.extras?.muted) { el.mute.checked = true; audio?.setMuted(true); }
      if (saved?.extras?.safe) { el.safe.checked = true; doc.body.classList.add('photosafe'); spiral?.setStill(true); }
      for (const id of state.bought) { const w = widgets.get(BY_ID[id]?.widget); if (w && !w.mounted) { w.mount(app, { item: BY_ID[id], count: E.owned(state, id), restored: true }); } }
      if (state.ended) onEnd?.(app, true);
    },
    start() {
      el.bubble.addEventListener('pointerdown', pop);
      drawCounter(); drawShop();
      lastTick = now();
      requestAnimationFrame(frame);
    },
    get paused() { return paused; },
    items: ITEMS,
  };
  return app;
}
