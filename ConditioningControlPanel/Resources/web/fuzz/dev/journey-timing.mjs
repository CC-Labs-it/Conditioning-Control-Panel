// Run with node dev/journey-timing.mjs [--all] [--games] [--ranks] [--windows]. Elapsed time is active play.
//   --games: the minute each on-screen game arrives (4 taps); --ranks: how much one rank row lifts total income when bought;
//   --windows: buys and mean gap per 10 minutes (4 taps), plus the order checks of the even beat (kinds in a row, looks,
//   copies, toys, plain ranks in the last 30 minutes).
import { pathToFileURL } from 'node:url';
import { allRoutes } from '../sim/route.js';
import { createJourney, EXTRAS } from '../sim/journey.js';
import { takeActivity, bankRate, trunkBoost, buyKind, toyLift, TOY_PLAY, GAMES, TOYS } from '../sim/journey-effects.js';
import * as E from '../sim/economy.js';
import { BY_ID } from '../data/items.js';
export function measureJourney(picks, {
  taps = 4,
  share = .125,
  option = 0,
  restore = null,
  toys = TOY_PLAY
} = {}) {
  let s = E.createRun(),
    shop = createJourney(s);
  const mods = [{
    fps: (v, s) => s.owned.depth ? v * 1.1 : v,
    click: (v, s) => s.owned.f1 ? v * 1.5 : v
  }];
  const paid = [];
  /* The reference player's income per second right now: taps, walls, per-second pay, prizes and the collect banks (claimed
   * once a minute at share / .25 of what they hold). */
  const income = () => {
    const r = E.rates(s, mods);
    const active = r.manualClick * taps + E.count(s, 'stray') * r.walls * r.bounce;
    const j = s.store.journey;
    const banks = Object.keys(j.banks).reduce((sum, id) => sum + bankRate(id, j.ranks[id]) * s.base.fps * trunkBoost(s), 0);
    return (active + r.fps) * (toyLift(s, toys) + (s.owned.gold ? share : 0)) + banks * s.pace * Math.min(1, share / .25);
  };
  let last = 0,
    nextClaim = 60,
    restored = false;
  while (!s.ended && s.clock < 12 * 3600e3) {
    const shelf = shop.shop(8);
    // The web player: no toy connected, so the haptic rows beside the plan are never offered (ui/platform.js).
    const items = [shelf.goal, ...shelf.rows].filter(it => it && (it.kind !== 'end' || it.id === E.heldEnding(s)) && !EXTRAS.includes(it.id));
    const ready = items.filter(it => (it.kind === 'fork' ? s.total : s.fuzz) + 1e-8 >= shop.price(it.id)).sort((a, b) => Number(b.kind === 'fork') - Number(a.kind === 'fork') || shop.price(a.id) - shop.price(b.id));
    const it = ready[0];
    if (it) {
      const before = income();
      const res = it.kind === 'fork' ? shop.choose(it.id, picks[it.id], mods) : shop.buy(it.id, it.options?.[option]?.[0], mods);
      if (res.ok) {
        if (it.kind !== 'fork' && res.cost > 0) {
          paid.push({
            id: it.id,
            gap: s.clock / 1000 - last,
            at: s.clock / 1000,
            lift: before > 0 ? income() / before - 1 : 0
          });
          last = s.clock / 1000;
        }
        if (restore && !restored && paid.length === 60) {
          s = restore(s);
          shop = createJourney(s);
          restored = true;
        }
        continue;
      }
    }
    if (!items.length) throw Error('Shop empty (held ' + E.heldEnding(s) + ', main ' + E.mainPathDone(s) + ', ended ' + JSON.stringify(s.ended) + ') on ' + JSON.stringify(picks) + ' after ' + paid.at(-1)?.id);
    const r = E.rates(s, mods);
    const active = r.manualClick * taps + E.count(s, 'stray') * r.walls * r.bounce;
    const side = (active + r.fps) * ((s.owned.gold ? share : 0) + toyLift(s, toys) - 1);   // prizes, plus the toys played at `toys` of their budgets
    const rate = active + r.fps + side;
    const missing = Math.min(...items.map(it => shop.price(it.id) - (it.kind === 'fork' ? s.total : s.fuzz)));
    const dt = Math.max(.01, Math.min(60, nextClaim - s.clock / 1000, (missing + .001) / Math.max(rate, .01)));
    E.tick(s, dt * 1000, mods);
    E.earn(s, active * dt, 'active');
    E.lump(s, side * dt, 'prizes');
    if (s.clock / 1000 >= nextClaim - .00001) {
      for (const id of Object.keys(s.store.journey.banks)) {
        E.lump(s, takeActivity(s, id) * Math.min(1, share / .25), 'activity_rank');
      }
      nextClaim += 60;
    }
  }
  if (!s.ended || BY_ID[s.ended.id]?.kind !== 'end') throw Error('Failed to reach the ending: ' + Object.values(picks));
  const sorted = paid.map(x => x.gap).sort((a, b) => a - b);
  return {
    picks,
    taps,
    minutes: s.clock / 60000,
    buys: paid.length,
    max: sorted.at(-1),
    median: sorted[Math.floor(sorted.length / 2)],
    paid,
    s
  };
}
/** Buys and mean gap (s) per `size`-minute window of a measured run. */
export function windows(report, size = 10) {
  const out = [];
  for (const p of report.paid) {
    const w = Math.floor(p.at / 60 / size);
    (out[w] ||= { from: w * size, buys: 0, gaps: 0 });
    out[w].buys++;
    out[w].gaps += p.gap;
  }
  return Array.from(out, (w, k) => w ? { ...w, mean: w.gaps / w.buys } : { from: k * size, buys: 0, gaps: 0, mean: 0 });
}
/** The even beat order checks on a measured run (owner 2026-11-10, handoff-1110/TC-EVEN-BEAT-BRIEF.md). */
export function orderReport(report) {
  const kinds = report.paid.map(p => buyKind(p.id, BY_ID[p.id]?.lane));
  let run = 1, longest = { n: 1, kind: kinds[0], at: 1 }, copyRun = 0, copyLongest = 0, looks4 = 0;
  for (let i = 1; i < kinds.length; i++) {
    run = kinds[i] === kinds[i - 1] ? run + 1 : 1;
    if (run > longest.n) longest = { n: run, kind: kinds[i], at: i + 1 };
  }
  for (const k of kinds) { copyRun = k === 'copy' ? copyRun + 1 : 0; copyLongest = Math.max(copyLongest, copyRun); }
  for (let i = 0; i + 4 <= kinds.length; i++) looks4 = Math.max(looks4, kinds.slice(i, i + 4).filter(k => k === 'look').length);
  const copiesBy15 = report.paid.filter(p => p.id === 'stray' && p.at <= 15 * 60).length;
  const toyAt = report.paid.map((p, i) => [p.id, i]).filter(([id]) => TOYS.includes(id)).map(([, i]) => i);
  const toyGap = Math.min(...toyAt.slice(1).map((n, k) => n - toyAt[k]));
  const end = report.minutes * 60 - 30 * 60, late = kinds.filter((k, i) => report.paid[i].at > end);
  return { longest, copyLongest, looks4, copiesBy15, toyGap, lateRanks: late.filter(k => k === 'rank').length, lateBuys: late.length };
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const routes = allRoutes().filter((_, i) => process.argv.includes('--all') || [0, 39, 84, 125, 159, 190].includes(i));
  if (process.argv.includes('--windows')) {
    for (const [i, r] of routes.entries()) {
      const rep = measureJourney(r, { taps: 4, share: .125, option: i % 2 });
      const w = windows(rep).map(x => x.buys + '/' + Math.round(x.mean) + 's').join(' ');
      const o = orderReport(rep);
      const first3 = rep.paid.filter(p => p.at <= 180).length;
      console.log(Object.values(r).join('/').padEnd(36), rep.minutes.toFixed(1), 'min | first 3 min', first3, '| per 10 min', w, '| max gap', Math.round(rep.max) + 's');
      console.log(' '.repeat(36), 'longest same kind', o.longest.n, o.longest.kind, '@' + o.longest.at, '| copies in a row', o.copyLongest, '| looks in 4', o.looks4, '| copies by min 15', o.copiesBy15, '| toy gap', o.toyGap, '| plain ranks last 30 min', o.lateRanks + '/' + o.lateBuys);
    }
    process.exit(0);
  }
  if (process.argv.includes('--games') || process.argv.includes('--ranks')) {
    const reports = routes.map((r, i) => measureJourney(r, { taps: 4, share: .125, option: i % 2 }));
    if (process.argv.includes('--games')) {
      for (const id of ['gold', ...TOYS]) {
        const at = reports.map(r => r.paid.findIndex(p => p.id === id)).map((n, k) => n < 0 ? null : [n + 1, reports[k].paid[n].at / 60]);
        const firstRank = reports.map(r => r.paid.findIndex(p => p.id === 'rank_' + id + '_1')).map((n, k) => n < 0 ? null : n + 1);
        const mins = at.filter(Boolean).map(a => a[1]);
        console.log(id.padEnd(14), 'purchase', at.filter(Boolean).map(a => a[0]).join('/'), 'minute', mins.length ? Math.min(...mins).toFixed(1) + '-' + Math.max(...mins).toFixed(1) : '-', 'first rank at purchase', firstRank.join('/'));
      }
    }
    if (process.argv.includes('--ranks')) {
      const by = {};
      for (const r of reports) for (const p of r.paid) if (p.id.startsWith('rank_') || ['overlay', 'takeover', 'wallpaper', 'tunnel'].includes(p.id)) {
        const fam = p.id.startsWith('rank_') ? p.id.replace(/^rank_/, '').replace(/_\d+$/, '') : p.id;
        (by[fam] ||= []).push(p.lift * 100);
      }
      for (const [fam, v] of Object.entries(by)) console.log(fam.padEnd(14), 'lift %', Math.min(...v).toFixed(1), '-', Math.max(...v).toFixed(1), 'mean', (v.reduce((a, b) => a + b, 0) / v.length).toFixed(1));
    }
    process.exit(0);
  }
  for (const taps of [4, 2]) {
    const reports = routes.map((r, i) => measureJourney(r, {
      taps,
      share: taps === 4 ? .125 : .0625,
      option: i % 2
    }));
    const range = k => {
      const v = reports.map(r => r[k]);
      return [Math.min(...v), Math.max(...v)].map(x => Math.round(x * 10) / 10);
    };
    console.log({
      routes: routes.length,
      taps,
      minutes: range('minutes'),
      maxGapSeconds: range('max'),
      paidPurchases: range('buys')
    });
  }
}
