/* ui/unlocks/basics/fx.js - shared juice for the basics lane: arrival checks, sound, bursts, stamps, the tools row, the
 * palette and a cheap Loom spiral. Nothing here touches the DOM at import time. Transient nodes do not go through ctx.el
 * because a scope remembers every node it ever made; they die with their animation instead. */

import { brandText } from '../../../data/brand.js';
import { drawSpiral, normalizeParams } from '../../../../arcademy/engine/loom/loomSpiral.js';

export const docOf = (ctx) => ctx.button.el.ownerDocument;

/** True when this row is the newest buy: mount also runs on reload and on a Control Panel switch, and neither should replay the arrival. */
export const fresh = (ctx) => { const o = ctx.state.order; return !ctx.restored && o[o.length - 1] === ctx.item.id; };

/** Sound is an unlock too: nothing sounds before Click Sound is bought. */
export function sfx(ctx, file, gain = 0.6) {
  if (ctx.has('sound')) ctx.audio.play(`assets/audio/${file}`, { bus: 'sfx', gain });
}

const BASE_CSS = `
.fz-chip { min-height: 34px; padding: 2px 10px; background: var(--panel); color: var(--ink); border: 1px solid var(--line); font-size: 13px; line-height: 1.1; touch-action: manipulation; pointer-events: auto; white-space: nowrap; }
.fz-chip:active { transform: translateY(1px); }
#fz-tools { order: -1; flex: none; display: flex; flex-wrap: wrap; justify-content: center; align-items: center; gap: 6px; margin-top: 4px; pointer-events: auto; }
#btn { position: relative; color: var(--btn-ink, inherit); transition: transform .07s ease-out, border-radius .35s ease-out, width .4s ease-out, height .4s ease-out; }
#btn-zone { transition: height .4s ease-out; }
@media (max-width: 759px) { .fz-chip { flex: none; min-height: 32px; padding: 0 7px; font-size: 12px; } #fz-tools { gap: 3px; width: 100%; flex-wrap: nowrap; justify-content: safe center; overflow-x: auto; scrollbar-width: none; touch-action: pan-x; } }
`;

export function baseCss(ctx) {
  const doc = docOf(ctx);
  if (doc.getElementById('fz-basics-css')) return;
  const s = doc.createElement('style');
  s.id = 'fz-basics-css';
  s.textContent = BASE_CSS;
  doc.head.appendChild(s);
}

/** Gentle switched on in the pause card removes a skin's CSS loop, but the resume plays the frozen animation back. Stop it here. */
export function gentleEndsLoops(ctx) {
  ctx.on('resume', () => {
    if (!ctx.gentle) return;
    for (const a of ctx.button.el.getAnimations()) if (a.animationName && a.animationName !== 'none') a.cancel();
  });
}

/** A node that lives as long as its animation. */
export function spawn(ctx, css, text, parent = 'over') {
  const e = docOf(ctx).createElement('div');
  e.style.cssText = `position:absolute;left:0;top:0;pointer-events:none;${css}`;
  if (text != null) e.textContent = brandText(text);
  (typeof parent === 'string' ? ctx.layer(parent) : parent).appendChild(e);
  return e;
}

export function play(ctx, e, frames, opts) {
  const a = ctx.anim(e, frames, opts);
  a.onfinish = a.oncancel = () => e.remove();
  return a;
}

export function btnCenter(ctx) {
  const r = ctx.button.el.getBoundingClientRect();
  return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height };
}

let live = 0;
const MAX_LIVE = 70;

