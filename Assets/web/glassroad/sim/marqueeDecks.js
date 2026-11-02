/* sim/marqueeDecks.js - where another part of the course passes over a dropping MARQUEE screen.
 * Pure (no three.js, no DOM, no RNG): built once per loaded course from its road frames and read only when a
 * dropping screen picks its fixed spot. On GOONWORKS the lower road runs about 30 m under an upper deck for long
 * stretches; a screen hung there fell through the deck and the deck crossed the picture in the chase view.
 */
const STEP_M = 2, CELL_M = 24, SELF_M = 60, ABOVE_M = 5;
const cache = new WeakMap();

/** Every road centre sample of the course (main road and forks) in a flat XZ grid. Cached per track object. */
export function deckIndex(track) {
  if (!track?.frameAt || !(track.lengthM > 0)) return null;
  let index = cache.get(track);
  if (index) return index;
  const xs = [], ys = [], zs = [], ws = [], ss = [], cells = new Map(), key = (i, k) => i * 73856093 ^ k * 19349663;
  const add = (f, s) => {
    if (!f?.p?.every(Number.isFinite)) return;
    const j = xs.length; xs.push(f.p[0]); ys.push(f.p[1]); zs.push(f.p[2]); ws.push(f.w > 0 ? f.w : 0); ss.push(s);
    const c = key(Math.floor(f.p[0] / CELL_M), Math.floor(f.p[2] / CELL_M));
    (cells.get(c) || cells.set(c, []).get(c)).push(j);
  };
  const count = Math.ceil(track.lengthM / STEP_M);
  for (let i = 0; i < count; i++) { const s = track.lengthM * i / count; add(track.frameAt(s), s); }
  for (const fork of track.forks || []) for (const f of fork.samples(STEP_M)) add(f, NaN);   // a fork never counts as itself
  let wide = 0; for (const w of ws) wide = Math.max(wide, w / 2);
  index = { xs, ys, zs, ws, ss, cells, key, lengthM: track.lengthM, wide };
  cache.set(track, index);
  return index;
}

/**
 * Height (along the spot's road normal) of the lowest other road whose surface reaches within radius metres of the
 * vertical column at lateral x over spot frame f ({p,n,b}); Infinity when nothing passes overhead. s is the spot's
 * main-road position (null on a fork): the same road within SELF_M of it is the screen's own road, never a deck.
 */
export function deckOver(index, f, x, radius, s = null) {
  if (!index || !f) return Infinity;
  const c = [0, 1, 2].map(i => f.p[i] + f.b[i] * x), n = f.n, reach = radius + index.wide;
  const i0 = Math.floor((c[0] - reach) / CELL_M), i1 = Math.floor((c[0] + reach) / CELL_M);
  const k0 = Math.floor((c[2] - reach) / CELL_M), k1 = Math.floor((c[2] + reach) / CELL_M);
  let low = Infinity;
  for (let i = i0; i <= i1; i++) for (let k = k0; k <= k1; k++) for (const j of index.cells.get(index.key(i, k)) || []) {
    if (Number.isFinite(s) && Number.isFinite(index.ss[j])) {
      const d = Math.abs(index.ss[j] - s) % index.lengthM;
      if (Math.min(d, index.lengthM - d) < SELF_M) continue;
    }
    const v = [index.xs[j] - c[0], index.ys[j] - c[1], index.zs[j] - c[2]], up = v[0] * n[0] + v[1] * n[1] + v[2] * n[2];
    if (up < ABOVE_M || up >= low) continue;
    const side = Math.hypot(v[0] - n[0] * up, v[1] - n[1] * up, v[2] - n[2] * up);
    if (side <= radius + index.ws[j] / 2) low = up;
  }
  return low;
}
