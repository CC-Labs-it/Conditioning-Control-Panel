/* ui/adcard/stage.js - the Living Ad ending around the card (owner 2026-11-09: "more cinematic ... no SFX, no tweening
 * between when we end and the card showing up, also we got a empty background behind").
 *
 *   livingAdTake(ctx)   the take phase (ENGINE.md `take`): the live page is printed into an ad. Crop marks close in on a
 *                       shutter snap, the page shrinks into a paper print, a PRINTED stamp lands, a printer slot rises
 *                       and the print feeds down into it. The real layers are only transformed (WAAPI, composite add),
 *                       so the engine's cleanup after the fade puts them back as they were.
 *   createStage(...)    the paper stage behind the card: theme rays turning slowly, halftone corners, the printer slot,
 *                       crayon Golden Stars drifting and line-boiling, clip-art stickers that play their clip now and then.
 *   CUES / cue()        every sound of the ending, one table (time -> file -> gain is in the hand-back notes).
 *
 * Gentle visuals and reduced motion: fades only; nothing turns, drifts, boils or squashes. One lamp (top left): shadows sit
 * down-right and never turn with a piece. Cosmetic randomness is Math.random; the run's RNG is untouched. */

import { THEME_PALETTES, secondTheme, mixHex } from '../palette.js';
import { cardColours } from './model.js';
import { crayonStarSvg, starVariant, LOOPY, RUSHED, BOIL_FRAMES } from '../unlocks/basics/gold-fx.js';
import { clips } from '../../assets/clipart/clips.js';
import { ADCARD_COPY } from '../../data/content/adcard.js';
import { loadCardCss } from './card.js';

const CSS_URL = new URL('./stage.css', import.meta.url).href;
const CLIPART = new URL('../../assets/clipart/', import.meta.url).href;
const SFX = 'assets/audio/sfx/', POP = 'assets/audio/pop/', PLUCK = (n) => `assets/audio/pluck/wood_${String(n).padStart(2, '0')}.wav`;

/** Every sound of the ending: [file, gain] on the sfx bus. A few hits, never a wall; no clip plays twice at once. */
export const CUES = Object.freeze({
  shutter: [SFX + 'snap.mp3', 0.5],          // take: the crop marks close in
  printStamp: [SFX + 'thud.mp3', 0.5],       // take: PRINTED lands on the print
  slot: [SFX + 'chip_pop.mp3', 0.22],        // take: the printer slot comes up
  feed: [SFX + 'tally_tick.mp3', 0.15],      // take: the print feeds in (three ticks)
  title: [SFX + 'thud.mp3', 0.36],           // show: the title stamp
  printer: [SFX + 'count_tick.mp3', 0.16],   // show: the printer works (two ticks)
  land: [POP + 'Pop2.mp3', 0.3],             // show: the poster lands
  logo: [SFX + 'chip_pop.mp3', 0.3],         // card beats from here (ui/adcard/card.js onBeat)
  pic: [POP + 'Pop3.mp3', 0.2],
  label: [PLUCK(9), 0.22],
  terms: [SFX + 'thud.mp3', 0.42],
  accepted: [SFX + 'unlock_card.mp3', 0.28],
  stat0: [PLUCK(10), 0.18], stat1: [PLUCK(12), 0.18], stat2: [PLUCK(14), 0.18],
  llamas: [POP + 'Pop.mp3', 0.12],
  foot: [SFX + 'reveal_chime.mp3', 0.28],
  still: [SFX + 'reveal_chime.mp3', 0.28],   // Gentle / reduced motion: the finished card arrives with one chime
});
const LOG = [];
/** Plays one cue. Every call is logged (time, name, file, gain) on window.__adcardCues for the dev drives. */
export function cue(ctx, name) {
  const c = CUES[name];
  if (!c) return;
  try {
    const win = ctx.world?.ownerDocument?.defaultView;
    if (win) { (win.__adcardCues ||= LOG).push({ t: Math.round(win.performance.now()), name, file: c[0], gain: c[1] }); }
  } catch { /* the log is a dev aid only */ }
  ctx.audio?.play?.(c[0], { bus: 'sfx', gain: c[1] });
}

