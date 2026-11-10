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

export const FATE_BY_ID = Object.freeze(Object.fromEntries(FATE_SETS.map((set) => [set.id, set])));
const SET_OF = Object.freeze(Object.fromEntries(FATE_SETS.flatMap((set) => [...set.cards, set.end].map((id) => [id, set.id]))));

/** The set id an item belongs to (a card or an ending), or null. */
export const setOf = (itemId) => SET_OF[itemId] || null;
/** The step 1-4 of a card, 0 for an ending or anything else. */
export const stepOf = (itemId) => { const set = FATE_BY_ID[SET_OF[itemId]]; return set ? set.cards.indexOf(itemId) + 1 : 0; };
/** The six seeds (card 1 of every set), in FATE_SETS order. */
export const seedIds = () => FATE_SETS.map((set) => set.cards[0]);
export const SEEDS = Object.freeze(seedIds());
/** Every card id, all sets. */
export const CARD_IDS = Object.freeze(FATE_SETS.flatMap((set) => set.cards));
/** The six ending ids on offer. */
export const FATE_ENDINGS = Object.freeze(FATE_SETS.map((set) => set.end));
export const isSeed = (itemId) => SEEDS.includes(itemId);
export const isCard = (itemId) => CARD_IDS.includes(itemId);

/** The sets a run may pick from, for the first theme it chose (f5 lane, or null before the pick): the open sets, plus
 *  the theme set when there is one. 3 or 4. */
export const setsFor = (theme) => FATE_SETS.filter((set) => !set.theme || set.theme === theme);

/** A set offered to this run's picks (fork picks, as in s.picks). */
export const offeredTo = (setId, picks = {}) => setsFor(picks.f5 || null).some((set) => set.id === setId);

/** Route harnesses: one sampled set for a fork route, the same every time (a full cross product would multiply the suite).
 *  The hash spreads the sets over the 192 routes; a theme route with a theme set sees it about one route in four. */
export function routeFate(picks = {}) {
  const sets = setsFor(picks.f5 || null);
  const key = Object.keys(picks).filter((f) => f !== 'fate').sort().map((f) => f + '=' + picks[f]).join('/');
  let h = 2166136261;
  for (let i = 0; i < key.length; i++) h = Math.imul(h ^ key.charCodeAt(i), 16777619) >>> 0;
  return sets[h % sets.length].id;
}
