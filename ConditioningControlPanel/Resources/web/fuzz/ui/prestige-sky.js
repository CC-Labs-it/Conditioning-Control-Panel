/* ============================================================================
 * ui/prestige-sky.js - Renewal Sky: the prestige constellation the player sees between runs. A port of the owner
 * approved mockup (artifact 7UV3pZjpzujgggf3cwn8vf, handoff-1109/tc-prestige-map/sky.src.html): eight crayon lanes curl
 * from their tips (cheap) into a hypno core; fog covers the depths not reached; the sky turns slowly and eases to a stop
 * while a card is open or a finger is down. Data from data/prestige.js, rules and state from sim/prestige.js (the
 * Core lane's contract): this file never decides a price, a depth or a reward, it asks.
 *
 * Flow (main.js renew): credits card -> Renew -> openRenewal(): P.renew(life, run) is applied and saved at once, the
 * contract shows the Stardust gained, the fog draws back one Depth, the player lights stars, then the Next run sheet
 * writes life.prestige.next and start() begins the run the way New run does.
 *
 * Rules kept: one lamp top left (shadows down-right, never rotated with a piece); Gentle visuals or reduced motion = no
 * spin, no boil, no slams, fades only; the sky's clock stops while the game is paused or suspended (Escape and Wake Up
 * open the game's own pause card above this sheet; Escape never closes anything here); every sound goes through the
 * audio kit's input(), so mute, pause and panic reach it.
 * ==========================================================================*/

import * as PD from '../data/prestige.js';
import { BY_ID } from '../data/items.js';
import { clock as fmtClock, fmt as fmtGame } from '../sim/format.js';
import { ICON_CATALOG } from './icon-catalog.js';
import * as GOLD_FX from './unlocks/basics/gold-fx.js';
import * as P_API from '../sim/prestige.js';
import * as LOOKS_API from '../sim/prestige-looks.js';

const { LANES, STAR_BY_ID, ENDING_STARS, FINE_RULES, WORD_BANK, MAX_DEPTH, STEPS, T_RUN, P_RUN } = PD;
const CORE_DATA = PD.CORE;
export const SKY_CURRENCY = PD.PRESTIGE_CURRENCY || 'Stardust';
const CURRENCY = SKY_CURRENCY;
const KINDS = { boost: '#f7e2a0', new: '#b9f2da', risk: '#ffc0c9' };
const CLIP = (name) => `assets/clipart/${name}.webp`;
const MAX_SHOOTER_CATCHES = 3;   // a visit can never become a Stardust farm

/* sim/prestige.js is the Core lane's: every rule (depth, prices, Stardust, renewal) lives there. These two stay as the
 * door main.js uses, so the credits card can ask what a renewal would pay. */
export async function loadPrestige() { return P_API; }
export const prestigeNow = () => P_API;

