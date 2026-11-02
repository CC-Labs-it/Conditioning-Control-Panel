/* ============================================================================
 * ui/app.js - the plain page: layers, counter, the button, shop presses, toasts, floats, the pause card and the frame
 * loop. It is the `ui` half of ui/engine.js. Colour, sound and motion all come from unlocks, never from here.
 * ==========================================================================*/

import { BY_ID, ITEMS } from '../data/items.js';
import { BRAND, brandText } from '../data/brand.js';
import { CONFIG } from '../data/config.js';
import { whole, clock } from '../sim/format.js';
import { SAVE_EVERY_MS } from '../sim/save.js';
import { createCards } from './cards.js';
import { createShop } from './shop.js';
import { openFork } from './fork.js';
import { pictureRow } from './picture-card.js';

export function createUi({ doc = document, init = {}, audio, extras = {} }) {
  const $ = (id) => doc.getElementById(id);
  const win = doc.defaultView;
  const el = {
    world: $('world'), page: $('page'), stage: $('stage'), counter: $('counter'), count: $('count'), cur: $('cur'), btn: $('btn'),
    wrap: $('btn-wrap'), lock: $('btn-lock'), pause: $('pause'), wake: $('wake'), resume: $('resume'), mute: $('mute'), gentle: $('gentle'),
    toast: $('l-toast'), fx: $('l-fx'), cards: $('cards'), saveBox: $('save-box'),
  };
  const LAYERS = { back: 'l-back', wash: 'l-wash', screens: 'l-screens', floaters: 'l-floaters', ticker: 'l-ticker', meters: 'l-meters', banner: 'l-banner', side: 'l-side', over: 'l-over', toast: 'l-toast', ending: 'l-ending' };
  const cards = createCards(doc, el.cards);
  const motionQuery = win.matchMedia?.('(prefers-reduced-motion: reduce)');
  let gentleOn = !!extras.gentle, initGentle = !!init.gentle, initReduced = !!init.reduced;
  let engine = null, shop = null;
  let particles = 0, dims = { w: win.innerWidth, h: win.innerHeight };
  let lockUntil = 0, swapUntil = 0, toastEl = null, tickerBusy = 0;
  let downTimer = 0;
  const skins = [];
  const tickerQ = [];

  const reduced = () => initReduced || !!motionQuery?.matches;
  const gentle = () => gentleOn || initGentle || reduced();
  const applyGentle = () => doc.body.classList.toggle('gentle', gentle());

  /* ---- layers ---- */
  function layer(name, side) {
    if (name === 'credits') return $('l-credits') || el.fx;
    if (name === 'side') {
      const rails = $('l-side').children;
      if (side === 'left') return rails[0];
      if (side === 'right') return rails[1];
      return rails[0].childElementCount <= rails[1].childElementCount ? rails[0] : rails[1];
    }
    const id = LAYERS[name];
    if (!id) throw new Error(`no layer ${name}`);
    return $(id);
  }

  /* ---- juice: floats, flying lumps, the counter ---- */
  function bumpCounter() {
    if (gentle()) return;
    el.counter.animate?.([{ transform: 'scale(1)' }, { transform: 'scale(1.06)' }, { transform: 'scale(1)' }], { duration: 160, easing: 'ease-out' });
  }

  function float(x, y, text, css = '') {
    if (particles >= CONFIG.MAX_PARTICLES) return;
    particles += 1;
    const f = doc.createElement('div');
    f.className = 'fl';
    if (css) f.style.cssText = css;
    f.textContent = text;
    el.fx.appendChild(f);
    const a = f.animate([{ transform: `translate(${x}px,${y}px) translate(-50%,0)`, opacity: 1 }, { transform: `translate(${x}px,${y - 30}px) translate(-50%,0)`, opacity: 0 }], { duration: 750, easing: 'ease-out' });
    a.onfinish = a.oncancel = () => { f.remove(); particles -= 1; };
  }

  /** A lump flies to the counter from where it was earned. */
  function paid(amount, at) {
    if (!at || particles >= CONFIG.MAX_PARTICLES || gentle()) { bumpCounter(); return; }
    particles += 1;
    const r = el.counter.getBoundingClientRect();
    const f = doc.createElement('div');
    f.className = 'fl';
    f.textContent = `+${whole(amount)}`;
    el.fx.appendChild(f);
    const a = f.animate([{ transform: `translate(${at.x}px,${at.y}px)`, opacity: 1 }, { transform: `translate(${r.left + r.width / 2}px,${r.top + r.height / 2}px)`, opacity: 0.4 }], { duration: 520, easing: 'cubic-bezier(.5,0,.8,.4)' });
    a.onfinish = a.oncancel = () => { f.remove(); particles -= 1; bumpCounter(); };
  }

  function toast(text) {
    toastEl?.remove();
    const t = doc.createElement('div');
    t.className = 'toast';
    t.textContent = text;
    el.toast.style.top = `${el.stage.getBoundingClientRect().bottom - 22}px`;
    el.toast.appendChild(t);
    toastEl = t;
    t.animate?.([{ opacity: 0 }, { opacity: 1, offset: 0.08 }, { opacity: 1, offset: 0.85 }, { opacity: 0 }], { duration: 2800, fill: 'both' }).finished?.then(() => t.remove(), () => {});
  }

  /* ctx.sayRate: held rates multiply. Thoughts past the rate are dropped, not delayed. */
  const sayRates = new Set();
  let sayOwed = 0;
  const sayRate = () => { let r = 1; for (const h of sayRates) r *= h.r; return r; };
  function holdSayRate(r) {
    const h = { r: Math.min(1, Math.max(0, r)) };
    sayRates.add(h);
    return () => sayRates.delete(h);
  }

  /** One thought at a time crossing the strip. Gentle holds it still and fades it. */
  function say(text) {
    sayOwed += sayRate();
    if (sayOwed < 1) return;
    sayOwed -= 1;
    tickerQ.push(text);
    if (tickerQ.length > 6) tickerQ.shift();
    pumpTicker();
  }
  function pumpTicker() {
    if (tickerBusy || !tickerQ.length) return;
    tickerBusy = 1;
    const host = $('l-ticker');
    const s = doc.createElement('span');
    s.style.cssText = 'position:absolute;left:0;top:0;';
    s.textContent = tickerQ.shift();
    host.appendChild(s);
    const w = host.clientWidth, tw = s.offsetWidth;
    const a = gentle()
      ? s.animate([{ opacity: 0 }, { opacity: 1, offset: 0.1 }, { opacity: 1, offset: 0.9 }, { opacity: 0 }], { duration: 4000 })
      : s.animate([{ transform: `translateX(${w}px)` }, { transform: `translateX(${-tw}px)` }], { duration: Math.max(4000, ((w + tw) / 70) * 1000) });
    a.onfinish = a.oncancel = () => { s.remove(); tickerBusy = 0; pumpTicker(); };
  }

  /* ---- the main button ---- */
  function applySkin() {
    const top = skins.length ? skins[skins.length - 1].name : '';
    if (top) el.btn.dataset.skin = top; else delete el.btn.dataset.skin;
  }

  function buttonFor(scope) {
    return {
      el: el.btn,
      skin(name) {
        const entry = { name, scope };
        skins.push(entry);
        applySkin();
        scope.cleanups.add(() => { const i = skins.indexOf(entry); if (i >= 0) skins.splice(i, 1); applySkin(); });
      },
      press() { return engine.tap({ auto: true }); },
      melt(ms = 4000) {
        const d = Math.min(8000, ms);
        const frames = gentle()
          ? [{ opacity: 1 }, { opacity: 0.6 }, { opacity: 1 }]
          : [{ transform: 'none', borderRadius: '0' }, { transform: 'scale(1.08,.72) translateY(14px) skewX(6deg)', borderRadius: '40% 40% 12% 12%' }, { transform: 'scale(.94,1.1) translateY(6px) skewX(-4deg)', borderRadius: '24% 24% 40% 40%' }, { transform: 'none', borderRadius: '0' }];
        const a = el.btn.animate(frames, { duration: d, easing: 'ease-in-out' });
        scope.anims.add(a);
        a.finished?.then(() => scope.anims.delete(a), () => scope.anims.delete(a));
        return a;
      },
      swap(ms = 6000) {
        const d = Math.min(12000, ms);
        const dx = Math.min(180, el.stage.clientWidth / 3);
        el.wrap.style.transform = `translateX(${el.wrap.style.transform.includes('-') ? dx : -dx}px)`;
        swapUntil = engine.real + d;
      },
      lockFor(ms, label = '') {
        lockUntil = engine.real + Math.min(CONFIG.LOCK_MAX_MS, Math.max(0, ms));
        el.btn.classList.add('locked');
        el.lock.hidden = !label;
        el.lock.textContent = brandText(label);
      },
    };
  }

  function tickButton() {
    if (lockUntil && engine.real >= lockUntil) { lockUntil = 0; el.btn.classList.remove('locked'); el.lock.hidden = true; }
    if (swapUntil && engine.real >= swapUntil) { swapUntil = 0; el.wrap.style.transform = ''; }
  }

  function wireButton() {
    el.btn.addEventListener('pointerdown', (ev) => {
      if (ev.button > 0) return;
      ev.preventDefault?.();
      engine.tap({ x: ev.clientX, y: ev.clientY });
    });
    el.btn.addEventListener('click', (ev) => { if (ev.detail === 0) engine.tap({}); });  // keyboard
    el.btn.addEventListener('contextmenu', (ev) => ev.preventDefault());
  }

  /** ctx.openLink only works inside a real press. Set while one trusted event is being handled, cleared right after it,
   *  so a timer, an await or a script's el.click() never counts. Pressing the game button or Wake Up does not count
   *  either: tapping to earn must never open a tab. */
  let pressTarget = null;
  function wirePresses() {
    const note = (ev) => {
      if (!ev.isTrusted) return;
      if (ev.type === 'keydown' && ev.key !== 'Enter' && ev.key !== ' ') return;
      pressTarget = ev.target;
      win.setTimeout(() => { pressTarget = null; }, 0);
    };
    for (const type of ['pointerup', 'click', 'keydown']) doc.addEventListener(type, note, true);
  }
  const inPress = () => !!pressTarget && !pressTarget.closest?.('#btn, #wake');

  /** Pause and suspend freeze every animation in the world, CSS ones included, and thaw only those they froze. */
  let frozen = [];
  function holdWorld(on) {
    if (on) {
      frozen = el.world.getAnimations?.({ subtree: true }).filter((a) => a.playState === 'running') || [];
      for (const a of frozen) a.pause();
    } else {
      for (const a of frozen) { if (a.playState === 'paused') a.play(); }  // anything cancelled during the pause stays cancelled
      frozen = [];
    }
  }

  /* ---- shop presses ---- */
  function press(id) {
    const it = BY_ID[id];
    if (engine.paused) return;
    if (engine.run.fuzz < engine.price(id)) { shop.denied(id); return; }
    if (it.kind === 'fork') {
      openFork(doc, cards, id, (lane) => engine.choose(id, lane));
      return;
    }
    if (it.kind === 'end' && id !== 'onemore') {
      cards.open({ title: it.name, body: `${it.what} ${it.line}`, front: true, buttons: [{ label: 'Yes', onPress: () => engine.buy(id) }, { label: 'Not yet' }] });
      return;
    }
    engine.buy(id);
  }

  /* ---- pause card ---- */
  let wakeGate = null;   // the intro: Wake Up and Escape have nothing to stop yet, and say so
  function pause(host) {
    if (wakeGate && host !== true) { wakeGate(); return; }
    if (engine.paused) { el.resume.focus(); return; }
    shop?.closeTip();
    engine.setPaused(true);
    el.pause.hidden = false;
    const after = engine.life.runs > 0;
    $('save-out').hidden = $('save-in').hidden = !after;
    el.saveBox.hidden = true;
    drawPictureRow();
    el.resume.focus();
    engine.save();
  }
  function drawPictureRow() {
    $('pics-now').textContent = pictureRow(engine.pictures);
    $('pics-change').hidden = !!wakeGate;
  }
  function resume() {
    if (engine.suspended) return;
    el.pause.hidden = true;
    engine.setPaused(false);
  }

  function wirePause(hooks) {
    el.wake.addEventListener('click', () => pause());
    el.resume.addEventListener('click', resume);
    $('pics-change').addEventListener('click', () => { if (!engine.suspended) hooks.changePictures?.(drawPictureRow); });
    el.mute.checked = !!extras.muted;
    audio?.setMuted(!!extras.muted);
    el.gentle.checked = gentleOn;
    el.mute.addEventListener('change', () => { audio?.setMuted(el.mute.checked); extras.muted = el.mute.checked; engine.save(); });
    el.gentle.addEventListener('change', () => { gentleOn = el.gentle.checked; extras.gentle = gentleOn; applyGentle(); engine.save(); });
    doc.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape') return;
      e.preventDefault();
      if (hooks.escapeTaken?.()) return;
      if (engine.paused) resume(); else pause();
    });
    const forget = $('forget');
    forget.addEventListener('click', () => {
      if (forget.dataset.sure) { hooks.forget(); return; }
      forget.dataset.sure = '1';
      forget.textContent = 'Sure. Forget it.';
      setTimeout(() => { forget.dataset.sure = ''; forget.textContent = 'Forget everything'; }, 4000);
    });
    $('save-out').addEventListener('click', () => { el.saveBox.hidden = false; el.saveBox.readOnly = true; el.saveBox.value = hooks.exportSave(); el.saveBox.select?.(); });
    const inBtn = $('save-in');
    inBtn.addEventListener('click', () => {
      if (!el.saveBox.hidden && !el.saveBox.readOnly && el.saveBox.value) { if (!hooks.importSave(el.saveBox.value)) inBtn.textContent = 'That is not a save'; return; }
      el.saveBox.hidden = false; el.saveBox.readOnly = false; el.saveBox.value = ''; el.saveBox.focus();
      inBtn.textContent = 'Load it';
    });
    if (hooks.leave) { const l = $('leave'); l.hidden = false; l.addEventListener('click', hooks.leave); }
  }

  /* ---- ending ---- */
  function endingFade() {
    return new Promise((res) => {
      const f = doc.createElement('div');
      f.className = 'endfade';
      el.world.appendChild(f);
      const a = f.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 1600, easing: 'ease-in', fill: 'forwards' });
      a.onfinish = () => res();
      setTimeout(res, 2000);
    });
  }

  function credits(id, { hooks }) {
    const it = BY_ID[id], run = engine.run, life = engine.life;
    const node = doc.createElement('div');
    const dl = doc.createElement('dl');
    const add = (k, v) => { const a = doc.createElement('dt'), b = doc.createElement('dd'); a.textContent = k; b.textContent = v; dl.append(a, b); };
    add('Route', hooks.routeNames(run) || 'None taken');
    add('Time', clock(run.clock));
    add('Unlocks', String(run.order.length - 1));
    add('Endings seen', `${Object.keys(life.endings).length} of ${engine.endingsTotal}`);
    node.appendChild(dl);
    const slot = doc.createElement('div');
    slot.id = 'l-credits';
    node.appendChild(slot);
    cards.open({ title: it.name, body: it.line, node, className: 'credits', clear: true, guard: false, buttons: [{ label: 'New run', keepOpen: true, onPress: hooks.newRun }] });
  }

  /* ---- the loop ---- */
  let shown = -1, lastDraw = 0, lastSave = 0, last = 0;
  function drawCounter() {
    const n = Math.floor(engine.run.fuzz);
    if (n !== shown) { shown = n; el.count.textContent = whole(n); }
  }
  function draw(force) {
    drawCounter();
    const t = win.performance.now();
    if (force || t - lastDraw > 200) { lastDraw = t; shop.draw(); }
  }
  function frame(t) {
    win.requestAnimationFrame(frame);
    const dt = last ? t - last : 16;
    last = t;
    engine.step(dt);
    tickButton();
    draw(false);
    if (t - lastSave > SAVE_EVERY_MS) { lastSave = t; engine.save(); }
  }

  const ui = {
    doc, win, world: el.world, layer, gentle, reduced, cards,
    parts: Object.freeze({ shop: $('shop'), buttonWrap: el.wrap, counter: el.counter, stage: el.stage }),
    bounds: () => dims,
    buttonFor, buttonLocked: () => lockUntil > 0, inPress,
    pressed() { el.btn.classList.add('down'); clearTimeout(downTimer); downTimer = setTimeout(() => el.btn.classList.remove('down'), 90); },
    holdWorld,
    paid, float, toast, say, holdSayRate, draw,
    modal: (spec) => cards.open(spec),
    denied: (id) => shop?.denied(id),
    /** The page's opening line goes with the first thing bought. */
    drawPhrase() {
      const p = $('phrase');
      const show = engine.run.order.length <= 1;
      if (p.hidden === !show) return;
      p.hidden = !show;
      shop?.fit();
    },
    showCount(n) { el.count.textContent = whole(n); shown = -1; },
    setWakeGate(fn) { wakeGate = fn; },
    bought(item) {
      ui.drawPhrase();
      if (item.kind === 'fork') return;
      if (item.line) toast(brandText(item.line));
      bumpCounter();
    },
    ending: endingFade,
    credits: null,
    pause, resume,
    setInit(f) { if (f.gentle !== undefined) initGentle = !!f.gentle; if (f.reduced !== undefined) initReduced = !!f.reduced; applyGentle(); },

    attach(eng, hooks) {
      engine = eng;
      shop = createShop({ doc, engine, onPress: press });
      ui.credits = (id) => credits(id, { hooks });
      el.cur.textContent = BRAND.currencyPlural;
      wireButton();
      wirePresses();
      wirePause(hooks);
      applyGentle();
      motionQuery?.addEventListener?.('change', applyGentle);
      const onResize = () => { dims = { w: win.innerWidth, h: win.innerHeight }; shop.fit(); };
      win.addEventListener('resize', onResize);
      onResize();
      drawCounter();
      ui.drawPhrase();
      shop.draw();
      shop.fit();
    },
    start() { win.requestAnimationFrame(frame); },
    items: ITEMS,
  };
  return ui;
}
