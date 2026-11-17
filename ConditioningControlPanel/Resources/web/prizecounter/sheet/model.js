/* sheet/model.js - the 2D counter sheet, pure (no DOM). state/shop.js owns today's rows; sheet/wire.js reads the preview
 * wire (effects, places, gift, stamps, goal, ledger). This only says what to draw.
 *   sheetTabs   Featured (when the wire has a front page), Karts, Chess, Bubbles and effects, Accessories, the rest, Owned
 *   rowsOf      every row as an entry { id, row, tab, group, ti, bay, slot, fx, x } (fx = the effect slot, x = wire extras)
 *   sections    a tab (or a search) -> headed sections of planks of entries, with a place mark where a shelf has one
 *   statusOf    the one status a card wears: Switched on, Wearing, Owned, Not out yet, Prime only, soon, free, a price tag
 *   roomModel   the fitting room for a state/shop.js card; fxRoom the same for an effect row (both ask twice)
 *   frontModel  the Featured page: the spotlight, gift, stamp card, saving for, the receipt, You can buy now */

import { faceOf } from '../state/shop.js';
import { tabKey, itemName, itemKey } from '../state/strings.js';
import { cardModel } from '../hud/card.js';
import { readPreview, FX_SLOTS, placesOf, isHere, couponOn, cardGone } from './wire.js';
import { shownOf } from './shown.js';

export const OWNED = 'owned', FEATURED = 'featured', EFFECTS = 'effects', KARTS = 'kart', CHESS = 'chess', ACCESSORIES = 'accessories';
export const NO_PREVIEW = readPreview(null);
/** The sheet's tabs (owner, 2026-11-17): Featured, Karts, Chess, Bubbles and effects, Accessories, then the state's other
 * tabs in its own order (Prime only, Back Room), then Owned. A state tab hangs in its group; the old tab names a link,
 * a test or a shot may still carry ("games", "you") are aliases. */
const GROUP = { profile: ACCESSORIES, desk: ACCESSORIES };
export const TAB_ALIAS = Object.freeze({ games: KARTS, you: ACCESSORIES });
export const groupOf = (tabId) => GROUP[tabId] || TAB_ALIAS[tabId] || tabId;
const LEAD = [KARTS, CHESS, EFFECTS, ACCESSORIES];
const TAB_WORDS = { featured: 'shop_sheet_tab_featured', kart: 'shop_sheet_tab_karts', chess: 'shop_sheet_tab_chess', effects: 'shop_sheet_tab_effects',
  accessories: 'shop_sheet_tab_accessories', games: 'shop_sheet_tab_games', you: 'shop_sheet_tab_you', prime: 'shop_sheet_prime_only', owned: 'shop_owned' };
export const tabLabelKey = (id) => TAB_WORDS[id] || tabKey(id);
/** A shorter word for a tab whose label is long, shown when the tab bar runs out of room (null = the label fits). */
const TAB_SHORT = { effects: 'shop_sheet_tab_effects_short' };
export const tabShortKey = (id) => TAB_SHORT[id] || null;
/** The section a row hangs in: its effect slot, or its state tab. */
const SECTION_WORDS = new Set(['kart', 'chess', 'profile', 'desk', 'prime', ...FX_SLOTS]);
export const sectionTitleKey = (k) => (SECTION_WORDS.has(k) ? `shop_sheet_sec_${k}` : tabKey(k));
/** The Back Room and Rain shelves keep their tab titles but get a shows line too (sheet/explain.js). */
const SHOWS_MORE = { backroom: 'shop_sheet_shows_backroom', 'rain-clothes': 'shop_sheet_shows_rain', 'rain-hair': 'shop_sheet_shows_rain' };
export const sectionShowsKey = (k) => (SECTION_WORDS.has(k) ? `shop_sheet_shows_${k}` : SHOWS_MORE[k] || null);

export function sheetTabs(tabIds, pv = NO_PREVIEW) {
  const has = new Set((tabIds || []).map(groupOf));
  if (pv.fx.length) has.add(EFFECTS);
  const out = pv.front ? [FEATURED] : [];
  for (const id of LEAD) if (has.has(id)) out.push(id);
  for (const id of tabIds || []) if (!out.includes(groupOf(id))) out.push(groupOf(id));
  return [...out, OWNED];
}
export const slotOf = (id) => String(id || '').split('.').slice(0, 2).join('.');
const isWearing = (row, wearing) => !!(wearing && wearing.get(slotOf(row.id)) === row.id);

