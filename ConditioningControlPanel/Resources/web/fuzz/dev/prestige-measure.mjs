// dev/prestige-measure.mjs - Renewal Sky pacing: staying-player runs at Depth 0..10, minutes to Drop and Stardust earned.
//   node dev/prestige-measure.mjs            (6 routes, 4 and 2 taps a second, three skies)
//   node dev/prestige-measure.mjs --json out.json
// Skies: "real" = a player who renews after every run and buys stars cheapest-first with the Stardust each run earned
// (always the first word, no Fine Print rules, Drop lit); "open" = every star the Depth allows (n <= Depth per lane);
// "full" = all 80 stars and the core.
//
// Same loop as dev/journey-timing.mjs (the reference player that pins the first run at 90 minutes), plus the Sky: the
// run stamp (pay rides on pace, prices and minutes on the Journey plan, start money, free buys, Skip Ahead) and the boost
// lanes through the real helpers in ui/unlocks/prestige/boosts.js, Trigger Words through sim/prestige-words.js and Ghost
// Run through sim/ghost.js. Every prize (Golden Buttons, words, Countdown) goes through E.sideLump like ctx.pay does.
// Modelled as a staying player would play them: Combo always live, every Countdown perfect, every word caught (half of
// them Instant), always ahead of the ghost from chapter 1, ghost hands as auto taps. Not modelled: Crit Chance (+5% of a
// rare event), Sleepwalk (this player buys as soon as anything is affordable anyway), Interest (gaps stay under a minute),
// toys owned through Keepsake (inside the prize share), away pay (a staying player is never away).
// The pure loop misses what unlock code pays on top; a browser cross-check is dev/measure.mjs --journey 1.
import { pathToFileURL } from 'node:url';
import { writeFileSync } from 'node:fs';
import { allRoutes } from '../sim/route.js';
import { createJourney, EXTRAS } from '../sim/journey.js';
import { takeActivity, journeyChapter, toyLift } from '../sim/journey-effects.js';
import { FORKS } from '../data/items.js';
import * as E from '../sim/economy.js';
import * as P from '../sim/prestige.js';
import { LANES, STAR_BY_ID, T_RUN, P_RUN, MAX_DEPTH, STEPS } from '../data/prestige.js';
import * as B from '../ui/unlocks/prestige/boosts.js';
import * as W from '../sim/prestige-words.js';
import { perks as ghostPerks } from '../sim/ghost.js';

const WORD_GAP_S = (W.GAP_MIN_S + W.GAP_MAX_S) / 2;
const COUNT_GAP_S = (B.COUNT_MIN_S + B.COUNT_MAX_S) / 2 + B.COUNT_FROM;

