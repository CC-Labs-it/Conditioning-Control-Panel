/* ui/unlocks/themes-b/emi-kit.js - EMI Takeover shared parts: the sprite (body, screen, face, picture slot), the
 * baked faces, the picture pool, bubbles. Used by the run-up swarm (emi-swarm.js) and the ending (emi-takeover.js). */
import { createFace } from '../../../../arcademy/emi/face.js';
import { FACE_CSS } from '../extras/emi-face.js';
import { EMI_BARKS } from '../../../data/content/emi-takeover.js';
import { LITE, frameRect } from '../../perf.js';
import { shopDoor } from '../../shop.js';
import * as T from '../../../sim/takeover.js';

export const art = (name) => `assets/img/emi/${name}.png`;
export const SWAY = ['body-sway1', 'body-sway2', 'body-sway3', 'body-sway4'];
export const IDLE_FACES = ['0_0', '0_0', '^_^', 'o_o', '^_~'];
export const BAKED = ['0_0', '^_^', 'o_o', '-_-', '^_~', '>_<'];
export const PINK = '#FF69B4';
export const rand = (lo, hi) => lo + Math.random() * (hi - lo);
export const any = (list) => list[Math.floor(Math.random() * list.length)];

export const CSS = `
${FACE_CSS}
.et-box{position:absolute;inset:0;pointer-events:none;}
.et-emi{position:absolute;left:0;top:0;pointer-events:none;will-change:transform;}
.et-emi img{position:absolute;display:block;pointer-events:none;-webkit-user-drag:none;user-select:none;}
.et-b{inset:0;width:100%;height:100%;}
.et-s{position:absolute;left:34.46%;top:29.46%;width:41.68%;height:37.63%;overflow:hidden;border-radius:2px;}
.et-s>img{inset:0;width:100%;height:100%;}
.et-f{image-rendering:pixelated;}
.et-p{object-fit:cover;visibility:hidden;}
.et-s[data-pic] .et-p{visibility:visible;}
.et-s[data-pic] .et-f{visibility:hidden;}
.et-s[data-off] .et-f{visibility:hidden;}
.et-credit{position:absolute;left:50%;top:100%;transform:translateX(-50%);font:10px var(--font);color:#fff;text-shadow:0 0 2px #000,0 0 2px #000;white-space:nowrap;}
.et-bub{position:absolute;left:0;top:0;z-index:4;width:max-content;max-width:min(220px,64vw);padding:5px 9px;background:#fff;color:#111;border:1px solid #333;border-radius:9px;font:600 14px/1.2 var(--font);box-shadow:1px 2px 0 rgba(0,0,0,.25);pointer-events:none;}
.row.et-guest{cursor:default;}
.row.et-guest .et-who{font-size:11px;line-height:1;color:var(--ink-soft);}
.row.et-guest .cost{font-size:12px;}
.row.et-guest.et-own .et-who{font-weight:700;color:var(--stamp);}
.et-err{position:absolute;left:50%;top:0;transform:translateX(-50%);width:min(560px,calc(100% - 24px));color:#fff4df;font:14px/1.45 Consolas,"Cascadia Mono",Menlo,"DejaVu Sans Mono","Courier New",monospace;}
.et-err .eh{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:6px;}
.et-err .eh img{position:static;height:40px;width:auto;}
.et-err .eh b{padding:2px 10px;background:#16110d;color:#ff8a8f;letter-spacing:2px;font-size:18px;}
.et-err .ln{min-height:1.45em;white-space:pre-wrap;}
.et-err .ln span{background:#16110d;padding:1px 6px;box-decoration-break:clone;-webkit-box-decoration-break:clone;}
@media (max-width:520px){.et-err{font-size:12.5px;}.et-err .eh img{height:32px;}}
`;

/* ---- the art: pose pictures kept as blob urls (a pose swap never refetches), faces painted once and reused ---- */

export const poses = new Map();
export const pose = (name) => {
  if (!poses.has(name)) {
    poses.set(name, art(name));
    fetch(art(name)).then((r) => (r.ok ? r.blob() : null)).then((b) => { if (b) poses.set(name, URL.createObjectURL(b)); }).catch(() => {});
  }
  return poses.get(name);
};

/** The face she ends on: (● ◡ ●), drawn as shapes in her own pink so it never depends on a font. */
export function uncannyUrl(doc) {
  const c = doc.createElement('canvas');
  c.width = 152; c.height = 137;
  const g = c.getContext('2d');
  g.strokeStyle = g.fillStyle = PINK;
  g.lineWidth = 7; g.lineCap = 'round';
  for (const x of [50, 102]) { g.beginPath(); g.arc(x, 62, 15, 0, Math.PI * 2); g.fill(); }
  g.beginPath(); g.arc(76, 64, 16, Math.PI * 0.2, Math.PI * 0.8); g.stroke();
  g.beginPath(); g.arc(77, 68, 66, Math.PI - 0.42, Math.PI + 0.42); g.stroke();
  g.beginPath(); g.arc(75, 68, 66, -0.42, 0.42); g.stroke();
  return c.toDataURL('image/png');
}

