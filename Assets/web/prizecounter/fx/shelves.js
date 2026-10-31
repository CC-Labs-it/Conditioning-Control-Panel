/* fx/shelves.js - shelf drop, settle and lift (lane B3-W3). Pure: works on anything with position / rotation /
 * children / visible, so the tests run it on plain objects. The shelf root is the pivot (top centre): the drop moves
 * position.y, the swing is rotation.z. Item slots (slot_r?c? with an item in them) hop and tilt after the landing.
 *
 *   const fx = createShelfFx({ level: () => fxLevel(init), on: (type, data) => ... });
 *   const done = fx.swap({ outgoing, incoming });   // a promise while the old shelves lift, else undefined
 *   fx.step(dt);                                     // every frame
 *
 * Events: land { node, impact } (thud + dust), rattle { node }, lift { node } (whoosh), fade { node, a, end } (Gentle). */
import { springStep, resting, between } from './spring.js';

// Ported from the approved 2D stand-in (px per 60 Hz frame -> metres per second), then tuned in 3D.
export const SHELF = {
  dropH: 6.5, dropStepH: 0.6, g: 52, stagger: 0.12, firstBeat: 0.2,
  bounce: 0.1, bob: [18, 0.3], swing: [9, 0.16], swingV: [0.22, 0.34],
  liftV: 2, liftStepV: 0.35, liftA: 45, liftStagger: 0.09, liftOut: 7, sway: 0.012,
  fadeIn: 0.4, fadeOut: 0.3,
  kick: [0.07, 0.23], hopV: [0.55, 0.85], hopG: 20, hopKeep: 0.3, tiltV: [0.9, 1.8], tilt: [24, 0.18],
};
const SLOT = /^slot_r\dc\d$/;

export function slotsOf(o, out = []) {
  for (const c of o.children || []) {
    if (SLOT.test(c.name)) { if (c.children && c.children.length) out.push(c); } else slotsOf(c, out);
  }
  return out;
}
// Seconds for a lift from rest to clear the top: liftOut = v t + a t^2 / 2.
export const liftTime = (i) => { const v = SHELF.liftV + i * SHELF.liftStepV; return (-v + Math.sqrt(v * v + 2 * SHELF.liftA * SHELF.liftOut)) / SHELF.liftA; };

// An item hop, exact: it flies on gravity, and each bounce off the deck keeps hopKeep of the impact speed.
export function hop(it, dt) {
  const g = SHELF.hopG;
  for (let left = dt, n = 0; left > 0 && (it.y > 0 || it.v > 0) && n < 8; n++) {
    const hit = Math.sqrt(Math.max(0, it.v * it.v + 2 * g * it.y)), tc = (it.v + hit) / g;
    if (tc > left) { it.y = Math.max(0, it.y + it.v * left - (g * left * left) / 2); it.v -= g * left; return; }
    left -= tc; it.y = 0; it.v = hit * SHELF.hopKeep;
    if (it.v < 0.08) it.v = 0;
  }
}

