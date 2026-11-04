/* net/items.js - effect bubbles online (NET.md section 5): the same API as sim/items.js (update, view, reset,
 * nearOpenEdge, trapRows, lobTarget) so boot.js, the bots and render swap it in, but the room rules rolls and hits.
 *
 * Local racers (this client's kart and the bot seats it keeps) touch a box: `pickup` goes up and the slot fills on
 * the room's `roll`. A press fires: `fire` goes up, self items apply here at once, item-fire is emitted at once.
 * `fired` from the room draws the throw, lob, trap or wave for everyone (driveline flight, same numbers as
 * sim/items.js); `hit` lands the effect on a LOCAL victim only (the victim applies it: Flash uses the victim's own
 * media consent and Gentle, never the thrower's) and emits item-hit for all; `fizzle` pops it. Speed bubbles stay
 * local per client. Remote karts carry their effect and shields in their records (net/remote.js). Three-free.
 */
import { racerFork, pathFrame, pathScale } from './trackPath.js';
import { S, RESPAWN } from '../sim/state.js';
import { wrap, damp, lerp, clamp } from '../core/math.js';
import { grantBoost } from '../sim/grant.js';
import { cancelDrift } from '../sim/drift.js';
import { endBoost } from '../sim/boost.js';
import { ITEMS, bucketOf, BUCKETS, SPIN_KINDS } from '../sim/itemTable.js';
import { itemUseMode } from '../sim/itemUse.js';

const GROW_S = 0.4, WAVE_S = 1.2, THROW_LEAD_M = 2.5, ARC_S = 0.5, PENDING_S = 2, ORPHAN_S = 3;   // as sim/items.js