/** Map a card beat (card.js onBeat) to its cue. */
export function beatCue(name, i = 0) {
  if (name === 'stat') return 'stat' + Math.min(2, i);
  return CUES[name] ? name : null;
}

/** The stylesheet, once per document. */
export function loadStageCss(doc) {
  let link = doc.querySelector('link[data-adstage]');
  if (!link) {
    link = doc.createElement('link');
    link.rel = 'stylesheet'; link.href = CSS_URL; link.dataset.adstage = '1';
    doc.head.append(link);
  }
  return link.sheet ? Promise.resolve() : new Promise((r) => { link.addEventListener('load', r, { once: true }); link.addEventListener('error', r, { once: true }); });
}

/** The stage colours: the card's own (cardColours) on a light paper of the first theme. */
export function stageLook(run) {
  const first = THEME_PALETTES[run?.picks?.f5] ? run.picks.f5 : 'bimbo';
  const C = cardColours(first, secondTheme(run));
  const P = THEME_PALETTES[first];
  const rgba = (h, a) => { const n = parseInt(h.slice(1), 16); return `rgba(${n >> 16},${(n >> 8) & 255},${n & 255},${a})`; };
  return Object.freeze({
    A: C.A, B: C.B, ink: P.ink,
    base: mixHex(P.soft, '#fbf5e9', 0.35),
    ra: rgba(C.A, 0.16), rb: rgba(C.B, 0.13), dot: rgba(P.ink, 0.13),
  });
}

function vars(el, look) {
  for (const [k, v] of Object.entries({ A: look.A, B: look.B, ink: look.ink, base: look.base, ra: look.ra, rb: look.rb, dot: look.dot })) el.style.setProperty('--st-' + k, v);
}

const RAY_MS = 150000;
const SLOT_H = (H) => (H < 600 ? 44 : 52);

/**
 * The paper stage in `parent`, its printer slot in `slotParent` (the slot must sit above whatever rises out of it).
 * Returns { root, slot, slotIn(delay), slotAway(delay), decorate(avoidRect, delay) }.
 */
export function createStage(ctx, doc, look, { parent = 'ending', slotParent = parent, calm = false, slotUp = true } = {}) {
  const win = doc.defaultView;
  const root = ctx.el('div', '', null, parent);
  root.className = 'adst';
  vars(root, look);
  for (const k of ['rays', 'dots', 'wash', 'deco']) { const d = doc.createElement('div'); d.className = 'adst-' + k; root.append(d); }
  const rays = root.querySelector('.adst-rays'), deco = root.querySelector('.adst-deco');
  if (!calm) {
    /* one shared phase from the page clock, so the take's stage and the show's stage turn as one */
    const a = ctx.anim(rays, [{ transform: 'translate(-50%,-50%) rotate(0deg)' }, { transform: 'translate(-50%,-50%) rotate(360deg)' }], { duration: RAY_MS, iterations: Infinity, easing: 'linear' });
    a.currentTime = win.performance.now() % RAY_MS;
  }
  const H = win.innerHeight;
  const slot = ctx.el('div', '', null, slotParent);
  slot.className = 'adst-slot';
  vars(slot, look);
  slot.style.setProperty('--st-slot', SLOT_H(H) + 'px');
  if (!slotUp) slot.style.transform = 'translateY(120%)';

  const api = {
    root, slot,
    slotIn(delay = 0) {
      slot.style.transform = '';
      if (calm) return ctx.anim(slot, [{ opacity: 0 }, { opacity: 1 }], { duration: 400, delay, fill: 'backwards' });
      return ctx.anim(slot, [{ transform: 'translateY(120%)' }, { transform: 'translateY(-8%)', offset: 0.7 }, { transform: 'none' }], { duration: 300, delay, easing: 'ease-out', fill: 'backwards' });
    },
    slotAway(delay = 0) {
      const a = calm
        ? ctx.anim(slot, [{ opacity: 1 }, { opacity: 0 }], { duration: 500, delay, fill: 'forwards' })
        : ctx.anim(slot, [{ transform: 'none' }, { transform: 'translateY(-10%)', offset: 0.25 }, { transform: 'translateY(130%)' }], { duration: 520, delay, easing: 'ease-in', fill: 'forwards' });
      return a;
    },
    /** Stars and stickers around (never on) the card's rect, with staggered entrances from `delay`. */
    decorate(avoid, delay = 0) { decorate(ctx, doc, deco, look, avoid, delay, calm); },
    /** A small paper confetti puff around a rect (not under Gentle or reduced motion). */
    puff(rect, delay = 0) { if (!calm) puff(ctx, doc, deco, look, rect, delay); },
  };
  return api;
}

