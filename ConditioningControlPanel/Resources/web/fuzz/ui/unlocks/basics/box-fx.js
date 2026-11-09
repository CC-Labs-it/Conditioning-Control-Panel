/* ui/unlocks/basics/box-fx.js - how a Mystery Box looks and feels (owner 2026-11-08: "redo the mystery box that falls every
 * so often, they are super basic graphic right now"). Pure presentation: never pays, never draws from the game's RNG (every
 * cosmetic wobble is Math.random), never decides anything. side.js owns timing, pay, sounds and the `box` event.
 * The look: a hand-drawn gift box in waxy crayon on a white paper cutout, chunky dark ink, tinted by the picked theme
 * (--accent body and lid, --accent-2 ribbon and bow, a paper "?" seal in --niche-ink), with a paper tag that holds the
 * label and the countdown. Every effect has an IN and an OUT: it drops and lands with squash and a dust puff, idles with a
 * jiggle and a lid peeking open on a warm glow, gets restless near the end, pops its lid and spills confetti when tapped,
 * and ties itself shut and sinks into a poof when missed. One lamp (top left): the shadow is its own element on the
 * ground and never tilts, squashes or travels with the box. Gentle visuals and reduced motion: fades only, 300 ms floor. */

import { docOf, spawn } from './fx.js';
import { CONFIG } from '../../../data/config.js';

const INK = '#2a2130';
const PAPER = '#fffdf5';
const SIZE = 84;

/* ---- colours: the theme lives in CSS vars; the box is baked into an SVG picture, so resolve them to real colours ---- */
function resolve(ctx) {
  const probe = spawn(ctx, 'visibility:hidden;');
  const doc = docOf(ctx), win = doc.defaultView;
  const read = (v, fb) => { probe.style.color = fb; probe.style.color = `var(${v}, ${fb})`; return win.getComputedStyle(probe).color || fb; };
  const out = { a: read('--accent', '#ff69b4'), b: read('--accent-2', '#7b5cff'), ink: read('--niche-ink', '#000') };
  probe.remove();
  /* The "?" sits on white paper: a pale theme ink would vanish there, so fall back to the box ink. */
  const m = out.ink.match(/[\d.]+/g);
  if (m && m.length >= 3) {
    const [r, g, b] = m.slice(0, 3).map(Number);
    const scale = out.ink.startsWith('color(') ? 255 : 1;
    if ((0.2126 * r + 0.7152 * g + 0.0722 * b) * scale > 150) out.ink = INK;
  }
  return out;
}

