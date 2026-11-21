/* data/fate.js - fate cards (owner 2026-11-10, handoff-1110/TC-FATE-CARDS-BRIEF.md). Pure data and helpers, no DOM.
 *
 * Six ending sets. A set is 4 fate cards (card 1 is the seed) and its ending. The three open sets are offered on every
 * route; a theme set only when Pick a Theme (f5, the first theme, never Second Theme) chose that theme. The first seed
 * bought picks the run's ending (sim/economy.js fateOf); no other set's cards ever open after that.
 * The items themselves carry `fate: '<set>'` (and `step: 1..4` on the cards) in data/items.js, made by dev/gen-items.mjs
 * from FATE_SETS below. Ids never change. */

/** id, end (the ending item id), theme (the f5 lane that offers it, or null for an open set), cards (ids, step 1-4). */
export const FATE_SETS = Object.freeze([
  { id: 'outside', end: 'outside', theme: null, cards: ['ft_window', 'ft_air', 'ft_grass', 'ft_door'] },
  { id: 'emi', end: 'go_end', theme: null, cards: ['go_tx', 'go_will', 'go_corp', 'go_hive'] },
  { id: 'button', end: 'o_out', theme: null, cards: ['ft_depth', 'ft_cursor', 'ft_pressed', 'ft_glass'] },
  { id: 'doll', end: 'bi_end', theme: 'bimbo', cards: ['ft_bubble', 'bi_gloss', 'bi_giggle', 'bi_pics'] },
  { id: 'unit', end: 'dr_end', theme: 'drone', cards: ['ft_scan', 'dr_hive', 'dr_update', 'ft_tag'] },
  { id: 'museum', end: 'go_mus', theme: 'goon', cards: ['ft_ticket', 'ft_placard', 'ft_rope', 'ft_gift'] },
].map((set) => Object.freeze({ ...set, cards: Object.freeze(set.cards) })));

/* THE PANEL (owner 2026-11-21, handoff-1121/TC-LLAMA-ROUND-BRIEF.md change 2): one extra open set, offered with the
 * seeds only to a player whose contract shelters at least `herd` llamas (life.llamas, read by the sim as the run's
 * `herd`). One card, then its ending: no cards 2-4, so the Journey fills those places with ordinary rows. Kept apart from
 * FATE_SETS so the six sets, their route sampling and their four-card pacing stay exactly as they were. Players who never
 * touch the llamas lose nothing. */
export const HERD_SETS = Object.freeze([
  { id: 'llama', end: 'll_end', theme: null, herd: 12, cards: ['ft_panel'] },
].map((set) => Object.freeze({ ...set, cards: Object.freeze(set.cards) })));
/** The herd sets' endings: secret until seen (the recap's endings shelf leaves them out until then). */
export const HERD_ENDINGS = Object.freeze(HERD_SETS.map((set) => set.end));
/** Every set a run can hold: the six, then the herd sets. */
export const ALL_SETS = Object.freeze([...FATE_SETS, ...HERD_SETS]);

export const FATE_BY_ID = Object.freeze(Object.fromEntries(ALL_SETS.map((set) => [set.id, set])));
const SET_OF = Object.freeze(Object.fromEntries(ALL_SETS.flatMap((set) => [...set.cards, set.end].map((id) => [id, set.id]))));

/** The set id an item belongs to (a card or an ending), or null. */
export const setOf = (itemId) => SET_OF[itemId] || null;
/** The step 1-4 of a card, 0 for an ending or anything else. */
export const stepOf = (itemId) => { const set = FATE_BY_ID[SET_OF[itemId]]; return set ? set.cards.indexOf(itemId) + 1 : 0; };
/** Every seed (card 1 of every set, the herd sets last), in ALL_SETS order. */
export const seedIds = () => ALL_SETS.map((set) => set.cards[0]);
export const SEEDS = Object.freeze(seedIds());
/** Every card id, all sets. */
export const CARD_IDS = Object.freeze(ALL_SETS.flatMap((set) => set.cards));
/** Every ending id a fate set holds. */
export const FATE_ENDINGS = Object.freeze(ALL_SETS.map((set) => set.end));
export const isSeed = (itemId) => SEEDS.includes(itemId);
export const isCard = (itemId) => CARD_IDS.includes(itemId);

/** True when a herd of this size opens the set (a set with no `herd` is always open on that count). */
export const herdOpens = (set, herd = 0) => !set?.herd || (Number(herd) || 0) >= set.herd;

/** The sets a run may pick from, for the first theme it chose (f5 lane, or null before the pick): the open sets, plus
 *  the theme set when there is one (3 or 4), plus a herd set when the run's herd is big enough. */
export const setsFor = (theme, herd = 0) => ALL_SETS.filter((set) => (!set.theme || set.theme === theme) && herdOpens(set, herd));

/** A set offered to this run's picks (fork picks, as in s.picks) and herd (life.llamas). */
export const offeredTo = (setId, picks = {}, herd = 0) => setsFor(picks.f5 || null, herd).some((set) => set.id === setId);

/** Route harnesses: one sampled set for a fork route, the same every time (a full cross product would multiply the suite).
 *  The hash spreads the sets over the 192 routes; a theme route with a theme set sees it about one route in four. */
export function routeFate(picks = {}) {
  const sets = setsFor(picks.f5 || null);
  const key = Object.keys(picks).filter((f) => f !== 'fate').sort().map((f) => f + '=' + picks[f]).join('/');
  let h = 2166136261;
  for (let i = 0; i < key.length; i++) h = Math.imul(h ^ key.charCodeAt(i), 16777619) >>> 0;
  return sets[h % sets.length].id;
}