/* ---------- decorations ---------- */
let starUrls = null;
function stars() {
  if (!starUrls) {
    const draws = [(f) => crayonStarSvg(f), (f) => starVariant(LOOPY, f), (f) => starVariant(RUSHED, f)];
    starUrls = draws.map((draw) => Array.from({ length: BOIL_FRAMES }, (_, f) => `url("data:image/svg+xml,${encodeURIComponent(draw(f))}")`));
  }
  return starUrls;
}

const STICKERS = ['billboard', 'megaphone', 'tag', 'sign'];
const overlaps = (a, b, pad = 0) => a.x < b.right + pad && a.x + a.w > b.left - pad && a.y < b.bottom + pad && a.y + a.h > b.top - pad;

function decorate(ctx, doc, host, look, avoid, delay, calm) {
  const win = doc.defaultView, W = win.innerWidth, H = win.innerHeight, slotH = SLOT_H(H);
  const placed = [];
  const fits = (r) => r.x >= 4 && r.y >= 4 && r.x + r.w <= W - 4 && r.y + r.h <= H - 8 && !placed.some((p) => overlaps(r, { left: p.x, right: p.x + p.w, top: p.y, bottom: p.y + p.h }, 10));
  const free = (r, pad) => !avoid || !overlaps(r, avoid, pad);
  const keepTopRight = (r) => !(r.x + r.w > W - 190 && r.y < 70);   // #game-controls (mute + Wake Up) sits there
  /* clip-art stickers, only where the margin beside the card has room for one */
  const side = Math.min(avoid ? avoid.left : W, avoid ? W - avoid.right : W);
  if (side >= 110) {
    const size = Math.max(80, Math.min(150, side * 0.62));
    const spots = [[0.5, 0.22], [0.5, 0.68], [0.5, 0.7], [0.5, 0.26]];   // left top, right low, left low, right high
    STICKERS.forEach((shape, i) => {
      const left = i % 2 === 0, cx = left ? avoid.left * spots[i][0] : avoid.right + (W - avoid.right) * spots[i][0];
      const c = clips[shape];
      if (!c) return;
      const k = size / Math.max(c.w, c.h), w = c.w * k, h = c.h * k;
      const r = { x: cx - w / 2 + (Math.random() - 0.5) * 18, y: H * spots[i][1] - h / 2 + (Math.random() - 0.5) * 24, w, h };
      if (!fits(r) || !keepTopRight(r) || r.y + r.h > H - slotH - 6) return;
      placed.push(r);
      sticker(ctx, doc, host, shape, r, delay + 260 + i * 170, calm);
    });
  }
  /* crayon stars: a handful, mostly in the margins; on a phone they peek out from behind the card */
  const urls = stars(), want = W < 600 ? 5 : 8;
  for (let n = 0, tries = 0; n < want && tries < 80; tries++) {
    const s = (W < 600 ? 30 : 40) + Math.random() * (W < 600 ? 18 : 30);
    const r = { x: Math.random() * (W - s), y: Math.random() * (H - s - slotH), w: s, h: s };
    if (!fits(r) || !keepTopRight(r) || !free(r, W < 600 ? -s * 0.45 : 14)) continue;
    placed.push(r);
    star(ctx, doc, host, urls[n % urls.length], r, delay + 120 + n * 110, calm);
    n++;
  }
}