/* ---- the drawing ---- */
const P = (pts) => `M${pts.map((p) => p.join(' ')).join(' L')}Z`;
const FILTERS = (() => {
  const mask = (k, b) => `<feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 ${k} ${b}" result="m"/>`;
  const wob = (seed, f, s) => `<feTurbulence type="fractalNoise" baseFrequency="${f}" numOctaves="2" seed="${seed}" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="${s}" xChannelSelector="R" yChannelSelector="G" result="s"/>`;
  return `<filter id="cut" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="turbulence" baseFrequency="0.04" numOctaves="1" seed="9" result="w"/><feDisplacementMap in="SourceGraphic" in2="w" scale="3.5" xChannelSelector="R" yChannelSelector="G"/></filter>`
    + `<filter id="wax" x="-20%" y="-20%" width="140%" height="140%">${wob(3, 0.05, 2.2)}<feTurbulence type="fractalNoise" baseFrequency="0.08 1.1" numOctaves="2" seed="6" result="n"/>${mask(-3.2, 2.6)}<feComposite in="s" in2="m" operator="in"/></filter>`
    + `<filter id="line" x="-20%" y="-20%" width="140%" height="140%">${wob(12, 0.06, 2)}<feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="1" seed="5" result="n"/>${mask(-2.4, 2.2)}<feComposite in="s" in2="m" operator="in"/></filter>`;
})();
const svg = (body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="-4 -4 108 112"><defs>${FILTERS}</defs>${body}</svg>`;
/** Paper cutout under a shape, then crayon fills, then the ink gone over twice. */
const cutout = (d) => `<g filter="url(#cut)"><path d="${d}" fill="#d8cfb8" stroke="#d8cfb8" stroke-width="13.2" stroke-linejoin="round"/><path d="${d}" fill="${PAPER}" stroke="${PAPER}" stroke-width="12" stroke-linejoin="round"/></g>`;
const inked = (d, w = 3.4) => `<g filter="url(#line)"><path d="${d}" fill="none" stroke="${INK}" stroke-width="${w}" stroke-linejoin="round" stroke-linecap="round"/><path d="${d}" transform="translate(.7 -.5)" fill="none" stroke="${INK}" stroke-width="${(w * 0.45).toFixed(2)}" stroke-linejoin="round" stroke-linecap="round" opacity=".55"/></g>`;
const wax = (d, fill, extra = '') => `<g filter="url(#wax)"><path d="${d}" fill="${fill}" ${extra}/></g>`;

/* A box seen a little from the right: front face, side face, lid (front lip, side lip, top), ribbon both ways, bow on top. */
const FRONT = [[13, 51], [66, 54], [67, 99], [12, 97]];
const SIDE = [[66, 54], [88, 44], [89, 89], [67, 99]];
const LID_F = [[6, 37], [71, 40], [72, 55], [7, 53]];
const LID_S = [[71, 40], [94, 29], [94, 43], [72, 55]];
const LID_T = [[6, 37], [29, 26], [94, 29], [71, 40]];
const BODY_OUT = P([[13, 51], [66, 54], [88, 44], [89, 89], [67, 99], [12, 97]]);
const LID_OUT = P([[6, 37], [29, 26], [94, 29], [94, 43], [72, 55], [7, 53]]);

function bodySvg(c) {
  const ribF = P([[34, 52.5], [45, 53], [45.5, 98.4], [34.5, 97.9]]);
  const ribS = P([[66.5, 74], [88.5, 64], [88.7, 72], [66.7, 82]]);
  const shadeS = P(SIDE);
  const hatch = [];
  for (let i = 0; i < 7; i++) { const o = i * 7; hatch.push(`M${62 + o * 0.5} ${100 - o * 0.2} L${70 + o * 0.5} ${48 - o * 0.2}`); }
  /* the "?" seal on the ribbon */
  const seal = `<g transform="translate(40 76) rotate(-6)"><path d="M-13 1 Q-13 -13 0 -13 Q13 -13 13 0 Q13 13 0 13 Q-12 13 -13 1Z" fill="${PAPER}"/>`
    + `<path d="M-4.6 -4.2 Q-4.4 -9 0.4 -9 Q5.2 -9 5 -4.6 Q4.8 -1.4 1.2 0.2 Q-0.4 1 -0.3 3.6" fill="none" stroke="${c.ink}" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round"/>`
    + `<circle cx="-0.2" cy="8.2" r="2.2" fill="${c.ink}"/></g>`;
  /* the open mouth, hidden under the lid until it lifts: a dark inside with a lit back wall */
  const mouth = P([[13, 51], [35, 41.5], [88, 44], [66, 54]]);
  const back = P([[13, 51], [35, 41.5], [88, 44], [86, 49], [36, 46.5], [16, 54]]);
  return svg(cutout(P([[13, 51], [35, 41], [88, 44], [89, 89], [67, 99], [12, 97]]))
    + `<path d="${mouth}" fill="#2b1a33"/>` + `<path d="${back}" fill="${c.a}" opacity=".55"/>` + `<path d="${back}" fill="#1a0f22" opacity=".35"/>`
    + inked(mouth, 3)
    + wax(P(FRONT), c.a) + wax(shadeS, c.a)
    + `<path d="${shadeS}" fill="#1a0f22" opacity=".26"/>`
    + `<clipPath id="sd"><path d="${shadeS}"/></clipPath><g opacity=".35" clip-path="url(#sd)">${wax(hatch.join(' '), 'none', `stroke="#1a0f22" stroke-width="2.2" stroke-linecap="round"`)}</g>`
    + wax(ribF, c.b) + wax(ribS, c.b) + `<path d="${ribS}" fill="#1a0f22" opacity=".2"/>`
    + `<path d="M17 58 Q19 70 17 82" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" opacity=".55"/>`
    + inked(ribF, 2.4) + inked(ribS, 2.2)
    + `<g filter="url(#line)">${seal.replace(`fill="${PAPER}"/>`, `fill="${PAPER}" stroke="${INK}" stroke-width="2.6"/>`)}</g>`
    + inked(P(FRONT)) + inked(P(SIDE)) + inked(BODY_OUT, 3.8));
}