export const faces = {};
export const sprites = new Set();
let facesAsked = false;
export function bakeFaces(doc) {
  if (facesAsked) return;
  facesAsked = true;
  const canvas = doc.createElement('canvas');
  const painter = createFace(canvas);
  const paint = () => {
    for (const f of BAKED) { painter.draw(f); faces[f] = canvas.toDataURL('image/png'); }
    faces.uncanny ||= uncannyUrl(doc);
    for (const s of sprites) { if (!s.el.isConnected) { sprites.delete(s); continue; } const f = s.face; s.face = ''; s.setFace(f); }
  };
  paint();
  painter.ready.then(paint);   // the bundled face font may land after the first paint
}

/** One EMI: the body picture, her screen with a face on it, and a picture slot over the face. */
export function sprite(doc, size, crisp = false) {
  const el = doc.createElement('div');
  el.className = 'et-emi';
  el.style.width = el.style.height = `${size}px`;
  const body = doc.createElement('img');
  body.className = 'et-b'; body.alt = ''; body.draggable = false;
  if (crisp) body.style.imageRendering = 'pixelated';
  body.src = pose('body-idle');
  const scr = doc.createElement('div');
  scr.className = 'et-s';
  const faceImg = doc.createElement('img');
  faceImg.className = 'et-f'; faceImg.alt = '';
  const picImg = doc.createElement('img');
  picImg.className = 'et-p'; picImg.alt = ''; picImg.referrerPolicy = 'strict-origin'; picImg.decoding = 'async';
  scr.append(faceImg, picImg);
  el.append(body, scr);
  const s = {
    el, body, scr, face: '', poseName: 'body-idle', pic: null,
    setFace(name) { if (name === s.face || !faces[name]) return; s.face = name; faceImg.src = faces[name]; },
    setPose(name) { if (name === s.poseName) return; s.poseName = name; body.src = pose(name); },
    /** A picture url, a plain colour ('#...'), or null for the face. */
    show(what) {
      if (what === s.pic) return;
      s.pic = what;
      if (!what) { delete scr.dataset.pic; scr.style.background = ''; return; }
      scr.dataset.pic = '1';
      if (what[0] === '#') { scr.style.background = what; picImg.style.display = 'none'; return; }
      scr.style.background = '';
      picImg.style.display = '';
      if (picImg.getAttribute('src') !== what) picImg.src = what;
    },
    /** The screen switched off: no face, no picture. */
    dark(on) { if (on) { s.show(null); scr.dataset.off = '1'; } else delete scr.dataset.off; },
  };
  sprites.add(s);
  s.setFace('0_0');
  return s;
}

export function controlsRect(doc, pad = 10) {
  const r = doc.getElementById('game-controls')?.getBoundingClientRect();
  return r && r.width ? { x: r.left - pad, y: r.top - pad, w: r.width + pad * 2, h: r.height + pad * 2 } : null;
}

/** The player's pictures for EMI screens: a small pool of stills. Empty means plain colour tiles. */
export async function morePictures(ctx, pool, max) {
  if (ctx.pick('f3') === 'blind') return;
  for (let i = pool.length; i < max; i++) {
    let p = null;
    try { p = await ctx.pictures.next({ niche: 'goon', still: true, allowScrolller: true }); } catch { p = null; }
    if (!p?.url || pool.some((q) => q.url === p.url)) break;
    pool.push({ url: p.url, credit: p.credit || '' });
  }
}
export const tilesOf = (ctx) => { const p = ctx.palette(); return [...new Set([p.a, p.b, '#f2a33c', '#fff4df'].filter((c) => /^#/.test(c || '')))]; };

/** A bubble over an EMI. Text only: nothing here is ever spoken. */
export function bubble(ctx, parent, text) {
  const el = parent.ownerDocument.createElement('div');
  el.className = 'et-bub';
  el.textContent = text;
  parent.appendChild(el);
  ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 250, fill: 'both' });
  return { el, w: el.offsetWidth, h: el.offsetHeight, until: 0 };
}
export function placeBubble(b, x, y, size, W) {
  const bx = Math.max(4, Math.min(W - b.w - 4, x + size / 2 - b.w / 2));
  const by = y - b.h - 6 < 4 ? y + size + 6 : y - b.h - 6;
  b.el.style.transform = `translate(${bx.toFixed(1)}px,${by.toFixed(1)}px)`;
}
