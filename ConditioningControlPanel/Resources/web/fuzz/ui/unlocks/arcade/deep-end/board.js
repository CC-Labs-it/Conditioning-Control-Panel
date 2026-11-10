/* ui/unlocks/arcade/deep-end/board.js - the 2048 board of The Deep End, ported small for T&C (owner 2026-11-07).
 * PURE: no DOM, no clock, no ctx. Rules from arcademy/games/the-deep-end/board.js: one merge per tile per move, merges
 * resolve toward the move (the tile nearer the edge survives), a move that slides nothing is a no-op, tier 11 never merges,
 * and one spawn per real move (tier 1 at 90%, tier 2 at 10%). Tiers are integers 1..11; names live in DEPTHS. */

export const TIER_MAX = 11;
export const DEPTHS = Object.freeze(['', 'Awake', 'Fuzzy', 'Drowsy', 'Heavy', 'Drifting', 'Sinking', 'Sunken', 'Submerged', 'Fathoms', 'Trench', 'Blackout']);
export const DIRS = Object.freeze({ up: [-1, 0], down: [1, 0], left: [0, -1], right: [0, 1] });

export function createBoard(n = 4) {
  return { n, tiles: [], nextId: 1 };
}

/** A saved board, checked. Anything odd starts fresh water. */
export function restoreBoard(saved, n = 4) {
  const b = createBoard(n);
  if (!saved || saved.n !== n || !Array.isArray(saved.tiles)) return b;
  const seen = new Set();
  for (const t of saved.tiles) {
    const ok = t && Number.isInteger(t.r) && Number.isInteger(t.c) && t.r >= 0 && t.c >= 0 && t.r < n && t.c < n
      && Number.isInteger(t.tier) && t.tier >= 1 && t.tier <= TIER_MAX && !seen.has(t.r * n + t.c);
    if (!ok) return createBoard(n);
    seen.add(t.r * n + t.c);
    b.tiles.push({ id: b.nextId++, tier: t.tier, r: t.r, c: t.c });
  }
  return b;
}

export const snapshot = (b) => ({ n: b.n, tiles: b.tiles.map(({ tier, r, c }) => ({ tier, r, c })) });

export function emptyCells(b) {
  const used = new Set(b.tiles.map((t) => t.r * b.n + t.c)), out = [];
  for (let i = 0; i < b.n * b.n; i++) if (!used.has(i)) out.push({ r: Math.floor(i / b.n), c: i % b.n });
  return out;
}

/** One new tile in an empty cell. Always draws two rolls so a seeded stream stays aligned. */
export function spawn(b, rng = Math.random) {
  const cells = emptyCells(b), a = rng(), k = rng();
  if (!cells.length) return null;
  const at = cells[Math.min(cells.length - 1, Math.floor(a * cells.length))];
  const tile = { id: b.nextId++, tier: k < 0.9 ? 1 : 2, r: at.r, c: at.c };
  b.tiles.push(tile);
  return tile;
}

/**
 * Slides every tile toward `dir`. Answers { moved, merges: [{ survivor, victim, tier }], slid: [{ id, r, c }] }.
 * Victims are removed from the board; their last position (the survivor's cell) is in `slid` so a view can follow them in.
 */
export function slide(b, dir) {
  const d = DIRS[dir];
  const out = { moved: false, merges: [], slid: [] };
  if (!d) return out;
  const [dr, dc] = d, n = b.n;
  const at = new Map(b.tiles.map((t) => [t.r * n + t.c, t]));
  const victims = new Set();
  for (let lane = 0; lane < n; lane++) {
    /* Cells of this lane, nearest the edge we move toward first. */
    const cells = [];
    for (let k = 0; k < n; k++) {
      const step = (dr > 0 || dc > 0) ? n - 1 - k : k;
      cells.push(dr ? { r: step, c: lane } : { r: lane, c: step });
    }
    const line = cells.map((p) => at.get(p.r * n + p.c)).filter(Boolean);
    let pos = 0, last = null, lastMerged = false;
    for (const t of line) {
      if (last && !lastMerged && last.tier === t.tier && t.tier < TIER_MAX) {
        last.tier += 1;
        lastMerged = true;
        victims.add(t);
        out.merges.push({ survivor: last.id, victim: t.id, tier: last.tier });
        out.slid.push({ id: t.id, r: last.r, c: last.c });
        out.moved = true;
        continue;
      }
      const cell = cells[pos++];
      if (t.r !== cell.r || t.c !== cell.c) { t.r = cell.r; t.c = cell.c; out.moved = true; }
      out.slid.push({ id: t.id, r: t.r, c: t.c });
      last = t; lastMerged = false;
    }
  }
  if (victims.size) b.tiles = b.tiles.filter((t) => !victims.has(t));
  return out;
}

export function canMove(b) {
  if (emptyCells(b).length) return true;
  const at = new Map(b.tiles.map((t) => [t.r * b.n + t.c, t.tier]));
  for (const t of b.tiles) {
    if (t.tier >= TIER_MAX) continue;
    if (at.get(t.r * b.n + t.c + 1) === t.tier && t.c + 1 < b.n) return true;
    if (at.get((t.r + 1) * b.n + t.c) === t.tier && t.r + 1 < b.n) return true;
  }
  return false;
}

export const deepest = (b) => b.tiles.reduce((m, t) => Math.max(m, t.tier), 0);

/* Journey upgrades (owner 2026-11-10): rank 1 Bigger Board = 5x5, rank 2 Chains = each extra match in one move pays 1.5x
 * the one before. */
export const sizeFor = (rank) => rank >= 1 ? 5 : 4;
export const CHAIN_STEP = 1.5;

/** Grows the board in place: every tile keeps its cell, the new row and column are open water. Never shrinks. */
export function growBoard(b, n) {
  if (Number.isInteger(n) && n > b.n) b.n = n;
  return b;
}

/**
 * Chain pay for one move, in units of the game's chain unit. Without Chains a move pays only from three matches, one unit
 * each (today's rule). With Chains a move pays from two matches, and every extra match pays 1.5x the one before
 * (1, 1.5, 2.25, ...), so it is never less than the plain rule.
 */
export function chainUnits(count, chains) {
  const k = Math.max(0, Math.floor(Number(count) || 0));
  if (!chains) return k >= 3 ? k : 0;
  if (k < 2) return 0;
  let sum = 0;
  for (let i = 0; i < k; i++) sum += CHAIN_STEP ** i;
  return sum;
}

/** A fresh dive: two tiles. */
export function deal(b, rng = Math.random) {
  b.tiles = [];
  spawn(b, rng);
  spawn(b, rng);
  return b;
}
