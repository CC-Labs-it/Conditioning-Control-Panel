/* sim/route.js - a greedy player through one route, on the real economy. Same method as the design's sim.js: buys
 * everything it can reach in price order, skips alternate endings and side games. A real run with side games is shorter. */

import { ITEMS, FORKS, FORK_OF_LANE } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import * as E from './economy.js';

export function allRoutes() {
  let all = [{}];
  for (const [f, def] of Object.entries(FORKS)) {
    const next = [];
    for (const a of all) for (const o of Object.keys(def.opts)) next.push({ ...a, [f]: o });
    all = next;
  }
  return all;
}

export function playRoute(picks, { taps = 4, mood = null, mods = null, maxS = 7200 } = {}) {
  const dt = 0.25;
  const s = E.createRun({ mood });
  const inRoute = (it) => { const f = FORK_OF_LANE[it.lane]; return !f || picks[f] === it.lane; };
  let pending = ITEMS.filter((r) => r.group === 'run' && r.id !== 'start' && r.kind !== 'end' && !r.grow && inRoute(r)).concat(ITEMS.filter((r) => r.id === 'drop'));
  pending = pending.map((r) => ({ r, cost: E.price(s, r.id, mods) })).sort((a, b) => a.cost - b.cost);
  let t = 0, longest = 0, lastBuy = 0, dirty = true;
  const at = { start: 0 };
  while (pending.length && t < maxS) {
    const rate = E.rates(s, mods, t * 1000);
    const strays = E.count(s, 'stray');
    E.tick(s, dt * 1000, mods);
    E.lump(s, (rate.click * taps + strays * rate.walls * rate.wall) * dt, 'route');
    t += dt;
    if (E.has(s, 'stray') && strays < 12) {
      const c = E.price(s, 'stray', mods);
      if (s.fuzz >= c && c / Math.max(0.01, rate.walls * rate.wall) < CONFIG.STRAY_PAYBACK_S) E.buy(s, 'stray', mods);
    } else if (!E.has(s, 'stray') && s.fuzz >= E.price(s, 'stray', mods)) E.buy(s, 'stray', mods);
    for (let i = 0; i < pending.length; i++) {
      const { r, cost } = pending[i];
      if (cost > s.fuzz) break;
      if (!r.req.every((q) => E.has(s, q))) continue;
      const res = r.kind === 'fork' ? E.choose(s, r.id, picks[r.id], mods) : E.buy(s, r.id, mods);
      if (!res.ok) continue;
      at[r.id] = t;
      longest = Math.max(longest, t - lastBuy); lastBuy = t;
      pending.splice(i, 1); i--;
      dirty = true;
      if (r.id === 'drop') return { end: t, buys: s.buys, fps: s.base.fps, at, longest, stuck: false, state: s };
    }
    // Rows whose parents can never be bought drop out, so the run cannot hang on them.
    if (dirty) {
      dirty = false;
      const reach = new Set(['stray', ...Object.keys(s.owned)]);
      let grew = true;
      while (grew) { grew = false; for (const { r } of pending) if (!reach.has(r.id) && r.req.every((q) => reach.has(q))) { reach.add(r.id); grew = true; } }
      pending = pending.filter(({ r }) => reach.has(r.id));
    }
  }
  return { end: t, buys: s.buys, fps: s.base.fps, at, longest, stuck: true, state: s };
}
