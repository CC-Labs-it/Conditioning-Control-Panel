/* ============================================================================
 * ui/app.js - the plain page: layers, counter, the button, shop presses, toasts, floats, the pause card and the frame
 * loop. It is the `ui` half of ui/engine.js. Colour, sound and motion all come from unlocks, never from here.
 * ==========================================================================*/

import { BY_ID, ITEMS } from '../data/items.js';
import { BRAND, brandText, brandTokens } from '../data/brand.js';
import { setRich } from './rich-text.js';
import { CONFIG } from '../data/config.js';
import { whole, clock } from '../sim/format.js';
import { SAVE_EVERY_MS } from '../sim/save.js';
import { bindButtonFace, wardrobeSkins } from './button-faces.js';
import { createCards } from './cards.js';
import { openUnitRecap } from './unit-recap.js';
import { bindCursorShop } from './cursor-shop.js';
import { createShop } from './shop.js';
import { bindAudioControls } from './audio-controls.js';
import { iconFor, stopIconMotion } from './icons.js';
import { openFork } from './fork.js';
import { pictureRow } from './picture-card.js';
import { openAllowedLink } from './links.js';
import { mountUsageRing } from './usage-ring.js';
import { createDock } from './dock.js';
import { bindDockVolume } from './dock-volume.js';
import { createPurchaseFeel } from './purchase-feel.js';
import { windowPreferences } from './preferences.js';
import { openClause, clauseLines, skipsCard } from './clause.js';
import { tally } from './adcard/model.js';
import { ridersOf } from '../data/folds.js';
import { handFor, copyHandFor } from './hands.js';
import { createTapWindow, emiBell } from '../sim/tap-rate.js';
import { createAwayClock } from './away.js';
import { runThemes, themed } from './theme-text.js';
import { nextFrame, frameRect } from './perf.js';

