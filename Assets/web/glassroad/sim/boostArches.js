import { S, RESPAWN } from './state.js';
import { grantBoost } from './grant.js';
import { boostArchPlan } from '../track/boostArches.js';

/** World-space swept crossing rejects reversing, teleports, forks and separate decks. */
export function archCrossed(track, arch, previous, st) {
  if (!previous || !st[S.grounded] || !previous.grounded || track.forkAtSeg?.(st[S.seg]) || previous.fork
    || st[S.respawnPhase] !== RESPAWN.NONE && st[S.respawnPhase] !== RESPAWN.GRACE) return false;
  const f = track.frameAt(arch.s), now = [st[S.posX], st[S.posY], st[S.posZ]];
  const delta = now.map((v, i) => v - previous.p[i]);
  if (Math.hypot(...delta) > 12 || delta.reduce((n, v, i) => n + v * f.t[i], 0) <= 0) return false;
  const project = (p, axis) => p.reduce((n, v, i) => n + (v - f.p[i]) * axis[i], 0);
  const before = project(previous.p, f.t), after = project(now, f.t);
  if (!(before < 0 && after >= 0)) return false;
  const k = -before / (after - before), at = now.map((v, i) => previous.p[i] + delta[i] * k);
  return Math.abs(project(at, f.b) - arch.x) <= arch.halfWidth - .5 && Math.abs(project(at, f.n)) <= 1.5;
}

export function createBoostArchRun(track, feel, events) {
  const plan = boostArchPlan(track), previous = new Map(), used = new Map();
  const rows = plan.arches.map(a => ({ ...a, pulse: 0 }));
  if (!rows.length) return { update() {}, rows, reset() {} };
  function update(dt, racers) {
    for (const a of rows) a.pulse = Math.max(0, a.pulse - dt);
    for (const r of racers) {
      const st = r.st, prev = previous.get(r.i);
      if (!r.finished) for (const a of rows) {
        const gap = Math.abs(st[S.s] - a.s);
        if (Math.min(gap, track.lengthM - gap) > 14) continue;
        const key = r.i + ':' + a.id, lap = st[S.lap];
        if (used.get(key) === lap || !archCrossed(track, a, prev, st)) continue;
        used.set(key, lap); a.pulse = .7;
        const sink = r.i === 0 ? events : { emit: (name, p) => events.emit('racer-event', { racer: r.i, name, p }) };
        grantBoost(st, feel.get('items.bubbleAmount'), feel.get('items.bubbleHoldS'), 'bubble', feel, sink);
        const f = track.frameAt(a.s), p = f.p.map((v, i) => v + f.b[i] * a.x);
        events.emit('boost-arch', { racer: r.i, i: a.id, p });
        events.emit('bubble-pop', { racer: r.i, i: -1 - a.id, p });
      }
      previous.set(r.i, { p: [st[S.posX], st[S.posY], st[S.posZ]], grounded: !!st[S.grounded], fork: !!track.forkAtSeg?.(st[S.seg]) });
    }
  }
  return { update, rows, reset() { previous.clear(); used.clear(); for (const a of rows) a.pulse = 0; } };
}

/** Time trials keep route rewards while every held/throwable item stays disabled. */
export function createArchOnlyItems(track, feel, events) {
  const run = createBoostArchRun(track, feel, events);
  const view = { boxes: [], bubbles: [], arches: run.rows, held: [], projectiles: [], traps: [], waves: [], shields: [], pops: [], tethers: [], guards: [] };
  return { update(dt, racers, standings, race) { if (race?.phase === 'racing') run.update(dt, racers); },
    view: () => view, reset: run.reset, nearOpenEdge: () => false, trapRows: () => [], lobTarget: () => -1 };
}
