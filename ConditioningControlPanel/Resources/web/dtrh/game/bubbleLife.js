/* ============================================================================
 * bubbleLife.js - the bubbles' own pictures move (owner, 2026-10-12).
 *
 * No new art: each bubble's shipped sprite is drawn on a small canvas inside
 * .cf-bubble-body and its middle is redrawn moved (the spiral turns, the heart
 * beats, the liquid sloshes). One shared clock at 30 fps draws every live
 * canvas; it slows itself when a frame runs long and stops when none are left.
 *
 * Trance rules hold: slow, nothing strobes. Motion level reduced = slower and
 * smaller, off = the still sprite. chaosField.js calls attachLife() at spawn;
 * a bubble that leaves the DOM drops out by itself.
 * ==========================================================================*/
import { motionScale } from '../shared/motion.js';

const S = 256, C = 128, TAU = Math.PI * 2;
export const LIFE_CAP = 28;          // canvases alive at once; past it a bubble stays still
const FRAME_MS = 33, SLOW_FRAME_MS = 50;

// A material bubble's canvas reaches past the bubble so its motes can drift AROUND it.
const HALO = 1.7;
const HALO_PAD = (S * (HALO - 1)) / 2;
const MOTE_TINT = {
  silicone: '255,214,222', makeup: '255,96,160', pills: '255,148,196',
  lace: '244,226,255', lube: '168,230,255', chrome: '206,214,228',
};
/** The mote colour for a material bubble's id ("material_chrome"), white for one unknown. Pure. */
export function moteTintFor(variantId) {
  return MOTE_TINT[String(variantId || '').replace(/^material_/, '')] || '255,255,255';
}

const rnd = (i) => { const x = Math.sin(i * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); };

/** Which effect a spec wears, or null for a bubble that stays still. Pure. */
export function lifeKindFor(spec) {
  if (!spec || !spec.sprite) return null;
  const id = String(spec.variantId || '');
  if (id === 'pink') return 'pinkfilter';
  if (id === 'plain') return 'bubble';
  if (id.startsWith('material_')) return 'material';
  return Object.prototype.hasOwnProperty.call(FX, id) ? id : null;
}

let off = null, o = null;
function scratch() {
  if (!off) { off = document.createElement('canvas'); off.width = off.height = S; o = off.getContext('2d'); }
}

