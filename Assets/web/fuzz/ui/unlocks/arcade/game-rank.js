/* ui/unlocks/arcade/game-rank.js - a game's Journey rank, read LIVE (owner 2026-11-10, late game wave).
 * Rank 1 and rank 2 of a game are named upgrades that change how it plays; rank 3+ only grow its collect bonus.
 * Outside Journey runs there are no ranks, so every upgrade is off. Never cache the answer at mount: a rank can be
 * bought while the game is open. */

export function gameRank(state, id) {
  const n = Math.floor(Number(state?.store?.journey?.ranks?.[id]));
  return n > 0 ? n : 0;
}
