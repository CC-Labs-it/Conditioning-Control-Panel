/* render/faces.js - EMI's screen faces (three-free, so node tests draw them on a mock context).
 *
 * The 16 ids match Astra's wave 3 atlas (BRIEF-POLISH2 "Faces"). Until `emi-faces` is in the manifest
 * each face is drawn procedurally: soft shapes on a 32 x 32 grid, scaled up nearest-neighbour onto the
 * 128 px screen canvas with a glow and faint scanlines (pixel-soft), readable on a kart far away. With
 * the atlas, karts.js picks the cell faceCell(id) instead (4 x 4, ids in this order, row 0 on top).
 * The screen redraws only when the face, the blink or the colour changes.
 */

export const FACE_IDS = Object.freeze(['neutral', 'happy', 'grin', 'wink', 'determined', 'drift-focus', 'dizzy', 'sleepy',
  'frozen', 'heart', 'x-eyes', 'smug', 'sad', 'sparkle', 'cheer', 'blank']);

// feel wave 2026-10-26 (lane C): rival glance / grin on overtake, 0.8 s, one change per racer per 2 s (render/worldFeel.js)
export { createFaceGlance } from './worldFeel.js';

/** Atlas cell of a face id (unknown ids show neutral): { col, row } on a 4 x 4 grid. */
export function faceCell(id) {
  const i = Math.max(0, FACE_IDS.indexOf(id));
  return { col: i % 4, row: (i / 4) | 0 };
}

const EL = 10, ER = 22, EY = 13.5, MY = 20;       // eye centres and mouth line on the 32 grid (as the old face)
const ICE = '#bfefff', TEAR = '#8fd8ff';

const oval = (g, x, y, rx, ry) => { g.beginPath(); g.ellipse(x, y, rx, ry, 0, 0, Math.PI * 2); g.fill(); };
const line = (g, pts) => { g.beginPath(); g.moveTo(pts[0], pts[1]); for (let k = 2; k < pts.length; k += 2) g.lineTo(pts[k], pts[k + 1]); g.stroke(); };
const arc = (g, x, y, r, a0, a1) => { g.beginPath(); g.arc(x, y, r, a0 * Math.PI, a1 * Math.PI); g.stroke(); };
const eyes = (g, fn) => { fn(EL, -1); fn(ER, 1); };
const openEye = (g, x) => oval(g, x, EY, 2.3, 3.3);
const shutEye = (g, x) => line(g, [x - 3, EY, x + 3, EY]);
const archEye = (g, x) => arc(g, x, EY + 2, 3, 1.15, 1.85);   // ^ (pleased, closed)
const smile = (g, r = 4) => arc(g, 16, MY - r * 0.55, r, 0.15, 0.85);
const frown = (g) => arc(g, 16, MY + 3, 3.5, 1.2, 1.8);
const openMouth = (g, r = 4.5) => { g.beginPath(); g.arc(16, MY - 1, r, 0, Math.PI); g.closePath(); g.fill(); };
const brows = (g, inner, outer) => eyes(g, (x, s) => line(g, [x + 3 * s, EY - 5 + outer, x - 2.5 * s, EY - 5 + inner]));

function star(g, x, y, ro, ri) {
  g.beginPath();
  for (let k = 0; k < 8; k++) {
    const a = (k / 8) * Math.PI * 2 - Math.PI / 2, r = k & 1 ? ri : ro;
    g[k ? 'lineTo' : 'moveTo'](x + Math.cos(a) * r, y + Math.sin(a) * r);
  }
  g.closePath(); g.fill();
}

function heart(g, x, y) {
  g.beginPath(); g.arc(x - 1.4, y - 0.8, 1.7, 0, Math.PI * 2); g.arc(x + 1.4, y - 0.8, 1.7, 0, Math.PI * 2); g.fill();
  g.beginPath(); g.moveTo(x - 3.05, y - 0.2); g.lineTo(x + 3.05, y - 0.2); g.lineTo(x, y + 3.4); g.closePath(); g.fill();
}

function spiral(g, x, y, turn) {
  g.beginPath();
  for (let a = 0; a <= Math.PI * 2.8; a += 0.25) {
    const r = 0.5 + a * 0.42, b = a + turn;
    g[a ? 'lineTo' : 'moveTo'](x + Math.cos(b) * r, y + Math.sin(b) * r);
  }
  g.save(); g.lineWidth = 1.2; g.stroke(); g.restore();
}

