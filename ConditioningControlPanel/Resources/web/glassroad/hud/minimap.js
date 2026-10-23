/* hud/minimap.js - the small top-down map: the track loop and one dot per racer.
 *
 * fitMap / projectInto / mapPoints are pure (tested in tests/hud-minimap.test.mjs). The draw
 * side is a 2D canvas: the loop is stroked once into a cached layer whenever the size or track
 * changes, then each frame clears and draws the layer plus the dots. World x maps to the right,
 * world z downward (seen from above), one scale for both axes so the shape keeps its look.
 *
 * Polish 6 (the stacked course): with heights (setTrack({ xz, y })) the loop is baked as one layer per
 * deck band (DECK_EDGES_M) and each frame draws the bands the player is not on first, lower ones dimmest,
 * then the player's own band on top at full ink. The player's band follows its place on the loop
 * (nearestOnLoop, searched round the last place so a deck never jumps to the one stacked over it).
 */

/**
 * Fit a closed loop of world (x, z) pairs into a w x h box with `pad` px on every side.
 * @param {Float32Array|number[]} xz
 * @returns {{ s: number, ox: number, oy: number, minX: number, minZ: number }} px = ox + (x - minX) * s
 */
export function fitMap(xz, w, h, pad = 6) {
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (let i = 0; i + 1 < xz.length; i += 2) {
    const x = xz[i], z = xz[i + 1];
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
  }
  if (!(maxX >= minX)) return { s: 1, ox: w / 2, oy: h / 2, minX: 0, minZ: 0 };
  const spanX = Math.max(maxX - minX, 1e-6), spanZ = Math.max(maxZ - minZ, 1e-6);
  const s = Math.max(0, Math.min((w - 2 * pad) / spanX, (h - 2 * pad) / spanZ));
  return { s, ox: (w - spanX * s) / 2, oy: (h - spanZ * s) / 2, minX, minZ };
}

/** World (x, z) -> map px written into out[0], out[1]. */
export function projectInto(fit, x, z, out) {
  out[0] = fit.ox + (x - fit.minX) * fit.s;
  out[1] = fit.oy + (z - fit.minZ) * fit.s;
  return out;
}

/** Every (x, z) pair of src projected into a new Float32Array of px pairs. */
export function mapPoints(fit, src) {
  const out = new Float32Array(src.length - (src.length % 2)), p = [0, 0];
  for (let i = 0; i < out.length; i += 2) {
    projectInto(fit, src[i], src[i + 1], p);
    out[i] = p[0]; out[i + 1] = p[1];
  }
  return out;
}

/** Thin a long loop to at most `max` points (the map is ~120 px wide). */
export function thinLoop(xz, max = 400) {
  const n = Math.floor(xz.length / 2);
  if (n <= max) return xz;
  const out = new Float32Array(max * 2);
  for (let k = 0; k < max; k++) { const i = Math.floor(k * n / max) * 2; out[k * 2] = xz[i]; out[k * 2 + 1] = xz[i + 1]; }
  return out;
}

/** Deck bands by height (metres): below 7 is the ground deck, 7 to 21 the middle deck, above that the top deck. */
export const DECK_EDGES_M = Object.freeze([7, 21]);
/** How strong a band other than the player's draws: lower decks dimmest, upper ones a little stronger. */
export const DECK_ALPHA = Object.freeze({ below: 0.32, above: 0.55 });

/** Deck band of a height: 0 ground, 1 middle, 2 top. */
export function bandOf(y, edges = DECK_EDGES_M) {
  let b = 0;
  for (const e of edges) if (y >= e) b++;
  return b;
}

/**
 * Index runs of a closed loop whose points sit in `band`: [[from, to], ...] inclusive, each grown by one
 * point either side so a band's stroke meets the next band's. A loop wholly in the band is one run.
 */
export function deckRuns(y, band, edges = DECK_EDGES_M) {
  const n = y.length, inB = (i) => bandOf(y[((i % n) + n) % n], edges) === band;
  let first = -1;
  for (let i = 0; i < n; i++) if (!inB(i)) { first = i; break; }
  if (first < 0) return n ? [[0, n]] : [];
  const out = [];
  let a = -1;
  for (let k = 1; k <= n; k++) {
    const i = first + k, ok = k < n && inB(i);
    if (ok && a < 0) a = i;
    if (!ok && a >= 0) { out.push([a - 1, i]); a = -1; }
  }
  return out;
}

/**
 * Index of the loop point nearest (x, z) in plan. With hint >= 0 only hint +- win points are searched
 * (the player moves along the loop, so a stacked deck never steals it); a result further than farM
 * searches them all.
 */
export function nearestOnLoop(xz, x, z, hint = -1, win = 12, farM = 30) {
  const n = Math.floor(xz.length / 2);
  if (!n) return -1;
  const scan = (from, count) => {
    let best = -1, bd = Infinity;
    for (let k = 0; k < count; k++) {
      const i = (((from + k) % n) + n) % n, dx = xz[i * 2] - x, dz = xz[i * 2 + 1] - z, d = dx * dx + dz * dz;
      if (d < bd) { bd = d; best = i; }
    }
    return [best, Math.sqrt(bd)];
  };
  if (hint >= 0) {
    const [i, d] = scan(hint - win, 2 * win + 1);
    if (d <= farM) return i;
  }
  return scan(0, n)[0];
}