function lidSvg(c) {
  const ribF = P([[33, 38.5], [45, 39], [45.4, 54.4], [33.4, 53.9]]);
  const ribT = P([[33, 38.5], [45, 39], [62, 28.4], [51, 28]]);
  const ribX = P([[17, 31.5], [83, 34.5], [82, 36.6], [16, 33.6]]);
  const ribS = P([[71.6, 46], [94, 35], [94, 39], [71.8, 50]]);
  /* the bow: two loops, two tails, a knot, sitting on the crossing */
  const loopL = 'M48 32 Q30 30 28 21 Q30 13 39 17 Q46 21 48 32Z';
  const loopR = 'M50 32 Q62 16 70 18 Q76 24 68 29 Q60 33 50 32Z';
  const tails = 'M47 33 Q40 40 36 44 L41 45 Q45 40 48 34Z M51 33 Q57 38 63 41 L60 44 Q55 39 50 34Z';
  const knot = 'M44 28 Q49 25 53 28 Q55 33 50 35 Q44 35 44 28Z';
  /* Owner 2026-11-08: no paper strip under the lid. Its cutout is clipped at the lid's bottom edge, so a closed lid sits
   * straight on the body (lid, ink, body) instead of lid, white strip, body. */
  const above = P([[-4, -4], [104, -4], [104, 44], [94, 44], [72, 56], [7, 54], [-4, 54]]);
  return svg(`<clipPath id="lidcut"><path d="${above}"/></clipPath><g clip-path="url(#lidcut)">${cutout(LID_OUT)}</g>` + cutout(loopL) + cutout(loopR)
    + wax(P(LID_T), c.a) + `<path d="${P(LID_T)}" fill="#fff" opacity=".22"/>`
    + wax(P(LID_F), c.a) + wax(P(LID_S), c.a) + `<path d="${P(LID_S)}" fill="#1a0f22" opacity=".26"/>`
    + wax(ribT, c.b) + wax(ribX, c.b) + wax(ribF, c.b) + wax(ribS, c.b) + `<path d="${ribS}" fill="#1a0f22" opacity=".2"/>`
    + inked(ribT, 2) + inked(ribX, 2) + inked(ribF, 2.2) + inked(ribS, 2)
    + inked(P(LID_T), 3) + inked(P(LID_F)) + inked(P(LID_S)) + inked(LID_OUT, 3.8)
    + wax(tails, c.b) + inked(tails, 2.6)
    + wax(loopL, c.b) + wax(loopR, c.b) + `<path d="M33 21 Q37 19 42 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" opacity=".6"/>`
    + inked(loopL, 3) + inked(loopR, 3)
    + wax(knot, c.b) + `<path d="${knot}" fill="#1a0f22" opacity=".15"/>` + inked(knot, 2.8));
}