// one painter per id; g strokes and fills in fg already. opts: { blink, cut(fn), t }
const PAINT = {
  neutral: (g, o) => { eyes(g, o.blink ? (x) => shutEye(g, x) : (x) => openEye(g, x)); smile(g); },
  happy: (g) => { eyes(g, (x) => archEye(g, x)); smile(g, 5); },
  grin: (g, o) => { eyes(g, o.blink ? (x) => shutEye(g, x) : (x) => openEye(g, x)); openMouth(g); },
  wink: (g) => { openEye(g, EL); archEye(g, ER); smile(g, 4.5); },
  determined: (g, o) => { eyes(g, o.blink ? (x) => shutEye(g, x) : (x) => oval(g, x, EY + 0.5, 2.2, 2.6)); brows(g, 1.8, -0.6); line(g, [12.5, MY + 1, 19.5, MY + 1]); },
  'drift-focus': (g, o) => { eyes(g, o.blink ? (x) => shutEye(g, x) : (x) => oval(g, x, EY + 0.6, 2.8, 1.4)); brows(g, 2.2, 0); line(g, [13, MY + 1.5, 17, MY + 1, 19.5, MY - 0.2]); },
  dizzy: (g, o) => { eyes(g, (x, s) => spiral(g, x, EY, s * (o.t || 0) * 3)); line(g, [11.5, MY + 1, 13.5, MY - 0.5, 15.5, MY + 1, 17.5, MY - 0.5, 19.5, MY + 1, 21, MY]); },
  sleepy: (g) => { eyes(g, (x) => arc(g, x, EY - 1.5, 3, 0.15, 0.85)); g.beginPath(); g.ellipse(16, MY + 1, 1.6, 1.9, 0, 0, Math.PI * 2); g.stroke(); },
  frozen: (g) => {
    g.strokeStyle = ICE; g.fillStyle = ICE;
    eyes(g, (x) => g.strokeRect(x - 2.4, EY - 2.6, 4.8, 5.2));
    line(g, [11, MY, 12.7, MY + 1.6, 14.4, MY, 16, MY + 1.6, 17.6, MY, 19.3, MY + 1.6, 21, MY]);
    for (const [x, y] of [[3, 4], [5, 3], [28, 27], [26, 28], [28, 4]]) g.fillRect(x, y, 1.2, 1.2);
  },
  heart: (g) => { eyes(g, (x) => heart(g, x, EY)); smile(g, 4.5); },
  'x-eyes': (g) => { eyes(g, (x) => { line(g, [x - 2.4, EY - 2.4, x + 2.4, EY + 2.4]); line(g, [x - 2.4, EY + 2.4, x + 2.4, EY - 2.4]); }); g.beginPath(); g.ellipse(16, MY + 1, 1.8, 2.2, 0, 0, Math.PI * 2); g.stroke(); },
  smug: (g, o) => {
    eyes(g, (x) => openEye(g, x));
    o.cut(() => eyes(g, (x) => g.fillRect(x - 3.5, EY - 4.5, 7, 4.3)));
    eyes(g, (x) => line(g, [x - 3, EY - 0.2, x + 3, EY - 0.2]));
    line(g, [12.5, MY + 1, 16.5, MY + 1.2, 20, MY - 1.2]);
  },
  sad: (g, o) => {
    eyes(g, o.blink ? (x) => shutEye(g, x) : (x) => openEye(g, x)); brows(g, -1.6, 0.6); frown(g);
    g.fillStyle = TEAR; g.beginPath(); g.ellipse(ER + 1.5, EY + 5, 1, 1.5, 0, 0, Math.PI * 2); g.fill();
  },
  sparkle: (g) => { eyes(g, (x) => star(g, x, EY, 4.2, 1.6)); openMouth(g, 4); },
  cheer: (g) => {
    eyes(g, (x) => archEye(g, x)); openMouth(g, 5.5);
    for (const [x, y] of [[4, 6], [27, 5], [28, 22]]) star(g, x, y, 1.8, 0.6);
  },
  blank: () => {},
};

/**
 * Draw face `id` on a 32 x 32 context g (cleared, transparent). opts: { fg, blink, t }. Unknown ids
 * draw neutral. Returns the id drawn.
 */
export function drawFace(g, id, { fg = '#ff8cc6', blink = false, t = 0, weight = 1.7 } = {}) {
  const paint = PAINT[id] ? id : 'neutral';
  g.save();
  g.fillStyle = fg; g.strokeStyle = fg; g.lineWidth = weight; g.lineCap = 'round'; g.lineJoin = 'round';
  const cut = (fn) => { g.save(); g.globalCompositeOperation = 'destination-out'; g.fillStyle = '#000'; fn(); g.restore(); };
  PAINT[paint](g, { blink, cut, t });
  g.restore();
  return paint;
}

