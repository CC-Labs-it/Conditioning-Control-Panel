/* net/snapshot.js - the client's uploads (NET.md section 3): its own kart plus every bot seat it keeps, in one
 * UPLOAD frame at 20 Hz (15 Hz on phones). Sim events become pulse bits that are ORed until the next record of
 * that seat; a trick holds its pose for POSE_MS. Pure: the caller passes ms and ticks.
 */
import { racerFork } from './trackPath.js';
import { createKart, kartFromState, PULSE, RATES } from './protocol.js';

const POSE_MS = 700;
const GRADE_POSE = { good: 1, great: 2 };

/** Pulse bits of one sim event (name, payload). */
export function pulseOf(name, p = {}) {
  switch (name) {
    case 'hop': return PULSE.HOP;
    case 'land': return (p.tier | 0) >= 2 ? PULSE.LAND_HEAVY : PULSE.LAND;
    case 'boost-start': return PULSE.BOOST;
    case 'airborne': return p.cause === 'lip' ? PULSE.LIP : 0;
    case 'trick': return PULSE.TRICK;
    case 'word-pop': return PULSE.WORD;
    case 'bubble-pop': return PULSE.BUBBLE;
    default: return 0;
  }
}

export function createUplink({ phone = false, track = null } = {}) {
  const per = new Map();   // seat -> { seq, pulses, pose, poseUntil, pops, k }
  const gapMs = 1000 / (phone ? RATES.upHzPhone : RATES.upHz);
  let lastAt = -Infinity;
  const of = (seat) => {
    let e = per.get(seat);
    if (!e) per.set(seat, (e = { seq: 0, pulses: 0, pose: 0, poseUntil: 0, pops: 0, k: createKart() }));
    return e;
  };

  return {
    gapMs,
    /** True when an upload is due at ms (then counts it as sent). */
    due(ms) { if (ms - lastAt < gapMs - 0.5) return false; lastAt = ms; return true; },
    /** Note a sim event of a seat this client uploads. */
    event(seat, name, p, ms = 0) {
      const e = of(seat);
      e.pulses |= pulseOf(name, p);
      if (name === 'trick' && GRADE_POSE[p && p.grade]) { e.pose = GRADE_POSE[p.grade]; e.poseUntil = ms + POSE_MS; }
    },
    /** A preset emote pose (16+) for a while. */
    pose(seat, id, ms, holdMs = 1500) { const e = of(seat); e.pose = id; e.poseUntil = ms + holdMs; },
    setPops(seat, n) {
      if (!Number.isFinite(n)) return;
      const e = of(seat);
      e.pops = Math.max(e.pops, Math.min(65535, Math.max(0, Math.floor(n))));
    },
    /** Continue a seat's seq after a record (the keeper takes a bot over). */
    seqFrom(seat, seq) { of(seat).seq = (seq + 1) & 0xffff; },
    seqOf: (seat) => of(seat).seq,
    /**
     * One wire kart per uploaded seat. rows: [{ seat, r, bot, paused, assist }] where r is a racer.
     * @returns wire karts (pooled per seat: send them before the next build)
     */
    build(rows, tick, ms) {
      const out = [];
      for (const row of rows) {
        const e = of(row.seat), r = row.r, fxv = r.fxv || {}, it = r.item || {};
        if (e.poseUntil && ms >= e.poseUntil) { e.pose = 0; e.poseUntil = 0; }
        kartFromState(e.k, r.st, {
          tick, seq: e.seq, seat: row.seat, paused: row.paused, bot: row.bot, finished: r.finished, assist: row.assist,
          item: it.id || null, rolling: !!it.rolling, effect: fxv.effect || null, shield: fxv.shield || (fxv.echo > 0 ? 'echo' : null),
          echo: fxv.echo || 0, pose: e.pose, pulses: e.pulses, pops: e.pops,
        });
        e.k.fork = track ? racerFork(track, r.st) : 0;
        e.seq = (e.seq + 1) & 0xffff; e.pulses = 0;
        out.push(e.k);
      }
      return out;
    },
    forget(seat) { per.delete(seat); },
  };
}