export function createUi({ doc = document, init = {}, audio, extras = {} }) {
  const $ = (id) => doc.getElementById(id);
  const win = doc.defaultView;
  const layouts = windowPreferences(extras);
  const el = {
    world: $('world'), page: $('page'), stage: $('stage'), counter: $('counter'), count: $('count'), cur: $('cur'), btn: $('btn'),
    wrap: $('btn-wrap'), lock: $('btn-lock'), pause: $('pause'), wake: $('wake'), resume: $('resume'), gentle: $('gentle'),
    toast: $('l-toast'), fx: $('l-fx'), cards: $('cards'), saveBox: $('save-box'),
  };
  const sideRails = $('l-side').children;
  // The layout lock sits in the HUD row and keeps its state with the dock layouts (ui/dock.js LOCK_COPY).
  const dock = createDock({ doc, rails: { left: sideRails[0], right: sideRails[1] }, strip: $('dock-strip'), onStrip: () => shop?.fit(),
    lock: { host: $('game-controls'), read: () => layouts.get('dock:lock').on === true, write(on) { layouts.set('dock:lock', { on }); engine?.save(); } } });
  const LAYERS = { back: 'l-back', wash: 'l-wash', screens: 'l-screens', floaters: 'l-floaters', ticker: 'l-ticker', meters: 'l-meters', banner: 'l-banner', side: 'l-side', over: 'l-over', fx: 'l-fx', toast: 'l-toast', ending: 'l-ending' };
  const cards = createCards(doc, el.cards);
  const motionQuery = win.matchMedia?.('(prefers-reduced-motion: reduce)');
  let gentleOn = !!extras.gentle, initGentle = !!init.gentle, initReduced = !!init.reduced;
  let engine = null, shop = null, purchaseFeel = null;
  let particles = 0, dims = { w: win.innerWidth, h: win.innerHeight };
  let lockUntil = 0, swapUntil = 0, toastEl = null, tickerBusy = 0;
  let downTimer = 0;
  const skins = [];
  /* Double Wielding (ui/hands.js): one entry per mounted scope, each with its right-hand juice. Empty = left button only. */
  const hands = [];
  const rightHand = (ev, gain) => { if (gain > 0) for (const h of hands) h(ev, gain); };
  let faces = null;
  const tickerQ = [];

  const reduced = () => initReduced || !!motionQuery?.matches;
  const gentle = () => gentleOn || initGentle || reduced();
  const applyGentle = () => {
    doc.body.classList.toggle('gentle', gentle());
    if (gentle()) { stopIconMotion(doc); purchaseFeel?.clear(); }
  };

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
    if (gentle() || el.counter.hasAttribute('data-score-heat')) return;
    el.counter.animate?.([{ transform: 'scale(1)' }, { transform: 'scale(1.06)' }, { transform: 'scale(1)' }], { duration: 160, easing: 'ease-out' });
  }

  function float(x, y, text, css = '') {
    if (particles >= CONFIG.MAX_PARTICLES) return;
    particles += 1;
    const f = doc.createElement('div');
    f.className = 'fl';
    if (css) f.style.cssText = css;
    setRich(f, text);
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
    setRich(t, text);
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
    setRich(s, tickerQ.shift());
    host.appendChild(s);
    const w = host.clientWidth, tw = s.offsetWidth;
    const a = gentle()
      ? s.animate([{ opacity: 0 }, { opacity: 1, offset: 0.1 }, { opacity: 1, offset: 0.9 }, { opacity: 0 }], { duration: 4000 })
      : s.animate([{ transform: `translateX(${w}px)` }, { transform: `translateX(${-tw}px)` }], { duration: Math.max(4000, ((w + tw) / 70) * 1000) });
    a.onfinish = a.oncancel = () => { s.remove(); tickerBusy = 0; pumpTicker(); };
  }

  /* ---- the main button ---- */
  function applySkin() {
    const automatic = skins.reduce((best, s) => (!best || s.seq > best.seq ? s : best), null)?.name || '';
    const run = engine.run;
    const choice = run.store.bubble?.choice;
    const shopActive = skins.some(s => s.scope.item.id === 'bubble');
    const available = choice && shopActive && wardrobeSkins(run,doc.documentElement.hasAttribute('data-combined-preview')).includes(choice);
    const top = available ? choice : automatic;
    faces?.equip(top);
    if (top) el.btn.dataset.skin = top; else delete el.btn.dataset.skin;
  }

  function buttonFor(scope) {
    return {
      el: el.btn,
      skin(name) {
        const entry = { name, scope, seq: engine.seqFor(scope, `skin:${name}`) };
        skins.push(entry);
        applySkin();
        scope.cleanups.add(() => { const i = skins.indexOf(entry); if (i >= 0) skins.splice(i, 1); applySkin(); });
      },
      press() { return engine.tap({ auto: true }); },
      /** A manual press from a bouncing copy of the button (ui/unlocks/basics.js). Only a real, trusted pointer event
       *  counts, so a script, timer or el.click() can never farm it; it then pays and plays exactly like #btn. */
      pressFrom(ev) {
        if (!ev?.isTrusted || ev.button > 0 && !hands.length) return 0;
        const hand = copyHandFor(ev, hands.length > 0);
        if (!hand) return 0;
        const gain = engine.tap({ x: ev.clientX, y: ev.clientY, via: 'copy' });
        if (hand === 'right') rightHand(ev, gain);
        return gain;
      },
      /** Double Wielding: the right mouse button presses #btn and the copies while this scope is mounted. onRight(ev, gain) is juice only. */
      bothHands(onRight = () => {}) {
        hands.push(onRight);
        scope.cleanups.add(() => { const i = hands.indexOf(onRight); if (i >= 0) hands.splice(i, 1); });
      },
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

  /* Trusted left presses on #btn only: the rate the EMI style's bell curve reads (sim/tap-rate.js). */
  const manualTaps = createTapWindow(3000);
  function wireButton() {
    const down = (ev) => {
      const hand = handFor(ev, hands.length > 0);
      if (!hand) return;
      ev.preventDefault?.();
      const via = !ev.isTrusted ? '' : hand === 'left' ? 'hand' : 'right';
      const t = Number.isFinite(ev.timeStamp) && ev.timeStamp > 0 ? ev.timeStamp : win.performance.now();
      let mult = 1;
      if (via === 'hand' && !engine.paused && !engine.suspended) {
        manualTaps.push(t);
        if (el.btn.dataset.skin === 'emi') mult = emiBell(manualTaps.rate(t));
      }
      const gain = engine.tap({ x: ev.clientX, y: ev.clientY, via, mult, t });
      if (hand === 'right') rightHand(ev, gain);
    };
    el.btn.addEventListener('pointerdown', down);
    el.btn.addEventListener('pointermove', (ev) => { if (ev.button >= 0 && hands.length) down(ev); });  // a chord: the other button joins
    el.btn.addEventListener('click', (ev) => { if (ev.detail === 0) engine.tap({ via: 'key' }); });  // keyboard
    el.btn.addEventListener('contextmenu', (ev) => ev.preventDefault());
  }

  /** ctx.openLink only works inside a real press. Set while one trusted event is being handled, cleared right after it,
   *  so a timer, an await or a script's el.click() never counts. Pressing the game button or Wake Up does not count
   *  either: tapping to earn must never open a tab. */
  let pressTarget = null;
  function wirePresses() {
    const note = (ev) => {
      if (!ev.isTrusted) return;
      const muting = ev.target.closest?.('#audio-mute') && !audio.muted;
      if (engine && !engine.paused && !engine.suspended && !muting) audio.arm();
      if (ev.type === 'keydown' && ev.key !== 'Enter' && ev.key !== ' ') return;
      pressTarget = ev.target;
      win.setTimeout(() => { pressTarget = null; }, 0);
    };
    for (const type of ['pointerup', 'click', 'keydown']) doc.addEventListener(type, note, true);
  }
  const inPress = () => !!pressTarget && !pressTarget.closest?.('#btn, #game-controls');

  /** Pause and suspend freeze every animation in the world, CSS ones included, and thaw only those they froze, or that were born frozen. */
  let frozen = [];
  const holdLate = (a) => { frozen.push(a); };
  let worldWasInert = false, forkCard = null, clauseCard = null;
  function holdWorld(on) {
    if (on) {
      frozen = [el.world, el.cards].flatMap((h) => h.getAnimations?.({ subtree: true }) || []).filter((a) => a.playState === 'running');
      for (const a of frozen) a.pause();
      /* The backdrop stops a mouse. This stops the keyboard: a tile's preview, a collect or a card's button behind the pause card. */
      worldWasInert = !!el.world.inert;
      el.world.inert = true;
      el.cards.inert = true;
    } else {
      for (const a of frozen) { if (a.playState === 'paused') a.play(); }  // anything cancelled during the pause stays cancelled
      frozen = [];
      el.world.inert = worldWasInert;
      el.cards.inert = false;
    }
    if (!forkCard?.closed) forkCard?.hold(on);
  }

  /* ---- shop presses ---- */
  function press(id, row) {
    const it = engine.item(id);
    if (!it || engine.paused) return;
    if (engine.funds(id) < engine.price(id)) { shop.denied(id); return; }
    if (it.options) {
      cards.open({title:it.name,body:it.what,front:true,buttons:[
        ...it.options.map(([key,label])=>({label,onPress:()=>engine.buy(id,key)})),{label:'Later'}]});
      return;
    }
    if (it.kind === 'fork') {
      forkCard = openFork(doc, cards, id, (lane) => { purchaseFeel?.prepare(it, row); engine.choose(id, lane); }, engine.run.prestige?.prepick?.[id]);
      return;
    }
    if (it.kind === 'end' && id !== 'onemore') {
      cards.open({ title: it.name, body: `${it.what} ${it.line}`, front: true, buttons: [{ label: 'Yes', onPress: () => engine.buy(id) }, { label: 'Not yet' }] });
      return;
    }
    /* The terms card (ui/clause.js): accept first, then the purchase lands exactly as before. */
    const riders = ridersOf(id).filter((r) => !engine.run.owned[r]);
    /* Owner 2026-11-07: the terms come once per icon. A repeat buy (another Bouncing Button, a rank of something owned)
     * whose icon the player already bought this run goes straight through. */
    const iconKey = it.iconId || id;
    const seenIcon = Object.keys(engine.run.owned).some((o) => engine.run.owned[o] > 0 && (o === iconKey || (engine.item(o) || {}).iconId === iconKey));
    if (CONFIG.TERMS_CARD && !skipsCard(it) && !seenIcon && clauseLines(id, riders).length) {
      if (clauseCard && !clauseCard.closed) return;
      const offs = [
        engine.subscribe('pause', () => dropClause()),
        engine.subscribe('ending', () => dropClause()),
        engine.subscribe('suspend', ({ on }) => { if (on) dropClause(); }),
      ];
      const settle = () => { for (const off of offs) off(); clauseCard = null; };
      const run = engine.run;
      clauseCard = openClause({
        doc, item: it, riders, gentle, purchase: run.buys - Object.keys(run.picks).length + 1, laterRun: !!run.mood,
        icon: (() => { try { return iconFor(doc, it); } catch { return null; } })(),
        onAccept: () => {
          settle();
          if (engine.paused || engine.suspended) return;
          purchaseFeel?.prepare(it, row);
          if (!engine.buy(id).ok) { purchaseFeel?.clear(); shop.denied(id); }
          else tally(engine.run, 'terms', clauseLines(id, riders).length);   // the Living Ad card's terms ACCEPTED
        },
        onDecline: (line) => { settle(); say(line); },
      });
      clauseCard.settle = settle;
      return;
    }
    purchaseFeel?.prepare(it, row);
    if (!engine.buy(id).ok) purchaseFeel?.clear();
  }
  /** Pause, panic or an ending takes the terms card down. Nothing is bought. */
  function dropClause() {
    const c = clauseCard;
    if (!c || c.closed) return;
    c.close();
    c.settle?.();
  }

  /* ---- pause card ---- */
  let wakeGate = null;   // the intro: Wake Up and Escape have nothing to stop yet, and say so
  let audioControls;
  function pause(host) {
    audioControls.close();
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
    const refreshSource = () => { if (!el.pause.hidden) drawPictureRow(); };
    engine.subscribe('pictures', refreshSource);
    engine.subscribe('picture-meter', refreshSource);
    el.wake.addEventListener('click', () => pause());
    el.resume.addEventListener('click', resume);
    $('pics-change').addEventListener('click', () => { if (!engine.suspended) hooks.changePictures?.(drawPictureRow); });
    audioControls = bindAudioControls(doc, audio, extras, () => engine.save());
    if (audio?.mixer) bindDockVolume({ doc, strip: $('dock-strip'), mixer: audio.mixer, avoid: () => $('game-controls')?.getBoundingClientRect?.() || null });
    el.gentle.checked = gentleOn;
    el.gentle.addEventListener('change', () => { gentleOn = el.gentle.checked; extras.gentle = gentleOn; applyGentle(); engine.save(); });
    doc.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape') return;
      e.preventDefault();
      if (engine.paused && audioControls.close()) return;
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
    /* Credits: the same link rule as ctx.openLink (real press, hosted = open-url frame, unhosted = new tab). */
    for (const b of el.pause.querySelectorAll('[data-credit]')) {
      b.addEventListener('click', () => openAllowedLink(b.dataset.credit, {
        inPress, hosted: !!engine.host?.isHosted, send: (t, f) => engine.host?.send?.(t, f), win,
      }));
    }
    mountAccountChip();
    /* The day's online picture meter, beside the chip (web host only, ui/usage-ring.js). */
    const ringRefresh = mountUsageRing({
      doc, win, pictures: engine.pictures, send: (t, f) => engine.host?.send?.(t, f),
      web: init?.caps?.platform === 'web' && !!engine.host?.isHosted,
    });
    engine.subscribe('pictures', ringRefresh);
    engine.subscribe('picture-meter', ringRefresh);
    ringRefresh();
  }

  /* Signed in on the site (app.cclabs.app/tc): the account sits beside the sound button and opens the dashboard.
     The site sends only a display name, never an id or a token. The top controls are outside inPress(), so the
     chip checks that its own click is a real one. */
  function mountAccountChip() {
    const name = typeof init?.identity?.displayName === 'string' ? init.identity.displayName.trim() : '';
    if (!name || init?.caps?.platform !== 'web' || !engine.host?.isHosted || $('account-chip')) return;
    const chip = doc.createElement('button');
    chip.type = 'button';
    chip.id = 'account-chip';
    chip.title = 'Signed in as ' + name + '. Open your CC Labs account.';
    chip.setAttribute('aria-label', chip.title);
    const dot = doc.createElement('i'); dot.setAttribute('aria-hidden', 'true');
    const label = doc.createElement('span'); label.textContent = name.length > 18 ? name.slice(0, 17) + '…' : name;
    chip.append(dot, label);
    chip.addEventListener('click', (ev) => openAllowedLink('https://app.cclabs.app/dashboard', {
      inPress: () => ev.isTrusted, hosted: true, send: (t, f) => engine.host?.send?.(t, f), win,
    }));
    $('game-controls').prepend(chip);
  }

  /* ---- ending ---- */
  function endingFade(_id, { colour, ms } = {}) {
    purchaseFeel?.clear();
    dock.cancelMove();
    doc.body.dataset.tcEnding = 'true';
    /* registry `fadeMs`: a take that already covers the page (Living Ad) shortens the fade nobody sees */
    const dur = Number.isFinite(ms) && ms >= 0 ? Math.min(1600, ms) : 1600;
    return new Promise((res) => {
      const f = doc.createElement('div');
      f.className = 'endfade';
      if (colour) f.style.background = colour;   // an ending row's own fade colour (registry `fadeColour`)
      el.world.appendChild(f);
      const a = f.animate([{ opacity: 0 }, { opacity: 1 }], { duration: dur, easing: 'ease-in', fill: 'forwards' });
      a.onfinish = () => res();
      setTimeout(res, dur + 400);
    });
  }

  function credits(id, { hooks }) {
    dock.cancelMove();
    doc.body.dataset.tcEnding = 'true';
    const it = BY_ID[id], run = engine.run, life = engine.life;
    const stats = {
      Route: hooks.routeNames(run) || 'None taken',
      Time: clock(run.clock),
      Unlocks: String(run.order.length - 1),
      'Endings seen': `${Object.keys(life.endings).length} of ${engine.endingsTotal}`,
    };
    const slot = doc.createElement('div');
    slot.id = 'l-credits';
    /* Renew (prestige, ui/prestige-sky.js): the run turns into Stardust and the Sky opens. New run stays beside it. */
    const renewDust = hooks.renewDust?.();
    const renew = hooks.renew ? [{ label: Number.isFinite(renewDust) ? `Renew: +${whole(renewDust)} ${hooks.renewCurrency || 'Stardust'}` : 'Renew', keepOpen: true, onPress: hooks.renew }] : [];
    /* Endless ending star (onemore, sim/prestige-endings.js): Keep playing carries this same run on, at full pay. */
    const keep = hooks.keepPlaying && hooks.canKeepPlaying?.() ? [{ label: 'Keep playing', keepOpen: true, onPress: hooks.keepPlaying }] : [];
    const buttons = [...renew, ...keep, { label: 'New run', keepOpen: true, onPress: hooks.newRun }];
    if (id === 'dr_end') {
      openUnitRecap({ cards, doc, slot, stats, buttons, line: it.line, run, life, audio, name: init.identity?.displayName || '' });
      return;
    }
    const node = doc.createElement('div'), dl = doc.createElement('dl');
    for (const [label, value] of Object.entries(stats)) {
      const dt = doc.createElement('dt'), dd = doc.createElement('dd');
      dt.textContent = label;
      dd.textContent = value;
      dl.append(dt, dd);
    }
    node.append(dl, slot);
    cards.open({ title: it.name, body: it.line, node, className: 'credits', clear: true, guard: false, buttons });
  }

  /* ---- the loop ---- */
  let shown = -1, lastDraw = 0, lastSave = 0, last = 0;
  function drawCounter() {
    const n = Math.floor(engine.run.fuzz);
    if (n !== shown) { shown = n; el.count.textContent = whole(n); }
  }
  let refreshCursorShop = null;
  function draw(force) {
    drawCounter();
    const t = win.performance.now();
    if (force || t - lastDraw > 200) { lastDraw = t; shop.draw(); refreshCursorShop?.(); }
  }
  /* Away pay: a hidden tab, a minimised window or a frame off screen draws no frames, so the gap goes to per-second pay
     (ui/away.js, engine.away). A host focus suspend is away too; panic, pause and the end pay nothing. */
  let awayClock = null;
  const backFromAway = (gain) => {
    if (whole(gain) === '0') return;
    /* Themed (owner 2026-11-10): a picked theme words it its own way about 60% of the time. */
    const plain = `While you were away: +${whole(gain)} ${BRAND.currencyPlural}`;
    toast(brandTokens(themed(runThemes(engine.run), 'away', plain).replace('{pay}', whole(gain))));
    draw(true);
  };
  function startAway() {
    awayClock = createAwayClock({ now: () => win.performance.now(), credit: (ms) => engine.away(ms), onBack: backFromAway, capMs: () => engine.awayCap?.() ?? CONFIG.AWAY_CAP_MS });
    win.setInterval(() => { if (awayClock.beat() > 0) engine.save(); }, CONFIG.AWAY_BEAT_MS);
  }
  function frame(t) {
    win.requestAnimationFrame(frame);
    // Read the shared FX targets while the page is still clean: one layout pass here, not one per effect (ui/perf.js).
    nextFrame(); frameRect(el.btn); frameRect(el.counter);
    const dt = last ? t - last : 16;
    last = t;
    const present = !(engine.suspended && engine.suspendReason === 'focus');
    engine.step(Math.min(dt, awayClock.frame(present)));
    tickButton();
    if (!engine.paused && !engine.suspended && !engine.run.ended) faces?.tick();
    draw(false);
    if (t - lastSave > SAVE_EVERY_MS) { lastSave = t; engine.save(); }
  }

  const ui = {
    doc, win, world: el.world, layer, gentle, reduced, cards, dock,
    parts: Object.freeze({ shop: $('shop'), buttonWrap: el.wrap, counter: el.counter, stage: el.stage }),
    bounds: () => dims,
    buttonFor, buttonLocked: () => lockUntil > 0, inPress,
    pressed() { el.btn.classList.add('down'); clearTimeout(downTimer); downTimer = setTimeout(() => el.btn.classList.remove('down'), 90); },
    holdWorld, holdLate,
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
    getWindowLayout: layouts.get,
    saveWindowLayout(key, value) { layouts.set(key, value); engine?.save(); },
    bought(item) {
      ui.drawPhrase();
      purchaseFeel?.bought(item);
      if (item.kind === 'fork') return;
      if (item.line) toast(item.line);
      bumpCounter();
    },
    ending: endingFade,
    credits: null,
    pause, resume,
    setInit(f) { if (f.gentle !== undefined) initGentle = !!f.gentle; if (f.reduced !== undefined) initReduced = !!f.reduced; applyGentle(); },

    attach(eng, hooks) {
      engine = eng;
      faces = bindButtonFace(el.btn,engine,gentle);
      engine.subscribe('wardrobe-equipped',applySkin);
      refreshCursorShop = bindCursorShop(engine,ui);
      purchaseFeel = createPurchaseFeel({ doc, engine, gentle, reduced, dock });
      shop = createShop({ doc, engine, onPress: press, onReady: (row) => purchaseFeel.ready(row) });
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
    start() { startAway(); win.requestAnimationFrame(frame); },
    items: ITEMS,
  };
  return ui;
}
