/* ============================================================================
 * ui/preview-bar-model.js - the dev preview bar's rules, no DOM (handoff-1121/TC-PREVIEW-BAR-BRIEF.md). The bar itself is
 * ui/preview-bar.js, loaded only when previewBarAllowed() says so, so the production host never even fetches it.
 *   previewBarAllowed  the host gate (localhost, 127.0.0.1, *.pages.dev; never framed by cclabs.app, never hosted)
 *   warpSlices         one frame of game time at a warp, cut into steps the engine accepts (it clamps a step at 1 s)
 *   playAhead          the reference player (dev/journey-timing.mjs measureJourney) stepping a live run forward
 *   depthSetup         a Renewal Depth 0-10 with a sensible owned-star set and some Stardust
 *   devConsoleAllowed  the hidden dev console: the same bar on a host that hands it over (caps.dev), behind devChord
 *   devAmount          an amount typed into the console (5000, 2.5m, 1b, 1e15)
 * ==========================================================================*/

import { FORKS } from '../data/items.js';
import { FATE_BY_ID, routeFate } from '../data/fate.js';
import { STAR_BY_ID, LANES, P_RUN, MAX_DEPTH } from '../data/prestige.js';
import * as E from '../sim/economy.js';
import { EXTRAS } from '../sim/journey.js';
import { takeActivity, bankRate, trunkBoost, toyLift, TOY_PLAY } from '../sim/journey-effects.js';

export const WARPS = Object.freeze([1, 4, 16, 60]);
export const SKIPS = Object.freeze([5, 15]);
export const HERD_MIN = 3, HERD_MAX = 12;
/** The engine clamps one step at a second (ui/engine.js step); a frame never asks for more than this much game time. */
export const STEP_MAX_MS = 1000;
export const FRAME_MAX_MS = 6000;

const hostOf = (url) => { try { return new URL(url).hostname.toLowerCase(); } catch { return ''; } };

/** True only on a local or Cloudflare preview host, never inside a cclabs.app frame and never inside a CCP host. `?bar=0`
 *  hides it on those hosts too. `framedBy` is the parent page's address (location.ancestorOrigins[0] or the referrer).
 *  A driven browser (navigator.webdriver: the dev/*-drive.mjs scripts) gets it only with ?bar=1, so old drives see the
 *  page they were written against. */
export function previewBarAllowed(loc, { framed = false, framedBy = '', hosted = false, automated = false } = {}) {
  const host = String(loc?.hostname || '').toLowerCase();
  const local = host === 'localhost' || host === '127.0.0.1';
  const pages = host.endsWith('.pages.dev') && host.length > '.pages.dev'.length;
  if (!local && !pages) return false;
  if (hosted) return false;
  let q;
  try { q = new URLSearchParams(loc?.search || ''); } catch { q = new URLSearchParams(); }
  if (q.get('bar') === '0') return false;
  const parent = hostOf(framedBy);
  if (parent === 'cclabs.app' || parent.endsWith('.cclabs.app')) return false;
  if (framed && !parent && q.get('bar') !== '1') return false;   // a frame we cannot name: only on request
  if (automated && q.get('bar') !== '1') return false;
  return true;
}
/** ?bar=1 opens the bar on load; otherwise it starts as the chip. */
export const startsOpen = (loc) => { try { return new URLSearchParams(loc?.search || '').get('bar') === '1'; } catch { return false; } };

/** One frame of real time `dtMs` at `warp`, as engine steps of at most STEP_MAX_MS. The frame is capped at FRAME_MAX_MS of
 *  game time so a stalled tab never runs minutes in one frame. 1x hands the frame through untouched. */
export function warpSlices(dtMs, warp = 1) {
  const dt = Math.max(0, Number(dtMs) || 0);
  const w = WARPS.includes(warp) ? warp : 1;
  if (w === 1) return [dt];
  let left = Math.min(FRAME_MAX_MS, dt * w);
  const out = [];
  while (left > 1e-9) { const d = Math.min(STEP_MAX_MS, left); out.push(d); left -= d; }
  return out.length ? out : [0];
}

/** mm:ss of run time; hours roll into the minutes (92:05). */
export function runClock(ms) {
  const s = Math.max(0, Math.floor((Number(ms) || 0) / 1000));
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
}

/** The lanes the bar picks for a fork it reaches: the run's own picks, else each fork's first lane (dev/journey planTotal). */
export function defaultPicks(run) {
  const out = {};
  for (const [f, def] of Object.entries(FORKS)) out[f] = run?.picks?.[f] || Object.keys(def.opts)[0];
  return out;
}