// The sprite's own middle, redrawn moved: darken the still copy, add the moved one as light.
function inner(ctx, img, R, draw, dim = 0.9, feather = 0.72) {
  o.setTransform(1, 0, 0, 1, 0, 0); o.globalCompositeOperation = 'source-over'; o.filter = 'none'; o.globalAlpha = 1;
  o.clearRect(0, 0, S, S);
  o.save(); draw(o); o.restore();
  o.filter = 'none'; o.globalAlpha = 1;
  o.globalCompositeOperation = 'destination-in';
  let g = o.createRadialGradient(C, C, R * feather, C, C, R);
  g.addColorStop(0, 'rgba(0,0,0,1)'); g.addColorStop(1, 'rgba(0,0,0,0)');
  o.fillStyle = g; o.fillRect(0, 0, S, S);
  o.globalCompositeOperation = 'source-over';
  if (dim > 0) {
    g = ctx.createRadialGradient(C, C, R * feather, C, C, R);
    g.addColorStop(0, `rgba(7,5,12,${dim})`); g.addColorStop(1, 'rgba(7,5,12,0)');
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(C, C, R, 0, TAU); ctx.fill();
  }
  ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.drawImage(off, 0, 0); ctx.restore();
}
const spun = (img, a, s = 1, dx = 0, dy = 0) => (c) => {
  c.translate(C + dx, C + dy); c.rotate(a); c.scale(s, s); c.drawImage(img, -C, -C);
};
function glint(ctx, x, y, size, a, col) {
  if (a <= 0.01) return;
  ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = a; ctx.translate(x, y);
  for (let k = 0; k < 2; k++) {
    const g = ctx.createLinearGradient(-size, 0, size, 0);
    g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(0.5, col); g.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = g; ctx.fillRect(-size, -0.7, size * 2, 1.4); ctx.rotate(Math.PI / 2);
  }
  ctx.fillStyle = col; ctx.beginPath(); ctx.arc(0, 0, 1.3, 0, TAU); ctx.fill();
  ctx.restore();
}
function twinkles(ctx, t, n, R, col, seed, size = 9) {
  for (let i = 0; i < n; i++) {
    const a = rnd(seed + i) * TAU, r = Math.sqrt(rnd(seed + i * 3.3)) * R;
    const ph = (t * (0.25 + rnd(seed + i * 7) * 0.2) + rnd(seed + i * 5)) % 1;
    glint(ctx, C + Math.cos(a) * r, C + Math.sin(a) * r, size * (0.6 + rnd(i + seed) * 0.6), Math.sin(ph * Math.PI) ** 3 * 0.9, col);
  }
}
function ringGlow(ctx, r, a, col, w = 2) {
  if (a <= 0.01) return;
  ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = a;
  ctx.strokeStyle = col; ctx.lineWidth = w;
  ctx.beginPath(); ctx.arc(C, C, r, 0, TAU); ctx.stroke();
  ctx.globalAlpha = a * 0.35; ctx.lineWidth = w * 3.5;
  ctx.beginPath(); ctx.arc(C, C, r, 0, TAU); ctx.stroke();
  ctx.restore();
}
// The soap film: a faint iridescent band that walks round the rim.
const FILM = ['rgba(255,120,200,1)', 'rgba(120,220,255,0)', 'rgba(170,255,190,0.8)', 'rgba(255,230,140,0)', 'rgba(190,140,255,0.9)', 'rgba(255,120,200,1)'];
function film(ctx, t, a = 0.16, r = 88) {
  if (!ctx.createConicGradient) return;
  const g = ctx.createConicGradient(t * 0.35, C, C);
  FILM.forEach((c, i) => g.addColorStop(i / (FILM.length - 1), c));
  ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = a;
  ctx.strokeStyle = g; ctx.lineWidth = 7;
  ctx.beginPath(); ctx.arc(C, C, r, 0, TAU); ctx.stroke(); ctx.restore();
}
function clipDisc(ctx, r) { ctx.beginPath(); ctx.arc(C, C, r, 0, TAU); ctx.clip(); }
// A slow two-bump beat, 0..1.
const beatOf = (ph) => Math.exp(-ph * 9) + 0.6 * Math.exp(-Math.pow((ph - 0.22) * 9, 2));

