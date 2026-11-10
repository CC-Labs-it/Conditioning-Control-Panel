import { ITEMS, BY_ID, FORKS } from '../data/items.js';
import { LEAD_OF } from '../data/folds.js';
import * as E from './economy.js';
import { ACTIVITIES, journeyChapters, journeyStretch, SPECIALTIES, rankDescription, setupJourney } from './journey-effects.js';
import { T_RUN } from '../data/prestige.js';

// Durations calibrate sticker prices on an independent first-run economy.
// The playable shop uses those prices and prerequisites. It never waits on a timer.
const forks = new Map([[8, 'f1'], [14, 'f5'], [28, 'f3'], [49, 'f2'], [77, 'f4']]);
const anchors = {
  1: 'stray',
  2: 'rounded',
  3: 'stray',
  4: 'fpsv',
  5: 'title',
  6: 'float',
  9: 'sense',
  10: 'softw',
  11: 'ticker',
  12: 'gold',
  13: 'screen',
  14: 'levels',
  15: 'theme',
  16: 'toy_box',
  17: 'lofi_radio',
  20: 'spec_rebounds',
  21: 'guide',  // owner 2026-11-07: the map comes early (was about purchase 51)
  26: 'wheel',
  29: 'media',
  30: 'panel',  // owner 2026-11-07: the Control Panel comes sooner (an icon beside the volume button; was about purchase 110)
  33: 'd_autoscroll',
  35: 'depth',
  38: 'pod',  // owner 2026-11-07: the theme podcast a little sooner (was about purchase 53)
  42: 'mini_race',  // owner 2026-11-07: Racing Thoughts arrives earlier (was purchase 85)
  50: 'em_head',
  51: 'quiz',
  52: 'mini_breakout',
  56: 'banner',
  60: 'mini_deep',  // owner 2026-11-07: The Deep End joins the arcade
  63: 'spec_breakout',
  78: 'glossy',
  79: 'mini_slots',
  80: 'dispenser',
  96: 'spec_dispenser',
  102: 'taste2',
  106: 'dualwield',  // owner 2026-11-08: Double Wielding, the mid-game power-up (about minute 27)
  110: 'overlay',
  118: 'takeover',
  120: 'spec_mix',
  125: 'tr_skin',
  129: 'em_call',
  131: 'wallpaper',
  135: 'tunnel',
  139: 'x_infinite',
  142: 'toy',
  144: 'drop'
};
const reserve = new Map(Object.entries(anchors).map(([n, id]) => [id, Number(n)]));
const themeHead = Object.fromEntries(ITEMS.filter(it => it.id.endsWith('_head') && FORKS.f5.opts[it.lane]).map(it => [it.lane, it.id]));
const themePod = Object.fromEntries(ITEMS.filter(it => it.id.endsWith('_pod') && FORKS.f5.opts[it.lane]).map(it => [it.lane, it.id]));
// The first chapter: Watch or Listen at about 40 s (purchase 8), Pick a Theme at about 2.3 min (purchase 14; was 3.6 before the owner sped everything up 35%).
const spacing = [.78, 1.15, .93, 1.18, .85, 1.11];
/* Renewal Sky: the timeline follows the run's planned minutes (T_RUN[depth]); 90 is today's run. */
const timelines = new Map();
function timeline(minutes = T_RUN[0]) {
  if (timelines.has(minutes)) return timelines.get(minutes);
  const stretch = journeyStretch(minutes), chapters = journeyChapters(minutes);
  const targetSeconds = [0.81, 2.27, 4.55, 7.8, 12.35, 18.2, 26, 35.75, 46.8, 59.8, 74.75, 91, 110.5, 140.4].map(t => t * stretch);  // x0.65 (owner 2026-11-07: 35% faster)
  const firstChapterEnd = targetSeconds.at(-1);
  for (let c = 1; c < chapters.length; c++) {
    const [min, end] = chapters[c],
      [prev, first] = chapters[c - 1];
    const weights = Array.from({
      length: end - first
    }, (_, i) => spacing[(i + c) % spacing.length]);
    const unit = (min - prev) * 60 / weights.reduce((a, b) => a + b);
    for (const weight of weights) targetSeconds.push(targetSeconds.at(-1) + unit * weight);
  }
  const t = { targetSeconds, firstChapterEnd };
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
  const r = E.rates(s, referenceMods);
  const total = r.fps + r.manualClick * 4 + E.count(s, 'stray') * r.walls * r.bounce;
  const bankRate = Object.keys(s.store.journey.banks).reduce((sum, id) => sum + s.store.journey.ranks[id] * s.base.fps * .01, 0);
  // The reference player stays: taps 4 a second and catches most Golden Buttons (Deep, Lucky, Obey are worth about a third).
  return total * (s.owned.gold ? 1.3 : 1) + bankRate * .5;
}
/* minutes: the run's planned length; priceMult: the run multiplier (prices scale with it, 1 = today). */
function buildPlan(picks, minutes = T_RUN[0], priceMult = 1) {
  const { targetSeconds, firstChapterEnd } = timeline(minutes);
  const pm = c => priceMult === 1 ? c : Math.max(1, Math.round(c * priceMult));
  const shadow = E.createRun(),
    j = setupJourney(shadow),
    entries = [];
  let elapsed = 0,
    spent = 0;
  for (let i = 1; i <= 144; i++) {
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
      pod: themePod[shadow.picks.f5]
    };
    const wanted = roles[anchors[i]] || anchors[i];
    const available = ITEMS.filter(it => it.kind === 'item' && !LEAD_OF[it.id] && E.isOpen(shadow, it) && !(reserve.get(it.id) > i));
    const families = ['stray', 'rounded', 'ticker', sense, 'levels', 'toy_box', 'wheel', media, 'quiz', 'mini_breakout', 'dispenser', 'mini_race', 'mini_deep'];
    const ranked = families.filter(id => id && shadow.owned[id] && (j.ranks[id] || 0) < 6).sort((a, b) => (j.ranks[a] || 0) - (j.ranks[b] || 0));
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
      if (ranked.length && i > 14 && (i % 3 === 0 || !available.length)) {
        parent = BY_ID[ranked[0]];
        rank = (j.ranks[parent.id] || 0) + 1;
        item = {
          ...extraItem(parent, 'rank_' + parent.id + '_' + rank, parent.name + ' · Rank ' + (rank + 1), rankDescription(parent.id)),
          rank
        };
      } else {
        item = available.sort((a, b) => E.price(shadow, a.id) - E.price(shadow, b.id))[0];
      }
    }
    if (!item) throw Error('Missing journey offer at ' + i);
    const rate = fixedRate(shadow);
    // Browser players also earn route rewards and chapter coupons outside the reference income.
    const target = targetSeconds[i - 1];
    const calibrated = target + .13 * Math.max(0, target - firstChapterEnd);
    const cost = i <= 6 ? E.price(shadow, item.id) : Math.max(1, Math.round(rate * (calibrated - elapsed)));
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
      if (ACTIVITIES.includes(parent.id)) j.banks[parent.id] = 0;
    } else if (specialty) {
      j.specs[item.id] = specialty.options[0][0];
    } else {
      shadow.fuzz = cost;
      if (!E.buy(shadow, item.id, null, cost).ok) throw Error('Invalid journey purchase ' + item.id);
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
    const key = Object.values(s.picks).join('/') + '#';
    if (key !== previous) {
      entries = buildPlan(s.picks, minutes, priceMult);
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
      if (it.options && !s.owned[entry.parent]) continue;
      if (!entry.rank && !it.options && (!E.isOpen(s, it) || E.count(s, it.id) + 1 !== entry.occurrence)) continue;
      offers.push(entry);
      if (offers.length === 3) break;
    }
    if (ready && !offers.some(e => e.item === gate.item)) offers.unshift(gate);
    return offers;
  }
  const find = id => shelf().find(e => e.item.id === id);
  function sticker(entry, mods) {
    return entry.item.kind === 'fork' ? entry.cost : Math.round(E.adjustPrice(s, entry.item, entry.cost, mods));
  }
  return {
    shop(size = 6) {
      const all = shelf();
      // Every available ending must remain selectable on the smallest shop layout.
      const limit = all[0]?.item.kind === 'end' ? all.length : 1 + Math.max(2, size);
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
      if (!e.rank && !e.item.options) return E.buy(s, id, mods, cost);
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
    }
  };
}