/* ---- jump targets: each is true on the shelf the reference player is looking at, before it buys anything ---- */
const ready = (shop, s, it) => (it.kind === 'fork' ? s.total : s.fuzz) + 1e-8 >= shop.price(it.id);
export const TARGETS = Object.freeze({
  theme: { label: 'Pick a Theme', hit: (shelf, s, shop) => shelf.some((it) => it.id === 'f5' && ready(shop, s, it)), past: (s) => !!s.picks.f5 },
  taste2: { label: 'Second Theme', hit: (shelf, s, shop) => shelf.some((it) => it.id === 'taste2' && ready(shop, s, it)), past: (s) => E.has(s, 'taste2') },
  fate: { label: 'Fate cards', hit: (shelf, s, shop) => shelf.some((it) => it.fate && it.step === 1 && ready(shop, s, it)), past: (s) => !!E.fateOf(s) },
  end: { label: 'End of the run', hit: (shelf, s, shop) => shelf.some((it) => it.kind === 'end' && ready(shop, s, it)), past: (s) => !!s.ended },
});

/**
 * The reference player of dev/journey-timing.mjs (measureJourney), stepping a live run `s` forward on its own shop:
 * 4 taps a second, the walls, per-second pay, prizes and toys at TOY_PLAY of their budgets, the collect banks claimed once
 * a minute at `share` / .25. Each round it buys the cheapest thing it can afford (forks first), prefers its fate seed,
 * never buys an ending, and stops after `ms` of run time or when `until` (a TARGETS key) shows on the shelf.
 * shop: { shop(size) -> {goal, rows}, price(id), buy(id, option), choose(fork, lane) } over the same run.
 * Returns { reason: 'time' | 'target' | 'past' | 'ended' | 'stuck' | 'cap', ms, bought, forks }.
 */
export function playAhead(s, shop, { ms = Infinity, until = null, mods = null, picks = defaultPicks(s), taps = 4, share = .125, toys = TOY_PLAY, option = 0, maxMs = 12 * 3600e3, maxRounds = 20000 } = {}) {
  const target = until ? TARGETS[until] : null;
  const out = { reason: 'time', ms: 0, bought: [], forks: [] };
  if (target?.past(s)) { out.reason = 'past'; return out; }
  const t0 = s.clock, stopAt = t0 + Math.min(ms, maxMs);
  const seed = FATE_BY_ID[picks.fate || routeFate(picks)]?.cards[0];
  const journey = s.store?.journey;
  let nextClaim = (Math.floor(s.clock / 60000) + 1) * 60;
  for (let round = 0; ; round += 1) {
    if (round >= maxRounds) { out.reason = 'cap'; break; }
    if (s.ended) { out.reason = 'ended'; break; }
    const shelf = shop.shop(8);
    const offered = [shelf.goal, ...shelf.rows].filter((it) => it && !EXTRAS.includes(it.id));
    if (target && target.hit(offered, s, shop)) { out.reason = 'target'; break; }
    if (s.clock >= stopAt - 1e-6) { out.reason = target ? 'cap' : 'time'; break; }
    const buyable = offered.filter((it) => it.kind !== 'end');
    const items = buyable.some((it) => it.id === seed) ? buyable.filter((it) => it.step !== 1 || it.id === seed) : buyable;
    const pick = items.filter((it) => ready(shop, s, it))
      .sort((a, b) => Number(b.kind === 'fork') - Number(a.kind === 'fork') || shop.price(a.id) - shop.price(b.id))[0];
    if (pick && !(target && target.hit([pick], s, shop))) {
      const res = pick.kind === 'fork' ? shop.choose(pick.id, picks[pick.id] || Object.keys(FORKS[pick.id].opts)[0])
        : shop.buy(pick.id, pick.options?.[option]?.[0]);
      if (res?.ok) {
        if (pick.kind === 'fork') out.forks.push([pick.id, s.picks[pick.id]]); else out.bought.push(pick.id);
        continue;
      }
    }
    if (!offered.length) { out.reason = 'stuck'; break; }
    const r = E.rates(s, mods);
    const active = r.manualClick * taps + E.count(s, 'stray') * r.walls * r.bounce;
    const side = (active + r.fps) * ((s.owned.gold ? share : 0) + toyLift(s, toys) - 1);
    const rate = Math.max(.01, active + r.fps + side);
    const want = items.length ? Math.min(...items.map((it) => shop.price(it.id) - (it.kind === 'fork' ? s.total : s.fuzz))) : Infinity;
    const dt = Math.max(.01, Math.min(60, nextClaim - s.clock / 1000, (stopAt - s.clock) / 1000, Number.isFinite(want) ? (want + .001) / rate : 60));
    E.tick(s, dt * 1000, mods);
    E.earn(s, active * dt, 'active');
    E.lump(s, Math.max(0, side) * dt, 'prizes');
    if (journey && s.clock / 1000 >= nextClaim - 1e-5) {
      for (const id of Object.keys(journey.banks)) E.lump(s, takeActivity(s, id) * Math.min(1, share / .25), 'activity_rank');
      nextClaim += 60;
    }
  }
  out.ms = s.clock - t0;
  return out;
}

