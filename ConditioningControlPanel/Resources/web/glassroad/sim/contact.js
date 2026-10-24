/* sim/contact.js - kart vs kart, ghosts and slipstream (CONTRACT-B1.md section 3, the board's "Contact").
 *
 * Saucers push apart along the line between them, projected on the road plane: the overlap is split by
 * weight, the closing speed is taken out and a sideways push (pushMps + pushPerDv x closing, capped) is
 * split the same way. Heavier = faster, boosting, Golden; a stalled kart is lighter. No spin, no stall.
 * Glass and Golden bubbles instead launch the other kart outward and upward on contact, with a pair cooldown.
 * A push may carry a kart past an open edge (Phase A drops it) unless its feel says gentle.on (Gentle
 * Visuals or the player's steering assist): then it is held inside the road.
 * Ghosts never touch: the first contact.ghostS after go, and the whole rescue incl. its 1.5 s grace.
 * Slipstream: sit slipMinM..slipMaxM behind a kart, inside slipWidthM, both fast and grounded, for
 * slipChargeS and a small boost fires (sim/grant.js), then slipCooldownS; on an open stretch the charge holds
 * full and fires once the road is railed again (polish 6). No three.js.
 */
import { S, RESPAWN, BOOST } from './state.js';
import { wrap, clamp } from '../core/math.js';
import { gap } from './gaps.js';
import { grantBoost } from './grant.js';
import { BUBBLE_RAM } from '../core/bubble.js';

const FR = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
const QO = { normal: [0, 0, 0], p: [0, 0, 0] };
const U = [0, 0, 0];
const COURT_LEAD_M = 25;   // the slipstream kick holds from here before a court to the end of its last hole

