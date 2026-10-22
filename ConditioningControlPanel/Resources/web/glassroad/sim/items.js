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
import { wrap, damp, lerp } from '../core/math.js';
import { grantBoost } from './grant.js';
import { cancelDrift } from './drift.js';
import { endBoost } from './boost.js';
import { ITEMS, bucketOf, rollItem, durationOf, makeRand, BUCKETS } from './itemTable.js';

const GROW_S = 0.4;          // feel gap: a respawned box or bubble grows in over this
const WAVE_S = 1.2;          // feel gap: a Pink wave stays in the view this long
const THROW_LEAD_M = 2.5;    // feel gap: a throw leaves this far ahead of the user
const ARC_S = 0.5;           // feel gap: the first arc out of the hand lasts this long
const SHATTER_WAIT_M = 6;    // feel gap: a Shatter throw waits this far behind a victim near an open edge

const VIEW_KINDS = { full: 1, mild: 1 };

export function createItems(track, feel, events, { seed = 1 } = {}) {
  const L = track.lengthM;
  const g = (k) => feel.get('items.' + k);
  const sd = (a, b) => { const d = wrap(a - b, L); return d > L / 2 ? d - L : d; };   // signed a - b
  let rand = makeRand(seed), now = 0, nextId = 1, list = [], ver = 0;

  // Open stretches: any segment without rails on both sides (open, railL, railR, the jump gap).
  const open = [];
  for (let i = 0; i < track.nodeCount; i++) {
    if (!(track.nodeFlags(i) || []).includes('rail')) open.push([track.nodeS[i], track.nodeS[i + 1] - track.nodeS[i]]);
  }
  function nearOpenEdge(s, m) {
    for (const [a, len] of open) { const d = wrap(s - a, L); if (d <= len + m || d >= L - m) return true; }
    return false;
  }

  const fr = track.frameAt(0);   // scratch frame, reused
  function point(s, x, h, out = [0, 0, 0]) {
    const f = track.frameAt(s, fr);
    for (let k = 0; k < 3; k++) out[k] = f.p[k] + f.b[k] * x + f.n[k] * h;
    return out;
  }

  const pick = (track.items || []).map((it, i) => {
    const f = track.frameAt(it.s);
    return { kind: it.kind, s: it.s, x: it.x, downT: 0, upT: 1e9,
      v: { i, p: point(it.s, it.x, 0), n: f.n.slice(), t: f.t.slice(), b: f.b.slice(), alive: true, k: 1 } };
  });
  let projectiles = [], traps = [], waves = [], pops = [], R = [];
  const sinks = [];
  const sink = (i) => (i === 0 ? events : (sinks[i] ||= { emit: (name, p) => events.emit('racer-event', { racer: i, name, p }) }));
  const rs = (i) => (R[i] ||= { eff: null, viewEndT: -1e9, ledger: [], glass: 0, golden: 0, echo: 0, echoT: 0 });
  const racer = (i) => (list[i] && list[i].i === i ? list[i] : list.find((r) => r.i === i));
  const free = (r) => !r.finished && (r.st[S.respawnPhase] === RESPAWN.NONE || r.st[S.respawnPhase] === RESPAWN.GRACE);
  const low = (r) => r.st[S.grounded] || r.st[S.height] <= g('pickHeightM');
  const near = (r, s, x, rad) => Math.abs(sd(r.st[S.s], s)) <= rad && Math.abs(r.st[S.lateral] - x) <= rad;
  const pop = (kind, at, s, x, h = 0) => pops.push({ kind, at, s, x, h, t: 0 });
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
    if (x.golden > 0) verdict = 'golden';
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
    events.emit('item-hit', { victim: v.i, by, kind, verdict, durationS: d, p: p || kartP(v) });
    return verdict;
  }

  function land(v, x, kind, d, by, mul) {
    const out = v.i === 0 && feel.get('gentle.on') ? Math.max(g('effectOutS'), feel.get('gentle.fadeMs') / 1000) : g('effectOutS');
    const last = x.ledger[x.ledger.length - 1];
    if (last && last.t1 > now) last.t1 = now;                          // the old effect is replaced, never stacked
    if (x.eff && VIEW_KINDS[ITEMS[x.eff.kind].view]) x.viewEndT = now;
    x.eff = { kind, t: 0, durS: d, out, by };
    if (mul > 0) { x.ledger.push({ t0: now, t1: now + d, mul }); x.viewEndT = now + d + out; }
    if (kind === 'shatter') {
      const st = v.st;
      st[S.stalled] = g('shatterS');
      st[S.engineV] *= g('shatterSpeedKeep');
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
      const d = st.progressOf(o.i) - me;
      if (d > 0 && d <= rangeM && d < bestD) { best = o.i; bestD = d; }
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
    let mode = it.use === 'trap' || (r.input.back && (it.use === 'throw' || it.use === 'lob')) ? 'drop' : it.use;
    const dropS = wrap(s - g('dropBehindM'), L);
    if (id === 'shatter' && nearOpenEdge(mode === 'drop' ? dropS : s, g('shatterEdgeSafeM'))) {
      events.emit('item-refused', { racer: r.i, id, reason: 'edge' });
      return;
    }
    let target = -1;
    if (mode === 'drop') {
      if (traps.length >= g('maxTraps')) { const o = traps.shift(); pop(o.kind, 'fizzle', o.s, o.x); }
      traps.push({ id: nextId++, kind: id, s: dropS, x, t: 0, layer: r.i });
    } else if (mode === 'throw') {
      target = aheadOf(r, standings, g('throwRangeM'));
      projectiles.push({ id: nextId++, kind: id, mode, by: r.i, target, t: 0, s: wrap(s + THROW_LEAD_M, L), x, h: g('throwHeightM'),
        v: Math.max(0, st[S.speedPct]) * feel.get('speed.topMps') + g('throwExtraMps') });
    } else if (mode === 'lob') {
      target = lobTarget(r.i, standings);
      if (target < 0) return;   // alone on the road: keep it
      projectiles.push({ id: nextId++, kind: id, mode, by: r.i, target, t: 0, fromS: s, fromX: x, s, x, h: g('throwHeightM') });
    } else if (mode === 'wide') {
      const me = standings.progressOf(r.i);
      waves.push({ id: nextId++, kind: id, from: r.i, s, x, t: 0 });
      for (const o of list) {
        const d = standings.progressOf(o.i) - me;
        if (o !== r && free(o) && d > 0 && d <= g('pinkRangeM')) hit(o, r.i, id, 'wide', null);
      }
    } else self(r, id, standings);
    r.item.id = null; r.item.rolling = false; r.item.rollT = 0; r.item.heldT = 0;
    events.emit('item-fire', { racer: r.i, id, mode, target, p: kartP(r) });
  }

  function self(r, id, standings) {
    const x = rs(r.i);
    if (id === 'surge') {
      const b = BUCKETS.indexOf(bucketOf(feel, standings.placeOf(r.i), standings.gapToLeader(r.i)));
      grantBoost(r.st, g('surgeAmount')[Math.max(0, b)], g('surgeS'), 'surge', feel, sink(r.i));
    } else if (id === 'glass') x.glass = g('glassMaxS');
    else if (id === 'echo') { x.echo = g('echoCopies'); x.echoT = g('echoS'); }
    else if (id === 'golden') x.golden = g('goldenS');
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
    if (x.glass > 0 && (x.glass -= dt) <= 0) { x.glass = 0; pop('glass', 'glass', r.st[S.s], r.st[S.lateral], 1); }
    if (x.golden > 0) x.golden = Math.max(0, x.golden - dt);
    if (x.echo > 0 && (x.echoT -= dt) <= 0) x.echo = 0;
  }

  function flights(dt) {
    projectiles = projectiles.filter((pr) => {
      pr.t += dt;
      const tg = pr.target >= 0 ? racer(pr.target) : null;
      if (pr.mode === 'lob') {
        const k = Math.min(1, pr.t / g('lobFlightS'));
        if (!tg) { pop(pr.kind, 'fizzle', pr.s, pr.x, pr.h); return false; }
        pr.s = wrap(pr.fromS + sd(tg.st[S.s], pr.fromS) * k, L);
        pr.x = lerp(pr.fromX, tg.st[S.lateral], k);
        pr.h = g('throwHeightM') + g('lobHeightM') * 4 * k * (1 - k);
        if (k < 1) return true;
        if (!free(tg)) { pop(pr.kind, 'fizzle', pr.s, pr.x, pr.h); return false; }
        const verdict = hit(tg, pr.by, pr.kind, 'lob', point(pr.s, pr.x, pr.h));
        pop(pr.kind, verdict === 'mercy' ? 'hit' : verdict, pr.s, pr.x, pr.h);
        return false;
      }
      if (pr.t >= g('throwLifeS')) { pop(pr.kind, 'fizzle', pr.s, pr.x, pr.h); return false; }
      pr.s = wrap(pr.s + pr.v * dt, L);
      const shatter = pr.kind === 'shatter';
      if (tg && free(tg)) {
        pr.x = damp(pr.x, tg.st[S.lateral], g('homeTcS'), dt);
        if (shatter && nearOpenEdge(tg.st[S.s], g('shatterEdgeSafeM')) && sd(tg.st[S.s], pr.s) < SHATTER_WAIT_M) {
          pr.s = wrap(tg.st[S.s] - SHATTER_WAIT_M, L);
        }
      }
      pr.h = g('throwHeightM') + g('throwArcM') * Math.sin(Math.PI * Math.min(1, pr.t / ARC_S));
      for (const o of list) {
        if (o.i === pr.by || !free(o) || !low(o) || !near(o, pr.s, pr.x, g('hitRadiusM'))) continue;
        if (shatter && nearOpenEdge(o.st[S.s], g('shatterEdgeSafeM'))) continue;
        const verdict = hit(o, pr.by, pr.kind, 'throw', point(pr.s, pr.x, pr.h));
        pop(pr.kind, verdict === 'mercy' ? 'hit' : verdict, pr.s, pr.x, pr.h);
        return false;
      }
      return true;
    });
  }

  function trapsTick(dt) {
    traps = traps.filter((tp) => {
      tp.t += dt;
      if (tp.t >= g('trapLifeS')) { pop(tp.kind, 'fizzle', tp.s, tp.x); return false; }
      const rad = tp.kind === 'melt' ? g('meltRadiusM') : g('trapRadiusM');
      for (const o of list) {
        if ((o.i === tp.layer && tp.t < g('trapArmS')) || !free(o) || !low(o) || !near(o, tp.s, tp.x, rad)) continue;
        if (tp.kind === 'shatter' && nearOpenEdge(o.st[S.s], g('shatterEdgeSafeM'))) continue;
        const verdict = hit(o, tp.layer, tp.kind, 'trap', point(tp.s, tp.x, 0));
        pop(tp.kind, verdict === 'glass' || verdict === 'golden' ? verdict : 'trap', tp.s, tp.x);
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
      else if (e.kind === 'shatter') {
        const k = e.t / e.durS;
        fxv.spinRad = Math.round(g('shatterSpinTurns')) * 2 * Math.PI * (1 - (1 - k) * (1 - k));
      }
      if (r.meta && r.meta.isBot && ITEMS[e.kind].bot === 'blind') m.blind = 1;
    }
    if (x.golden > 0) m.shoveMul = g('goldenShoveMul');
    fxv.shield = x.golden > 0 ? 'golden' : x.glass > 0 ? 'glass' : null;
    fxv.echo = x.echo;
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
    for (const w of waves) w.t += dt;
    waves = waves.filter((w) => w.t < WAVE_S);
    for (const p of pops) p.t += dt;
    pops = pops.filter((p) => p.t < g('popShowS'));
    for (const r of racers) writeMods(r);
  }

  // ---- view -----------------------------------------------------------------------------------
  // Built at most once per tick (ver moves on update and reset) into pooled rows: the arrays and their
  // row objects are reused, so a reader must not keep a row past the next tick.
  const V = { boxes: [], bubbles: [], held: [], projectiles: [], traps: [], waves: [], shields: [], pops: [] };
  for (const b of pick) (b.kind === 'box' ? V.boxes : V.bubbles).push(b.v);
  const POOL = { held: [], projectiles: [], traps: [], waves: [], shields: [], pops: [] };
  let builtVer = -1;
  /** Next pooled row of V[key] (call after V[key].length = 0). */
  function row(key) {
    const out = V[key], pool = POOL[key], k = out.length;
    if (k >= pool.length) pool.push(key === 'traps' ? { p: [0, 0, 0], n: [0, 0, 0] } : key === 'held' || key === 'shields' ? {} : { p: [0, 0, 0] });
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
      o.id = pr.id; o.kind = pr.kind; o.mode = pr.mode; point(pr.s, pr.x, pr.h, o.p); o.t = pr.t; o.target = pr.target; o.s = pr.s; o.x = pr.x;
    }
    const armS = g('trapArmS');
    for (const tp of traps) {
      const o = row('traps'), f = track.frameAt(tp.s, fr);
      for (let k = 0; k < 3; k++) { o.p[k] = f.p[k] + f.b[k] * tp.x; o.n[k] = f.n[k]; }
      o.id = tp.id; o.kind = tp.kind; o.t = tp.t; o.armed = tp.t >= armS; o.s = tp.s; o.x = tp.x;
    }
    for (const w of waves) {
      const o = row('waves');
      o.id = w.id; o.kind = w.kind; o.from = w.from; point(w.s, w.x, 0, o.p); o.t = w.t; o.rangeM = g('pinkRangeM');
    }
    for (const r of list) {
      const x = R[r.i];
      if (!x) continue;
      if (x.glass > 0) shield(r, 'glass', g('glassMaxS') - x.glass, x.glass / g('glassMaxS'), 0);
      if (x.golden > 0) shield(r, 'golden', g('goldenS') - x.golden, x.golden / g('goldenS'), 0);
      if (x.echo > 0) shield(r, 'echo', g('echoS') - x.echoT, Math.max(0, x.echoT / g('echoS')), x.echo);
    }
    for (const p of pops) { const o = row('pops'); o.kind = p.kind; o.at = p.at; point(p.s, p.x, p.h, o.p); o.t = p.t; }
    return V;
  }

  /** The live traps on the road, read-only rows { id, kind, s, x, t, layer }: the cheap query for the bots
   * (no frames, no copies). Do not keep or edit them. */
  const trapRows = () => traps;

  function reset() {
    rand = makeRand(seed); now = 0; nextId = 1; ver++;
    projectiles = []; traps = []; waves = []; pops = []; R = [];
    for (const b of pick) { b.downT = 0; b.upT = 1e9; }
  }

  return { update, view, reset, nearOpenEdge, trapRows, lobTarget };
}
