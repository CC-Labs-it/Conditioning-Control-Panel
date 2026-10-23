/* sim/respawn.js - the rescue timeline after a drop (plan section 6, CONTRACT.md section 4).
 *
 * Anchor: node latchSeg (latched at TAKE-OFF), or the first landing node when that node is a jump lip
 * or inside a gap; never inside a vertical loop (the node before its magnet run), never over a court hole
 * (sim/court-paths.js) and never inside a stone field (the last node before it, sim/stones.js). The kart faces respawn.aheadNodes
 * nodes further on. PULL draws it to a hover point
 * above the anchor, arriving exactly at pullS; HOVER holds; RELEASE lowers it to the road and lets it
 * go at releasePct x top; GRACE (no drop, no wall cancel) runs while kart.step drives again.
 */
import { S, RESPAWN, getPos, getQuat, setPos, setQuat, setVel } from './state.js';
import { v3, q4, v3sub, v3norm, v3reject, v3addScaled, v3lerp, v3scale, qslerp, qlook, smoothstep } from '../core/math.js';
import { gap } from './gaps.js';
import { courtRescueNode } from './court-paths.js';
import { fieldRescueNode } from './stones.js';

const fOut = () => ({ p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
const A = fOut(), B = fOut(), F = v3(), H = v3(), P = v3(), Q = q4(), QC = q4(), V = v3(), ZERO = [0, 0, 0];

/** Anchor node index for the current latch. */
export function anchorNode(st, track, aheadNodes = 0) {
  const n = track.nodeCount;
  let i = (((st[S.latchSeg] | 0) % n) + n) % n;
  const s = track.nodeS[i];
  for (const j of track.jumps || []) {
    if (s >= j.s - 1e-6 && s < j.landS) {
      let k = 0;
      while (k < n && track.nodeS[k] < j.landS - 1e-6) k++;
      i = k % n;
      break;
    }
  }
  // polish 6: never inside a vertical loop (magnet run): back to the last node before the run, on the entry straight
  const mag = (k) => { const f = track.nodeFlags ? track.nodeFlags(k) : null; return !!f && f.indexOf('magnet') >= 0; };
  for (let k = 0; k < n && mag(i); k++) i = (i - 1 + n) % n;
  // wave 2 STONES: never inside a stone field either (the last deck node before it: retry the stones)
  return fieldRescueNode(track, courtRescueNode(track, i), aheadNodes);
}

export function updateRespawn(st, track, feel, dt, events) {
  const ph = st[S.respawnPhase];
  if (ph === RESPAWN.NONE) return;
  if (ph === RESPAWN.GRACE) {
    st[S.respawnT] += dt;
    if (st[S.respawnT] >= feel.get('respawn.graceS')) { st[S.respawnPhase] = RESPAWN.NONE; st[S.respawnT] = 0; }
    return;
  }
  const i = anchorNode(st, track, feel.get('respawn.aheadNodes')), ahead = (i + feel.get('respawn.aheadNodes')) % track.nodeCount;
  track.frameAt(track.nodeS[i], A); track.frameAt(track.nodeS[ahead], B);
  v3norm(F, v3reject(F, v3sub(F, B.p, A.p), A.n));
  qlook(Q, F, A.n);
  const hoverM = gap(feel, 'respawn.hoverM'), top = feel.get('speed.topMps');
  v3addScaled(H, A.p, A.n, hoverM);
  const t0 = st[S.respawnT];
  if (ph === RESPAWN.PULL && t0 === 0) events.emit('rescue-pull', {});
  const t = (st[S.respawnT] = t0 + dt);
  setVel(st, ZERO);
  st[S.airborne] = 0; st[S.grounded] = 0; st[S.edgeT] = 0; st[S.steerSlew] = 0; st[S.yawRate] = 0;
  st[S.seg] = i; st[S.segT] = 0; st[S.s] = track.nodeS[i]; st[S.lateral] = 0;

  if (ph === RESPAWN.PULL) {
    const left = feel.get('respawn.pullS') - t0;
    if (left <= dt) {
      setPos(st, H); setQuat(st, Q);
      next(st, RESPAWN.HOVER, events, 'rescue-hover');
    } else {
      const k = dt / left;                      // straight in, arriving at pullS
      setPos(st, v3lerp(P, getPos(P, st), H, k));
      setQuat(st, qslerp(QC, getQuat(QC, st), Q, k));
    }
    st[S.height] = hoverM;
  } else if (ph === RESPAWN.HOVER) {
    setPos(st, H); setQuat(st, Q); st[S.height] = hoverM;
    if (t >= feel.get('respawn.hoverS')) {
      next(st, RESPAWN.RELEASE, events, 'rescue-release');
      st[S.engineV] = feel.get('respawn.releasePct') * top;
    }
  } else if (ph === RESPAWN.RELEASE) {
    const rel = feel.get('respawn.releaseS');
    const h = hoverM * (1 - smoothstep(0, rel, t));
    setPos(st, v3addScaled(P, A.p, A.n, h)); setQuat(st, Q); st[S.height] = h;
    if (t >= rel) {
      const v = feel.get('respawn.releasePct') * top;
      setPos(st, A.p); setVel(st, v3scale(V, F, v));
      st[S.engineV] = v; st[S.speedPct] = v / top; st[S.height] = 0;
      st[S.grounded] = 1; st[S.airTime] = 0; st[S.latchSeg] = i;
      st[S.respawnPhase] = RESPAWN.GRACE; st[S.respawnT] = 0;
      events.emit('land', { tier: 1, airTime: 0 });
    }
  }
}

function next(st, phase, events, name) {
  st[S.respawnPhase] = phase; st[S.respawnT] = 0;
  events.emit(name, {});
}
