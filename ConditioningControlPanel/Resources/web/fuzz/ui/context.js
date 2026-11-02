/* ============================================================================
 * ui/context.js - the `ctx` every unlock's mount gets. The list is the contract (handoff-1102/GAME-BUILD-BRIEF.md +
 * ENGINE.md); tests/contract.test.mjs checks every member exists. Everything made through ctx dies with the scope.
 * ==========================================================================*/

import { BRAND, brandText } from '../data/brand.js';
import { CONFIG } from '../data/config.js';
import { clipUrl } from '../data/words.js';
import { fmt, money } from '../sim/format.js';
import * as E from '../sim/economy.js';
import { nextAd } from './ads.js';

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
      if (engine.paused || engine.suspended) a.pause();
      return a;
    },

    on: (event, fn) => engine.subscribe(event, fn, scope),
    emit: (event, data) => engine.emit(event, data),

    pay: (amount, source = item.id, at = null) => engine.pay(amount, source, at),
    wallHit: (at) => engine.wallHit(at),
    rates: () => engine.rates(),
    mod(m) {
      scope.mods.push(m);
      engine.mods.push(m);
      return () => { const i = engine.mods.indexOf(m); if (i >= 0) engine.mods.splice(i, 1); scope.mods.splice(scope.mods.indexOf(m), 1); };
    },

    get button() { return ui.buttonFor(scope); },

    say: (text) => ui.say(brandText(text)),
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
    },
    voice(slug, opts) {
      const hostUrl = engine.env.caps?.words?.urls?.[slug];
      const url = hostUrl || (engine.env.caps?.words?.bank === 'host' ? null : clipUrl(slug));
      if (url) audio?.play(url, { bus: 'voice', ...opts });
    },

    pictures: engine.pictures,
    ads: { next: (o) => nextAd(o) },

    get gentle() { return ui.gentle(); },
    get reduced() { return ui.reduced(); },
    host: (type, fields) => host.send?.(type, fields),
    get hosted() { return !!host.isHosted; },
    get caps() { return engine.env.caps || {}; },

    store: {
      get: (k) => bag()[k],
      set: (k, v) => { bag()[k] = v; },
    },
    config: CONFIG,
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
]);