/** Every row with where it hangs, holes skipped: the effect rows first (the wire's), then the state's. */
export function rowsOf(st, pv = NO_PREVIEW) {
  const out = [];
  pv.fx.forEach((f) => out.push({ id: f.row.id, row: f.row, tab: EFFECTS, group: EFFECTS, ti: -1, bay: FX_SLOTS.indexOf(f.slot), slot: 0, fx: f.slot, x: pv.extras.get(f.row.id) || {} }));
  if (st) st.tabs.forEach((tab, ti) => tab.shelves.forEach((s, bay) => s.rows.forEach((row, slot) => {
    if (row) out.push({ id: row.id, row, tab: tab.id, group: groupOf(tab.id), ti, bay, slot, fx: null, x: pv.extras.get(row.id) || {} });
  })));
  return out;
}
export const locate = (st, id, pv) => rowsOf(st, pv).find((e) => e.id === id) || null;
const uniq = (list) => { const seen = new Set(); return list.filter((e) => !seen.has(e.id) && seen.add(e.id)); };

/** The name on a card: the wire's name for an effect row ("Heart"), else the item name. label adds the slot's noun ("Heart bubbles"). */
/** An effect row's word from the lexicon (`shop_item_fx_*_name` / `_how`), else the wire's English. */
const fxWord = (t, id, part, wire) => { const k = itemKey(id).replace(/_name$/, '_' + part), v = t(k); return v !== k ? v : wire; };
export const nameOf = (e, t) => (e.fx ? fxWord(t, e.id, 'name', e.x.name || e.id.split('.').pop()) : itemName(t, e.id));
/** An effect row's short how line ("typewriter"), or null. */
export const howOf = (e, t) => (e.x && e.x.how ? fxWord(t, e.id, 'how', e.x.how) : null);
export const labelOf = (e, t) => (e.fx ? t('shop_sheet_fx_label', nameOf(e, t), t('shop_sheet_noun_' + e.fx)) : nameOf(e, t));
/** The words for each ledger code (the server sends codes, never words). buy and coupon name their row. */
const LEDGER_WORDS = { gift: 'shop_sheet_gift', level: 'shop_sheet_led_level', pops: 'shop_sheet_led_pops', play: 'shop_sheet_led_play' };
/** The ledger's line: a code reads as its words (an unknown code as a neutral line, never the code); an older body's
 * { t } (or a bare string) reads as an item's name when it is an item id, else as the host's own words. */
export const ledgerText = (st, pv, t, line) => {
  const l = typeof line === 'string' ? { t: line } : line || {};
  if (l.code) {
    const e = l.id ? locate(st, l.id, pv) : null;
    if (l.code === 'buy') return e ? labelOf(e, t) : t('shop_sheet_led_buy');
    if (l.code === 'coupon') return e ? t('shop_sheet_led_coupon', labelOf(e, t)) : t('shop_sheet_led_coupon_any');
    return t(LEDGER_WORDS[l.code] || 'shop_sheet_led_other');
  }
  const e = locate(st, l.t, pv);
  return e ? labelOf(e, t) : String(l.t || t('shop_sheet_led_other'));
};

/** A shelf head's place mark: the places every row on it shares, or null when they differ. */
function sharedPlaces(list, pv) {
  if (!list.length) return null;
  const first = placesOf(pv, list[0].id).join();
  return list.every((e) => placesOf(pv, e.id).join() === first) ? placesOf(pv, list[0].id) : null;
}

/** Sections: { key, title, meta?, places?, mixed?, planks: [[entry]], empty? }. ctx: { tab, query, t, pv, platform, hereOnly }.
 * mixed = a list across shelves (search, Owned): each away item wears its own place tab. */
