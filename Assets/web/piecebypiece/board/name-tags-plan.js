/* ============================================================================
 * board/name-tags-plan.js - the numbers and the bookkeeping behind the floating
 * name tags (board/name-tags.js draws them).
 *
 * Pure: no three, no DOM, so smoke/name-tags-smoke.mjs pins every rule here.
 *
 *   labelFor(player, fallback)  a tag's two lines: the name (capped) and the rating
 *   namesFor(deal, opts)        both tags' labels for a new deal (hotseat, solo,
 *                               online seat, the stands)
 *   anchorFor(side, aspect)     where a side's tag floats, in board units
 *   reduce(state, type, p)      the bus, folded: whose turn, checks in a row,
 *                               the end; hands back the beats each tag plays
 *   spring(...)                 one damped spring step (every bounce rides one)
 *   tremble(t, run)             the in-check shake, bigger with every check in a row
 *   sweatRate(run)              sweat drops a second, the same way
 *   endBeat(result, winner, s)  'win' | 'lose' | 'draw' for one side
 * ==========================================================================*/

/** Every number the tags are made of. One place, on purpose. */
export const TAG = Object.freeze({
  width: 2.3,            // board units; a square is 1.0
  height: 0.78,
  depth: 0.1,
  bevel: 0.035,
  corner: 0.22,
  floatY: 0.85,          // the rest height of the tag's middle
  sizeAt: 15,            // camera distance at which a tag is drawn full size: nearer, smaller
  bobAmp: 0.07,          // idle bob, board units
  bobHz: 0.33,
  turnLift: 0.22,        // the side to move rises a little...
  turnGlow: 1,           // ...and lights up
  nameMax: 16,           // characters before the name is cut
  hop: 2.6,              // the mover's hop, an upward kick (units/s)
  lean: 2.2,             // and its lean, a roll kick (rad/s)
  flinch: 3.2,           // losing a man: a squash kick
  flinchTilt: 2.4,       // and a jerk back (rad/s)
  proud: 1.4,            // taking one: a little swell
  gotcha: 1.6,           // giving check: a lean in (rad/s)
  winHop: 6.2,           // the end: the winner jumps...
  winSpin: 1.0,          // ...and turns once round (turns)
  winScale: 0.16,        // ...and stays a size up
  loseSink: 0.42,        // the loser sinks...
  loseDroop: 0.5,        // ...and droops forward (rad)
  springK: 70,           // the bounce springs: stiffness
  springC: 9,            //                    and damping
  slowK: 18,             // the lift / sink / droop: slower and softer
  slowC: 8.5,
  face: 6,               // how fast a tag turns to face the camera (1/s)
  inward: 0.32,          // and how far it stays turned toward the board (rad), so its edge shows
  fadeIn: 0.45,          // s, the pop in when a game is dealt
  fadeOut: 0.3,          // s, the way out (menu, tags off)
  trembleBase: 0.016,    // board units of shake at one check
  trembleStep: 0.012,    // added per further check in a row
  trembleMax: 0.06,
  trembleHz: 23,
  sweatBase: 2.2,        // drops a second at one check
  sweatStep: 2.0,        // added per further check in a row
  sweatMax: 9,
  dropLife: 0.95,        // s a drop lasts
  gravity: 7.5,
});

const other = (s) => (s === 'w' ? 'b' : 'w');
const isSide = (s) => s === 'w' || s === 'b';
const SIDE_WORD = { w: 'White', b: 'Black' };

/** A name cut to TAG.nameMax characters, with an ellipsis when it had to be. Never empty. */
export function capName(name, max = TAG.nameMax) {
  const s = String(name == null ? '' : name).replace(/\s+/g, ' ').trim();
  if (!s) return '';
  const chars = Array.from(s);
  return chars.length <= max ? s : chars.slice(0, max - 1).join('').trimEnd() + '\u2026';
}

/**
 * A player in any of the shapes the page carries ({display_name, rating}, {name, rating},
 * a bare string, null) to { name, rating }. `fallback` names an unnamed seat.
 */
export function labelFor(player, fallback = '') {
  let name = '';
  let rating = null;
  if (typeof player === 'string') name = player;
  else if (player && typeof player === 'object') {
    name = player.name || player.display_name || '';
    const r = Number(player.rating);
    if (player.rating !== null && player.rating !== undefined && player.rating !== '' && Number.isFinite(r)) rating = Math.round(r);
  }
  return { name: capName(name) || capName(fallback) || '', rating };
}

/**
 * Both labels for a deal. `deal` is the bus's `local` payload; `players` (optional) is what a
 * later `players` / `watch-info` said ({w, b} or {white, black}); `own` is the player's own name.
 */
export function namesFor(deal = {}, { players = null, own = '' } = {}) {
  const d = deal || {};
  const pick = (side) => {
    if (players) {
      const p = players[side] ?? players[side === 'w' ? 'white' : 'black'];
      if (p) return p;
    }
    if (d.players && d.players[side]) return d.players[side];
    const m = d.match || null;
    if (m) {
      const seat = isSide(m.side) ? m.side : null;
      const named = m[side === 'w' ? 'white' : 'black'];
      if (named) return named;
      if (d.mode === 'online' && seat) return side === seat ? (own || 'You') : (m.opponent || null);
    }
    if (d.mode === 'solo' && Array.isArray(d.sides) && d.sides[0] === side) return own || 'You';
    return null;
  };
  return { w: labelFor(pick('w'), SIDE_WORD.w), b: labelFor(pick('b'), SIDE_WORD.b) };
}

/**
 * Where a side's tag floats: on that side's half, off the board to its player's left, the two
 * a half turn apart, so from either seat the near tag sits clear of the squares on the left and
 * the far one across on the right. On a screen held upright there is no room out at the sides,
 * so the tags tuck in behind the ends.
 */
