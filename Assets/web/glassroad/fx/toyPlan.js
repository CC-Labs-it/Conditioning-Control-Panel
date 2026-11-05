/* fx/toyPlan.js - how a race feels on a connected toy (owner 2026-10-31: the toy is the scoreboard). Pure.
 *
 * A steady buzz (the bed) builds while you race clean, moments surge over it, and a hit cuts it to silence. The numbers
 * live here, never in feel.json: any feel.json edit moves the course hash. Levels are a share of the player's own limit,
 * and the host scales them again (desktop: the Racing Thoughts row; phone: 60%). A Lovense command is one flat level and
 * Buttplug lags about a second, so these are long pulses at about three a second, never a mirror of pad rumble. */
import { bumpFeel, TRAP_EVENTS } from './cues.js';

export const TOY_BED = Object.freeze({ floor: .1, span: .35, finalLift: .15, speedShare: .7, streakShare: .3, riseS: 6, fallS: 2,
  streakPerWord: .2, streakFadePerS: .05, wordCharge: .06, hitCharge: .5, trapCharge: .35 });
export const TOY_SEND = Object.freeze({ bedGapMs: 333, bedStep: .03, bedRefreshMs: 500, pulseGapMs: 300, sameBlowMs: 200 });
export const TOY_SILENCE = Object.freeze({ hitS: 1.5, trapS: 1 });

const TRAPS = new Set(TRAP_EVENTS);
const DRIFT = Object.freeze({ 1: [.5, 600], 2: [.7, 1000], 3: [.9, 1400] });
const BOOSTS = Object.freeze({ pad: [.6, 800], launch: [.6, 800], trick: [.7, 900], surge: [.7, 900], bubble: [.4, 500], slip: [.4, 500] });
const pulse = (level, ms, tag, extra) => ({ level, ms, tag, ...extra });

/** The finish by place: first, the podium, the rest (place 0 = unknown reads as the rest). */
export function finishPulse(place) {
  if (place === 1) return pulse(.9, 3000, 'finish');
  if (place === 2 || place === 3) return pulse(.6, 2000, 'finish');
  return pulse(.4, 1000, 'finish');
}

/** One bus event to one pulse, or null. Only the player's own events: racer 0, as fx/cues.js reads them.
 *  silenceS: the bed and later pulses stop for that long once the pulse ends. charge: what it takes off the bed. */
export function toyPulse(name, p = {}, { bumpMinDv = 3, place = 0 } = {}) {
  if (TRAPS.has(name)) return (p.racer ?? 0) === 0 ? pulse(.8, 350, 'trap', { silenceS: TOY_SILENCE.trapS, charge: TOY_BED.trapCharge }) : null;
  switch (name) {
    case 'boost-start': {
      const row = p.kind === 'drift' ? DRIFT[Math.min(3, Math.max(1, p.tier | 0))] : BOOSTS[p.kind];
      return row ? pulse(row[0], row[1], 'boost') : null;
    }
    case 'word-pop': return p.lineDone ? pulse(.8, 600, 'line') : pulse(.45, 250, 'word');
    case 'item-hit':
      if (p.victim === 0 && p.verdict === 'hit') return pulse(.9, 400, 'hit', { silenceS: TOY_SILENCE.hitS, charge: TOY_BED.hitCharge });
      if (p.victim === 0 && (p.verdict === 'glass' || p.verdict === 'golden')) return pulse(.3, 200, 'blocked');
      return p.by === 0 && p.victim !== 0 && p.verdict === 'hit' ? pulse(.6, 300, 'landed') : null;
    case 'hitter-hit': return p.racer === 0 ? pulse(.9, 400, 'hit', { silenceS: TOY_SILENCE.hitS, charge: TOY_BED.hitCharge }) : null;
    case 'bump': return (p.a === 0 || p.b === 0) && bumpFeel(p.dv, bumpMinDv) ? pulse(.3, 200, 'bump') : null;
    case 'wall': return (Number(p.dv) || 0) >= bumpMinDv ? pulse(.3, 200, 'wall') : null;
    case 'land': return !p.airWord && (p.tier | 0) >= 2 ? pulse(.5, 300, 'land') : null;
    case 'finish': return finishPulse(place);
    default: return null;
  }
}

/** The bed's level from charge 0..1. final = the last lap lifts it. */
export function bedLevel(charge, final = false) {
  const c = Math.min(1, Math.max(0, Number(charge) || 0));
  return TOY_BED.floor + TOY_BED.span * c + (final ? TOY_BED.finalLift : 0);
}

/** Moves charge toward what the kart is doing: slowly up, faster down. */
export function chargeStep(charge, speedShare, streak, dt) {
  const target = Math.min(1, Math.max(0, speedShare)) * TOY_BED.speedShare + Math.min(1, Math.max(0, streak)) * TOY_BED.streakShare;
  const tau = target > charge ? TOY_BED.riseS : TOY_BED.fallS;
  return charge + (target - charge) * Math.min(1, dt / tau);
}
