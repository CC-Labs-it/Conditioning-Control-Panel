/* ============================================================================
 * ui/context.js - the `ctx` every unlock's mount gets. The list is the contract (handoff-1102/GAME-BUILD-BRIEF.md +
 * ENGINE.md); tests/contract.test.mjs checks every member exists. Everything made through ctx dies with the scope.
 * ==========================================================================*/

import { BRAND, brandText, brandTokens } from '../data/brand.js';
import { setRich } from './rich-text.js';
import { BY_ID } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { clipUrl } from '../data/words.js';
import { fmt, money } from '../sim/format.js';
import * as E from '../sim/economy.js';
import { openAllowedLink } from './links.js';
import { AD_ASK, adLinkStep } from './ads.js';
import { paletteOf } from './palette.js';
import { slotOrder } from './dock.js';
import { incomeFeel } from './income-feel.js';
import { iconFor } from './icons.js';
import { noteSound, soundOwners } from './dock-volume.js';

/** A deep read-only view: unlocks read the state and change it only through the verbs. */
const views = new WeakMap();
function readOnly(o) {
  if (!o || typeof o !== 'object') return o;
  if (views.has(o)) return views.get(o);
  const p = new Proxy(o, {
    get: (t, k) => readOnly(t[k]),
    set: () => false,
    deleteProperty: () => false,
    defineProperty: () => false,
  });
  views.set(o, p);
  return p;
}

const brandObj = Object.freeze({ ...BRAND, text: brandText, money });
const AUDIO_SOURCES = {
  sound: ['clicks', 'Button clicks'], wallpop: ['bounce', 'Bouncing buttons'],
  a_hum: ['hum', 'Low hum'], a_phones: ['hum', 'Low hum'], a_bin: ['binaural', 'Binaural hum'],
  a_tick: ['metronome', 'Metronome'], a_whisp: ['whispers', 'Whispers'],
};