/** One run on prestige record p. Depth 0 with no stars is exactly dev/journey-timing.mjs. */
export function measureRun(picks, p, { taps = 4, share = .125, option = 0, catches = 0 } = {}) {
  const view = P.prestigeView(() => p);
  const st = P.runStamp(p);
  const s = E.createRun({ prestige: st });
  const shop = createJourney(s);
  const has = (id) => P.has(p, id);
  const ghost = ghostPerks(P.level(p, 'ghost'));
  const ref = [{
    fps: (v, s) => s.owned.depth ? v * 1.1 : v,
    click: (v, s) => s.owned.f1 ? v * 1.5 : v
  }];
  const sky = [];
  if (P.level(p, 'pay')) sky.push({ post: true, fps: (v) => v * B.payMult(view), click: (v) => v * B.payMult(view), wall: (v) => v * B.payMult(view) });
  if (P.level(p, 'idle')) sky.push({ post: true, fps: (v) => v * B.idleMult(view) });
  if (P.level(p, 'active')) sky.push({ post: true, click: (v) => v * B.tapMult(view) });
  if (has('active_6')) sky.push({ timed: true, click: (v) => v * (1 + B.COMBO_MAX) });
  if (ghost.beat) { const f = (v, s) => (journeyChapter(s) >= 1 ? v * (1 + ghost.beat) : v); sky.push({ timed: true, fps: f, click: f }); }
  const mods = sky.length ? [...ref, ...sky] : ref;
  const goldF = B.prizeMult(view) / B.goldGapMult(view);
  if (st.start) { s.fuzz += st.start; s.total += st.start; st.gift += st.start; }
  if (st.skip) skipChapter(s, shop, st, p.last, mods);
  const paid = [];
  let last = 0, nextClaim = 60, wordT = 0, countT = 0, caught = 0;
  while (!s.ended && s.clock < 12 * 3600e3) {
    const shelf = shop.shop(8);
    const items = [shelf.goal, ...shelf.rows].filter(it => it && (it.kind !== 'end' || it.id === E.heldEnding(s)) && !EXTRAS.includes(it.id));
    const ready = items.filter(it => (it.kind === 'fork' ? s.total : s.fuzz) + 1e-8 >= shop.price(it.id)).sort((a, b) => Number(b.kind === 'fork') - Number(a.kind === 'fork') || shop.price(a.id) - shop.price(b.id));
    const it = ready[0];
    if (it) {
      const free = it.kind !== 'fork' && st.free > 0 ? shop.price(it.id) : 0;
      if (free) { s.fuzz += free; s.total += free; st.gift += free; }
      const res = it.kind === 'fork' ? shop.choose(it.id, picks[it.id], mods) : shop.buy(it.id, it.options?.[option]?.[0], mods);
      if (res.ok) {
        if (free) st.free -= 1;
        if (it.kind !== 'fork' && res.cost > 0) { paid.push(s.clock / 1000 - last); last = s.clock / 1000; }
        continue;
      }
      if (free) { s.fuzz -= free; s.total -= free; st.gift -= free; }
    }
    if (!items.length) throw Error('Shop empty on ' + JSON.stringify(picks));
    const r = E.rates(s, mods);
    const active = (r.manualClick * taps + r.click * ghost.taps + E.count(s, 'stray') * r.walls * r.bounce) * s.pace;
    const fps = r.fps * s.pace;
    const side = s.owned.gold ? (active + fps) * share * (sky.length ? goldF : 1) : 0;
    const toys = (active + fps) * (toyLift(s) - 1);   // the toys played at half their own budgets, outside the shared prize pot
    const rate = active + fps + side + toys;
    const missing = Math.min(...items.map(it => shop.price(it.id) - (it.kind === 'fork' ? s.total : s.fuzz)));
    const dt = Math.max(.01, Math.min(60, nextClaim - s.clock / 1000, (missing + .001) / Math.max(rate, .01)));
    E.tick(s, dt * 1000, mods);
    E.earn(s, active * dt, 'active');
    if (sky.length) E.sideLump(s, side * dt, 'prizes'); else E.lump(s, side * dt, 'prizes');
    E.lump(s, toys * dt, 'prizes');
    const worth = Math.max(r.fps, r.click * 2, 2) * s.pace;
    if (has('trig_1') && (wordT += dt) >= WORD_GAP_S / (has('trig_2') ? W.OFTEN : 1)) {
      wordT = 0;
      const sec = (W.catchSeconds(has, catches + caught, false) + W.catchSeconds(has, catches + caught, true)) / 2;
      E.sideLump(s, worth * sec, 'trig');
      caught += 1;
    }
    if (has('active_7') && (countT += dt) >= COUNT_GAP_S) { countT = 0; E.sideLump(s, worth * 60, 'countdown'); }
    if (s.clock / 1000 >= nextClaim - .00001) {
      for (const id of Object.keys(s.store.journey.banks)) E.lump(s, takeActivity(s, id) * Math.min(1, share / .25), 'activity_rank');
      nextClaim += 60;
    }
  }
  if (!s.ended) throw Error('Failed to reach the ending: ' + Object.values(picks));
  const sorted = [...paid].sort((a, b) => a - b);
  return { minutes: s.clock / 60000, buys: paid.length, max: sorted.at(-1), run: s, caught };
}

/** Skip Ahead, as ui/engine.js skipChapter does it. */
function skipChapter(s, shop, st, lastRun, mods) {
  for (let n = 0; n < 80 && !s.ended && journeyChapter(s) < 1; n += 1) {
    const it = shop.shop(8).goal;
    if (!it || it.kind === 'end') break;
    const cost = shop.price(it.id);
    s.fuzz += cost; s.total += cost; st.gift += cost;
    const res = it.kind === 'fork'
      ? shop.choose(it.id, st.prepick[it.id] || lastRun?.route?.[it.id] || Object.keys(FORKS[it.id].opts)[0], mods)
      : shop.buy(it.id, it.options?.[0]?.[0], mods);
    if (!res.ok) { s.fuzz -= cost; s.total -= cost; st.gift -= cost; break; }
  }
}