/** The shop the reference player uses on the plain (non-Journey) run: the economy's own window, prices and verbs. */
export function plainShop(s, mods = null, featured = []) {
  return {
    shop: (size) => E.shopWindow(s, mods, size, featured),
    price: (id) => E.price(s, id, mods),
    buy: (id) => E.buy(s, id, mods),
    choose: (f, lane) => E.choose(s, f, lane, mods),
  };
}
/** The Journey shop with its fixed mods, in the shape playAhead reads. */
export function journeyShop(journey, mods = null) {
  return {
    shop: (size) => journey.shop(size),
    price: (id) => journey.price(id, mods),
    buy: (id, option) => journey.buy(id, option, mods),
    choose: (f, lane) => journey.choose(f, lane, mods),
  };
}

/* Boost and mechanic lanes fill first; Fine Print (the challenge rules) only from Depth 2, two stars behind. */
const LANE_SHARE = { pay: 1, head: .6, idle: .6, active: .6, trig: .5, luck: .5, ghost: .5, fine: .4 };
/** A Renewal Depth 0-10 as a prestige save would hold it: stars no deeper than the depth, about half of each lane, some
 *  Stardust to spend, the default ending lit and a last run for the ghost. Depth 0 is a fresh prestige. */
export function depthSetup(depth, base) {
  const d = Math.max(0, Math.min(MAX_DEPTH, Math.floor(Number(depth) || 0)));
  const p = base;
  p.depth = d;
  p.own = {};
  p.lit = {};
  p.dust = d ? Math.round(P_RUN[Math.min(P_RUN.length - 1, d - 1)] * 1.5) : 0;
  p.spent = 0;
  if (!d) { p.last = null; return p; }
  for (const lane of LANES) {
    const n = Math.min(d, Math.max(0, Math.round(d * (LANE_SHARE[lane.id] ?? .5))));
    for (let k = 1; k <= n; k += 1) if (STAR_BY_ID[`${lane.id}_${k}`]) p.own[`${lane.id}_${k}`] = 1;
  }
  delete p.own.head_9;   // Skip Ahead buys chapter 1 for the player: the bar never takes the opening away
  p.lit = { drop: 1 };
  p.last = { route: {}, toys: ['wheel'], ms: 5_400_000 };
  return p;
}

/* ---- the hidden dev console (owner 2026-11-23): the same bar, behind one chord, on a host that hands it over ---- */
/** A host opens the console with caps.dev === true. Only a host that prices nothing may send it (the standalone
 *  program): the website and the CCP app never do, so the chord does nothing there. */
export const devConsoleAllowed = (caps, { hosted = false } = {}) => !!hosted && caps?.dev === true;
/** Ctrl + Alt + Shift + T, one press (a held key does not count). Three modifiers: nobody lands on it by accident. */
export function devChord(ev) {
  return !!ev && ev.ctrlKey === true && ev.altKey === true && ev.shiftKey === true && !ev.metaKey && ev.code === 'KeyT' && !ev.repeat;
}
export const DEV_AMOUNT_MAX = 1e30;
const AMOUNT_UNIT = { k: 1e3, m: 1e6, b: 1e9, t: 1e12 };
/** An amount typed into the console: 5000, 2.5m, 1b, 3t or 1e15. Whole, at least 1, at most DEV_AMOUNT_MAX; else 0. */
export function devAmount(text) {
  const m = /^(\d+(?:\.\d+)?(?:e\d{1,2})?)([kmbt])?$/i.exec(String(text ?? '').replace(/[\s,_]/g, ''));
  if (!m) return 0;
  const n = Number(m[1]) * (m[2] ? AMOUNT_UNIT[m[2].toLowerCase()] : 1);
  return Number.isFinite(n) && n >= 1 ? Math.min(DEV_AMOUNT_MAX, Math.floor(n)) : 0;
}
/** The Expo's six ways out (ui/expo/renewal-endings.js endingTitles), as the console lists them. */
export const EXPO_EXITS = Object.freeze([
  ['renewal', 'Renewal'], ['conduct', 'Acceptance by Conduct'], ['stayed', 'Stayed'],
  ['finger', 'Finger'], ['model', 'Model Visitor'], ['full', 'The Thirteenth'],
]);