export function makeContext(engine, scope) {
  const { ui, audio, host } = engine;
  const item = scope.item;
  const after = item.group === 'after';
  const [source, sourceLabel] = AUDIO_SOURCES[item.id] || [item.id, brandText(item.name)];
  /* What an unlock plays is noted under its id, so its bottom tab can show that volume (ui/dock-volume.js). */
  const soundOptions = opts => { const o = { source, sourceLabel, ...opts }; noteSound(item.id, o.source); return o; };
  const bag = () => { const owner = after ? engine.life : engine.run; return (owner.store[item.id] ||= {}); };

  const addEl = (el) => {
    scope.els.add(el);
    /* A long run makes thousands of short-lived nodes (feed tiles, a wall's fades). What has already left the page need not wait for the unlock. */
    if (scope.els.size > 2048) for (const e of scope.els) if (!e.isConnected) scope.els.delete(e);
    return el;
  };

  const ctx = {
    item,
    get count() { return E.count(engine.run, item.id); },
    get state() { return readOnly(engine.run); },
    get life() { return readOnly(engine.life); },
    has: (id) => E.has(engine.run, id),
    pick: (f) => E.pick(engine.run, f),
    /* Ends the run now on ending `id` (Fine Print Speedrun), the same path as the dev door's end. Once only. */
    endRun(id = 'drop') { const r = engine.run; if (engine.stopped || r.ended) return; E.grant(r, id); r.ended = { id, at: r.clock }; engine.end(id); },
    fmt,
    rng: engine.rng,
    brand: brandObj,

    layer: (name, side) => ui.layer(name, side),
    /* The run's colours: { a, b, soft, ink, spiral: [c1, c2], themes }. Event 'palette' fires when they change. */
    palette: () => paletteOf(engine.run),
    /* A movable object. Its bottom tab owns visibility; layout preferences survive new runs. */
    dock(opts = {}) {
      scope.docks = (scope.docks || 0) + 1;
      const key = `_dock${scope.docks}`;
      const layoutKey = `${item.id}:${key}`;
      const legacy = bag()[key] || opts.layout || {};
      const saved = ui.getWindowLayout?.(layoutKey, legacy) || legacy;
      const bought = engine.run.order.indexOf(item.id);
      const s = ui.dock.add({
        anchor: opts.anchor,
        edge: saved.edge || opts.edge,
        title: brandText(opts.title ?? item.name),
        icon: opts.icon ?? item.emoji ?? '',
        order: opts.order ?? slotOrder(bought, scope.docks - 1),
        chip: opts.chip !== false,
        onChip: opts.onChip || null,
        door: opts.door || null,
        pin: !!opts.pin,
        keep: !!opts.keep,
        width: opts.width,
        reserveHeight: opts.reserveHeight,
        min: typeof saved.min === 'boolean' ? saved.min : opts.min,
        onMin: opts.onMin,
        layout: saved,
        onLayout(value) { bag()[key] = { ...bag()[key], ...value }; ui.saveWindowLayout?.(layoutKey, value); opts.onLayout?.(value); },
      });
      for (const host of [s.root, s.chip]) {
        const badge = host?.querySelector('.dk-i');
        if (badge) badge.replaceChildren(iconFor(ui.doc, item));
      }
      /* The first time only: it slides in from its own edge, and its chip gives a little bounce. */
      if (!saved.edge && !ctx.gentle) {
        ctx.anim(s.root, [{ transform: `translateX(${s.side === 'left' ? -40 : 40}px)`, opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 300, easing: 'ease-out' });
        if (s.chip) ctx.anim(s.chip, [{ transform: 'scale(.6)' }, { transform: 'scale(1.12)', offset: 0.6 }, { transform: 'none' }], { duration: 300, easing: 'ease-out' });
      }
      bag()[key] = { ...saved, ...s.layout() };
      s.root.dataset.fzOwner = item.id;
      if (s.chip) s.chip.dataset.fzOwner = item.id;
      incomeFeel(ctx, () => s.body.dock?.visible ? s.root : s.chip);
      ctx.on('pause', s.cancelMove);
      ctx.on('suspend', s.cancelMove);
      ctx.on('ending', s.cancelMove);
      scope.cleanups.add(s.remove);
      return s.body;
    },
    world: ui.world,
    parts: Object.freeze({ get shop() { return ui.parts?.shop ?? null; }, get buttonWrap() { return ui.parts?.buttonWrap ?? null; }, get counter() { return ui.parts?.counter ?? null; }, get stage() { return ui.parts?.stage ?? null; } }),
    bounds: () => ui.bounds(),

    el(tag, cssText = '', text = null, parent = null) {
      const e = ui.doc.createElement(tag);
      e.dataset.fzOwner = item.id;
      if (cssText) e.style.cssText = cssText;
      if (text != null) setRich(e, text);
      const host_ = typeof parent === 'string' ? ui.layer(parent) : (parent || ui.layer('floaters'));
      host_.appendChild(e);
      return addEl(e);
    },
    css(text) {
      const s = ui.doc.createElement('style');
      s.textContent = text;
      /* A row switched back on takes its old place in the cascade. */
      scope.cssN = (scope.cssN || 0) + 1;
      const seq = engine.seqFor(scope, `css:${scope.cssN}`);
      s.dataset.fzSeq = seq;
      const later = [...ui.doc.head.querySelectorAll('style[data-fz-seq]')].find((o) => Number(o.dataset.fzSeq) > seq);
      ui.doc.head.insertBefore(s, later || null);
      return addEl(s);
    },
    vars: (map) => engine.holdVars(map, scope),

    every(ms, fn) {
      const t = { at: engine.now + ms, every: Math.max(16, ms), fn };
      scope.timers.add(t);
      return () => scope.timers.delete(t);
    },
    after(ms, fn) {
      const t = { at: engine.now + ms, every: 0, fn };
      scope.timers.add(t);
      return () => scope.timers.delete(t);
    },
    frame(fn) {
      scope.frames.add(fn);
      return () => scope.frames.delete(fn);
    },
    anim(el, keyframes, opts) {
      const a = el.animate(keyframes, typeof opts === 'number' ? { duration: opts } : opts);
      scope.anims.add(a);
      a.finished?.then(() => scope.anims.delete(a), () => scope.anims.delete(a));
      a.playbackRate = engine.speedFactor();
      if (engine.paused || engine.suspended) { a.pause(); ui.holdLate?.(a); }
      return a;
    },
    speed: (multiplier) => engine.holdSpeed(multiplier, scope),

    on: (event, fn) => engine.subscribe(event, fn, scope),
    /* Runs when the unlock goes, for things ctx cannot see: a registry of its own, a media element. */
    cleanup(fn) { scope.cleanups.add(fn); },
    emit: (event, data) => engine.emit(event, data),

    pay: (amount, source = item.id, at = null) => engine.pay(amount, source, at, item.id),
    spend: (amount, reason = item.id) => engine.spend(amount, reason),
    guardBuy: (fn) => engine.addGuard(fn, scope),
    wallHit: (at) => engine.wallHit(at, item.id),
    rates: () => engine.rates(),
    mod(m) {
      // A modifier always sees the same clock as ctx.frame and ctx.every, whatever the sim is passing.
      const held = Object.fromEntries(Object.entries(m).map(([k, f]) => [k, typeof f === 'function' ? (v, s, _now, it) => f(v, s, engine.now, it) : f]));
      scope.mods.push(held);
      engine.mods.push(held);
      return () => { const i = engine.mods.indexOf(held); if (i >= 0) engine.mods.splice(i, 1); scope.mods.splice(scope.mods.indexOf(held), 1); };
    },

    get button() { return ui.buttonFor(scope); },

    say: (text) => ui.say(brandTokens(text)),
    sayRate(rate) {
      const off = ui.holdSayRate?.(rate) || (() => {});
      const release = () => { off(); scope.cleanups.delete(release); };
      scope.cleanups.add(release);
      return release;
    },
    toast: (text) => ui.toast(brandTokens(text)),
    float: (x, y, text, css) => ui.float(x, y, brandTokens(text), css),
    /* A card nobody answers lets go of the page. It waits while the player is at it; left alone it takes its last button,
     * the way out, unless the spec says the choice must not be made for them (noDefault). A card with `ms` runs its own time. */
    modal(spec) {
      let cancel = null;
      const doc = ui.doc;
      const giveUp = () => {
        h.close();
        const last = spec.buttons?.[spec.buttons.length - 1];
        if (last && !last.keepOpen && !spec.noDefault) last.onPress?.();
      };
      const arm = () => { cancel?.(); cancel = ctx.after(CONFIG.CARD_GIVE_UP_MS, giveUp); };
      const h = ui.modal(spec.ms ? spec : {
        ...spec,
        onOpen(dom) { spec.onOpen?.(dom); arm(); dom.addEventListener('pointerdown', arm, true); doc.addEventListener?.('keydown', arm, true); },
        onClose() { cancel?.(); cancel = null; doc.removeEventListener?.('keydown', arm, true); spec.onClose?.(); },
      }, scope);
      scope.cleanups.add(h.close);
      return h;
    },

    audio: {
      mixer: audio?.mixer,
      play: (path, opts) => audio?.play(path, soundOptions(opts)),
      /* Underwater: everything the unlock `ownerId` plays goes through a lowpass and a small cut (on) or back (off). */
      muffle: (ownerId, on) => {
        /* The registry (noteSound) knows the real source ids an owner streamed or played under; the static map is the fallback. */
        const ids = new Set([(AUDIO_SOURCES[ownerId] || [ownerId])[0], ...(soundOwners.get(ownerId) || [])]);
        for (const id of ids) audio?.muffle?.(id, on);
      },
      /* Underwater for the whole page except this unlock's own sound and the voices (the Bimbodoll bubble past the counter). */
      muffleOthers: (on) => audio?.muffleOthers?.([source, 'voices'], on),
      /* A live input on this unlock's source for synthesised sound: { ac, node, stop() } or null before the first gesture. */
      input: (opts) => audio?.input?.(soundOptions(opts)) || null,
      /* Fine Print Silent: the page stays silent while `on`, and lets go when the unlock does. */
      silence: (on) => { audio?.setRule?.(!!on); if (on) scope.cleanups.add(() => audio?.setRule?.(false)); },
      /* Only this unlock's own sources are heard while `on` (an ending show), also ones it starts later; lets go with the unlock. */
      solo: (on) => {
        audio?.solo?.(on ? (id) => id === source || !!soundOwners.get(item.id)?.has(id) : null);
        if (on) scope.cleanups.add(() => audio?.solo?.(null));
      },
      /* Silence everything for a moment, capped. Wall clock on purpose: it must always end, also across a pause. */
      muteFor(ms) {
        audio?.setBlank?.(true);
        clearTimeout(engine.blankTimer);
        engine.blankTimer = setTimeout(() => audio?.setBlank?.(false), Math.min(Math.max(0, ms), 15000));
      },
      loop(path, opts) {
        const l = audio?.loop(path, soundOptions(opts)) || { stop() {} };
        scope.loops.add(l);
        return { stop(ms) { scope.loops.delete(l); l.stop(ms); } };
      },
      stream(url, opts) {
        const s = !scope.dead && audio?.stream?.(url, soundOptions(opts));
        if (!s) return { el: null, play() {}, pause() {}, setGain() {}, stop() {}, playing: false };
        const h = {
          el: s.el, play: s.play, pause: s.pause, setGain: s.setGain,
          stop(ms) { scope.loops.delete(h); s.stop(ms); },
          get playing() { return s.playing; },
        };
        scope.loops.add(h);
        return h;
      },
    },
    voice(slug, opts) {
      const hostUrl = engine.env.caps?.words?.urls?.[slug];
      const url = hostUrl || (engine.env.caps?.words?.bank === 'host' ? null : clipUrl(slug));
      if (url) audio?.play(url, soundOptions({ bus: 'voice', ...opts }));
    },

    pictures: engine.pictures,
    ads: {
      next: (o = {}) => engine.ads.next({ ...o, theme: o.theme === undefined ? E.pick(engine.run, 'f5') : o.theme }),
      jokesOff(on = true) {
        const before = engine.ads.jokesOff;
        engine.ads.hold(scope, !!on);
        scope.cleanups.add(() => { const was = engine.ads.jokesOff; engine.ads.hold(scope, false); if (was !== engine.ads.jokesOff) engine.emit('ads', { jokesOff: engine.ads.jokesOff }); });
        if (before !== engine.ads.jokesOff) engine.emit('ads', { jokesOff: engine.ads.jokesOff });
      },
      get jokesAreOff() { return engine.ads.jokesOff; },
    },

    get paused() { return !!(engine.paused || engine.suspended); },
    /* True on a reload from a save and when the Control Panel switched this row back on: it did not just arrive, so no arrival show and no first-time gift. */
    get restored() { return !!scope.restored; },
    /* The Control Panel's door to the engine: { isOn(id), set(id, on), waitingOn(id), groups() }. */
    switches: engine.switches,
    get gentle() { return ui.gentle(); },
    get reduced() { return ui.reduced(); },
    host: (type, fields) => host.send?.(type, fields),
    openLink(url) {
      return openAllowedLink(url, { inPress: () => !!ui.inPress?.(), hosted: !!host.isHosted, send: (t, f) => host.send?.(t, f), win: ui.win });
    },
    /** A real ad's link: opens a tab only while the run has opened fewer than AD_TABS_PER_RUN. False = nothing opened. */
    openAdLink(url) {
      const used = (engine.run.store.adtabs ||= { n: 0 });
      const step = adLinkStep(used, CONFIG.AD_TABS_PER_RUN);
      if (step === 'none') return false;
      const open = () => {
        if (used.n >= CONFIG.AD_TABS_PER_RUN) return false;
        const ok = openAllowedLink(url, { inPress: () => !!ui.inPress?.(), hosted: !!host.isHosted, send: (t, f) => host.send?.(t, f), win: ui.win });
        if (ok) used.n += 1;
        return ok;
      };
      if (step === 'open') return open();
      /* Second press on: ask first. Only a real press may ask, and Open is a real press of its own (ui/app.js inPress). */
      if (!ui.inPress?.() || !ui.modal) return false;
      ui.modal({ title: AD_ASK.title, front: true, buttons: [{ label: AD_ASK.open, onPress: open }, { label: AD_ASK.stay }] });
      return false;
    },
    get hosted() { return !!host.isHosted; },
    get caps() { return engine.env.caps || {}; },
    /** {displayName} when the host signed the player in (web /tc), else null. Never an id or a token. */
    get identity() { return engine.env.identity || null; },

    store: {
      getLayout: (key) => ui.getWindowLayout?.(key) || {},
      setLayout: (key, point) => ui.saveWindowLayout?.(key, point),
      get: (k) => bag()[k],
      set: (k, v) => { bag()[k] = v; },
    },
    config: CONFIG,
    /* Renewal Sky, read only: { depth, dust, next, run, level(lane), has(starId), lit(endingId) } (sim/prestige.js). */
    get prestige() { return engine.prestige; },
    /* A prestige lane's own bag in the life save (survives New run); null for every other unlock. */
    get pstore() { return scope.lane ? engine.pstore(scope.lane) : null; },
    /* The shop door, prestige lanes only (Sleepwalk buys, Interest reads the goal row); null for every other unlock. */
    get shop() { return scope.lane ? (n) => engine.shop(n) : null; },
    get price() { return scope.lane ? (id) => engine.price(id) : null; },
    get buy() { return scope.lane ? (id) => engine.buy(id) : null; },
    /** Keep one owned Look for the next run (z_perk), or null for none. Refuses anything that is not a Look the player has owned. */
    setPerk(id) {
      if (id === null) engine.life.perk = null;
      else if (BY_ID[id]?.lane === 'look' && engine.life.seen[id]) engine.life.perk = id;
      else return false;
      engine.save();
      return true;
    },
  };
  return ctx;
}

/** Every member the brief and ENGINE.md promise. tests/contract.test.mjs reads this. */
export const CTX_MEMBERS = Object.freeze([
  'item', 'count', 'state', 'has', 'pick', 'fmt', 'rng', 'brand',
  'layer', 'el', 'every', 'after', 'frame', 'anim', 'on', 'pay', 'mod', 'button',
  'say', 'toast', 'modal', 'audio', 'voice', 'pictures', 'ads', 'gentle', 'reduced', 'host', 'hosted', 'caps', 'store',
  // additions the unlock lanes will want, documented in ENGINE.md
  'life', 'world', 'bounds', 'css', 'vars', 'emit', 'wallHit', 'rates', 'float', 'config',
  'speed', 'parts', 'openLink', 'openAdLink', 'spend', 'guardBuy', 'setPerk', 'sayRate', 'paused', 'palette', 'dock', 'restored', 'switches', 'cleanup',
  // Renewal Sky (2026-11-09, handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md)
  'prestige', 'pstore', 'endRun', 'shop', 'price', 'buy',
  // Living Ad card (2026-11-09)
  'identity',
]);
