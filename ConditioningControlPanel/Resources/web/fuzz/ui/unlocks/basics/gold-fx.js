/* ui/unlocks/basics/gold-fx.js - how the Golden Button looks and feels (owner 2026-11-07: "the golden buttons need juice").
 * Pure presentation: never pays, never draws from the game's RNG, never decides a prize. side.js owns the economics and
 * calls in here. Every effect has an IN and an OUT (docs/JUICE-PLAYBOOK.md). Gentle visuals and reduced motion get slow
 * drift and fades only: no slam, no squash, no reel, no motes, no spin, nothing faster than 3 Hz. One lamp (top left): the
 * drop shadow is its own element and never turns or tilts with the star.
 * Owner 2026-11-08: the coin became a gold crayon star on a paper cutout that turns slowly, and it falls, rises or crosses. */

import { CONFIG } from '../../../data/config.js';
import { docOf, play, rings, sfx, spawn } from './fx.js';
import { frameRect } from '../../perf.js';

const GOLD = '#f6cf3a', GOLD_DEEP = '#d9a10f', GOLD_INK = '#3a2a00', STAMP = '#d42f86';
const SIZE = 84;
/** Seconds for one slow turn of the star. */
const TURN_S = 8;

/** The star, drawn once as an SVG picture, the way a kid does it with a gold crayon (owner 2026-11-08: "too orderly and
 * detailed, should be more scribbly"): a lopsided five-point star, filled with fast back-and-forth scribbles that leave
 * paper showing and run a little over the line, then outlined in a few loose passes whose strokes overshoot at the tips.
 * All on a white paper cutout with a snipped edge. Fixed seed, so every star is the same drawing.
 * frame > 0 is the same drawing gone over again (owner 2026-11-08, line boil): the silhouette and the base strokes come from
 * the same seed, and a second hand (boilHand) nudges every stroke point, overshoot and bow a little, with fresh grain,
 * wobble and paper-edge seeds. Frame 0 is the approved drawing, unchanged.
 * Owner 2026-11-08 ("does not actually animate... like a sketchbook"): the nudges were under a pixel at 92 px, so the
 * frames read as one picture. Now frame > 0 keeps only the silhouette points from the base seed, then the hand starts
 * over (frame seed): a fresh scribble fill at a slightly different angle and start, fresh overshoots and bows, and each
 * outline pass shifted as a whole, so every frame is a new take of the same star, flipped like a sketchbook. */
