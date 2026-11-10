import { ITEMS, BY_ID, FORKS } from '../data/items.js';
import { LEAD_OF } from '../data/folds.js';
import * as E from './economy.js';
import { ACTIVITIES, GAMES, TOYS, SAVOUR_BUYS, PACE_MINUTES, PLAN_BUYS, PLAN_CLOCK, toyLift, bankRate, buyKind, trunkBoost, journeyStretch, rankCap, rankName, SPECIALTIES, rankDescription, setupJourney } from './journey-effects.js';
import { T_RUN } from '../data/prestige.js';
import { FATE_BY_ID, offeredTo } from '../data/fate.js';

// Durations calibrate sticker prices on an independent first-run economy.
// The playable shop uses those prices and prerequisites. It never waits on a timer.
/* Even beat (owner 2026-11-10, handoff-1110/TC-EVEN-BEAT-BRIEF.md). Purchase index to minute on the 4-tap reference follows
 * PACE_MINUTES (journey-effects.js): 9 buys by about minute 3, then a buy every 32-48 s. Forks moved earlier so the toys
 * can arrive one at a time (Mini Slots needs Pictures, Pop-Up Question needs EMI behind Rest or Tap): was 8 / 14 / 28 / 49 / 77.
 * The chapter purchase ends (14, 28, 49, ...) stay as they were; a chapter no longer has to end on a fork. */
const forks = new Map([[4, 'f1'], [14, 'f5'], [17, 'f3'], [20, 'f2'], [66, 'f4']]);
/* Toys and games, each its own moment (TOYS in journey-effects.js), at least TOY_GAP purchases apart. Minute on the 4-tap
 * reference: Prize Wheel 3.5, Mini Slots 8, Pop-Up Question 12, Racing Thoughts 15.5, Mystery Boxes 19.5, Mini Breakout 23.5,
 * The Deep End 30, Toy Box 44, Prize Dispenser 56. Was (2026-11-10 late game wave): 4 / 10 / 19 / 18 / 24 / 28 / 38 / 49 / 62.
 * Free numbers are left between anchors so the planner can put a copy, a look or a rank between features (no three of a
 * kind in a row). The four trunk shows and the late specialties sit later so the last half hour is not a wall of ranks. */
const anchors = {
  1: 'stray',
  2: 'rounded',
  3: 'stray',
  4: 'fpsv',
  5: 'sense',  // Watch or Listen comes after purchase 4, so its first row lands at once
  6: 'title',
  7: 'gold',
  8: 'float',
  9: 'screen',
  10: 'levels',
  11: 'wheel',
  12: 'softw',
  14: 'ticker',
  15: 'theme',  // Pick a Theme at about minute 5
  17: 'lofi_radio',  // on the shelf with the first themed rows (tests/lofi-unlock.test.mjs)
  18: 'mini_slots',
  19: 'media',
  20: 'depth',
  22: 'em_head',
  23: 'guide',  // owner 2026-11-07: the map comes early
  25: 'quiz',
  26: 'panel',  // owner 2026-11-07: the Control Panel comes sooner (an icon beside the volume button)
  27: 'spec_rebounds',
  30: 'pod',  // the theme podcast at about minute 14
  32: 'mini_race',
  33: 'd_autoscroll',
  36: 'banner',
  39: 'boxes',
  46: 'mini_breakout',
  57: 'mini_deep',
  67: 'glossy',
  /* Fate cards (owner 2026-11-10, handoff-1110/TC-FATE-CARDS-BRIEF.md): Second Theme moves up from purchase 92 (about
   * minute 53) so the seeds can arrive right behind it, about minute 39, and the picked set's cards 2-4 land near minutes
   * 45, 60 and 75 of the 90-minute run. fate1..fate4 are roles: the planned set's card at that step (buildPlan `fate`).
   * At fate1 the shelf offers every seed the run may pick, at the same price (createJourney). */
  69: 'taste2',
  70: 'fate1',
  72: 'spec_breakout',
  79: 'fate2',
  80: 'toy_box',
  95: 'dualwield',  // owner 2026-11-08: Double Wielding, the mid-game power-up
  99: 'dispenser',
  104: 'fate3',
  108: 'overlay',
  114: 'spec_mix',
  117: 'tr_skin',
  120: 'takeover',
  123: 'fate4',
  124: 'em_call',
  127: 'spec_dispenser',
  129: 'wallpaper',
  133: 'x_infinite',
  137: 'tunnel',
  144: 'drop'
};
/** A new toy or game never lands within TOY_GAP - 1 purchases of the one before it. */
export const TOY_GAP = 7;
/** Even beat shares: copies of the Bouncing Button (COPY_START, then one per COPY_EVERY rows), looks (LOOK_START, then one per
 *  LOOK_EVERY rows) and rank rows (RANK_SHARE of the rows from purchase 15) are each spread evenly over the run. */
