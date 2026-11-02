/* ============================================================================
 * ui/context.js - the `ctx` every unlock's mount gets. The list is the contract (handoff-1102/GAME-BUILD-BRIEF.md +
 * ENGINE.md); tests/contract.test.mjs checks every member exists. Everything made through ctx dies with the scope.
 * ==========================================================================*/

import { BRAND, brandText } from '../data/brand.js';
import { BY_ID } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { clipUrl } from '../data/words.js';
import { fmt, money } from '../sim/format.js';
import * as E from '../sim/economy.js';
import { allowedLink } from './links.js';

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

export function makeContext(engine, scope) {
  const { ui, audio, host } = engine;
  const item = scope.item;
  const after = item.group === 'after';
  const bag = () => { const owner = after ? engine.life : engine.run; return (owner.store[item.id] ||= {}); };

  const addEl = (el) => { scope.els.add(el); return el; };

  const ctx = {
    item,
    get count() { return E.count(engine.run, item.id); },
    get state() { return readOnly(engine.run); },
    get life() { return readOnly(engine.life); },
    has: (id) => E.has(engine.run, id),
    pick: (f) => E.pick(engine.run, f),
    fmt,
    rng: engine.rng,
    brand: brandObj,

    layer: (name, side) => ui.layer(name, side),
    world: ui.world,
    parts: Object.freeze({ get shop() { return ui.parts?.shop ?? null; }, get buttonWrap() { return ui.parts?.buttonWrap ?? null; }, get counter() { return ui.parts?.counter ?? null; }, get stage() { return ui.parts?.stage ?? null; } }),
    bounds: () => ui.bounds(),

    el(tag, cssText = '', text = null, parent = null) {
      const e = ui.doc.createElement(tag);
      if (cssText) e.style.cssText = cssText;
      if (text != null) e.textContent = brandText(text);
      const host_ = typeof parent === 'string' ? ui.layer(parent) : (parent || ui.layer('floaters'));
      host_.appendChild(e);
      return addEl(e);
    },
    css(text) {
      const s = ui.doc.createElement('style');
      s.textContent = text;
      ui.doc.head.appendChild(s);
      return addEl(s);
    },
    vars(map) {
      const root = ui.doc.documentElement.style;
      const old = Object.keys(map).map((k) => [k, root.getPropertyValue(k)]);
      for (const [k, v] of Object.entries(map)) root.setProperty(k, v);
      scope.cleanups.add(() => { for (const [k, v] of old) { if (v) root.setProperty(k, v); else root.removeProperty(k); } });
    },

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
      if (engine.paused || engine.suspended) a.pause();
      return a;
    },
    speed: (multiplier) => engine.holdSpeed(multiplier, scope),

    on: (event, fn) => engine.subscribe(event, fn, scope),
    emit: (event, data) => engine.emit(event, data),

    pay: (amount, source = item.id, at = null) => engine.pay(amount, source, at),
    spend: (amount, reason = item.id) => engine.spend(amount, reason),
    guardBuy: (fn) => engine.addGuard(fn, scope),
    wallHit: (at) => engine.wallHit(at),
    rates: () => engine.rates(),
    mod(m) {
      // A modifier always sees the same clock as ctx.frame and ctx.every, whatever the sim is passing.
      const held = Object.fromEntries(Object.entries(m).map(([k, f]) => [k, typeof f === 'function' ? (v, s, _now, it) => f(v, s, engine.now, it) : f]));
      scope.mods.push(held);
      engine.mods.push(held);
      return () => { const i = engine.mods.indexOf(held); if (i >= 0) engine.mods.splice(i, 1); scope.mods.splice(scope.mods.indexOf(held), 1); };
    },

    get button() { return ui.buttonFor(scope); },

    say: (text) => ui.say(brandText(text)),
    sayRate(rate) {
      const off = ui.holdSayRate?.(rate) || (() => {});
      const release = () => { off(); scope.cleanups.delete(release); };
      scope.cleanups.add(release);
      return release;
    },
    toast: (text) => ui.toast(brandText(text)),
    float: (x, y, text, css) => ui.float(x, y, brandText(text), css),
    modal(spec) {
      const h = ui.modal(spec, scope);
      scope.cleanups.add(h.close);
      return h;
    },

    audio: {
      play: (path, opts) => audio?.play(path, opts),
      /* Silence everything for a moment, capped. Wall clock on purpose: it must always end, also across a pause. */
      muteFor(ms) {
        audio?.setBlank?.(true);
        clearTimeout(engine.blankTimer);
        engine.blankTimer = setTimeout(() => audio?.setBlank?.(false), Math.min(Math.max(0, ms), 15000));
      },
      loop(path, opts) {
        const l = audio?.loop(path, opts) || { stop() {} };
        scope.loops.add(l);
        return { stop(ms) { scope.loops.delete(l); l.stop(ms); } };
      },
      stream(url, opts) {
        const s = !scope.dead && audio?.stream?.(url, opts);
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
      if (url) audio?.play(url, { bus: 'voice', ...opts });
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
    get gentle() { return ui.gentle(); },
    get reduced() { return ui.reduced(); },
    host: (type, fields) => host.send?.(type, fields),
    openLink(url) {
      const href = allowedLink(url);
      if (!href || !ui.inPress?.()) return false;
      if (host.isHosted) host.send?.('open-url', { url: href });
      else ui.win.open(href, '_blank', 'noopener');
      return true;
    },
    get hosted() { return !!host.isHosted; },
    get caps() { return engine.env.caps || {}; },

    store: {
      get: (k) => bag()[k],
      set: (k, v) => { bag()[k] = v; },
    },
    config: CONFIG,
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
  'speed', 'parts', 'openLink', 'spend', 'guardBuy', 'setPerk', 'sayRate', 'paused',
]);