const BOIL_FRAMES = 4;
/** Seconds for one boil cycle: 4 frames at 9 per second, held without a crossfade. */
const BOIL_S = 0.44;
function boilHand(frame) {
  if (!frame) return () => 0;
  let s = 977 + frame * 7919;
  return (amt) => ((s = (s * 16807) % 2147483647) / 2147483647 - 0.5) * amt;
}
/** The seed a redraw's hand starts over from once the silhouette is set (owner 2026-11-08, sketchbook boil). */
const takeSeed = (base, frame) => 4099 + base * 131 + frame * 7919;
function crayonStarSvg(frame = 0) {
  let seed = 7;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const hand = boilHand(frame), fs = frame * 13;
  const pts = [];
  const lop = [0, 2.5, -3, 1.5, 3.5, -1, -2.5, 2, 1, -3];
  for (let i = 0; i < 10; i++) {
    const ang = -Math.PI / 2 + (i * Math.PI) / 5 + (rnd() - 0.5) * 0.12;
    const r = i % 2 ? 18 + rnd() * 4 : 39 + lop[i];
    pts.push([50 + Math.cos(ang) * r, 52 + Math.sin(ang) * r]);
  }
  /* a redraw keeps the silhouette above and starts the hand over from here (frame 0 never reseeds) */
  if (frame) seed = takeSeed(7, frame);
  const f1 = (n) => n.toFixed(1);
  const d = 'M' + pts.map((p) => f1(p[0]) + ' ' + f1(p[1])).join(' L') + 'Z';
  /* scribble fill: a zigzag swept across the star at an angle, uneven reach, uneven spacing */
  const zig = (angle0, step, reach, wob) => {
    const angle = angle0 + (frame ? (rnd() - 0.5) * 0.35 : 0);
    const c = Math.cos(angle), sn = Math.sin(angle), out = [];
    let t = -48 - (frame ? rnd() * step : 0), side = 1;
    while (t < 48) {
      const u = side * (reach + (rnd() - 0.5) * wob);
      out.push([50 + c * t - sn * u + hand(2.2), 52 + sn * t + c * u + hand(2.2)]);
      t += step * (0.6 + rnd() * 0.8); side = -side;
    }
    return 'M' + out.map((p) => f1(p[0]) + ' ' + f1(p[1])).join(' L');
  };
  /* the outline: each edge its own stroke, a little past both corners, gone over three times, never quite in the same place */
  const edges = (pass) => {
    let o = '';
    const sx = hand(2.6), sy = hand(2.6);
    for (let i = 0; i < 10; i++) {
      const p = pts[i], q = pts[(i + 1) % 10], dx = q[0] - p[0], dy = q[1] - p[1], len = Math.hypot(dx, dy) || 1;
      const over = 1.5 + rnd() * 3 + hand(2), j = () => (rnd() - 0.5) * (1.6 + pass) + hand(2.2);
      const ax = p[0] - (dx / len) * over + j() + sx, ay = p[1] - (dy / len) * over + j() + sy;
      const bx = q[0] + (dx / len) * over + j() + sx, by = q[1] + (dy / len) * over + j() + sy;
      const bow = (rnd() - 0.5) * 3 + hand(3.5);
      o += ' M' + f1(ax) + ' ' + f1(ay) + ' Q' + f1((ax + bx) / 2 - (dy / len) * bow) + ' ' + f1((ay + by) / 2 + (dx / len) * bow) + ' ' + f1(bx) + ' ' + f1(by);
    }
    return o;
  };
  const mask = (k, b) => '<feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 ' + k + ' ' + b + '" result="m"/>';
  const wob = (sd, fr, sc) => '<feTurbulence type="fractalNoise" baseFrequency="' + fr + '" numOctaves="2" seed="' + sd + '" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="' + sc + '" xChannelSelector="R" yChannelSelector="G" result="s"/>';
  const grain = (id, sd, fr, sc, k, b) => '<filter id="' + id + '" x="-20%" y="-20%" width="140%" height="140%">' + wob(sd, fr, sc) + '<feTurbulence type="fractalNoise" baseFrequency="0.5 1.6" numOctaves="2" seed="' + (sd + 3) + '" result="n"/>' + mask(k, b) + '<feComposite in="s" in2="m" operator="in"/></filter>';
  return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-6 -6 112 112"><defs>'
    + '<filter id="cut" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="turbulence" baseFrequency="0.05" numOctaves="2" seed="' + (4 + fs) + '" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="6" xChannelSelector="R" yChannelSelector="G"/></filter>'
    + grain('wax', 7 + fs, 0.06, 3, -2.6, 2.1) + grain('wax2', 9 + fs, 0.08, 3.5, -3.2, 2.3) + grain('line', 11 + fs, 0.07, 2.6, -2.4, 2.05)
    + '<mask id="in"><path d="' + d + '" fill="#fff" stroke="#fff" stroke-width="5" stroke-linejoin="round"/></mask></defs>'
    + '<g filter="url(#cut)"><path d="' + d + '" fill="#d8cfb8" stroke="#d8cfb8" stroke-width="19" stroke-linejoin="round"/><path d="' + d + '" fill="#fffdf5" stroke="#fffdf5" stroke-width="17.5" stroke-linejoin="round"/></g>'
    + '<g mask="url(#in)">'
    + '<g filter="url(#wax)"><path d="' + d + '" fill="#ffe27a" opacity=".3"/></g>'
    + '<g filter="url(#wax)"><path d="' + zig(0.55, 5.6, 40, 10) + '" fill="none" stroke="#f7c21a" stroke-width="4.6" stroke-linejoin="round" stroke-linecap="round"/></g>'
    + '<g filter="url(#wax2)" opacity=".45"><path d="' + zig(0.2, 13, 30, 18) + '" fill="none" stroke="#eb9f0c" stroke-width="3.4" stroke-linejoin="round" stroke-linecap="round"/></g>'
    + '<g filter="url(#wax2)" opacity=".55"><path d="' + zig(1.9, 11, 26, 20) + '" fill="none" stroke="#fff3b0" stroke-width="2.6" stroke-linejoin="round" stroke-linecap="round"/></g>'
    + '</g>'
    + '<g filter="url(#line)" fill="none" stroke-linecap="round">'
    + '<path d="' + edges(0) + '" stroke="#a65f08" stroke-width="3.4"/>'
    + '<path d="' + edges(1) + '" stroke="#c27a0a" stroke-width="2.2" opacity=".85"/>'
    + '<path d="' + edges(2) + '" stroke="#8f4f06" stroke-width="1.5" opacity=".6"/>'
    + '</g></svg>';
}
/** Two more hand-drawn stars from the same box of crayons (owner 2026-11-08 approved A, B and C of four):
 * B is filled with quick loops and leaves an edge open, C is rushed and lopsided with strokes well over the lines. */