export function createNetItems(track, feel, events, { send = () => {}, tick = () => 0 } = {}) {
  const L = track.lengthM;
  const g = (k) => feel.get('items.' + k);
  const sd = (a, b) => { const d = wrap(a - b, L); return d > L / 2 ? d - L : d; };
  let list = [], now = 0, shotId = 1, ver = 0, flying = [], traps = [], waves = [], pops = [], R = [], links = [];

  const open = [];
  for (let i = 0; i < track.nodeCount; i++) if (!(track.nodeFlags(i) || []).includes('rail')) open.push([track.nodeS[i], track.nodeS[i + 1] - track.nodeS[i]]);
  function nearOpenEdge(s, m) {
    for (const [a, len] of open) { const d = wrap(s - a, L); if (d <= len + m || d >= L - m) return true; }
    return false;
  }
  const fr = track.frameAt(0);
  function point(s, x, h, out = [0, 0, 0], fork = 0) {
    const f = pathFrame(track, fork, s, fr);
    for (let k = 0; k < 3; k++) out[k] = f.p[k] + f.b[k] * x + f.n[k] * h;
    return out;
  }
  const pick = (track.items || []).map((it, i) => {
    const f = track.frameAt(it.s);
    return { kind: it.kind, s: it.s, x: it.x, downT: 0, upT: 1e9,
      v: { i, p: point(it.s, it.x, 0), n: f.n.slice(), t: f.t.slice(), b: f.b.slice(), alive: true, k: 1 } };
  });
  const sinks = [];
  const sink = (i) => (i === 0 ? events : (sinks[i] ||= { emit: (name, p) => events.emit('racer-event', { racer: i, name, p }) }));
  const rs = (i) => (R[i] ||= { eff: null, glass: 0, golden: 0, echo: 0, echoT: 0, pendT: 0, snapGuard: 0 });
  const bySeat = (seat) => list.find((r) => r.meta.seat === seat) || null;
  const local = (r) => r && r.kind !== 'remote';
  const free = (r) => !r.finished && (r.st[S.respawnPhase] === RESPAWN.NONE || r.st[S.respawnPhase] === RESPAWN.GRACE);
  const low = (r) => r.st[S.grounded] || r.st[S.height] <= g('pickHeightM');
  const near = (r, s, x, rad) => !racerFork(track,r.st) && Math.abs(sd(r.st[S.s], s)) <= rad && Math.abs(r.st[S.lateral] - x) <= rad;
  const pop = (kind, at, s, x, h = 0) => pops.push({ kind, at, s, x, h, t: 0 });
  const kartP = (r) => [r.st[S.posX], r.st[S.posY], r.st[S.posZ]];
  const seatArg = (r, m) => (r.i === 0 ? m : Object.assign(m, { seat: r.meta.seat }));

  // ---- room messages ----------------------------------------------------------------------------
  function onRoll(m) {
    ver++;
    const b = pick[m.box], r = bySeat(m.seat);
    if (b) { b.downT = g('boxRespawnS'); b.upT = 0; pop('box', 'box', b.s, b.x); }
    if (!r) return;
    if (local(r)) { Object.assign(r.item, { id: m.item, rolling: true, rollT: 0, heldT: 0 }); rs(r.i).pendT = 0; }
    events.emit('item-box', { racer: r.i, box: m.box, p: b ? b.v.p.slice() : kartP(r) });
  }
  function onPickupRefused(seat) { const r = bySeat(seat); if (r) rs(r.i).pendT = 0; }

  function onFired(m) {
    ver++;
    const r = bySeat(m.seat), tg = m.target === null ? null : bySeat(m.target), by = r ? r.i : -1, target = tg ? tg.i : -1;
    if (r && !local(r)) events.emit('item-fire', { racer: by, id: m.item, mode: m.mode, target, p: kartP(r) });
    if (r && !local(r) && m.item === 'snap') { pop('snap', 'hit', r.st[S.s], r.st[S.lateral], 1); events.emit('snap-burst', { racer: by, p: kartP(r) }); }
    const fork = m.fork || 0;
    if (m.mode === 'drop') traps.push({ fork, sid: m.sid, id: m.sid, kind: m.item, s: wrap(m.s - g('dropBehindM') * pathScale(track,fork,m.s), L), x: m.x, t: 0, layer: by });
    else if (m.mode === 'throw') {
      const v = (r ? Math.max(0, r.st[S.speedPct]) : 1) * feel.get('speed.topMps') + g('throwExtraMps');
      flying.push({ fork, sid: m.sid, kind: m.item, mode: 'throw', by, target, t: 0, s: wrap(m.s + THROW_LEAD_M * pathScale(track,fork,m.s), L), x: m.x, h: g('throwHeightM'), v, homing: target >= 0 });
    } else if (m.mode === 'lob') {
      const pr = { fork, targetFork: m.targetFork || 0, worldFrom: point(m.s,m.x,g('throwHeightM'),undefined,fork), sid: m.sid, kind: m.item, mode: 'lob', by, target, t: 0, fromS: m.s, fromX: m.x, s: m.s, x: m.x, h: g('throwHeightM'),
        aimD: tg ? sd(tg.st[S.s], m.s) : 0, aimX: tg ? tg.st[S.lateral] : m.x, homing: target >= 0 };
      updateLanding(pr); flying.push(pr);
    } else if (m.mode === 'wide') waves.push({ fork, id: m.sid, kind: m.item, from: by, s: m.s, x: m.x, t: 0 });
    else if (m.mode === 'bounce') {
      // the room aims the Disc (vx); this flight only draws it banking off the rails
      const v = (r ? Math.max(0, r.st[S.speedPct]) : 1) * feel.get('speed.topMps') + g('throwExtraMps');
      flying.push({ fork, sid: m.sid, kind: m.item, mode: 'bounce', by, target: -1, t: 0, s: wrap(m.s + THROW_LEAD_M * pathScale(track,fork,m.s), L), x: m.x, h: g('throwHeightM'), v, homing: false, vx: m.vx || 0, bounces: 0 });
    } else if (m.mode === 'tether' && tg) {
      links.push({ sid: m.sid, by, target, t: 0 });
      events.emit('tether-hook', { by, target, p: kartP(tg) });
    }
  }
  function unlink(sid, reason) {
    const l = links.find((k) => k.sid === sid);
    if (!l) return null;
    links = links.filter((k) => k !== l);
    events.emit('tether-break', { by: l.by, target: l.target, reason });
    return l;
  }

  /** The room's verdict: the victim (if local) applies it, everyone sees it. */
  function onHit(m) {
    ver++;
    const v = bySeat(m.victim), byR = bySeat(m.by);
    const shot = flying.find((p) => p.sid === m.sid) || traps.find((p) => p.sid === m.sid);
    flying = flying.filter((p) => p !== shot); traps = traps.filter((p) => p !== shot);
    const link = shot ? null : unlink(m.sid, 'pass');
    if (!v) return;
    // Cosmetic pursuit can diverge from the room. Its verdict always bursts on
    // the victim, with no locally predicted damage or collision decision.
    const p = kartP(v);
    if (shot) pop(m.item, shot.layer !== undefined ? (m.verdict === 'glass' || m.verdict === 'golden' ? m.verdict : 'trap') : m.verdict === 'mercy' ? 'hit' : m.verdict, v.st[S.s], v.st[S.lateral], 1);
    if (local(v)) {
      const x = rs(v.i);
      if (m.verdict === 'hit') {
        land(v, x, m.item, m.durationS, byR ? byR.i : -1);
        if (m.item === 'snap') x.eff.side = Math.sign(v.st[S.lateral] - (byR ? byR.st[S.lateral] : 0)) || 1;
      }
      else if (m.verdict === 'glass') { x.glass = 0; pop('glass', 'glass', v.st[S.s], v.st[S.lateral], 1); }
      else if (m.verdict === 'decoy') x.echo = Math.max(0, x.echo - 1);
    }
    events.emit('item-hit', { victim: v.i, by: byR ? byR.i : -1, kind: m.item, how: shot ? (shot.layer !== undefined ? 'trap' : shot.mode) : link ? 'tether' : m.item === 'snap' ? 'wide' : null, verdict: m.verdict, durationS: m.verdict === 'hit' ? m.durationS : 0, p });
  }
  function onFizzle(m) {
    ver++;
    const shot = flying.find((p) => p.sid === m.sid) || traps.find((p) => p.sid === m.sid);
    if (!shot) { unlink(m.sid, 'timeout'); return; }
    flying = flying.filter((p) => p !== shot); traps = traps.filter((p) => p !== shot);
    pop(shot.kind, 'fizzle', shot.s, shot.x, shot.h || 0);
  }

  function land(v, x, kind, d, by) {
    const out = v.i === 0 && feel.get('gentle.on') ? Math.max(g('effectOutS'), feel.get('gentle.fadeMs') / 1000) : g('effectOutS');
    x.eff = { kind, t: 0, durS: d, out, by };
    if (SPIN_KINDS.includes(kind)) {
      const st = v.st;
      st[S.stalled] = d; st[S.engineV] *= g(kind + 'SpeedKeep');
      cancelDrift(st, 'stall', sink(v.i)); endBoost(st, feel, sink(v.i));
    }
  }

  // ---- local racers -----------------------------------------------------------------------------
  function use(r, standings) {
    const id = r.item.id, st = r.st, s = st[S.s], x = st[S.lateral];
    const mode = itemUseMode(id, r.input.back);
    if ((id === 'shatter' || (mode === 'drop' && SPIN_KINDS.includes(id))) && nearOpenEdge(mode === 'drop' ? wrap(s - g('dropBehindM'), L) : s, g('shatterEdgeSafeM'))) {
      events.emit('item-refused', { racer: r.i, id, reason: 'edge' });
      return;
    }
    if (mode === 'lob' && lobTarget(r.i, standings) < 0) return;            // alone on the road: keep it
    if (mode === 'tether' && !tetherable(r, standings)) return;            // nobody close ahead: keep it
    if (send(seatArg(r, { t: 'fire', id: shotId, item: id, mode, tick: tick(), s, x })) === false) return;
    shotId = (shotId % 65535) + 1;
    if (mode === 'self') self(r, id, standings);
    r.item.id = null; r.item.rolling = false; r.item.rollT = 0; r.item.heldT = 0;
    events.emit('item-fire', { racer: r.i, id, mode, target: -1, p: kartP(r) });
  }
  function self(r, id, standings) {
    const x = rs(r.i);
    if (id === 'surge') {
      const b = BUCKETS.indexOf(bucketOf(feel, standings.placeOf(r.i), standings.gapToLeader(r.i)));
      grantBoost(r.st, g('surgeAmount')[Math.max(0, b)], g('surgeS'), 'surge', feel, sink(r.i));
    } else if (id === 'glass') x.glass = g('glassMaxS');
    else if (id === 'echo') { x.echo = g('echoCopies'); x.echoT = g('echoS'); }
    else if (id === 'golden') x.golden = g('goldenS');
    else if (id === 'snap') {
      // the guard and the burst are local; the room shoves the karts nearby and drops a Tether on you
      x.snapGuard = g('snapGuardS');
      pop('snap', 'hit', r.st[S.s], r.st[S.lateral], 1);
      events.emit('snap-burst', { racer: r.i, p: kartP(r) });
    }
  }
  function tetherable(r, standings) {
    if (links.some((l) => l.by === r.i)) return false;
    const me = standings.progressOf(r.i), fork = racerFork(track, r.st);
    return list.some((o) => o !== r && free(o) && !o.meta.left && racerFork(track, o.st) === fork
      && standings.progressOf(o.i) - me > 0 && standings.progressOf(o.i) - me <= g('tetherRangeM'));
  }
  function lobTarget(i, standings) {
    for (const j of standings.order) { if (j === i) continue; const o = list[j]; if (o && !o.finished && !o.meta.left) return j; }
    return -1;
  }
  function pickups(r) {
    if (!low(r)) return;
    const x = rs(r.i);
    for (let i = 0; i < pick.length; i++) {
      const b = pick[i];
      if (b.downT > 0) continue;
      if (b.kind === 'box') {
        if (r.item.id || r.item.rolling || x.pendT > 0 || !near(r, b.s, b.x, g('boxRadiusM'))) continue;
        if (send(seatArg(r, { t: 'pickup', box: i, tick: tick() })) === false) continue;
        b.downT = g('boxRespawnS'); b.upT = 0; x.pendT = PENDING_S;          // hidden once sent, filled on roll
      } else if (near(r, b.s, b.x, g('bubbleRadiusM'))) {
        b.downT = g('bubbleRespawnS'); b.upT = 0;
        grantBoost(r.st, g('bubbleAmount'), g('bubbleHoldS'), 'bubble', feel, sink(r.i));
        pop('bubble', 'bubble', b.s, b.x);
        events.emit('bubble-pop', { racer: r.i, i, p: b.v.p.slice() });
      }
    }
  }
  function tickLocal(r, dt) {
    const it = r.item, x = rs(r.i);
    if (x.pendT > 0) x.pendT = Math.max(0, x.pendT - dt);
    if (it.rolling) {
      it.rollT += dt;
      if (it.rollT >= g('rollS')) { it.rolling = false; events.emit('item-roll', { racer: r.i, id: it.id }); }
    } else if (it.id) it.heldT += dt;
    if (x.eff && (x.eff.t += dt) >= x.eff.durS + x.eff.out) x.eff = null;
    if (x.glass > 0 && (x.glass -= dt) <= 0) { x.glass = 0; pop('glass', 'glass', r.st[S.s], r.st[S.lateral], 1); }
    if (x.golden > 0) x.golden = Math.max(0, x.golden - dt);
    if (x.snapGuard > 0) x.snapGuard = Math.max(0, x.snapGuard - dt);
    if (x.echo > 0 && (x.echoT -= dt) <= 0) x.echo = 0;
  }
  function writeMods(r) {
    const m = r.mods, x = rs(r.i), e = x.eff, fxv = r.fxv;
    m.steerMul = 1; m.steerAdd = 0; m.throttleMul = 1; m.gripMul = 1; m.slideMul = 1; m.capMul = 1; m.shoveMul = 1; m.blind = 0;
    fxv.spinRad = 0; fxv.effect = e ? e.kind : null;
    if (e && e.t < e.durS) {
      if (e.kind === 'spiral') m.steerAdd = g('spiralSteerAmp') * Math.sin(2 * Math.PI * g('spiralSteerHz') * e.t);
      else if (e.kind === 'freeze') m.steerMul = g('freezeSteerMul');
      else if (e.kind === 'melt') { m.gripMul = g('meltGripMul'); m.slideMul = g('meltSlideMul'); }
      else if (e.kind === 'pendulum') m.steerAdd = g('pendulumSteerAmp') * Math.sin(2 * Math.PI * g('pendulumSteerHz') * e.t);
      else if (e.kind === 'snap') m.steerAdd = (e.side || 1) * g('snapShoveSteer') * (1 - e.t / e.durS);
      else if (e.kind === 'bigSpiral') m.capMul = g('bigSpiralCapMul');
      else if (SPIN_KINDS.includes(e.kind)) { const k = e.t / e.durS; fxv.spinRad = Math.round(g(e.kind + 'SpinTurns')) * 2 * Math.PI * (1 - (1 - k) * (1 - k)); }
      if (r.meta.isBot && ITEMS[e.kind].bot === 'blind') m.blind = 1;
    }
    if (x.golden > 0) m.shoveMul = g('goldenShoveMul');
    fxv.shield = x.golden > 0 ? 'golden' : x.glass > 0 ? 'glass' : null;
    fxv.echo = x.echo;
    for (const l of links) {
      if (l.by === r.i) m.capMul *= g('tetherTowCap');
      else if (l.target === r.i) m.capMul *= g('tetherDragCap');
    }
  }

  // ---- shown flights (the room rules the outcome; these only draw it) ---------------------------
  function flights(dt) {
    flying = flying.filter((pr) => {
      const oldP = projectilePoint(pr);
      pr.t += dt;
      const tg = pr.target >= 0 ? list[pr.target] : null;
      if (pr.mode === 'lob') {
        const k = Math.min(1, pr.t / g('lobFlightS'));
        pr.homing = !!(tg && free(tg) && k < g('lobCommitFrac'));
        if (pr.homing) {
          pr.targetFork = racerFork(track,tg.st);
          const f = pathFrame(track,pr.targetFork,tg.st[S.s]), remaining = Math.max(0, g('lobFlightS') - pr.t);
          const speed = tg.st[S.velX] * f.t[0] + tg.st[S.velY] * f.t[1] + tg.st[S.velZ] * f.t[2];
          const wanted = pr.aimD + sd(tg.st[S.s], wrap(pr.fromS + pr.aimD, L)) + speed * remaining;
          pr.aimD += clamp(wanted - pr.aimD, -g('lobRetargetMps') * dt, g('lobRetargetMps') * dt);
          pr.aimX += clamp(tg.st[S.lateral] - pr.aimX, -g('homeLateralMps') * dt, g('homeLateralMps') * dt);
        }
        updateLanding(pr);
        pr.s = wrap(pr.fromS + pr.aimD * k, L);
        pr.x = lerp(pr.fromX, pr.aimX, k);
        pr.h = g('throwHeightM') + g('lobHeightM') * 4 * k * (1 - k);
        velocity(pr, oldP, dt);
        return pr.t < g('lobFlightS') + ORPHAN_S;
      }
      if (pr.mode === 'bounce') {
        pr.x += pr.vx * dt;
        const f = pathFrame(track, pr.fork, pr.s), half = (Number.isFinite(f.w) ? f.w : 12) / 2 - g('projectileRadiusM'), side = Math.sign(pr.x);
        if (Math.abs(pr.x) > half) {
          const flags = f.flags || [];
          // an open edge or a spent Disc flies on until the room's fizzle
          if ((flags.includes('rail') || flags.includes(side > 0 ? 'railR' : 'railL')) && pr.bounces < g('discBounces')) {
            pr.x = side * half; pr.vx = -pr.vx; pr.bounces++;
            events.emit('disc-bounce', { by: pr.by, projectileId: pr.sid, p: projectilePoint(pr) });
          }
        }
      }
      const gap = tg ? sd(tg.st[S.s], pr.s) : -1;
      pr.homing = !!(tg && free(tg) && racerFork(track,tg.st) === (pathFrameFork(pr)) && gap > 0 && gap <= g('throwRangeM'));
      pr.s = wrap(pr.s + pr.v * dt * pathScale(track,pr.fork,pr.s), L);
      if (pr.homing) pr.x += clamp(damp(pr.x, tg.st[S.lateral], g('homeTcS'), dt) - pr.x, -g('homeLateralMps') * dt, g('homeLateralMps') * dt);
      pr.h = g('throwHeightM') + g('throwArcM') * Math.sin(Math.PI * Math.min(1, pr.t / ARC_S));
      velocity(pr, oldP, dt);
      return pr.t < g(pr.mode === 'bounce' ? 'discLifeS' : 'throwLifeS') + ORPHAN_S;
    });
    traps = traps.filter((tp) => (tp.t += dt) < g('trapLifeS') + ORPHAN_S);
    links = links.filter((l) => (l.t += dt) < g('tetherMaxS') + ORPHAN_S);
  }

  function pathFrameFork(pr) { const f=track.forks?.[(pr.fork||0)-1]; return f && pr.s>=f.fromS && pr.s<=f.toS ? pr.fork : 0; }
  function projectilePoint(pr, out=[0,0,0]) {
    if(pr.mode==='lob' && (pr.fork||pr.targetFork) && pr.landingP) {
      const k=Math.min(1,pr.t/g('lobFlightS'));
      for(let j=0;j<3;j++)out[j]=lerp(pr.worldFrom[j],pr.landingP[j],k);
      out[1]+=g('lobHeightM')*4*k*(1-k)+g('throwHeightM')*k;
      return out;
    }
    return point(pr.s,pr.x,pr.h,out,pr.fork);
  }
  function updateLanding(pr) {
    if (!pr.homing && pr.landingP) return;
    const s = wrap(pr.fromS + pr.aimD, L);
    pr.landingP ||= [0, 0, 0]; pr.landingN ||= [0, 1, 0];
    point(s, pr.aimX, 0, pr.landingP, pr.targetFork);
    const n = (pr.targetFork ? pathFrame(track,pr.targetFork,s) : track.surfaceAt ? track.surfaceAt(s, pr.aimX) : track.frameAt(s)).n;
    for (let j = 0; j < 3; j++) pr.landingN[j] = n[j];
  }

  function velocity(pr, oldP, dt) {
    const p = projectilePoint(pr);
    pr.velocity ||= [0, 0, 0];
    for (let k = 0; k < 3; k++) pr.velocity[k] = dt > 0 ? (p[k] - oldP[k]) / dt : 0;
  }

  function update(dt, racers, standings, race) {
    if (!race || (race.phase !== 'racing' && race.phase !== 'finished')) return;
    list = racers; now += dt; ver++;
    for (const b of pick) { if (b.downT > 0) b.downT = Math.max(0, b.downT - dt); else b.upT += dt; }
    for (const r of racers) if (local(r)) tickLocal(r, dt);
    for (const r of racers) {
      if (!local(r) || !free(r)) continue;
      pickups(r);
      if (r.input && r.input.item && r.item.id && !r.item.rolling) use(r, standings);
    }
    flights(dt);
    for (const w of waves) w.t += dt;
    waves = waves.filter((w) => w.t < WAVE_S);
    for (const p of pops) p.t += dt;
    pops = pops.filter((p) => p.t < g('popShowS'));
    for (const r of racers) if (local(r)) writeMods(r);
  }

  // ---- view (the sim/items.js shape; rows pooled per tick) ---------------------------------------
  const V = { boxes: [], bubbles: [], held: [], projectiles: [], traps: [], waves: [], shields: [], pops: [], tethers: [], guards: [] };
  for (const b of pick) (b.kind === 'box' ? V.boxes : V.bubbles).push(b.v);
  const projectileRows = [];
  let builtVer = -1;
  function view() {
    if (builtVer === ver) return V;
    builtVer = ver;
    for (const b of pick) { b.v.alive = b.downT <= 0; b.v.k = b.v.alive ? Math.min(1, b.upT / GROW_S) : 0; }
    V.held = []; V.projectiles = []; V.traps = []; V.waves = []; V.shields = []; V.pops = []; V.tethers = []; V.guards = [];
    for (const r of list) if (r.item && (r.item.id || r.item.rolling)) V.held.push({ racer: r.i, id: r.item.id, rolling: r.item.rolling, rollT: r.item.rollT });
    for (const pr of flying) {
      const i = V.projectiles.length, o = projectileRows[i] || (projectileRows[i] = { p: [0, 0, 0], v: [0, 0, 0] });
      o.id = pr.sid; o.kind = pr.kind; o.mode = pr.mode; projectilePoint(pr, o.p); o.t = pr.t; o.target = pr.target; o.s = pr.s; o.x = pr.x;
      o.by = pr.by; o.homing = pr.homing; o.radiusM = g(pr.mode === 'lob' ? 'lobRadiusM' : 'projectileRadiusM');
      o.progress = Math.min(1, pr.t / (pr.mode === 'lob' ? g('lobFlightS') : ARC_S));
      for (let j = 0; j < 3; j++) o.v[j] = pr.velocity ? pr.velocity[j] : 0;
      if (pr.mode === 'lob') {
        o.landingP ||= [0, 0, 0]; o.landingN ||= [0, 1, 0];
        for (let j = 0; j < 3; j++) { o.landingP[j] = pr.landingP[j]; o.landingN[j] = pr.landingN[j]; }
      } else { o.landingP = null; o.landingN = null; }
      V.projectiles.push(o);
    }
    const armS = g('trapArmS');
    for (const tp of traps) {
      const f = pathFrame(track,tp.fork,tp.s,fr), p = [0, 0, 0];
      for (let k = 0; k < 3; k++) p[k] = f.p[k] + f.b[k] * tp.x;
      V.traps.push({ id: tp.sid, kind: tp.kind, p, n: f.n.slice(), t: tp.t, armed: tp.t >= armS, s: tp.s, x: tp.x, radiusM: g(tp.kind === 'melt' ? 'meltRadiusM' : 'trapRadiusM') });
    }
    for (const w of waves) V.waves.push({ id: w.id, kind: w.kind, from: w.from, p: point(w.s, w.x, 0, undefined, w.fork), t: w.t, rangeM: g('pinkRangeM') });
    for (const r of list) {
      const x = local(r) ? R[r.i] : null, sh = r.fxv && r.fxv.shield, ec = r.fxv ? r.fxv.echo : 0;
      if (x) {
        if (x.glass > 0) V.shields.push({ racer: r.i, kind: 'glass', t: g('glassMaxS') - x.glass, k: x.glass / g('glassMaxS'), copies: 0 });
        if (x.golden > 0) V.shields.push({ racer: r.i, kind: 'golden', t: g('goldenS') - x.golden, k: x.golden / g('goldenS'), copies: 0 });
        if (x.echo > 0) V.shields.push({ racer: r.i, kind: 'echo', t: g('echoS') - x.echoT, k: Math.max(0, x.echoT / g('echoS')), copies: x.echo });
        if (x.snapGuard > 0) V.guards.push({ racer: r.i, k: x.snapGuard / g('snapGuardS') });
      } else if (!local(r)) {
        if (sh) V.shields.push({ racer: r.i, kind: sh, t: 1, k: 1, copies: 0 });
        if (ec > 0) V.shields.push({ racer: r.i, kind: 'echo', t: 1, k: 1, copies: ec });
      }
    }
    for (const l of links) {
      const a = list[l.by], b = list[l.target];
      if (a && b) V.tethers.push({ id: l.sid, by: l.by, target: l.target, t: l.t, k: Math.min(1, l.t / g('tetherMaxS')), from: kartP(a), to: kartP(b) });
    }
    for (const p of pops) V.pops.push({ kind: p.kind, at: p.at, p: point(p.s, p.x, p.h), t: p.t });
    return V;
  }

  /** Live trap rows { id, kind, s, x, t, layer } for the bots; read-only, not kept past a tick. */
  const trapRows = () => traps;

  function reset() {
    now = 0; shotId = 1; ver++; flying = []; traps = []; waves = []; pops = []; R = []; links = [];
    for (const b of pick) { b.downT = 0; b.upT = 1e9; }
  }

  return { update, view, reset, nearOpenEdge, trapRows, lobTarget, onRoll, onPickupRefused, onFired, onHit, onFizzle, state: rs };
}
