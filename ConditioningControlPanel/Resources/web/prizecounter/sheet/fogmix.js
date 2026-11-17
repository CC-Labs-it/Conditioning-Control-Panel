/* sheet/fogmix.js - Two-tone fog as the desktop draws it (CCP.Core Super/FogMix.cs, Services/Super/Draw/FogMixDraw.cs):
 * the same blob field, the same seam and the same OKLCH colour ramp, so the shop shows what the app shows. The second
 * colour moves through the first as soft separate regions with a thin seam that keeps its colour (never an averaged,
 * murky tint). Pure maths plus one small drawing helper: the field goes on a small grid, stretched soft over the canvas,
 * a few thousand cells a frame whatever the canvas size. Keep the numbers in step with FogMix.cs (the tests pin both). */

export const RAMP_SIZE = 64, BLOBS = 5, SEAM_LOW = 0.18, SEAM_HIGH = 0.95, WALK_LOW = 0.16, WALK_HIGH = 0.84, MID_SOFTEN = 0.28;
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));

/** The raw blob field at (x, y), screen units with the height = 1 and x in 0..aspect, at mix time t seconds. */
export function field(x, y, aspect, t) {
  const wx = x + 0.11 * Math.sin(y * 2.6 + t * 0.14) + 0.05 * Math.sin(y * 5.3 - t * 0.19 + 1.7);
  const wy = y + 0.10 * Math.sin(x * 2.2 - t * 0.12 + 0.6) + 0.05 * Math.sin(x * 4.9 + t * 0.16 + 2.9);
  let s = 0;
  for (let i = 0; i < BLOBS; i++) {
    const bx = aspect * (0.5 + 0.42 * Math.sin(t * (0.10 + i * 0.022) + i * 2.1));
    const by = 0.5 + 0.40 * Math.cos(t * (0.08 + i * 0.028) + i * 1.3);
    const r = 0.27 + 0.07 * Math.sin(t * 0.13 + i * 1.7);
    const dx = wx - bx, dy = wy - by;
    s += Math.exp(-(dx * dx + dy * dy) / (r * r));
  }
  return s;
}

/** How much of the second colour a field value shows, 0..1, smooth at the seam. */
export function shape(s) { const k = clamp((s - SEAM_LOW) / (SEAM_HIGH - SEAM_LOW), 0, 1); return k * k * (3 - 2 * k); }
/** How far along the walk a ramp position (0..1) is. */
export function walk(k) { const x = clamp((k - WALK_LOW) / (WALK_HIGH - WALK_LOW), 0, 1); return x * x * (3 - 2 * x); }

const lin = (v) => { const c = v / 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
const enc = (c) => { c = clamp(c, 0, 1); const s = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055; return Math.round(clamp(s, 0, 1) * 255); };
export function toOklab([R, G, B]) {
  const r = lin(R), g = lin(G), b = lin(B);
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  return [0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s];
}
function labToLinear(L, a, b) {
  const l_ = L + 0.3963377774 * a + 0.2158037573 * b, m_ = L - 0.1055613458 * a - 0.0638541728 * b, s_ = L - 0.0894841775 * a - 1.2914855480 * b;
  const l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
  return [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s];
}
const inGamut = (c) => c.every((v) => v >= -1e-4 && v <= 1 + 1e-4);
export function toOklch(rgb) { const [L, a, b] = toOklab(rgb); let h = Math.atan2(b, a) * 180 / Math.PI; if (h < 0) h += 360; return [L, Math.hypot(a, b), h]; }
/** OKLCH to [r, g, b]; past the gamut the colour keeps lightness and hue and loses chroma. */
export function fromOklch(L, C, h) {
  const rad = h * Math.PI / 180, cos = Math.cos(rad), sin = Math.sin(rad);
  L = clamp(L, 0, 1); C = Math.max(0, C);
  let rgb = labToLinear(L, C * cos, C * sin);
  if (!inGamut(rgb)) {
    let lo = 0, hi = C;
    for (let i = 0; i < 18; i++) { const mid = (lo + hi) / 2; if (inGamut(labToLinear(L, mid * cos, mid * sin))) lo = mid; else hi = mid; }
    rgb = labToLinear(L, lo * cos, lo * sin);
  }
  return rgb.map(enc);
}

/** The ramp from a to b ([r, g, b] each), RAMP_SIZE entries, as FogMix.Ramp. */
export function ramp(a, b) {
  let [la, ca, ha] = toOklch(a), [lb, cb, hb] = toOklch(b);
  if (ca < 0.02) ha = hb;
  if (cb < 0.02) hb = ha;
  let dh = hb - ha;
  if (dh > 180) dh -= 360;
  if (dh < -180) dh += 360;
  const far = clamp((Math.abs(dh) - 90) / 90, 0, 1), out = [];
  for (let i = 0; i < RAMP_SIZE; i++) {
    const u = walk(i / (RAMP_SIZE - 1)), soften = 1 - MID_SOFTEN * far * Math.sin(Math.PI * u);
    out.push(fromOklch(la + (lb - la) * u, (ca + (cb - ca) * u) * soften, ha + dh * u));
  }
  out[0] = [...a]; out[RAMP_SIZE - 1] = [...b];
  return out;
}

export const rgbOfHex = (hex) => { const m = String(hex || '').replace('#', ''); return m.length === 6 ? [0, 2, 4].map((i) => parseInt(m.slice(i, i + 2), 16)) : [255, 95, 168]; };

/** A reusable two-tone painter for one canvas: draw(g, ownHex, secondHex, t, w, h, alpha) paints the mix over (0, 0, w, h)
 * at one even strength (alpha 0..1), as the desktop's flat tint does. Needs a document for its small grid canvas. */
export function makeFogMix(doc) {
  const COLS = 48;
  let small = null, img = null, key = '', table = null;
  return function draw(g, ownHex, secondHex, t, w, h, alpha) {
    if (!(w > 0 && h > 0) || !doc) return;
    const aspect = w / h, gw = COLS, gh = clamp(Math.round(COLS / aspect), 8, COLS);
    if (!small) small = doc.createElement('canvas');
    if (small.width !== gw || small.height !== gh) { small.width = gw; small.height = gh; img = null; }
    const sg = small.getContext && small.getContext('2d');
    if (!sg) return;
    if (!img) img = sg.createImageData(gw, gh);
    const k = ownHex + secondHex;
    if (k !== key) { key = k; table = ramp(rgbOfHex(ownHex), rgbOfHex(secondHex)); }
    const px = img.data, top = table.length - 1, a8 = Math.round(clamp(alpha, 0, 1) * 255);
    for (let y = 0, j = 0; y < gh; y++) {
      const fy = (y + 0.5) / gh;
      for (let x = 0; x < gw; x++, j += 4) {
        const c = table[Math.round(shape(field((x + 0.5) / gw * aspect, fy, aspect, t)) * top)];
        px[j] = c[0]; px[j + 1] = c[1]; px[j + 2] = c[2]; px[j + 3] = a8;
      }
    }
    sg.putImageData(img, 0, 0);
    g.save(); g.imageSmoothingEnabled = true; if ('imageSmoothingQuality' in g) g.imageSmoothingQuality = 'high';
    g.drawImage(small, 0, 0, w, h);
    g.restore();
  };
}