function starVariant(o, frame = 0) {
  let seed = o.seed;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const hand = boilHand(frame), fs = frame * 13, h = (n, a = 2.2) => f1(n + hand(a));
  const f1 = (n) => n.toFixed(1);
  const pts = [];
  for (let i = 0; i < 10; i++) {
    const ang = -Math.PI / 2 + o.tilt + (i * Math.PI) / 5 + (rnd() - 0.5) * o.angJ;
    const r = i % 2 ? o.inner + rnd() * 4 : 39 + (rnd() - 0.5) * o.lop;
    pts.push([50 + Math.cos(ang) * r, 52 + Math.sin(ang) * r]);
  }
  /* a redraw keeps the silhouette above and starts the hand over from here (frame 0 never reseeds) */
  if (frame) seed = takeSeed(o.seed, frame);
  let d;
  if (o.round) {
    // chubby: each outer point is a soft curve through the tip
    d = '';
    for (let i = 0; i < 10; i++) {
      const p = pts[i], q = pts[(i + 1) % 10], m = [(p[0] + q[0]) / 2, (p[1] + q[1]) / 2];
      if (i === 0) d = 'M' + f1(m[0]) + ' ' + f1(m[1]);
      else d += ' Q' + f1(p[0]) + ' ' + f1(p[1]) + ' ' + f1(m[0]) + ' ' + f1(m[1]);
    }
    const p0 = pts[0], m0 = [(pts[9][0] + p0[0]) / 2, (pts[9][1] + p0[1]) / 2];
    d += ' L' + f1(m0[0]) + ' ' + f1(m0[1]) + ' Q' + f1(p0[0]) + ' ' + f1(p0[1]) + ' ' + d.slice(1, d.indexOf(' Q') > 0 ? d.indexOf(' Q') : undefined) + 'Z';
  } else d = 'M' + pts.map((p) => f1(p[0]) + ' ' + f1(p[1])).join(' L') + 'Z';
  const zig = (angle0, step, reach, wob) => {
    const angle = angle0 + (frame ? (rnd() - 0.5) * 0.35 : 0);
    const c = Math.cos(angle), sn = Math.sin(angle), out = [];
    let t = -48 - (frame ? rnd() * step : 0), side = 1;
    while (t < 48) { const u = side * (reach + (rnd() - 0.5) * wob); out.push([50 + c * t - sn * u + hand(2.2), 52 + sn * t + c * u + hand(2.2)]); t += step * (0.6 + rnd() * 0.8); side = -side; }
    return 'M' + out.map((p) => f1(p[0]) + ' ' + f1(p[1])).join(' L');
  };
  const loops = (n, r0) => {
    // a run of overlapping loops wandering across the star, like a kid filling fast in circles
    let x = 22, y = 30 + rnd() * 10, out = 'M' + f1(x) + ' ' + f1(y);
    for (let i = 0; i < n; i++) {
      const r = r0 * (0.7 + rnd() * 0.6), nx = x + 5 + rnd() * 3, ny = y + (rnd() - 0.4) * 9;
      out += ' C' + h(x + r) + ' ' + h(y - r * 1.4) + ' ' + h(nx + r * 1.2) + ' ' + h(ny + r) + ' ' + h(nx) + ' ' + h(ny);
      x = nx; y = ny;
      if (x > 80) { x = 18 + rnd() * 8; y += 13; out += ' L' + f1(x) + ' ' + f1(y); }
      if (y > 86) break;
    }
    return out;
  };
  const edges = (pass) => {
    let out = '';
    const sx = hand(2.6), sy = hand(2.6);
    for (let i = 0; i < 10; i++) {
      if (o.gap && i === o.gap && pass === 0) continue;
      const p = pts[i], q = pts[(i + 1) % 10], dx = q[0] - p[0], dy = q[1] - p[1], len = Math.hypot(dx, dy) || 1;
      const over = o.over * (0.5 + rnd()) + hand(2), j = () => (rnd() - 0.5) * (o.jit + pass) + hand(2.2);
      const ax = p[0] - (dx / len) * over + j() + sx, ay = p[1] - (dy / len) * over + j() + sy;
      const bx = q[0] + (dx / len) * over + j() + sx, by = q[1] + (dy / len) * over + j() + sy;
      const bow = (rnd() - 0.5) * o.bow + hand(3.5);
      out += ' M' + f1(ax) + ' ' + f1(ay) + ' Q' + f1((ax + bx) / 2 - (dy / len) * bow) + ' ' + f1((ay + by) / 2 + (dx / len) * bow) + ' ' + f1(bx) + ' ' + f1(by);
    }
    return out;
  };
  const mask = (k, b) => '<feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 ' + k + ' ' + b + '" result="m"/>';
  const wob = (sd, fr, sc) => '<feTurbulence type="fractalNoise" baseFrequency="' + fr + '" numOctaves="2" seed="' + sd + '" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="' + sc + '" xChannelSelector="R" yChannelSelector="G" result="s"/>';
  const grain = (id, sd, fr, sc, k, b) => '<filter id="' + id + '" x="-20%" y="-20%" width="140%" height="140%">' + wob(sd, fr, sc) + '<feTurbulence type="fractalNoise" baseFrequency="0.5 1.6" numOctaves="2" seed="' + (sd + 3) + '" result="n"/>' + mask(k, b) + '<feComposite in="s" in2="m" operator="in"/></filter>';
  const fill = o.loops
    ? '<g filter="url(#wax)"><path d="' + loops(60, o.loops) + '" fill="none" stroke="#f7c21a" stroke-width="4" stroke-linecap="round"/></g>'
    : '<g filter="url(#wax)"><path d="' + zig(o.ang, o.step, 40, 12) + '" fill="none" stroke="#f7c21a" stroke-width="' + o.sw + '" stroke-linejoin="round" stroke-linecap="round"/></g>';
  const shade = o.shade ? '<g filter="url(#wax2)" opacity=".8"><clipPath id="sh"><path d="M50 52 L110 52 L110 110 L50 110Z"/></clipPath><path clip-path="url(#sh)" d="' + zig(o.ang + 0.25, 3.8, 40, 6) + '" fill="none" stroke="#e5850a" stroke-width="2.6" stroke-linecap="round"/></g>' : '';
  let lines = '';
  const cols = ['#a65f08', '#c27a0a', '#8f4f06', '#b8690a'];
  for (let k = 0; k < o.passes; k++) lines += '<path d="' + edges(k) + '" stroke="' + (o.ink || cols[k % 4]) + '" stroke-width="' + (o.lw - k * 0.6).toFixed(1) + '" opacity="' + (1 - k * 0.15).toFixed(2) + '"/>';
  return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-6 -6 112 112"><defs>'
    + '<filter id="cut" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="turbulence" baseFrequency="0.05" numOctaves="2" seed="' + (o.seed + fs) + '" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="6" xChannelSelector="R" yChannelSelector="G"/></filter>'
    + grain('wax', o.seed + 1 + fs, 0.06, 3, -2.6, 2.1) + grain('wax2', o.seed + 2 + fs, 0.08, 3.5, -3.2, 2.3) + grain('line', o.seed + 4 + fs, 0.07, o.lineWob, -2.4, 2.05)
    + '<mask id="in"><path d="' + d + '" fill="#fff" stroke="#fff" stroke-width="' + o.spill + '" stroke-linejoin="round"/></mask></defs>'
    + '<g filter="url(#cut)"><path d="' + d + '" fill="#d8cfb8" stroke="#d8cfb8" stroke-width="' + (o.margin + 1.5) + '" stroke-linejoin="round"/><path d="' + d + '" fill="#fffdf5" stroke="#fffdf5" stroke-width="' + o.margin + '" stroke-linejoin="round"/></g>'
    + '<g mask="url(#in)"><g filter="url(#wax)"><path d="' + d + '" fill="#ffe27a" opacity="' + o.wash + '"/></g>' + fill + shade + '</g>'
    + '<g filter="url(#line)" fill="none" stroke-linecap="round">' + lines + '</g></svg>';
}
const LOOPY = { seed: 31, tilt: 0.08, angJ: 0.16, lop: 6, inner: 17, loops: 6, spill: 7, wash: 0.25, passes: 2, jit: 2.2, over: 2, bow: 4, lw: 3.6, lineWob: 3, margin: 16, gap: 3 };
const RUSHED = { seed: 53, tilt: -0.12, angJ: 0.3, lop: 12, inner: 20, ang: 1.15, step: 8.5, sw: 5.5, spill: 11, wash: 0.12, passes: 2, jit: 3.5, over: 5, bow: 6, lw: 3.8, lineWob: 4, margin: 18 };
/* The Renewal Sky (ui/prestige-sky.js) draws its shooting Golden Stars from the same three approved drawings. */
export { crayonStarSvg, starVariant, LOOPY, RUSHED, BOIL_FRAMES };
const asUrl = (svg) => `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
/** The three approved stars. Each Golden Button picks one (Math.random: cosmetic, the run's RNG stays untouched) and its
 * catch stars wear the same drawing. Each entry holds BOIL_FRAMES redraws of that star, built once here. */
const STARS = [(f) => crayonStarSvg(f), (f) => starVariant(LOOPY, f), (f) => starVariant(RUSHED, f)]
  .map((draw) => Array.from({ length: BOIL_FRAMES }, (_, f) => asUrl(draw(f))));
const STAR = STARS[0][0];
/* One frame layer shows at a time, held (step-end) for BOIL_S / BOIL_FRAMES, no crossfade. */
const BOIL_KEYS = `@keyframes fzBoil { 0% { opacity: 1; } ${(100 / BOIL_FRAMES).toFixed(2)}% { opacity: 0; } 100% { opacity: 0; } }`;
let lastStar = 0;

const CSS = `
.fz-gold { position: absolute; left: 0; top: 0; width: ${SIZE}px; height: ${SIZE}px; padding: 0; margin: 0; border: 0; background: none; pointer-events: auto; touch-action: manipulation; cursor: pointer; will-change: transform; -webkit-tap-highlight-color: transparent; }
.fz-gold:focus-visible { outline: 3px solid ${STAMP}; outline-offset: 4px; border-radius: 50%; }
.fz-gold .shade { position: absolute; left: 17px; top: 21px; width: 58px; height: 56px; border-radius: 50%; background: rgba(40, 26, 0, .3); filter: blur(6px); }
.fz-gold .face { position: absolute; inset: 0; }
.fz-gold .star { position: absolute; inset: 0; }
.fz-gold.wink .star { animation: fzGoldWink .5s ease-in-out 2; }
@keyframes fzGoldWink { 0%, 100% { transform: scale(1); } 50% { transform: scale(1.12, .3); } }
.fz-boil > i { position: absolute; inset: 0; background: ${STAR} center / contain no-repeat; opacity: 0; }
.fz-boil > i:first-child { opacity: 1; }
.fz-boil.on > i { animation: fzBoil ${BOIL_S}s step-end infinite; }
${BOIL_KEYS}
body.gentle .fz-gold.wink .star, body.gentle .fz-boil.on > i { animation: none; }
@media (prefers-reduced-motion: reduce) { .fz-gold.wink .star, .fz-boil.on > i { animation: none; } }
.fz-gold-stamp { font: 700 30px/1 "Tinos", "Times New Roman", serif; letter-spacing: .06em; color: ${STAMP}; border: 4px solid ${STAMP}; outline: 2px solid ${STAMP}; outline-offset: 3px; padding: 6px 14px 4px; background: rgba(247, 245, 239, .9); white-space: nowrap; mix-blend-mode: multiply; box-shadow: 0 0 0 1px rgba(212, 47, 134, .15); }
.fz-gold-reel { width: 64px; height: 44px; overflow: hidden; border: 3px solid ${GOLD_INK}; border-radius: 8px; background: #fff7d6; box-shadow: inset 0 3px 0 rgba(0, 0, 0, .12); }
.fz-gold-reel .strip { display: grid; }
.fz-gold-reel .cell { height: 44px; display: grid; place-items: center; font: 900 16px/1 Tahoma, Verdana, sans-serif; color: #6e4b00; }
.fz-gold-ring { position: absolute; left: 0; top: 0; border-radius: 50%; pointer-events: none; background: conic-gradient(${GOLD} calc(var(--p, 1) * 1turn), rgba(246, 207, 58, .18) 0); -webkit-mask: radial-gradient(farthest-side, transparent calc(100% - 7px), #000 calc(100% - 6px)); mask: radial-gradient(farthest-side, transparent calc(100% - 7px), #000 calc(100% - 6px)); box-shadow: 0 0 18px rgba(246, 207, 58, .55); }
.fz-gold-veil { position: fixed; inset: 0; pointer-events: none; background: radial-gradient(ellipse at 50% 45%, transparent 52%, rgba(214, 150, 20, .26) 100%); opacity: 0; transition: opacity .9s ease; }
.fz-gold-veil.on { opacity: 1; }
.fz-gold-combo { position: absolute; left: 0; top: 0; pointer-events: none; font: 900 26px/1 Tahoma, Verdana, sans-serif; color: #fff; padding: 5px 12px 6px; border: 3px solid ${GOLD_INK}; border-radius: 10px; box-shadow: 0 3px 0 ${GOLD_INK}; white-space: nowrap; transform-origin: 50% 50%; }
.fz-gold-ticket { width: 58px; height: 30px; display: grid; place-items: center; font: 900 11px/1 Tahoma, Verdana, sans-serif; letter-spacing: .06em; color: #6e4b00; background: linear-gradient(135deg, #fff3b0, ${GOLD} 60%, ${GOLD_DEEP}); border: 3px solid ${GOLD_INK}; border-radius: 5px; text-transform: uppercase; }
`;

const calm = (ctx) => ctx.gentle || ctx.reduced;

/** Live particle count for this file, inside the game-wide ceiling. */
let live = 0;
const room = () => live < Math.max(4, Math.floor(CONFIG.MAX_PARTICLES / 2));
function particle(ctx, css, frames, opts) {
  if (!room()) return null;
  const d = spawn(ctx, css);
  live += 1;
  const a = ctx.anim(d, frames, opts);
  a.onfinish = a.oncancel = () => { d.remove(); live -= 1; };
  return d;
}

export function createGoldFx(ctx, { frenzy = 7, deep = 5 } = {}) {
  const doc = docOf(ctx);
  ctx.css(CSS);

  /** Line boil: one layer per redraw of the star, stacked; a CSS steps cycle shows one at a time (compositor opacity
   * only, no layout, nothing per frame in JS). Gentle visuals and reduced motion get frame 0 alone, held. */
  function boil(el, frames, phase = 0) {
    const live = !calm(ctx), n = live ? frames.length : 1;
    el.classList.add('fz-boil');
    if (live) el.classList.add('on');
    for (let i = 0; i < n; i++) {
      const l = doc.createElement('i');
      l.style.backgroundImage = frames[i];
      if (live) l.style.animationDelay = `${(-((i + phase) % n) * BOIL_S / n).toFixed(3)}s`;
      el.appendChild(l);
    }
  }

  /* ---- 2. the look ---- */
  function makeButton() {
    const b = ctx.el('button', '', null, 'over');
    b.type = 'button';
    b.className = 'fz-gold';
    b.setAttribute('aria-label', 'Golden Button');
    const shade = doc.createElement('span'); shade.className = 'shade';
    const face = doc.createElement('span'); face.className = 'face';
    const star = doc.createElement('span'); star.className = 'star';
    lastStar = Math.floor(Math.random() * STARS.length);
    boil(star, STARS[lastStar]);
    face.appendChild(star);
    b.append(shade, face);
    b.fzFace = face;
    b.fzSpin = Math.random() * 360;
    return b;
  }

  /* ---- 1. the path: it falls from the top, rises from the bottom, or crosses either way (Math.random: cosmetic only,
   * the run's RNG stays untouched). Every path lasts at least as long as the old left-to-right crossing, so it stays
   * as easy to catch, and it ends on screen where the miss puff plays. ---- */
  function route(w, h, yHint) {
    const base = ctx.gentle ? 45 : 85;
    const r = Math.random(), kind = r < 0.36 ? 'fall' : r < 0.72 ? 'rise' : 'cross';
    const dir = Math.random() < 0.5 ? 1 : -1;
    const cross = (w + SIZE * 0.1 + 10) / base;
    const T = kind === 'cross' ? cross : Math.max(cross, (h + SIZE * 0.1 + 10) / base);
    const sway = Math.max(0, Math.min(70, (w - SIZE - 24) / 4)) * (calm(ctx) ? 0.5 : 1);
    const x0 = sway + 12 + Math.random() * Math.max(0, w - SIZE - 24 - sway * 2);
    const phase = Math.random() * Math.PI * 2, waves = 1.2 + Math.random() * 0.8;
    const clampX = (x) => Math.max(4, Math.min(w - SIZE - 4, x));
    const at = (t) => {
      const p = Math.min(1, t / T), s = Math.sin(p * Math.PI * waves * 2 + phase);
      let x, y;
      if (kind === 'cross') {
        x = dir > 0 ? -SIZE - 10 + p * (w + SIZE * 0.1 + 10) : w + 10 - p * (w + SIZE * 0.1 + 10);
        y = yHint - Math.sin(p * Math.PI) * (calm(ctx) ? 0 : 40) + s * 12;
      } else {
        const span = h + SIZE * 0.1 + 10;
        y = kind === 'fall' ? -SIZE - 10 + p * span : h + 10 - p * span;
        x = clampX(x0 + s * sway);
      }
      return { x, y, left: T - t, done: t >= T };
    };
    const start = at(0);
    /* The glint shows at the edge it comes in from, and motes trail behind it. */
    const glintAt = kind === 'fall' ? { x: start.x + SIZE / 2, y: 0, flat: true } : kind === 'rise' ? { x: start.x + SIZE / 2, y: h - 10, flat: true }
      : { x: dir > 0 ? 0 : w - 10, y: start.y + SIZE / 2, flat: false };
    const trail = kind === 'fall' ? [0, -1] : kind === 'rise' ? [0, 1] : [-dir, 0];
    return { kind, at, start, glintAt, trail, T };
  }

  /* ---- 1b. arrival: an edge glint about a second before entry ---- */
  function glint(path) {
    sfx(ctx, 'sfx/chime1.mp3', 0.22);
    const { x, y, flat } = path.glintAt, long = SIZE + 76;
    const at = flat ? `translate(${x - long / 2}px,${y}px)` : `translate(${x}px,${y - long / 2}px)`;
    const sc = flat ? 'scaleX' : 'scaleY';
    const g = spawn(ctx, `transform:${at};width:${flat ? long : 10}px;height:${flat ? 10 : long}px;border-radius:5px;background:linear-gradient(${flat ? 90 : 180}deg,transparent,${GOLD},transparent);box-shadow:0 0 22px ${GOLD};`);
    play(ctx, g, calm(ctx)
      ? [{ opacity: 0 }, { opacity: 0.8, offset: 0.4 }, { opacity: 0 }]
      : [{ opacity: 0, transform: `${at} ${sc}(.2)` }, { opacity: 1, transform: `${at} ${sc}(1.1)`, offset: 0.35 }, { opacity: 0, transform: `${at} ${sc}(.7)` }],
      { duration: 1000, easing: 'ease-out' });
  }

  /** Where the star is this frame. t is seconds since it set off, left the seconds before it would leave the screen.
   * Only the face turns; the shade stays put under the lamp. */
  function place(b, x, y, t, left) {
    const bob = calm(ctx) ? 0 : Math.sin(t * Math.PI * 0.9) * 6;
    const leaving = left < 3 ? Math.max(0.78, 0.78 + 0.22 * (left / 3)) : 1;
    b.style.transform = `translate(${x}px,${y + bob}px) scale(${leaving.toFixed(3)})`;
    if (!calm(ctx)) b.fzFace.style.transform = `rotate(${(b.fzSpin + (t / TURN_S) * 360).toFixed(2)}deg)`;
  }

  let moteAt = 0;
  function motes(x, y, now, trail = [-1, 0]) {
    if (calm(ctx) || now < moteAt) return;
    moteAt = now + 140;
    const [tx, ty] = trail, wig = Math.sin(now / 90) * 14;
    const ox = x + SIZE * (0.5 + tx * 0.3) + (ty ? wig : 0), oy = y + SIZE * (0.5 + ty * 0.3) + (tx ? wig : 0);
    const s = 3 + (now % 3);
    particle(ctx, `width:${s}px;height:${s}px;border-radius:50%;background:${GOLD};box-shadow:0 0 6px ${GOLD};`,
      [{ transform: `translate(${ox}px,${oy}px)`, opacity: 0.95 }, { transform: `translate(${ox + tx * 26 + (ty ? -8 : 0)}px,${oy + ty * 26 + (tx ? 18 : 0)}px) scale(.3)`, opacity: 0 }],
      { duration: 900, easing: 'ease-out' });
  }

  /* ---- 3. catch ---- */
  function counterCenter() {
    const r = ctx.parts.counter && frameRect(ctx.parts.counter);
    return r?.width ? { x: r.left + r.width / 2, y: r.top + r.height / 2, r } : null;
  }
  function flashCounter() {
    const c = ctx.parts.counter;
    if (!c?.isConnected) return;
    ctx.anim(c, calm(ctx)
      ? [{ opacity: 0.7 }, { opacity: 1 }]
      : [{ color: GOLD_DEEP, textShadow: `0 0 14px ${GOLD}`, scale: '1.08' }, { color: GOLD_DEEP, textShadow: `0 0 10px ${GOLD}`, scale: '1', offset: 0.4 }, {}],
      { duration: 900, easing: 'ease-out' });
  }
  function coinsToCounter(x, y, n = 7) {
    const c = counterCenter();
    if (!c) return;
    if (calm(ctx)) { flashCounter(); return; }
    let first = true;
    for (let i = 0; i < n; i++) {
      const dx = (i - (n - 1) / 2) * 12, delay = i * 55;
      const midX = (x + c.x) / 2 + dx * 2, midY = Math.min(y, c.y) - 70 - (i % 3) * 12;
      /* Little crayon stars, each turning its own way on the trip. */
      const r0 = (i * 47) % 360, r1 = r0 + (i % 2 ? 260 : -260);
      const d = particle(ctx, 'width:20px;height:20px;', [{ transform: `translate(${x - 10}px,${y - 10}px) rotate(${r0}deg) scale(.6)`, opacity: 0 }, { transform: `translate(${x - 10 + dx}px,${y - 33}px) rotate(${r0 + (r1 - r0) * 0.2}deg) scale(1.1)`, opacity: 1, offset: 0.2 }, { transform: `translate(${midX}px,${midY}px) rotate(${r0 + (r1 - r0) * 0.6}deg) scale(1)`, opacity: 1, offset: 0.6 }, { transform: `translate(${c.x - 10}px,${c.y - 10}px) rotate(${r1}deg) scale(.5)`, opacity: 0.2 }],
        { duration: 820, delay, easing: 'cubic-bezier(.3,0,.65,1)', fill: 'backwards' });
      if (d) { d.className = 'fz-gold-mini'; boil(d, STARS[lastStar], i); }
      if (d && first) { first = false; ctx.after(820 + delay, flashCounter); }
    }
  }
  /** The button that was pressed squashes, a ring snaps out, and it goes. The button node is the caller's to remove. */
  function catchHit(b, x, y) {
    b.style.pointerEvents = 'none';
    if (!calm(ctx)) {
      ctx.anim(b.fzFace, [{ scale: '1 1' }, { scale: '1.3 .72', offset: 0.3 }, { scale: '.9 1.12', offset: 0.65 }, { scale: '0 0' }], { duration: 230, easing: 'ease-out', fill: 'forwards' });
      rings(ctx, x, y, { color: GOLD, n: 2, size: 150 });
    } else {
      ctx.anim(b, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'forwards' });
      rings(ctx, x, y, { color: GOLD, n: 1, size: 150 });
    }
    coinsToCounter(x, y);
  }

  /* ---- 4. reveal: a short reel, then a rubber stamp ---- */
  const REEL = ['+', `x${frenzy}`, `x${deep}`, 'free'];
  function reveal(x, y, key, text, onDone) {
    const { w } = ctx.bounds();
    const sx = Math.max(150, Math.min(w - 150, x)), sy = Math.max(110, y - 70);
    const stamp = () => {
      sfx(ctx, 'sfx/thud.mp3', 0.35);
      const s = spawn(ctx, '', null);
      s.className = 'fz-gold-stamp';
      s.textContent = String(text).toUpperCase().replace(/ X([0-9])/g, ' x$1');
      const at = `translate(${sx}px,${sy}px) translate(-50%,-50%)`;
      play(ctx, s, calm(ctx)
        ? [{ opacity: 0, transform: `${at} rotate(-7deg)` }, { opacity: 1, transform: `${at} rotate(-7deg)`, offset: 0.2 }, { opacity: 1, transform: `${at} rotate(-7deg)`, offset: 0.8 }, { opacity: 0, transform: `${at} rotate(-7deg)` }]
        : [{ opacity: 0, transform: `${at} rotate(-14deg) scale(2.3)` }, { opacity: 1, transform: `${at} rotate(-6deg) scale(.92)`, offset: 0.09 }, { opacity: 1, transform: `${at} rotate(-7deg) scale(1)`, offset: 0.14 }, { opacity: 1, transform: `${at} rotate(-7deg) scale(1)`, offset: 0.82 }, { opacity: 0, transform: `${at} rotate(-7deg) scale(1.04)` }],
        { duration: 1900, easing: 'ease-out' });
      if (!calm(ctx)) {
        for (let i = 0; i < 6; i++) {
          const a = (i / 6) * Math.PI * 2, r = 70;
          particle(ctx, `width:6px;height:6px;border-radius:50%;background:${STAMP};opacity:.7;`,
            [{ transform: `translate(${sx}px,${sy}px)` }, { transform: `translate(${sx + Math.cos(a) * r}px,${sy + Math.sin(a) * r * 0.5}px) scale(.3)`, opacity: 0 }],
            { duration: 420, delay: 160, easing: 'ease-out', fill: 'backwards' });
        }
      }
      onDone?.(sx, sy);
    };
    if (calm(ctx)) { stamp(); return; }
    const box = spawn(ctx, `transform:translate(${sx - 32}px,${sy - 22}px);`);
    box.className = 'fz-gold-reel';
    const strip = doc.createElement('div');
    strip.className = 'strip';
    const land = ['lump', 'frenzy', 'deep', 'free'].indexOf(key);
    const cells = [...REEL, ...REEL, ...REEL].slice(0, 8 + land + 1);
    for (const c of cells) { const e = doc.createElement('div'); e.className = 'cell'; e.textContent = c; strip.appendChild(e); }
    box.appendChild(strip);
    const reel = ctx.anim(strip, [{ transform: 'translateY(0)' }, { transform: `translateY(${-(cells.length - 1) * 44}px)` }], { duration: 520, easing: 'cubic-bezier(.2,.6,.3,1.08)', fill: 'forwards' });
    const out = () => {
      play(ctx, box, [{ opacity: 1, transform: `translate(${sx - 32}px,${sy - 22}px)` }, { opacity: 0, transform: `translate(${sx - 32}px,${sy - 38}px)` }], { duration: 260, easing: 'ease-in' });
      stamp();
    };
    reel.onfinish = out;
    reel.oncancel = () => box.remove();
  }

  /* ---- 5. per-prize looks ---- */
  let ring = null, deepRing = null, veil = null, chip = null, lastSpark = 0, lastFloat = 0, chipLevel = 0;
  function ensure() {
    if (!ring) { ring = ctx.el('div', 'display:none;', null, 'over'); ring.className = 'fz-gold-ring'; }
    if (!deepRing) { deepRing = ctx.el('div', 'display:none;width:30px;height:30px;', null, 'over'); deepRing.className = 'fz-gold-ring'; }
    if (!veil) { veil = ctx.el('div', '', null, 'over'); veil.className = 'fz-gold-veil'; }
    if (!chip) { chip = ctx.el('div', 'display:none;', null, 'over'); chip.className = 'fz-gold-combo'; }
  }
  /** Free buy: a gold ticket flies to the shop and sticks for a moment. */
  function ticket(x, y) {
    const r = ctx.parts.shop?.getBoundingClientRect();
    const tx = r?.width ? r.left + Math.min(r.width - 40, 60) : x, ty = r?.width ? r.top + 24 : y - 120;
    const t = spawn(ctx, '');
    t.className = 'fz-gold-ticket';
    t.textContent = 'Free';
    play(ctx, t, calm(ctx)
      ? [{ opacity: 0, transform: `translate(${tx - 29}px,${ty - 15}px)` }, { opacity: 1, offset: 0.25, transform: `translate(${tx - 29}px,${ty - 15}px)` }, { opacity: 1, offset: 0.8, transform: `translate(${tx - 29}px,${ty - 15}px)` }, { opacity: 0, transform: `translate(${tx - 29}px,${ty - 15}px)` }]
      : [{ transform: `translate(${x - 29}px,${y - 15}px) rotate(0deg) scale(.6)`, opacity: 0 }, { transform: `translate(${(x + tx) / 2 - 29}px,${Math.min(y, ty) - 80}px) rotate(200deg) scale(1.15)`, opacity: 1, offset: 0.45 }, { transform: `translate(${tx - 29}px,${ty - 15}px) rotate(352deg) scale(1)`, opacity: 1, offset: 0.7 }, { transform: `translate(${tx - 29}px,${ty - 15}px) rotate(352deg) scale(1)`, opacity: 1, offset: 0.9 }, { transform: `translate(${tx - 29}px,${ty - 15}px) rotate(352deg) scale(.9)`, opacity: 0 }],
      { duration: 2200, easing: 'ease-out' });
  }
  /** A manual tap during Frenzy: gold sparks and a small multiple. */
  function tapSpark(x, y, mult, now) {
    if (now < lastSpark) return;
    lastSpark = now + (calm(ctx) ? 600 : 110);
    if (!calm(ctx)) {
      for (let i = 0; i < 4; i++) {
        const a = -Math.PI / 2 + (i - 1.5) * 0.5, d = 38 + i * 6;
        particle(ctx, `width:5px;height:5px;border-radius:50%;background:${GOLD};box-shadow:0 0 6px ${GOLD};`,
          [{ transform: `translate(${x}px,${y}px)` }, { transform: `translate(${x + Math.cos(a) * d}px,${y + Math.sin(a) * d}px) scale(.4)`, opacity: 0 }],
          { duration: 420, easing: 'ease-out' });
      }
    }
    const f = particle(ctx, `font:900 15px/1 Tahoma,Verdana,sans-serif;color:${GOLD_DEEP};text-shadow:0 1px 0 #fff3b0;white-space:nowrap;`,
      calm(ctx) ? [{ transform: `translate(${x + 18}px,${y - 34}px)`, opacity: 0 }, { opacity: 1, offset: 0.3 }, { transform: `translate(${x + 18}px,${y - 34}px)`, opacity: 0 }]
        : [{ transform: `translate(${x + 14}px,${y - 20}px) scale(.7)`, opacity: 0 }, { transform: `translate(${x + 18}px,${y - 44}px) scale(1.1)`, opacity: 1, offset: 0.3 }, { transform: `translate(${x + 20}px,${y - 62}px) scale(1)`, opacity: 0 }],
      { duration: 650, easing: 'ease-out' });
    if (f) f.textContent = `x${mult}`;
  }

  /** Every frame: the Frenzy ring on the button, the Deep veil and its small ring, Deep per-second floats, the combo chip. */
  function frame(now, { frenzyLeft, frenzyMs, deepLeft, deepMs, perSecond, fmt }) {
    ensure();
    const btn = frameRect(ctx.button.el);
    if (frenzyLeft > 0 && btn.width) {
      const s = Math.max(btn.width, btn.height) + 22;
      ring.style.display = 'block';
      ring.style.width = ring.style.height = `${s}px`;
      ring.style.transform = `translate(${btn.left + btn.width / 2 - s / 2}px,${btn.top + btn.height / 2 - s / 2}px)`;
      ring.style.setProperty('--p', (frenzyLeft / frenzyMs).toFixed(3));
    } else if (ring.style.display !== 'none') ring.style.display = 'none';
    const c = counterCenter();
    if (deepLeft > 0) {
      veil.classList.add('on');
      if (c) {
        deepRing.style.display = 'block';
        deepRing.style.transform = `translate(${c.r.right + 8}px,${c.y - 15}px)`;
        deepRing.style.setProperty('--p', (deepLeft / deepMs).toFixed(3));
      }
      if (c && now >= lastFloat) {
        lastFloat = now + 1000;
        const x = c.r.left + 10 + ((now / 1000) % 3) * (c.r.width / 3), y = c.r.bottom + 4;
        const f = particle(ctx, `font:700 14px/1 Tahoma,Verdana,sans-serif;color:${GOLD_DEEP};text-shadow:0 1px 0 #fff3b0;white-space:nowrap;`,
          calm(ctx) ? [{ transform: `translate(${x}px,${y}px)`, opacity: 0 }, { opacity: 1, offset: 0.3 }, { transform: `translate(${x}px,${y}px)`, opacity: 0 }]
            : [{ transform: `translate(${x}px,${y}px)`, opacity: 0 }, { transform: `translate(${x}px,${y - 14}px)`, opacity: 1, offset: 0.3 }, { transform: `translate(${x}px,${y - 34}px)`, opacity: 0 }],
          { duration: 950, easing: 'ease-out' });
        if (f) f.textContent = `+${fmt(perSecond)}`;
      }
    } else {
      veil.classList.remove('on');
      if (deepRing.style.display !== 'none') deepRing.style.display = 'none';
    }
    /* ---- 6. the combo chip: two buffs at once show what they make together ---- */
    const level = (frenzyLeft > 0 ? 1 : 0) + (deepLeft > 0 ? 1 : 0);
    const product = (frenzyLeft > 0 ? frenzy : 1) * (deepLeft > 0 ? deep : 1);
    if (level >= 2 && c) {
      chip.style.display = 'block';
      chip.textContent = `x${product}`;
      const heat = Math.min(1, Math.log10(product) / 2.5);
      chip.style.background = `linear-gradient(135deg, ${GOLD} 0%, color-mix(in srgb, ${GOLD_DEEP} ${Math.round((1 - heat) * 100)}%, ${STAMP}) 100%)`;
      chip.style.transform = `translate(${c.r.right + 48}px,${c.y - 20}px)`;
      if (chipLevel < level) {
        sfx(ctx, level >= 3 ? 'sfx/chime3.mp3' : 'sfx/chime2.mp3', 0.3);
        if (!calm(ctx)) ctx.anim(chip, [{ scale: '.4' }, { scale: '1.25', offset: 0.5 }, { scale: '1' }], { duration: 360, easing: 'ease-out' });
        else ctx.anim(chip, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 });
      }
    } else if (chip.style.display !== 'none') {
      const out = ctx.anim(chip, [{ opacity: 1 }, { opacity: 0 }], { duration: 300 });
      out.onfinish = () => { chip.style.display = 'none'; };
    }
    chipLevel = level;
  }

  /* ---- 7. miss: it slows, winks, and goes up in a puff ---- */
  function miss(b, x, y) {
    b.style.pointerEvents = 'none';
    b.classList.add('wink');
    const cx = x + SIZE / 2, cy = y + SIZE / 2;
    ctx.after(calm(ctx) ? 200 : 900, () => {
      if (!calm(ctx)) {
        for (let i = 0; i < 10; i++) {
          const a = (i / 10) * Math.PI * 2, d = 30 + (i % 3) * 10;
          particle(ctx, `width:5px;height:5px;border-radius:50%;background:${GOLD};`,
            [{ transform: `translate(${cx}px,${cy}px)`, opacity: 1 }, { transform: `translate(${cx + Math.cos(a) * d}px,${cy + Math.sin(a) * d}px) scale(.3)`, opacity: 0 }],
            { duration: 520, easing: 'ease-out' });
        }
      }
      const a = ctx.anim(b, [{ opacity: 1 }, { opacity: 0 }], { duration: calm(ctx) ? 1000 : 260, fill: 'forwards' });
      a.onfinish = a.oncancel = () => b.remove();
    });
  }

  return { makeButton, route, glint, place, motes, catchHit, reveal, ticket, tapSpark, frame, miss, size: SIZE };
}