/**
 * A screen canvas (128 px) for one kart. doc = document. set(id, blink, t) redraws only on a change
 * (dizzy turns its spirals every tick of t, so pass t only for animated faces) and returns true when
 * the canvas changed (the caller flags its texture). bg / fg: screen and feature colours.
 * Polish 3: zoom (features drawn larger about the middle) and weight (line width on the 32 grid) make the
 * face read at speed; FACE_LOOK holds the defaults.
 */
export const FACE_LOOK = Object.freeze({ zoom: 1.2, weight: 2.2, glowDiv: 8, gain: 1.7 });

export function createFaceCanvas(doc, { bg = '#2a1630', fg = '#ff8cc6', size = 128, zoom = FACE_LOOK.zoom, weight = FACE_LOOK.weight } = {}) {
  const canvas = doc.createElement('canvas'), lo = doc.createElement('canvas');
  canvas.width = canvas.height = size; lo.width = lo.height = 32;
  const g = canvas.getContext('2d'), l = lo.getContext('2d');
  let key = '';
  function set(id, blink = false, t = 0) {
    const tick = id === 'dizzy' ? Math.floor(t * 8) : 0, k = id + (blink ? '~' : '') + tick + fg;
    if (k === key) return false;
    key = k;
    l.clearRect(0, 0, 32, 32);
    drawFace(l, id, { fg, blink, t: tick / 8, weight });
    g.save();
    g.fillStyle = bg; g.fillRect(0, 0, size, size);
    g.imageSmoothingEnabled = false;
    g.shadowColor = fg; g.shadowBlur = size / FACE_LOOK.glowDiv;
    const z = size * zoom, o = (size - z) / 2;
    g.drawImage(lo, o, o, z, z);
    g.shadowBlur = 0;
    g.fillStyle = 'rgba(0, 0, 0, 0.16)';                  // faint scanlines: the pixel-screen look
    for (let y = 0; y < size; y += 4) g.fillRect(0, y + 3, size, 1);
    g.restore();
    return true;
  }
  return { canvas, set, setColour(c) { if (c && c !== fg) { fg = c; key = ''; } } };
}

// ---- head marks (polish 3): a manga mark over EMI's head, readable at speed ----
// Drawn once into a 4 x 2 atlas canvas (cells of MARK_PX), row 0 on top; karts.js shows one cell per kart
// on a sprite (the swirl turns by the sprite's rotation). Bright fills with a dark outline read on any sky.

export const MARK_IDS = Object.freeze(['swirl', 'sweat', 'zz', 'ice', 'heart', 'spark', 'anger', 'stars']);
export const MARK_PX = 128;
/** Where and how a mark shows (kart-local metres; the side offset is for the sweat drop and the anger mark). */
export const MARK_LOOK = Object.freeze({ sizeM: 0.9, upM: 1.85, sideM: 0.45, sided: Object.freeze(['sweat', 'anger']), bobM: 0.06, bobHz: 1.6,
  spinRadps: 3.2, wobbleRad: 0.15, wobbleHz: 1.4, popIn: 0.5 });
const INK = '#1a0a24';

/** Atlas cell of a mark id (unknown ids give null): { col, row } on a 4 x 2 grid. */
export function markCell(id) {
  const i = MARK_IDS.indexOf(id);
  return i < 0 ? null : { col: i % 4, row: (i / 4) | 0 };
}

function star4(g, x, y, ro, ri) {
  g.beginPath();
  for (let k = 0; k < 8; k++) {
    const a = (k / 8) * Math.PI * 2 - Math.PI / 2, r = k & 1 ? ri : ro;
    g[k ? 'lineTo' : 'moveTo'](x + Math.cos(a) * r, y + Math.sin(a) * r);
  }
  g.closePath();
}

