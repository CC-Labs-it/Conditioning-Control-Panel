import { createFlashMedia } from './flashMedia.js';
import { acquireLoomAtlas } from '../render/loomAtlas.js';
import { screenAmounts } from '../render/items/fxmath.js';
import { createAtmosphereClock } from '../render/screen-atmosphere.js';

const sat = x => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
const ease = x => { const n = sat(x); return n * n * (3 - 2 * n); };
const hash = text => { let n = 0; for (const c of String(text)) n = (n * 31 + c.charCodeAt(0)) >>> 0; return n; };
export const FLASH_BURST_CAPACITY = 9;
export const VEIL_COSMETIC_ONLY = true; // Stun and mercy accounting require a separate owner decision.
export const FLASH_HOLD_S = 2, FLASH_RISE_S = .3, FLASH_FADE_S = .8;
const random = (seed, salt) => {
  let n = (seed + Math.imul(salt + 1, 0x9e3779b9)) >>> 0;
  n = Math.imul(n ^ n >>> 16, 0x21f0aaad); n = Math.imul(n ^ n >>> 15, 0x735a2d97);
  return ((n ^ n >>> 15) >>> 0) / 4294967296;
};
// Goon exec/flashes.js: centres 14..86vw / 16..80vh, max image side 43.7vmin.
// Keep the racing version click-through and the owner's shorter two-second hold.
export function flashBurstLayout(index, age, gentle = false, aspect = 16 / 9, seed = 0, imageAspect = 4 / 3) {
  if (gentle) return { x: index % 2 ? .855 : .015, y: .24 + (Math.floor(index / 2) % 3) * .19,
    w: .13, h: .16, tilt: 0, scale: 1 };
  const a = Math.max(.25, Math.min(4, aspect || 16 / 9)), ratio = Math.max(.2, Math.min(5, imageAspect || 4 / 3));
  const size = .437 * Math.min(1, a), width = size * Math.min(1, ratio), height = size * Math.min(1, 1 / ratio);
  const w = width / a, h = height, id = (seed + index * 7919) >>> 0;
  return { x: .14 + random(id, 0) * .72 - w / 2, y: .16 + random(id, 1) * .64 - h / 2,
    w, h, tilt: random(id, 2) * 16 - 8, scale: .82 + ease(age / FLASH_RISE_S) * .18 };
}