function star(ctx, doc, host, frames, r, delay, calm) {
  const el = doc.createElement('div');
  el.className = 'adst-star';
  Object.assign(el.style, { width: r.w + 'px', height: r.h + 'px', translate: `${r.x}px ${r.y}px` });
  const phase = Math.floor(Math.random() * frames.length);
  frames.forEach((url, i) => {
    const l = doc.createElement('i');
    l.style.backgroundImage = url;
    if (!calm) l.style.animationDelay = `${(-((i + phase) % frames.length) * 0.44 / frames.length).toFixed(3)}s`;
    el.append(l);
  });
  host.append(el);
  if (calm) { ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 700, delay, fill: 'backwards' }); return; }
  el.classList.add('boil');
  ctx.anim(el, [{ opacity: 0, transform: 'scale(.2)' }, { opacity: 1, transform: 'scale(1.15)', offset: 0.65 }, { transform: 'none' }], { duration: 420, delay, easing: 'ease-out', fill: 'backwards' });
  /* a slow drift, each star on its own clock (well under 3 Hz) */
  const dx = (Math.random() - 0.5) * 26, dy = -10 - Math.random() * 14;
  ctx.anim(el, [{ transform: 'translate(0,0)' }, { transform: `translate(${dx.toFixed(1)}px,${dy.toFixed(1)}px)`, offset: 0.5 }, { transform: 'translate(0,0)' }],
    { duration: 6500 + Math.random() * 4500, delay: delay + 420, iterations: Infinity, easing: 'ease-in-out', composite: 'add' });
}

function sticker(ctx, doc, host, shape, r, delay, calm) {
  const c = clips[shape], el = doc.createElement('div');
  el.className = 'adst-sticker';
  Object.assign(el.style, { width: r.w + 'px', height: r.h + 'px', translate: `${r.x}px ${r.y}px`, backgroundImage: `url("${CLIPART}${shape}.webp")` });
  host.append(el);
  if (calm) { ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 700, delay, fill: 'backwards' }); return; }
  ctx.anim(el, [{ opacity: 0, transform: 'scale(.3)' }, { opacity: 1, transform: 'scale(1.08)', offset: 0.6 }, { transform: 'none' }], { duration: 460, delay, easing: 'ease-out', fill: 'backwards' });
  /* now and then the sticker plays its own clip (the motion sheet), then rests on its poster */
  const url = `${CLIPART}${shape}.motion.webp`, rows = Math.ceil(c.frames / c.columns);
  let ready = null;
  const playClip = () => {
    if (!el.isConnected) return;
    ready ||= new Promise((res) => { const im = new doc.defaultView.Image(); im.onload = () => res(true); im.onerror = () => res(false); im.src = url; });
    ready.then((ok) => {
      if (!ok || !el.isConnected) return;
      el.style.backgroundImage = `url("${url}")`;
      el.style.backgroundSize = `${c.columns * 100}% ${rows * 100}%`;
      const keys = Array.from({ length: c.frames }, (_, i) => ({ offset: i / (c.frames - 1), easing: 'steps(1, end)',
        backgroundPosition: `${(i % c.columns) / Math.max(1, c.columns - 1) * 100}% ${Math.floor(i / c.columns) / Math.max(1, rows - 1) * 100}%` }));
      const a = ctx.anim(el, keys, { duration: c.ms });
      const rest = () => { if (!el.isConnected) return; el.style.backgroundImage = `url("${CLIPART}${shape}.webp")`; el.style.backgroundSize = ''; };
      a.finished.then(rest, rest);
    });
    ctx.after(6000 + Math.random() * 6000, playClip);
  };
  ctx.after(delay + 900 + Math.random() * 2500, playClip);
}

