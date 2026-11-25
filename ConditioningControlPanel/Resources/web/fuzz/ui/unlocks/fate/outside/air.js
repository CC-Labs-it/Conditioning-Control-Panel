/* ui/unlocks/fate/outside/air.js - Fresh Air (ft_air, Go Outside card 2). Now and then a breeze crosses the page: a few
 * leaves and a scrap of paper tumble across, and the things on the page (dock objects, shop tiles) lean a few pixels the
 * way the wind blows as the gust front passes them, then settle. Every lean is the individual `rotate` / `translate`
 * properties with composite 'add', so it stacks on whatever the tile or object is already doing and never touches the
 * shop's own box or any transform. Some gusts bring a bird: a short chirp phrase, synthesised the way the Go Outside show
 * sings its birds (ui/unlocks/extras/outside-show.js), very quiet, on its own mixer source. No new sound files.
 * Gentle and reduced motion: no flying, no leaning; two leaves fade in and out where they lie. */

import { BREEZE, between, gustDelay } from './rules.js';
import { flyerUrl } from './art.js';
import { LITE, frameRect } from '../../../perf.js';
import { fresh } from '../../basics/fx.js';

/* Go Outside's bird songs (outside-show.js SONGS), one phrase at a time. b = pitch. */
const SONGS = [
  (b) => Array.from({ length: 3 + Math.floor(Math.random() * 3) }, (_, i) => ({ dt: i * 0.13, f0: 4300 * b, f1: 2700 * b, dur: 0.075 })),
  (b) => [{ dt: 0, f0: 2300 * b, f1: 2700 * b, dur: 0.24 }, { dt: 0.32, f0: 2900 * b, f1: 1950 * b, dur: 0.3 }],
  (b) => [{ dt: 0, f0: 5200 * b, f1: 5600 * b, dur: 0.05 }, { dt: 0.09, f0: 5300 * b, f1: 3900 * b, dur: 0.12 }, { dt: 0.4, f0: 5200 * b, f1: 3800 * b, dur: 0.12 }],
];

function sing(io, pan) {
  const ac = io.ac, t0 = ac.currentTime + 0.05;
  const song = SONGS[Math.floor(Math.random() * SONGS.length)](0.92 + Math.random() * 0.16);
  for (const n of song) {
    const t = t0 + n.dt, o = ac.createOscillator(), g = ac.createGain();
    const p = ac.createStereoPanner ? ac.createStereoPanner() : null;
    o.type = 'sine';
    o.frequency.setValueAtTime(n.f0, t);
    o.frequency.exponentialRampToValueAtTime(Math.max(200, n.f1), t + n.dur);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(0.12, t + Math.min(0.012, n.dur / 3));
    g.gain.exponentialRampToValueAtTime(0.0001, t + n.dur);
    if (p) { p.pan.value = pan; o.connect(g).connect(p).connect(io.node); } else o.connect(g).connect(io.node);
    o.start(t); o.stop(t + n.dur + 0.02);
  }
}