const urlOf = (s) => `url("data:image/svg+xml,${encodeURIComponent(s)}")`;
const cache = new Map();
function art(ctx) {
  const c = resolve(ctx), key = `${c.a}|${c.b}|${c.ink}`;
  if (!cache.has(key)) {
    if (cache.size > 8) cache.clear();
    cache.set(key, { body: urlOf(bodySvg(c)), lid: urlOf(lidSvg(c)), a: c.a, b: c.b });
  }
  return cache.get(key);
}

const CSS = `
.fz-box { position: absolute; width: ${SIZE}px; height: ${SIZE + 4}px; padding: 0; margin: 0; border: 0; background: none; pointer-events: auto; touch-action: manipulation; cursor: pointer; -webkit-tap-highlight-color: transparent; }
.fz-box:focus-visible { outline: 3px solid var(--stamp); outline-offset: 2px; border-radius: 10px; }
.fz-box .shade { position: absolute; left: 20px; top: ${SIZE - 9}px; width: 66px; height: 15px; border-radius: 50%; background: rgba(42, 22, 46, .3); filter: blur(4px); transform-origin: 40% 50%; }
.fz-box .rig { position: absolute; inset: 0; transform-origin: 50% 92%; will-change: transform; }
.fz-box .body, .fz-box .lid { position: absolute; inset: 0; background: center / contain no-repeat; }
.fz-box .lid { transform-origin: 8% 47%; }
.fz-box .glow { position: absolute; left: 6px; top: 6px; width: 76px; height: 46px; border-radius: 50%; opacity: 0; background: radial-gradient(ellipse at 50% 70%, rgba(255, 246, 196, .95), rgba(255, 214, 92, .55) 40%, transparent 70%); }
.fz-box .tag { position: absolute; top: 34px; display: flex; flex-direction: column; gap: 3px; padding: 4px 6px 5px 11px; min-width: 50px; max-width: 74px; box-sizing: border-box; background: ${PAPER}; color: ${INK}; border: 2px solid ${INK}; border-radius: 3px 6px 6px 3px; clip-path: polygon(7px 0, 100% 0, 100% 100%, 7px 100%, 0 50%); font: 700 10px/1.05 Tahoma, Verdana, sans-serif; text-align: left; white-space: normal; box-shadow: 2px 2px 0 rgba(42, 33, 48, .25); transform-origin: 0 50%; }
.fz-box .tag::before { content: ""; position: absolute; left: 6px; top: 50%; width: 4px; height: 4px; margin-top: -2px; border-radius: 50%; background: ${INK}; }
.fz-box .tag.right { left: 76px; rotate: 9deg; }
.fz-box .tag.left { right: 76px; rotate: -9deg; transform-origin: 100% 50%; clip-path: polygon(0 0, calc(100% - 7px) 0, 100% 50%, calc(100% - 7px) 100%, 0 100%); padding: 4px 11px 5px 6px; border-radius: 6px 3px 3px 6px; }
.fz-box .tag.left::before { left: auto; right: 6px; }
.fz-box .string { position: absolute; top: 40px; width: 12px; height: 2px; background: ${INK}; }
.fz-box .tag.right + .string { left: 68px; rotate: 18deg; }
.fz-box .tag.left + .string { left: 4px; rotate: -18deg; }
.fz-box .fuse { position: relative; height: 5px; border: 1.5px solid ${INK}; border-radius: 3px; background: ${PAPER}; overflow: hidden; }
.fz-box .fuse i { position: absolute; inset: 0; background: var(--fz-box-b); transform-origin: 0 50%; transition: background-color .4s; }
.fz-box.live .tag { animation: fzBoxSwing 2.6s ease-in-out infinite; }
.fz-box.late .tag { animation-duration: .7s; }
.fz-box.late .fuse i { background: var(--stamp); }
.fz-box.late .glow { animation: fzBoxGlow .8s ease-in-out infinite; }
@keyframes fzBoxSwing { 0%, 100% { transform: rotate(-5deg); } 50% { transform: rotate(5deg); } }
@keyframes fzBoxGlow { 0%, 100% { opacity: .15; } 50% { opacity: .6; } }
body.gentle .fz-box .tag, body.gentle .fz-box .glow, .fz-box.calm .tag, .fz-box.calm .glow { animation: none !important; }
@media (prefers-reduced-motion: reduce) { .fz-box .tag, .fz-box .glow { animation: none !important; } }
`;

