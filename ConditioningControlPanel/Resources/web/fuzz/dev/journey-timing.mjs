// Run with node dev/journey-timing.mjs [--all] [--games] [--ranks]. Elapsed time is active play.
//   --games: the minute each on-screen game arrives (4 taps); --ranks: how much one rank row lifts total income when bought.
import { pathToFileURL } from 'node:url';
import { allRoutes } from '../sim/route.js';
import { createJourney, EXTRAS } from '../sim/journey.js';
import { takeActivity, bankRate, trunkBoost, GAMES } from '../sim/journey-effects.js';
import * as E from '../sim/economy.js';
import { BY_ID } from '../data/items.js';
export function measureJourney(picks, {
  taps = 4,
  share = .125,
  option = 0,
  restore = null
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
    return (active + r.fps) * (1 + (s.owned.gold ? share : 0)) + banks * s.pace * Math.min(1, share / .25);
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
    const side = s.owned.gold ? (active + r.fps) * share : 0;
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
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const routes = allRoutes().filter((_, i) => process.argv.includes('--all') || [0, 39, 84, 125, 159, 190].includes(i));
  if (process.argv.includes('--games') || process.argv.includes('--ranks')) {
    const reports = routes.map((r, i) => measureJourney(r, { taps: 4, share: .125, option: i % 2 }));
    if (process.argv.includes('--games')) {
      for (const id of ['gold', ...GAMES, 'boxes']) {
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