/* ---------------- shared helpers ---------------- */
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const easeOut = (t) => 1 - Math.pow(1 - t, 3);
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (n) => (n < 1000 ? String(Math.round(n)) : n < 1e6 ? (n / 1e3).toFixed(n < 1e4 ? 1 : 0).replace(/\.0$/, '') + 'k' : (n / 1e6).toFixed(1).replace(/\.0$/, '') + 'M');
function rngOf(seed) {
  let s = seed >>> 0;
  return () => { s = (s + 0x6D2B79F5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
function hexA(h, a) { const v = parseInt(h.slice(1), 16); return `rgba(${v >> 16},${(v >> 8) & 255},${v & 255},${a})`; }
function shade(h, k) { const v = parseInt(h.slice(1), 16); const f = (c) => Math.round(k < 0 ? c * (1 + k) : c + (255 - c) * k); return `rgb(${f(v >> 16)},${f((v >> 8) & 255)},${f(v & 255)})`; }
const DUST_SVG = '<svg class="ps-dust" viewBox="-20 -20 40 40" aria-hidden="true"><path d="M0-17 C3-5 5-3 17 0 C5 3 3 5 0 17 C-3 5 -5 3 -17 0 C-5-3 -3-5 0-17Z" fill="#f6c544" stroke="#2a2031" stroke-width="3" stroke-linejoin="round"/><path d="M-2-8 L-1 3 M2-5 L2 6 M-5 0 L5 0" stroke="#fff3c9" stroke-width="1.6" stroke-linecap="round"/></svg>';

/* geometry: lanes run from the outer tip (t = 0) to the core (t = 1) */
const R_OUT = 1000, R_IN = 250, CORE_R = 100, SWEEP = Math.PI * 1.12;
const armR = (t) => (t < 0 ? R_OUT + -t * 900 : Math.max(CORE_R + 8, R_OUT - (R_OUT - R_IN) * Math.pow(t, 0.82)));
const armTh = (a, t) => (a * Math.PI) / 4 + SWEEP * t - Math.PI / 2;
const pos = (a, t) => { const r = armR(t), th = armTh(a, t); return [Math.cos(th) * r, Math.sin(th) * r]; };
const rNode = (k) => armR(k / (STEPS - 1));
const ringEdge = (d) => (d >= MAX_DEPTH ? 0 : (rNode(d - 1) + rNode(d)) / 2);   // fog radius with d depths open

/* crayon shapes: one silhouette per star, four line-boil takes */
function starPts(seed, frame, R, n, inner, jit) {
  const base = rngOf(seed), wob = rngOf(seed * 7 + frame * 131 + 3);
  const rot = -Math.PI / 2 + (base() - 0.5) * 0.35, pts = [];
  for (let i = 0; i < n * 2; i++) {
    const rr = (i % 2 ? R * inner : R) * (1 + (base() - 0.5) * 2 * jit) * (1 + (wob() - 0.5) * (frame ? 0.05 : 0));
    const ang = rot + (i * Math.PI) / n + (base() - 0.5) * 0.1 + (wob() - 0.5) * (frame ? 0.05 : 0);
    pts.push([Math.cos(ang) * rr, Math.sin(ang) * rr]);
  }
  return pts;
}
function wobblePath(pts, rng, wob, sub = 3) {
  const p = new Path2D();
  for (let i = 0; i < pts.length; i++) {
    const [x0, y0] = pts[i], [x1, y1] = pts[(i + 1) % pts.length];
    const nx = -(y1 - y0), ny = x1 - x0, L = Math.hypot(nx, ny) || 1;
    for (let s = 0; s < sub; s++) {
      const u = s / sub, d = s ? (rng() - 0.5) * wob : 0;
      const x = x0 + (x1 - x0) * u + (nx / L) * d, y = y0 + (y1 - y0) * u + (ny / L) * d;
      if (i === 0 && s === 0) p.moveTo(x, y); else p.lineTo(x, y);
    }
  }
  p.closePath();
  return p;
}
function hatchPath(seed, R, frame) {
  const r = rngOf(seed * 13 + frame * 17 + 5), p = new Path2D(), ang = 0.62 + (r() - 0.5) * 0.3, c = Math.cos(ang), s = Math.sin(ang), gap = R * 0.17;
  for (let o = -R; o <= R; o += gap) {
    const x0 = -R * 1.2, x1 = R * 1.2, j = () => (r() - 0.5) * R * 0.08;
    p.moveTo(c * x0 - s * (o + j()), s * x0 + c * (o + j()));
    p.lineTo(-s * (o + j() * 2), c * (o + j() * 2));
    p.lineTo(c * x1 - s * (o + j()), s * x1 + c * (o + j()));
  }
  return p;
}
function makeShape(seed, R, n, inner) {
  const takes = [];
  for (let f = 0; f < 4; f++) {
    const pts = starPts(seed, f, R, n, inner, 0.08), rr = rngOf(seed + f * 999);
    const cut = starPts(seed + 1, f, R * 1.3, n, Math.min(0.72, inner + 0.2), 0.05);
    takes.push({ star: wobblePath(pts, rr, R * 0.06), star2: wobblePath(starPts(seed, f + 4, R * 1.02, n, inner, 0.08), rr, R * 0.07),
      cut: wobblePath(cut, rr, R * 0.05, 4), hatch: hatchPath(seed, R, f) });
  }
  return takes;
}
function sparkPath(s) { const p = new Path2D(); p.moveTo(0, -s); p.quadraticCurveTo(s * 0.15, -s * 0.15, s, 0); p.quadraticCurveTo(s * 0.15, s * 0.15, 0, s); p.quadraticCurveTo(-s * 0.15, s * 0.15, -s, 0); p.quadraticCurveTo(-s * 0.15, -s * 0.15, 0, -s); return p; }

/* The game's three approved crayon Golden Stars (ui/unlocks/basics/gold-fx.js), four boil takes each. Read through the
 * module's exports when it offers them (patch owed to gold-fx.js, see the lane report); until then the sky's own crayon
 * star stands in, filled gold. */
function goldStarSources() {
  const g = GOLD_FX;
  if (typeof g.crayonStarSvg !== 'function' || typeof g.starVariant !== 'function' || !g.LOOPY || !g.RUSHED) return null;
  const n = g.BOIL_FRAMES || 4;
  const looks = [(f) => g.crayonStarSvg(f), (f) => g.starVariant(g.LOOPY, f), (f) => g.starVariant(g.RUSHED, f)];
  try { return looks.map((draw) => Array.from({ length: n }, (_, f) => 'data:image/svg+xml,' + encodeURIComponent(draw(f)))); } catch { return null; }
}

/* ============================================================================
 * openRenewal: the one door. Applies the renewal, saves, opens the sky with the contract, and on "Sign and start"
 * writes life.prestige.next and calls start(). Returns { close() }.
 * opts: { doc, win, life, run, audio, gentle(), frozen(), save(), start(), host, visit }. visit = { caught } is the
 * caller's record of this Sky visit (main.js keeps it in the save extras, so a reload reopens the Sky where it was).
 * ==========================================================================*/
export async function openRenewal(opts) {
  const P = await loadPrestige();
  const { life, run } = opts;
  if (!life.prestige) life.prestige = P.createPrestige();
  const p = life.prestige;
  const depthBefore = p.depth, dustBefore = p.dust;
  const endedId = run?.ended?.id || null;
  const litBefore = !!(endedId && p.lit?.[endedId]);
  const res = P.renew(life, run) || {};
  const gained = Number.isFinite(res.dust) ? res.dust : p.dust - dustBefore;
  opts.save?.();
  return openSky({ ...opts, P, renewal: {
    gained, depthBefore, depthAfter: p.depth, runNo: Math.max(1, life.runs || depthBefore + 1),
    score: run?.total || 0, ms: run?.clock || 0, newEnding: endedId && !litBefore && p.lit?.[endedId] ? endedId : null,
  } });
}

/** The sky itself (also usable without a renewal, for a look between runs: renewal = null). */
export function openSky({ doc = document, win = window, life, P, audio = null, gentle = () => false, frozen = () => false, save = () => {}, start = () => {}, renewal = null, visit = null }) {
  const p = life.prestige;
  const calm = () => !!gentle() || !!win.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

  /* ---------- DOM ---------- */
  const root = doc.createElement('div');
  root.id = 'ps-sky';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-modal', 'true');
  root.setAttribute('aria-label', 'Renewal Sky');
  root.innerHTML = `
    <canvas class="ps-cv" aria-label="The ${esc(CURRENCY)} sky. Drag to move, scroll or pinch to zoom, tap a star to see it."></canvas>
    <div class="ps-hud top"><div class="ps-cluster">
      <div class="ps-pill ps-wallet" title="${esc(CURRENCY)}">${DUST_SVG}<b data-k="dust">0</b></div>
      <div class="ps-pill mult" title="Pay multiplier next run"><img alt="" src="${CLIP('wallet')}"><b data-k="mult">x1.00</b></div>
      <div class="ps-stamp" data-k="stamp" title="Depth"><small>Depth</small><b data-k="depth">0</b></div>
      <div class="ps-clock" title="Next run length"><svg viewBox="-22 -22 44 44" aria-hidden="true">
        <circle r="18" fill="#efe4d1" stroke="#2a2031" stroke-width="3"/><path data-k="wedge" fill="#ff6fb5"/><circle r="18" fill="none" stroke="#2a2031" stroke-width="3"/>
        <line data-k="hand" x1="0" y1="0" x2="0" y2="-13" stroke="#2a2031" stroke-width="3" stroke-linecap="round"/><circle r="3" fill="#2a2031"/></svg>
        <b data-k="min">90</b><small>min</small></div>
    </div></div>
    <div class="ps-hud bottom">
      <div class="ps-legend" role="toolbar" aria-label="Lanes"></div>
      <div class="ps-zoom">
        <button class="ps-ib" data-act="zin" type="button" aria-label="Zoom in">+</button>
        <button class="ps-ib" data-act="zout" type="button" aria-label="Zoom out">&minus;</button>
        <button class="ps-ib" data-act="zfit" type="button" aria-label="Show the whole sky"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/></svg></button>
      </div>
      <button class="ps-next ps-looks" data-act="looks" type="button"><img alt="" src="${CLIP('dress')}"><span>Looks</span></button>
      <button class="ps-next" data-act="next" type="button"><img alt="" src="${CLIP('scroll')}"><span>Next run</span></button>
    </div>
    <div class="ps-card" hidden></div>
    <div class="ps-over" hidden></div>
    <div class="ps-note" hidden><div class="paper">Tap a glowing star</div><svg viewBox="0 0 70 56"><path d="M4 8 C 30 0, 56 14, 62 46 M52 38 L62 48 L67 35"/></svg></div>
    <div class="ps-flash"></div>`;
  doc.body.appendChild(root);
  const q = (sel) => root.querySelector(sel);
  const K = (k) => root.querySelector(`[data-k="${k}"]`);
  const cv = q('canvas'), cx = cv.getContext('2d');
  const cardEl = q('.ps-card'), overEl = q('.ps-over'), noteEl = q('.ps-note'), legendEl = q('.ps-legend'), nextBtn = q('[data-act="next"]');
  const hudTop = q('.ps-hud.top'), hudBot = q('.ps-hud.bottom');

  /* ---------- art (game paths, never inlined) ---------- */
  const IMG = {};
  const img = (name) => {
    if (IMG[name]) return IMG[name];
    const im = new win.Image(); im.src = name === '_bubble' ? 'assets/img/bubble.png' : CLIP(name); IMG[name] = im; return im;
  };
  const ok = (im) => im && im.complete && im.naturalWidth > 0;
  const GREY = {};
  function greyOf(name) {
    if (GREY[name]) return GREY[name];
    const im = img(name); if (!ok(im)) return null;
    const c = doc.createElement('canvas'); c.width = im.naturalWidth; c.height = im.naturalHeight;
    const g = c.getContext('2d'); g.drawImage(im, 0, 0);
    try {
      const d = g.getImageData(0, 0, c.width, c.height);
      for (let i = 0; i < d.data.length; i += 4) { const v = d.data[i] * 0.3 + d.data[i + 1] * 0.55 + d.data[i + 2] * 0.15; d.data[i] = v * 0.8 + 30; d.data[i + 1] = v * 0.78 + 28; d.data[i + 2] = v * 0.9 + 40; }
      g.putImageData(d, 0, 0);
    } catch { /* a tainted canvas: the colour icon at low alpha is fine */ }
    return (GREY[name] = c);
  }
  const goldSrc = goldStarSources();
  const GOLDI = goldSrc ? goldSrc.map((look) => look.map((src) => {
    const im = new win.Image(), o = { im, c: null };
    im.onload = () => { const c = doc.createElement('canvas'); c.width = c.height = 192; c.getContext('2d').drawImage(im, 0, 0, 192, 192); o.c = c; };
    im.src = src; return o;
  })) : null;
  const goldImg = (look, f) => { if (!GOLDI) return null; const o = GOLDI[look][f % GOLDI[look].length]; return o.c || (ok(o.im) ? o.im : null); };
  const GOLD_STANDIN = makeShape(4242, 26, 5, 0.46);

  /* ---------- sound: plucked notes only, through the kit ---------- */
  let input = null;
  const out = () => {
    if (!audio?.input) return null;
    if (!input) input = audio.input({ bus: 'sfx', gain: 1.25, source: 'prestige-sky', sourceLabel: 'Renewal Sky' });
    return input;
  };
  const SCALE = [0, 2, 4, 7, 9];
  const noteHz = (i) => 261.63 * 2 ** ((SCALE[((i % 5) + 5) % 5] + 12 * Math.floor(i / 5)) / 12);
  function pluck(hz, when = 0, gain = 0.5, len = 0.9) {
    const o = out(); if (!o || frozen()) return; const ac = o.ac, t = ac.currentTime + when;
    const g = ac.createGain(), lp = ac.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.setValueAtTime(hz * 6, t); lp.frequency.exponentialRampToValueAtTime(hz * 1.5, t + len);
    g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(gain, t + 0.006); g.gain.exponentialRampToValueAtTime(0.0008, t + len);
    for (const [type, mul, lv] of [['triangle', 1, 1], ['sine', 2, 0.35], ['sine', 3.01, 0.12]]) { const os = ac.createOscillator(), og = ac.createGain(); os.type = type; os.frequency.value = hz * mul; og.gain.value = lv; os.connect(og).connect(lp); os.start(t); os.stop(t + len + 0.05); }
    lp.connect(g).connect(o.node);
  }
  function swell(dur = 2.6) {
    const o = out(); if (!o || frozen()) return; const ac = o.ac, t = ac.currentTime, g = ac.createGain(), lp = ac.createBiquadFilter();
    lp.type = 'lowpass'; lp.frequency.setValueAtTime(300, t); lp.frequency.exponentialRampToValueAtTime(2400, t + dur * 0.7); lp.frequency.exponentialRampToValueAtTime(400, t + dur);
    g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(0.16, t + dur * 0.6); g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    for (const hz of [130.8, 196, 261.6, 329.6]) for (const det of [-6, 6]) { const os = ac.createOscillator(); os.type = 'sawtooth'; os.frequency.value = hz; os.detune.value = det; os.connect(lp); os.start(t); os.stop(t + dur + 0.1); }
    lp.connect(g).connect(o.node);
  }
  function thunk() {
    const o = out(); if (!o || frozen()) return; const ac = o.ac, t = ac.currentTime, os = ac.createOscillator(), g = ac.createGain();
    os.type = 'sine'; os.frequency.setValueAtTime(160, t); os.frequency.exponentialRampToValueAtTime(48, t + 0.18); g.gain.setValueAtTime(0.7, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.25); os.connect(g).connect(o.node); os.start(t); os.stop(t + 0.3);
  }

  /* ---------- the sky's own clock: stops with the game's pause and suspend ---------- */
  let now = 0;
  const timers = [];
  const later = (sec, fn) => { timers.push({ at: now + sec, fn }); };
  const tweens = [];
  const tween = (dur, fn, done) => { tweens.push({ t0: now, dur, fn, done }); };

  /* ---------- state view ---------- */
  let viewDepth = renewal ? renewal.depthBefore : p.depth;
  let hiddenLit = renewal?.newEnding || null;
  let pending = null;   // the star whose Stardust is still flying to it
  const lvReal = (a) => P.level(p, LANES[a].id);
  const lv = (a) => lvReal(a) - (pending && pending.kind === 'star' && pending.a === a ? 1 : 0);
  const coreOwned = () => !!p.core && pending?.kind !== 'core';
  const owned = (n) => (n.kind === 'star' ? n.k < lv(n.a) : n.kind === 'end' ? !!p.lit?.[n.id] && hiddenLit !== n.id : coreOwned());
  const ringOpen = (n) => (n.kind === 'star' ? n.k < viewDepth : n.kind === 'core' ? viewDepth >= MAX_DEPTH : true);
  const isNext = (n) => (n.kind === 'star' ? n.k === lv(n.a) : n.kind === 'core' ? !coreOwned() && LANES.every((_, a) => lv(a) >= STEPS) : false);
  const canBuy = (n) => (n.kind === 'star' || n.kind === 'core') && !owned(n) && isNext(n) && ringOpen(n);
  const affordable = (n) => canBuy(n) && p.dust >= n.cost;
  const payMult = () => { try { return (P.mult ? P.mult(p) : P.runMods(p)?.payMult) || 1; } catch { return 1; } };
  const nextMin = () => { try { return P.runMods(p)?.minutes || T_RUN[Math.min(viewDepth, MAX_DEPTH - 1)]; } catch { return T_RUN[Math.min(viewDepth, MAX_DEPTH - 1)]; } };

  /* ---------- nodes ---------- */
  const NODES = [];
  LANES.forEach((lane, a) => {
    lane.stars.forEach((st, k) => {
      const t = k / (STEPS - 1), [x, y] = pos(a, t), big = k % 3 === 0, R = big ? 56 : 28;
      NODES.push({ kind: 'star', id: st.id, a, k, x, y, t, R, big, icon: big ? lane.icons[k / 3] : null, name: st.name, fx: st.flav, real: st.plain,
        cost: st.cost, shape: makeShape(a * 100 + k * 7 + 11, R, 5, big ? 0.5 : 0.46), wake: -9, lit: -9, hop: Math.random() * 6 });
    });
  });
  ENDING_STARS.forEach((e, i) => {
    const g = i % 8, k = i < 8 ? 2 : 5, t = (k + 1) / (STEPS - 1);
    const r = (rNode(k + 1) + rNode(k - 1)) / 2, th = armTh(g, t) + 0.03;
    NODES.push({ kind: 'end', id: e.id, e: i, x: Math.cos(th) * r, y: Math.sin(th) * r, R: 32, icon: e.icon, name: e.name, fx: 'Perk: ' + e.perk,
      real: `An ending star. Finish a run on ${e.which} and it lights for good, with its perk in every later run.`, shape: makeShape(900 + i * 31, 34, 4, 0.38), lit: -9, wake: -9, hop: 0 });
  });
  const CORE = { kind: 'core', id: 'core', x: 0, y: 0, R: CORE_R, name: CORE_DATA.name, fx: CORE_DATA.flav, real: CORE_DATA.plain, cost: CORE_DATA.cost, lit: -9, wake: -9 };
  NODES.push(CORE);
  const starAt = (a, k) => NODES[a * STEPS + k];

  const ARM_T0 = -0.2, ARM_T1 = 1.13, ARM_SAMPLES = 240;
  const tToIdx = (t) => Math.round(((t - ARM_T0) / (ARM_T1 - ARM_T0)) * ARM_SAMPLES);
  const ARM_LINES = LANES.map((_, a) => [0, 1, 2, 3].map((f) => {
    const r = rngOf(a * 77 + f * 5 + 1), o = [];
    let n1 = 0, n2 = 0;
    for (let i = 0; i <= ARM_SAMPLES; i++) {
      const t = ARM_T0 + ((ARM_T1 - ARM_T0) * i) / ARM_SAMPLES, [x, y] = pos(a, t);
      n1 = n1 * 0.7 + (r() - 0.5) * 2.2; n2 = n2 * 0.7 + (r() - 0.5) * 2.2;
      const [x2, y2] = pos(a, t + 0.001), dx = x2 - x, dy = y2 - y, L = Math.hypot(dx, dy) || 1;
      o.push([x - (dy / L) * n1, y + (dx / L) * n1, x - (dy / L) * n2, y + (dx / L) * n2]);
    }
    return o;
  }));

  /* ---------- canvas + camera ---------- */
  let W = 0, H = 0, DPR = 1, topH = 80, botH = 80, fitZ = 1;
  const cam = { x: 0, y: 0, z: 1, rot: 0 };
  let spin = 0, spinV = 0, nebRot = 0, renewSpin = false, introT0 = null, camFly = null;
  const ROT = () => cam.rot + spin;
  function resize() {
    DPR = Math.min(2, win.devicePixelRatio || 1); W = win.innerWidth; H = win.innerHeight;
    cv.width = Math.round(W * DPR); cv.height = Math.round(H * DPR);
    topH = (hudTop.offsetHeight || 70) + 6; botH = (hudBot.offsetHeight || 70) + 6;
    root.style.setProperty('--ps-bot', botH + 'px');
    fitZ = Math.min(W / 2 / (R_OUT + 230), (H - topH - botH) / 2 / (R_OUT + 150));
  }
  const onResize = () => { const was = cam.z / fitZ; resize(); cam.z = fitZ * was; };
  win.addEventListener('resize', onResize);
  resize();
  cam.z = fitZ;
  const midY = () => topH + (H - topH - botH) / 2;
  const w2s = (x, y) => { const dx = x - cam.x, dy = y - cam.y, r = ROT(), c = Math.cos(r), s = Math.sin(r); return [W / 2 + (dx * c - dy * s) * cam.z, midY() + (dx * s + dy * c) * cam.z]; };
  const s2w = (sx, sy) => { const dx = (sx - W / 2) / cam.z, dy = (sy - midY()) / cam.z, r = -ROT(), c = Math.cos(r), s = Math.sin(r); return [cam.x + dx * c - dy * s, cam.y + dx * s + dy * c]; };
  const flyTo = (x, y, z, dur = 0.8) => { if (calm()) { Object.assign(cam, { x, y, z }); return; } camFly = { t0: now, dur, from: { x: cam.x, y: cam.y, z: cam.z }, to: { x, y, z } }; };

  /* grain, nebulae, fog sprite: painted once */
  function noiseTile(n, a) { const c = doc.createElement('canvas'); c.width = c.height = n; const g = c.getContext('2d'), d = g.createImageData(n, n);
    for (let i = 0; i < d.data.length; i += 4) { const v = Math.random() * 255; d.data[i] = d.data[i + 1] = d.data[i + 2] = v; d.data[i + 3] = Math.random() * a; } g.putImageData(d, 0, 0); return c; }
  const grain = cx.createPattern(noiseTile(160, 26), 'repeat');
  const nebula = (seed, count, arms, cols, spread, base) => {
    const c = doc.createElement('canvas'); c.width = c.height = 1024; const g = c.getContext('2d'), r = rngOf(seed); g.globalCompositeOperation = 'lighter';
    for (let i = 0; i < count; i++) {
      const arm = i % arms, t = r(), th = (arm * Math.PI * 2) / arms + t * Math.PI * spread, rad = base + t * (arms === 2 ? 460 : 400), sp = (arms === 2 ? 30 : 50) + r() * (arms === 2 ? 70 : 90);
      const x = 512 + Math.cos(th) * rad + (r() - 0.5) * sp, y = 512 + Math.sin(th) * rad + (r() - 0.5) * sp, s = arms === 2 ? 20 + r() * 70 * (1 - t * 0.5) : 30 + r() * 80;
      const gr = g.createRadialGradient(x, y, 0, x, y, s), col = cols[(i * 7 + arm) % cols.length], al = arms === 2 ? 0.05 + r() * 0.06 : 0.03 + r() * 0.05;
      gr.addColorStop(0, `rgba(${col},${al})`); gr.addColorStop(1, `rgba(${col},0)`); g.fillStyle = gr; g.beginPath(); g.arc(x, y, s, 0, 7); g.fill();
    }
    return c;
  };
  const NEB = nebula(42, 520, 2, ['255,111,181', '184,156,255', '79,216,207', '108,196,255', '255,154,82'], 2.6, 40);
  const NEB2 = nebula(7, 300, 3, ['255,111,181', '246,197,68', '184,156,255', '255,93,114'], 1.8, 120);
  const FOG = doc.createElement('canvas'); FOG.width = FOG.height = 128;
  { const g = FOG.getContext('2d'), gr = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    gr.addColorStop(0, 'rgba(58,44,96,.95)'); gr.addColorStop(0.55, 'rgba(48,36,84,.55)'); gr.addColorStop(1, 'rgba(40,30,72,0)'); g.fillStyle = gr; g.fillRect(0, 0, 128, 128); }
  const SPIRAL = new Path2D();
  for (const off of [0, Math.PI]) for (let th = 0; th <= Math.PI * 8; th += 0.05) { const r = th / (Math.PI * 8), x = Math.cos(th + off) * r, y = Math.sin(th + off) * r; if (th === 0) SPIRAL.moveTo(x, y); else SPIRAL.lineTo(x, y); }
  const SPARK = sparkPath(1);
  const BG_R = 2700;
  const BG = Array.from({ length: 560 }, (_, i) => { const r = rngOf(i * 3 + 9); return { r: 60 + Math.sqrt(r()) * BG_R, a: r() * 7, s: 0.5 + r() * 1.7, p: r() * 7, f: 0.6 + r() * 2.4, cross: r() < 0.08, born: -9 }; });
  const FLOAT_ICONS = ['heart', 'bow', 'ring', 'lipstick', 'mic', 'radio', 'palette', 'spiral', 'bubbles', 'lock', 'crown', 'dice', 'headphones', 'eye', 'drop', 'cursor', 'confetti', 'moon'];
  const FLOATERS = FLOAT_ICONS.map((icon, i) => { const r = rngOf(i * 17 + 5); img(icon); return { icon, r: 900 + r() * 1700, a: r() * 7, s: 30 + r() * 30, p: r() * 7, d: 0.35 + r() * 0.3, born: -9 }; });
  const BUBBLES = Array.from({ length: 11 }, (_, i) => { const r = rngOf(i * 23 + 1); return { x: r(), y: r(), s: 26 + r() * 70, v: 10 + r() * 22, p: r() * 7, w: 10 + r() * 26 }; });
  img('_bubble');
  for (const l of LANES) for (const ic of l.icons) img(ic);
  for (const e of ENDING_STARS) img(e.icon);
  const FOGB = Array.from({ length: 170 }, (_, i) => { const r = rngOf(i * 11 + 3); return { u: Math.sqrt(r()), a: r() * 7, s: 0.5 + r() * 0.9, v: (0.03 + r() * 0.05) * (r() < 0.5 ? 1 : 0.7) }; });
  const shooters = [];
  let nextShooter = 3, catches = Math.min(MAX_SHOOTER_CATCHES, Math.max(0, Math.floor(+visit?.caught || 0)));   // visit: main.js keeps it across a reload
  let fogR = ringEdge(viewDepth), fogTarget = fogR;

  /* ---------- effects ---------- */
  const FX = { shocks: [], sparks: [], motes: [], wisps: [], waves: [], comets: [] };
  function burst(x, y, hue, n = 22, power = 1) {
    FX.shocks.push({ x, y, t0: now, hue, R: 90 * power });
    if (calm()) return;
    for (let i = 0; i < n; i++) { const a = Math.random() * 7, v = (120 + Math.random() * 260) * power;
      FX.sparks.push({ x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, t0: now, life: 0.7 + Math.random() * 0.6, hue: Math.random() < 0.3 ? '#fff6dc' : hue, s: 5 + Math.random() * 7, r: Math.random() * 7, vr: (Math.random() - 0.5) * 8 }); }
  }

  /* ---------- drawing ---------- */
  let focusArm = -1, gA = 1;
  const armAlpha = (a) => (focusArm < 0 || focusArm === a ? 1 : 0.28);
  const armAlphaOf = (n) => (n.kind === 'star' ? armAlpha(n.a) : focusArm < 0 ? 1 : 0.35);
  const frameOf = (i) => (calm() ? 0 : (Math.floor(now * 9) + i) % 4);
  function roundRect(x, y, w, h, r) { cx.beginPath(); cx.moveTo(x + r, y); cx.arcTo(x + w, y, x + w, y + h, r); cx.arcTo(x + w, y + h, x, y + h, r); cx.arcTo(x, y + h, x, y, r); cx.arcTo(x, y, x + w, y, r); cx.closePath(); }

  function drawSky(dt) {
    const g = cx.createRadialGradient(W / 2, H / 2, 0, W / 2, H / 2, Math.max(W, H) * 0.75);
    g.addColorStop(0, '#2a1c4a'); g.addColorStop(0.5, '#1b1234'); g.addColorStop(1, '#0d081b');
    cx.fillStyle = g; cx.fillRect(0, 0, W, H);
    const R0 = ROT(), sp = renewSpin ? 6 : 1, still = calm() ? 0 : 1;
    const [ox, oy] = w2s(0, 0), size = 2900 * cam.z;
    cx.save(); cx.translate(ox, oy); cx.rotate(nebRot + R0); cx.globalAlpha = 0.9; cx.drawImage(NEB, -size / 2, -size / 2, size, size); cx.restore();
    const breathe = 1 + 0.06 * Math.sin(now * 0.3) * still, s2 = size * 1.25 * breathe;
    cx.save(); cx.translate(ox, oy); cx.rotate(-nebRot * 1.6 + R0); cx.globalAlpha = 0.75 + 0.2 * Math.sin(now * 0.21) * still; cx.drawImage(NEB2, -s2 / 2, -s2 / 2, s2, s2); cx.restore();
    const sr = 2500 * cam.z;
    cx.save(); cx.translate(ox, oy); cx.rotate(R0 - now * 0.09 * still * sp); cx.scale(sr, sr);
    cx.strokeStyle = '#d9ccff'; cx.lineCap = 'round'; cx.lineWidth = (26 / sr) * Math.max(1, cam.z / fitZ);
    cx.globalAlpha = 0.045 + 0.03 * (0.5 + 0.5 * Math.sin(now * 0.7)) * still; cx.stroke(SPIRAL);
    cx.lineWidth = 3 / sr; cx.globalAlpha = 0.1; cx.setLineDash([6 / sr, 14 / sr]); cx.lineDashOffset = (-now * 40 / sr) * still; cx.stroke(SPIRAL); cx.setLineDash([]);
    cx.restore();
    const c = Math.cos(R0), sn = Math.sin(R0), pz = cam.z * 0.55 + 0.12, px = cam.x * 0.55, py = cam.y * 0.55, cy0 = midY();
    cx.fillStyle = '#efe7ff';
    for (const b of BG) {
      if (still) { b.a += (dt * 1.25) / Math.sqrt(b.r) * sp; b.r -= dt * b.r * 0.011 * sp; if (b.r < 50) { b.r = BG_R * (0.92 + Math.random() * 0.08); b.a = Math.random() * 7; b.born = now; } }
      const wx = Math.cos(b.a) * b.r - px, wy = Math.sin(b.a) * b.r - py, x = W / 2 + (wx * c - wy * sn) * pz, y = cy0 + (wx * sn + wy * c) * pz;
      if (x < -6 || y < -6 || x > W + 6 || y > H + 6) continue;
      const tw = still ? 0.45 + 0.55 * Math.abs(Math.sin(now * b.f + b.p)) : 0.7, fade = clamp((now - b.born) / 2, 0, 1) * clamp(b.r / 260, 0, 1);
      cx.globalAlpha = tw * 0.85 * fade;
      if (b.cross) { cx.fillRect(x - b.s * 2, y - 0.5, b.s * 4, 1); cx.fillRect(x - 0.5, y - b.s * 2, 1, b.s * 4); }
      else if (b.s > 1.7 && still) {
        const a2 = b.a - (0.05 * 600) / b.r, wx2 = Math.cos(a2) * b.r * 1.004 - px, wy2 = Math.sin(a2) * b.r * 1.004 - py;
        cx.strokeStyle = '#efe7ff'; cx.lineWidth = b.s * 0.9; cx.lineCap = 'round'; cx.globalAlpha *= 0.7; cx.beginPath(); cx.moveTo(x, y);
        cx.lineTo(W / 2 + (wx2 * c - wy2 * sn) * pz, cy0 + (wx2 * sn + wy2 * c) * pz); cx.stroke();
      } else { cx.beginPath(); cx.arc(x, y, b.s * 0.8, 0, 7); cx.fill(); }
    }
    for (const f of FLOATERS) {
      if (still) { f.a += (dt * 0.9) / Math.sqrt(f.r) * sp; f.r -= dt * f.r * 0.006 * sp; if (f.r < 260) { f.r = 2500 + Math.random() * 300; f.a = Math.random() * 7; f.born = now; } }
      const im = img(f.icon); if (!ok(im)) continue;
      const qz = cam.z * f.d + 0.1, wx = Math.cos(f.a) * f.r - cam.x * f.d, wy = Math.sin(f.a) * f.r - cam.y * f.d;
      const x = W / 2 + (wx * c - wy * sn) * qz, y = cy0 + (wx * sn + wy * c) * qz + Math.sin(now * 0.8 + f.p) * 6 * still, sz = f.s * (0.7 + f.d);
      if (x < -sz || y < -sz || x > W + sz || y > H + sz) continue;
      cx.globalAlpha = 0.2 * clamp((now - f.born) / 3, 0, 1) * clamp((f.r - 260) / 500, 0, 1); cx.drawImage(im, x - sz / 2, y - sz / 2, sz, sz);
    }
    const bi = img('_bubble');
    if (ok(bi)) for (const b of BUBBLES) {
      if (still) { b.y -= (dt * b.v) / H * sp; if (b.y < -0.15) { b.y = 1.12; b.x = Math.random(); } }
      const x = b.x * W + Math.sin(now * 0.6 + b.p) * b.w * still - cam.x * cam.z * 0.08, y = b.y * H - cam.y * cam.z * 0.08, sz = b.s * (1 + 0.04 * Math.sin(now * 2 + b.p) * still);
      cx.globalAlpha = 0.42; cx.drawImage(bi, x - sz / 2, y - sz / 2, sz, sz);
    }
    cx.globalAlpha = 1;
  }

  /* now and then one of the game's crayon Golden Stars shoots across; tap it for a little Stardust (3 a visit) */
  function drawShooters(dt) {
    if (!calm() && !busy && now > nextShooter && catches < MAX_SHOOTER_CATCHES) {
      nextShooter = now + 6 + Math.random() * 6;
      const fromLeft = Math.random() < 0.5, y0 = topH + (H - topH - botH) * (0.1 + Math.random() * 0.7);
      shooters.push({ look: Math.floor(Math.random() * 3), t0: now, dur: 3.2 + Math.random() * 1.6, x0: fromLeft ? -60 : W + 60, x1: fromLeft ? W + 60 : -60,
        y0, y1: y0 + (Math.random() - 0.3) * H * 0.4, arc: (Math.random() - 0.5) * H * 0.3, s: 54 + Math.random() * 26, spin: ((Math.random() < 0.5 ? 1 : -1) * Math.PI * 2) / 8, trail: [], caught: false });
    }
    for (let i = shooters.length - 1; i >= 0; i--) {
      const o = shooters[i], pr = (now - o.t0) / o.dur;
      if (pr >= 1 || o.caught) { shooters.splice(i, 1); continue; }
      const x = o.x0 + (o.x1 - o.x0) * pr, y = o.y0 + (o.y1 - o.y0) * pr + Math.sin(pr * Math.PI) * o.arc;
      o.x = x; o.y = y;
      o.trail.push([x, y, now]); while (o.trail.length && now - o.trail[0][2] > 0.5) o.trail.shift();
      cx.globalCompositeOperation = 'lighter';
      for (let j = 0; j < o.trail.length; j += 2) { const [tx, ty, tt] = o.trail[j], k = 1 - (now - tt) / 0.5;
        cx.save(); cx.translate(tx + Math.sin(tt * 40) * 6, ty + Math.cos(tt * 33) * 6); cx.rotate(tt * 5); cx.scale(5 * k, 5 * k); cx.fillStyle = j % 4 ? '#fff3c9' : '#f6c544'; cx.globalAlpha = k * 0.9; cx.fill(SPARK); cx.restore(); }
      cx.globalCompositeOperation = 'source-over';
      const gl = cx.createRadialGradient(x, y, 0, x, y, o.s); gl.addColorStop(0, 'rgba(246,197,68,.35)'); gl.addColorStop(1, 'rgba(246,197,68,0)');
      cx.globalAlpha = 1; cx.fillStyle = gl; cx.beginPath(); cx.arc(x, y, o.s, 0, 7); cx.fill();
      const f = Math.floor(now * 9) % 4, im = goldImg(o.look, f);
      cx.save(); cx.translate(x, y); cx.rotate((now - o.t0) * o.spin);
      if (im) cx.drawImage(im, -o.s / 2, -o.s / 2, o.s, o.s);
      else { const tk = GOLD_STANDIN[f]; const z = o.s / 64; cx.scale(z, z); cx.fillStyle = '#fbf6ee'; cx.fill(tk.cut); cx.fillStyle = '#f6cf3a'; cx.fill(tk.star); cx.strokeStyle = '#8a5c08'; cx.lineWidth = 2.4; cx.stroke(tk.star); }
      cx.restore();
    }
  }
  function catchShooter(x, y) {
    const o = shooters.find((s) => Math.hypot(s.x - x, s.y - y) < s.s * 0.7); if (!o) return false;
    o.caught = true; catches += 1; if (visit) visit.caught = catches;
    const gain = Math.max(5, Math.round(P_RUN[Math.min(p.depth, MAX_DEPTH - 1)] * 0.03));
    const [wx, wy] = s2w(x, y); burst(wx, wy, '#f6c544', 20, 0.9); [0, 4, 7].forEach((st, i) => pluck(noteHz(st + 10), i * 0.06, 0.3, 0.8));
    p.dust += gain; save(); setDust(p.dust); legend(); return true;
  }

  const LABELS = [];
  function drawLabels() {
    const [ox, oy] = w2s(0, 0), small = W < 600, k = small ? 1 : clamp(cam.z / fitZ, 0.9, 1.25);
    LANES.forEach((lane, a) => {
      const [tx, ty] = w2s(...pos(a, -0.12)), dx = tx - ox, dy = ty - oy, L = Math.hypot(dx, dy) || 1;
      const fs = (small ? 12 : 15) * k, ts = (small ? 9.5 : 11) * k, ic = small ? 0 : 26 * k, pad = 8 * k;
      cx.font = `600 ${fs}px SkyFredoka, Fredoka, sans-serif`; const nw = cx.measureText(lane.name).width;
      cx.font = `600 ${ts}px SkyFredoka, Fredoka, sans-serif`; const tag = lane.tag.toUpperCase(), tw = cx.measureText(tag).width + 10 * k;
      const w = pad + (ic ? ic + 5 * k : 0) + nw + 6 * k + tw + pad, h = Math.max(fs, ic) + 12 * k;
      let x = tx + (dx / L) * (w * 0.42) - w / 2, y = ty + (dy / L) * (h * 0.9) - h / 2;
      x = clamp(x, 8, W - w - 8); y = clamp(y, topH, H - h - botH);
      if (tx < -200 || ty < -200 || tx > W + 200 || ty > H + 200) { LABELS[a] = null; return; }
      LABELS[a] = { x, y, w, h };
      const al = armAlpha(a) * gA, n = lv(a), ready = n < STEPS && affordable(starAt(a, n));
      cx.save(); cx.globalAlpha = al;
      cx.fillStyle = 'rgba(5,2,14,.5)'; roundRect(x + 3, y + 4, w, h, 10 * k); cx.fill();
      cx.fillStyle = focusArm === a ? hexA(lane.hue, 1) : '#fbf6ee'; roundRect(x, y, w, h, 10 * k); cx.fill();
      cx.strokeStyle = '#2a2031'; cx.lineWidth = 2.5; cx.stroke();
      cx.fillStyle = lane.hue; cx.fillRect(x + 6 * k, y + h - 5 * k, Math.max(0, ((w - 12 * k) * n) / STEPS), 2.5 * k);
      let cxp = x + pad;
      if (ic) { const im = img(lane.icons[0]); if (ok(im)) cx.drawImage(im, cxp - 2 * k, y + (h - ic - 4 * k) / 2, ic + 4 * k, ic + 4 * k); cxp += ic + 5 * k; }
      cx.fillStyle = '#2a2031'; cx.textBaseline = 'middle'; cx.textAlign = 'left'; cx.font = `600 ${fs}px SkyFredoka, Fredoka, sans-serif`; cx.fillText(lane.name, cxp, y + h / 2 + 1); cxp += nw + 6 * k;
      const th = ts + 7 * k; cx.fillStyle = KINDS[lane.kind] || KINDS.boost; roundRect(cxp, y + (h - th) / 2, tw, th, 5 * k); cx.fill(); cx.lineWidth = 1.6; cx.stroke();
      cx.fillStyle = '#2a2031'; cx.font = `600 ${ts}px SkyFredoka, Fredoka, sans-serif`; cx.fillText(tag, cxp + 5 * k, y + h / 2 + 1);
      if (ready) { const pr = 5 + Math.sin(now * 5) * 1.2 * (calm() ? 0 : 1); cx.fillStyle = lane.hue; cx.beginPath(); cx.arc(x + w - 2, y + 2, pr, 0, 7); cx.fill(); cx.lineWidth = 2; cx.stroke(); }
      cx.restore();
    });
  }
  const labelAt = (x, y) => LABELS.findIndex((r) => r && x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h);
  function strokeArm(a, t0, t1, style, f) {
    const L = ARM_LINES[a][f], i0 = clamp(tToIdx(t0), 0, ARM_SAMPLES), i1 = clamp(tToIdx(t1), 0, ARM_SAMPLES);
    if (i1 <= i0) return;
    for (const pass of style.passes) {
      cx.beginPath();
      for (let i = i0; i <= i1; i++) { const pt = L[i], [x, y] = w2s(pass.b ? pt[2] : pt[0], pass.b ? pt[3] : pt[1]); if (i === i0) cx.moveTo(x, y); else cx.lineTo(x, y); }
      cx.strokeStyle = pass.c; cx.globalAlpha = pass.a * gA; cx.lineWidth = pass.w; cx.setLineDash(pass.d || []); cx.lineDashOffset = pass.o || 0; cx.stroke();
    }
    cx.setLineDash([]); cx.globalAlpha = 1;
  }
  function drawArms() {
    const zs = clamp(cam.z / fitZ, 0.7, 2.4), still = calm();
    cx.lineCap = 'round'; cx.lineJoin = 'round';
    LANES.forEach((lane, a) => {
      const f = frameOf(a), n = lv(a), k1 = STEPS - 1, aa = armAlpha(a);
      cx.save(); cx.globalAlpha = aa;
      strokeArm(a, ARM_T0, 0, { passes: [{ c: '#ddd3f2', a: 0.14 * aa, w: 1.4 * zs, d: [2 * zs, 9 * zs] }] }, f);
      strokeArm(a, 0, ARM_T1, { passes: [{ c: '#ddd3f2', a: 0.2 * aa, w: 1.6 * zs, d: [2 * zs, 7 * zs] }] }, f);
      if (n > 0) {
        let tEnd = (n - 1) / k1; const last = starAt(a, n - 1), grow = clamp((now - last.lit) / 0.45, 0, 1);
        if (n > 1 && grow < 1) tEnd = (n - 2) / k1 + (1 / k1) * easeOut(grow);
        const full = n >= STEPS;
        if (full) tEnd = 1;
        const lit = { passes: [{ c: lane.hue, a: 0.16 * aa, w: (full ? 18 : 12) * zs }, { c: lane.hue, a: 0.95 * aa, w: 4.2 * zs }, { c: '#fff7e8', a: 0.55 * aa, w: 1.4 * zs, b: 1 }] };
        strokeArm(a, ARM_T0 * 0.4, tEnd, lit, f);
        if (coreOwned() && full) strokeArm(a, 1, ARM_T1, lit, f);
        if (n < STEPS && grow >= 1) strokeArm(a, (n - 1) / k1, n / k1, { passes: [{ c: lane.hue, a: (n < viewDepth ? 0.7 : 0.3) * aa, w: 2.6 * zs, d: [7 * zs, 9 * zs], o: still ? 0 : -now * 30 * zs }] }, f);
      } else {
        strokeArm(a, ARM_T0 * 0.4, 0, { passes: [{ c: lane.hue, a: 0.6 * aa, w: 2.6 * zs, d: [7 * zs, 9 * zs], o: still ? 0 : -now * 30 * zs }] }, f);
      }
      cx.restore();
    });
  }
  const flow = LANES.map(() => []);
  function drawFlow(dt) {
    if (calm()) return;
    cx.globalCompositeOperation = 'lighter';
    LANES.forEach((lane, a) => {
      const n = lv(a); if (!n) { flow[a].length = 0; return; }
      const tMax = coreOwned() && n >= STEPS ? ARM_T1 : (n - 1) / (STEPS - 1), F = flow[a];
      if (Math.random() < dt * (2 + n * 0.9)) F.push({ t: ARM_T0 * 0.4, v: 0.07 + Math.random() * 0.05, o: (Math.random() - 0.5) * 6, s: 1.5 + Math.random() * 2 });
      for (let i = F.length - 1; i >= 0; i--) {
        const pt = F[i]; pt.t += pt.v * dt * (1 + (renewSpin ? 3 : 0));
        if (pt.t > tMax) { F.splice(i, 1); continue; }
        const [x, y] = pos(a, pt.t), [sx, sy] = w2s(x, y + pt.o), fade = clamp((tMax - pt.t) * 12, 0, 1) * clamp((pt.t - ARM_T0 * 0.4) * 10, 0, 1);
        cx.globalAlpha = 0.8 * fade * armAlpha(a); cx.fillStyle = lane.hue; cx.beginPath(); cx.arc(sx, sy, pt.s * clamp(cam.z / fitZ, 0.8, 2), 0, 7); cx.fill();
        cx.globalAlpha = 0.9 * fade * armAlpha(a); cx.fillStyle = '#fff'; cx.beginPath(); cx.arc(sx, sy, pt.s * 0.45, 0, 7); cx.fill();
      }
    });
    cx.globalCompositeOperation = 'source-over'; cx.globalAlpha = 1;
  }
  function drawRings() {
    const [ox, oy] = w2s(0, 0);
    cx.save(); cx.lineWidth = 1.2; cx.setLineDash([3, 9]); cx.strokeStyle = '#ddd3f2';
    for (let d = 1; d < MAX_DEPTH; d++) {
      const r = ringEdge(d) * cam.z; if (r < fogR * cam.z - 2) continue;
      cx.globalAlpha = 0.1; cx.beginPath(); cx.arc(ox, oy, r, 0, 7); cx.stroke();
    }
    cx.restore();
  }
  function drawStar(n, i, alphaIn) {
    const [sx, sy] = w2s(n.x, n.y), z = Math.max(cam.z, (n.big ? 21 : n.kind === 'end' ? 15 : 11) / n.R);
    n.sx = sx; n.sy = sy; n.sr = n.R * 1.3 * z;
    if (sx < -120 || sy < -120 || sx > W + 120 || sy > H + 120) return;
    const still = calm(), hue = n.kind === 'end' ? '#fff1c4' : LANES[n.a].hue, take = n.shape[frameOf(i)], own = owned(n), open = ringOpen(n);
    const ready = canBuy(n), rich = ready && p.dust >= n.cost;
    let scale = 1, lift = 0;
    if (!still) {
      const wakeT = now - n.wake; if (wakeT < 0.6) scale *= 1 + 0.35 * Math.sin(clamp(wakeT / 0.6, 0, 1) * Math.PI);
      const litT = now - n.lit; if (litT < 0.7) scale *= 1 + 0.55 * Math.sin(clamp(litT / 0.7, 0, 1) * Math.PI) * (1 - litT / 0.7);
      if (rich) { const h = (now + n.hop) % 7; if (h < 0.5) lift = -Math.sin((h / 0.5) * Math.PI) * 9; scale *= 1 + 0.05 * Math.sin(now * 3 + n.hop); }
    }
    cx.save(); cx.globalAlpha = alphaIn * armAlphaOf(n);
    const zz = z * scale, y = sy + lift;
    if (own) {
      const gl = cx.createRadialGradient(sx, y, 0, sx, y, n.R * 2.6 * zz); gl.addColorStop(0, hexA(hue, 0.45)); gl.addColorStop(1, hexA(hue, 0));
      cx.fillStyle = gl; cx.beginPath(); cx.arc(sx, y, n.R * 2.6 * zz, 0, 7); cx.fill();
      cx.save(); cx.translate(sx + 3, y + 4); cx.scale(zz, zz); cx.fillStyle = 'rgba(5,2,14,.5)'; cx.fill(take.cut); cx.restore();   // one lamp: shadow down-right
      cx.translate(sx, y); cx.scale(zz, zz);
      cx.fillStyle = '#fbf6ee'; cx.fill(take.cut);
      cx.fillStyle = hexA(hue, 0.55); cx.fill(take.star);
      cx.save(); cx.clip(take.star); cx.strokeStyle = hue; cx.lineWidth = n.R * 0.11; cx.lineCap = 'round'; cx.stroke(take.hatch); cx.restore();
      cx.strokeStyle = shade(hue, -0.45); cx.lineWidth = n.R * 0.085; cx.lineJoin = 'round'; cx.stroke(take.star); cx.globalAlpha *= 0.5; cx.lineWidth = n.R * 0.05; cx.stroke(take.star2);
    } else {
      cx.translate(sx, y); cx.scale(zz, zz);
      if (ready && open) {
        cx.save(); cx.rotate(still ? 0 : now * 0.6); cx.setLineDash([n.R * 0.3, n.R * 0.25]); cx.strokeStyle = hue; cx.globalAlpha *= rich ? 0.95 : 0.45; cx.lineWidth = n.R * 0.08;
        cx.beginPath(); cx.arc(0, 0, n.R * (rich ? 1.55 + 0.08 * Math.sin(now * 4) * (still ? 0 : 1) : 1.45), 0, 7); cx.stroke(); cx.restore();
        if (rich) { const gl = cx.createRadialGradient(0, 0, 0, 0, 0, n.R * 2); gl.addColorStop(0, hexA(hue, 0.28)); gl.addColorStop(1, hexA(hue, 0)); cx.fillStyle = gl; cx.beginPath(); cx.arc(0, 0, n.R * 2, 0, 7); cx.fill(); }
      }
      cx.fillStyle = rich ? hexA(hue, 0.22) : 'rgba(30,21,55,.75)'; cx.fill(take.star);
      cx.strokeStyle = rich ? hue : '#ddd3f2'; cx.globalAlpha *= open ? (rich ? 1 : 0.55) : 0.3; cx.lineWidth = n.R * 0.07; cx.setLineDash(open ? [] : [n.R * 0.15, n.R * 0.2]); cx.stroke(take.star); cx.setLineDash([]);
    }
    cx.restore();
    if (n.icon) {
      const dR = (n.kind === 'end' ? 20 : 34) * zz;
      cx.save(); cx.globalAlpha = alphaIn * armAlphaOf(n) * (own ? 1 : open ? 0.9 : 0.4);
      if (own) { cx.fillStyle = 'rgba(5,2,14,.45)'; cx.beginPath(); cx.arc(sx + 2, y + 3, dR, 0, 7); cx.fill(); }
      cx.fillStyle = own ? '#fbf6ee' : '#2b2244'; cx.beginPath(); cx.arc(sx, y, dR, 0, 7); cx.fill();
      cx.strokeStyle = own ? shade(hue, -0.3) : rich ? hue : '#8f84ad'; cx.lineWidth = Math.max(1.5, 3 * zz); cx.stroke();
      const im = own || rich ? img(n.icon) : greyOf(n.icon), s = dR * 1.7;
      if (im && (im.tagName !== 'IMG' || ok(im))) { cx.globalAlpha *= own || rich ? 1 : 0.7; cx.drawImage(im, sx - s / 2, y - s / 2, s, s); }
      cx.restore();
      if (cam.z / fitZ > 1.3 && open && n.kind === 'star') {
        cx.save(); cx.globalAlpha = alphaIn * armAlphaOf(n) * clamp((cam.z / fitZ - 1.3) * 3, 0, 1); cx.font = `600 ${clamp((14 * cam.z) / fitZ / 1.4, 13, 19)}px SkyFredoka, Fredoka, sans-serif`;
        cx.textAlign = 'center'; cx.textBaseline = 'top'; const ty = y + n.R * 1.25 * zz + 6; cx.lineJoin = 'round'; cx.lineWidth = 4; cx.strokeStyle = 'rgba(19,12,37,.9)'; cx.strokeText(n.name, sx, ty);
        cx.fillStyle = own ? '#fff7e8' : '#cfc4ea'; cx.fillText(n.name, sx, ty); cx.restore();
      }
    }
  }
  function drawCore(alphaIn, overFog) {
    const [sx, sy] = w2s(0, 0), z = cam.z, R = CORE_R * z, open = viewDepth >= MAX_DEPTH, still = calm(), got = coreOwned();
    CORE.sx = sx; CORE.sy = sy; CORE.sr = R;
    if (overFog) {
      const pulse = still ? 0.5 : 0.5 + 0.5 * Math.sin(now * 1.3), gl = cx.createRadialGradient(sx, sy, 0, sx, sy, R * 3.2);
      gl.addColorStop(0, `rgba(255,140,200,${(open ? 0.35 : 0.22) + pulse * 0.12})`); gl.addColorStop(1, 'rgba(255,111,181,0)');
      cx.globalAlpha = alphaIn; cx.fillStyle = gl; cx.beginPath(); cx.arc(sx, sy, R * 3.2, 0, 7); cx.fill(); cx.globalAlpha = 1;
      return;
    }
    cx.save(); cx.globalAlpha = alphaIn; cx.translate(sx, sy);
    cx.fillStyle = 'rgba(5,2,14,.5)'; cx.beginPath(); cx.arc(3, 5, R * 1.08, 0, 7); cx.fill();
    cx.fillStyle = '#fbf6ee'; cx.beginPath(); cx.arc(0, 0, R * 1.08, 0, 7); cx.fill();
    cx.beginPath(); cx.arc(0, 0, R, 0, 7); cx.clip();
    cx.fillStyle = got ? '#ff6fb5' : open ? '#5a3f8f' : '#3f2f6b'; cx.fillRect(-R, -R, R * 2, R * 2);
    cx.rotate(still ? 0 : -now * (got ? 1.8 : 0.7) * (renewSpin ? 4 : 1));
    cx.strokeStyle = got ? '#fff3f9' : open ? '#ffb3d9' : '#9a86d0'; cx.lineWidth = R * 0.09; cx.lineCap = 'round';
    for (const off of [0, Math.PI]) { cx.beginPath(); for (let th = 0; th < Math.PI * 4.4; th += 0.1) { const r = ((R * th) / (Math.PI * 4)) * 1.02; const x = Math.cos(th + off) * r, y = Math.sin(th + off) * r; if (th === 0) cx.moveTo(x, y); else cx.lineTo(x, y); } cx.stroke(); }
    cx.restore();
    cx.save(); cx.globalAlpha = alphaIn; cx.strokeStyle = '#2a2031'; cx.lineWidth = Math.max(2, 4 * z); cx.beginPath(); cx.arc(sx, sy, R, 0, 7); cx.stroke();
    if (canBuy(CORE)) { cx.setLineDash([10 * z, 8 * z]); cx.lineDashOffset = still ? 0 : -now * 20; cx.strokeStyle = '#ff6fb5'; cx.lineWidth = 4 * z; cx.beginPath(); cx.arc(sx, sy, R * 1.35, 0, 7); cx.stroke(); }
    cx.restore();
  }
  function drawFog(dt) {
    if (fogR < 2) return;
    const [ox, oy] = w2s(0, 0), still = calm();
    for (const b of FOGB) {
      if (!still) b.a += b.v * dt * (1.3 - b.u * 0.6) * (renewSpin ? 6 : 1);
      const r = b.u * fogR * 0.97, x = ox + Math.cos(b.a + ROT()) * r * cam.z, y = oy + Math.sin(b.a + ROT()) * r * cam.z;
      const s = (90 + b.s * 170) * clamp(fogR / 700, 0.45, 1.1) * cam.z, edge = clamp((1 - b.u) / 0.22, 0, 1);
      if (x < -s || y < -s || x > W + s || y > H + s) continue;
      cx.globalAlpha = 0.62 * edge; cx.drawImage(FOG, x - s, y - s, s * 2, s * 2);
    }
    cx.globalAlpha = 1;
    const R = fogR * cam.z;
    cx.save(); cx.strokeStyle = '#ddd3f2'; cx.globalAlpha = 0.45; cx.lineWidth = 1.8; cx.setLineDash([4, 10]); cx.lineDashOffset = still ? 0 : now * 8;
    cx.beginPath(); cx.arc(ox, oy, R, 0, 7); cx.stroke(); cx.setLineDash([]);
    if (viewDepth < MAX_DEPTH && R > 40) {
      cx.globalAlpha = 0.95; cx.translate(ox, oy - R); cx.rotate(-0.06);
      const txt = 'Depth ' + (viewDepth + 1); cx.font = '600 13px SkyFredoka, Fredoka, sans-serif'; const tw = cx.measureText(txt).width + 18;
      cx.fillStyle = '#2b2244'; cx.strokeStyle = '#ddd3f2'; cx.lineWidth = 2; roundRect(-tw / 2, -12, tw, 24, 8); cx.fill(); cx.stroke();
      cx.fillStyle = '#ddd3f2'; cx.textAlign = 'center'; cx.textBaseline = 'middle'; cx.fillText(txt, 0, 1);
    }
    cx.restore();
  }
  function drawFX(dt) {
    for (let i = FX.shocks.length - 1; i >= 0; i--) { const s = FX.shocks[i], pr = (now - s.t0) / 0.7; if (pr >= 1) { FX.shocks.splice(i, 1); continue; }
      const [x, y] = w2s(s.x, s.y); cx.strokeStyle = s.hue; cx.globalAlpha = (1 - pr) * 0.9; cx.lineWidth = 6 * (1 - pr) + 1; cx.beginPath(); cx.arc(x, y, s.R * easeOut(pr) * cam.z * 1.6 + 10, 0, 7); cx.stroke(); }
    for (let i = FX.sparks.length - 1; i >= 0; i--) { const s = FX.sparks[i], age = now - s.t0; if (age > s.life) { FX.sparks.splice(i, 1); continue; }
      s.x += s.vx * dt; s.y += s.vy * dt; s.vx *= Math.pow(0.05, dt); s.vy *= Math.pow(0.05, dt); s.r += s.vr * dt;
      const [x, y] = w2s(s.x, s.y), k = 1 - age / s.life, zs = clamp(cam.z / fitZ, 0.8, 2); cx.save(); cx.translate(x, y); cx.rotate(s.r); cx.scale(s.s * k * zs, s.s * k * zs);
      cx.fillStyle = s.hue; cx.globalAlpha = Math.min(1, k * 1.5); cx.fill(SPARK); cx.restore(); }
    for (let i = FX.motes.length - 1; i >= 0; i--) { const m = FX.motes[i], pr = (now - m.t0) / m.dur; if (pr >= 1) { FX.motes.splice(i, 1); m.done?.(); continue; } if (pr < 0) continue;
      const [tx, ty] = m.to(), e = ease(pr), x = m.x + (tx - m.x) * e + Math.sin(pr * Math.PI) * m.bx, y = m.y + (ty - m.y) * e + Math.sin(pr * Math.PI) * m.by;
      cx.save(); cx.translate(x, y); cx.rotate(pr * 6); cx.scale(7 * (1 - pr * 0.4), 7 * (1 - pr * 0.4)); cx.fillStyle = '#f6c544'; cx.globalAlpha = 1; cx.fill(SPARK); cx.restore(); }
    for (let i = FX.wisps.length - 1; i >= 0; i--) { const w = FX.wisps[i], pr = (now - w.t0) / w.life; if (pr >= 1) { FX.wisps.splice(i, 1); continue; } if (pr < 0) continue;
      const r = w.r + (w.r2 - w.r) * easeOut(pr), [x, y] = w2s(Math.cos(w.a) * r, Math.sin(w.a) * r), s = w.s * cam.z * (1 + pr);
      cx.globalAlpha = 0.6 * (1 - pr); cx.drawImage(FOG, x - s, y - s, s * 2, s * 2); }
    for (let i = FX.comets.length - 1; i >= 0; i--) { const c = FX.comets[i], pr = (now - c.t0) / c.dur; if (pr >= 1) { FX.comets.splice(i, 1); c.done(); continue; }
      const [tx, ty] = w2s(c.n.x, c.n.y), e = ease(pr), x = c.x + (tx - c.x) * e, y = c.y + (ty - c.y) * e;
      c.trail.push([x, y]); if (c.trail.length > 26) c.trail.shift();
      cx.lineCap = 'round'; for (let j = 1; j < c.trail.length; j++) { cx.strokeStyle = '#fff1c4'; cx.globalAlpha = (j / c.trail.length) * 0.8; cx.lineWidth = (j / c.trail.length) * 7; cx.beginPath(); cx.moveTo(...c.trail[j - 1]); cx.lineTo(...c.trail[j]); cx.stroke(); }
      cx.save(); cx.translate(x, y); cx.rotate(now * 8); cx.scale(11, 11); cx.fillStyle = '#fff'; cx.globalAlpha = 1; cx.fill(SPARK); cx.restore(); }
    for (let i = FX.waves.length - 1; i >= 0; i--) { const w = FX.waves[i], pr = (now - w.t0) / w.dur; if (pr >= 1.15) { FX.waves.splice(i, 1); continue; } if (pr < 0) continue;
      const t = ARM_T0 * 0.4 + (ARM_T1 - ARM_T0 * 0.4) * clamp(pr, 0, 1), [x, y] = pos(w.a, t), [sx, sy] = w2s(x, y), rad = (60 * cam.z) / fitZ * 0.6 + 30, g = cx.createRadialGradient(sx, sy, 0, sx, sy, rad);
      g.addColorStop(0, hexA('#ffffff', 0.85 * (1 - Math.max(0, pr - 1) * 6))); g.addColorStop(0.3, hexA(LANES[w.a].hue, 0.5)); g.addColorStop(1, hexA(LANES[w.a].hue, 0));
      cx.globalCompositeOperation = 'lighter'; cx.fillStyle = g; cx.beginPath(); cx.arc(sx, sy, rad, 0, 7); cx.fill(); cx.globalCompositeOperation = 'source-over'; }
    cx.globalAlpha = 1;
  }

  /* ---------- loop ---------- */
  let raf = 0, last = 0, closed = false, wasFrozen = false, measuredAt = -1;
  function loop(ms) {
    if (closed) return;
    raf = win.requestAnimationFrame(loop);
    const tnow = ms / 1000, dtRaw = last ? Math.min(0.05, tnow - last) : 0; last = tnow;
    const fz = !!frozen();
    if (fz !== wasFrozen) { wasFrozen = fz; root.classList.toggle('frozen', fz); }
    if (fz) return;   // pause, panic: the sky's clock, its sound and its css all hold
    root.classList.toggle('calm', calm());
    const dt = dtRaw; now += dt;
    if (introT0 === null) introT0 = now;
    // the HUD wraps as fonts and the legend settle: keep the fit and the card's floor honest (two layout reads, twice a second)
    if (now - measuredAt > 0.5) { measuredAt = now; const t = (hudTop.offsetHeight || 70) + 6, b = (hudBot.offsetHeight || 70) + 6; if (t !== topH || b !== botH) onResize(); }
    for (let i = timers.length - 1; i >= 0; i--) if (now >= timers[i].at) { const t = timers.splice(i, 1)[0]; t.fn(); }
    const still = calm();
    nebRot += dt * (renewSpin ? 0.5 : 0.03) * (still ? 0 : 1);
    // the sky eases to a stop while a card is open or a finger is down, so nothing slides out from under the reader
    spinV += ((still || sel || ptrs.size || !overEl.hidden ? 0 : 0.022) - spinV) * (1 - Math.pow(0.05, dt)); spin += dt * spinV;
    for (let i = tweens.length - 1; i >= 0; i--) { const tw = tweens[i], pr = clamp((now - tw.t0) / tw.dur, 0, 1); tw.fn(pr); if (pr >= 1) { tweens.splice(i, 1); tw.done?.(); } }
    if (camFly) { const pr = clamp((now - camFly.t0) / camFly.dur, 0, 1), e = ease(pr); cam.x = camFly.from.x + (camFly.to.x - camFly.from.x) * e; cam.y = camFly.from.y + (camFly.to.y - camFly.from.y) * e;
      cam.z = camFly.from.z * Math.pow(camFly.to.z / camFly.from.z, e); if (pr >= 1) camFly = null; }
    fogR += (fogTarget - fogR) * (still ? 1 : 1 - Math.pow(0.02, dt));
    cx.setTransform(DPR, 0, 0, DPR, 0, 0);
    drawSky(dt); drawRings();
    const ip = still ? 1 : clamp((now - introT0) / 2.2, 0, 1);
    const appear = (n) => (still ? 1 : clamp((ip * 1.5 - (Math.hypot(n.x, n.y) / R_OUT) * 0.9) * 3, 0, 1));
    gA = ip; drawArms(); gA = 1; drawFlow(dt);
    const inFog = (n) => n.kind !== 'core' && Math.hypot(n.x, n.y) < fogR - 4;
    drawFog(dt);
    drawCore(appear(CORE), false);
    if (fogR > 2) {
      gA = ip;
      LANES.forEach((lane, a) => { let i = 0; while (i < ARM_SAMPLES && Math.hypot(ARM_LINES[a][0][i][0], ARM_LINES[a][0][i][1]) > fogR) i++;
        const tf = ARM_T0 + ((ARM_T1 - ARM_T0) * i) / ARM_SAMPLES; cx.save(); cx.globalAlpha = armAlpha(a); strokeArm(a, tf, ARM_T1, { passes: [{ c: lane.hue, a: 0.32 * armAlpha(a), w: 1.6, d: [2, 8] }] }, 0); cx.restore(); });
      gA = 1;
    }
    NODES.forEach((n, i) => { if (n.kind !== 'core' && inFog(n)) drawStar(n, i, appear(n) * 0.55); });
    NODES.forEach((n, i) => { if (n.kind !== 'core' && (!inFog(n) || (n.kind === 'end' && owned(n)))) drawStar(n, i, appear(n)); });
    drawCore(appear(CORE), true);
    gA = ip; drawLabels(); gA = 1;
    drawShooters(dt);
    drawFX(dt);
    cx.fillStyle = grain; cx.globalAlpha = 0.55; cx.fillRect(0, 0, W, H); cx.globalAlpha = 1;
    placeNote(); placeCard();
  }

  /* ---------- HUD ---------- */
  let shownDust = renewal ? p.dust - renewal.gained : p.dust, spendTimer = null;
  function setDust(v, anim = true) {
    const from = shownDust;
    if (!anim || calm()) { shownDust = v; K('dust').textContent = fmt(v); return; }
    tween(0.6, (pr) => { shownDust = from + (v - from) * easeOut(pr); K('dust').textContent = fmt(shownDust); });
    const w = q('.ps-wallet'); w.classList.remove('bump', 'spend'); void w.offsetWidth; w.classList.add('bump'); if (v < from) w.classList.add('spend');
    if (spendTimer) spendTimer.dead = true;
    spendTimer = { dead: false }; const mine = spendTimer; later(0.6, () => { if (!mine.dead) w.classList.remove('spend'); });
  }
  function setClock(min, anim = true) {
    const draw = (m) => { const f = m / 90, a = f * Math.PI * 2, r = 18, x = Math.sin(a) * r, y = -Math.cos(a) * r;
      K('wedge').setAttribute('d', f >= 0.999 ? 'M0-18A18 18 0 1 1 0 18A18 18 0 1 1 0-18Z' : `M0 0L0 -${r}A${r} ${r} 0 ${f > 0.5 ? 1 : 0} 1 ${x.toFixed(2)} ${y.toFixed(2)}Z`);
      K('hand').setAttribute('x2', (Math.sin(a) * 13).toFixed(2)); K('hand').setAttribute('y2', (-Math.cos(a) * 13).toFixed(2)); K('min').textContent = Math.round(m); };
    const from = +K('min').textContent || min; if (!anim || calm()) return draw(min); tween(1.2, (pr) => draw(from + (min - from) * ease(pr)));
  }
  function hud(anim = true) {
    setDust(p.dust, anim);
    const m = payMult(); K('mult').textContent = 'x' + (m < 100 ? m.toFixed(2) : Math.round(m));
    K('depth').textContent = viewDepth; setClock(viewDepth === p.depth ? nextMin() : T_RUN[Math.min(viewDepth, MAX_DEPTH - 1)], anim); legend();
  }
  LANES.forEach((lane, a) => {
    const b = doc.createElement('button'); b.type = 'button'; b.className = 'ps-arm'; b.style.setProperty('--c', lane.hue); b.title = lane.name;
    b.innerHTML = `<img src="${CLIP(lane.icons[0])}" alt=""><span class="nm">${esc(lane.name)}<b></b></span><span class="bar"><i></i></span>`; b.setAttribute('aria-label', lane.name);
    b.addEventListener('mouseenter', () => { focusArm = a; }); b.addEventListener('mouseleave', () => { focusArm = -1; });
    b.addEventListener('focus', () => { focusArm = a; }); b.addEventListener('blur', () => { focusArm = -1; });
    b.addEventListener('click', () => { if (!frozen()) goLane(a); });
    legendEl.appendChild(b);
  });
  function goLane(a) { const n = starAt(a, Math.min(lv(a), STEPS - 1)); flyTo(n.x * 0.85, n.y * 0.85, Math.max(cam.z, fitZ * 1.7)); select(n); }
  function legend() {
    [...legendEl.children].forEach((b, a) => { const n = lv(a); b.querySelector('b').textContent = `${n}/${STEPS}`; b.querySelector('i').style.width = (n / STEPS) * 100 + '%';
      const nx = n < STEPS ? starAt(a, n) : null; b.classList.toggle('ready', !!nx && affordable(nx)); b.classList.toggle('full', n >= STEPS); });
  }

  /* ---------- star card ---------- */
  let sel = null;
  function select(n) {
    sel = n; if (!n) { cardEl.hidden = true; return; }
    hideNote();
    const own = owned(n), hue = n.kind === 'star' ? LANES[n.a].hue : n.kind === 'end' ? '#f6c544' : '#ff6fb5';
    const icon = n.icon || (n.kind === 'star' ? LANES[n.a].icons[Math.floor(n.k / 3)] : 'spiral');
    const lane = n.kind === 'star' ? LANES[n.a] : null;
    const tag = lane ? `<span class="tag">${esc(lane.name)} ${n.k + 1}/${STEPS}</span><span class="tag kind" style="--kc:${KINDS[lane.kind] || KINDS.boost}">${esc(lane.tag)}</span>`
      : `<span class="tag">${n.kind === 'end' ? 'Ending' : 'The core'}</span>`;
    let foot;
    if (own) foot = '<span class="lit">Lit</span>';
    else if (n.kind === 'end') foot = `<p>Reach the ending <b>${esc(n.name)}</b> to light it.</p>`;
    else if (!ringOpen(n)) foot = `<p>Opens at Depth ${n.kind === 'core' ? MAX_DEPTH : n.k + 1}.</p>`;
    else if (!isNext(n)) foot = n.kind === 'core' ? '<p>Light the last star of every lane first.</p>' : '<p>Light the star before it first.</p>';
    else { const short = n.cost - p.dust; foot = `<span class="cost">${DUST_SVG}${fmt(n.cost)}</span>
      <button class="ps-go" data-act="buy" type="button" ${short > 0 ? 'disabled' : ''}>${short > 0 ? 'Need ' + fmt(short) + ' more' : 'Light it'}</button>`; }
    const greyIcon = !own && !(canBuy(n) && p.dust >= n.cost);
    cardEl.style.setProperty('--c', hue);
    cardEl.innerHTML = `<button class="ps-x" type="button" aria-label="Close">&times;</button><div class="head"><span class="disc"><img src="${CLIP(icon)}" alt="" class="${greyIcon ? 'grey' : ''}"></span>
      <div><span class="tags">${tag}</span><h3>${esc(n.name)}</h3></div></div><p class="flav">${esc(n.fx)}</p><p class="real">${esc(n.real)}</p><div class="foot">${foot}</div>`;
    cardEl.hidden = false; cardEl.classList.remove('in'); void cardEl.offsetWidth; cardEl.classList.add('in');
    cardEl.querySelector('.ps-x').onclick = () => select(null);
    const go = cardEl.querySelector('[data-act="buy"]'); if (go) { go.onclick = () => { if (!frozen()) buy(n); }; go.focus({ preventScroll: true }); }
    placeCard();
  }
  function placeCard() {
    if (!sel || cardEl.hidden || W <= 600) return;
    const n = sel, cw = cardEl.offsetWidth, ch = cardEl.offsetHeight, pad = 16;
    let x = n.sx + (n.sr || 30) + 18, y = n.sy - 40;
    if (x + cw > W - pad) x = n.sx - (n.sr || 30) - 18 - cw;
    x = clamp(x, pad, W - cw - pad); y = clamp(y, topH, H - ch - botH);
    cardEl.style.left = x + 'px'; cardEl.style.top = y + 'px';
  }

  /* ---------- buying: the contract decides, the sky shows it ---------- */
  function buy(n) {
    if (busy || pending || !affordable(n)) return;
    try { if (n.kind === 'core') P.buyCore(p); else P.buyStar(p, n.id); } catch { /* refused */ }
    const took = n.kind === 'core' ? !!p.core : P.has(p, n.id);
    if (!took) { thunk(); select(n); return; }
    pending = n;
    save(); setDust(p.dust);
    const wr = q('.ps-wallet').getBoundingClientRect(), cnt = calm() ? 1 : Math.min(9, 3 + Math.round(Math.log2(Math.max(2, n.cost))));
    let arrived = 0;
    const ignite = () => {
      pending = null;
      if (n.kind === 'core') { finale(); return; }
      n.lit = now;
      burst(n.x, n.y, LANES[n.a].hue, n.big ? 30 : 18, n.big ? 1.2 : 0.8);
      pluck(noteHz(n.k + (n.a % 3)), 0, 0.5); if (n.big) pluck(noteHz(n.k + 2 + (n.a % 3)), 0.07, 0.3);
      const nx = n.k + 1 < STEPS ? starAt(n.a, n.k + 1) : null; if (nx) later(0.42, () => { nx.wake = now; });
      if (lv(n.a) >= STEPS) laneComplete(n.a);
      hud(false); select(n);
    };
    for (let i = 0; i < cnt; i++) FX.motes.push({ x: wr.left + 20, y: wr.top + wr.height / 2, to: () => [n.sx, n.sy], t0: now + i * 0.045, dur: calm() ? 0.01 : 0.5,
      bx: (Math.random() - 0.5) * 120, by: (Math.random() - 0.5) * 120 - 40,
      done: () => { if (++arrived === cnt) ignite(); else pluck(noteHz(10 + arrived), 0, 0.06, 0.25); } });
  }
  function laneComplete(a) {
    FX.waves.push({ a, t0: now + 0.3, dur: 1.2 });
    [0, 2, 4, 5, 7].forEach((s, i) => pluck(noteHz(s + 5), 0.35 + i * 0.09, 0.32, 1.4));
    for (let k = 0; k < STEPS; k++) { const n = starAt(a, k); later(0.3 + k * 0.11, () => { n.wake = now; if (k % 3 === 0) burst(n.x, n.y, LANES[a].hue, 8, 0.5); }); }
  }
  function finale() {
    busy = true; nextBtn.disabled = true;
    swell(4); flyTo(0, 0, fitZ * 2.6, 1.4);
    LANES.forEach((_, a) => FX.waves.push({ a, t0: now + a * 0.05, dur: 1.2 }));
    if (!calm()) { renewSpin = true; const r0 = cam.rot; tween(3, (pr) => { cam.rot = r0 - Math.PI * 4 * ease(pr); }, () => { cam.rot = 0; renewSpin = false; }); }
    later(1.5, () => { const f = q('.ps-flash'); f.classList.remove('go'); void f.offsetWidth; f.classList.add('go'); thunk(); burst(0, 0, '#ff6fb5', 60, 2.4); [0, 2, 4, 7, 9, 12].forEach((s, i) => pluck(noteHz(s + 5), i * 0.1, 0.35, 1.8)); });
    later(3.2, () => { busy = false; nextBtn.disabled = false; flyTo(0, 0, fitZ, 1.2); hud(false); select(CORE); });
  }
  function lightEnding(id) {
    const n = NODES.find((x) => x.kind === 'end' && x.id === id); if (!n) { hiddenLit = null; return; }
    const fin = () => { hiddenLit = null; n.lit = now; burst(n.x, n.y, '#fff1c4', 26, 1); [0, 4, 7].forEach((s, i) => pluck(noteHz(s + 7), i * 0.08, 0.35, 1.2)); hud(false); };
    if (calm()) { fin(); return; }
    const side = Math.random() * 7;
    FX.comets.push({ n, x: W / 2 + Math.cos(side) * Math.max(W, H), y: H / 2 + Math.sin(side) * Math.max(W, H), t0: now, dur: 1.1, trail: [], done: fin });
  }

  /* ---------- the renewal show (already applied and saved; this only shows it) ---------- */
  let busy = false;
  function showRenewal(r) {
    busy = true; nextBtn.disabled = true;
    const D = r.depthBefore, min = T_RUN[Math.min(D, MAX_DEPTH - 1)];
    swell(3);
    if (!calm()) {
      renewSpin = true; const r0 = cam.rot;
      tween(2.8, (pr) => { cam.rot = r0 + Math.PI * 2 * ease(pr); }, () => { cam.rot = 0; renewSpin = false; });
      LANES.forEach((_, a) => { if (lv(a)) FX.waves.push({ a, t0: now + 0.2 + a * 0.06, dur: 1.4 }); });
    }
    later(calm() ? 0.1 : 0.65, () => {
      overEl.innerHTML = `<div class="ps-paper in" role="status"><div class="run"><b>Run ${r.runNo}</b><span><img src="${CLIP('clock')}" alt="">${esc(r.ms ? fmtClock(r.ms) : min + ' min')}</span></div>
        <div class="row"><span>Score</span><b>${esc(fmtGame(r.score))}</b></div>
        <div class="row"><span>${esc(CURRENCY)}</span><b>${DUST_SVG}<span data-k="earn">+0</span></b></div>
        <div class="sig">signed<svg viewBox="0 0 150 34"><path d="M6 26c10-18 16-20 18-8s-6 14 0 4 12-16 16-6-4 10 4 4 10-12 14-6 2 8 10 2 14-8 18-2 4 6 12 0 18-4 46-6"/></svg></div>
        <div class="ps-ok">Renewed</div></div>`;
      overEl.hidden = false;
      tween(calm() ? 0.01 : 0.9, (pr) => { K('earn').textContent = '+' + fmt(r.gained * easeOut(pr)); if (!calm() && Math.random() < 0.3) pluck(noteHz(8 + Math.floor(pr * 6)), 0, 0.05, 0.2); });
      later(1.15, () => { overEl.querySelector('.ps-ok')?.classList.add('slam'); thunk(); });
      later(2.5, () => {
        const ct = overEl.querySelector('.ps-paper'); ct?.classList.remove('in'); ct?.classList.add('out');
        later(0.45, () => { if (overEl.querySelector('.ps-paper.out')) { overEl.hidden = true; overEl.innerHTML = ''; } });
        // the terms renewed: the Stardust lands, the fog draws back one depth
        const oldR = fogR; viewDepth = r.depthAfter; fogTarget = ringEdge(viewDepth);
        if (!calm()) for (let i = 0; i < 46; i++) { const a = Math.random() * 7; FX.wisps.push({ a, r: oldR * (0.85 + Math.random() * 0.2), r2: oldR * (1.3 + Math.random() * 0.5), s: 70 + Math.random() * 90, t0: now + Math.random() * 0.3, life: 1.6 + Math.random() * 0.8 }); }
        const st = K('stamp'); st.classList.remove('slam'); void st.offsetWidth; st.classList.add('slam'); thunk();
        hud(true);
        if (D < MAX_DEPTH) LANES.forEach((lane, a) => { const n = starAt(a, Math.min(D, STEPS - 1)); later(0.7 + a * 0.09, () => { n.wake = now; burst(n.x, n.y, lane.hue, 6, 0.45); pluck(noteHz(a + 3), 0, 0.18, 0.7); }); });
        if (hiddenLit) later(1.7, () => lightEnding(hiddenLit));
        later(2, () => { busy = false; nextBtn.disabled = false; legend(); if (!tipped()) showNote(); });
      });
    });
  }

  /* ---------- Next run: rules, words, keepsakes, then start ---------- */
  function nextSheet() {
    if (busy || pending) return;
    select(null); hideNote();
    const has = (id) => { try { return !!P.has(p, id); } catch { return false; } };
    const prev = p.next || { rules: [], words: [], keep: [] };
    const openRules = (() => { try { return (P.rulesOpen(p) || []).map((r) => (typeof r === 'string' ? r : r?.id)).filter((id) => FINE_RULES.some((x) => x.id === id)); } catch { return []; } })();
    const slots = (() => { try { const s = P.fineSlots(p); return Number.isFinite(s) ? s : openRules.length; } catch { return 0; } })();
    const full = has(PD.FULL_CONTRACT.star);
    const wordN = ['trig_1', 'trig_5', 'trig_8'].filter(has).length;
    const keepN = ['head_7', 'head_8'].filter(has).length;
    const toys = [...new Set((p.last?.toys || []).filter((id) => BY_ID[id]))];
    const pick = {
      rules: (prev.rules || []).filter((id) => openRules.includes(id)).slice(0, full ? openRules.length : slots),
      words: (prev.words || []).filter((w) => WORD_BANK.includes(w)).slice(0, wordN),
      keep: (prev.keep || []).filter((id) => toys.includes(id)).slice(0, keepN),
    };
    const factor = () => { try { return P.fineMult(pick.rules, p) || 1; } catch { return 1; } };
    const ruleMult = (r) => (r.better && has(r.better.star) ? r.better.mult : r.mult);
    const chip = (group, id, label, extra = '', icon = '') => `<button class="ps-chip" type="button" data-g="${group}" data-id="${esc(id)}" aria-pressed="false">${icon ? `<img alt="" src="${icon}">` : ''}${esc(label)}${extra ? ` <small>${esc(extra)}</small>` : ''}</button>`;
    const rulesHtml = openRules.length
      ? `<div class="ps-chips" style="--c:#ffc0c9">${openRules.map((id) => { const r = FINE_RULES.find((x) => x.id === id); return chip('rules', id, r.name, 'x' + ruleMult(r)); }).join('')}${full ? chip('full', 'full', 'Full Contract', 'x' + PD.FULL_CONTRACT.mult) : ''}</div>
        <div class="ps-factor"><span>${esc(CURRENCY)} this run</span><b data-k="factor">x1</b></div>`
      : '<p class="none">The Fine Print lane adds optional rules here.</p>';
    const wordsHtml = wordN ? `<div class="ps-chips" style="--c:#b89cff">${WORD_BANK.map((w) => chip('words', w, w)).join('')}</div>` : '<p class="none">The Trigger Words lane adds a word to catch here.</p>';
    const keepHtml = keepN ? (toys.length ? `<div class="ps-chips" style="--c:#6fe3b8">${toys.map((id) => chip('keep', id, BY_ID[id].name, '', ICON_CATALOG[id]?.shape ? CLIP(ICON_CATALOG[id].shape) : '')).join('')}</div>` : '<p class="none">No toys from last run to pack.</p>')
      : '<p class="none">The Head Start lane lets you pack a toy here.</p>';
    overEl.innerHTML = `<div class="ps-paper ps-sheet in" role="dialog" aria-label="Next run">
      <button class="ps-x" type="button" aria-label="Back to the sky" data-act="back">&times;</button>
      <h2>Next run</h2><p class="sub">Depth ${p.depth}. About ${Math.round(nextMin())} minutes.</p>
      <section><h4>Fine Print <small>${openRules.length ? (full ? 'any number' : `up to ${slots}`) : ''}</small></h4>${rulesHtml}</section>
      <section><h4>Trigger Words <small>${wordN ? `pick ${wordN}` : ''}</small></h4>${wordsHtml}</section>
      <section><h4>Keepsakes <small>${keepN ? `pack ${keepN}` : ''}</small></h4>${keepHtml}</section>
      <div class="acts"><button class="ps-go plain" type="button" data-act="back">Back to the sky</button><button class="ps-go" type="button" data-act="sign">Sign and start</button></div></div>`;
    overEl.classList.add('dim'); overEl.hidden = false;
    const sheet = overEl.querySelector('.ps-sheet');
    const limit = { rules: full ? openRules.length : slots, words: wordN, keep: keepN };
    const paint = () => {
      for (const b of sheet.querySelectorAll('.ps-chip')) {
        const g = b.dataset.g, id = b.dataset.id;
        if (g === 'full') { b.setAttribute('aria-pressed', String(pick.rules.length >= openRules.length && openRules.length > 0)); continue; }
        const on = pick[g].includes(id);
        b.setAttribute('aria-pressed', String(on));
        b.disabled = !on && limit[g] > 0 && pick[g].length >= limit[g] && limit[g] > 1 ? true : false;
      }
      const f = sheet.querySelector('[data-k="factor"]'); if (f) f.textContent = 'x' + (Math.round(factor() * 100) / 100);
    };
    sheet.addEventListener('click', (e) => {
      if (frozen()) return;
      const b = e.target.closest('button'); if (!b) return;
      if (b.dataset.act === 'back') { closeSheet(); return; }
      if (b.dataset.act === 'sign') {
        p.next = { rules: [...pick.rules], words: [...pick.words], keep: [...pick.keep] };
        save();
        [0, 4, 7, 12].forEach((s, i) => pluck(noteHz(s + 5), i * 0.07, 0.3, 1));
        finish();
        return;
      }
      const g = b.dataset.g, id = b.dataset.id; if (!g) return;
      if (g === 'full') { pick.rules = pick.rules.length >= openRules.length ? [] : [...openRules]; }
      else {
        const list = pick[g], at = list.indexOf(id);
        if (at >= 0) list.splice(at, 1);
        else if (limit[g] === 1) { list.length = 0; list.push(id); }   // one slot: a tap swaps
        else if (list.length < limit[g]) list.push(id);
      }
      pluck(noteHz(7 + (pick[g === 'full' ? 'rules' : g].length % 5)), 0, 0.22, 0.5);
      paint();
    });
    paint();
    sheet.querySelector('[data-act="sign"]').focus({ preventScroll: true });
  }
  function closeSheet() { overEl.classList.remove('dim'); overEl.hidden = true; overEl.innerHTML = ''; }

  /* ---------- Looks shelf (sim/prestige-looks.js): after the core, Stardust buys the run shops' cosmetics for good ---------- */
  function looksSheet() {
    if (busy || pending) return;
    select(null); hideNote();
    const LK = LOOKS_API;
    LK.looksOf(p);
    const paintShelf = () => {
      const open = coreOwned();
      const groups = LK.LOOK_KINDS.map((k) => {
        const rows = LK.LOOKS.filter((l) => l.kind === k.id).map((l) => {
          const own = LK.ownsLook(p, l.id), short = l.cost - p.dust;
          const act = own ? '<span class="lit">Owned</span>'
            : `<button class="ps-go" type="button" data-look="${esc(l.id)}" ${!open || short > 0 ? 'disabled' : ''}>${!open ? 'Locked' : short > 0 ? 'Need ' + fmt(short) : 'Buy'}</button>`;
          return `<div class="ps-look${own ? ' own' : ''}"><img alt="" src="${esc(l.art || CLIP(k.icon))}"><span><b>${esc(l.name)}</b><small>${DUST_SVG}${fmt(l.cost)}</small></span>${act}</div>`;
        }).join('');
        return `<section><h4>${esc(k.name)}</h4><p class="none">${esc(LK.LOOK_LINES[k.id])}</p><div class="ps-looks-list">${rows}</div></section>`;
      }).join('');
      overEl.innerHTML = `<div class="ps-paper ps-sheet ps-shelf" role="dialog" aria-label="Looks">
        <button class="ps-x" type="button" aria-label="Back to the sky" data-act="back">&times;</button>
        <h2>Looks</h2><p class="sub">${open ? `Spend ${esc(CURRENCY)} on looks you keep in every run.` : esc(LK.LOCKED_LINE)}</p>
        ${groups}
        <div class="acts"><button class="ps-go plain" type="button" data-act="back">Back to the sky</button></div></div>`;
    };
    paintShelf();
    overEl.classList.add('dim'); overEl.hidden = false;
    overEl.querySelector('.ps-sheet').classList.add('in');
    overEl.onclick = (e) => {
      if (!overEl.querySelector('.ps-shelf') || frozen()) return;
      const b = e.target.closest('button'); if (!b) return;
      if (b.dataset.act === 'back') { overEl.onclick = null; closeSheet(); return; }
      const id = b.dataset.look; if (!id) return;
      let res = null;
      try { res = LK.buyLook(p, id); } catch { res = null; }
      if (!res?.ok) { thunk(); return; }
      save(); setDust(p.dust); legend();
      [0, 4, 7].forEach((st, i) => pluck(noteHz(st + 8), i * 0.06, 0.3, 0.8));
      const top = overEl.querySelector('.ps-sheet')?.scrollTop || 0;
      paintShelf();
      const sh = overEl.querySelector('.ps-sheet'); if (sh) sh.scrollTop = top;
    };
    overEl.querySelector('[data-act="back"]').focus({ preventScroll: true });
  }

  /* ---------- note ---------- */
  const TIP_KEY = 'fuzz.prestige.sky.tipped';
  const tipped = () => { try { return win.localStorage.getItem(TIP_KEY) === '1'; } catch { return true; } };
  function showNote() { if (NODES.some((n) => affordable(n))) noteEl.hidden = false; }
  function hideNote() {
    if (noteEl.hidden) return;
    try { win.localStorage.setItem(TIP_KEY, '1'); } catch { /* private mode */ }
    noteEl.classList.add('gone'); later(0.5, () => { noteEl.hidden = true; });
  }
  function placeNote() {
    if (noteEl.hidden) return;
    const n = NODES.find((x) => affordable(x) && x.sx > 0); if (!n) return;
    noteEl.style.left = clamp(n.sx - 200, 8, W - 230) + 'px'; noteEl.style.top = clamp(n.sy - 100, topH, H - 140) + 'px';
  }

  /* ---------- input ---------- */
  const ptrs = new Map(); let downAt = null, moved = 0, pinch0 = null, hoverLabel = false;
  function nodeAt(x, y) { let best = null, bd = 1e9; for (const n of NODES) { if (n.sx === undefined) continue; const d = Math.hypot(n.sx - x, n.sy - y); if (d < Math.max(n.sr, 16) + 6 && d < bd) { bd = d; best = n; } } return best; }
  const clampCam = () => { const lim = R_OUT * 1.1; cam.x = clamp(cam.x, -lim, lim); cam.y = clamp(cam.y, -lim, lim); };
  function zoomAt(sx, sy, k) { const [wx, wy] = s2w(sx, sy); cam.z = clamp(cam.z * k, fitZ * 0.8, fitZ * 4.5); const [wx2, wy2] = s2w(sx, sy); cam.x += wx - wx2; cam.y += wy - wy2; clampCam(); }
  cv.addEventListener('pointerdown', (e) => {
    if (frozen()) return;
    cv.setPointerCapture?.(e.pointerId); ptrs.set(e.pointerId, [e.clientX, e.clientY]); camFly = null;
    if (ptrs.size === 1) { downAt = [e.clientX, e.clientY]; moved = 0; } else if (ptrs.size === 2) { const [a, b] = [...ptrs.values()]; pinch0 = { d: Math.hypot(a[0] - b[0], a[1] - b[1]), z: cam.z }; }
  });
  cv.addEventListener('pointermove', (e) => {
    if (frozen()) return;
    if (!ptrs.has(e.pointerId)) {
      const n = nodeAt(e.clientX, e.clientY), la = labelAt(e.clientX, e.clientY), sh = shooters.some((s) => Math.hypot(s.x - e.clientX, s.y - e.clientY) < s.s * 0.7);
      if (e.pointerType === 'mouse') { if (la >= 0) { focusArm = la; hoverLabel = true; } else if (hoverLabel) { focusArm = -1; hoverLabel = false; } }
      cv.classList.toggle('hot', !!n || la >= 0 || sh); return;
    }
    const prev = ptrs.get(e.pointerId); ptrs.set(e.pointerId, [e.clientX, e.clientY]);
    if (ptrs.size === 2 && pinch0) { const [a, b] = [...ptrs.values()]; const d = Math.hypot(a[0] - b[0], a[1] - b[1]); zoomAt((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (pinch0.z * d) / pinch0.d / cam.z); return; }
    const dx = e.clientX - prev[0], dy = e.clientY - prev[1]; moved += Math.abs(dx) + Math.abs(dy);
    if (moved > 6) { cv.classList.add('drag'); const c = Math.cos(-ROT()), s = Math.sin(-ROT()); cam.x -= (dx * c - dy * s) / cam.z; cam.y -= (dx * s + dy * c) / cam.z; clampCam(); }
  });
  const up = (e) => {
    if (!ptrs.has(e.pointerId)) return; ptrs.delete(e.pointerId); cv.classList.remove('drag'); if (ptrs.size < 2) pinch0 = null;
    if (frozen() || ptrs.size || moved > 6 || !downAt || e.type !== 'pointerup') return;
    if (catchShooter(e.clientX, e.clientY)) return;
    const la = labelAt(e.clientX, e.clientY); if (la >= 0) { goLane(la); return; }
    const n = nodeAt(e.clientX, e.clientY);
    if (n) { if (sel === n && affordable(n)) buy(n); else select(n); } else select(null);
  };
  cv.addEventListener('pointerup', up); cv.addEventListener('pointercancel', up);
  cv.addEventListener('wheel', (e) => { e.preventDefault(); if (frozen()) return; camFly = null; zoomAt(e.clientX, e.clientY, Math.exp(-e.deltaY * 0.0015)); }, { passive: false });
  q('[data-act="zin"]').onclick = () => flyTo(cam.x, cam.y, clamp(cam.z * 1.5, fitZ * 0.8, fitZ * 4.5), 0.35);
  q('[data-act="zout"]').onclick = () => flyTo(cam.x, cam.y, clamp(cam.z / 1.5, fitZ * 0.8, fitZ * 4.5), 0.35);
  q('[data-act="zfit"]').onclick = () => flyTo(0, 0, fitZ, 0.6);
  nextBtn.onclick = () => { if (!frozen()) nextSheet(); };
  q('[data-act="looks"]').onclick = () => { if (!frozen()) looksSheet(); };
  // Enter lights the selected star. Escape is the game's (it opens the pause card above this sheet).
  const onKey = (e) => { if (e.key === 'Enter' && sel && !frozen() && affordable(sel) && (doc.activeElement === doc.body || doc.activeElement === cv)) { e.preventDefault(); buy(sel); } };
  doc.addEventListener('keydown', onKey);

  /* ---------- open and close ---------- */
  let finishing = false;
  function finish() {
    if (finishing) return; finishing = true;
    if (calm()) { close(); start(); return; }
    root.classList.add('out');
    later(0.45, () => { close(); start(); });
  }
  function close() {
    if (closed) return; closed = true;
    win.cancelAnimationFrame(raf);
    win.removeEventListener('resize', onResize);
    doc.removeEventListener('keydown', onKey);
    input?.stop?.();
    root.remove();
  }

  fogR = fogTarget = ringEdge(viewDepth);
  if (!calm()) { cam.z = fitZ * 2.4; cam.rot = -0.5; flyTo(0, 0, fitZ, 2.2); tween(2.2, (pr) => { cam.rot = -0.5 * (1 - ease(pr)); }); }
  K('dust').textContent = fmt(shownDust);
  K('depth').textContent = viewDepth;
  hud(false);
  shownDust = renewal ? p.dust - renewal.gained : p.dust; K('dust').textContent = fmt(shownDust);
  if (renewal) showRenewal(renewal); else if (!tipped()) later(2.4, showNote);
  raf = win.requestAnimationFrame(loop);
  return { close, get open() { return !closed; } };
}