function puff(ctx, doc, host, look, rect, delay) {
  const cols = [look.A, look.B, '#fffdf6', '#f6cf3a'];
  for (let i = 0; i < 16; i++) {
    const b = doc.createElement('i');
    b.className = 'adst-bit';
    const fromTop = i % 2 === 0, x = rect.left + Math.random() * rect.width, y = fromTop ? rect.top + 10 : rect.top + rect.height * (0.2 + Math.random() * 0.6);
    const sx = fromTop ? (Math.random() - 0.5) * 120 : (x < rect.left + rect.width / 2 ? -1 : 1) * (40 + Math.random() * 90);
    Object.assign(b.style, { background: cols[i % cols.length], translate: `${x.toFixed(0)}px ${y.toFixed(0)}px`, boxShadow: '1px 1px 0 rgba(20,10,20,.18)' });
    host.append(b);
    const spin = (Math.random() - 0.5) * 540;
    const a = ctx.anim(b, [
      { opacity: 0, transform: 'translate(0,0) rotate(0deg)' },
      { opacity: 1, transform: `translate(${(sx * 0.6).toFixed(0)}px,${(-60 - Math.random() * 60).toFixed(0)}px) rotate(${(spin * 0.4).toFixed(0)}deg)`, offset: 0.3 },
      { opacity: 0, transform: `translate(${sx.toFixed(0)}px,${(90 + Math.random() * 120).toFixed(0)}px) rotate(${spin.toFixed(0)}deg)` },
    ], { duration: 1300 + Math.random() * 500, delay: delay + Math.random() * 80, easing: 'cubic-bezier(.25,.6,.45,1)', fill: 'both' });
    const gone = () => b.remove();
    a.finished.then(gone, gone);
  }
}

/* ---------- the take ---------- */
/** Layers of the world that make up the visible page; the end fade, the ending layer and Wake Up are not among them. */
const PAGE_LAYERS = ['l-back', 'l-wash', 'l-screens', 'page', 'l-floaters', 'l-side', 'l-over', 'l-fx', 'l-toast'];
/** Milliseconds the take holds the screen: crop and snap, shrink, stamp, slot, feed. */
export const TAKE_MS = 2900, CALM_TAKE_MS = 1500;

export function livingAdTake(ctx) {
  const doc = ctx.world?.ownerDocument || document, win = doc.defaultView;
  const calm = !!(ctx.gentle || ctx.reduced);
  const look = stageLook(ctx.state);
  return new Promise((done) => {
    let over = false;
    const finish = () => { if (!over) { over = true; done(); } };
    ctx.on('suspend', (s) => { if (s?.on && s.reason === 'panic') finish(); });
    /* the card's fonts are the stamp's; the sheet loads them again later (cached) */
    const fonts = doc.fonts?.load ? doc.fonts.load('400 10px AdcBlock').catch(() => null) : null;
    const ready = Promise.all([loadStageCss(doc), fonts, ensureCardFonts(doc)]);
    Promise.race([ready, new Promise((r) => win.setTimeout(r, 700))]).then(() => {
      if (over) return;
      try { (calm ? calmTake : printTake)(ctx, doc, look, finish); } catch (e) { console.error(e); finish(); }
    });
  });
}

/* the card stylesheet holds the font faces (AdcRound, AdcBlock); load it early so the stamp has its face */
const ensureCardFonts = (doc) => loadCardCss(doc).catch(() => null);

function stampEl(ctx, doc, look, size) {
  const st = ctx.el('div', '', null, 'ending');
  st.className = 'adst-stamp';
  const ink = doc.createElement('span'); ink.className = 'adst-ink'; ink.textContent = ADCARD_COPY.printed;
  st.append(ink);
  vars(st, look);
  Object.assign(st.style, { fontSize: size + 'px', translate: '-50% -50%', rotate: '-11deg' });
  return st;
}

function calmTake(ctx, doc, look, finish) {
  cue(ctx, 'shutter');
  const stage = createStage(ctx, doc, look, { calm: true });
  ctx.anim(stage.root, [{ opacity: 0 }, { opacity: 1 }], { duration: 900, fill: 'backwards' });
  stage.slotIn(300);
  const W = doc.defaultView.innerWidth;
  const st = stampEl(ctx, doc, look, Math.max(28, Math.min(64, W * 0.07)));
  ctx.anim(st, [{ opacity: 0 }, { opacity: 1, offset: 0.4 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }], { duration: 1200, delay: 300, fill: 'both' });
  ctx.after(450, () => cue(ctx, 'printStamp'));
  ctx.after(CALM_TAKE_MS, finish);
}

