// Run with node dev/journey-timing.mjs [--all]. Elapsed time is active play.
import { pathToFileURL } from 'node:url';
import { allRoutes } from '../sim/route.js';
import { createJourney } from '../sim/journey.js';
import { takeActivity } from '../sim/journey-effects.js';
import * as E from '../sim/economy.js';
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
  let last = 0,
    nextClaim = 60,
    restored = false;
  while (!s.ended && s.clock < 12 * 3600e3) {
    const shelf = shop.shop(8);
    const items = [shelf.goal, ...shelf.rows].filter(it => it && (it.kind !== 'end' || it.id === 'drop'));
    const ready = items.filter(it => (it.kind === 'fork' ? s.total : s.fuzz) + 1e-8 >= shop.price(it.id)).sort((a, b) => Number(b.kind === 'fork') - Number(a.kind === 'fork') || shop.price(a.id) - shop.price(b.id));
    const it = ready[0];
    if (it) {
      const res = it.kind === 'fork' ? shop.choose(it.id, picks[it.id], mods) : shop.buy(it.id, it.options?.[option]?.[0], mods);
      if (res.ok) {
        if (it.kind !== 'fork' && res.cost > 0) {
          paid.push({
            id: it.id,
            gap: s.clock / 1000 - last,
            at: s.clock / 1000
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
    if (!items.length) throw Error('Shop empty on ' + JSON.stringify(picks) + ' after ' + paid.at(-1)?.id);
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
  if (s.ended?.id !== 'drop') throw Error('Failed to reach Drop: ' + Object.values(picks));
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