export function anchorFor(side, aspect = 16 / 9) {
  const narrow = Number(aspect) < 0.9;
  const x = narrow ? -2.4 : -4.7;
  const z = narrow ? 5.65 : 1.5;
  const y = narrow ? 0.95 : TAG.floatY;
  const s = side === 'b' ? -1 : 1;
  return { x: x * s, y, z: z * s };
}

/** A fresh bookkeeping state. */
export function tagState() {
  return { ply: -1, toMove: 'w', run: { w: 0, b: 0 }, checked: null, over: null, dealt: false };
}

/** True while `side` stands in check: its move, and the last move against it was check. */
export function inCheck(state, side) {
  return !!state && !state.over && state.toMove === side && (state.run[side] || 0) > 0;
}

/**
 * The bus, folded. Hands back { state, beats } where every beat is { side, kind }:
 *   hop      that side just moved           flinch   it just lost a man
 *   proud    it just took one               gotcha   it just gave check
 *   win | lose | draw   the end             reset    a new deal, everything home
 * `p` is the event's payload; a non-sequential turn (a resync, a take-back, a deal) moves
 * nobody and asks `p.inCheck` (the referee's word, when the caller has it) for the check.
 */
export function reduce(state, type, p = {}) {
  const s = { ...state, run: { ...state.run } };
  const beats = [];
  const payload = p || {};
  switch (type) {
    case 'local':
    case 'newgame': {
      const fresh = tagState();
      fresh.dealt = true;
      fresh.toMove = 'w';
      return { state: fresh, beats: [{ side: 'w', kind: 'reset' }, { side: 'b', kind: 'reset' }] };
    }
    case 'capture': {
      if (isSide(payload.victimSide)) beats.push({ side: payload.victimSide, kind: 'flinch' });
      const by = isSide(payload.by) ? payload.by : (isSide(payload.victimSide) ? other(payload.victimSide) : null);
      if (by) beats.push({ side: by, kind: 'proud' });
      break;
    }
    case 'check': {
      if (!isSide(payload.side)) break;
      s.run[payload.side] = (s.run[payload.side] || 0) + 1;
      s.checked = payload.side;
      beats.push({ side: other(payload.side), kind: 'gotcha' });
      break;
    }
    case 'turn': {
      if (!isSide(payload.side)) break;
      const ply = Number.isFinite(payload.ply) ? payload.ply : s.ply + 1;
      if (ply === s.ply + 1 && s.ply >= 0) {
        beats.push({ side: other(payload.side), kind: 'hop' });
        // the side to move was not checked by that move: its run of checks is broken
        if (s.checked !== payload.side) s.run[payload.side] = 0;
      } else {
        s.run = { w: 0, b: 0 };
        if (payload.inCheck) s.run[payload.side] = 1;
      }
      s.ply = ply;
      s.toMove = payload.side;
      s.checked = null;
      s.over = null;
      break;
    }
    case 'gameover': {
      s.over = { result: payload.result || 'over', winner: isSide(payload.winner) ? payload.winner : null };
      s.checked = null;
      for (const side of ['w', 'b']) beats.push({ side, kind: endBeat(s.over.result, s.over.winner, side) });
      break;
    }
    default: break;
  }
  return { state: s, beats };
}

/** 'win' | 'lose' | 'draw' for `side` at the end of a game. */
export function endBeat(result, winner, side) {
  if (!isSide(winner)) return 'draw';
  return winner === side ? 'win' : 'lose';
}

/** One step of a damped spring toward `target`. Returns [x, v]. Stable for dt up to 0.1. */
export function spring(x, v, target, k, c, dt) {
  const steps = Math.max(1, Math.ceil((dt || 0) / 0.016));
  const h = (dt || 0) / steps;
  for (let i = 0; i < steps; i++) {
    const a = -k * (x - target) - c * v;
    v += a * h;
    x += v * h;
  }
  return [x, v];
}

/** Shake amplitude for `run` checks in a row (0 = none). */
export function trembleAmp(run) {
  const n = Math.max(0, Math.floor(Number(run) || 0));
  if (!n) return 0;
  return Math.min(TAG.trembleMax, TAG.trembleBase + TAG.trembleStep * (n - 1));
}

/** The shake at time `t` for `run` checks in a row: { x, y, rz }. Zero when calm. */
export function tremble(t, run) {
  const a = trembleAmp(run);
  if (!a) return { x: 0, y: 0, rz: 0 };
  const w = 2 * Math.PI * TAG.trembleHz;
  return {
    x: a * (Math.sin(w * t) * 0.6 + Math.sin(w * 1.71 * t + 1.3) * 0.4),
    y: a * 0.5 * Math.sin(w * 1.37 * t + 0.4),
    rz: a * 1.6 * (Math.sin(w * 0.83 * t + 2.1) * 0.7 + Math.sin(w * 2.11 * t) * 0.3),
  };
}

/** Sweat drops a second for `run` checks in a row (0 = dry). */
export function sweatRate(run) {
  const n = Math.max(0, Math.floor(Number(run) || 0));
  if (!n) return 0;
  return Math.min(TAG.sweatMax, TAG.sweatBase + TAG.sweatStep * (n - 1));
}

/** The idle bob at time `t` for a side (the two drift out of step). */
export function bob(t, side) {
  return TAG.bobAmp * Math.sin(2 * Math.PI * TAG.bobHz * t + (side === 'b' ? Math.PI * 0.7 : 0));
}

/** The calm colour cue, 0..1, for reduced motion: how much check tint a side wears. */
export function checkTint(run) {
  const n = Math.max(0, Math.floor(Number(run) || 0));
  return n ? Math.min(1, 0.55 + 0.15 * (n - 1)) : 0;
}
