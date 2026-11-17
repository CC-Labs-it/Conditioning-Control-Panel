/* sheet/skins.js - the animated bubble skins, ported from the owner's bubble lab (C:/wt-shop/ref/bubble-lab.html): each
 * skin has something alive inside, its own pop with particles, and its own sound (sfx, played only from a click, through
 * sheet/sound.js). Plain canvas 2D, no DOM besides the shell picture. The shop shows these moving, never stills.
 *   makeBubble(kind, x, y, r)   one bubble; kind = the effect id's last part (soap, rose, champagne, heart, spiral ...)
 *   stepBubble / drawBubble / popBubble   per frame; popBubble starts the skin's own pop (pre-burst, then particles)
 *   stepParts / drawParts       a world's particles ({ c, p: [] }); counts scale with the bubble and are capped
 *   loadShells(src)             the shipped soap bubble picture (sheet/art/soap.png), tinted per skin */

const TAU = Math.PI * 2;
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
const lerp = (a, b, k) => a + (b - a) * k;
const frac = (v) => v - Math.floor(v);
const smooth = (a, b, x) => { if (b <= a) return x < a ? 0 : 1; const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
const backOut = (k) => { const c = 1.9, x = k - 1; return 1 + (c + 1) * x * x * x + c * x * x; };
export function rng(seed) { let s = seed >>> 0; return () => { s = (s + 0x6D2B79F5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
export const hash = (a, b) => { let h = (Math.imul(a | 0, 374761393) + Math.imul(b | 0, 668265263)) >>> 0; h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };
const hex = (n) => 'rgb(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ')';
export const NOTES = [440, 493.88, 554.37, 659.25, 739.99];   // A major pentatonic, the lab's key
/** Particle counts: the lab's showcase numbers, scaled down for small bubbles (a shelf thumbnail is about 30 px). */
const many = (b, n) => Math.max(3, Math.round(n * clamp(b.r / 46, 0.3, 1)));

/* ---------- shells: the shipped soap bubble, tinted where a skin wants it ---------- */
const shells = {};
export const TINTS = { rose: 'rgba(255,111,168,.62)', mint: 'rgba(79,224,180,.62)', lilac: 'rgba(169,139,255,.62)', gold: 'rgba(255,212,140,.5)', ice: 'rgba(160,214,255,.7)' };
export function loadShells(src, doc = globalThis.document, onload = () => {}) {
  if (shells.soap || !doc || typeof Image === 'undefined') return;
  const im = new Image();
  im.onload = () => {
    shells.soap = im;
    for (const [k, col] of Object.entries(TINTS)) {
      const c = doc.createElement('canvas'); c.width = c.height = 256;
      const x = c.getContext('2d'); x.drawImage(im, 0, 0, 256, 256); x.globalCompositeOperation = 'source-atop'; x.fillStyle = col; x.fillRect(0, 0, 256, 256);
      shells[k] = c;
    }
    onload();
  };
  im.src = src;
}
function shell(c, r, key, alpha) {
  const im = shells[key] || shells.soap;
  c.save(); c.globalAlpha *= alpha == null ? 1 : alpha;
  if (im) { const s = 2 * r / 0.72; c.drawImage(im, -s / 2, -s / 2, s, s); }
  else { c.strokeStyle = 'rgba(255,255,255,.6)'; c.lineWidth = r * 0.05; c.beginPath(); c.arc(0, 0, r * 0.96, 0, TAU); c.stroke(); }
  c.restore();
}
function gloss(c, r, a) { c.save(); c.rotate(-0.62); c.fillStyle = 'rgba(255,255,255,' + a + ')'; c.beginPath(); c.ellipse(0, -r * 0.6, r * 0.26, r * 0.11, 0, 0, TAU); c.fill(); c.restore(); }

/* ---------- the heart outline, unit size: the classic curve pulled a fifth toward a circle, then smoothed ---------- */
let HEART = null;
function heart() {
  if (HEART || typeof Path2D === 'undefined') return HEART;
  const N = 120; let pts = [];
  for (let i = 0; i < N; i++) {
    const a = i / N * TAU, hx = 16 * Math.pow(Math.sin(a), 3) / 17, hy = -(13 * Math.cos(a) - 5 * Math.cos(2 * a) - 2 * Math.cos(3 * a) - Math.cos(4 * a)) / 17 - 0.13;
    pts.push([lerp(hx, Math.sin(a) * 0.88, 0.2), lerp(hy, -Math.cos(a) * 0.86 + 0.02, 0.2)]);
  }
  for (let pass = 0; pass < 4; pass++) pts = pts.map((p, i) => { let x = 0, y = 0; const wt = [1, 2, 3, 2, 1]; for (let d = -2; d <= 2; d++) { const q = pts[(i + d + N) % N]; x += q[0] * wt[d + 2]; y += q[1] * wt[d + 2]; } return [x / 9, y / 9]; });
  HEART = new Path2D(); pts.forEach((p, i) => (i ? HEART.lineTo(p[0], p[1]) : HEART.moveTo(p[0], p[1]))); HEART.closePath();
  return HEART;
}
function star4(c, x, y, s, col) { c.fillStyle = col; c.beginPath(); c.moveTo(x, y - s); c.quadraticCurveTo(x, y, x + s, y); c.quadraticCurveTo(x, y, x, y + s); c.quadraticCurveTo(x, y, x - s, y); c.quadraticCurveTo(x, y, x, y - s); c.fill(); }

/* ---------- the Loom, as CCP.Core/Super/LoomSpiral.cs weaves it: a new spiral for every bubble ---------- */
const SWATCH = [0xff69b4, 0xe56cc0, 0x8a5cff, 0x00e5ff, 0x39ff9d, 0xffd94d, 0xff6a2f, 0xffffff];
function threads(n, rnd) { const p = SWATCH.slice(); for (let i = p.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [p[i], p[j]] = [p[j], p[i]]; } return p.slice(0, clamp(n, 1, p.length)); }
const snap = (v, s) => Math.round(v / s) * s;
export function rollSpec(rnd) {
  const L = { arms: 1 + Math.floor(rnd() * 8), turns: clamp(snap(0.5 + rnd() * 3.5, 0.25), 0.5, 6), duty: clamp(snap(0.3 + rnd() * 0.4, 0.05), 0.2, 0.8),
    style: Math.floor(rnd() * 4), dir: rnd() < 0.5 ? 1 : -1, colors: threads(1 + Math.floor(rnd() * 3), rnd), gradient: rnd() < 0.35 };
  const on2 = rnd() < 0.35;
  const L2 = { arms: 2 + Math.floor(rnd() * 5), turns: clamp(snap(0.5 + rnd() * 2.5, 0.25), 0.5, 6), duty: 0.5, style: 0, dir: -L.dir, colors: threads(1, rnd), gradient: false };
  return { L, L2, on2, glow: rnd() < 0.4 ? 0.3 + rnd() * 0.5 : 0 };
}
function styleT(style, rc) { if (style === 0) return Math.pow(rc, 0.5714286); if (style === 1) return rc; if (style === 2) return 0.5 - Math.sin(Math.asin(1 - 2 * rc) / 3); return Math.log(1 + rc * 6) / 1.9459101; }
/** Rendered once per bubble into a small picture, then only rotated (the styles turn as a pure angular offset). */
function loomTex(l, glow, size, doc) {
  const cv = doc.createElement('canvas'); cv.width = cv.height = size;
  const c = cv.getContext('2d'); if (!c) return null;
  const img = c.createImageData(size, size), d = img.data, half = size / 2, px = 2 / size, n = l.colors.length;
  const pick = (i) => l.colors[((Math.floor(i) % n) + n) % n];
  for (let y = 0, o = 0; y < size; y++) for (let x = 0; x < size; x++, o += 4) {
    const ux = (x + 0.5 - half) / half, uy = (y + 0.5 - half) / half, r = Math.sqrt(ux * ux + uy * uy);
    if (r >= 1) continue;
    const a = Math.atan2(uy, ux) - TAU * l.turns * styleT(l.style, r), qf = frac(a / TAU), pos = qf * l.arms, arm = Math.floor(pos), s = pos - arm;
    const aa = Math.min(px * l.arms / (TAU * Math.max(r, 0.02)), 0.45);
    let cov = smooth(0, aa, s) * (1 - smooth(l.duty, l.duty + aa, s));
    if (glow > 0.001) { const k = 14 + (4 - 14) * glow, halo = Math.exp(-Math.max(0, s - l.duty) * k) + Math.exp(-(1 - s) * k); cov = Math.min(1, cov + glow * 0.55 * halo * (1 - cov)); }
    let col;
    if (!l.gradient) col = pick(arm);
    else { const g = frac(qf * l.arms / n) * n, i0 = Math.floor(g), f = g - i0, c0 = pick(i0), c1 = pick(i0 + 1);
      col = (Math.round(lerp((c0 >> 16) & 255, (c1 >> 16) & 255, f)) << 16) | (Math.round(lerp((c0 >> 8) & 255, (c1 >> 8) & 255, f)) << 8) | Math.round(lerp(c0 & 255, c1 & 255, f)); }
    d[o] = (col >> 16) & 255; d[o + 1] = (col >> 8) & 255; d[o + 2] = col & 255; d[o + 3] = Math.round(cov * (1 - smooth(0.74, 1, r) * 0.85) * 255);
  }
  c.putImageData(img, 0, 0);
  return cv;
}

/* ---------- particles ---------- */
const P = (w, o) => { o.t = 0; o.rot = o.rot || 0; if (w.p.length < (w.cap || 600)) w.p.push(o); };
const wave = (w, b, color, grow, life) => P(w, { kind: 'wave', x: b.x, y: b.y, size: b.r, grow: grow || 1.7, life: life || 0.38, color, vx: 0, vy: 0, g: 0, drag: 0 });
export function stepParts(w, dt) {
  for (let i = w.p.length - 1; i >= 0; i--) {
    const p = w.p[i]; p.t += dt;
    if (p.t >= p.life) { w.p.splice(i, 1); continue; }
    if (p.kind === 'orbit') { p.rad += p.vr * dt; p.vr *= Math.exp(-p.drag * dt); p.ang += p.w * dt; p.w *= Math.exp(-1.6 * dt); p.x = p.cx + Math.cos(p.ang) * p.rad; p.y = p.cy + Math.sin(p.ang) * p.rad; continue; }
    const k = Math.exp(-(p.drag || 0) * dt);
    p.vx *= k; p.vy = p.vy * k + (p.g || 0) * dt;
    p.x += p.vx * dt + (p.swa ? Math.sin(p.t * p.swf + p.sw0) * p.swa * dt : 0); p.y += p.vy * dt;
    if (p.vr) p.rot += p.vr * dt;
  }
}
export function drawParts(w) {
  const c = w.c, H = heart();
  for (const p of w.p) {
    const u = p.t / p.life, a = Math.min(1, (1 - u) / 0.45);
    c.save(); c.globalAlpha = a; c.translate(p.x, p.y);
    switch (p.kind) {
      case 'dot': case 'orbit': c.fillStyle = p.color; c.beginPath(); c.arc(0, 0, p.size * (p.shrink ? 1 - u * 0.6 : 1), 0, TAU); c.fill(); break;
      case 'star': star4(c, 0, 0, p.size * (0.35 + 0.65 * Math.abs(Math.sin(p.t * p.tw))), p.color); break;
      case 'ring': c.strokeStyle = p.color; c.lineWidth = Math.max(0.6, p.size * 0.2); c.beginPath(); c.arc(0, 0, p.size, 0, TAU); c.stroke();
        c.fillStyle = 'rgba(255,255,255,.85)'; c.beginPath(); c.arc(-p.size * 0.35, -p.size * 0.35, p.size * 0.22, 0, TAU); c.fill(); break;
      case 'heart': if (!H) break; c.rotate(p.rot); c.scale(p.size, p.size); c.fillStyle = p.color; c.fill(H);
        c.fillStyle = 'rgba(255,255,255,.55)'; c.beginPath(); c.ellipse(-0.38, -0.4, 0.2, 0.11, -0.6, 0, TAU); c.fill(); break;
      case 'hwave': { if (!H) break; const s = p.size * (1 + p.grow * u); c.scale(s, s); c.strokeStyle = p.color; c.lineWidth = 0.06 * (1 - u); c.stroke(H); break; }
      case 'wave': c.strokeStyle = p.color; c.lineWidth = Math.max(0.5, p.size * 0.09 * (1 - u)); c.beginPath(); c.arc(0, 0, p.size * (1 + (p.grow - 1) * (1 - (1 - u) * (1 - u))), 0, TAU); c.stroke(); break;
      case 'shard': c.rotate(p.rot); c.beginPath(); p.pts.forEach((q, i) => (i ? c.lineTo(q[0], q[1]) : c.moveTo(q[0], q[1]))); c.closePath();
        c.fillStyle = p.color; c.fill(); c.strokeStyle = 'rgba(255,255,255,.85)'; c.lineWidth = 1; c.stroke(); break;
      case 'square': { c.rotate(p.rot); const f = Math.abs(Math.sin(p.rot * 1.7)); c.globalAlpha = a * (0.35 + 0.65 * f); c.fillStyle = f > 0.8 ? '#ffffff' : p.color; c.fillRect(-p.size / 2, -p.size / 2 * (0.3 + 0.7 * f), p.size, p.size * (0.3 + 0.7 * f)); break; }
      case 'beam': { c.rotate(p.rot + u * p.vr0); const g = c.createLinearGradient(0, 0, p.size, 0); g.addColorStop(0, p.color); g.addColorStop(1, 'rgba(255,255,255,0)'); c.fillStyle = g; c.globalAlpha = a * 0.7;
        c.beginPath(); c.moveTo(0, 0); c.lineTo(p.size * (0.4 + u), -p.size * (p.wd || 0.05)); c.lineTo(p.size * (0.4 + u), p.size * (p.wd || 0.05)); c.closePath(); c.fill(); break; }
      case 'flake': c.rotate(p.rot); c.strokeStyle = p.color; c.lineWidth = Math.max(1, p.size * 0.28); c.beginPath();
        for (let i = 0; i < 3; i++) { const q = i * Math.PI / 3; c.moveTo(-Math.cos(q) * p.size, -Math.sin(q) * p.size); c.lineTo(Math.cos(q) * p.size, Math.sin(q) * p.size); } c.stroke(); break;
      case 'stick': c.rotate(p.rot); c.strokeStyle = p.color; c.lineWidth = p.lw; c.lineCap = 'round'; c.beginPath(); c.moveTo(-p.size / 2, 0); c.lineTo(p.size / 2, 0); c.stroke(); break;
      case 'puff': { const s = p.size * (1 + p.grow * u), g = c.createRadialGradient(0, 0, 0, 0, 0, s); g.addColorStop(0, p.color); g.addColorStop(1, 'rgba(255,255,255,0)'); c.globalAlpha = a * 0.6; c.fillStyle = g; c.beginPath(); c.arc(0, 0, s, 0, TAU); c.fill(); break; }
      default: break;
    }
    c.restore();
  }
}
const soapBurst = (w, b) => { wave(w, b, 'rgba(255,255,255,.8)'); for (let i = 0, n = many(b, 22); i < n; i++) { const a = b.rnd() * TAU, v = b.r * (1.5 + b.rnd() * 4); P(w, { kind: 'dot', x: b.x + Math.cos(a) * b.r * 0.8, y: b.y + Math.sin(a) * b.r * 0.8, vx: Math.cos(a) * v, vy: Math.sin(a) * v, g: b.r * 8, drag: 3, life: 0.25 + b.rnd() * 0.3, size: b.r * (0.012 + b.rnd() * 0.016) + 0.4, color: 'rgba(255,255,255,.9)' }); } };
const soapSfx = (b, t, au) => { au.osc('sine', 380, b.note * 2, t, 0.07, 0.35); au.nz(t, 0.02, 0.1, 'highpass', 3000); };
const tinted = (key) => ({ pre: 0, rise: 1, draw(c, b) { shell(c, b.r, key); }, burst: soapBurst, sfx: soapSfx });

/* ---------- the skins ---------- */
export const SKINS = {
  soap: { pre: 0, rise: 1, draw(c, b) { shell(c, b.r, 'soap'); }, burst: soapBurst, sfx: soapSfx },
  rose: tinted('rose'), mint: tinted('mint'), lilac: tinted('lilac'),

  champagne: { pre: 0, rise: 1.15,
    draw(c, b, t) {
      const r = b.r;
      c.save(); c.beginPath(); c.arc(0, 0, r * 0.93, 0, TAU); c.clip();
      const g = c.createLinearGradient(0, -r, 0, r); g.addColorStop(0, 'rgba(255,224,160,.04)'); g.addColorStop(1, 'rgba(255,196,90,.24)');
      c.fillStyle = g; c.fillRect(-r, -r, 2 * r, 2 * r);
      const breathe = 1 + 0.28 * Math.sin(t * 0.9 + b.seed), beads = r < 30 ? 36 : 70;   // every bead takes its own crooked way up
      for (let i = 0; i < beads; i++) {
        const h1 = hash(b.seed, i), h2 = hash(b.seed + 7, i), h3 = hash(b.seed + 11, i), h4 = hash(b.seed + 17, i), h5 = hash(b.seed + 23, i);
        const u = frac(h1 + t * (0.65 + h2 * 1.15)), y = lerp(r * 0.82, -r * 0.6, Math.pow(u, 0.85)), wd = r * (0.02 + 0.2 * Math.pow(u, 0.8)) * breathe;
        const x = (i % 3 - 1) * r * 0.08 * (1 - u) + wd * (0.62 * Math.sin(u * (5 + h3 * 9) + h4 * TAU + t * (0.6 + h5)) + 0.38 * Math.sin(u * (13 + h5 * 14) + h3 * TAU - t * (1.3 + h4))) + (h4 - 0.5) * r * 0.34 * u * u;
        const s = r * (0.008 + 0.022 * h3 * h3 + 0.006 * u) + 0.3, al = (0.45 + 0.55 * h5) * Math.min(1, u * 8) * Math.min(1, (1 - u) * 10);
        c.fillStyle = 'rgba(255,250,232,' + al + ')'; c.beginPath(); c.arc(x, y, s, 0, TAU); c.fill();
      }
      for (let i = 0; i < 4; i++) {
        const h1 = hash(b.seed + 31, i), h2 = hash(b.seed + 37, i), h3 = hash(b.seed + 41, i);
        const u = frac(h1 + t * (0.15 + h2 * 0.12)), y = lerp(r * 0.7, -r * 0.62, u);
        const x = (h3 - 0.5) * r * 1.1 * Math.sqrt(Math.max(0, 1 - Math.pow(y / (r * 0.92), 2))) + Math.sin(t * 1.4 + i * 2) * r * 0.05;
        const s = r * (0.03 + 0.03 * h2), al = 0.6 * Math.min(1, u * 6) * Math.min(1, (1 - u) * 6);
        c.strokeStyle = 'rgba(255,236,190,' + al + ')'; c.lineWidth = Math.max(0.6, s * 0.2); c.beginPath(); c.arc(x, y, s, 0, TAU); c.stroke();
      }
      for (let i = 0; i < 38; i++) {
        const h = hash(b.seed + 3, i), an = -Math.PI / 2 + (h - 0.5) * 1.9, life = frac(h * 9 + t * (0.5 + h));
        const rr = r * (0.84 - hash(b.seed + 5, i) * 0.2), s = r * (0.012 + 0.022 * hash(b.seed + 9, i)) * (0.4 + 0.6 * Math.sin(life * Math.PI));
        c.fillStyle = 'rgba(255,250,235,.5)'; c.beginPath(); c.arc(Math.cos(an) * rr, Math.sin(an) * rr, Math.max(0.2, s), 0, TAU); c.fill();
      }
      c.restore();
      shell(c, r, 'gold');
    },
    burst(w, b) {
      const r = b.r;
      wave(w, b, 'rgba(255,214,140,.9)', 1.9, 0.4);
      for (let i = 0, n = many(b, 120); i < n; i++) { const a = -Math.PI / 2 + (b.rnd() - 0.5) * TAU * 0.85, v = r * (1 + b.rnd() * 5.5), d = b.rnd() * r * 0.8;
        P(w, { kind: i % 3 ? 'dot' : 'ring', x: b.x + Math.cos(a) * d, y: b.y + Math.sin(a) * d, vx: Math.cos(a) * v, vy: Math.sin(a) * v, g: -r * 3.4, drag: 2.6, life: 0.5 + b.rnd() * 1.1, size: r * (0.012 + b.rnd() * 0.028) + 0.3, color: i % 3 ? 'rgba(255,244,214,.9)' : 'rgba(255,214,140,.95)', swa: r * 0.9, swf: 6 + b.rnd() * 7, sw0: b.rnd() * 6 }); }
      for (let i = 0, n = many(b, 34); i < n; i++) { const a = b.rnd() * TAU, v = r * (3 + b.rnd() * 6); P(w, { kind: 'dot', x: b.x, y: b.y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, g: 0, drag: 5, life: 0.2 + b.rnd() * 0.25, size: r * 0.012 + 0.3, color: 'rgba(255,236,190,.9)' }); }
    },
    sfx(b, t, au) {
      au.osc('sine', 190, 70, t, 0.09, 0.55); au.nz(t, 0.035, 0.3, 'bandpass', 1300, 0, 1.2); au.nz(t + 0.03, 0.85, 0.07, 'highpass', 7000);
      for (let i = 0; i < 34; i++) { const d = Math.pow(Math.random(), 1.6) * 0.8; au.nz(t + 0.04 + d, 0.012, 0.11 * (1 - d), 'highpass', 5200 + Math.random() * 3000); }
      for (let i = 0; i < 6; i++) { const d = 0.05 + i * 0.06 + Math.random() * 0.03, f = b.note * (2 + i * 0.5); au.osc('sine', f, f * 1.5, t + d, 0.03, 0.09); }
    } },

  heart: { pre: 0.16, rise: 1,
    beat(b, t) { const p = frac(t * 1.15 + b.seed * 0.137), bump = (c0, w0) => Math.exp(-Math.pow((p - c0) / w0, 2)); return { p, v: bump(0.06, 0.045) + bump(0.26, 0.05) * 0.6 }; },
    draw(c, b, t, k) {
      const H = heart(); if (!H) return;
      const r = b.r, bt = this.beat(b, t), sq = k > 0 ? (k < 0.5 ? 1 - k * 0.3 : 0.85 + (k - 0.5) * 0.8) : 1, s = r * (1 + 0.075 * bt.v) * sq;
      c.save(); c.scale(s, s); c.lineJoin = 'round';
      c.strokeStyle = 'rgba(255,90,125,' + (0.12 + 0.1 * bt.v) + ')'; c.lineWidth = 0.2; c.stroke(H);
      const g = c.createRadialGradient(-0.26, -0.32, 0.05, 0, 0, 1.02);
      g.addColorStop(0, 'rgba(255,176,188,.5)'); g.addColorStop(0.5, 'rgba(240,50,88,.55)'); g.addColorStop(0.86, 'rgba(206,18,60,.66)'); g.addColorStop(1, 'rgba(255,150,172,.5)');
      c.fillStyle = g; c.fill(H);
      c.save(); c.clip(H);
      const gl = c.createRadialGradient(0, 0, 0, 0, 0, 0.8); gl.addColorStop(0, 'rgba(255,130,150,' + (0.12 + 0.55 * bt.v) + ')'); gl.addColorStop(1, 'rgba(255,60,90,0)');
      c.fillStyle = gl; c.fillRect(-1, -1, 2, 2);
      const e = frac(bt.p - 0.06), es = 0.16 + 0.8 * e;      // each beat sends a small heart from the middle to the wall
      c.save(); c.scale(es, es); c.strokeStyle = 'rgba(255,210,220,' + (1 - e) * 0.7 + ')'; c.lineWidth = 0.03 / es; c.stroke(H); c.restore();
      c.strokeStyle = 'rgba(255,255,255,.2)'; c.lineWidth = 0.06; c.lineCap = 'round'; c.beginPath(); c.arc(0, -0.05, 0.68, 0.2 * Math.PI, 0.45 * Math.PI); c.stroke();
      c.restore();
      c.strokeStyle = 'rgba(255,200,212,.4)'; c.lineWidth = 0.075; c.stroke(H);
      c.strokeStyle = 'rgba(255,236,241,.55)'; c.lineWidth = 0.022; c.stroke(H);
      c.fillStyle = 'rgba(255,255,255,.2)'; c.beginPath(); c.ellipse(-0.4, -0.4, 0.3, 0.17, -0.62, 0, TAU); c.fill();
      c.fillStyle = 'rgba(255,255,255,.66)'; c.beginPath(); c.ellipse(-0.43, -0.42, 0.18, 0.085, -0.62, 0, TAU); c.fill();
      if (k > 0.5) { c.fillStyle = 'rgba(255,255,255,' + (k - 0.5) * (b.gentle ? 0.3 : 1.2) + ')'; c.fill(H); }
      c.restore();
    },
    burst(w, b) {
      const r = b.r, cols = ['#ff2d55', '#ff5c7a', '#ff8fa3', '#e0113f', '#ffc2cf'];
      P(w, { kind: 'hwave', x: b.x, y: b.y, size: r, grow: 1.1, life: 0.45, color: 'rgba(255,170,190,.9)', vx: 0, vy: 0 });
      for (let i = 0, n = many(b, 30); i < n; i++) { const a = b.rnd() * TAU, v = r * (1.2 + b.rnd() * 4);
        P(w, { kind: 'heart', x: b.x + Math.cos(a) * r * 0.3, y: b.y + Math.sin(a) * r * 0.3, vx: Math.cos(a) * v, vy: Math.sin(a) * v, g: -r * 1.3, drag: 2.1, life: 0.8 + b.rnd() * 0.9, size: r * (0.04 + b.rnd() * b.rnd() * 0.09) + 0.8, color: cols[i % 5], rot: (b.rnd() - 0.5) * 0.8, vr: (b.rnd() - 0.5) * 1.5, swa: r * 0.7, swf: 3 + b.rnd() * 3, sw0: b.rnd() * 6 }); }
      for (let i = 0, n = many(b, 30); i < n; i++) { const a = b.rnd() * TAU, v = r * (2 + b.rnd() * 5); P(w, { kind: 'dot', x: b.x, y: b.y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, g: -r * 0.6, drag: 3.6, life: 0.3 + b.rnd() * 0.4, size: r * (0.01 + b.rnd() * 0.012) + 0.3, color: 'rgba(255,205,215,.9)' }); }
    },
    sfx(b, t, au) {
      for (const [d, gn] of [[0, 0.8], [0.15, 0.6]]) { au.osc('sine', 82, 44, t + d, 0.12, gn); au.osc('triangle', 164, 92, t + d, 0.08, gn * 0.28); }
      const n = b.note; au.osc('sine', n * 2, n * 2, t + 0.3, 0.55, 0.13); au.osc('sine', n * 2.5, n * 2.5, t + 0.38, 0.6, 0.1); au.osc('sine', n * 4, n * 4, t + 0.3, 0.2, 0.04);
    } },

  spiral: { pre: 0.46, rise: 0.95,
    init(b, doc) { const sp = rollSpec(rng(b.seed * 7919 + 13)), size = b.r > 30 ? 112 : 64; b.spec = sp; b.tex = loomTex(sp.L, sp.glow, size, doc); b.tex2 = sp.on2 ? loomTex(sp.L2, 0, size, doc) : null; b.spin = 0.5 + hash(b.seed, 4) * 0.7; },
    draw(c, b, t, k) {
      const r = b.r, sp = b.spec, kk = k * k, ri = r * 0.9, extra = kk * 17;
      const one = (sc, al, lag) => {
        c.save(); c.scale(sc, sc); c.globalAlpha *= al;
        if (b.tex2) { c.save(); c.rotate(sp.L2.dir * (t * b.spin * 0.7 + extra - lag)); c.globalAlpha *= 0.8; c.drawImage(b.tex2, -ri, -ri, 2 * ri, 2 * ri); c.restore(); }
        if (b.tex) { c.save(); c.rotate(sp.L.dir * (t * b.spin + extra - lag)); c.drawImage(b.tex, -ri, -ri, 2 * ri, 2 * ri); c.restore(); }
        c.restore();
      };
      if (k > 0) { one(1 - Math.pow(Math.max(0, k - 0.14), 2) * 0.96, 0.25, 0.5); one(1 - Math.pow(Math.max(0, k - 0.07), 2) * 0.96, 0.4, 0.25); }
      const sc = (1 - kk * 0.96) * (1 + Math.sin(t * 1.7 + b.seed) * 0.02);
      one(sc, 1, 0);
      c.save(); c.scale(sc, sc); shell(c, r, 'soap', 1 - k * 0.7); c.restore();
      if (k > 0.8) { c.fillStyle = 'rgba(255,255,255,' + (k - 0.8) * (b.gentle ? 1.5 : 5) + ')'; c.beginPath(); c.arc(0, 0, r * 0.12, 0, TAU); c.fill(); }
    },
    burst(w, b) {
      const sp = b.spec, cols = sp.L.colors.concat(sp.on2 ? sp.L2.colors : []), arms = Math.max(3, sp.L.arms), dir = sp.L.dir;
      wave(w, b, 'rgba(255,255,255,.9)', 2.1, 0.42);
      const per = Math.ceil(many(b, 126) / arms);
      for (let arm = 0; arm < arms; arm++) for (let j = 0; j < per; j++) { const f = j / per;
        P(w, { kind: 'orbit', cx: b.x, cy: b.y, x: b.x, y: b.y, ang: arm / arms * TAU + f * 2.6 * dir + (b.rnd() - 0.5) * 0.08, rad: b.r * 0.04, vr: b.r * (1.5 + f * 3.4 + b.rnd() * 0.4), w: dir * (8.5 - f * 5.5), drag: 2.4, life: 0.6 + f * 0.5 + b.rnd() * 0.2, size: b.r * (0.034 - f * 0.02) + 0.4, shrink: true, color: hex(cols[arm % cols.length]) }); }
      for (let i = 0, n = many(b, 26); i < n; i++) { const a = b.rnd() * TAU, v = b.r * (3 + b.rnd() * 5); P(w, { kind: 'dot', x: b.x, y: b.y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, g: 0, drag: 5, life: 0.2 + b.rnd() * 0.25, size: b.r * 0.011 + 0.3, color: 'rgba(255,255,255,.9)' }); }
    },
    sfx(b, t, au) {
      const n = b.note, o = au.osc('sine', n * 0.5, n * 4, t, 0.44, 0.16), lfo = au.ac.createOscillator(), lg = au.ac.createGain();
      lfo.frequency.value = 19; lg.gain.value = 30; lfo.connect(lg); lg.connect(o.frequency); lfo.start(t); lfo.stop(t + 0.5);
      au.nz(t, 0.44, 0.1, 'bandpass', 400, 5200, 3);
      const e = au.echo(0.115, 0.46), t2 = t + 0.46; au.osc('sine', n * 4, n * 4, t2, 0.25, 0.22, e); au.osc('triangle', n * 2, n * 2, t2, 0.18, 0.1, e); au.nz(t2, 0.03, 0.12, 'highpass', 4000);
    } },

  marble: { pre: 0.2, rise: 0.55,
    cols: [[255, 45, 142], [138, 92, 255], [34, 211, 238], [57, 255, 157], [255, 217, 77], [255, 106, 47], [255, 255, 255]],
    pal(u) { const p = this.cols, x = frac(u) * p.length, i = Math.floor(x), f = smooth(0, 1, x - i), a = p[i], d = p[(i + 1) % p.length]; return [lerp(a[0], d[0], f), lerp(a[1], d[1], f), lerp(a[2], d[2], f)]; },
    cracks(b) {
      const out = [], stops = [0, 0.3, 0.56, 0.78, 0.97];
      for (let i = 0; i < 9; i++) { const a = i / 9 * TAU + hash(b.seed, i) * 0.5, pts = [];
        for (let s = 0; s < stops.length; s++) { const aj = a + (s ? (hash(b.seed + s, i) - 0.5) * 0.42 * (s < 4 ? 1 : 0.2) : 0); pts.push([Math.cos(aj) * stops[s], Math.sin(aj) * stops[s]]); }
        out.push(pts); }
      return out;
    },
    draw(c, b, t, k) {
      const r = b.r; b.t = t;
      c.save();
      if (k > 0 && !b.gentle) c.translate((hash(b.seed, Math.floor(t * 60)) - 0.5) * r * 0.07, (hash(b.seed + 1, Math.floor(t * 60)) - 0.5) * r * 0.07);
      const tn = this.pal(t * 0.09 + b.seed * 0.013 + 0.5);
      const g = c.createRadialGradient(-r * 0.32, -r * 0.38, r * 0.05, 0, 0, r);
      g.addColorStop(0, 'rgba(255,255,255,.6)'); g.addColorStop(0.45, 'rgba(' + Math.round(lerp(196, tn[0], 0.18)) + ',' + Math.round(lerp(220, tn[1], 0.18)) + ',' + Math.round(lerp(240, tn[2], 0.18)) + ',.4)');
      g.addColorStop(0.9, 'rgba(104,140,186,.62)'); g.addColorStop(1, 'rgba(232,242,255,.85)');
      c.fillStyle = g; c.beginPath(); c.arc(0, 0, r, 0, TAU); c.fill();
      c.save(); c.beginPath(); c.arc(0, 0, r * 0.9, 0, TAU); c.clip();
      c.save(); c.rotate(Math.sin(t * 0.33 + b.seed) * 0.45);   // three vanes round one tumbling axis, the colours travel
      const N = 3, S = r < 30 ? 12 : 22, tw = 2.3 + Math.sin(t * 0.47 + b.seed * 0.3) * 0.9, ph = t * 1.05 + b.seed, items = [];
      const at = (y, j) => { const en = Math.sqrt(Math.max(0, 1 - Math.pow(y / 0.86, 2))), q = y * tw + ph + j * TAU / N, cq = Math.cos(q); return [Math.sin(q) * 0.4 * en, (Math.abs(cq) * 0.16 + 0.02) * en, cq]; };
      for (let j = 0; j < N; j++) { let y0 = -0.84, p0 = at(y0, j);
        for (let i = 1; i <= S; i++) { const y1 = lerp(-0.84, 0.84, i / S), p1 = at(y1, j); items.push([(p0[2] + p1[2]) / 2, p0[0] - p0[1], p0[0] + p0[1], y0, p1[0] - p1[1], p1[0] + p1[1], y1, j]); y0 = y1; p0 = p1; } }
      items.sort((a, d) => a[0] - d[0]);
      c.lineWidth = 0.7;
      for (const it of items) {
        const col = this.pal(t * 0.09 + it[7] / N * 0.55 + (it[3] + it[6]) * 0.08 + b.seed * 0.013), sh = 0.5 + 0.5 * (it[0] * 0.5 + 0.5);
        const st = 'rgba(' + Math.round(col[0] * sh) + ',' + Math.round(col[1] * sh) + ',' + Math.round(col[2] * sh) + ',.9)';
        c.fillStyle = st; c.strokeStyle = st; c.beginPath(); c.moveTo(it[1] * r, it[3] * r); c.lineTo(it[2] * r, it[3] * r); c.lineTo(it[5] * r, it[6] * r); c.lineTo(it[4] * r, it[6] * r); c.closePath(); c.fill(); c.stroke();
      }
      c.restore();
      c.strokeStyle = 'rgba(30,58,100,.34)'; c.lineWidth = r * 0.12; c.beginPath(); c.arc(0, 0, r * 0.84, 0.5 * Math.PI, 1.1 * Math.PI); c.stroke();
      const ca = c.createRadialGradient(r * 0.42, r * 0.46, 0, r * 0.42, r * 0.46, r * 0.42); ca.addColorStop(0, 'rgba(255,255,255,.42)'); ca.addColorStop(1, 'rgba(255,255,255,0)');
      c.fillStyle = ca; c.fillRect(-r, -r, 2 * r, 2 * r);
      c.restore();
      c.strokeStyle = 'rgba(255,255,255,.8)'; c.lineWidth = r * 0.05; c.beginPath(); c.arc(0, 0, r * 0.975, 0, TAU); c.stroke();
      c.strokeStyle = 'rgba(30,50,80,.35)'; c.lineWidth = 1; c.beginPath(); c.arc(0, 0, r, 0, TAU); c.stroke();
      gloss(c, r, 0.95);
      c.fillStyle = 'rgba(255,255,255,.7)'; c.beginPath(); c.arc(-r * 0.2, -r * 0.72, r * 0.035, 0, TAU); c.fill();
      if (k > 0) {
        c.strokeStyle = 'rgba(255,255,255,.95)'; c.lineWidth = Math.max(0.8, r * 0.022); c.lineJoin = 'round';
        const reach = Math.min(1, k * 1.5) * 4;
        for (const pts of this.cracks(b)) { c.beginPath(); c.moveTo(0, 0); for (let s = 1; s <= 4; s++) { const f = clamp(reach - (s - 1), 0, 1); if (f <= 0) break; c.lineTo(lerp(pts[s - 1][0], pts[s][0], f) * r, lerp(pts[s - 1][1], pts[s][1], f) * r); } c.stroke(); }
        if (k > 0.75) { c.fillStyle = 'rgba(255,255,255,' + (k - 0.75) * (b.gentle ? 0.6 : 2.4) + ')'; c.beginPath(); c.arc(0, 0, r, 0, TAU); c.fill(); }   // Gentle: a slow glow, never a white flash
      }
      c.restore();
    },
    burst(w, b) {
      const r = b.r, lines = this.cracks(b), n = lines.length, t = b.t || 0;
      wave(w, b, 'rgba(255,255,255,.85)', 1.8, 0.3);
      for (let i = 0; i < n; i++) {
        const a = lines[i], d = lines[(i + 1) % n];
        for (let s = 0; s < 4; s++) {
          const pc = s === 0 ? [a[0], a[1], d[1]] : [a[s], a[s + 1], d[s + 1], d[s]];
          let cx = 0, cy = 0; pc.forEach((q) => { cx += q[0]; cy += q[1]; }); cx /= pc.length; cy /= pc.length;
          const len = Math.hypot(cx, cy) || 1, v = r * (0.9 + s * 0.7 + b.rnd() * 1.3), col = this.pal(t * 0.09 + b.rnd() * 0.5 + b.seed * 0.013);
          P(w, { kind: 'shard', x: b.x + cx * r, y: b.y + cy * r, pts: pc.map((q) => [(q[0] - cx) * r, (q[1] - cy) * r]), vx: cx / len * v, vy: cy / len * v - r * 1.2, g: r * 11, drag: 0.5, life: 0.9 + b.rnd() * 0.7, vr: (b.rnd() - 0.5) * 14,
            color: s < 3 && b.rnd() < 0.4 ? 'rgba(' + Math.round(col[0]) + ',' + Math.round(col[1]) + ',' + Math.round(col[2]) + ',.72)' : 'rgba(205,228,250,.42)' });
        }
      }
      for (let i = 0, m = many(b, 54); i < m; i++) { const an = b.rnd() * TAU, v = r * (1.5 + b.rnd() * 6); P(w, { kind: 'dot', x: b.x + Math.cos(an) * r * 0.4, y: b.y + Math.sin(an) * r * 0.4, vx: Math.cos(an) * v, vy: Math.sin(an) * v - r, g: r * 9, drag: 1.6, life: 0.4 + b.rnd() * 0.6, size: r * (0.008 + b.rnd() * 0.014) + 0.3, color: 'rgba(255,255,255,.95)' }); }
      for (let i = 0, m = many(b, 10); i < m; i++) { const an = b.rnd() * TAU, v = r * (1 + b.rnd() * 3); P(w, { kind: 'star', x: b.x + Math.cos(an) * r * 0.5, y: b.y + Math.sin(an) * r * 0.5, vx: Math.cos(an) * v, vy: Math.sin(an) * v, g: r * 5, drag: 1.5, life: 0.4 + b.rnd() * 0.5, size: r * (0.06 + b.rnd() * 0.06), tw: 14 + b.rnd() * 10, color: 'rgba(255,255,255,.95)' }); }
    },
    sfx(b, t, au) {
      au.nz(t, 0.01, 0.25, 'highpass', 3500); au.osc('square', 240, 150, t, 0.04, 0.1); au.nz(t + 0.09, 0.008, 0.14, 'highpass', 4200);
      const t2 = t + 0.2; au.nz(t2, 0.28, 0.32, 'highpass', 2600);
      for (let i = 0; i < 7; i++) { const f = 2100 + Math.random() * 5200; au.osc('sine', f, f * 0.985, t2, 0.06 + Math.random() * 0.25, 0.09); }
      for (let i = 0; i < 12; i++) { const f = 3000 + Math.random() * 5500; au.osc('sine', f, f, t2 + 0.12 + Math.random() * 0.6, 0.05, 0.045); }
    } },

  disco: { pre: 0.09, rise: 0.9,
    draw(c, b, t, k) {
      const r = b.r, R = r * 0.98, rot = t * 0.55 + b.seed;
      for (let m = 0; m < 8; m++) { const an = t * 0.5 * (m % 2 ? 1 : -1) + m * 0.8, rr = r * (1.1 + 0.14 * hash(b.seed, m)), al = 0.16 + 0.12 * Math.sin(t * 2 + m);
        c.fillStyle = m % 2 ? 'rgba(255,90,170,' + al + ')' : 'rgba(255,255,255,' + al + ')'; c.beginPath(); c.arc(Math.cos(an) * rr, Math.sin(an) * rr, r * 0.028 + 0.3, 0, TAU); c.fill(); }
      c.save(); c.rotate(-0.2);
      const g = c.createRadialGradient(-r * 0.3, -r * 0.35, r * 0.05, 0, 0, r); g.addColorStop(0, '#3a3644'); g.addColorStop(1, '#09080d');
      c.fillStyle = g; c.beginPath(); c.arc(0, 0, r, 0, TAU); c.fill();
      c.save(); c.beginPath(); c.arc(0, 0, R, 0, TAU); c.clip();
      const rows = r < 30 ? 9 : 13, cols = r < 30 ? 18 : 28, dph = Math.PI / rows, dth = TAU / cols, gp = 0.08, glints = [];   // every facet is a real patch of the sphere
      const l1 = [Math.cos(t * 0.9 + b.seed) * 0.62, Math.sin(t * 0.7) * 0.45 - 0.3, 0.68], l2 = [-Math.cos(t * 0.6 + 2 + b.seed) * 0.6, Math.sin(t * 0.8 + 1) * 0.5, 0.62];
      for (let i = 0; i < rows; i++) {
        const pa = -Math.PI / 2 + (i + gp) * dph, pb = -Math.PI / 2 + (i + 1 - gp) * dph, pm = (pa + pb) / 2;
        const ya = Math.sin(pa) * R, yb = Math.sin(pb) * R, ca = Math.cos(pa) * R, cb = Math.cos(pb) * R, cm = Math.cos(pm), sm = Math.sin(pm);
        for (let j = 0; j < cols; j++) {
          const th = j * dth + rot + (i % 2) * dth * 0.5, ta = th + dth * gp, tb = th + dth * (1 - gp), tm = (ta + tb) / 2, cz = Math.cos(tm);
          if (cz <= 0) continue;
          const sa = Math.cos(ta) < 0 ? Math.sign(Math.sin(ta)) : Math.sin(ta), sb = Math.cos(tb) < 0 ? Math.sign(Math.sin(tb)) : Math.sin(tb);
          const h = hash(i * 53 + (b.seed % 89), j), nx = Math.sin(tm) * cm + (h - 0.5) * 0.22, ny = sm + (hash(j + 7, i) - 0.5) * 0.22, nzz = cz * cm;
          const lam = Math.max(0, -0.45 * nx - 0.55 * ny + 0.7 * nzz);
          const s1 = Math.pow(Math.max(0, nx * l1[0] + ny * l1[1] + nzz * l1[2]), 18), s2 = Math.pow(Math.max(0, nx * l2[0] + ny * l2[1] + nzz * l2[2]), 14);
          c.fillStyle = 'rgb(' + Math.min(255, Math.round(26 + 70 * lam + 255 * s1 + 255 * s2)) + ',' + Math.min(255, Math.round(26 + 70 * lam + 255 * s1 + 70 * s2)) + ',' + Math.min(255, Math.round(34 + 82 * lam + 255 * s1 + 170 * s2)) + ')';
          c.beginPath(); c.moveTo(sa * ca, ya); c.lineTo(sb * ca, ya); c.lineTo(sb * cb, yb); c.lineTo(sa * cb, yb); c.closePath(); c.fill();
          if (s1 + s2 > 0.8) glints.push([(sa + sb) / 2 * cm * R, (ya + yb) / 2, s1 > s2, s1 + s2]);
        }
      }
      const vg = c.createRadialGradient(0, 0, R * 0.5, 0, 0, R); vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(0,0,0,.55)');
      c.fillStyle = vg; c.fillRect(-R, -R, 2 * R, 2 * R);
      c.restore();
      c.strokeStyle = 'rgba(255,255,255,.22)'; c.lineWidth = 1; c.beginPath(); c.arc(0, 0, R, 0, TAU); c.stroke();
      glints.sort((a, d) => d[3] - a[3]); const lit = [];
      for (const q of glints) { if (lit.length >= 2) break; if (lit.some((o) => o[2] === q[2] || Math.hypot(o[0] - q[0], o[1] - q[1]) < r * 0.5)) continue; lit.push(q); }
      for (const q of lit) star4(c, q[0], q[1], r * 0.17 * Math.min(1, q[3]), q[2] ? 'rgba(255,255,255,.95)' : 'rgba(255,150,205,.95)');
      c.restore();
      if (k > 0) { c.fillStyle = 'rgba(255,255,255,' + k * (b.gentle ? 0.25 : 0.9) + ')'; c.beginPath(); c.arc(0, 0, r, 0, TAU); c.fill(); }
    },
    burst(w, b) {
      const r = b.r;
      wave(w, b, 'rgba(255,255,255,.8)', 2, 0.32);
      for (let i = 0; i < 12; i++) P(w, { kind: 'beam', x: b.x, y: b.y, rot: i / 12 * TAU + b.rnd(), vr0: 0.8, wd: 0.028, size: r * 2.8, life: 0.5, color: i % 2 ? 'rgba(255,90,170,.9)' : 'rgba(255,255,255,.9)', vx: 0, vy: 0 });
      for (let i = 0, n = many(b, 96); i < n; i++) { const an = b.rnd() * TAU, v = r * (1.5 + b.rnd() * 5.5), d = b.rnd() * r * 0.8;
        P(w, { kind: 'square', x: b.x + Math.cos(an) * d, y: b.y + Math.sin(an) * d, vx: Math.cos(an) * v, vy: Math.sin(an) * v - r, g: r * 5, drag: 1.9, life: 0.7 + b.rnd() * 0.8, size: r * (0.028 + b.rnd() * 0.035) + 0.5, rot: b.rnd() * 6, vr: (b.rnd() - 0.5) * 18, color: i % 4 ? '#b9b6c9' : '#ff4fa8' }); }
      for (let i = 0, n = many(b, 16); i < n; i++) { const an = b.rnd() * TAU, v = r * (1 + b.rnd() * 4); P(w, { kind: 'star', x: b.x + Math.cos(an) * r * 0.6, y: b.y + Math.sin(an) * r * 0.6, vx: Math.cos(an) * v, vy: Math.sin(an) * v, g: r * 2, drag: 2, life: 0.4 + b.rnd() * 0.6, size: r * (0.07 + b.rnd() * 0.08), tw: 12 + b.rnd() * 12, color: i % 3 ? 'rgba(255,255,255,.95)' : 'rgba(255,150,205,.95)' }); }
    },
    sfx(b, t, au) {
      for (let i = 0; i < 3; i++) au.nz(t + i * 0.012, 0.02, 0.3, 'bandpass', 1500, 0, 1.4); au.nz(t + 0.036, 0.16, 0.22, 'bandpass', 1300, 0, 0.9);
      const f = au.ac.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = 3200; f.connect(au.out);
      [1, 1.25, 1.5, 2].forEach((m, i) => au.osc('sawtooth', b.note * m, b.note * m, t + 0.04 + i * 0.055, 0.09, 0.1, f));
      au.nz(t + 0.05, 0.4, 0.05, 'highpass', 8000);
    } },

  frost: { pre: 0.1, rise: 0.8,
    draw(c, b, t, k) {
      const r = b.r, g = clamp(b.age / 4.5, 0.12, 1);
      c.save(); c.beginPath(); c.arc(0, 0, r * 0.93, 0, TAU); c.clip();
      c.fillStyle = 'rgba(190,228,255,' + (0.05 + 0.1 * g) + ')'; c.fillRect(-r, -r, 2 * r, 2 * r);
      c.fillStyle = 'rgba(240,250,255,.8)';
      for (let i = 0; i < 70; i++) { const h = hash(b.seed, i); if (h > g) continue; const an = hash(b.seed + 2, i) * TAU, rr = r * (0.66 + hash(b.seed + 4, i) * 0.26), s = r * 0.016 * (1 + hash(b.seed + 6, i)) + 0.4; c.fillRect(Math.cos(an) * rr, Math.sin(an) * rr, s, s); }
      c.restore();
      c.save(); c.rotate(t * 0.12 + b.seed); c.strokeStyle = 'rgba(236,248,255,.94)'; c.lineWidth = Math.max(1, r * 0.034); c.lineCap = 'round';
      const L = r * 0.62 * g;
      for (let a = 0; a < 6; a++) { c.rotate(TAU / 6); c.beginPath(); c.moveTo(0, 0); c.lineTo(0, -L);
        for (const f of [0.38, 0.68]) { if (g <= f) continue; const bl = r * 0.2 * (1 - f * 0.45) * Math.min(1, (g - f) * 3); c.moveTo(0, -r * 0.62 * f); c.lineTo(bl * 0.8, -r * 0.62 * f - bl * 0.6); c.moveTo(0, -r * 0.62 * f); c.lineTo(-bl * 0.8, -r * 0.62 * f - bl * 0.6); }
        c.stroke(); }
      c.restore();
      shell(c, r, 'ice');
      if (k > 0) { c.strokeStyle = 'rgba(255,255,255,' + k + ')'; c.lineWidth = r * 0.03; c.beginPath(); for (let a = 0; a < 5; a++) { const an = a / 5 * TAU + b.seed; c.moveTo(0, 0); c.lineTo(Math.cos(an) * r * 0.9 * k, Math.sin(an) * r * 0.9 * k); } c.stroke(); }
    },
    burst(w, b) {
      const r = b.r;
      P(w, { kind: 'puff', x: b.x, y: b.y, size: r * 0.7, grow: 1.3, life: 0.7, color: 'rgba(200,232,255,.8)', vx: 0, vy: 0 });
      for (let a = 0; a < 6; a++) { const an = a / 6 * TAU + b.seed, v = r * (1.2 + b.rnd()); P(w, { kind: 'stick', x: b.x + Math.cos(an) * r * 0.3, y: b.y + Math.sin(an) * r * 0.3, vx: Math.cos(an) * v, vy: Math.sin(an) * v, g: r * 6, drag: 1.2, life: 0.9 + b.rnd() * 0.4, size: r * 0.5, lw: Math.max(1, r * 0.034), rot: an, vr: (b.rnd() - 0.5) * 9, color: 'rgba(236,248,255,.95)' }); }
      for (let i = 0, n = many(b, 40); i < n; i++) { const an = b.rnd() * TAU, v = r * (1 + b.rnd() * 4); P(w, { kind: 'dot', x: b.x + Math.cos(an) * r * 0.5, y: b.y + Math.sin(an) * r * 0.5, vx: Math.cos(an) * v, vy: Math.sin(an) * v, g: r * 1.2, drag: 3.4, life: 0.7 + b.rnd() * 1.1, size: r * (0.008 + b.rnd() * 0.012) + 0.3, color: 'rgba(240,250,255,.9)', swa: r * 0.6, swf: 2 + b.rnd() * 3, sw0: b.rnd() * 6 }); }
      for (let i = 0, n = many(b, 64); i < n; i++) { const an = b.rnd() * TAU, v = r * (0.8 + b.rnd() * 3); P(w, { kind: 'flake', x: b.x + Math.cos(an) * r * 0.5, y: b.y + Math.sin(an) * r * 0.5, vx: Math.cos(an) * v, vy: Math.sin(an) * v, g: r * 1.6, drag: 3, life: 1.1 + b.rnd() * 1, size: r * (0.014 + b.rnd() * 0.024) + 0.4, rot: b.rnd() * 3, vr: (b.rnd() - 0.5) * 3, color: 'rgba(240,250,255,.95)', swa: r * 0.8, swf: 2 + b.rnd() * 3, sw0: b.rnd() * 6 }); }
    },
    sfx(b, t, au) {
      for (let i = 0; i < 16; i++) au.nz(t + Math.random() * 0.22, 0.008, 0.2, 'bandpass', 3200 + Math.random() * 3500, 0, 4);
      au.osc('sine', b.note * 4, b.note * 4, t + 0.1, 0.5, 0.06); au.osc('sine', b.note * 6.02, b.note * 6.02, t + 0.1, 0.3, 0.03);
      au.nz(t + 0.1, 0.55, 0.12, 'lowpass', 1500, 300);
    } },
};
/** A look that is not out yet (Bubblegum, Latex, Oil slick): a dashed outline in its colour, a soap pop. */
const TODO = { gum: '#ff5fa8', latex: '#4a4458', oil: '#6b5bd6', diamond: '#e8f4ff' };
const todo = (col) => ({ pre: 0, rise: 1, burst: soapBurst, sfx: soapSfx,
  draw(c, b) { const r = b.r; c.save(); c.beginPath(); c.arc(0, 0, r, 0, TAU); c.fillStyle = col; c.globalAlpha = 0.4; c.fill(); c.globalAlpha = 1;
    c.setLineDash([4, 4]); c.lineWidth = 1.5; c.strokeStyle = '#fffc'; c.stroke(); c.setLineDash([]);
    c.beginPath(); c.arc(-r * 0.35, -r * 0.35, r * 0.16, 0, TAU); c.fillStyle = '#fffb'; c.fill(); c.restore(); } });
export const skinOf = (kind, colour) => SKINS[kind] || todo(typeof colour === 'string' ? colour : TODO[kind] || '#ffffff55');

let SEED = 1;
export function makeBubble(kind, x, y, r, { doc = globalThis.document, colour, gentle = false } = {}) {
  const seed = (SEED++ * 2654435761) >>> 0, skin = skinOf(kind, colour);
  const b = { kind, skin, x, y, r, seed, rnd: rng(seed), age: 0, grow: 0, state: 'alive', pt: 0, gentle, note: NOTES[seed % 5] / (r > 46 ? 2 : 1),
    vy: 22 + hash(seed, 1) * 20, wf: 0.6 + hash(seed, 2) * 0.9, w0: hash(seed, 3) * 6, wa: 8 + hash(seed, 5) * 12 };
  if (skin.init && doc) skin.init(b, doc);
  return b;
}
/** Starts the skin's own pop; play(skin, b) sounds it (only ever from a click). False when it cannot pop yet. */
export function popBubble(b, play) {
  if (b.state !== 'alive' || b.grow < 0.6) return false;
  b.state = 'pop'; b.pt = 0;
  if (play) play(b);
  return true;
}
export function stepBubble(w, b, dt) {
  b.age += dt; b.grow = Math.min(1, b.grow + dt * 2.6);
  if (b.state === 'pop') { b.pt += dt; if (b.pt >= b.skin.pre) { b.skin.burst(w, b); b.state = 'dead'; } }
}
export function drawBubble(w, b, t) {
  const c = w.c, k = b.state === 'pop' ? clamp(b.pt / (b.skin.pre || 1), 0, 1) : 0;
  c.save(); c.translate(b.x, b.y); const gs = backOut(b.grow); c.scale(gs, gs);
  b.skin.draw(c, b, t + (b.seed % 50), k);
  c.restore();
}
