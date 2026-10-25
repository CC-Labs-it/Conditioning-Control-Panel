/* sim/items.js - effect bubbles in the race: boxes, pickup bubbles, the held slot, throws, lobs, traps,
 * hit verdicts, mercy and the per-racer mods (CONTRACT-B1.md section 3, "Item rules").
 *
 * Everything runs on s (arc length) and lateral, the same frame the karts use, so a throw follows the
 * driveline. Mercy lives here only: 2 s immune after a screen effect ends, and a rolling ledger keeps
 * blocked view under items.viewBudgetS per items.viewWindowS (a hit is trimmed to fit). One effect per
 * victim; a new hit replaces the old one. Shatter never fires, lands or is laid near an open edge.
 * Three-free. Bot events (boost, drift cancel) go out as racer-event like every other bot event.
 */
import { S, RESPAWN } from './state.js';
import { wrap, damp, lerp, clamp } from '../core/math.js';
import { grantBoost } from './grant.js';
import { cancelDrift } from './drift.js';
import { endBoost } from './boost.js';
import { ITEMS, bucketOf, rollItem, durationOf, makeRand, BUCKETS, SPIN_KINDS } from './itemTable.js';

const GROW_S = 0.4;          // feel gap: a respawned box or bubble grows in over this
const WAVE_S = 1.2;          // feel gap: a Pink wave stays in the view this long
const THROW_LEAD_M = 2.5;    // feel gap: a throw leaves this far ahead of the user
const ARC_S = 0.5;           // feel gap: the first arc out of the hand lasts this long
const SHATTER_WAIT_M = 6;    // feel gap: a Shatter throw waits this far behind a victim near an open edge

const VIEW_KINDS = { full: 1, mild: 1 };