export function sections(st, { tab: asked, query = '', t, pv = NO_PREVIEW, platform = 'desktop', hereOnly = false }) {
  if (!st) return [];
  const tab = groupOf(asked);   // an old tab name ("games", "you") reads as its new tab
  const keep = (e) => !hereOnly || isHere(placesOf(pv, e.id), platform);
  const all = rowsOf(st, pv);
  const q = String(query).trim().toLowerCase();
  if (q) {
    const hits = uniq(all).filter(keep).filter((e) => `${labelOf(e, t)} ${howOf(e, t) || ''} ${t(sectionTitleKey(e.fx || e.tab))}`.toLowerCase().includes(q));
    return [{ key: 'q', title: t('shop_sheet_results', hits.length), meta: t('shop_sheet_for', String(query).trim()), mixed: true,
      planks: hits.length ? [hits] : [], empty: hits.length ? null : t('shop_sheet_no_match') }];
  }
  if (tab === OWNED) {
    const by = new Map();
    for (const e of uniq(all)) if (e.row.owned && !e.id.endsWith('.none') && keep(e)) { const k = e.fx || e.tab; (by.get(k) || by.set(k, []).get(k)).push(e); }
    if (!by.size) return [{ key: OWNED, title: t('shop_owned'), planks: [], empty: t('shop_sheet_none_owned') }];
    return [...by].map(([k, list]) => ({ key: k, title: t(sectionTitleKey(k)), meta: t('shop_sheet_n_owned', list.length), mixed: true, planks: [list] }));
  }
  if (tab === FEATURED) return [];
  const mine = all.filter((e) => e.group === tab);
  const out = [...new Set(mine.map((e) => e.fx || e.tab))].map((k) => {
    const list = mine.filter((e) => (e.fx || e.tab) === k), shown = list.filter(keep), planks = [];
    if (list[0].fx) planks.push(shown);
    else for (const e of shown) (planks[e.bay] ||= []).push(e);
    const shows = sectionShowsKey(k);
    return { key: k, title: t(sectionTitleKey(k)), meta: shows ? t(shows) : null, places: k === 'prime' ? null : sharedPlaces(list, pv),
      planks: planks.filter((p) => p && p.length), empty: null };
  }).filter((s) => s.planks.length);
  if (out.length) return out;
  return [{ key: tab, title: t(tabLabelKey(tab)), planks: [], empty: mine.length && hereOnly ? t('shop_sheet_none_here', t('shop_sheet_where_' + platform)) : t('shop_soon') }];
}

/** Paper for a small price, pink for a middle one, ink and gold from 730 up. */
export const tagTier = (n) => (n >= 730 ? 't3' : n >= 360 ? 't2' : 't1');

/** ctx: { sp, tier, wearing (session marks for state rows), pv }. -> { kind, price?, tier?, short? } */
export function statusOf(e, { sp = 0, tier = 0, wearing = null, pv = NO_PREVIEW } = {}) {
  const row = e.row, x = e.x || {}, eq = pv.equip;
  if (e.fx && x.special && row.owned && eq && eq.mix) return { kind: 'on' };
  if (e.fx && eq && eq[e.fx] === row.id) return { kind: 'wearing' };
  if (row.owned) return { kind: !e.fx && isWearing(row, wearing) ? 'wearing' : 'owned' };
  if (x.todo) return { kind: 'todo', prime: row.needs === 'prime' };
  if (couponOn(pv, row)) return { kind: 'free' };
  const f = faceOf(row, sp, tier);
  if (f.face === 'soon' || f.face === 'prime' || f.face === 'discord') return { kind: f.face };
  if (row.priceSp === 0) return { kind: 'free' };
  return { kind: 'price', price: row.priceSp, tier: tagTier(row.priceSp), short: f.face === 'short' };
}
export function statusText(s, t, pv = NO_PREVIEW) {
  return { on: t('shop_sheet_switched_on'), wearing: t('shop_sheet_wearing'), owned: t('shop_owned'), prime: t('shop_sheet_prime_only'),
    soon: t('shop_soon'), discord: t('shop_link_discord'), todo: t(s.prime ? 'shop_sheet_todo_prime' : 'shop_sheet_todo'),
    free: t(pv.stamps && pv.stamps.couponReady ? 'shop_sheet_free_stamps' : 'shop_sheet_free'), price: t('shop_price', s.price) }[s.kind];
}

/** A flat picture where the tree has one; the 3D-only items (Rain, karts, chess, Back Room) get their family glyph. */
export function artOf(id, index, family, base = './') {
  const own = index && index.items && index.items[id];
  const at = (p) => `${base}assets/${p}`;
  if (own && (own.kind === 'card' || own.kind === 'avatar') && own.src) return { kind: own.kind, src: at(own.src) };
  if (own && own.kind === 'desk' && own.over) return { kind: 'desk', base: at(own.base), src: at(own.over) };
  const still = own && (own.still || (own.fallback && own.fallback.still));
  if (still) return { kind: 'swatch', src: at(still) };
  return { kind: 'glyph', family };
}

