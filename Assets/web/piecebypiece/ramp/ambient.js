/* ============================================================================
 * ramp/ambient.js - the stands' own calm Distraction, for players who send none.
 *
 * WHY (owner run, 2026-10-14). The stands replay the TURN player's Distraction
 * from the fx record that player's page posts (POST /match/:id/fx). Pages older
 * than spectate v2 (the released 7.1.8, the web and phone builds before it)
 * never post one, so a watched game between two of them carried no fx at all
 * and the watcher saw no pictures, however well their own were set up.
 *
 * WHAT. While the side on the move has never sent a record in this match, the
 * stands run a quiet one of their own, in the watcher's own pictures:
 *   - the heat climbs slowly through a turn, to a low ceiling, and a move is the
 *     breath as always;
 *   - a picture on a capture, on a check, and every few plies, paced by the game;
 *   - one more on a long think.
 * The first record a side sends switches it off for that side for the rest of
 * the match: a real record always wins. Options: "Players on older versions:
 * ambient pictures" (presentation().watchAmbient, on by default).
 *
 * Pure: no DOM, no timers, the caller hands the clock in.
 * ==========================================================================*/

export const AMBIENT_TUNING = {
  heatMax: 0.3,          // the ceiling: a calm screen, the board always readable
  climbMs: 45000,        // a turn this long reaches it
  everyPlies: 4,         // a picture every this many plies, capture or not
  thinkEveryMs: 25000,   // and one more each time a think runs this long
  kind: 'flash',         // a picture popping at the edges (ramp/layers/flash.js)
};

const SIDE = (s) => (s === 'w' || s === 'b' ? s : null);
const clamp01 = (v) => Math.min(1, Math.max(0, Number(v) || 0));

/**
 * @param {object} [tuning] AMBIENT_TUNING by default
 * @returns the plan: noteFx, active, move, capture, check, tick, heat, reset, debug
 */
export function createAmbient(tuning = AMBIENT_TUNING) {
  const t = { ...AMBIENT_TUNING, ...(tuning || {}) };
  const recorded = { w: false, b: false };
  let turn = 'w';
  let turnAt = 0;
  let plies = 0;
  let pending = 0;        // pictures owed to the next move (a capture, a check, the pacing)
  let nextThinkAt = 0;

  const plan = {
    /** Back to a fresh match (a new watch, a leave). */
    reset(now = 0, side = 'w') {
      recorded.w = recorded.b = false;
      turn = SIDE(side) || 'w';
      turnAt = now;
      plies = 0;
      pending = 0;
      nextThinkAt = now + t.thinkEveryMs;
    },

    /** A record off the tape from this side: from now on its turns are the real thing. */
    noteFx(side) {
      const s = SIDE(side);
      if (s) recorded[s] = true;
    },

    /** Does the ambient run for this side (the side on the move by default)? */
    active(side = turn) {
      const s = SIDE(side);
      return !!s && !recorded[s];
    },

    /** A capture by `by` (the mover). Owed to the move it rides on. */
    capture(by) { if (plan.active(SIDE(by) || turn)) pending += 1; },

    /** A check given (`side` is the side IN check, so the mover is the other one). */
    check(side) { const s = SIDE(side); if (plan.active(s ? otherOf(s) : turn)) pending += 1; },

    /**
     * A move landed and `side` is now on the move. Returns how many pictures to
     * show now (after the breath): what the move owed plus the pacing.
     */
    move(side, now) {
      const mover = turn;
      const s = SIDE(side);
      if (s) turn = s;
      turnAt = now;
      nextThinkAt = now + t.thinkEveryMs;
      plies += 1;
      let n = pending;
      pending = 0;
      if (!plan.active(mover)) n = 0;
      else if (t.everyPlies > 0 && plies % t.everyPlies === 0 && n === 0) n = 1;
      return n;
    },

    /** The turn goes on: how many pictures a long think earns now (0 or 1). */
    tick(now) {
      if (!plan.active() || now < nextThinkAt) return 0;
      nextThinkAt = now + t.thinkEveryMs;
      return 1;
    },

    /** The heat the stands ride now, or null when the side on the move sends its own. */
    heat(now) {
      if (!plan.active()) return null;
      return t.heatMax * clamp01((now - turnAt) / Math.max(1, t.climbMs));
    },

    get kind() { return t.kind; },

    debug: () => ({ recorded: { ...recorded }, turn, plies, pending }),
  };
  plan.reset(0);
  return plan;
}

function otherOf(s) { return s === 'w' ? 'b' : 'w'; }