// Every effect draws in a 256 box over the still sprite. t = seconds, m = 1 full / under 1 reduced.
const FX = {
  flash(ctx, img, t, m) {
    // no redrawn middle here: the sprite is all light, and the dark disc under a moved
    // copy read as a black hole in the bubble (owner, 2026-10-12). Rays and glints only.
    const p = Math.sin(t * 1.5);
    ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.translate(C, C); ctx.rotate(-t * 0.22);
    for (let k = 0; k < 4; k++) {
      const L = 74 + 14 * Math.sin(t * 1.1 + k * 1.7);
      const g = ctx.createLinearGradient(0, 0, L, 0);
      g.addColorStop(0, `rgba(255,225,240,${0.5 + 0.2 * p})`); g.addColorStop(1, 'rgba(255,200,230,0)');
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(0, -1.6); ctx.lineTo(L, 0); ctx.lineTo(0, 1.6); ctx.fill();
      ctx.rotate(Math.PI / 2);
    }
    ctx.restore();
    twinkles(ctx, t, 5, 76, 'rgba(255,230,245,1)', 11, 7);
    film(ctx, t);
  },
  subliminal(ctx, img, t, m) {
    const ph = (t / 1.7) % 1;
    inner(ctx, img, 66, spun(img, 0, 1 + 0.085 * beatOf(ph) * m));
    for (let k = 0; k < 2; k++) {
      const q = (ph + k * 0.5) % 1;
      ringGlow(ctx, 34 + q * 52, (1 - q) * 0.4, 'rgba(190,150,255,1)', 1.5);
    }
    film(ctx, t);
  },
  // Pop-up Notification heart: the same slow beat, in its own coral.
  heart(ctx, img, t, m) {
    const ph = (t / 2.1) % 1;
    inner(ctx, img, 62, spun(img, 0, 1 + 0.09 * beatOf(ph) * m));
    for (let k = 0; k < 2; k++) {
      const q = (ph + k * 0.5) % 1;
      ringGlow(ctx, 36 + q * 48, (1 - q) * 0.35, 'rgba(255,150,140,1)', 1.5);
    }
    film(ctx, t);
  },
  pinkfilter(ctx, img, t, m) {
    const tilt = (Math.sin(t * 1.15) * 0.17 + Math.sin(t * 2.7) * 0.04) * m;
    inner(ctx, img, 60, spun(img, tilt));
    ringGlow(ctx, 47, 0.18 + 0.14 * Math.sin(t * 1.15), 'rgba(255,60,190,1)', 2);
    film(ctx, t);
  },
  spiral(ctx, img, t, m) {
    inner(ctx, img, 70, spun(img, t * 0.55, 1 + 0.035 * Math.sin(t * 0.9)));
    ctx.save(); ctx.globalAlpha = 0.35; inner(ctx, img, 70, spun(img, t * 0.55 - 0.5, 1.07), 0); ctx.restore();
    film(ctx, t);
  },
  braindrain(ctx, img, t, m) {
    inner(ctx, img, 88, (c) => {
      c.globalCompositeOperation = 'lighter';
      for (let k = 0; k < 4; k++) {
        const q = ((t * 0.22 * (0.5 + m * 0.5) + k / 4) % 1);
        c.globalAlpha = Math.sin(q * Math.PI) * 0.5;
        c.drawImage(img, Math.sin(t * 0.8 + k) * 3, -14 + q * 46, S, S * (1 + q * 0.08));
      }
    }, 0.92, 0.8);
    film(ctx, t);
  },
  bambifreeze(ctx, img, t, m) {
    inner(ctx, img, 68, spun(img, t * 0.14, 1 + 0.025 * Math.sin(t * 0.8)));
    ctx.save(); clipDisc(ctx, 90); ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 14; i++) {
      const q = (t * (0.07 + rnd(i) * 0.06) * (0.4 + m * 0.6) + rnd(i * 2.2)) % 1;
      const x = 44 + rnd(i * 5.1) * 168 + Math.sin(t * 0.9 + i) * 5;
      ctx.fillStyle = `rgba(200,240,255,${Math.sin(q * Math.PI) * 0.7})`;
      ctx.beginPath(); ctx.arc(x, 36 + q * 184, 0.8 + rnd(i * 9) * 1.3, 0, TAU); ctx.fill();
    }
    ctx.restore();
    twinkles(ctx, t, 4, 62, 'rgba(170,240,255,1)', 23, 8);
    film(ctx, t);
  },
  video(ctx, img, t, m) {
    const p = 0.5 + 0.5 * Math.sin(t * 1.8);
    inner(ctx, img, 30, spun(img, 0, 1 + 0.09 * p * m, p * 1.5 * m, 0), 0.9, 0.6);
    ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.lineCap = 'round';
    const a0 = t * 1.1;
    for (let k = 0; k < 10; k++) {
      ctx.strokeStyle = `rgba(255,150,165,${(k / 10) * 0.75})`; ctx.lineWidth = 2.6;
      ctx.beginPath(); ctx.arc(C, C + 1, 43, a0 + k * 0.11, a0 + (k + 1) * 0.11 + 0.02); ctx.stroke();
    }
    ctx.restore();
    ringGlow(ctx, 43, 0.12 + 0.12 * p, 'rgba(255,60,90,1)', 2);
    film(ctx, t);
  },
  htlink(ctx, img, t, m) {
    inner(ctx, img, 86, spun(img, Math.sin(t * 0.35) * 0.22, 1 + 0.02 * Math.sin(t * 0.7)), 0.9, 0.8);
    ctx.save(); clipDisc(ctx, 88);
    for (let i = 0; i < 6; i++) {
      const q = (t * (0.11 + rnd(i) * 0.07) * (0.4 + m * 0.6) + rnd(i * 4.4)) % 1;
      ctx.save();
      ctx.translate(58 + rnd(i * 6.6) * 140, 28 + q * 200); ctx.rotate(rnd(i) * 3 + t * (rnd(i * 2) - 0.5));
      ctx.globalAlpha = Math.sin(q * Math.PI) * 0.8;
      ctx.fillStyle = 'rgba(205,225,240,0.9)'; ctx.fillRect(-9, -6.5, 18, 13);
      ctx.fillStyle = 'rgba(70,95,120,0.9)'; ctx.fillRect(-7, -4.5, 14, 9);
      ctx.restore();
    }
    ctx.restore();
    film(ctx, t);
  },
  golden(ctx, img, t, m) {
    inner(ctx, img, 84, spun(img, t * 0.2, 1 + 0.03 * Math.sin(t * 0.7)), 0.9, 0.78);
    twinkles(ctx, t, 9, 60, 'rgba(255,220,120,1)', 31, 9);
    film(ctx, t);
  },
  gold_droplet(ctx, img, t, m) {
    const bob = Math.sin(t * 1.3) * 3.5 * m;
    inner(ctx, img, 34, spun(img, 0, 1, 0, bob), 0.9, 0.6);
    for (let k = 0; k < 2; k++) {
      const q = (t / 2.6 + k * 0.5) % 1;
      ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = (1 - q) * 0.45;
      ctx.strokeStyle = 'rgba(255,205,90,1)'; ctx.lineWidth = 1.4;
      ctx.beginPath(); ctx.ellipse(C, C + 34, 6 + q * 40, 2 + q * 11, 0, 0, TAU); ctx.stroke(); ctx.restore();
    }
    glint(ctx, C - 4, C - 6 + bob, 10, 0.4 + 0.5 * Math.max(0, Math.sin(t * 0.9)) ** 4, 'rgba(255,240,190,1)');
    film(ctx, t, 0.22);
  },
  prism(ctx, img, t, m) {
    inner(ctx, img, 92, (c) => { c.filter = `hue-rotate(${Math.round((t * 22) % 360)}deg)`; spun(img, t * 0.28)(c); }, 0.95, 0.86);
    film(ctx, t, 0.22, 90);
  },
  // The white rabbit (and the sweeper): the ball never sits still.
  darter(ctx, img, t, m) {
    const a = t * 3.2, r = 4.5 * m;
    inner(ctx, img, 58, spun(img, 0, 1 + 0.06 * Math.sin(t * 6.4) * m, Math.cos(a) * r, Math.sin(a * 1.3) * r));
    twinkles(ctx, t * 1.6, 4, 70, 'rgba(255,190,235,1)', 53, 7);
    film(ctx, t * 1.5);
  },
  // The Tease: the cross leans one way, then the other, and its glow comes and goes.
  tease(ctx, img, t, m) {
    const p = 0.5 + 0.5 * Math.sin(t * 1.3);
    inner(ctx, img, 58, spun(img, Math.sin(t * 0.65) * 0.3 * m, 1 + 0.05 * p));
    ringGlow(ctx, 50 + 6 * p, 0.1 + 0.2 * p, 'rgba(255,60,90,1)', 1.5);
    film(ctx, t);
  },
  // The Brittle: thin glass. It turns a little and light runs along the cracks.
  brittle(ctx, img, t, m) {
    inner(ctx, img, 66, spun(img, Math.sin(t * 0.3) * 0.5, 1));
    twinkles(ctx, t * 1.3, 7, 52, 'rgba(230,245,255,1)', 67, 10);
    const q = (t / 3.4) % 1;
    ringGlow(ctx, 58, Math.sin(q * Math.PI) ** 6 * 0.5, 'rgba(220,240,255,1)', 1.2);
    film(ctx, t);
  },
  // The Echo: copies of itself leave the middle and thin out.
  echo(ctx, img, t, m) {
    for (let k = 0; k < 3; k++) {
      const q = (t / 2.8 + k / 3) % 1;
      ringGlow(ctx, 34 + q * 50 * (0.5 + m * 0.5), (1 - q) * 0.45, 'rgba(205,195,240,1)', 1.5);
    }
    const g = (t / 2.8) % 1;
    ctx.save(); ctx.globalAlpha = (1 - g) * 0.16;
    inner(ctx, img, 84, spun(img, 0, 1 + g * 0.35), 0, 0.8);
    ctx.restore();
    film(ctx, t);
  },
  // A crafting ingredient: its own cutout, with a few glints on it.
  // and a slow ring of motes in its own colour, drifting round and a little outward.
  material(ctx, img, t, m, e) {
    twinkles(ctx, t, 4, 70, 'rgba(255,255,255,1)', 79, 9);
    const tint = (e && e.tint) || '255,255,255';
    const n = 10;
    ctx.save(); ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < n; i++) {
      const life = (t * (0.11 + rnd(i * 3.1) * 0.07) + rnd(i * 5.7)) % 1;     // 0..1, then it starts over
      const a = (i / n) * TAU + rnd(i) * 0.9 + t * (i % 2 ? 0.22 : -0.16);
      const r = 104 + life * 62 + 6 * Math.sin(t * 0.9 + i);
      const x = C + Math.cos(a) * r, y = C + Math.sin(a) * r - life * 14;
      const alpha = Math.sin(life * Math.PI) ** 1.5 * (0.5 + 0.3 * rnd(i * 9.3));
      if (alpha <= 0.02) continue;
      const size = 5 + 5 * rnd(i * 2.3);
      const g = ctx.createRadialGradient(x, y, 0, x, y, size);
      g.addColorStop(0, `rgba(${tint},${alpha})`); g.addColorStop(1, `rgba(${tint},0)`);
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, size, 0, TAU); ctx.fill();
      if (i % 3 === 0) glint(ctx, x, y, size * 1.5, alpha * 0.8, 'rgba(255,255,255,1)');
    }
    ctx.restore();
  },
  bubble(ctx, img, t, m) {
    film(ctx, t, 0.36, 88); film(ctx, -t * 0.7 + 2, 0.2, 78);
    twinkles(ctx, t, 3, 70, 'rgba(255,255,255,1)', 47, 7);
  },
};