/** The rows before Watch or Listen sell at their catalog prices (a few seconds each). */
const CATALOG_ROWS = 4;
const COPY_START = 2, COPY_EVERY = 6, LOOK_START = 2, LOOK_EVERY = 8, RANK_SHARE = .45;
const RANK_SPACING = 6;
/* Owner 2026-11-10: the haptic rows (Toy Link, Phone Buzz) are offered only when the host says a toy is connected (the CCP
 * desktop app, later), and Friend Remote is cut. None of them is ever a plan row: a row the shelf cannot show would hold
 * Drop back. Toy Link and Phone Buzz ride beside the plan instead (EXTRAS), at their own price, once open. */
export const EXTRAS = ['toy', 'buzz'];
const UNPLANNED = new Set([...EXTRAS, 'remote']);
const reserve = new Map(Object.entries(anchors).map(([n, id]) => [id, Number(n)]));
const themeHead = Object.fromEntries(ITEMS.filter(it => it.id.endsWith('_head') && FORKS.f5.opts[it.lane]).map(it => [it.lane, it.id]));
const themePod = Object.fromEntries(ITEMS.filter(it => it.id.endsWith('_pod') && FORKS.f5.opts[it.lane]).map(it => [it.lane, it.id]));
/* Even beat (owner 2026-11-10): the plan's seconds for each purchase follow sim/journey-effects.js PACE_MINUTES (a quick hook,
 * then an even beat that slows a little each half hour), mapped onto the plan clock by PLAN_CLOCK. Was seven chapters of
 * jittered spacing (.78-1.18), which put 31 buys in the first 10 minutes and 58 s gaps in the last 10.
 * Renewal Sky: the timeline follows the run's planned minutes (T_RUN[depth]); 90 is today's run. */
const timelines = new Map();
function timeline(minutes = T_RUN[0]) {
  if (timelines.has(minutes)) return timelines.get(minutes);
  const k = journeyStretch(minutes) * PLAN_CLOCK * 60;
  const t = { targetSeconds: PACE_MINUTES.slice(1).map(m => m * k) };
  timelines.set(minutes, t);
  return t;
}
const referenceMods = [{
  fps: (v, s) => s.owned.depth ? v * 1.1 : v,
  click: (v, s) => s.owned.f1 ? v * 1.5 : v
}];
function extraItem(parent, id, name, what) {
  return {
    id,
    name,
    what,
    line: '',
    iconId: parent.id,
    lane: parent.lane,
    group: 'run',
    kind: 'item',
    req: [parent.id]
  };
}
function fixedRate(s) {
  const r = { ...E.rates(s, referenceMods), boost: trunkBoost(s) };
  const total = r.fps + r.manualClick * 4 + E.count(s, 'stray') * r.walls * r.bounce;
  const banks = Object.keys(s.store.journey.banks).reduce((sum, id) => sum + bankRate(id, s.store.journey.ranks[id]) * s.base.fps * r.boost, 0);
  // The reference player stays: taps 4 a second and catches most Golden Buttons (Deep, Lucky, Obey are worth about a third).
  return total * ((s.owned.gold ? 1.3 : 1) + toyLift(s) - 1) + banks * .5;   // toys add to the prizes, they do not multiply them
}
/** The ending set a plan is built for: the given one when this run is offered it, else Go Outside (always offered). The
 *  rows before the seed do not depend on it, so a plan made before the pick stays right up to the seed. */