export function createShelfFx({ level, on = () => {}, rng = Math.random }) {
  const units = new Map();
  let clock = 0;
  const lr = (nodes) => [...nodes].sort((a, b) => a.position.x - b.position.x);

  function unitFor(node) {
    const old = units.get(node);
    const u = old || { node, y0: node.position.y, r0: node.rotation.z,
      items: slotsOf(node).map((n) => ({ n, y0: n.position.y, r0: n.rotation.z })) };
    Object.assign(u, { y: old ? old.y : 0, v: 0, bob: { x: 0, v: 0 }, swing: { x: 0, v: 0 }, settleAt: 0 });
    u.items.forEach((it) => Object.assign(it, { y: 0, v: 0, tilt: { x: 0, v: 0 }, kickAt: Infinity, still: true }));
    units.set(node, u);
    return u;
  }
  function apply(u) {
    u.node.position.y = u.y0 + u.y + u.bob.x;
    u.node.rotation.z = u.r0 + u.swing.x;
    for (const it of u.items) { it.n.position.y = it.y0 + it.y; it.n.rotation.z = it.r0 + it.tilt.x; }
  }
  function rest(u) { u.y = 0; u.bob = { x: 0, v: 0 }; u.swing = { x: 0, v: 0 }; u.items.forEach((it) => { it.y = 0; it.tilt = { x: 0, v: 0 }; }); apply(u); }

  function swap({ outgoing = [], incoming = [] }) {
    const L = level();
    if (L.mode === 'off') {
      outgoing.forEach((n) => units.delete(n));
      lr(incoming).forEach((n) => { rest(unitFor(n)); units.delete(n); n.visible = true; on('land', { node: n, impact: 0 }); });
      return undefined;
    }
    let gone = 0, finish;
    const done = outgoing.length ? new Promise((r) => { finish = r; }) : undefined;
    lr(outgoing).forEach((n, i) => {
      const u = units.get(n);
      if (u && u.phase === 'wait') { units.delete(n); n.visible = false; gone++; return; }
      const w = unitFor(n);
      Object.assign(w, { phase: L.fade ? 'fadeOut' : 'lift', t0: clock + (L.fade ? 0 : i * SHELF.liftStagger), v: SHELF.liftV + i * SHELF.liftStepV,
        out: () => { units.delete(n); if (++gone === outgoing.length) finish(); } });
      if (i === 0 && !L.fade) on('lift', { node: n });
    });
    if (done && gone === outgoing.length) finish();
    // The new shelves wait above the frame until the old ones have cleared it, so they never pass through each other.
    const clear = lr(outgoing).map((n, i) => (units.has(n) ? (L.fade ? SHELF.fadeOut : i * SHELF.liftStagger + liftTime(i)) : -1));
    const start = clock + (Math.max(-1, ...clear) < 0 ? SHELF.firstBeat : Math.max(...clear));
    lr(incoming).forEach((n, i) => {
      const u = unitFor(n);
      Object.assign(u, { phase: 'wait', next: L.fade ? 'fadeIn' : 'drop', t0: start + (L.fade ? 0 : i * SHELF.stagger), y: L.fade ? 0 : SHELF.dropH + i * SHELF.dropStepH });
      n.visible = false;
      apply(u);
    });
    return done;
  }

  function land(u, L) {
    const impact = u.v;
    u.y = 0; u.v = 0; u.phase = 'settle'; u.settleAt = clock;
    u.bob = { x: 0, v: impact * SHELF.bounce * L.amp };
    u.swing = { x: 0, v: (rng() < 0.5 ? -1 : 1) * between(rng, SHELF.swingV) * L.amp };
    u.items.forEach((it) => { it.kickAt = clock + between(rng, SHELF.kick); });
    on('land', { node: u.node, impact });
  }

  function step(dt) {
    clock += dt;
    const L = level();
    for (const u of [...units.values()]) {
      const tt = clock - u.t0;
      if (u.phase === 'wait') { if (tt < 0) continue; u.phase = u.next; u.node.visible = true; }
      if (u.phase === 'drop') {
        const d = Math.min(dt, clock - u.t0);
        u.v += SHELF.g * d; u.y -= u.v * d;
        if (u.y <= 0) land(u, L);
      } else if (u.phase === 'settle') {
        springStep(u.bob, ...SHELF.bob, dt); springStep(u.swing, ...SHELF.swing, dt);
        let still = resting(u.bob) && resting(u.swing, 2e-4);
        for (const it of u.items) {
          if (clock >= it.kickAt) {
            it.kickAt = Infinity; it.still = false;
            it.v = between(rng, SHELF.hopV) * L.amp;
            it.tilt.v = (rng() < 0.5 ? -1 : 1) * between(rng, SHELF.tiltV) * L.amp;
            on('rattle', { node: it.n });
          }
          if (!it.still) {
            hop(it, dt);
            springStep(it.tilt, ...SHELF.tilt, dt);
            it.still = it.y === 0 && it.v === 0 && resting(it.tilt, 2e-4);
          }
          still = still && it.still && it.kickAt === Infinity;
        }
        if (still) { rest(u); units.delete(u.node); continue; }
      } else if (u.phase === 'lift') {
        if (tt < 0) continue;
        u.v += SHELF.liftA * dt; u.y += u.v * dt;
        u.swing.x = Math.sin(tt * 11) * SHELF.sway * L.amp;
        if (u.y >= SHELF.liftOut) { u.node.visible = false; u.out(); continue; }
      } else if (u.phase === 'fadeIn' || u.phase === 'fadeOut') {
        const k = Math.min(1, tt / (u.phase === 'fadeIn' ? SHELF.fadeIn : SHELF.fadeOut)), a = u.phase === 'fadeIn' ? k : 1 - k;
        on('fade', { node: u.node, a, end: k >= 1 });
        if (k >= 1) {
          if (u.phase === 'fadeOut') { u.node.visible = false; u.out(); } else { units.delete(u.node); on('land', { node: u.node, impact: 0 }); }
          continue;
        }
      }
      apply(u);
    }
  }

  return { swap, step, busy: () => units.size > 0, get clock() { return clock; } };
}
