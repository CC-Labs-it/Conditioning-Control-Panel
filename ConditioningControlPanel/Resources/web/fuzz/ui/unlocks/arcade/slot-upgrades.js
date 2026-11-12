// Mini Slots Journey upgrades (owner 2026-11-10, brief handoff-1110/TC-LATE-GAME-BRIEF.md). Pure rules on top of the saved
// machine in sim/arcade.js, so that file stays the shared base and a run without ranks plays exactly as before.
//   rank 1 Hold: after a spin, tap a reel to hold it for the next spin (once per coin).
//   rank 2 Fourth Coin: room for 4 coins, a coin every 35 s.
// "Two wins in every six spins" holds whatever the player holds: the bag still decides win or loss, the hold only decides
// which symbol a win lands on (hold a spiral and the next win is the Spiral sweep) and keeps that reel still on a loss.
import { slotState, beginSpin, COIN_MS } from '../../../sim/arcade.js';

export const SLOT_CAP = 3, BIG_CAP = 4, BIG_COIN_MS = 35000;
/** The machine's Journey rank, read live each frame. Outside Journey there are no ranks. */
export const slotRank = (state) => Math.max(0, Math.floor(Number(state?.store?.journey?.ranks?.mini_slots) || 0));
export const coinCap = (rank = 0) => (rank >= 2 ? BIG_CAP : SLOT_CAP);
export const coinMs = (rank = 0) => (rank >= 2 ? BIG_COIN_MS : COIN_MS);
export const canHold = (rank = 0) => rank >= 1;

/** slotState plus the two fields the upgrades add: a fourth coin and the held reel. */
export function restoreSlot(saved) {
  const s = slotState(saved);
  if (saved && Number.isFinite(saved.coins) && saved.coins > SLOT_CAP) s.coins = Math.min(BIG_CAP, Math.floor(saved.coins));
  s.hold = saved && Number.isInteger(saved.hold) && saved.hold >= 0 && saved.hold < 3 ? saved.hold : null;
  return s;
}

/** slotStep with the rank's tray size and coin time. Rank 0 is slotStep exactly. Coins above the cap are never taken away. */
export function slotStepRanked(s, ms, rank = 0) {
  const dt = Math.max(0, Number.isFinite(ms) ? ms : 0), cap = coinCap(rank), every = coinMs(rank);
  if (s.coins < cap) {
    s.elapsed += dt;
    while (s.elapsed >= every && s.coins < cap) { s.elapsed -= every; s.coins++; }
  }
  if (s.coins >= cap) s.elapsed = 0;
  if (s.pending) s.pending.left = Math.max(0, s.pending.left - dt);
}

/** Pick (or move, or drop) the held reel between spins. Refused while the reels turn or without the rank. */
export function toggleHold(s, reel, rank = 0) {
  if (!canHold(rank) || s.pending || !Number.isInteger(reel) || reel < 0 || reel > 2) return false;
  s.hold = s.hold === reel ? null : reel;
  return true;
}

/** beginSpin, then the hold: the same bag draw and RNG calls, the held reel keeps the symbol it shows. One hold per coin. */
export function beginSpinHeld(s, rng, rank = 0) {
  const reel = canHold(rank) ? s.hold : null, symbol = reel == null ? null : s.last[reel];
  if (!beginSpin(s, rng)) return false;
  s.hold = null;
  if (reel != null && Number.isInteger(symbol)) {
    const base = (symbol - reel + 4) % 4;
    s.pending.reels = s.pending.win ? [symbol, symbol, symbol] : [0, 1, 2].map((i) => (base + i) % 4);
    s.pending.held = reel;
  }
  return true;
}