/** Particles from a point. from/to: the angle range in radians. Gentle gets one soft fading dot instead. */
export function burst(ctx, x, y, { color = 'var(--stamp)', n = 10, reach = 46, size = 7, from = 0, to = Math.PI * 2, round = true, ms = 620 } = {}) {
  if (ctx.gentle) {
    const d = spawn(ctx, `width:${reach}px;height:${reach}px;margin:${-reach / 2}px 0 0 ${-reach / 2}px;transform:translate(${x}px,${y}px);border-radius:50%;background:${color};`);
    play(ctx, d, [{ opacity: 0 }, { opacity: 0.35 }, { opacity: 0 }], { duration: 500 });
    return;
  }
  const count = Math.min(n, MAX_LIVE - live);
  for (let i = 0; i < count; i++) {
    const a = from + ((to - from) * (i + ctx.rng())) / count;
    const dist = reach * (0.6 + ctx.rng() * 0.6);
    const px = x + Math.cos(a) * dist, py = y + Math.sin(a) * dist;
    const d = spawn(ctx, `width:${size}px;height:${size}px;background:${color};${round ? 'border-radius:50%;' : ''}will-change:transform;`);
    live += 1;
    const anim = ctx.anim(d, [{ transform: `translate(${x}px,${y}px) scale(1)`, opacity: 1 }, { transform: `translate(${px}px,${py}px) scale(.4)`, opacity: 0 }], { duration: ms, easing: 'cubic-bezier(.15,.7,.4,1)' });
    anim.onfinish = anim.oncancel = () => { d.remove(); live -= 1; };
  }
}

/** Rings spreading from a point: how sound looks. Gentle gets a slower single ring. */
export function rings(ctx, x, y, { color = 'var(--stamp)', n = 3, size = 90 } = {}) {
  const count = ctx.gentle ? 1 : n;
  for (let i = 0; i < count; i++) {
    const r = spawn(ctx, `left:${x}px;top:${y}px;width:${size}px;height:${size}px;margin:${-size / 2}px 0 0 ${-size / 2}px;box-sizing:border-box;border:3px solid ${color};border-radius:50%;`);
    play(ctx, r, [{ transform: 'scale(.3)', opacity: 0.9 }, { transform: 'scale(1.7)', opacity: 0 }], { duration: 800, delay: i * 170, easing: 'ease-out', fill: 'backwards' });
  }
}

/** A word that swells in, holds and fades. Never flashes. */
export function pop(ctx, x, y, text, { color = 'var(--stamp)', size = 22, ms = 1000, weight = 700, parent = 'over' } = {}) {
  const half = String(text).length * size * 0.32 + 6, { w, h } = ctx.bounds();
  x = half * 2 >= w ? w / 2 : Math.max(half, Math.min(w - half, x));
  y = Math.max(size, Math.min(h - size, y));
  const e = spawn(ctx, `transform:translate(${x}px,${y}px) translate(-50%,-50%);color:${color};font-size:${size}px;font-weight:${weight};white-space:nowrap;text-shadow:0 1px 0 rgba(255,255,255,.35);`, text, parent);
  const base = `translate(${x}px,${y}px) translate(-50%,-50%)`;
  const frames = ctx.gentle
    ? [{ opacity: 0 }, { opacity: 1, offset: 0.25 }, { opacity: 1, offset: 0.75 }, { opacity: 0 }]
    : [{ opacity: 0, transform: `${base} scale(.6)` }, { opacity: 1, transform: `${base} scale(1.1)`, offset: 0.16 }, { opacity: 1, transform: `${base} scale(1)`, offset: 0.3 }, { opacity: 1, transform: `${base} scale(1)`, offset: 0.78 }, { opacity: 0, transform: `${base} scale(.98)` }];
  play(ctx, e, frames, { duration: Math.max(ms, 500), easing: 'ease-out' });
  return e;
}

/** A squash on anything, on the `scale` property so it never fights the pressed state on `transform`. */
export function squish(ctx, el, amount = 1) {
  if (ctx.gentle) return;
  const s = (v) => 1 + (v - 1) * amount;
  ctx.anim(el, [{ scale: '1 1' }, { scale: `${s(1.22)} ${s(0.76)}`, offset: 0.25 }, { scale: `${s(0.92)} ${s(1.1)}`, offset: 0.6 }, { scale: '1 1' }], { duration: 380, easing: 'ease-out' });
}

