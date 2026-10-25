/* The pendulum's position is shared by its visible chain and swept collision. */
import { S, BOOST, RESPAWN } from './state.js';
import { clamp, wrap } from '../core/math.js';
import { gateSweep, createKeepGates } from './keepGates.js';
import { machineryBeatAt, beatCycle, machineryRearming } from '../track/machineryBeat.js';
export const KEEP_CHAIN = Object.freeze({ cycleS: 6.4, pivotM: 15, lengthM: 12, angleRad: .72, radiusM: 2.5, stallS: .45 });
// A four-beat swing crosses the driving line on each alternating beat.
export function pendulumBeatPhase(row, cycleS, beat) {
  const period = 4;
  const offset = Math.round((row.offsetS || 0) / beat.beatS), safe = machineryRearming(beat, offset, period);
  const u = beatCycle(beat.beat + offset, period), rate = Math.PI * 2 / (period * beat.beatS);
  const elapsed = beat.beat - beatCycle(-offset, period), releaseBeats = Math.max(2, Math.ceil(3 / beat.beatS));
  const releasing = beat.rearming && elapsed >= 0 && elapsed < releaseBeats;
  // The initial pickup already follows the swing. Only a seek parks and lifts it.
  const parked = safe && beat.rearming;
  const f = clamp(elapsed / releaseBeats, 0, 1), lift = parked ? 5 : releasing ? 5 * (1 - f * f * (3 - 2 * f)) : 0;
  return { swing: parked ? 1 : Math.cos(u * Math.PI * 2 / period),
    speed: parked ? 0 : -Math.sin(u * Math.PI * 2 / period) * rate, lift,
    liftSpeed: releasing && !safe ? -30 * f * (1 - f) / (releaseBeats * beat.beatS) : 0, safe };
}
export function keepChainPhase(row, timeS, beat = null) {
  const cycleS = row.cycleS || KEEP_CHAIN.cycleS;
  if (beat) {
    const k = KEEP_CHAIN, p = pendulumBeatPhase(row, cycleS, beat), angle = p.swing * k.angleRad;
    return { angle, x: (row.x || 0) + Math.sin(angle) * k.lengthM, y: k.pivotM + p.lift - Math.cos(angle) * k.lengthM,
      pivotY: k.pivotM + p.lift, warning: p.safe || Math.abs(p.swing) < .7, radiusM: k.radiusM, safe: p.safe };
  }
  const k = KEEP_CHAIN, phase = (Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (row.offsetS || 0)) * Math.PI * 2 / cycleS;
  const angle = Math.sin(phase) * k.angleRad;
  return { angle, x: (row.x || 0) + Math.sin(angle) * k.lengthM, y: k.pivotM - Math.cos(angle) * k.lengthM,
    warning: Math.abs(Math.sin(phase)) < .7, radiusM: k.radiusM };
}
export function createKeepChains(track, { chains = [] } = {}) {
  const K = KEEP_CHAIN, rows = chains.map(row => {
    if (!Number.isFinite(row.s) || track.frameAt(row.s).w < 30) throw Error('Chain hall needs a 30 m deck');
    return { ...row, frame: track.frameAt(row.s) };
  });
  // The same ordinary steering used by gate bypasses holds a safe outer lane.
  const lanes = createKeepGates(track, { gates: rows.map(r => ({ ...r, w: 14 })) });
  let memory = new WeakMap(), hits = 0;
  function local(st, f) {
    const d = [st[S.posX] - f.p[0], st[S.posY] - f.p[1], st[S.posZ] - f.p[2]];
    return [f.b, f.t, f.n].map(axis => axis.reduce((v, a, i) => v + a * d[i], 0));
  }
  function apply(r, previous, timeS, events = r.events) {
    if (r.finished || r.kind === 'remote' || r.st[S.respawnPhase] !== RESPAWN.NONE || !Number.isFinite(timeS)) return null;
    const st = r.st, old = memory.get(r) || { timeS: timeS - 1 / 60, until: -1 };
    memory.set(r, { ...old, timeS });
    if (timeS <= old.timeS || timeS - old.timeS > .25 || timeS < old.until) return null;
    const distance = Math.hypot(...[0, 1, 2].map(j => st[S.posX + j] - previous[S.posX + j]));
    if (distance > 25) return null;
    for (const row of rows) {
      const ds = wrap(previous[S.s] - row.s + track.lengthM / 2, track.lengthM) - track.lengthM / 2;
      if (Math.abs(ds) > distance + K.radiusM + 3) continue;
      const a = local(previous, row.frame), b = local(st, row.frame);
      const pa = keepChainPhase(row, old.timeS, machineryBeatAt(track, old.timeS)), pb = keepChainPhase(row, timeS, machineryBeatAt(track, timeS));
      if (pa.safe || pb.safe) continue;
      const t = gateSweep([a[0] - pa.x, a[1]], [b[0] - pb.x, b[1]], K.radiusM + .65, K.radiusM + .65);
      if (t === null) continue;
      const h = a[2] + (b[2] - a[2]) * t, y = pa.y + (pb.y - pa.y) * t;
      if (h + 1.6 < y - K.radiusM || h > y + K.radiusM) continue;
      // A bounded hit slows the kart without pinning it underneath the swinging weight.
      for (let j = 0; j < 3; j++) st[S.velX + j] *= .22;
      st[S.engineV] *= .22; st[S.stalled] = Math.max(st[S.stalled], K.stallS);
      st[S.boostKind] = BOOST.NONE; st[S.boostT] = 0; st[S.boostAmount] = 0; st[S.boostHoldS] = 0;
      st[S.driftDir] = 0; st[S.driftCharge] = 0; st[S.driftStage] = 0;
      memory.set(r, { timeS, until: timeS + 1.2 }); hits++;
      events?.emit('keep-chain-hit', { id: row.id, stallS: K.stallS }); return row.id;
    }
    return null;
  }
  return { rows, apply, steer: lanes.steer, reset() { memory = new WeakMap(); hits = 0; lanes.reset(); },
    inspect(timeS = 0) { return { hits, chains: rows.map(r => ({ id: r.id, s: r.s, ...keepChainPhase(r, timeS, machineryBeatAt(track,timeS)) })) }; } };
}