// each painter draws on the 32 grid: inked (outline pass) then coloured
const MARK = {
  swirl(g, ink) {
    g.beginPath();
    for (let a = 0; a <= Math.PI * 4.6; a += 0.2) { const r = 1 + a * 0.95; g[a ? 'lineTo' : 'moveTo'](16 + Math.cos(a) * r, 16 + Math.sin(a) * r); }
    g.strokeStyle = ink ? INK : '#9fe9ff'; g.lineWidth = ink ? 5.5 : 2.6; g.stroke();
  },
  sweat(g, ink) {
    g.beginPath(); g.moveTo(16, 3); g.bezierCurveTo(22, 12, 26, 18, 26, 21); g.arc(16, 21, 10, 0, Math.PI); g.bezierCurveTo(6, 18, 10, 12, 16, 3); g.closePath();
    if (ink) { g.strokeStyle = INK; g.lineWidth = 3.5; g.stroke(); return; }
    g.fillStyle = '#8fd8ff'; g.fill();
    g.fillStyle = '#ffffff'; g.beginPath(); g.ellipse(12, 21, 2.2, 3.6, -0.4, 0, Math.PI * 2); g.fill();
  },
  zz(g, ink) {
    const z = (x, y, s) => { g.beginPath(); g.moveTo(x, y); g.lineTo(x + s, y); g.lineTo(x, y + s); g.lineTo(x + s, y + s); g.stroke(); };
    g.strokeStyle = ink ? INK : '#c9b6ff'; g.lineWidth = ink ? 5.5 : 2.6;
    z(4, 15, 12); z(18, 5, 9);
  },
  ice(g, ink) {
    g.strokeStyle = ink ? INK : '#dff4ff'; g.lineWidth = ink ? 5.5 : 2.4;
    for (let k = 0; k < 3; k++) {
      const a = (k / 3) * Math.PI, c = Math.cos(a) * 12, s = Math.sin(a) * 12;
      g.beginPath(); g.moveTo(16 - c, 16 - s); g.lineTo(16 + c, 16 + s); g.stroke();
      for (const d of [-1, 1]) {
        const px = 16 + c * 0.6 * d, py = 16 + s * 0.6 * d;
        g.beginPath(); g.moveTo(px + Math.cos(a + 2.4 * d) * 4, py + Math.sin(a + 2.4 * d) * 4); g.lineTo(px, py);
        g.lineTo(px + Math.cos(a - 2.4 * d) * 4, py + Math.sin(a - 2.4 * d) * 4); g.stroke();
      }
    }
  },
  heart(g, ink) {
    g.beginPath(); g.moveTo(16, 27); g.bezierCurveTo(3, 18, 3, 6, 10, 6); g.bezierCurveTo(13, 6, 15, 8, 16, 11);
    g.bezierCurveTo(17, 8, 19, 6, 22, 6); g.bezierCurveTo(29, 6, 29, 18, 16, 27); g.closePath();
    if (ink) { g.strokeStyle = INK; g.lineWidth = 3.5; g.stroke(); return; }
    g.fillStyle = '#ff4fb0'; g.fill();
    g.fillStyle = '#ffd0ea'; g.beginPath(); g.ellipse(10.5, 11, 2, 2.8, -0.5, 0, Math.PI * 2); g.fill();
  },
  spark(g, ink) {
    const parts = [[15, 16, 13, 3.6], [26, 7, 5, 1.5], [7, 26, 4, 1.2]];
    for (const [x, y, ro, ri] of parts) {
      star4(g, x, y, ro, ri);
      if (ink) { g.strokeStyle = INK; g.lineWidth = 3; g.stroke(); } else { g.fillStyle = x === 15 ? '#ffd23a' : '#ffffff'; g.fill(); }
    }
  },
  anger(g, ink) {
    g.strokeStyle = ink ? INK : '#ff3355'; g.lineWidth = ink ? 6 : 3;
    for (const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
      g.beginPath(); g.moveTo(16 + sx * 3, 16 + sy * 11); g.quadraticCurveTo(16 + sx * 3, 16 + sy * 3, 16 + sx * 11, 16 + sy * 3); g.stroke();
    }
  },
  stars(g, ink) {
    for (const [x, y, r] of [[16, 7, 6], [7, 22, 5], [25, 22, 5]]) {
      star4(g, x, y, r, r * 0.4);
      if (ink) { g.strokeStyle = INK; g.lineWidth = 3; g.stroke(); } else { g.fillStyle = '#ffe27a'; g.fill(); }
    }
  },
};

/** Draw mark id into the 32-unit square at the context's current transform; returns the id or null. */
export function drawMark(g, id) {
  if (!MARK[id]) return null;
  g.save();
  g.lineCap = 'round'; g.lineJoin = 'round';
  MARK[id](g, true);
  MARK[id](g, false);
  g.restore();
  return id;
}

/** The mark atlas canvas: 4 x 2 cells of MARK_PX, every mark drawn once. doc = document. */
export function createMarkAtlas(doc, px = MARK_PX) {
  const canvas = doc.createElement('canvas');
  canvas.width = px * 4; canvas.height = px * 2;
  const g = canvas.getContext('2d'), s = px / 32;
  for (const id of MARK_IDS) {
    const c = markCell(id);
    g.save();
    g.translate(c.col * px, c.row * px); g.scale(s * 0.88, s * 0.88); g.translate(32 * 0.068, 32 * 0.068);
    drawMark(g, id);
    g.restore();
  }
  return canvas;
}