/** Cosmetic only. The shared approved decoder retains consent, expiry and source-off ownership. */
export function createFlashBurst({ source = () => null, pool = null, photosafe, gentle, parent = globalThis.document?.body,
  make = () => document.createElement('canvas'), acquireLoom = acquireLoomAtlas, visibility = globalThis.document } = {}) {
  const queue = [], seen = new Set(), slots = [], emitted = [], screenSeeds = new WeakMap(), atmosphere = createAtmosphereClock(), amounts = {};
  let loom = null, fallback = make(); fallback.width = fallback.height = 256;
  const placeholder = fallback.getContext('2d');
  placeholder.fillStyle = '#25152e'; placeholder.fillRect(0, 0, 256, 256);
  for (let y = 0; y < 8; y++) for (let x = 0; x < 8; x++) {
    placeholder.fillStyle = (x + y) % 2 ? '#633351' : '#493044';
    placeholder.fillRect(x * 32 + 3, y * 32 + 3, 26, 26);
  }
  try {
    loom = acquireLoom?.();
    if (loom?.texture?.image) {
      const atlas = loom.texture.image;
      fallback.getContext('2d').drawImage(atlas, 0, 0, atlas.width / 3, atlas.height / 2, 0, 0, 256, 256);
    }
  } catch { loom?.dispose(); loom = null; }
  for (let i = 0; i < FLASH_BURST_CAPACITY; i++) slots.push({ row: null, age: 0,
    media: createFlashMedia({ source, parent, make, fallback: null, fallbackSource: () => fallback }) });
  let clock = 0, nextAt = 0, total = 0, screenId = 0, disposed = false;
  const calm = () => typeof gentle === 'boolean' ? gentle : !!gentle?.on;
  function fire(cue) {
    if (disposed || !cue?.id || seen.has(cue.id)) return;
    seen.add(cue.id);
    const soft = calm(), wanted = soft ? 3 : Math.max(5, Math.min(9, Math.floor(cue.params?.count || 7)));
    // A burst never repeats a picture (owner, 2026-10-27): with fewer distinct stills than cards, fewer cards.
    // Consecutive seeds walk the pool in order, so up to its size every card is a different still.
    const distinct = typeof pool === 'function' ? Math.floor(pool() || 0) : 0;
    const count = distinct > 0 ? Math.min(wanted, distinct) : wanted;
    for (let i = 0; i < count && queue.length < 54; i++) queue.push({ id: cue.id, index: i,
      seed: hash(cue.id) + i, soft, expires: clock + 4, owner: cue.owner });
  }
  function retireScreen(screen) {
    for (let i = queue.length - 1; i >= 0; i--) if (queue[i].owner === screen) queue.splice(i, 1);
    for (const slot of slots) if (slot.row?.owner === screen) slot.row.fadeAt ??= slot.age;
  }
  // The effect objects persist across frames. A hit's seed changes only when a new effect starts.
  // Keep this latch across clear/seek so a still-held item cannot create a second burst.
  function fromScreen(screen) {
    if (screen?.kind !== 'flash') {
      if (screen && screenSeeds.has(screen) && screen.kind !== 'none') retireScreen(screen);
      return;
    }
    if (screenSeeds.has(screen) && screenSeeds.get(screen) === screen.params?.seed) return;
    retireScreen(screen);
    screenSeeds.set(screen, screen.params?.seed);
    fire({ id: 'screen-flash-' + ++screenId, owner: screen });
  }
  function clear() {
    queue.length = emitted.length = 0; seen.clear(); clock = nextAt = total = 0;
    atmosphere.update(null, 0, 0);
    for (const slot of slots) { slot.row = null; slot.age = 0; slot.media.update(null, 0); }
  }
  const hide = () => { if (visibility?.hidden) clear(); };
  visibility?.addEventListener?.('visibilitychange', hide);
  function update(dt, { paused = false, aspect = 16 / 9, screen, flashes = [] } = {}) {
    if (disposed) return;
    if (visibility?.hidden) { clear(); return; }
    const step = paused ? 0 : Math.max(0, Math.min(.1, dt || 0)), soft = calm(); clock += step;
    if (step > 0) for (const flash of flashes) fromScreen(flash);
    const a = screenAmounts(screen, amounts), fog = atmosphere.update(screen, a.pink, step, a.timeMul);
    const visibleAmount = 1 - Math.max(a.blackout, fog * .95);
    while (emitted.length && clock - emitted[0] >= 1) emitted.shift();
    while (queue.length && queue[0].expires < clock) queue.shift();
    if (soft) for (let i = queue.length - 1; i >= 0; i--) if (queue[i].index >= 3) queue.splice(i, 1);
    if (step > 0 && queue.length && clock >= nextAt && emitted.length < 3) {
      const free = slots.find(slot => !slot.row);
      if (free) {
        nextAt = clock + .34;
        if (photosafe?.request('song-flash') !== false) {
          free.row = queue.shift(); free.age = 0; emitted.push(clock); total++;
        }
      }
    }
    for (const slot of slots) {
      const r = slot.row;
      if (!r) { slot.media.update(null, 0, aspect); continue; }
      r.soft ||= soft; slot.age += step;
      const rise = Math.max(FLASH_RISE_S, gentle?.rise?.(r.soft ? .65 : FLASH_RISE_S) || (r.soft ? .65 : FLASH_RISE_S));
      const fade = Math.max(FLASH_FADE_S, gentle?.fade?.(r.soft ? 1.3 : FLASH_FADE_S) || (r.soft ? 1.3 : FLASH_FADE_S));
      if (soft && r.index >= 3 && r.fadeAt === undefined) r.fadeAt = slot.age;
      const outAt = Math.min(rise + FLASH_HOLD_S, r.fadeAt ?? Infinity);
      const alpha = ease(slot.age / rise) * (1 - ease((slot.age - outAt) / fade)) * visibleAmount;
      slot.media.update({ kind: 'flash', k: alpha, params: { seed: r.seed, nativeAspect: true } }, step, aspect);
      const p = flashBurstLayout(r.index, slot.age, r.soft, aspect, r.seed, slot.media.aspect), canvas = slot.media.canvas;
      Object.assign(canvas.style, { left: p.x * 100 + 'vw', top: p.y * 100 + 'vh', width: p.w * 100 + 'vw',
        height: p.h * 100 + 'vh', transform: `rotate(${p.tilt}deg) scale(${p.scale})` });
      if (slot.age >= outAt + fade) { slot.row = null; slot.media.update(null, 0, aspect); }
    }
  }
  return { fire, update, clear,
    /** A card is up or queued. No allocation: a media provider reads this every frame. */
    get busy() {
      if (queue.length) return true;
      for (let i = 0; i < slots.length; i++) if (slots[i].row) return true;
      return false;
    },
    get stats() { return { clock, total, pending: queue.length, active: slots.filter(s => s.row).length, capacity: slots.length }; },
    dispose() {
      if (disposed) return; clear(); disposed = true;
      visibility?.removeEventListener?.('visibilitychange', hide);
      for (const slot of slots) slot.media.dispose();
      if (fallback) { fallback.width = fallback.height = 1; fallback = null; }
      loom?.dispose(); loom = null;
    } };
}