const calm = (ctx) => ctx.gentle || ctx.reduced;

let live = 0;
const room = () => live < Math.max(6, Math.floor((CONFIG.MAX_PARTICLES || 60) / 2));
function particle(ctx, css, frames, opts) {
  if (!room()) return null;
  const d = spawn(ctx, css);
  live += 1;
  const a = ctx.anim(d, frames, opts);
  a.onfinish = a.oncancel = () => { d.remove(); live -= 1; };
  return d;
}

export function createBoxFx(ctx) {
  const doc = docOf(ctx);
  ctx.css(CSS);
  const mk = (cls, parent) => { const e = doc.createElement(cls === 'fuse-i' ? 'i' : 'span'); if (cls !== 'fuse-i') e.className = cls; parent.appendChild(e); return e; };

  /** Builds one box at (x, y) on the over layer. Returns a handle with the moves side.js plays. */
  function make(x, y, label) {
    const { w } = ctx.bounds();
    const look = art(ctx);
    const b = ctx.el('button', `left:${x}px;top:${y}px;`, null, 'over');
    b.type = 'button';
    b.className = calm(ctx) ? 'fz-box calm' : 'fz-box';
    b.setAttribute('aria-label', `Mystery box: ${label}`);
    b.style.setProperty('--fz-box-b', look.b);
    const shade = mk('shade', b), rig = mk('rig', b);
    const glow = mk('glow', rig), body = mk('body', rig), lid = mk('lid', rig);
    body.style.backgroundImage = look.body;
    lid.style.backgroundImage = look.lid;
    /* the paper tag hangs on whichever side has room */
    const side = x + SIZE / 2 > w / 2 ? 'left' : 'right';
    const tag = mk(`tag ${side}`, rig);
    const text = doc.createElement('span'); text.textContent = label; tag.appendChild(text);
    const fuse = mk('fuse', tag), fill = mk('fuse-i', fuse);
    mk('string', rig);
    /* glow sits behind the lid but above the body's top edge */
    rig.insertBefore(glow, lid);

    let gone = false, stopIdle = null;
    const cx = x + SIZE / 2, groundY = y + SIZE - 2;

    /* ---- IN ---- */
    function arrive() {
      if (calm(ctx)) {
        ctx.anim(b, [{ opacity: 0 }, { opacity: 1 }], { duration: 400, easing: 'ease-out' });
        b.classList.add('live');
        return 400;
      }
      const fall = Math.max(260, Math.min(460, 220 + y * 0.6));
      const lift = -(y + SIZE + 40);
      ctx.anim(rig, [
        { transform: `translateY(${lift}px) rotate(-8deg) scale(.92,1.1)` },
        { transform: 'translateY(0) rotate(0deg) scale(.94,1.08)', offset: 0.62, easing: 'ease-out' },
        { transform: 'scale(1.24,.74)', offset: 0.72, easing: 'ease-out' },
        { transform: 'scale(.9,1.12)', offset: 0.84, easing: 'ease-in-out' },
        { transform: 'scale(1.04,.97)', offset: 0.93 },
        { transform: 'none' },
      ], { duration: fall / 0.62, easing: 'ease-in' });
      ctx.anim(shade, [{ transform: 'scale(.25)', opacity: 0 }, { transform: 'scale(1)', opacity: 1, offset: 0.62 }, { transform: 'scale(1.18,1)', offset: 0.72 }, { transform: 'scale(1)' }], { duration: fall / 0.62, easing: 'ease-in' });
      ctx.anim(lid, [{ transform: 'none' }, { transform: 'none', offset: 0.6 }, { transform: 'translateY(-7px) rotate(-6deg)', offset: 0.74 }, { transform: 'none', offset: 0.86 }], { duration: fall / 0.62 });
      ctx.after(fall, () => { if (!gone) dust(); });
      ctx.after(fall / 0.62, () => { if (!gone) b.classList.add('live'); });
      return fall;
    }
    function dust() {
      for (let i = 0; i < 8; i++) {
        const dir = i % 2 ? 1 : -1, s = 7 + Math.random() * 8, sx = cx + dir * (14 + Math.random() * 18), sy = groundY - 4;
        const ex = sx + dir * (18 + Math.random() * 26), ey = sy - 6 - Math.random() * 12;
        particle(ctx, `width:${s.toFixed(1)}px;height:${s.toFixed(1)}px;border-radius:50%;background:#efe6d2;border:1.5px solid rgba(42,33,48,.35);`,
          [{ transform: `translate(${sx}px,${sy}px) scale(.5)`, opacity: 0.95 }, { transform: `translate(${ex}px,${ey}px) scale(1.25)`, opacity: 0 }],
          { duration: 420 + Math.random() * 180, easing: 'cubic-bezier(.2,.7,.3,1)' });
      }
    }

    /* ---- WAIT ---- */
    function countdown(ms) {
      ctx.anim(fill, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: ms, easing: 'linear', fill: 'forwards' });
      ctx.after(Math.max(0, ms - 2000), () => { if (!gone) b.classList.add('late'); });
    }
    function idle(startMs, endMs) {
      if (calm(ctx)) {
        /* a slow warm glow behind the lid, no movement */
        ctx.anim(glow, [{ opacity: 0 }, { opacity: 0.45 }], { duration: 1200, delay: 300, fill: 'forwards' });
        return;
      }
      let at = startMs + 250, peek = Math.random() < 0.5;
      const tick = () => {
        if (gone) return;
        const late = at >= endMs - 2000;
        if (peek && !late) {
          ctx.anim(lid, [{ transform: 'none' }, { transform: 'translate(-1px,-5px) rotate(-11deg)', offset: 0.3 }, { transform: 'translate(-1px,-4px) rotate(-9deg)', offset: 0.7 }, { transform: 'none' }], { duration: 900, easing: 'ease-in-out' });
          ctx.anim(glow, [{ opacity: 0 }, { opacity: 0.95, offset: 0.35 }, { opacity: 0.8, offset: 0.7 }, { opacity: 0 }], { duration: 900 });
        } else {
          const k = late ? 1.6 : 1;
          ctx.anim(rig, [{ transform: 'none' }, { transform: `rotate(${-4 * k}deg) scale(1.03,.97)`, offset: 0.2 }, { transform: `rotate(${4 * k}deg)`, offset: 0.45 }, { transform: `rotate(${-2 * k}deg)`, offset: 0.7 }, { transform: 'none' }], { duration: late ? 380 : 460, easing: 'ease-in-out' });
          if (late) ctx.anim(lid, [{ transform: 'none' }, { transform: 'translateY(-4px) rotate(-5deg)', offset: 0.4 }, { transform: 'none' }], { duration: 380 });
        }
        peek = !peek;
        const gap = late ? 520 + Math.random() * 120 : 1100 + Math.random() * 900;
        at += gap;
        if (at < endMs - 200) stopIdle = ctx.after(gap, tick);
      };
      stopIdle = ctx.after(at, tick);
    }

    /* ---- OUT: tapped ---- */
    function open() {
      gone = true;
      stopIdle?.();
      b.style.pointerEvents = 'none';
      b.classList.remove('live', 'late');
      const mouthX = cx, mouthY = y + SIZE * 0.42;
      if (calm(ctx)) {
        ctx.anim(lid, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'forwards' });
        ctx.anim(glow, [{ opacity: 0.4 }, { opacity: 1, offset: 0.4 }, { opacity: 0 }], { duration: 700, fill: 'forwards' });
        const a = ctx.anim(b, [{ opacity: 1 }, { opacity: 1, offset: 0.4 }, { opacity: 0 }], { duration: 750, fill: 'forwards' });
        a.onfinish = a.oncancel = () => b.remove();
        return { x: mouthX, y: mouthY };
      }
      const dir = Math.random() < 0.5 ? -1 : 1;
      ctx.anim(lid, [
        { transform: 'none', opacity: 1 },
        { transform: `translate(${dir * 18}px,-58px) rotate(${dir * 28}deg)`, opacity: 1, offset: 0.4, easing: 'ease-in' },
        { transform: `translate(${dir * 40}px,-20px) rotate(${dir * 70}deg)`, opacity: 0 },
      ], { duration: 700, easing: 'cubic-bezier(.2,.8,.4,1)', fill: 'forwards' });
      ctx.anim(glow, [{ opacity: 0.6, transform: 'scale(1)' }, { opacity: 1, transform: 'translateY(-10px) scale(1.4,1.6)', offset: 0.25 }, { opacity: 0, transform: 'translateY(-16px) scale(1.2,1.3)' }], { duration: 600, fill: 'forwards' });
      ctx.anim(tag, [{ opacity: 1 }, { opacity: 0, transform: 'translateY(16px) rotate(20deg)' }], { duration: 380, easing: 'ease-in', fill: 'forwards' });
      const body_ = ctx.anim(rig, [
        { transform: 'none', opacity: 1 },
        { transform: 'scale(1.18,.8)', opacity: 1, offset: 0.14 },
        { transform: 'scale(.94,1.08)', opacity: 1, offset: 0.32 },
        { transform: 'scale(1)', opacity: 1, offset: 0.6 },
        { transform: 'translateY(6px) scale(.7)', opacity: 0 },
      ], { duration: 900, easing: 'ease-out', fill: 'forwards' });
      ctx.anim(shade, [{ opacity: 1 }, { opacity: 1, offset: 0.6 }, { opacity: 0, transform: 'scale(.6)' }], { duration: 900, fill: 'forwards' });
      body_.onfinish = body_.oncancel = () => b.remove();
      /* light beam up out of the box */
      const beam = particle(ctx, `width:46px;height:120px;margin-left:-23px;background:linear-gradient(to top, rgba(255,240,170,.85), rgba(255,240,170,0));clip-path:polygon(28% 100%, 72% 100%, 100% 0, 0 0);transform-origin:50% 100%;`,
        [{ transform: `translate(${mouthX}px,${mouthY - 120}px) scaleY(.2)`, opacity: 0 }, { transform: `translate(${mouthX}px,${mouthY - 120}px) scaleY(1)`, opacity: 1, offset: 0.25 }, { transform: `translate(${mouthX}px,${mouthY - 120}px) scaleY(1.1)`, opacity: 0 }],
        { duration: 650, easing: 'ease-out' });
      void beam;
      /* paper confetti spills out of the mouth and tumbles down */
      const cols = [look.a, look.b, '#ffd84a', PAPER, '#7fd6ff'];
      for (let i = 0; i < 18; i++) {
        const ang = -Math.PI / 2 + (Math.random() - 0.5) * 1.9, v = 70 + Math.random() * 80;
        const px = mouthX + Math.cos(ang) * v, py = mouthY + Math.sin(ang) * v;
        const fx = px + (Math.random() - 0.5) * 40, fy = py + 70 + Math.random() * 60;
        const r0 = Math.random() * 360, r1 = r0 + (Math.random() < 0.5 ? -1 : 1) * (260 + Math.random() * 300);
        const wq = 6 + Math.random() * 5, hq = 4 + Math.random() * 4;
        particle(ctx, `width:${wq.toFixed(1)}px;height:${hq.toFixed(1)}px;margin:${(-hq / 2).toFixed(1)}px 0 0 ${(-wq / 2).toFixed(1)}px;background:${cols[i % cols.length]};border:1.5px solid ${INK};border-radius:1px;`,
          [{ transform: `translate(${mouthX}px,${mouthY}px) rotate(${r0}deg) scale(.4)`, opacity: 1 },
            { transform: `translate(${px}px,${py}px) rotate(${(r0 + r1) / 2}deg) scale(1)`, opacity: 1, offset: 0.35, easing: 'ease-in' },
            { transform: `translate(${fx}px,${fy}px) rotate(${r1}deg) scaleX(.3)`, opacity: 0 }],
          { duration: 1100 + Math.random() * 400, delay: i * 12, easing: 'cubic-bezier(.15,.7,.35,1)', fill: 'backwards' });
      }
      return { x: mouthX, y: mouthY };
    }

    /* ---- OUT: missed. The lid shuts tight, the box sighs, sinks and goes up in a little grey poof. ---- */
    function expire() {
      gone = true;
      stopIdle?.();
      b.style.pointerEvents = 'none';
      b.classList.remove('live', 'late');
      if (calm(ctx)) {
        const a = ctx.anim(b, [{ opacity: 1 }, { opacity: 0 }], { duration: 700, fill: 'forwards' });
        a.onfinish = a.oncancel = () => b.remove();
        return;
      }
      ctx.anim(lid, [{ transform: 'none' }, { transform: 'translateY(3px) scaleY(.92)', offset: 0.2 }, { transform: 'translateY(2px)' }], { duration: 400, fill: 'forwards' });
      ctx.anim(tag, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(55deg) translateY(4px)', offset: 0.5 }, { transform: 'rotate(70deg) translateY(10px)', opacity: 0 }], { duration: 900, easing: 'ease-in', fill: 'forwards' });
      const a = ctx.anim(rig, [
        { transform: 'none', opacity: 1, filter: 'saturate(1)' },
        { transform: 'scale(1.06,.9)', opacity: 1, filter: 'saturate(.4)', offset: 0.25 },
        { transform: 'scale(.98,.94)', opacity: 1, filter: 'saturate(.3)', offset: 0.55 },
        { transform: 'translateY(10px) scale(.55,.4)', opacity: 0, filter: 'saturate(0)' },
      ], { duration: 950, easing: 'ease-in', fill: 'forwards' });
      ctx.anim(shade, [{ transform: 'scale(1)', opacity: 1 }, { transform: 'scale(.5)', opacity: 0 }], { duration: 950, easing: 'ease-in', fill: 'forwards' });
      a.onfinish = a.oncancel = () => b.remove();
      ctx.after(600, () => {
        for (let i = 0; i < 9; i++) {
          const ang = (i / 9) * Math.PI * 2 + Math.random() * 0.4, d = 18 + Math.random() * 16, s = 9 + Math.random() * 9;
          const sx = cx, sy = groundY - 22;
          particle(ctx, `width:${s.toFixed(1)}px;height:${s.toFixed(1)}px;margin:${(-s / 2).toFixed(1)}px 0 0 ${(-s / 2).toFixed(1)}px;border-radius:50%;background:#d9d3cb;border:1.5px solid rgba(42,33,48,.4);`,
            [{ transform: `translate(${sx}px,${sy}px) scale(.4)`, opacity: 0.95 }, { transform: `translate(${sx + Math.cos(ang) * d}px,${sy + Math.sin(ang) * d * 0.7 - 10}px) scale(1.3)`, opacity: 0 }],
            { duration: 520 + Math.random() * 200, easing: 'ease-out' });
        }
      });
    }

    return { el: b, arrive, countdown, idle, open, expire, center: { x: cx, y: y + SIZE / 2 } };
  }

  return { make, size: SIZE };
}