export function createItems(track, feel, events, { seed = 1 } = {}) {
  const L = track.lengthM;
  const g = (k) => feel.get('items.' + k);
  const pathFrame = (s, seg, out) => track.pathFrameAt ? track.pathFrameAt(s, seg, out) : track.frameAt(s, out);
  const forkOf = seg => track.forkAtSeg?.(seg) || null;
  function pathGap(s, seg, targetS, targetSeg) {
    const fork = forkOf(seg);
    if (fork !== forkOf(targetSeg)) return Infinity;
    return fork ? fork.localS(targetS) - fork.localS(s) : sd(targetS, s);
  }
  const sd = (a, b) => { const d = wrap(a - b, L); return d > L / 2 ? d - L : d; };   // signed a - b
  let rand = makeRand(seed), now = 0, nextId = 1, list = [], ver = 0;

  // Open stretches: any segment without rails on both sides (open, railL, railR, the jump gap).
  const open = [];
  for (let i = 0; i < track.nodeCount; i++) {
    if (!(track.nodeFlags(i) || []).includes('rail')) open.push([track.nodeS[i], track.nodeS[i + 1] - track.nodeS[i]]);
  }
  function nearOpenEdge(s, m, seg = -1) {
    const fork = track.forkAtSeg?.(seg);
    if (fork) {
      const local = fork.localS(s), lo = Math.max(0, local - m), hi = Math.min(fork.lengthM, local + m);
      const count = Math.max(1, Math.ceil((hi - lo) / 2));
      for (let i = 0; i <= count; i++) {
        const flags = fork.frameAt(lo + (hi - lo) * i / count).flags || [];
        if (!(flags.includes('rail') || (flags.includes('railL') && flags.includes('railR')))) return true;
      }
      if (local < m && nearOpenEdge(fork.fromS, m - local)) return true;
      if (fork.lengthM - local < m && nearOpenEdge(fork.toS, m - (fork.lengthM - local))) return true;
      return false;
    }
    for (const [a, len] of open) { const d = wrap(s - a, L); if (d <= len + m || d >= L - m) return true; }
    return false;
  }

  function advance(s, seg, metres) {
    const fork = track.forkAtSeg?.(seg);
    if (!fork) return { s: wrap(s + metres, L), seg: -1 };
    const local = fork.localS(s) + metres;
    if (local < 0) return { s: wrap(fork.fromS + local, L), seg: -1 };
    return local < fork.lengthM ? { s: fork.mainS(local), seg }
      : { s: wrap(fork.toS + local - fork.lengthM, L), seg: -1 };
  }

  const fr = track.frameAt(0);   // scratch frame, reused
  function point(s, x, h, out = [0, 0, 0], seg = -1) {
    const f = track.pathFrameAt ? track.pathFrameAt(s, seg, fr) : track.frameAt(s, fr);
    for (let k = 0; k < 3; k++) out[k] = f.p[k] + f.b[k] * x + f.n[k] * h;
    return out;
  }

  // Presentation follows the same aim as flight. Preserve the committed normal
  // as well as position so a late target dodge cannot drag the landing marker.
  function updateLanding(pr, target) {
    if (!pr.homing && pr.landingP) return;
    pr.landingP ||= [0, 0, 0]; pr.landingN ||= [0, 1, 0];
    let n;
    if (pr.aimP) {
      for (let j = 0; j < 3; j++) pr.landingP[j] = pr.aimP[j];
      n = track.query(...pr.aimP, target.st[S.seg]).normal || pathFrame(target.st[S.s], target.st[S.seg]).n;
    } else {
      const s = wrap(pr.fromS + pr.aimD, L);
      point(s, pr.aimX, 0, pr.landingP);
      n = (track.surfaceAt ? track.surfaceAt(s, pr.aimX) : track.frameAt(s)).n;
    }
    for (let j = 0; j < 3; j++) pr.landingN[j] = n[j];
  }

  const pick = (track.items || []).map((it, i) => {
    const f = track.frameAt(it.s);
    return { kind: it.kind, s: it.s, x: it.x, downT: 0, upT: 1e9,
      v: { i, p: point(it.s, it.x, 0), n: f.n.slice(), t: f.t.slice(), b: f.b.slice(), alive: true, k: 1 } };
  });
  let projectiles = [], traps = [], waves = [], pops = [], R = [], links = [];
  const sinks = [];
  const sink = (i) => (i === 0 ? events : (sinks[i] ||= { emit: (name, p) => events.emit('racer-event', { racer: i, name, p }) }));
  const rs = (i) => (R[i] ||= { eff: null, viewEndT: -1e9, ledger: [], glass: 0, golden: 0, echo: 0, echoT: 0, snapGuard: 0 });
  const racer = (i) => (list[i] && list[i].i === i ? list[i] : list.find((r) => r.i === i));
  const free = (r) => !r.finished && (r.st[S.respawnPhase] === RESPAWN.NONE || r.st[S.respawnPhase] === RESPAWN.GRACE);
  const low = (r) => r.st[S.grounded] || Math.abs(r.st[S.height]) <= g('pickHeightM');
  const near = (r, s, x, rad, seg = -1) => {
    if (!track.forks?.length) return Math.abs(sd(r.st[S.s], s)) <= rad && Math.abs(r.st[S.lateral] - x) <= rad;
    const f = track.pathFrameAt(s, seg), delta = f.p.map((v, i) => r.st[S.posX + i] - v - f.b[i] * x);
    const dot = axis => delta.reduce((sum, v, i) => sum + v * axis[i], 0);
    return Math.abs(dot(f.t)) <= rad && Math.abs(dot(f.b)) <= rad && Math.abs(dot(f.n)) <= g('pickHeightM') + 1;
  };
  const pop = (kind, at, s, x, h = 0, seg = -1, worldP = null) => pops.push({ kind, at, s, x, h, seg, worldP, t: 0 });
  const flightPop = (pr, at) => pop(pr.kind, at, pr.s, pr.x, pr.h, pr.seg, pr.worldP?.slice() || null);
  const flightPoint = pr => pr.worldP || point(pr.s, pr.x, pr.h, undefined, pr.seg);
  const kartP = (r) => [r.st[S.posX], r.st[S.posY], r.st[S.posZ]];

  // ---- verdicts -------------------------------------------------------------------------------
  function usedView(x) {
    const W = g('viewWindowS');
    let used = 0;
    x.ledger = x.ledger.filter((e) => e.t1 > now - W);
    for (const e of x.ledger) used += e.mul * Math.max(0, Math.min(e.t1, now) - Math.max(e.t0, now - W));
    return used;
  }

  /** Judge and land one hit; emits item-hit. how: 'throw'|'lob'|'wide'|'trap'. @returns verdict */
  function hit(v, by, kind, how, p) {
    const x = rs(v.i), view = ITEMS[kind].view;
    let verdict = 'hit', d = durationOf(feel, kind);
    if (x.snapGuard > 0 && (how === 'throw' || how === 'lob' || how === 'trap')) verdict = 'snapped';
    else if (x.golden > 0) verdict = 'golden';
    else if (x.glass > 0) { verdict = 'glass'; x.glass = 0; }
    else if ((how === 'throw' || how === 'lob') && x.echo > 0 && rand() < g('echoDecoyChance')) { verdict = 'decoy'; x.echo--; }
    else if (VIEW_KINDS[view]) {
      const mul = view === 'mild' ? g('mildBlockMul') : 1;
      if (now >= x.viewEndT && now - x.viewEndT < g('mercyS')) verdict = 'mercy';
      else {
        d = Math.min(d, Math.max(0, g('viewBudgetS') - usedView(x)) / mul);
        if (d < g('minHitS')) verdict = 'mercy';
        else land(v, x, kind, d, by, mul);
      }
    } else land(v, x, kind, d, by, 0);
    if (verdict !== 'hit') d = 0;
    events.emit('item-hit', { victim: v.i, by, kind, how, verdict, durationS: d, p: p || kartP(v) });
    return verdict;
  }

  function land(v, x, kind, d, by, mul) {
    const out = v.i === 0 && feel.get('gentle.on') ? Math.max(g('effectOutS'), feel.get('gentle.fadeMs') / 1000) : g('effectOutS');
    const last = x.ledger[x.ledger.length - 1];
    if (last && last.t1 > now) last.t1 = now;                          // the old effect is replaced, never stacked
    if (x.eff && VIEW_KINDS[ITEMS[x.eff.kind].view]) x.viewEndT = now;
    x.eff = { kind, t: 0, durS: d, out, by };
    if (mul > 0) { x.ledger.push({ t0: now, t1: now + d, mul }); x.viewEndT = now + d + out; }
    if (SPIN_KINDS.includes(kind)) {
      const st = v.st;
      st[S.stalled] = d;
      st[S.engineV] *= g(kind + 'SpeedKeep');
      cancelDrift(st, 'stall', sink(v.i));
      endBoost(st, feel, sink(v.i));
    }
  }

  // ---- use ------------------------------------------------------------------------------------
  function aheadOf(r, st, rangeM) {
    let best = -1, bestD = Infinity;
    const me = st.progressOf(r.i);
    for (const o of list) {
      if (o === r || o.finished) continue;
      const progress = st.progressOf(o.i) - me;
      const d = pathGap(r.st[S.s], r.st[S.seg], o.st[S.s], o.st[S.seg]);
      if (progress > 0 && d > 0 && d <= rangeM && d < bestD) { best = o.i; bestD = d; }
    }
    return best;
  }

  /** A lob's target: the first racer in the standings that is not i and still racing (the leader, or
   * place 2 when i leads). -1 when nobody is left. */
  function lobTarget(i, standings) {
    for (const j of standings.order) {
      if (j === i) continue;
      const o = racer(j);
      if (o && !o.finished) return j;
    }
    return -1;
  }

  function use(r, standings) {
    const id = r.item.id, it = ITEMS[id], st = r.st, s = st[S.s], x = st[S.lateral];
    let mode = it.use === 'trap' || (r.input.back && (it.use === 'throw' || it.use === 'lob' || it.use === 'bounce')) ? 'drop' : it.use;
    const fork = track.forkAtSeg?.(st[S.seg]), seg = fork ? st[S.seg] : -1;
    const dropped = advance(s, seg, -g('dropBehindM')), dropS = dropped.s;
    if (id === 'shatter' && nearOpenEdge(mode === 'drop' ? dropS : s, g('shatterEdgeSafeM'), mode === 'drop' ? dropped.seg : seg)) {
      events.emit('item-refused', { racer: r.i, id, reason: 'edge' });
      return;
    }
    let target = -1, fired = null;
    if (mode === 'drop') {
      if (traps.length >= g('maxTraps')) { const o = traps.shift(); pop(o.kind, 'fizzle', o.s, o.x, 0, o.seg); }
      traps.push({ id: nextId++, kind: id, s: dropS, x, seg: dropped.seg, t: 0, layer: r.i });
    } else if (mode === 'throw') {
      target = aheadOf(r, standings, g('throwRangeM'));
      fired = { id: nextId++, kind: id, mode, by: r.i, target, t: 0, ...advance(s, seg, THROW_LEAD_M), x, h: g('throwHeightM'),
        v: Math.max(0, st[S.speedPct]) * feel.get('speed.topMps') + g('throwExtraMps'), homing: target >= 0 };
      projectiles.push(fired);
    } else if (mode === 'bounce') {
      // A Disc never homes: it leaves aimed at where the kart ahead is now (or angled to the steer side
      // with nobody ahead) and banks off rails, so a dodge makes it miss and come back off the wall.
      const v = Math.max(0, st[S.speedPct]) * feel.get('speed.topMps') + g('throwExtraMps'), maxVx = g('discLateralMps');
      const ahead = racer(aheadOf(r, standings, g('throwRangeM')));
      const vx = ahead ? clamp((ahead.st[S.lateral] - x) * v / Math.max(1, pathGap(s, seg, ahead.st[S.s], ahead.st[S.seg])), -maxVx, maxVx)
        : (Math.sign(r.input.steer || 0) || (rand() < 0.5 ? -1 : 1)) * maxVx;
      fired = { id: nextId++, kind: id, mode, by: r.i, target: -1, t: 0, ...advance(s, seg, THROW_LEAD_M), x, h: g('throwHeightM'),
        v, homing: false, vx, bounces: 0 };
      projectiles.push(fired);
    } else if (mode === 'tether') {
      // Hooks the kart ahead; the link holds until the tower passes it (then it spins), or range / time runs out.
      target = aheadOf(r, standings, g('tetherRangeM'));
      if (target < 0 || links.some(l => l.by === r.i || l.target === target)) return;   // nobody close: keep it
      links.push({ id: nextId++, by: r.i, target, t: 0 });
      events.emit('tether-hook', { by: r.i, target, p: kartP(racer(target)) });
    } else if (mode === 'lob') {
      target = lobTarget(r.i, standings);
      if (target < 0) return;   // alone on the road: keep it
      fired = { id: nextId++, kind: id, mode, by: r.i, target, t: 0, fromS: s, fromX: x, s, x, h: g('throwHeightM'),
        aimD: sd(racer(target).st[S.s], s), aimX: racer(target).st[S.lateral], homing: true, seg };
      if (track.forks?.length) {
        fired.fromP = point(s, x, g('throwHeightM'), undefined, seg);
        fired.aimP = kartP(racer(target));
        fired.worldP = fired.fromP.slice();
      }
      updateLanding(fired, racer(target));
      projectiles.push(fired);
    } else if (mode === 'wide') {
      const me = standings.progressOf(r.i);
      waves.push({ id: nextId++, kind: id, from: r.i, s, x, seg, t: 0 });
      for (const o of list) {
        const d = pathGap(s, seg, o.st[S.s], o.st[S.seg]);
        if (o !== r && free(o) && standings.progressOf(o.i) > me && d > 0 && d <= g('pinkRangeM')) hit(o, r.i, id, 'wide', null);
      }
    } else self(r, id, standings);
    r.item.id = null; r.item.rolling = false; r.item.rollT = 0; r.item.heldT = 0;
    events.emit('item-fire', { racer: r.i, id, mode, target, p: kartP(r),
      projectileId: fired ? fired.id : null, launchP: fired ? point(fired.s, fired.x, fired.h, undefined, fired.seg) : kartP(r), direction: pathFrame(s, seg).t.slice() });
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
      // A shockwave: nearby karts are shoved aside, a link on you breaks, and for snapGuardS anything that
      // reaches you pops (verdict 'snapped').
      x.snapGuard = g('snapGuardS');
      for (const l of links.filter(l => l.target === r.i)) breakLink(l, 'snap');
      const me = kartP(r), rad = g('snapRadiusM');
      for (const o of list) {
        if (o === r || !free(o) || Math.hypot(...kartP(o).map((v, j) => v - me[j])) > rad) continue;
        if (nearOpenEdge(o.st[S.s], g('shatterEdgeSafeM'), o.st[S.seg])) continue;   // never shove a kart off an open edge
        if (hit(o, r.i, 'snap', 'wide', kartP(o)) === 'hit') rs(o.i).eff.side = Math.sign(o.st[S.lateral] - r.st[S.lateral]) || 1;
      }
      pop('snap', 'hit', r.st[S.s], r.st[S.lateral], 1, r.st[S.seg]);   // the shockwave reads as an impact burst
      events.emit('snap-burst', { racer: r.i, p: me });
    }
  }

  // ---- tether links ---------------------------------------------------------------------------
  function breakLink(l, reason) {
    links = links.filter(k => k !== l);
    events.emit('tether-break', { by: l.by, target: l.target, reason });
  }

  function linksTick(dt, standings) {
    for (const l of links.slice()) {
      l.t += dt;
      const tower = racer(l.by), vic = racer(l.target);
      if (!tower || !vic || !free(tower) || !free(vic)) { breakLink(l, 'out'); continue; }
      // A shortcut or separated path can cross lap progress without physically passing the target.
      const distance = Math.abs(pathGap(tower.st[S.s], tower.st[S.seg], vic.st[S.s], vic.st[S.seg]));
      if (!Number.isFinite(distance) || distance > g('tetherBreakM')) { breakLink(l, 'range'); continue; }
      if (standings.progressOf(l.by) > standings.progressOf(l.target)) {
        breakLink(l, 'pass');
        // the spin is the one real crash of the kit: like Shatter, never near an open edge
        if (!nearOpenEdge(vic.st[S.s], g('shatterEdgeSafeM'), vic.st[S.seg])) hit(vic, l.by, 'tether', 'tether', kartP(vic));
      } else if (l.t >= g('tetherMaxS')) breakLink(l, 'timeout');
    }
  }

  // ---- per tick -------------------------------------------------------------------------------
  function pickups(r, standings) {
    if (!low(r)) return;
    const boxR = g('boxRadiusM'), bubbleR = g('bubbleRadiusM');   // read once per racer, not per pickup
    for (let i = 0; i < pick.length; i++) {
      const b = pick[i];
      if (b.downT > 0) continue;
      if (b.kind === 'box') {
        if (r.item.id || r.item.rolling || !near(r, b.s, b.x, boxR)) continue;
        b.downT = g('boxRespawnS'); b.upT = 0;
        const id = rollItem(feel, bucketOf(feel, standings.placeOf(r.i), standings.gapToLeader(r.i)), rand);
        r.item.id = id; r.item.rolling = true; r.item.rollT = 0; r.item.heldT = 0;
        pop('box', 'box', b.s, b.x);
        events.emit('item-box', { racer: r.i, box: i, p: b.v.p.slice() });
      } else if (near(r, b.s, b.x, bubbleR)) {
        b.downT = g('bubbleRespawnS'); b.upT = 0;
        grantBoost(r.st, g('bubbleAmount'), g('bubbleHoldS'), 'bubble', feel, sink(r.i));
        pop('bubble', 'bubble', b.s, b.x);
        events.emit('bubble-pop', { racer: r.i, i, p: b.v.p.slice() });
      }
    }
  }

  function tickRacer(r, dt) {
    const it = r.item, x = rs(r.i);
    if (it.rolling) {
      it.rollT += dt;
      if (it.rollT >= g('rollS')) { it.rolling = false; events.emit('item-roll', { racer: r.i, id: it.id }); }
    } else if (it.id) it.heldT += dt;
    if (x.eff && (x.eff.t += dt) >= x.eff.durS + x.eff.out) x.eff = null;
    if (x.glass > 0 && (x.glass -= dt) <= 0) { x.glass = 0; pop('glass', 'glass', r.st[S.s], r.st[S.lateral], 1, r.st[S.seg]); }
    if (x.golden > 0) x.golden = Math.max(0, x.golden - dt);
    if (x.echo > 0 && (x.echoT -= dt) <= 0) x.echo = 0;
    if (x.snapGuard > 0) x.snapGuard = Math.max(0, x.snapGuard - dt);
  }

  function flights(dt) {
    projectiles = projectiles.filter((pr) => {
      pr.t += dt;
      const oldP = flightPoint(pr).slice(), oldS = pr.s, oldX = pr.x;
      const oldGround = track.forks?.length ? point(pr.s, pr.x, 0, undefined, pr.seg) : null;
      const tg = pr.target >= 0 ? racer(pr.target) : null;
      if (pr.mode === 'lob') {
        const k = Math.min(1, pr.t / g('lobFlightS'));
        if (!tg || !free(tg)) { flightPop(pr, 'fizzle'); return false; }
        // Commit the landing point before the terminal dive. Unwrapped distance prevents a
        // moving target across the half-lap boundary from flipping the whole trajectory.
        pr.homing = k < g('lobCommitFrac');
        if (pr.homing) {
          const f = pathFrame(tg.st[S.s], tg.st[S.seg]), remaining = Math.max(0, g('lobFlightS') - pr.t);
          const speed = tg.st[S.velX] * f.t[0] + tg.st[S.velY] * f.t[1] + tg.st[S.velZ] * f.t[2];
          if (pr.worldP) {
            const wantedP = kartP(tg);
            for (let j = 0; j < 3; j++) wantedP[j] += tg.st[S.velX + j] * remaining;
            const distance = Math.hypot(...wantedP.map((v, j) => v - pr.aimP[j]));
            const gain = distance > 0 ? Math.min(1, g('lobRetargetMps') * dt / distance) : 0;
            for (let j = 0; j < 3; j++) pr.aimP[j] += (wantedP[j] - pr.aimP[j]) * gain;
          }
          const wanted = pr.aimD + sd(tg.st[S.s], wrap(pr.fromS + pr.aimD, L)) + speed * remaining;
          pr.aimD += clamp(wanted - pr.aimD, -g('lobRetargetMps') * dt, g('lobRetargetMps') * dt);
          pr.aimX += clamp(tg.st[S.lateral] - pr.aimX, -g('homeLateralMps') * dt, g('homeLateralMps') * dt);
        }
        updateLanding(pr, tg);
        pr.s = wrap(pr.fromS + pr.aimD * k, L);
        pr.x = lerp(pr.fromX, pr.aimX, k);
        pr.h = g('throwHeightM') + g('lobHeightM') * 4 * k * (1 - k);
        if (pr.worldP) for (let j = 0; j < 3; j++) pr.worldP[j] = lerp(pr.fromP[j], pr.aimP[j], k) + (j === 1 ? g('lobHeightM') * 4 * k * (1 - k) : 0);
        velocity(pr, oldP, dt);
        if (k < 1) return true;
        if (pr.worldP ? !worldNear(tg, pr.worldP, pathFrame(tg.st[S.s], tg.st[S.seg]).n, g('hitRadiusM')) : Math.hypot(sd(tg.st[S.s], pr.s), tg.st[S.lateral] - pr.x) > g('hitRadiusM') || !sameLevel(tg, pr.s, pr.x, pr.seg)) { flightPop(pr, 'fizzle'); return false; }
        const verdict = hit(tg, pr.by, pr.kind, 'lob', flightPoint(pr).slice());
        flightPop(pr, verdict === 'mercy' ? 'hit' : verdict);
        return false;
      }
      if (pr.t >= g(pr.mode === 'bounce' ? 'discLifeS' : 'throwLifeS')) { flightPop(pr, 'fizzle'); return false; }
      let stepM = pr.v * dt;
      const shatter = pr.kind === 'shatter' || pr.kind === 'disc';
      if (pr.mode === 'bounce') {
        pr.x += pr.vx * dt;
        const f = pathFrame(pr.s, pr.seg), half = (Number.isFinite(f.w) ? f.w : 12) / 2 - g('projectileRadiusM'), side = Math.sign(pr.x);
        if (Math.abs(pr.x) > half) {
          const flags = f.flags || [];
          if (!(flags.includes('rail') || flags.includes(side > 0 ? 'railR' : 'railL'))) { flightPop(pr, 'fizzle'); return false; }
          pr.x = side * half; pr.vx = -pr.vx;
          if (++pr.bounces > g('discBounces')) { flightPop(pr, 'fizzle'); return false; }
          events.emit('disc-bounce', { by: pr.by, projectileId: pr.id, p: flightPoint(pr).slice() });
        }
      }
      const gap = tg ? pathGap(pr.s, pr.seg, tg.st[S.s], tg.st[S.seg]) : -1;
      pr.homing = !!(tg && free(tg) && gap > 0 && gap <= g('throwRangeM'));
      if (pr.homing) {
        pr.x += clamp(damp(pr.x, tg.st[S.lateral], g('homeTcS'), dt) - pr.x, -g('homeLateralMps') * dt, g('homeLateralMps') * dt);
        if (shatter && nearOpenEdge(tg.st[S.s], g('shatterEdgeSafeM'), tg.st[S.seg])) {
          stepM = Math.min(stepM, Math.max(0, gap - SHATTER_WAIT_M));
        }
      }
      Object.assign(pr, advance(pr.s, pr.seg, stepM));
      pr.h = g('throwHeightM') + g('throwArcM') * Math.sin(Math.PI * Math.min(1, pr.t / ARC_S));
      velocity(pr, oldP, dt);
      const ground = oldGround ? point(pr.s, pr.x, 0, undefined, pr.seg) : null;
      for (const o of list) {
        if (o.i === pr.by || !free(o) || !low(o)) continue;
        if (oldGround ? !worldSweep(o, oldGround, ground, pathFrame(pr.s, pr.seg).n, g('hitRadiusM')) : !sweptNear(o, oldS, oldX, pr.x, stepM, g('hitRadiusM')) || !sameLevel(o, pr.s, pr.x, pr.seg)) continue;
        if (shatter && nearOpenEdge(o.st[S.s], g('shatterEdgeSafeM'), o.st[S.seg])) continue;
        const verdict = hit(o, pr.by, pr.kind, 'throw', flightPoint(pr).slice());
        flightPop(pr, verdict === 'mercy' ? 'hit' : verdict);
        return false;
      }
      return true;
    });
  }

  function velocity(pr, oldP, dt) {
    const p = flightPoint(pr);
    pr.velocity ||= [0, 0, 0];
    for (let k = 0; k < 3; k++) pr.velocity[k] = dt > 0 ? (p[k] - oldP[k]) / dt : 0;
  }
  function sameLevel(r, s, x, seg = -1) {
    const p = point(s, x, 0, undefined, seg), f = pathFrame(s, seg);
    const h = (r.st[S.posX] - p[0]) * f.n[0] + (r.st[S.posY] - p[1]) * f.n[1] + (r.st[S.posZ] - p[2]) * f.n[2];
    return Math.abs(h) <= g('pickHeightM');
  }
  function worldNear(r, p, normal, radius) { return worldSweep(r, p, p, normal, radius); }
  function worldSweep(r, from, to, normal, radius) {
    const delta = kartP(r).map((v, j) => v - from[j]), line = to.map((v, j) => v - from[j]);
    const dot = (a, b) => a.reduce((sum, v, j) => sum + v * b[j], 0);
    const height = dot(delta, normal), rise = dot(line, normal);
    const flatDelta = delta.map((v, j) => v - normal[j] * height), flatLine = line.map((v, j) => v - normal[j] * rise);
    const len2 = dot(flatLine, flatLine), k = len2 > 0 ? clamp(dot(flatDelta, flatLine) / len2, 0, 1) : 0;
    return Math.abs(height - rise * k) <= g('pickHeightM') && Math.hypot(...flatDelta.map((v, j) => v - flatLine[j] * k)) <= radius;
  }
  function sweptNear(r, fromS, fromX, toX, stepM, radius) {
    const d = sd(r.st[S.s], fromS), dx = r.st[S.lateral] - fromX, sweepX = toX - fromX;
    const len2 = stepM * stepM + sweepX * sweepX;
    const k = len2 > 0 ? clamp((d * stepM + dx * sweepX) / len2, 0, 1) : 1;
    return Math.hypot(d - stepM * k, dx - sweepX * k) <= radius;
  }

  function trapsTick(dt) {
    traps = traps.filter((tp) => {
      tp.t += dt;
      if (tp.t >= g('trapLifeS')) { pop(tp.kind, 'fizzle', tp.s, tp.x, 0, tp.seg); return false; }
      const rad = tp.kind === 'melt' ? g('meltRadiusM') : g('trapRadiusM');
      for (const o of list) {
        if ((o.i === tp.layer && tp.t < g('trapArmS')) || !free(o) || !low(o)
          || (track.forks?.length ? !worldNear(o, point(tp.s, tp.x, 0, undefined, tp.seg), pathFrame(tp.s, tp.seg).n, rad) : Math.hypot(sd(o.st[S.s], tp.s), o.st[S.lateral] - tp.x) > rad || !sameLevel(o, tp.s, tp.x, tp.seg))) continue;
        if (SPIN_KINDS.includes(tp.kind) && nearOpenEdge(o.st[S.s], g('shatterEdgeSafeM'), o.st[S.seg])) continue;
        const verdict = hit(o, tp.layer, tp.kind, 'trap', point(tp.s, tp.x, 0, undefined, tp.seg));
        pop(tp.kind, verdict === 'glass' || verdict === 'golden' ? verdict : 'trap', tp.s, tp.x, 0, tp.seg);
        return false;
      }
      return true;
    });
  }

  function writeMods(r) {
    const m = r.mods, x = rs(r.i), e = x.eff, fxv = r.fxv;
    m.steerMul = 1; m.steerAdd = 0; m.throttleMul = 1; m.gripMul = 1; m.slideMul = 1; m.capMul = 1; m.shoveMul = 1; m.blind = 0;
    fxv.spinRad = 0;
    fxv.effect = e ? e.kind : null;
    if (e && e.t < e.durS) {
      if (e.kind === 'spiral') m.steerAdd = g('spiralSteerAmp') * Math.sin(2 * Math.PI * g('spiralSteerHz') * e.t);
      else if (e.kind === 'freeze') m.steerMul = g('freezeSteerMul');
      else if (e.kind === 'melt') { m.gripMul = g('meltGripMul'); m.slideMul = g('meltSlideMul'); }
      else if (e.kind === 'pendulum') m.steerAdd = g('pendulumSteerAmp') * Math.sin(2 * Math.PI * g('pendulumSteerHz') * e.t);
      else if (e.kind === 'snap') m.steerAdd = (e.side || 1) * g('snapShoveSteer') * (1 - e.t / e.durS);
      else if (e.kind === 'bigSpiral') m.capMul = g('bigSpiralCapMul');
      else if (SPIN_KINDS.includes(e.kind)) {
        const k = e.t / e.durS;
        fxv.spinRad = Math.round(g(e.kind + 'SpinTurns')) * 2 * Math.PI * (1 - (1 - k) * (1 - k));
      }
      if (r.meta && r.meta.isBot && ITEMS[e.kind].bot === 'blind') m.blind = 1;
    }
    if (x.golden > 0) m.shoveMul = g('goldenShoveMul');
    fxv.shield = x.golden > 0 ? 'golden' : x.glass > 0 ? 'glass' : null;
    fxv.echo = x.echo;
    for (const l of links) {
      if (l.by === r.i) m.capMul *= g('tetherTowCap');
      else if (l.target === r.i) m.capMul *= g('tetherDragCap');
    }
  }

  function update(dt, racers, standings, race) {
    if (!race || (race.phase !== 'racing' && race.phase !== 'finished')) return;
    list = racers;
    now += dt; ver++;
    for (const b of pick) {
      if (b.downT > 0) b.downT = Math.max(0, b.downT - dt);
      else b.upT += dt;
    }
    for (const r of racers) tickRacer(r, dt);
    for (const r of racers) {
      if (!free(r)) continue;
      pickups(r, standings);
      if (r.input && r.input.item && r.item.id && !r.item.rolling) use(r, standings);
    }
    flights(dt);
    trapsTick(dt);
    linksTick(dt, standings);
    for (const w of waves) w.t += dt;
    waves = waves.filter((w) => w.t < WAVE_S);
    for (const p of pops) p.t += dt;
    pops = pops.filter((p) => p.t < g('popShowS'));
    for (const r of racers) writeMods(r);
  }

  // ---- view -----------------------------------------------------------------------------------
  // Built at most once per tick (ver moves on update and reset) into pooled rows: the arrays and their
  // row objects are reused, so a reader must not keep a row past the next tick.
  const V = { boxes: [], bubbles: [], held: [], projectiles: [], traps: [], waves: [], shields: [], pops: [], tethers: [], guards: [] };
  for (const b of pick) (b.kind === 'box' ? V.boxes : V.bubbles).push(b.v);
  const POOL = { held: [], projectiles: [], traps: [], waves: [], shields: [], pops: [], tethers: [], guards: [] };
  let builtVer = -1;
  /** Next pooled row of V[key] (call after V[key].length = 0). */
  function row(key) {
    const out = V[key], pool = POOL[key], k = out.length;
    if (k >= pool.length) pool.push(key === 'traps' ? { p: [0, 0, 0], n: [0, 0, 0] } : key === 'tethers' ? { from: [0, 0, 0], to: [0, 0, 0] }
      : key === 'held' || key === 'shields' || key === 'guards' ? {} : { p: [0, 0, 0] });
    out.push(pool[k]);
    return pool[k];
  }
  function shield(r, kind, t, k, copies) { const o = row('shields'); o.racer = r.i; o.kind = kind; o.t = t; o.k = k; o.copies = copies; }
  function view() {
    if (builtVer === ver) return V;
    builtVer = ver;
    for (const b of pick) { b.v.alive = b.downT <= 0; b.v.k = b.v.alive ? Math.min(1, b.upT / GROW_S) : 0; }
    for (const key in POOL) V[key].length = 0;
    for (const r of list) {
      if (!r.item || !(r.item.id || r.item.rolling)) continue;
      const o = row('held'); o.racer = r.i; o.id = r.item.id; o.rolling = r.item.rolling; o.rollT = r.item.rollT;
    }
    for (const pr of projectiles) {
      const o = row('projectiles');
      o.id = pr.id; o.kind = pr.kind; o.mode = pr.mode; point(pr.s, pr.x, pr.h, o.p, pr.seg); if (pr.worldP) for (let j = 0; j < 3; j++) o.p[j] = pr.worldP[j]; o.t = pr.t; o.target = pr.target; o.s = pr.s; o.x = pr.x;
      o.by = pr.by; o.homing = pr.homing; o.radiusM = g(pr.mode === 'lob' ? 'lobRadiusM' : 'projectileRadiusM');
      o.progress = Math.min(1, pr.t / (pr.mode === 'lob' ? g('lobFlightS') : ARC_S));
      o.v ||= [0, 0, 0]; for (let k = 0; k < 3; k++) o.v[k] = pr.velocity ? pr.velocity[k] : 0;
      if (pr.mode === 'lob') {
        o.landingP ||= [0, 0, 0]; o.landingN ||= [0, 1, 0];
        for (let j = 0; j < 3; j++) { o.landingP[j] = pr.landingP[j]; o.landingN[j] = pr.landingN[j]; }
      } else { o.landingP = null; o.landingN = null; }
    }
    const armS = g('trapArmS');
    for (const tp of traps) {
      const o = row('traps'), f = track.pathFrameAt ? track.pathFrameAt(tp.s, tp.seg, fr) : track.frameAt(tp.s, fr);
      for (let k = 0; k < 3; k++) { o.p[k] = f.p[k] + f.b[k] * tp.x; o.n[k] = f.n[k]; }
      o.id = tp.id; o.kind = tp.kind; o.t = tp.t; o.armed = tp.t >= armS; o.s = tp.s; o.x = tp.x;
      o.radiusM = g(tp.kind === 'melt' ? 'meltRadiusM' : 'trapRadiusM');
    }
    for (const w of waves) {
      const o = row('waves');
      o.id = w.id; o.kind = w.kind; o.from = w.from; point(w.s, w.x, 0, o.p, w.seg); o.t = w.t; o.rangeM = g('pinkRangeM');
    }
    for (const r of list) {
      const x = R[r.i];
      if (!x) continue;
      if (x.glass > 0) shield(r, 'glass', g('glassMaxS') - x.glass, x.glass / g('glassMaxS'), 0);
      if (x.golden > 0) shield(r, 'golden', g('goldenS') - x.golden, x.golden / g('goldenS'), 0);
      if (x.echo > 0) shield(r, 'echo', g('echoS') - x.echoT, Math.max(0, x.echoT / g('echoS')), x.echo);
      if (x.snapGuard > 0) { const o = row('guards'); o.racer = r.i; o.k = x.snapGuard / g('snapGuardS'); }
    }
    for (const l of links) {
      const a = racer(l.by), b = racer(l.target);
      if (!a || !b) continue;
      const o = row('tethers'); o.id = l.id; o.by = l.by; o.target = l.target; o.t = l.t; o.k = Math.min(1, l.t / g('tetherMaxS'));
      for (let j = 0; j < 3; j++) { o.from[j] = a.st[S.posX + j]; o.to[j] = b.st[S.posX + j]; }
    }
    for (const p of pops) { const o = row('pops'); o.kind = p.kind; o.at = p.at; point(p.s, p.x, p.h, o.p, p.seg); if (p.worldP) for (let j = 0; j < 3; j++) o.p[j] = p.worldP[j]; o.t = p.t; }
    return V;
  }

  /** The live traps on the road, read-only rows { id, kind, s, x, t, layer }: the cheap query for the bots
   * (no frames, no copies). Do not keep or edit them. */
  const trapRows = () => traps;

  function reset() {
    rand = makeRand(seed); now = 0; nextId = 1; ver++;
    projectiles = []; traps = []; waves = []; pops = []; R = []; links = [];
    for (const b of pick) { b.downT = 0; b.upT = 1e9; }
  }

  return { update, view, reset, nearOpenEdge, trapRows, lobTarget };
}