/** thinLoop for a loop with heights: the same picks from both. */
export function thinLoopY(xz, y, max = 400) {
  const n = Math.floor(xz.length / 2);
  if (n <= max) return { xz, y };
  const ox = new Float32Array(max * 2), oy = new Float32Array(max);
  for (let k = 0; k < max; k++) { const i = Math.floor(k * n / max); ox[k * 2] = xz[i * 2]; ox[k * 2 + 1] = xz[i * 2 + 1]; oy[k] = y[i]; }
  return { xz: ox, y: oy };
}

const INK = 'rgba(253, 247, 255, 0.78)', SHADOW = 'rgba(14, 10, 22, 0.55)', BOT = 'rgba(253, 247, 255, 0.9)';
const PINK = '#ff5fa8', GOLD = '#ffd36b';

/** @param {HTMLCanvasElement} canvas */
export function createMinimap(canvas, { doc = globalThis.document } = {}) {
  const ctx = canvas.getContext && canvas.getContext('2d');
  let loop = null, ys = null, fit = null, layer = null, bands = null, w = 0, h = 0, dpr = 1, at = -1;
  const p = [0, 0];

  function size() {
    const cw = canvas.clientWidth || 120, ch = canvas.clientHeight || 96;
    const d = Math.min(2, globalThis.devicePixelRatio || 1);
    if (cw === w && ch === h && d === dpr && layer) return;
    w = cw; h = ch; dpr = d;
    canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
    bake();
  }
  function bake() {
    layer = null; bands = null;
    if (!loop || !ctx) return;
    fit = fitMap(loop, w, h, 7);
    const pts = mapPoints(fit, loop), n = pts.length / 2;
    const sheet = () => {
      const c = doc.createElement('canvas');
      c.width = canvas.width; c.height = canvas.height;
      const g = c.getContext('2d');
      g.scale(dpr, dpr); g.lineJoin = 'round'; g.lineCap = 'round';
      return [c, g];
    };
    const stroke = (g, path) => {
      path(); g.strokeStyle = SHADOW; g.lineWidth = 6; g.stroke();
      path(); g.strokeStyle = INK; g.lineWidth = 2.5; g.stroke();
    };
    const levels = ys ? new Set(Array.from(ys, (v) => bandOf(v))) : null;
    if (!levels || levels.size < 2) {
      const [c, g] = sheet();
      stroke(g, () => { g.beginPath(); g.moveTo(pts[0], pts[1]); for (let i = 2; i < pts.length; i += 2) g.lineTo(pts[i], pts[i + 1]); g.closePath(); });
      layer = c;
      return;
    }
    // one sheet per deck band, each holding only its own runs of the loop
    bands = [];
    for (const b of [...levels].sort()) {
      const [c, g] = sheet();
      stroke(g, () => {
        g.beginPath();
        for (const [a, z] of deckRuns(ys, b)) {
          for (let i = a; i <= z; i++) { const k = ((i % n) + n) % n; if (i === a) g.moveTo(pts[k * 2], pts[k * 2 + 1]); else g.lineTo(pts[k * 2], pts[k * 2 + 1]); }
        }
      });
      bands.push({ band: b, sheet: c });
    }
    layer = bands[0].sheet;
  }
  function dot(x, y, r, fill) {
    ctx.beginPath(); ctx.arc(x, y, r + 1.5, 0, Math.PI * 2); ctx.fillStyle = SHADOW; ctx.fill();
    ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fillStyle = fill; ctx.fill();
  }
  return {
    /** xz: the centreline as x, z pairs; y (optional): its height per point, which turns on the deck layers. */
    setTrack({ xz, y } = {}) {
      if (xz && xz.length >= 6 && y && y.length * 2 >= xz.length) { const t = thinLoopY(xz, y); loop = t.xz; ys = t.y; }
      else { loop = xz && xz.length >= 6 ? thinLoop(xz) : null; ys = null; }
      at = -1; w = 0; size();
    },
    /** @param {Float32Array} map x, z per racer (racer 0 = you) @param {number} leader racer index in 1st */
    draw(map, leader = -1) {
      if (!ctx) return;
      size();
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      if (!layer || !fit) return;
      if (!bands) ctx.drawImage(layer, 0, 0);
      else {
        // the player's deck on top at full ink, the others under it (lower decks dimmest)
        if (map && map.length >= 2) at = nearestOnLoop(loop, map[0], map[1], at);
        const mine = at >= 0 ? bandOf(ys[at]) : bands[0].band;
        for (const b of bands) {
          if (b.band === mine) continue;
          ctx.globalAlpha = b.band < mine ? DECK_ALPHA.below : DECK_ALPHA.above;
          ctx.drawImage(b.sheet, 0, 0);
        }
        ctx.globalAlpha = 1;
        const own = bands.find((b) => b.band === mine);
        if (own) ctx.drawImage(own.sheet, 0, 0);
      }
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      if (!map) return;
      for (let i = map.length - 2; i >= 0; i -= 2) {   // you last, on top
        const k = i / 2;
        projectInto(fit, map[i], map[i + 1], p);
        if (k === 0) dot(p[0], p[1], 4.5, PINK);
        else dot(p[0], p[1], 3, k === leader ? GOLD : BOT);
      }
    },
  };
}