export function mountAir(ctx) {
  const still = () => ctx.gentle || ctx.reduced;
  const sky = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;', null, 'fx');
  const doc = sky.ownerDocument;
  let io = null, over = false;
  ctx.cleanup(() => { over = true; try { io?.stop(); } catch { /* gone */ } io = null; });
  ctx.on('ending', () => { over = true; });

  const bird = (dir) => {
    if (!ctx.has('sound') || Math.random() > BREEZE.birdChance) return;
    io ||= ctx.audio.input?.({ bus: 'ambient', gain: 0.5, sourceLabel: 'Birdsong' }) || null;
    if (io) sing(io, -dir * (0.3 + Math.random() * 0.4));
  };

  /* One leaf: an image that lives as long as its animation. */
  const flyer = (css) => {
    const e = doc.createElement('img');
    e.alt = ''; e.draggable = false;
    e.src = flyerUrl(Math.random() < 0.22 ? 2 : Math.random() < 0.5 ? 1 : 0);
    const size = 18 + Math.round(Math.random() * 12);
    e.style.cssText = `position:absolute;left:0;top:0;width:${size}px;height:auto;pointer-events:none;${css}`;
    sky.appendChild(e);
    return e;
  };
  const done = (a, e) => { a.onfinish = a.oncancel = () => e.remove(); };

  const leaves = (dir) => {
    const { w, h } = ctx.bounds();
    const n = LITE ? BREEZE.liteLeaves : BREEZE.leaves;
    for (let i = 0; i < n; i++) {
      if (still()) {
        if (i > 1) break;
        const e = flyer(`transform:translate(${Math.round(w * (dir > 0 ? 0.08 + Math.random() * 0.2 : 0.72 + Math.random() * 0.2))}px,${Math.round(h * (0.3 + Math.random() * 0.4))}px) rotate(${Math.round(Math.random() * 360)}deg);opacity:0;`);
        done(ctx.anim(e, [{ opacity: 0 }, { opacity: 0.9, offset: 0.3 }, { opacity: 0.9, offset: 0.7 }, { opacity: 0 }], { duration: 2600, delay: i * 500, easing: 'ease-in-out' }), e);
        continue;
      }
      const e = flyer('');
      const y0 = h * (0.12 + Math.random() * 0.7), amp = 16 + Math.random() * 26, spin = (220 + Math.random() * 400) * dir;
      const fromX = dir > 0 ? -40 : w + 40, toX = dir > 0 ? w + 40 : -40;
      const frames = [];
      for (let k = 0; k <= 6; k++) {
        const t = k / 6;
        frames.push({ transform: `translate(${Math.round(fromX + (toX - fromX) * t)}px,${Math.round(y0 + Math.sin(t * Math.PI * 2.2) * amp + t * 30)}px) rotate(${Math.round(spin * t)}deg)` });
      }
      done(ctx.anim(e, frames, { duration: 4300 + Math.random() * 2200, delay: Math.random() * 900, easing: 'ease-in-out' }), e);
    }
  };

  /* The things the gust passes: dock objects on both rails, then the visible shop tiles. Read once a gust. */
  const targets = () => {
    const out = [];
    for (const side of ['left', 'right']) {
      const rail = ctx.layer('side', side);
      if (rail) for (const el of rail.children) if (el.matches?.('section.dk')) out.push(el);
    }
    const shop = ctx.parts.shop;
    if (shop) for (const el of shop.querySelectorAll('.row')) out.push(el);
    const { w, h } = ctx.bounds();
    const seen = [];
    for (const el of out) {
      if (seen.length >= BREEZE.maxLean) break;
      const r = frameRect(el);
      if (r.width < 4 || r.height < 4 || r.right < 0 || r.left > w || r.bottom < 0 || r.top > h) continue;
      seen.push({ el, x: r.left + r.width / 2, big: r.width > 160 });
    }
    return seen;
  };

  const lean = (dir) => {
    const { w } = ctx.bounds();
    for (const { el, x, big } of targets()) {
      const k = (big ? 0.55 : 1) * (0.6 + Math.random() * 0.4);
      const deg = BREEZE.leanDeg * dir * k, px = BREEZE.leanPx * dir * k;
      ctx.anim(el, [
        { rotate: '0deg', translate: '0px 0px' },
        { rotate: `${deg.toFixed(2)}deg`, translate: `${px.toFixed(1)}px 0px`, offset: 0.28 },
        { rotate: `${(-deg * 0.45).toFixed(2)}deg`, translate: `${(-px * 0.3).toFixed(1)}px 0px`, offset: 0.56 },
        { rotate: `${(deg * 0.16).toFixed(2)}deg`, translate: '0px 0px', offset: 0.8 },
        { rotate: '0deg', translate: '0px 0px' },
      ], { duration: 1700, delay: gustDelay(x, w, dir) + 300, easing: 'ease-in-out', composite: 'add' });
    }
  };

  const gust = () => {
    if (over) return;
    const dir = Math.random() < 0.5 ? 1 : -1;
    leaves(dir);
    if (!still()) lean(dir);
    ctx.emit('breeze', { dir, crossMs: BREEZE.crossMs });
    bird(dir);
  };
  const next = (ms) => ctx.after(ms, () => { gust(); next(between(BREEZE.gapMs, Math.random())); });
  next(fresh(ctx) ? BREEZE.arriveMs : between(BREEZE.firstMs, Math.random()));
  return {};
}