/** The realistic player's shopping: cheapest star first until nothing fits, then the core. */
export function buyCheapest(p) {
  const stars = Object.values(STAR_BY_ID);
  for (;;) {
    const ok = stars.filter((s) => P.canBuy(p, s.id).ok).sort((a, b) => a.cost - b.cost || a.n - b.n);
    if (!ok.length) break;
    P.buyStar(p, ok[0].id);
  }
  if (P.canCore(p).ok) P.buyCore(p);
}

function skyAt(depth, kind) {
  const p = P.createPrestige();
  p.depth = depth;
  for (const l of LANES) for (const s of l.stars) if (kind === 'full' || s.n <= depth) p.own[s.id] = 1;
  if (kind === 'full') p.core = true;
  if (depth > 0) p.lit.drop = 1;
  return p;
}

const ROUTES = () => allRoutes().filter((_, i) => [0, 39, 84, 125, 159, 190].includes(i));
const range = (v) => [Math.min(...v), Math.max(...v)].map((x) => Math.round(x * 10) / 10);
const mean = (v) => v.reduce((a, b) => a + b, 0) / v.length;

/** A run on every route at both tap rates; dust = what renewing the 4-tap runs would earn (mean). */
function measureSky(p, catches = 0) {
  const out = { depth: p.depth, mult: Math.round(P.mult(p) * 100) / 100, stars: Object.keys(p.own).length, price: Math.round(P.runMods(p).priceMult * 100) / 100 };
  for (const taps of [4, 2]) {
    const rs = ROUTES().map((r, i) => measureRun(r, p, { taps, share: taps === 4 ? .125 : .0625, option: i % 2, catches }));
    out[`m${taps}`] = range(rs.map((x) => x.minutes));
    out[`gap${taps}`] = Math.round(Math.max(...rs.map((x) => x.max)));
    if (taps === 4) {
      out.dust = Math.round(mean(rs.map((x) => P.dustFor(x.run, p))));
      out.caught = Math.round(mean(rs.map((x) => x.caught)));
    }
  }
  out.target = T_RUN[Math.min(p.depth, T_RUN.length - 1)];
  out.pRun = P_RUN[Math.min(p.depth, P_RUN.length - 1)];
  return out;
}

export function realistic() {
  const p = P.createPrestige();
  const rows = [], jump = [];
  let catches = 0;
  for (let d = 0; d <= MAX_DEPTH; d += 1) {
    const row = measureSky(p, catches);
    rows.push({ ...row, bought: Object.keys(p.own).length });
    catches += row.caught;
    if (d === MAX_DEPTH) break;
    const route = ROUTES()[d % 6];
    const res = measureRun(route, p, { taps: 4, catches });
    res.run.ended = { id: 'drop', at: res.run.clock };
    P.renew({ prestige: p }, res.run);
    // The jump: the next run with the stars just bought against the same Depth without them (route 0, 4 taps).
    const before = measureRun(ROUTES()[0], p, { taps: 4, catches }).minutes;
    buyCheapest(p);
    jump[p.depth] = Math.round((1 - measureRun(ROUTES()[0], p, { taps: 4, catches }).minutes / before) * 100);
  }
  for (const r of rows) r.jump = jump[r.depth] ?? null;
  return rows;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const real = realistic();
  const open = [];
  for (let d = 0; d <= MAX_DEPTH; d += 1) open.push(measureSky(skyAt(d, 'open')));
  const full = measureSky(skyAt(MAX_DEPTH, 'full'));
  const line = (r) => `${String(r.depth).padStart(2)}  ${String(r.target).padStart(3)}  ${r.m4.join('-').padEnd(11)} ${r.m2.join('-').padEnd(11)} ${String(r.gap4).padStart(4)}  ${String(r.dust).padStart(6)} / ${String(r.pRun).padEnd(6)} x${r.mult} price x${r.price} stars ${r.stars}${r.jump != null ? `, fresh stars ${r.jump}% faster` : ''}`;
  const head = 'd   T    4 taps min  2 taps min  gap4  dust / P_RUN';
  console.log('REAL sky (renew every run, cheapest-first)\n' + head); for (const r of real) console.log(line(r));
  console.log('\nOPEN sky (every star the Depth allows)\n' + head); for (const r of open) console.log(line(r));
  console.log('\nFULL sky (80 stars + core) at Depth 10\n' + head); console.log(line(full));
  const j = process.argv.indexOf('--json');
  if (j > 0) writeFileSync(process.argv[j + 1], JSON.stringify({ real, open, full }, null, 1));
}
