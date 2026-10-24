/* Physical Boudoir traps. The caller supplies one race clock and resolves before progress. */
import { S, BOOST, RESPAWN, cloneState } from './state.js';
import { clamp, wrap } from '../core/math.js';
import { gateSweep, createKeepGates } from './keepGates.js';
import { makeQuery } from '../track/query.js';
import { BOUDOIR_HAZARD as K, getBoudoirSetpieces, powderPressPhase, mascaraHeads, mascaraPhase, ribbonPhase, mirrorGatePhase } from '../track/boudoir-setpieces.js';
const RADIUS = .7, HEIGHT = 1.6;
const dot = (a, b) => a.reduce((sum, v, i) => sum + v * b[i], 0);
const dist = (a, b) => Math.hypot(...[0, 1, 2].map(i => a[S.posX + i] - b[S.posX + i]));
function local(st, f) { const d = [0, 1, 2].map(i => st[S.posX + i] - f.p[i]); return [dot(d, f.b), dot(d, f.t), dot(d, f.n)]; }
function clearBoost(st) { st[S.boostKind] = BOOST.NONE; st[S.boostT] = st[S.boostAmount] = st[S.boostHoldS] = 0; st[S.driftDir] = st[S.driftCharge] = st[S.driftStage] = 0; }
export function createBoudoirHazards(track, layout = null) {
  const plan = layout?.presses ? layout : getBoudoirSetpieces(track, layout || undefined);
  const rows = [...plan.presses, ...plan.sweepers.flatMap(mascaraHeads), ...plan.mirrorGates].map(row => ({ ...row, frame: track.frameAt(row.s) }));
  const unique = [...plan.presses, ...plan.sweepers, ...plan.mirrorGates.filter(row => row.lane === 1)].map(row => ({ ...row, x: 0, w: 14 }));
  const lanes = createKeepGates(track, { gates: unique });
  let memory = new WeakMap(), hits = 0, ribbonPasses = 0;
  const query = makeQuery();
  function mem(r) { let m = memory.get(r); if (!m) { m = { time: null, after: null, cooldown: new Map(), ribbon: null }; memory.set(r, m); } return m; }
  function sweep(r, a, b, ta, tb, m, events) {
    const st = r.st, travel = dist(a, b); if (travel > 25) return null;
    for (const row of rows) {
      const delta = wrap(a[S.s] - row.s + track.lengthM / 2, track.lengthM) - track.lengthM / 2;
      if (Math.abs(delta) > travel + row.depthM / 2 + Math.abs(row.longitudinalM || 0) + 4) continue;
      const A = local(a, row.frame), B = local(b, row.frame), brush = row.kind === 'sweeper';
      const pa = brush ? mascaraPhase(row, ta) : null, pb = brush ? mascaraPhase(row, tb) : null;
      const fraction = gateSweep([A[0] - (brush ? pa.x : row.x), A[1] - (brush ? pa.z : 0)], [B[0] - (brush ? pb.x : row.x), B[1] - (brush ? pb.z : 0)],
        (brush ? pa.radiusM : row.w / 2) + RADIUS, row.depthM / 2 + RADIUS);
      if (fraction === null) continue;
      const time = ta + (tb - ta) * fraction, h = A[2] + (B[2] - A[2]) * fraction;
      const phase = brush ? mascaraPhase(row, time) : row.kind === 'mirror' ? mirrorGatePhase(row, time) : powderPressPhase(row, time);
      const low = brush ? phase.y - phase.radiusM : phase.liftM, high = low + (brush ? phase.radiusM * 2 : row.kind === 'mirror' ? K.mirrorHeightM : K.pressHeightM);
      if (h + HEIGHT < low || h > high || h < -1.5) continue;
      const contactId = row.stationId || row.id, fresh = time >= (m.cooldown.get(contactId) ?? -Infinity);
      if (brush && !fresh) continue;
      const along = [st[S.velX], st[S.velY], st[S.velZ]], f = row.frame;
      if (brush) {
        const forward = dot(along, f.t), sideways = dot(along, f.b), push = Math.sign(phase.vx || A[0] - phase.x || 1) * K.brushImpulseMps;
        for (let j = 0; j < 3; j++) st[S.velX + j] += f.t[j] * (forward * -.32) + f.b[j] * (push - sideways);
        st[S.engineV] *= .68;
      } else {
        const k = Math.max(0, fraction - .03 / Math.max(travel, .03));
        for (let j = 0; j < 3; j++) st[S.posX + j] = a[S.posX + j] + (b[S.posX + j] - a[S.posX + j]) * k;
        const forward = dot(along, f.t), rebound = forward >= 0 ? -1.5 : 1.5;
        for (let j = 0; j < 3; j++) st[S.velX + j] += f.t[j] * (rebound - forward);
        st[S.engineV] = 0;
        track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0, query);
        st[S.seg] = query.seg; st[S.segT] = query.segT; st[S.s] = query.s; st[S.lateral] = query.lateral; st[S.height] = query.height;
      }
      clearBoost(st);
      if (fresh) {
        const stallS = brush ? .25 : row.kind === 'mirror' ? .22 : .35;
        st[S.stalled] = Math.max(st[S.stalled], stallS); m.cooldown.set(contactId, time + 1.1); hits++;
        events?.emit(brush ? 'boudoir-sweeper-hit' : row.kind === 'mirror' ? 'boudoir-mirror-hit' : 'boudoir-press-hit',
          { id: contactId, headId: brush ? row.id : undefined, s: row.s, x: brush ? phase.x : row.x, z: brush ? phase.z : 0, stallS, impulseMps: brush ? K.brushImpulseMps : 0 });
      }
      return contactId;
    }
    return null;
  }
  function conveyor(r, timeS, dt, m, events) {
    const st = r.st, row = plan.conveyors.find(z => st[S.s] >= z.s0 && st[S.s] <= z.s1 && Math.abs(st[S.lateral] - z.x) <= z.w / 2 - .4);
    if (!row || !st[S.grounded] || st[S.airborne]) { m.ribbon = null; return; }
    const p = ribbonPhase(row, timeS), key = row.id + ':' + p.phase;
    if (p.direction) {
      const frame = track.frameAt(st[S.s]), velocity = [st[S.velX], st[S.velY], st[S.velZ]], speed = dot(velocity, frame.t);
      const target = p.direction > 0 ? Math.max(speed, K.ribbonMaxMps) : Math.min(speed, 16);
      const change = clamp(target - speed, -K.ribbonDragMps2 * dt, K.ribbonAccelMps2 * dt);
      for (let i = 0; i < 3; i++) st[S.velX + i] += frame.t[i] * change;
      st[S.engineV] = Math.max(0, st[S.engineV] + change);
      if (m.ribbon !== key) { ribbonPasses++; events?.emit('boudoir-ribbon', { id: row.id, direction: p.direction, s: st[S.s] }); }
    }
    m.ribbon = key;
  }
  function apply(r, previous, timeS, events = r?.events) {
    if (!r || r.finished || r.kind === 'remote' || !Number.isFinite(timeS)) return null;
    const st = r.st, m = mem(r), oldTime = m.time ?? Math.max(0, timeS - 1 / 60), dt = timeS - oldTime;
    m.time = timeS;
    if (st[S.respawnPhase] !== RESPAWN.NONE || dt < 0 || dt > .25 || dist(previous, st) > 25) { m.after = null; m.cooldown.clear(); m.ribbon = null; return null; }
    if (dt === 0) return null;
    const hit = (m.after && sweep(r, m.after, previous, oldTime, oldTime, m, events)) || sweep(r, previous, st, oldTime, timeS, m, events);
    if (!hit) conveyor(r, timeS, dt, m, events);
    if (m.after) m.after.set(st); else m.after = cloneState(st);
    return hit;
  }
  return { plan, rows, apply, steer: lanes.steer,
    reset() { memory = new WeakMap(); hits = ribbonPasses = 0; lanes.reset(); },
    inspect(timeS = 0) { return { hits, ribbonPasses,
      presses: plan.presses.map(row => ({ ...row, ...powderPressPhase(row, timeS) })),
      sweepers: plan.sweepers.map(row => ({ ...row, heads: mascaraHeads(row).map(head => ({ ...head, ...mascaraPhase(head, timeS) })) })),
      conveyors: plan.conveyors.map(row => ({ ...row, ...ribbonPhase(row, timeS) })),
      mirrorGates: plan.mirrorGates.map(row => ({ ...row, ...mirrorGatePhase(row, timeS) })) }; },
  };
}
