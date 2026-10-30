/* net/clock.js - server time on the client (NET.md section 2). Pure: the caller passes its own ms clock.
 *
 * ping { c } -> pong { c, s }: rtt = recv - c, offset = s - (c + rtt / 2). The offset of the sample with the
 * smallest rtt of the last 8 wins. A new best within 250 ms of the current offset is slewed at 20 ms per second
 * of wall time (no jump in a remote kart or the song); a bigger change (first sync, a sleeping tab) is adopted at
 * once. Ping plan: one at hello, then 1 s apart for 5 samples, then every 15 s.
 */
import { TICK_HZ, RATES } from './protocol.js';

const SLEW_MS_PER_S = 20, JUMP_MS = 250;

export function createServerClock({ now = () => Date.now() } = {}) {
  const samples = [];
  let offset = 0, target = 0, synced = false, lastT = now(), t0 = 0, sent = 0, lastPingAt = -Infinity;

  /** Fold a pong in. recvMs defaults to now(). @returns rtt */
  function pong(c, s, recvMs = now()) {
    const rtt = Math.max(0, recvMs - c);
    samples.push({ rtt, off: s - (c + rtt / 2) });
    if (samples.length > RATES.pingSamples) samples.shift();
    let best = samples[0];
    for (const x of samples) if (x.rtt < best.rtt) best = x;
    target = best.off;
    if (!synced || Math.abs(target - offset) > JUMP_MS) { offset = target; synced = true; }
    lastT = recvMs;
    return rtt;
  }

  /** Move the offset toward the best sample (call once per frame). */
  function slew(t = now()) {
    const step = Math.max(0, t - lastT) / 1000 * SLEW_MS_PER_S;
    lastT = t;
    offset = target > offset ? Math.min(target, offset + step) : Math.max(target, offset - step);
  }

  /** True when a ping is due at t (then counts it as sent). */
  function pingDue(t = now()) {
    const gap = sent === 0 ? 0 : sent < 6 ? 1000 : 15000;
    if (t - lastPingAt < gap) return false;
    lastPingAt = t; sent++;
    return true;
  }

  const serverNow = (t = now()) => t + offset;
  const roomMs = (t = now()) => serverNow(t) - t0;
  const tick = (t = now()) => Math.max(0, Math.floor(roomMs(t) * TICK_HZ / 1000));
  /** Server ms of a room tick. */
  const tickMs = (k) => t0 + k * 1000 / TICK_HZ;
  const medianRtt = () => {
    if (samples.length < 3) return 999;
    const r = samples.map((x) => x.rtt).sort((a, b) => a - b);
    return r[r.length >> 1];
  };

  return {
    pong, slew, pingDue, serverNow, roomMs, tick, tickMs, medianRtt,
    /** Adopt the room's t0, and a first offset from welcome.serverMs (rough until a pong lands). */
    setRoom(roomT0, serverMs, recvMs = now()) {
      t0 = roomT0;
      if (!synced && Number.isFinite(serverMs)) { offset = target = serverMs - recvMs; lastT = recvMs; }
    },
    /** A new socket starts the ping plan over (samples are kept). */
    restartPings() { sent = 0; lastPingAt = -Infinity; },
    get offset() { return offset; }, get synced() { return synced; }, get t0() { return t0; }, get samples() { return samples.length; },
  };
}