/** The row of small controls under the counter. In the page flow, so it can never cover the button or the shop. */
export function tools(ctx) {
  const doc = docOf(ctx);
  let r = doc.getElementById('fz-tools');
  if (!r) {
    r = doc.createElement('div');
    r.id = 'fz-tools';
    ctx.layer('meters').appendChild(r);
  }
  return r;
}

export function chip(ctx, label, onPress) {
  const b = ctx.el('button', '', label, tools(ctx));
  b.type = 'button';
  b.className = 'fz-chip';
  b.addEventListener('click', () => onPress(b));
  return b;
}

/* ---- the palette ---- */
const pal = (page, ink, soft, faint, btn, btnInk, btnEdge, panel, panelShort, line, fill, stamp, ready) => ({
  '--page': page, '--ink': ink, '--ink-soft': soft, '--ink-faint': faint, '--btn': btn, '--btn-ink': btnInk, '--btn-edge': btnEdge,
  '--panel': panel, '--panel-short': panelShort, '--line': line, '--fill': fill, '--stamp': stamp, '--ready': ready,
});

export const PALETTES = Object.freeze({
  pink: pal('#ffdcee', '#2a0f22', '#6d3a56', '#8e5f78', '#ff69b4', '#fff', '#d4488f', '#fff5fa', '#f8d3e5', '#d994b9', '#ffb3d9', '#c2186b', '#ffffff'),
  violet: pal('#dccfff', '#1d1238', '#4c3d78', '#6e5f9a', '#7b5cff', '#fff', '#5a3fd0', '#f6f2ff', '#cfc1f5', '#9a87d8', '#b7a4f8', '#5a2bd0', '#ffffff'),
  teal: pal('#c4f0ea', '#08302b', '#2c5f58', '#4e7f78', '#16a596', '#fff', '#0d7f73', '#effbf9', '#b2e4dd', '#74bfb4', '#8edcd0', '#0a7a6d', '#ffffff'),
  black: pal('#111016', '#f0f0f5', '#b8b5c4', '#8a8796', '#2c2935', '#fff', '#000', '#1d1b24', '#16141b', '#45414f', '#3a3644', '#ff9ccf', '#34303e'),
  dark: pal('#17132c', '#f0f0f5', '#bdb8d2', '#8f8aa8', '#3b2f6e', '#fff', '#120e24', '#231e3e', '#1b1732', '#4a4370', '#403666', '#ff9ccf', '#3a3360'),
});

/** Write a palette straight on the root. The player's own press wins over anything mounted before it. */
export function setPalette(ctx, name) {
  ctx.vars(PALETTES[name]);   // through ctx, or the page keeps these colours after the row is switched off
}

/* ---- the Loom, once ---- */
/** Draw one frame of the Loom to a small canvas and turn the canvas: the spiral's motion is a pure rotation, so this
 *  looks the same as redrawing, costs nothing per frame, and Gentle just holds it still. */
export function spiralCanvas(ctx, css, parent, { px = 512, colors = ['#ff4fa8', '#6a3cff'], arms = 4, turns = 2, bg = null, turnMs = 16000 } = {}) {
  const c = ctx.el('canvas', css, null, parent);
  c.width = c.height = px;
  const g = c.getContext('2d');
  const params = normalizeParams({ arms, turns, style: 'log', duty: 0.5, colors, bg: bg || '#000000' });
  if (!bg) g.fillRect = () => {};   // the Loom paints its background first: skip it for a see-through spiral
  const DRAW = 2048;   // the Loom's rings follow the size it is told: a small canvas blown up to the page would show stairs
  g.scale(px / DRAW, px / DRAW);
  drawSpiral(g, DRAW, params, 0);
  const turn = ctx.anim(c, [{ rotate: '0deg' }, { rotate: '360deg' }], { duration: turnMs, iterations: Infinity });
  const hold = () => { if (ctx.gentle) turn.pause(); else if (turn.playState === 'paused' && !paused) turn.play(); };
  let paused = false;
  ctx.on('pause', () => { paused = true; });
  ctx.on('resume', () => { paused = false; hold(); });
  hold();
  ctx.every(1000, hold);
  return { el: c, turn };
}