export function createContact(track, feel, events) {
  const L = track.lengthM;
  const cool = new Map();          // pair key -> seconds left before the next push and bump cue
  const slip = [];                 // per racer { charge, cool, inWake }

  function reset() { cool.clear(); slip.length = 0; }
  /** A kart on a stretch with an open side (no rail there). */
  function openAt(st) {
    const f = track.nodeFlags ? track.nodeFlags(st[S.seg] | 0) : null;
    return !!f && f.indexOf('rail') < 0 && !(f.indexOf('railL') >= 0 && f.indexOf('railR') >= 0) && f.indexOf('jump') < 0;
  }
  // water wave 1: the kick also waits through a court approach (a slipstream kick at the aperture entry
  // pushed a bot onto a hole line faster than it could reach a supported band; the player gets the same hold)
  function courtAt(st) {
    if (track.forkAtSeg?.(st[S.seg])) return false;
    const cs = track.courts || [];
    for (const c of cs) {
      if (!c.holes || !c.holes.length) continue;
      const end = Math.max(...c.holes.map((h) => h.s + h.len));
      if (wrap(st[S.s] - (c.fromS - COURT_LEAD_M), L) <= end - c.fromS + COURT_LEAD_M) return true;
    }
    // wave 2 STONES: and from stones.leadM before a stone field to its end (a kick at the entry at the wrong
    // lateral is a fall off the stones)
    const fs = track.fields;
    if (!fs || !fs.length) return false;
    const lead = feel.get('stones.leadM');
    for (const f of fs) if (wrap(st[S.s] - (f.fromS - lead), L) <= f.toS - f.fromS + lead) return true;
    return false;
  }

  /** Inside a stone field (or stones.leadM before it): a shove there keeps only stones.shoveMul of its sideways part. */
  function onStones(st) {
    if (track.forkAtSeg?.(st[S.seg])) return false;
    const fs = track.fields;
    if (!fs || !fs.length) return false;
    const lead = feel.get('stones.leadM');
    for (const f of fs) if (wrap(st[S.s] - (f.fromS - lead), L) <= f.toS - f.fromS + lead) return true;
    return false;
  }

  function weight(r) {
    const st = r.st, v = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
    let w = 1 + v / feel.get('contact.speedRefMps');
    if (st[S.boostKind] !== BOOST.NONE) w *= feel.get('contact.boostShoveMul');
    if (st[S.stalled] > 0) w /= feel.get('contact.stalledShoveMul');
    return w * (r.mods ? r.mods.shoveMul : 1);
  }

  function move(r, sx, sy, sz) { r.st[S.posX] += sx; r.st[S.posY] += sy; r.st[S.posZ] += sz; }
  function push(r, sx, sy, sz) { r.st[S.velX] += sx; r.st[S.velY] += sy; r.st[S.velZ] += sz; }

  /** A push never carries a kart through a rail; under Gentle or assist the open sides hold too (soft rails). */
  function holdInside(r) {
    const st = r.st;
    const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0, QO);
    if (q.flags && q.flags.indexOf('jump') >= 0) return;
    if (!q.railed && !r.feel.get('gentle.on')) return;
    const rad = gap(r.feel, 'kart.radiusM');
    if (q.edgeDist >= rad) return;
    const b = (track.pathFrameAt ? track.pathFrameAt(q.s, q.seg, FR) : track.frameAt(q.s, FR)).b, side = q.lateral >= 0 ? 1 : -1, pen = rad - q.edgeDist;
    move(r, -b[0] * side * pen, -b[1] * side * pen, -b[2] * side * pen);
    const out = (st[S.velX] * b[0] + st[S.velY] * b[1] + st[S.velZ] * b[2]) * side;
    if (out > 0) push(r, -b[0] * side * out, -b[1] * side * out, -b[2] * side * out);
    st[S.lateral] = q.lateral - side * pen;
  }

  function update(dt, racers, race) {
    const live = race.phase === 'racing' || race.phase === 'finished';
    const startGhost = race.phase === 'racing' && race.timeS < feel.get('contact.ghostS');
    for (const r of racers) r.fxv.ghost = live && (startGhost || r.st[S.respawnPhase] !== RESPAWN.NONE);
    if (!live) { for (const r of racers) r.fxv.slip = 0; return; }
    for (const [k, v] of cool) { if (v - dt <= 0) cool.delete(k); else cool.set(k, v - dt); }

    const radius = feel.get('contact.radiusM'), maxH = feel.get('contact.maxHeightM');
    const n = racers.length;
    for (let i = 0; i < n; i++) {
      const ra = racers[i];
      if (ra.fxv.ghost) continue;
      for (let j = i + 1; j < n; j++) {
        const rb = racers[j];
        if (rb.fxv.ghost) continue;
        const shieldA = ra.fxv.shield === 'glass' || ra.fxv.shield === 'golden';
        const shieldB = rb.fxv.shield === 'glass' || rb.fxv.shield === 'golden';
        const R2 = (shieldA ? BUBBLE_RAM.radiusM : radius) + (shieldB ? BUBBLE_RAM.radiusM : radius);
        const a = ra.st, b = rb.st;
        const dx = b[S.posX] - a[S.posX], dy = b[S.posY] - a[S.posY], dz = b[S.posZ] - a[S.posZ];
        if (dx * dx + dy * dy + dz * dz >= R2 * R2) continue;
        const f = track.pathFrameAt ? track.pathFrameAt(a[S.s], a[S.seg], FR) : track.frameAt(a[S.s], FR), nrm = f.n;
        const dn = dx * nrm[0] + dy * nrm[1] + dz * nrm[2];
        if (Math.abs(dn) > maxH) continue;
        U[0] = dx - nrm[0] * dn; U[1] = dy - nrm[1] * dn; U[2] = dz - nrm[2] * dn;
        let len = Math.hypot(U[0], U[1], U[2]);
        if (len < 1e-4) {                       // dead on top of each other: split across the road
          const side = b[S.lateral] >= a[S.lateral] ? 1 : -1;
          U[0] = f.b[0] * side; U[1] = f.b[1] * side; U[2] = f.b[2] * side; len = 0;
        } else { U[0] /= len; U[1] /= len; U[2] /= len; }
        const wa = weight(ra), wb = weight(rb), sa = wb / (wa + wb), sb = wa / (wa + wb);
        const over = R2 - len;
        move(ra, -U[0] * over * sa, -U[1] * over * sa, -U[2] * over * sa);
        move(rb, U[0] * over * sb, U[1] * over * sb, U[2] * over * sb);

        const key = i * 64 + j;
        const closing = (a[S.velX] - b[S.velX]) * U[0] + (a[S.velY] - b[S.velY]) * U[1] + (a[S.velZ] - b[S.velZ]) * U[2];
        if (!cool.has(key)) {
          const c = Math.max(0, closing);
          if (shieldA || shieldB) {
            const fling = (r, side) => {
              const st=r.st, speed=Math.min(BUBBLE_RAM.maxMps,BUBBLE_RAM.pushMps+c*0.25);
              push(r,U[0]*side*speed,U[1]*side*speed,U[2]*side*speed);
              const vn=st[S.velX]*nrm[0]+st[S.velY]*nrm[1]+st[S.velZ]*nrm[2];
              const lift=Math.max(0,BUBBLE_RAM.upMps-vn);
              push(r,nrm[0]*lift,nrm[1]*lift,nrm[2]*lift);
              st[S.airborne]=1; st[S.grounded]=0; st[S.airTime]=0; st[S.hopT]=0;
              st[S.latchSeg]=st[S.seg];
            };
            if (shieldA) fling(rb,1);
            if (shieldB) fling(ra,-1);
            cool.set(key,BUBBLE_RAM.cooldownS);
            const p=[(a[S.posX]+b[S.posX])/2,(a[S.posY]+b[S.posY])/2,(a[S.posZ]+b[S.posZ])/2];
            events.emit('bump',{a:i,b:j,dv:Math.max(c,18),p});
            events.emit('bubble-ram',{a:i,b:j,attacker:shieldA?ra.i:rb.i,victim:shieldA?rb.i:ra.i,both:shieldA&&shieldB,p});
            holdInside(ra); holdInside(rb);
            continue;
          }
          const p = Math.min(feel.get('contact.maxPushMps'), feel.get('contact.pushMps') + feel.get('contact.pushPerDv') * c);
          const ka = (c + p) * sa, kb = (c + p) * sb;   // take the closing speed out, then push apart
          // wave 2 STONES: on the stones the sideways part of the shove is mostly lost (a full push threw a kart
          // 3.5 m across a 5 m stone into the slit between lanes); the along-road part stays
          let lx = 0, ly = 0, lz = 0;
          if (onStones(a) || onStones(b)) {
            const ub = U[0] * f.b[0] + U[1] * f.b[1] + U[2] * f.b[2], keep = 1 - feel.get('stones.shoveMul');
            lx = f.b[0] * ub * keep; ly = f.b[1] * ub * keep; lz = f.b[2] * ub * keep;
          }
          push(ra, -(U[0] - lx) * ka, -(U[1] - ly) * ka, -(U[2] - lz) * ka);
          push(rb, (U[0] - lx) * kb, (U[1] - ly) * kb, (U[2] - lz) * kb);
          const loss = 1 - feel.get('contact.engineLoss');
          a[S.engineV] *= loss; b[S.engineV] *= loss;
          cool.set(key, feel.get('contact.cooldownS'));
          if (c >= feel.get('contact.cueMinDvMps')) {
            events.emit('bump', { a: i, b: j, dv: c,
              p: [(a[S.posX] + b[S.posX]) / 2, (a[S.posY] + b[S.posY]) / 2, (a[S.posZ] + b[S.posZ]) / 2] });
          }
        }
        holdInside(ra); holdInside(rb);
      }
    }

    // slipstream
    const minM = feel.get('contact.slipMinM'), maxM = feel.get('contact.slipMaxM'), wid = feel.get('contact.slipWidthM');
    const minPct = feel.get('contact.slipMinPct'), chargeS = feel.get('contact.slipChargeS');
    const fast = (r) => !r.fxv.ghost && r.st[S.grounded] > 0 && r.st[S.airborne] === 0 && r.st[S.speedPct] > minPct;
    for (const r of racers) {
      const w = (slip[r.i] ||= { charge: 0, cool: 0, inWake: false });
      w.cool = Math.max(0, w.cool - dt);
      let wake = false;
      if (w.cool <= 0 && fast(r)) {
        for (const o of racers) {
          if (o === r || !fast(o)) continue;
          const ownFork = track.forkAtSeg?.(r.st[S.seg]), otherFork = track.forkAtSeg?.(o.st[S.seg]);
          if (ownFork !== otherFork) continue;
          const ds = ownFork ? ownFork.localS(o.st[S.s]) - ownFork.localS(r.st[S.s]) : wrap(o.st[S.s] - r.st[S.s], L);
          if (ds >= minM && ds <= maxM && Math.abs(o.st[S.lateral] - r.st[S.lateral]) < wid) { wake = true; break; }
        }
      }
      if (wake) {
        if (!w.inWake) { w.inWake = true; events.emit('slipstream', { racer: r.i, state: 'in' }); }
        w.charge += dt;
        // polish 6: the boost waits for railed road (a slipstream kick on an open bend throws the kart off it)
        if (w.charge >= chargeS && (openAt(r.st) || courtAt(r.st))) w.charge = chargeS;
        else if (w.charge >= chargeS) {
          const ok = grantBoost(r.st, feel.get('contact.slipAmount'), feel.get('contact.slipHoldS'), 'slip', r.feel, r.events || events);
          events.emit('slipstream', { racer: r.i, state: ok ? 'fire' : 'out' });
          w.charge = 0; w.inWake = false; w.cool = feel.get('contact.slipCooldownS');
        }
      } else {
        if (w.inWake) { w.inWake = false; events.emit('slipstream', { racer: r.i, state: 'out' }); }
        w.charge = Math.max(0, w.charge - dt);
      }
      r.fxv.slip = clamp(w.charge / chargeS, 0, 1);
    }
  }

  return { update, reset };
}