function printTake(ctx, doc, look, finish) {
  const win = doc.defaultView, W = win.innerWidth, H = win.innerHeight, world = ctx.world;
  const s = W < 600 ? 0.64 : 0.56, tilt = -2.5, border = Math.round(Math.max(9, Math.min(18, W * 0.012))), slotH = SLOT_H(H);
  /* under the page, in the world's own stacking: the stage, the print's shadow and its paper (all z-index -1, in order) */
  const back = createStage(ctx, doc, look, { parent: world, slotParent: 'ending', slotUp: false });
  back.root.style.zIndex = '-1';
  const shade = ctx.el('div', '', null, world); shade.className = 'adst-shade';
  const paper = ctx.el('div', '', null, world); paper.className = 'adst-print';
  const bs = win.getComputedStyle(doc.body);
  paper.style.backgroundColor = bs.backgroundColor;
  paper.style.backgroundImage = bs.backgroundImage;
  world.insertBefore(paper, world.firstChild); world.insertBefore(shade, paper); world.insertBefore(back.root, shade);

  const EASE = 'cubic-bezier(.55,0,.25,1)';
  const shrink = [{ transform: 'none' }, { transform: `rotate(${tilt - 0.6}deg) scale(${(s * 0.965).toFixed(3)})`, offset: 0.72 }, { transform: `rotate(${tilt}deg) scale(${s})` }];
  const dyPrint = (H / 2 + (s * H) / 2 + 30) / s;
  const feed = [{ transform: 'none' }, { transform: `translateY(${(-16 / s).toFixed(1)}px) rotate(.6deg)`, offset: 0.2, easing: 'cubic-bezier(.6,0,.9,.5)' }, { transform: `translateY(${dyPrint.toFixed(1)}px) rotate(${-tilt}deg)` }];
  const knock = [{ transform: 'none' }, { transform: 'scale(.985)', offset: 0.35 }, { transform: 'none' }];
  const movers = PAGE_LAYERS.map((id) => doc.getElementById(id)).filter((el) => el && el.isConnected);
  const origins = movers.map((el) => el.style.transformOrigin);
  /* the engine forgets an animation once it finishes, and a held (fill: forwards) one would keep the real page moved
   * into the credits: every animation on a real node is cancelled here, after the fade */
  const held = [];
  const hold = (el, frames, opts) => { const a = ctx.anim(el, frames, opts); held.push(a); return a; };
  ctx.cleanup(() => { for (const a of held) { try { a.cancel(); } catch { /* gone */ } } movers.forEach((el, i) => { el.style.transformOrigin = origins[i]; }); });
  for (const el of movers) {
    const r = el.getBoundingClientRect();
    el.style.transformOrigin = `${(W / 2 - r.left).toFixed(1)}px ${(H / 2 - r.top).toFixed(1)}px`;
  }
  for (const el of [...movers, paper]) {
    hold(el, shrink, { duration: 800, delay: 250, easing: EASE, fill: 'forwards', composite: 'add' });
    hold(el, knock, { duration: 220, delay: 1470, easing: 'ease-out', composite: 'add' });
    hold(el, feed, { duration: 900, delay: 1950, fill: 'forwards', composite: 'add' });
  }
  /* the paper grows its white border; the shadow sits down-right and never turns */
  ctx.anim(paper, [{ boxShadow: '0 0 0 0 #fffdf6' }, { boxShadow: `0 0 0 ${(border / s).toFixed(1)}px #fffdf6` }], { duration: 800, delay: 250, easing: EASE, fill: 'forwards' });
  ctx.anim(shade, [{ opacity: 0, transform: 'none' }, { opacity: 1, transform: `translate(${12}px,${14}px) scale(${(s + (2 * border) / W).toFixed(3)})` }], { duration: 800, delay: 250, easing: EASE, fill: 'forwards' });
  ctx.anim(shade, [{ transform: 'none' }, { transform: `translateY(${(H / 2 + (s * H) / 2 + 40).toFixed(0)}px)`, easing: 'cubic-bezier(.6,0,.9,.5)' }], { duration: 900, delay: 1950, fill: 'forwards', composite: 'add' });
  /* the stage comes up under the page as it shrinks; the dock strip and its tabs leave */
  ctx.anim(back.root, [{ opacity: 0 }, { opacity: 1 }], { duration: 500, delay: 250, fill: 'backwards' });
  const strip = doc.getElementById('dock-strip');
  if (strip) hold(strip, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, delay: 150, fill: 'forwards' });

  /* crop marks: in from the corners on the snap, then they follow the print in */
  const pw = s * W + 2 * border + 14, ph = s * H + 2 * border + 14, px = (W - pw) / 2, py = (H - ph) / 2, M = 34, out = 26, inset = 12;
  const corners = { tl: [[inset, inset], [px, py], -1, -1], tr: [[W - inset - M, inset], [px + pw - M, py], 1, -1], bl: [[inset, H - inset - M], [px, py + ph - M], -1, 1], br: [[W - inset - M, H - inset - M], [px + pw - M, py + ph - M], 1, 1] };
  for (const [k, [a, b, sx, sy]] of Object.entries(corners)) {
    const m = ctx.el('div', '', null, 'ending'); m.className = 'adst-crop ' + k; vars(m, look);
    const at = (p, dx = 0, dy = 0) => `translate(${(p[0] + dx).toFixed(1)}px,${(p[1] + dy).toFixed(1)}px)`;
    ctx.anim(m, [
      { opacity: 0, transform: at(a, sx * out, sy * out) }, { opacity: 1, transform: at(a), offset: 0.2, easing: EASE }, { opacity: 1, transform: at(a), offset: 0.24, easing: EASE },
      { opacity: 1, transform: at(b), offset: 0.95 }, { opacity: 1, transform: at(b) },
    ], { duration: 1050, easing: 'ease-out', fill: 'forwards' });
    ctx.anim(m, [{ opacity: 1 }, { opacity: 0 }], { duration: 260, delay: 1700, fill: 'forwards', composite: 'replace' });
  }
  ctx.after(200, () => cue(ctx, 'shutter'));

  /* PRINTED lands on the print, then rides into the slot with it */
  const st = stampEl(ctx, doc, look, Math.round(Math.max(30, Math.min(78, s * W * 0.085))));
  ctx.anim(st, [{ opacity: 0, transform: 'scale(2.3)' }, { opacity: 1, transform: 'scale(.93)', offset: 0.62 }, { opacity: 1, transform: 'none' }], { duration: 360, delay: 1250, easing: 'ease-in', fill: 'both' });
  const down = (H / 2 + (s * H) / 2 + 40).toFixed(0);
  ctx.anim(st, [{ translate: '-50% -50%' }, { translate: '-50% calc(-50% - 16px)', offset: 0.2, easing: 'cubic-bezier(.6,0,.9,.5)' }, { translate: `-50% calc(-50% + ${down}px)` }], { duration: 900, delay: 1950, fill: 'forwards' });
  ctx.after(1470, () => cue(ctx, 'printStamp'));

  /* the slot comes up, the print feeds in on three soft ticks */
  back.slot.parentNode.append(back.slot);   // above the crop marks and the stamp
  back.slotIn(1560);
  ctx.after(1600, () => cue(ctx, 'slot'));
  [2150, 2370, 2590].forEach((t) => ctx.after(t, () => cue(ctx, 'feed')));

  /* the same stage, now above the end fade, so the fade underneath never shows; the show starts on it */
  ctx.after(TAKE_MS, () => {
    const cover = createStage(ctx, doc, look, { slotUp: true });
    cover.root.parentNode.insertBefore(cover.root, cover.root.parentNode.firstChild);
    finish();
  });
}