const BACK = (t) => ({ id: 'back', label: t('shop_sheet_put_back'), main: false, cls: 'line' });
const saveAction = (id, pv, t) => (pv.goal === undefined ? null : pv.goal === id
  ? { id: 'saving', label: t('shop_sheet_saving_this'), main: false, cls: 'line', disabled: true }
  : { id: 'save', label: t('shop_sheet_save_this'), main: true, cls: '' });
const askActions = (t, price, claim, pending) => [
  { id: claim ? 'yesclaim' : 'yes', label: claim ? t('shop_sheet_yes_claim') : t('shop_sheet_yes_buy', price), main: true, cls: 'gold', disabled: !!pending },
  { id: 'cancel', label: t('shop_sheet_cancel'), main: false, cls: 'line', disabled: !!pending }];
const askLine = (t, price, claim, away) => (claim ? t('shop_sheet_ask_claim') : t('shop_sheet_ask', price)) + (away ? ' ' + t('shop_sheet_ask_where') : '');

/** The fitting room for shop.card(): null = nothing picked. asking = Buy was pressed and the Yes step is up.
 * row = the card's row (for the coupon), away = it does not show on this host (the confirm says where it does).
 * at = the card's entry and goes = init.goes: once worn, `shown` says where it shows and may carry a "take me there". */
export function roomModel(card, { sp, t, wearing, asking = false, pv = NO_PREVIEW, row = null, away = false, at = null, goes = null }) {
  if (!card) return null;
  const m = cardModel(card, sp, t);
  const claim = card.face === 'buy' && couponOn(pv, row || { priceSp: card.priceSp, sale: 'on', owned: null });
  const lines = m.lines.filter((l) => l.cls !== 'after' || (!asking && !claim));
  let actions = [BACK(t)], ask = null;
  if (card.face === 'buy' && asking) { ask = askLine(t, card.priceSp, claim, away); actions = askActions(t, card.priceSp, claim, card.pending); }
  else if (card.face === 'buy') actions = [claim ? { id: 'claim', label: t('shop_sheet_claim'), main: true, cls: 'gold' } : { id: 'buy', label: t('shop_buy'), main: true, cls: 'gold' }, BACK(t)];
  else if (card.face === 'owned') {
    const mode = card.wear === undefined ? 'wear' : card.wear;   // state/shop.js wearMode, already read against init.wears
    const on = wearing && wearing.get(slotOf(card.itemId)) === card.itemId;
    if (mode === 'garage') actions = [{ id: 'garage', label: t('shop_wear_in_garage'), main: true, cls: 'line' }];
    else if (mode === 'wear' && !on) actions = [{ id: 'wear', label: t('shop_wear'), main: true, cls: '' }, BACK(t)];
  } else if (card.face === 'short') { const s = saveAction(card.itemId, pv, t); if (s) actions = [s, BACK(t)]; }
  const on = !!(wearing && wearing.get(slotOf(card.itemId)) === card.itemId);
  const shown = card.face === 'owned' && at && !away ? shownOf(at, t, { on, goes }) : null;
  return { itemId: card.itemId, face: card.face, name: m.name, desc: m.desc, lines, ask, note: m.note, pending: !!card.pending, actions, shown };
}