export const PLAN_FATE = 'outside';
export const planFate = (fate, picks = {}) => (FATE_BY_ID[fate] && offeredTo(fate, picks) ? fate : PLAN_FATE);
/* minutes: the run's planned length; priceMult: the run multiplier (prices scale with it, 1 = today); fate: the ending set
 * the plan places (fate1..fate4 anchors, then its ending last); picks.fate is read when no fate is given. */
export function buildPlan(picks, minutes = T_RUN[0], priceMult = 1, fate = picks.fate) {
  const cards = FATE_BY_ID[planFate(fate, picks)].cards;
  const { targetSeconds } = timeline(minutes);
  const pm = c => priceMult === 1 ? c : Math.max(1, Math.round(c * priceMult));
  const shadow = E.createRun(),
    j = setupJourney(shadow),
    entries = [],
    kinds = [];  // kinds[i] = the kind of paid plan row i (1-based; forks are not rows)
  let elapsed = 0,
    spent = 0,
    lastCost = 0,
    ranks = 0;
  const arrived = {}, lastRank = {};
  const anchorKind = n => anchors[n] ? buyKind(anchors[n], BY_ID[anchors[n]]?.lane) : null;
  /* Even beat order rules (owner 2026-11-10): no three of one kind in a row (the next anchored row counts), at most 2 looks
   * in any 4 rows, copies of the Bouncing Button spread out, and a new toy never right behind another. */
  function fits(i, kind) {
    const k1 = kinds[i - 1], k2 = kinds[i - 2], n1 = anchorKind(i + 1), n2 = anchorKind(i + 2);
    if ((k1 === kind && k2 === kind) || (k1 === kind && n1 === kind) || (n1 === kind && n2 === kind)) return false;
    if (kind === 'look' && [kinds[i - 1], kinds[i - 2], kinds[i - 3], n1].filter(k => k === 'look').length >= 2) return false;
    if (kind === 'copy' && i > CATALOG_ROWS && (k1 === 'copy' || k2 === 'copy')) return false;
    if (kind === 'toy' && kinds.slice(Math.max(1, i - TOY_GAP + 1), i).includes('toy')) return false;
    return true;
  }
  for (let i = 1; i <= PLAN_BUYS; i++) {
    const sense = shadow.picks.f1 === 'ears' ? 'a_hum' : 'e_spiral';
    const media = {
      scroll: 'd_scroll',
      slides: 'v_frame',
      blind: 'b_frame'
    }[shadow.picks.f3];
    const roles = {
      sense,
      media,
      theme: themeHead[shadow.picks.f5],
      pod: themePod[shadow.picks.f5],
      fate1: cards[0],
      fate2: cards[1],
      fate3: cards[2],
      fate4: cards[3]
    };
    const wanted = roles[anchors[i]] || anchors[i];
    const available = ITEMS.filter(it => it.kind === 'item' && !it.fate && !LEAD_OF[it.id] && !UNPLANNED.has(it.id) && E.isOpen(shadow, it) && !(reserve.get(it.id) > i) && !(TOYS.includes(it.id) && !reserve.has(it.id)));
    const families = ['stray', 'rounded', 'ticker', sense, 'levels', 'toy_box', 'wheel', media, 'quiz', 'mini_breakout', 'dispenser', 'mini_race', 'mini_deep', 'mini_slots'];
    // Savour rule (owner 2026-11-10): a game's first rank waits SAVOUR_BUYS purchases after the game arrived, and one family
    // never ranks twice within RANK_SPACING purchases while another family can (no three Dispenser upgrades in a row).
    const rankable = families.filter(id => id && shadow.owned[id] && (j.ranks[id] || 0) < rankCap(id) && !(GAMES.includes(id) && !j.ranks[id] && i - arrived[id] < SAVOUR_BUYS));
    const spaced = rankable.filter(id => !(i - lastRank[id] < RANK_SPACING));
    const ranked = (spaced.length ? spaced : rankable).sort((a, b) => (j.ranks[a] || 0) - (j.ranks[b] || 0));
    const specialty = SPECIALTIES[wanted];
    let item = BY_ID[wanted === 'drop' ? E.heldEnding(shadow) : wanted],   // one ending a run: the plan ends on the held one
      parent,
      rank = 0;
    if (specialty) {
      parent = BY_ID[specialty.parent];
      if (!shadow.owned[parent.id]) throw Error('Missing specialty parent ' + parent.id);
      item = {
        ...extraItem(parent, wanted, specialty.name, specialty.options.map(o => o[1] + ': ' + o[2]).join(' Or ')),
        options: specialty.options
      };
    } else if (!item || !E.isOpen(shadow, item) || LEAD_OF[item.id]) {
      /* A free row: rank rows are spread evenly from purchase 15 (RANK_SHARE of the rows), the rest go cheapest first,
       * each only where it keeps the order rules; when nothing fits, the cheapest row anyway. */
      const nextRank = id => 'rank_' + id + '_' + ((j.ranks[id] || 0) + 1);
      const sorted = available.sort((a, b) => E.price(shadow, a.id) - E.price(shadow, b.id));
      const rankOpts = ranked.filter(id => fits(i, buyKind(nextRank(id))));
      const itemOpts = sorted.filter(it => fits(i, buyKind(it.id, it.lane)));
      /* Each kind keeps its own even share of the rows (owner 2026-11-10: small look buys and copies must not come in
       * clumps): the kind furthest behind its share goes first; features fill the rest, cheapest first. */
      const look = itemOpts.find(it => buyKind(it.id, it.lane) === 'look'),
        copy = itemOpts.find(it => it.id === 'stray'),
        feature = itemOpts.find(it => buyKind(it.id, it.lane) === 'feature');
      const behind = [
        [i >= 15 ? RANK_SHARE * (i - 14) - ranks : -1, rankOpts[0] && { fam: rankOpts[0] }],
        [COPY_START + (i - CATALOG_ROWS) / COPY_EVERY - E.count(shadow, 'stray'), copy && { it: copy }],
        [LOOK_START + (i - CATALOG_ROWS) / LOOK_EVERY - kinds.filter(k => k === 'look').length, look && { it: look }]
      ].filter(([d, opt]) => d > 0 && opt).sort((a, b) => b[0] - a[0]);
      const pick = behind[0]?.[1] || (feature && { it: feature }) || (itemOpts[0] && { it: itemOpts[0] }) || (rankOpts[0] && { fam: rankOpts[0] })
        || (sorted[0] && { it: sorted[0] }) || (ranked[0] && { fam: ranked[0] });
      if (pick?.fam) {
        parent = BY_ID[pick.fam];
        rank = (j.ranks[parent.id] || 0) + 1;
        item = {
          ...extraItem(parent, 'rank_' + parent.id + '_' + rank, rankName(parent.id, rank, parent.name), rankDescription(parent.id, rank)),
          rank
        };
      } else item = pick?.it;
    }
    if (!item) throw Error('Missing journey offer at ' + i);
    kinds[i] = buyKind(item.id, item.lane);
    if (rank) ranks++;
    const rate = fixedRate(shadow);
    const target = targetSeconds[i - 1];
    // Each planned price sits a little above the one before it, so a player buying the cheapest row keeps the plan's order.
    const cost = i <= CATALOG_ROWS ? E.price(shadow, item.id) : Math.max(1, Math.round(rate * (target - elapsed)), Math.ceil(lastCost * 1.005));
    lastCost = cost;
    const occurrence = E.count(shadow, item.id) + 1;
    entries.push({
      item,
      cost: pm(cost),
      occurrence,
      rank,
      parent: parent?.id
    });
    spent += cost;
    elapsed += cost / rate;
    if (rank) {
      j.ranks[parent.id] = rank;
      lastRank[parent.id] = i;
      if (ACTIVITIES.includes(parent.id)) j.banks[parent.id] = 0;
    } else if (specialty) {
      j.specs[item.id] = specialty.options[0][0];
    } else {
      shadow.fuzz = cost;
      if (!E.buy(shadow, item.id, null, cost).ok) throw Error('Invalid journey purchase ' + item.id);
      if (GAMES.includes(item.id)) arrived[item.id] = i;
    }
    const fork = forks.get(i);
    if (fork) {
      entries.push({
        item: BY_ID[fork],
        cost: pm(spent),
        occurrence: 1
      });
      if (!picks[fork]) break;
      if (!E.choose(shadow, fork, picks[fork], null, 0, 0).ok) throw Error('Invalid journey fork ' + fork);
    }
  }
  return entries;
}
/** What a run of `minutes` spends on the plan to Drop (first option at every fork), at price scale 1: the
 *  Stardust reference (sim/prestige.js dustFor). */
