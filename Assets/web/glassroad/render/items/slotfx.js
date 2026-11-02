/* render/items/slotfx.js - the HUD item slot's life (polish 2). No three.js; plain DOM and 2D canvas.
 *
 * The integrator mounts it on the slot: createSlotFx(slotEl, iconEl, { feel, phone }) once, then
 * update(dt, item, gentle) every frame with the same item hud.update gets ({ id, rolling, rollT, rollS } | null).
 * Held: motes in the item's colour orbit the bubble on a tilted ring, passing dim behind it and bright in
 * front (two canvases either side of the icon). Rolling: on every tick of hud/format.js rollFrame the new
 * card slides in from the right, tilted and a little small, and settles (CSS translate / rotate / scale,
 * which compose with the slot's own pop animations). Gentle: smaller moves, half the orbit speed, no
 * twinkle. Draws at most 30 times a second and only while something is held.
 */
import { rollFrame } from '../../hud/format.js';
import { SLOT_LOOK as L, MOTE_TINT } from './look.js';
import { slotOrbit, shuffleAt } from './fxmath.js';

export function createSlotFx(slot, icon, { feel = null, phone = false, doc = globalThis.document } = {}) {
  if (!slot || !doc || !doc.createElement) return { update() {}, dispose() {} };
  const layer = (z) => {
    const c = doc.createElement('canvas');
    c.className = 'slotfx'; c.setAttribute('aria-hidden', 'true');
    Object.assign(c.style, { position: 'absolute', left: '-25%', top: '-25%', width: '150%', height: '150%', pointerEvents: 'none', zIndex: String(z) });
    return c;
  };
  const back = layer(0), front = layer(2);
  const view = doc.defaultView;
  if (view && view.getComputedStyle(slot).position === 'static') slot.style.position = 'relative';
  if (icon && view && view.getComputedStyle(icon).position === 'static') { icon.style.position = 'relative'; icon.style.zIndex = '1'; }
  slot.prepend(back); slot.append(front);
  const cb = back.getContext('2d'), cf = front.getContext('2d');
  let px = 0, measureT = 0, acc = 0, t = 0, drawn = false, lastTick = -1, since = 9, shuffled = false;

  function measure() {
    const w = Math.round(slot.clientWidth * 1.5 * Math.min(2, (view && view.devicePixelRatio) || 1));
    if (w > 0 && w !== px) { px = w; back.width = back.height = front.width = front.height = w; }
  }
  function clear() { if (drawn) { cb.clearRect(0, 0, px, px); cf.clearRect(0, 0, px, px); drawn = false; } }

  function orbit(id, gentle) {
    const tint = MOTE_TINT[id] || MOTE_TINT.box, n = phone ? L.orbitPhoneN : L.orbitN, c = px / 2, k = px / 1.5;
    clear();
    for (let i = 0; i < n; i++) {
      const o = slotOrbit(i, n, t, gentle), ctx = o.front ? cf : cb;
      const x = c + o.x * k, y = c + o.y * k, r = o.size * (px / 150), a = o.alpha * (o.front ? 1 : 0.45);
      ctx.fillStyle = `rgba(${tint},${(a * 0.3).toFixed(3)})`;
      ctx.beginPath(); ctx.arc(x, y, r * 2.6, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = `rgba(255,255,255,${a.toFixed(3)})`;
      ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
    }
    drawn = true;
  }

  function shuffle(s, gentle) {
    if (!icon) return;
    const v = shuffleAt(s, gentle);
    icon.style.translate = `${(v.dx * 100).toFixed(2)}% 0`;
    icon.style.rotate = `${v.rotDeg.toFixed(2)}deg`;
    icon.style.scale = v.scale.toFixed(3);
    shuffled = true;
  }
  function unshuffle() {
    if (!shuffled || !icon) return;
    icon.style.translate = ''; icon.style.rotate = ''; icon.style.scale = '';
    shuffled = false;
  }

  return {
    update(dt, item, gentle = false) {
      dt = Math.max(0, Math.min(dt || 0, 0.1));
      t += dt; measureT -= dt;
      if (measureT <= 0) { measure(); measureT = 0.5; }
      const rolling = !!(item && item.rolling), id = item && !item.rolling ? item.id : null;
      if (rolling) {
        const rollS = item.rollS > 0 ? item.rollS : (feel && feel.get('items.rollS')) || 1;
        const f = rollFrame(item.rollT || 0, rollS, item.id, { gentle });
        if (f.tick >= 0 && f.tick !== lastTick) { lastTick = f.tick; since = 0; }
        since += dt;
        if (f.tick >= 0) shuffle(since, gentle); else unshuffle();
        clear();
        return;
      }
      lastTick = -1; since = 9; unshuffle();
      if (!id || !px) { clear(); return; }
      acc += dt;
      if (acc < 1 / 30 && drawn) return;
      acc = 0;
      orbit(id, gentle);
    },
    dispose() { unshuffle(); back.remove(); front.remove(); },
  };
}