/** The fitting room for an effect row. ctx: { sp, t, pv, asking, pending, note, away, goes }. */
export function fxRoom(e, { sp, t, pv = NO_PREVIEW, asking = false, pending = false, note = null, away = false, goes = null }) {
  if (!e) return null;
  const row = e.row, x = e.x || {}, eq = pv.equip || {}, claim = couponOn(pv, row), price = claim ? 0 : row.priceSp;
  const lines = [], add = (id, label, cls = '', main = true, disabled = false) => ({ id, label, cls, main, disabled });
  let actions = [], ask = null;
  if (!row.owned && !x.todo && row.needs !== 'prime') {
    if (sp >= price) lines.push({ cls: 'after', text: price === 0 ? t('shop_sheet_costs_nothing') : t('shop_sheet_leaves', sp - price) });
    else lines.push({ cls: 'short', text: t('shop_sheet_need_more', price - sp) });
  }
  if (x.todo) actions = [add('todo', t(row.needs === 'prime' ? 'shop_sheet_todo_prime' : 'shop_sheet_todo'), 'line', false, true)];
  else if (x.special && row.owned) actions = [eq.mix ? add('mixoff', t('shop_sheet_switch_off'), 'line') : add('mixon', t('shop_sheet_switch_on'))];
  else if (row.owned && eq[e.fx] === row.id) actions = [row.priceSp > 0 ? add('takeoff', t(away ? 'shop_sheet_take_off_desktop' : 'shop_sheet_take_off'), 'line') : add('default', t('shop_sheet_is_default'), 'line', false, true)];
  else if (row.owned) actions = [add('fxwear', t(away ? 'shop_sheet_wear_desktop' : 'shop_wear'))];
  else if (row.sale !== 'on') actions = [];
  else if (sp >= price) {
    if (asking) { ask = askLine(t, price, claim, away); actions = askActions(t, price, claim, pending); }
    else actions = [claim ? add('claim', t('shop_sheet_claim'), 'gold') : add('buy', t('shop_buy'), 'gold')];
  } else { const s = saveAction(row.id, pv, t); if (s) actions = [s]; }
  if (!ask) actions.push(BACK(t));
  // worn (a bought look, never the free default) or Two-tone switched on: where it shows now
  const on = row.priceSp > 0 && (x.special ? !!eq.mix : eq[e.fx] === row.id);
  const shown = !away ? shownOf(e, t, { on, goes }) : null;
  return { itemId: row.id, face: 'fx', name: labelOf(e, t), desc: x.special ? t('shop_sheet_twotone_where') : t('shop_sheet_shows_' + e.fx) + '.', lines, ask,
    note: pending ? t('shop_pending') : note, pending: !!pending, actions, shown };
}

/** Can this be bought now: not owned, out, not Prime-locked, priced, and the wallet covers it (or the coupon does). */
export function buyable(e, { sp = 0, tier = 0, pv = NO_PREVIEW } = {}) {
  const r = e.row;
  if (r.owned || r.sale !== 'on' || (e.x && e.x.todo) || r.needs === 'discord' || (r.needs === 'prime' && tier < 2) || r.priceSp <= 0) return false;
  return couponOn(pv, r) || r.priceSp <= sp;
}
const hereFirst = (list, pv, platform) => [...list.filter((e) => isHere(placesOf(pv, e.id), platform)), ...list.filter((e) => !isHere(placesOf(pv, e.id), platform))];
export const goalEntry = (st, pv) => (pv.goal ? locate(st, pv.goal, pv) : null);
export const goalReady = (st, pv, sp) => { const g = goalEntry(st, pv); return !!(g && !g.row.owned && sp >= g.row.priceSp); };
/** What waits on the Featured tab: an unopened gift, today's stamp, a ready coupon, a reached goal. */
export function badgeCount(st, pv, sp) {
  const s = pv.stamps;
  return (pv.gift && pv.gift.ready ? 1 : 0) + (s && !s.done && !s.stamped.includes(s.day) ? 1 : 0) + (s && s.couponReady ? 1 : 0) + (goalReady(st, pv, sp) ? 1 : 0);
}

/** The Featured page. ctx: { sp, tier, pv, platform, hereOnly, spot (an "Also new" chip the player put under the light) }. Every part is null (goal: undefined) when the wire did not send it. */
export function frontModel(st, ctx) {
  const { pv = NO_PREVIEW, platform = 'desktop', hereOnly = false, sp = 0 } = ctx;
  const keep = (e) => !hereOnly || isHere(placesOf(pv, e.id), platform);
  let fresh = hereFirst(uniq(pv.fresh.map((id) => locate(st, id, pv)).filter((e) => e && !e.row.owned && keep(e))), pv, platform);
  if (ctx.spot && fresh.some((e) => e.id === ctx.spot)) fresh = [fresh.find((e) => e.id === ctx.spot), ...fresh.filter((e) => e.id !== ctx.spot)];
  const g = goalEntry(st, pv);
  const can = hereFirst(uniq(rowsOf(st, pv)).filter((e) => buyable(e, ctx) && keep(e)), pv, platform).slice(0, 6);
  return {
    hostNote: platform !== 'desktop',
    spot: fresh[0] || null, also: fresh.slice(1),
    gift: pv.gift, stamps: cardGone(pv.stamps) ? null : pv.stamps,   // a finished card stays while its coupon waits
    goal: pv.goal === undefined ? undefined : g && !g.row.owned ? { entry: g, have: Math.min(sp, g.row.priceSp), price: g.row.priceSp, left: Math.max(0, g.row.priceSp - sp) } : null,
    ledger: pv.ledger, can,
  };
}
