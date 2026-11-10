/* ui/unlocks/fate/outside/grass.js - Touch Grass (ft_grass, Go Outside card 3). Crayon grass blades along the bottom edge,
 * sitting just above the tab bar (never over its controls: the strip lives outside the world, and this canvas takes no
 * pointer at all). A mouse or a finger moving through the blades pushes them the way it moves; each blade pays a little
 * the first time it is brushed and then rests for a while (grass-model.js COOLDOWN_MS), so it cannot be farmed hard.
 * The pointer is read from a passive document listener, so the shop, the tabs and the page keep every press.
 * Gentle and reduced motion: the blades stand still; a brushed blade only brightens and fades back. Phones: fewer blades,
 * 30 fps at most, 15 when nothing moves, and nothing at all while nothing changes under Gentle. */

import { makeBlades, brush, step, touchPay, gustPush } from './grass-model.js';
import { INK } from './art.js';
import { LITE, canvasDpr, paintEvery, frameRect } from '../../../perf.js';
import { fresh } from '../../basics/fx.js';

const GREENS = ['#5d9a3e', '#79b24f', '#4a8634'];
const LIGHTS = ['#9fd36e', '#b7e07f', '#86c35c'];
const GROW_MS = 2600;

export function mountGrass(ctx) {
  const still = () => ctx.gentle || ctx.reduced;
  const H = LITE ? 40 : 54;
  const holder = ctx.el('div', `position:absolute;left:0;right:0;bottom:calc(var(--dock-h, 0px) + env(safe-area-inset-bottom, 0px));height:${H}px;pointer-events:none;`, null, 'floaters');
  holder.dataset.fate = 'grass';
  const cv = ctx.el('canvas', `position:absolute;left:0;top:0;width:100%;height:${H}px;pointer-events:none;`, null, holder);
  const g = cv.getContext('2d');
  const doc = holder.ownerDocument;
  let W = 0, blades = [], ground = [], dpr = 1, dirty = true, acc = 0, now = 0, alive = true;
  let grow = fresh(ctx) ? 0 : 1;
  if (grow < 1 && still()) { grow = 1; ctx.anim(holder, [{ opacity: 0 }, { opacity: 1 }], { duration: 900 }); }

  const build = () => {
    const w = Math.round(ctx.bounds().w || 0);
    if (!w || w === W) return;
    W = w; dpr = canvasDpr(2);
    cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
    blades = makeBlades(W, H - 6, Math.random, LITE);
    ground = Array.from({ length: Math.ceil(W / 18) + 1 }, (_, i) => [i * 18, H - 4 - Math.random() * 3]);
    dirty = true;
  };
  build();
  ctx.every(1000, build);

  const draw = () => {
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, W, H);
    g.lineCap = 'round'; g.lineJoin = 'round';
    for (const b of blades) {
      const len = b.h * grow;
      if (len < 1) continue;
      const tx = b.x + Math.sin(b.a) * len, ty = H - Math.cos(b.a) * len;
      const cx = b.x + Math.sin(b.a * 0.45) * len * 0.5, cy = H - len * 0.58;
      g.beginPath(); g.moveTo(b.x, H); g.quadraticCurveTo(cx, cy, tx, ty);
      g.strokeStyle = INK; g.globalAlpha = 0.72; g.lineWidth = 4.6; g.stroke();
      g.globalAlpha = 1; g.strokeStyle = GREENS[b.hue]; g.lineWidth = 2.8; g.stroke();
      const lit = b.glow || 0;
      g.beginPath(); g.moveTo(b.x - 0.6, H - len * 0.25); g.quadraticCurveTo(cx - 0.6, cy, tx - 0.4, ty + 1.5);
      g.strokeStyle = LIGHTS[b.hue]; g.globalAlpha = 0.45 + lit * 0.55; g.lineWidth = 1 + lit * 1.4; g.stroke();
      g.globalAlpha = 1;
    }
    /* the ground: a ragged crayon band */
    g.beginPath(); g.moveTo(0, H);
    for (const [x, y] of ground) g.lineTo(x, y);
    g.lineTo(W, H); g.closePath();
    g.fillStyle = '#3f6e2a'; g.fill();
    g.strokeStyle = INK; g.globalAlpha = 0.5; g.lineWidth = 1.6; g.stroke(); g.globalAlpha = 1;
    dirty = false;
  };

  /* A Fresh Air gust (ui/unlocks/fate/outside/air.js emits `breeze`) bends the grass as it crosses. */
  let gust = null, lastMoving = 0;
  ctx.on('breeze', (e) => { if (!still() && e && (e.dir === 1 || e.dir === -1)) gust = { dir: e.dir, at: now, crossMs: Number(e.crossMs) || 2400 }; });

  ctx.frame((dt, t) => {
    now = t;
    if (!W) return;
    acc += dt * 1000;
    if (grow < 1) { grow = Math.min(1, grow + (dt * 1000) / GROW_MS); dirty = true; }
    let glowing = false;
    for (const b of blades) if (b.glow > 0) { b.glow = Math.max(0, b.glow - dt * 1.6); glowing = true; }
    if (glowing) dirty = true;
    if (still()) {
      for (const b of blades) if (b.a !== b.base || b.v) { b.a = b.base; b.v = 0; dirty = true; }
      if (!dirty || acc < paintEvery(30)) return;
    } else {
      if (gust && t - gust.at > gust.crossMs + 2500) gust = null;
      /* the ambient sway alone is slow: 15 fps is plenty for it; a brush, a gust or a glow gets the full rate */
      const busy = lastMoving >= 0.05 || glowing || grow < 1 || !!gust || dirty;
      if (acc < (busy ? paintEvery(30) : 1000 / 15)) return;
      const push = gust ? (b) => gustPush(b.x, W, gust.dir, t - gust.at, gust.crossMs) : null;
      lastMoving = step(blades, acc / 1000, 0.07 * Math.sin(t / 1000 * 0.55), push);
    }
    acc = 0;
    draw();
  });

  /* ---- the pointer: a passive listener, nothing is ever prevented or stopped ---- */
  let pending = 0, lastX = 0, lastY = 0, prevX = null;
  const onMove = (e) => {
    if (!alive || ctx.paused || grow < 0.6 || !blades.length) return;
    const r = frameRect(holder);
    const up = r.bottom - e.clientY, x = e.clientX - r.left;
    if (up < -6 || up > H + 6 || x < -10 || x > r.width + 10) { prevX = null; return; }
    const dx = Number.isFinite(e.movementX) && e.movementX ? e.movementX : prevX == null ? 0 : x - prevX;
    prevX = x;
    const paying = brush(blades, x, up, dx, now);
    if (still()) for (const i of paying) blades[i].glow = 1;
    else for (const i of paying) blades[i].glow = 0.8;
    if (paying.length) { pending += paying.length; lastX = e.clientX; lastY = r.top + 6; }
    dirty = true;
  };
  doc.addEventListener('pointermove', onMove, { passive: true });
  ctx.cleanup(() => { alive = false; doc.removeEventListener('pointermove', onMove); });
  ctx.on('ending', () => { alive = false; });

  /* Brushes pay in small bunches, never one lump per pixel moved. */
  ctx.every(400, () => {
    if (!pending) return;
    const n = pending; pending = 0;
    /* the lump flies "+N" (the credited amount) from the grass to the counter */
    ctx.pay(touchPay(ctx.rates().fps) * n, 'grass', { x: lastX, y: lastY });
  });
  return {};
}
