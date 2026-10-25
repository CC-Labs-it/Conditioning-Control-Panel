import { createFlashMedia } from './flashMedia.js';
import { acquireLoomAtlas } from '../render/loomAtlas.js';
import { screenAmounts } from '../render/items/fxmath.js';
import { createAtmosphereClock } from '../render/screen-atmosphere.js';

const sat = x => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
const ease = x => { const n = sat(x); return n * n * (3 - 2 * n); };
const hash = text => { let n = 0; for (const c of String(text)) n = (n * 31 + c.charCodeAt(0)) >>> 0; return n; };
export const FLASH_BURST_CAPACITY = 9;
export const VEIL_COSMETIC_ONLY = true; // Stun and mercy accounting require a separate owner decision.
// Reserved side gutters keep the centre, top status row and bottom controls uncovered.
export function flashBurstLayout(index, age, gentle = false) {
  const side = index % 2, drift = gentle ? 0 : Math.min(.055, Math.max(0, age) * .012);
  return { x: side ? .855 + drift : .015 - drift, y: .24 + (Math.floor(index / 2) % 3) * .19,
    w: .13, h: .16, tilt: gentle ? 0 : (side ? 1 : -1) * (4 + age * 1.3) };
}

/** Cosmetic only. The shared approved decoder retains consent, expiry and source-off ownership. */
export function createFlashBurst({ source = () => null, photosafe, gentle, parent = globalThis.document?.body,
  make = () => document.createElement('canvas'), acquireLoom = acquireLoomAtlas } = {}) {
  const queue = [], seen = new Set(), slots = [], emitted = [], atmosphere = createAtmosphereClock(), amounts = {};
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
  let clock = 0, nextAt = 0, total = 0, disposed = false;
  const calm = () => typeof gentle === 'boolean' ? gentle : !!gentle?.on;
  function fire(cue) {
    if (disposed || !cue?.id || seen.has(cue.id)) return;
    seen.add(cue.id);
    const soft = calm(), count = soft ? 3 : Math.max(5, Math.min(9, Math.floor(cue.params?.count || 7)));
    for (let i = 0; i < count && queue.length < 54; i++) queue.push({ id: cue.id, index: i,
      seed: hash(cue.id) + i, soft, life: Math.max(3, Math.min(5, cue.params?.driftS || 4)) });
  }
  function clear() {
    queue.length = emitted.length = 0; seen.clear(); clock = nextAt = total = 0;
    atmosphere.update(null, 0, 0);
    for (const slot of slots) { slot.row = null; slot.age = 0; slot.media.update(null, 0); }
  }
  function update(dt, { paused = false, aspect = 16 / 9, screen } = {}) {
    if (disposed) return;
    const step = paused ? 0 : Math.max(0, Math.min(.1, dt || 0)), soft = calm(); clock += step;
    const a = screenAmounts(screen, amounts), fog = atmosphere.update(screen, a.pink, step, a.timeMul);
    const visibility = 1 - Math.max(a.blackout, fog * .95);
    while (emitted.length && clock - emitted[0] >= 1) emitted.shift();
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
      const rise = Math.max(.3, gentle?.rise?.(r.soft ? .65 : .3) || (r.soft ? .65 : .3));
      const fade = Math.max(.9, gentle?.fade?.(r.soft ? 1.3 : .9) || (r.soft ? 1.3 : .9));
      if (soft && r.index >= 3 && r.fadeAt === undefined) r.fadeAt = slot.age;
      const outAt = Math.min(r.life - fade, r.fadeAt ?? Infinity);
      const alpha = ease(slot.age / rise) * (1 - ease((slot.age - outAt) / fade)) * visibility;
      slot.media.update({ kind: 'flash', k: alpha, params: { seed: r.seed, corner: true } }, step, aspect);
      const p = flashBurstLayout(r.index, slot.age, r.soft), canvas = slot.media.canvas;
      Object.assign(canvas.style, { left: p.x * 100 + 'vw', top: p.y * 100 + 'vh', width: p.w * 100 + 'vw',
        height: p.h * 100 + 'vh', transform: `rotate(${p.tilt}deg)` });
      if (slot.age >= outAt + fade) { slot.row = null; slot.media.update(null, 0, aspect); }
    }
  }
  return { fire, update, clear,
    get stats() { return { clock, total, pending: queue.length, active: slots.filter(s => s.row).length, capacity: slots.length }; },
    dispose() {
      if (disposed) return; clear(); disposed = true;
      for (const slot of slots) slot.media.dispose();
      if (fallback) { fallback.width = fallback.height = 1; fallback = null; }
      loom?.dispose(); loom = null;
    } };
}
