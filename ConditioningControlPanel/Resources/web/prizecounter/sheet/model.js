/* sheet/model.js - the 2D counter sheet, pure (no DOM). state/shop.js owns every decision; this only says what to draw.
 *   sheetTabs   the index tabs: the state's shelves in house order, then Owned
 *   sections    a tab (or a search) -> headed sections of planks of rows
 *   statusOf    the one status a card wears: Wearing, Owned, Prime only, soon, Link Discord first, or a price tag
 *   artOf       an item's picture from assets/preview/index.json, else the family's ink glyph
 *   roomModel   the fitting room: name, lines, note and actions; Buy asks twice (Buy, then Yes / Cancel)
 *   slotOf      what one Wear replaces (desk.hat, profile.frame ...): a session-local "Wearing" mark */

import { faceOf } from '../state/shop.js';
import { tabKey, itemName } from '../state/strings.js';
import { cardModel } from '../hud/card.js';

export const OWNED = 'owned';
export const sheetTabs = (tabIds) => [...(tabIds || []), OWNED];
export const slotOf = (id) => String(id || '').split('.').slice(0, 2).join('.');
const isWearing = (row, wearing) => !!(wearing && wearing.get(slotOf(row.id)) === row.id);

/** Every row of the state with where it hangs, holes skipped. */
export function rowsOf(st) {
  const out = [];
  if (!st) return out;
  st.tabs.forEach((tab, ti) => tab.shelves.forEach((s, bay) => s.rows.forEach((row, slot) => { if (row) out.push({ row, tab: tab.id, ti, bay, slot }); })));
  return out;
}
export const locate = (st, id) => rowsOf(st).find((r) => r.row.id === id) || null;

/** Sections to draw: { key, title, meta?, planks: [[entry]] }. An empty list answers one section with an `empty` line. */
export function sections(st, { tab, query = '', t }) {
  if (!st) return [];
  const q = String(query).trim().toLowerCase();
  if (q) {
    const seen = new Set();
    const hits = rowsOf(st).filter((e) => !seen.has(e.row.id) && seen.add(e.row.id) && itemName(t, e.row.id).toLowerCase().includes(q));
    return [{ key: 'q', title: `"${String(query).trim()}"`, planks: hits.length ? [hits] : [], empty: hits.length ? null : t('shop_sheet_no_match') }];
  }
  if (tab === OWNED) {
    const seen = new Set(), by = new Map();
    for (const e of rowsOf(st)) if (e.row.owned && !seen.has(e.row.id)) { seen.add(e.row.id); (by.get(e.tab) || by.set(e.tab, []).get(e.tab)).push(e); }
    if (!by.size) return [{ key: OWNED, title: t('shop_owned'), planks: [], empty: t('shop_sheet_none_owned') }];
    return [...by].map(([id, list]) => ({ key: id, title: t(tabKey(id)), meta: String(list.length), planks: [list] }));
  }
  const all = rowsOf(st).filter((e) => e.tab === tab);
  const planks = [];
  for (const e of all) (planks[e.bay] ||= []).push(e);
  return [{ key: tab, title: t(tabKey(tab)), planks: planks.filter(Boolean), empty: all.length ? null : t('shop_soon') }];
}

/** Paper for a small price, pink for a middle one, ink and gold from 730 up. */
export const tagTier = (n) => (n >= 730 ? 't3' : n >= 360 ? 't2' : 't1');

/** { kind: 'wearing'|'owned'|'prime'|'soon'|'discord'|'price', price?, tier?, short? } */
export function statusOf(row, sp, tier, wearing) {
  const f = faceOf(row, sp, tier);
  if (f.face === 'owned') return { kind: isWearing(row, wearing) ? 'wearing' : 'owned' };
  if (f.face === 'soon' || f.face === 'prime' || f.face === 'discord') return { kind: f.face };
  return { kind: 'price', price: row.priceSp, tier: tagTier(row.priceSp), short: f.face === 'short' };
}
export function statusText(s, t) {
  return { wearing: t('shop_sheet_wearing'), owned: t('shop_owned'), prime: t('shop_sheet_prime_only'), soon: t('shop_soon'),
    discord: t('shop_link_discord'), price: t('shop_price', s.price) }[s.kind];
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

/** The fitting room for shop.card(): null = nothing picked. asking = Buy was pressed and the Yes step is up. */
export function roomModel(card, { sp, t, wearing, asking = false }) {
  if (!card) return null;
  const m = cardModel(card, sp, t);
  const lines = m.lines.filter((l) => l.cls !== 'after' || !asking);
  const back = { id: 'back', label: t('shop_keep_looking'), main: false, cls: 'line' };
  let actions = [back], ask = null;
  if (card.face === 'buy' && asking) {
    ask = t('shop_sheet_ask', card.priceSp);
    actions = [{ id: 'yes', label: t('shop_sheet_yes_buy', card.priceSp), main: true, cls: 'gold', disabled: !!card.pending },
      { id: 'cancel', label: t('shop_sheet_cancel'), main: false, cls: 'line', disabled: !!card.pending }];
  } else if (card.face === 'buy') actions = [{ id: 'buy', label: t('shop_buy'), main: true, cls: 'gold' }, back];
  else if (card.face === 'owned') {
    const mode = card.wear === undefined ? 'wear' : card.wear;   // state/shop.js wearMode, already read against init.wears
    const on = wearing && wearing.get(slotOf(card.itemId)) === card.itemId;
    if (mode === 'garage') actions = [{ id: 'garage', label: t('shop_wear_in_garage'), main: true, cls: 'line' }];
    else if (mode === 'wear' && !on) actions = [{ id: 'wear', label: t('shop_wear'), main: true, cls: '' }, back];
  }
  return { itemId: card.itemId, face: card.face, name: m.name, desc: m.desc, lines, ask, note: m.note, pending: !!card.pending, actions };
}