// ---- sprites: one 256 copy per url, shared by every bubble that wears it ----
const sprites = new Map();
function spriteFor(url) {
  let s = sprites.get(url);
  if (s) return s;
  s = { canvas: null, ready: false, failed: false };
  sprites.set(url, s);
  const img = new Image();
  img.onload = () => {
    try {
      const c = document.createElement('canvas'); c.width = c.height = S;
      c.getContext('2d').drawImage(img, 0, 0, S, S);
      s.canvas = c; s.ready = true;
    } catch (_) { s.failed = true; }
  };
  img.onerror = () => { s.failed = true; };
  img.src = url;
  return s;
}

// ---- the one clock ----
const lives = new Set();
let raf = 0, last = 0, cost = 0, seq = 0;

function drop(e) {
  lives.delete(e);
  try { e.canvas.remove(); } catch (_) {}
}

let heldByPause = false;
/** A held pause holds the pictures where they are. */
export function setLifePaused(v) { heldByPause = !!v; }

function tick(ms) {
  raf = lives.size ? requestAnimationFrame(tick) : 0;
  if (ms - last < (cost > 9 ? SLOW_FRAME_MS : FRAME_MS)) return;
  last = ms;
  if (document.hidden || heldByPause) return;
  const m = motionScale();
  const t0 = ms / 1000;
  const began = performance.now();
  for (const e of lives) {
    if (!e.canvas.isConnected) { lives.delete(e); continue; }
    const sp = e.sprite;
    if (sp.failed) { drop(e); continue; }
    if (!sp.ready) continue;
    if (m <= 0 && e.still) continue;
    const ctx = e.ctx;
    const pad = e.halo ? HALO_PAD : 0;
    ctx.setTransform(e.k, 0, 0, e.k, pad * e.k, pad * e.k);
    ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-over'; ctx.filter = 'none';
    ctx.clearRect(-pad, -pad, S + pad * 2, S + pad * 2);
    ctx.drawImage(sp.canvas, 0, 0);
    if (!e.shown) { e.shown = true; e.body.style.backgroundImage = 'none'; }
    e.still = m <= 0;
    if (e.still) continue;
    try { FX[e.kind](ctx, sp.canvas, (t0 + e.phase) * (m < 1 ? 0.5 : 1), m < 1 ? 0.4 : 1, e); }
    catch (_) { /* this frame keeps the still sprite */ }
  }
  cost += (performance.now() - began - cost) * 0.1;
}

/** Give a bubble's body its living picture. No-op for a bubble with no effect,
 * past the cap, or where a canvas is not to be had. */
export function attachLife(body, spec) {
  const kind = lifeKindFor(spec);
  if (!kind || !body || lives.size >= LIFE_CAP) return;
  try {
    scratch();
    const canvas = document.createElement('canvas');
    const res = (spec.sizePx || 0) * (window.devicePixelRatio || 1) > 150 ? S : 128;
    const halo = kind === 'material';
    canvas.width = canvas.height = halo ? Math.round(res * HALO) : res;
    canvas.className = halo ? 'cf-bubble-life cf-bubble-life--halo' : 'cf-bubble-life';
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    body.insertBefore(canvas, body.firstChild);
    lives.add({ canvas, ctx, body, kind, halo, tint: halo ? moteTintFor(spec.variantId) : null, k: res / S, sprite: spriteFor(spec.sprite), phase: rnd(++seq * 13) * 20, shown: false, still: false });
    if (!raf) raf = requestAnimationFrame(tick);
  } catch (_) { /* the still sprite stays */ }
}
