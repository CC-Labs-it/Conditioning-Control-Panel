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
export function drawFace(g, id, { fg = '#ff8cc6', blink = false, t = 0 } = {}) {
  const paint = PAINT[id] ? id : 'neutral';
  g.save();
  g.fillStyle = fg; g.strokeStyle = fg; g.lineWidth = 1.7; g.lineCap = 'round'; g.lineJoin = 'round';
  const cut = (fn) => { g.save(); g.globalCompositeOperation = 'destination-out'; g.fillStyle = '#000'; fn(); g.restore(); };
  PAINT[paint](g, { blink, cut, t });
  g.restore();
  return paint;
}

/**
 * A screen canvas (128 px) for one kart. doc = document. set(id, blink, t) redraws only on a change
 * (dizzy turns its spirals every tick of t, so pass t only for animated faces) and returns true when
 * the canvas changed (the caller flags its texture). bg / fg: screen and feature colours.
 */
export function createFaceCanvas(doc, { bg = '#2a1630', fg = '#ff8cc6', size = 128 } = {}) {
  const canvas = doc.createElement('canvas'), lo = doc.createElement('canvas');
  canvas.width = canvas.height = size; lo.width = lo.height = 32;
  const g = canvas.getContext('2d'), l = lo.getContext('2d');
  let key = '';
  function set(id, blink = false, t = 0) {
    const tick = id === 'dizzy' ? Math.floor(t * 8) : 0, k = id + (blink ? '~' : '') + tick + fg;
    if (k === key) return false;
    key = k;
    l.clearRect(0, 0, 32, 32);
    drawFace(l, id, { fg, blink, t: tick / 8 });
    g.save();
    g.fillStyle = bg; g.fillRect(0, 0, size, size);
    g.imageSmoothingEnabled = false;
    g.shadowColor = fg; g.shadowBlur = size / 12;
    g.drawImage(lo, 0, 0, size, size);
    g.shadowBlur = 0;
    g.fillStyle = 'rgba(0, 0, 0, 0.16)';                  // faint scanlines: the pixel-screen look
    for (let y = 0; y < size; y += 4) g.fillRect(0, y + 3, size, 1);
    g.restore();
    return true;
  }
  return { canvas, set, setColour(c) { if (c && c !== fg) { fg = c; key = ''; } } };
}
