/* net/interp.js - remote karts drawn in the past (NET.md section 3, client render rule).
 *
 * Records arrive per seat with the sender's tick; each is stamped with the server ms of that tick. A remote kart
 * is drawn RATES.renderDelayMs (100) in the past: between the two records around that time, in track space
 * (s wrap aware, lateral, height) so it follows the bends, quat slerped, velocity lerped. Past the newest record
 * it dead-reckons along s, lateral and height at the last rates for at most deadReckonMs (250), then holds.
 * When a new record moves the target at the drawn time (it replaces a dead reckoning), the difference is blended
 * out over blendMs (150); a jump over SNAP_M (a rescue) snaps. A record that does not change the kart at the drawn
 * time changes nothing on screen. Output is a full sim KartState. Pure, three-free.
 */
import { pathFrame } from './trackPath.js';
import { S, createState } from '../sim/state.js';
import { wrap, qslerp, qnorm } from '../core/math.js';
import { RATES, seqNewer, createKart } from './protocol.js';

const KEEP = 24, SNAP_M = 20, SNAP_X = 6;

export function createInterp(track, { delayMs = RATES.renderDelayMs, deadMs = RATES.deadReckonMs, blendMs = RATES.blendMs } = {}) {
  const L = track.lengthM, seats = new Map();
  const ds = (a, b) => wrap(b - a + L / 2, L) - L / 2;          // signed b - a along the loop
  const fr = track.frameAt(0, { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
  const qa = [0, 0, 0, 1], qb = [0, 0, 0, 1], qo = [0, 0, 0, 1];
  const raw = { s: 0, x: 0, h: 0 };

  function slot(seat) {
    let e = seats.get(seat);
    if (!e) seats.set(seat, (e = { buf: [], err: { s: 0, x: 0, h: 0 }, errAt: 0, seenSeq: -1, hold: false }));
    return e;
  }

  /** Store one decoded record (copied) stamped at server ms `ms`. Older or repeated seqs are dropped. */
  function push(k, ms) {
    const e = slot(k.seat), last = e.buf[e.buf.length - 1];
    if (last && !seqNewer(k.seq, last.k.seq)) return false;
    const c = Object.assign(createKart(), k, { q: k.q.slice(), v: k.v.slice() });
    e.buf.push({ k: c, ms: last ? Math.max(ms, last.ms) : ms });
    if (e.buf.length > KEEP) e.buf.shift();
    return true;
  }

  /** Newest record of a seat (the bot keeper seeds from it). */
  const newest = (seat) => { const e = seats.get(seat); return e && e.buf.length ? e.buf[e.buf.length - 1] : null; };

  /** Track-space target at render ms t from the first n records into raw; @returns the record that rules the discrete fields. */
  function target(e, t, n = e.buf.length) {
    const b = e.buf, top = b[n - 1];
    if (n === 1 || t <= b[0].ms) {
      const k = n === 1 ? top.k : b[0].k;
      raw.s = k.s; raw.x = k.x; raw.h = k.h; qnorm(qo, k.q);
      return k;
    }
    if (t >= top.ms) {
      const p = b[n - 2], dtS = (top.ms - p.ms) / 1000, d = ds(p.k.s, top.k.s);
      const ok = !e.hold && dtS > 1e-3 && Math.abs(d) < SNAP_M;
      const aheadS = ok ? Math.min(t - top.ms, deadMs) / 1000 : 0, inv = ok ? 1 / dtS : 0;
      raw.s = wrap(top.k.s + d * inv * aheadS, L);
      raw.x = top.k.x + (top.k.x - p.k.x) * inv * aheadS;
      raw.h = top.k.h + (top.k.h - p.k.h) * inv * aheadS;
      qnorm(qo, top.k.q);
      return top.k;
    }
    let i = n - 1;
    while (i > 0 && b[i - 1].ms > t) i--;
    const A = b[i - 1], B = b[i], a = (t - A.ms) / Math.max(1e-6, B.ms - A.ms), d = ds(A.k.s, B.k.s);
    if (Math.abs(d) > SNAP_M) { raw.s = B.k.s; raw.x = B.k.x; raw.h = B.k.h; qnorm(qo, B.k.q); return B.k; }
    raw.s = wrap(A.k.s + d * a, L); raw.x = A.k.x + (B.k.x - A.k.x) * a; raw.h = A.k.h + (B.k.h - A.k.h) * a;
    qslerp(qo, qnorm(qa, A.k.q), qnorm(qb, B.k.q), a);
    return a < 0.5 ? A.k : B.k;
  }

  const blendAt = (e, t) => Math.max(0, Math.min(1, 1 - (t - e.errAt) / blendMs));

  /** New records landed: blend from where the kart would be at THIS drawn time without them, so nothing moves unless
   * they changed it. (Against the last drawn pose instead, every record cost the kart one frame of travel: 40 m/s is
   * 67 cm a frame, 15 to 20 times a second.) */
  function rebase(e, t) {
    const b = e.buf;
    let n = b.length - 1;
    while (n > 0 && b[n - 1].k.seq !== e.seenSeq) n--;
    e.seenSeq = b[b.length - 1].k.seq;
    if (n === 0) return;                                        // first record, or the old ones have left the buffer
    const was = target(e, t, n), f = blendAt(e, t);
    const os = wrap(raw.s + e.err.s * f, L), ox = raw.x + e.err.x * f, oh = raw.h + e.err.h * f, ofork = was.fork || 0;
    const k = target(e, t);
    const es = ds(raw.s, os), ex = ox - raw.x, eh = oh - raw.h;
    const big = Math.abs(es) > SNAP_M || Math.abs(ex) > SNAP_X || ofork !== (k.fork || 0);
    e.err.s = big ? 0 : es; e.err.x = big ? 0 : ex; e.err.h = big ? 0 : eh; e.errAt = t;
  }

  /**
   * Write seat's kart at server ms `nowMs` (drawn at nowMs - delay) into the KartState `out`.
   * @returns the wire record whose discrete fields were used, or null when the seat has none yet
   */
  function sample(seat, nowMs, out = createState()) {
    const e = seats.get(seat);
    if (!e || !e.buf.length) return null;
    const t = nowMs - delayMs;
    if (e.buf[e.buf.length - 1].k.seq !== e.seenSeq) rebase(e, t);
    const k = target(e, t), f = blendAt(e, t);
    const s = wrap(raw.s + e.err.s * f, L), x = raw.x + e.err.x * f, h = raw.h + e.err.h * f;
    writeState(out, k, s, x, h);
    return k;
  }

  function writeState(out, k, s, x, h) {
    const F = pathFrame(track, k.fork, s, fr);
    for (let i = 0; i < 3; i++) out[S.posX + i] = F.p[i] + F.b[i] * x + F.n[i] * h;
    out[S.quatX] = qo[0]; out[S.quatY] = qo[1]; out[S.quatZ] = qo[2]; out[S.quatW] = qo[3];
    out[S.velX] = k.v[0]; out[S.velY] = k.v[1]; out[S.velZ] = k.v[2];
    out[S.engineV] = Math.hypot(k.v[0], k.v[1], k.v[2]);
    out[S.s] = s; out[S.lateral] = x; out[S.height] = h;
    if (F.seg !== undefined) { out[S.seg] = F.seg; out[S.segT] = F.segT; }
    out[S.sectionIdx] = track.sectionAt(s);
    out[S.lap] = k.lap; out[S.checkpoint] = k.checkpoint; out[S.respawnPhase] = k.respawn;
    out[S.stalled] = k.stalled ? 1 : 0; out[S.speedPct] = k.speedPct;
    out[S.driftDir] = k.driftDir; out[S.driftStage] = k.driftStage; out[S.boostKind] = k.boostKind;
    out[S.airborne] = k.airborne ? 1 : 0; out[S.grounded] = k.grounded ? 1 : 0;
    return out;
  }

  /** The newest record of a seat as a KartState, no delay (seeding a bot the keeper takes over). */
  function seed(seat, out = createState()) {
    const n = newest(seat);
    if (!n) return null;
    qnorm(qo, n.k.q);
    return writeState(out, n.k, n.k.s, n.k.x, n.k.h);
  }

  /** OR of the pulses of the records now behind the drawn time, each record counted once. */
  function takePulses(seat, nowMs) {
    const e = seats.get(seat), t = nowMs - delayMs;
    let p = 0;
    if (e) for (const x of e.buf) if (!x.used && x.ms <= t) { p |= x.k.pulses; x.used = true; }
    return p;
  }

  return {
    push, sample, newest, seed, takePulses,
    /** Hold a seat still (idle: no dead reckoning). */
    hold(seat, on) { slot(seat).hold = !!on; },
    drop(seat) { seats.delete(seat); },
    clear() { seats.clear(); },
    has: (seat) => !!(seats.get(seat) && seats.get(seat).buf.length),
  };
}