const totals = new Map();
export function planTotal(minutes = T_RUN[0]) {
  if (totals.has(minutes)) return totals.get(minutes);
  const picks = Object.fromEntries(Object.entries(FORKS).map(([f, def]) => [f, Object.keys(def.opts)[0]]));
  let sum = 0;
  try {
    for (const e of buildPlan(picks, minutes)) if (e.item.kind !== 'fork') sum += e.cost;
  } catch { sum = 0; }
  totals.set(minutes, sum);
  return sum;
}
export function createJourney(s) {
  const j = setupJourney(s);
  const minutes = s.prestige?.minutes || T_RUN[0], priceMult = s.prestige?.price || 1;
  let previous = '',
    entries = [];
  function plan() {
    const fate = E.fateOf(s);
    const key = Object.values(s.picks).join('/') + '#' + (fate || '');
    if (key !== previous) {
      entries = buildPlan(s.picks, minutes, priceMult, fate);
      previous = key;
    }
    return entries;
  }
  function taken(e) {
    if (e.rank) return (j.ranks[e.parent] || 0) >= e.rank;
    if (e.item.options) return !!j.specs[e.item.id];
    return E.count(s, e.item.id) >= e.occurrence;
  }
  // Keep the next few purchases on the shelf. Stop at each unchosen route fork.
  let catalog = null,
    cachedBuys = -1,
    cachedEnd,
    cachedReady = false,
    offers = [];
  function shelf() {
    const rows = plan();
    const gate = rows.at(-1);
    const ready = gate?.item.kind === 'fork' && s.total >= gate.cost && E.isOpen(s, gate.item);
    if (catalog === rows && cachedBuys === s.buys && cachedEnd === s.ended && ready === cachedReady) return offers;
    catalog = rows;
    cachedBuys = s.buys;
    cachedEnd = s.ended;
    cachedReady = ready;
    offers = [];
    if (s.ended) return offers;
    for (const entry of rows) {
      if (taken(entry)) continue;
      const it = entry.item;
      if (it.kind === 'fork') {
        // A discounted chapter still earns its choice when all its purchases are complete.
        if (!offers.length && E.isOpen(s, it)) offers.push({
          ...entry,
          cost: Math.min(entry.cost, s.total)
        });
        break;
      }
      if (it.fate && it.step === 1 && !E.fateOf(s)) {
        // The seeds arrive together (fate cards): every seed this run may pick, at the planned price. The first one bought
        // picks the run's ending, and the plan is rebuilt for its set.
        if (!offers.length) for (const seed of ITEMS) if (seed.fate && seed.step === 1 && E.isOpen(s, seed)) offers.push(seed === it ? entry : { ...entry, item: seed });
        break;
      }
      if (it.kind === 'end') {
        // One ending a run (owner 2026-11-10): the plan's last row is an ending; the player's own held ending (sim/economy.js
        // heldEnding) takes its place, at the planned price, only once the main path is done.
        if (offers.length) break;
        const held = BY_ID[E.heldEnding(s)];
        if (held && E.isOpen(s, held)) offers.push(held === it ? entry : { ...entry, item: held });
        break;
      }
      if (offers.some(e => e.item.id === it.id)) continue;
      if (entry.rank && ((j.ranks[entry.parent] || 0) + 1 !== entry.rank || !s.owned[entry.parent])) continue;
      if (entry.rank === 1 && s.buys - (j.arrive[entry.parent] ?? -Infinity) < SAVOUR_BUYS) continue;
      if (it.options && !s.owned[entry.parent]) continue;
      if (!entry.rank && !it.options && (!E.isOpen(s, it) || E.count(s, it.id) + 1 !== entry.occurrence)) continue;
      offers.push(entry);
      if (offers.length === 3) break;
    }
    if (ready && !offers.some(e => e.item === gate.item)) offers.unshift(gate);
    for (const id of EXTRAS) {
      const it = BY_ID[id];
      if (it && offers.length && E.isOpen(s, it)) offers.push({ item: it, cost: Math.max(1, Math.round(E.price(s, id) * priceMult)), occurrence: 1, extra: true });
    }
    return offers;
  }
  const find = id => shelf().find(e => e.item.id === id);
  function sticker(entry, mods) {
    return entry.item.kind === 'fork' ? entry.cost : Math.round(E.adjustPrice(s, entry.item, entry.cost, mods));
  }
  return {
    shop(size = 6) {
      const all = shelf();
      // Every available ending, and every seed of the fate pick, must remain selectable on the smallest shop layout.
      const limit = all[0]?.item.kind === 'end' || all[0]?.item.step === 1 ? all.length : 1 + Math.max(2, size);
      return {
        goal: all[0]?.item || null,
        rows: all.slice(1, limit).map(e => e.item),
        more: Math.max(0, all.length - limit)
      };
    },
    item: id => find(id)?.item || BY_ID[id],
    price(id, mods) {
      const entry = find(id);
      return entry ? sticker(entry, mods) : E.price(s, id, mods);
    },
    buy(id, option, mods) {
      const e = find(id);
      if (!e) return {
        ok: false,
        reason: 'closed'
      };
      const cost = sticker(e, mods);
      if (!e.rank && !e.item.options) {
        const res = E.buy(s, id, mods, cost);
        if (res.ok && GAMES.includes(id)) j.arrive[id] = s.buys;
        return res;
      }
      if (s.fuzz < cost) return {
        ok: false,
        reason: 'short',
        cost
      };
      if (e.item.options && !e.item.options.some(([key]) => key === option)) return {
        ok: false,
        reason: 'choice'
      };
      s.fuzz -= cost;
      s.buys++;
      if (e.rank) {
        j.ranks[e.parent] = e.rank;
        if (ACTIVITIES.includes(e.parent)) j.banks[e.parent] ??= 0;
      } else j.specs[id] = option;
      return {
        ok: true,
        item: e.item,
        count: e.rank || 1,
        cost,
        riders: []
      };
    },
    choose(id, lane, mods) {
      const e = find(id);
      return e ? E.choose(s, id, lane, mods, e.cost, s.total) : {
        ok: false,
        reason: 'closed'
      };
    },
    /** Read-only look-ahead for the "Next up" strip (ui/shop-next.js): up to n planned items after the shelf, in plan
     *  order, each id once. A fork or ending row is included and ends the list, so nothing past a choice is shown. */
    peek(n = 3) {
      const on = new Set(shelf().map(e => e.item.id)), out = [];
      for (const e of plan()) {
        if (out.length >= n) break;
        if (taken(e) || on.has(e.item.id) || out.some(it => it.id === e.item.id)) continue;
        out.push(e.item);
        if (e.item.kind === 'fork' || e.item.kind === 'end' || (e.item.step === 1 && !E.fateOf(s))) break;
      }
      return out;
    }
  };
}
