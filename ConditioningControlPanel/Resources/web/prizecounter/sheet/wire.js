/* sheet/wire.js - the preview wire (C:/wt-shop/BRIEF.md "The preview wire"), pure. Every field is optional: an absent
 * field reads as null (or empty) and the part that draws it hides, so the real server's body still draws the slice-1
 * counter. state/shop.js keeps reading today's tabs; this reads the rest of the same body.
 *   readPreview  the shop-state body -> { preview, fx, extras, places, fresh, equip, gift, stamps, goal, ledger, front }
 *   placesOf / isHere / placeMark / awayParts   where a row shows (absent = everywhere)
 *   couponOn     the stamp card's free 120 item applies to this row */

import { readRow } from '../state/shop.js';

export const PLACES = ['desktop', 'web', 'phone'];
export const FX_SLOTS = ['bubble', 'card', 'behind', 'fog', 'word'];
export const EQUIP_KEYS = [...FX_SLOTS, 'fog2'];
export const COUPON_PRICE = 120;
const obj = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? v : null);
const int = (v, d = 0) => (Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : d);
const str = (v) => (typeof v === 'string' && v ? v : null);

/** A places list in house order; absent, empty or unknown = all three. */
export function readPlaces(v) {
  const p = Array.isArray(v) ? PLACES.filter((x) => v.includes(x)) : [];
  return p.length ? p : PLACES.slice();
}

/** The extra row fields: slot, name, how, isNew, todo (true or a colour), special. null when the row has none. */
function readExtra(r) {
  const x = {};
  if (FX_SLOTS.includes(r.slot)) x.slot = r.slot;
  if (str(r.name)) x.name = r.name;
  if (str(r.how)) x.how = r.how;
  if (r.isNew === true) x.isNew = true;
  if (r.todo === true || str(r.todo)) x.todo = r.todo;
  if (r.special === true) x.special = true;
  return Object.keys(x).length ? x : null;
}

const EMPTY = Object.freeze({ preview: false, fx: [], extras: new Map(), places: new Map(), fresh: [], equip: null, gift: null,
  stamps: null, goal: undefined, ledger: null, front: false });

export function readPreview(body) {
  const b = obj(body);
  if (!b || b.ok !== true) return EMPTY;
  const extras = new Map(), places = new Map(), fx = [];
  for (const tab of Array.isArray(b.tabs) ? b.tabs : []) {
    if (!obj(tab)) continue;
    for (const s of Array.isArray(tab.shelves) ? tab.shelves : []) for (const r of (obj(s) && Array.isArray(s.rows) ? s.rows : [])) {
      if (!obj(r) || !str(r.id)) continue;
      if ('places' in r) places.set(r.id, readPlaces(r.places));
      const x = readExtra(r);
      if (x) extras.set(r.id, x);
      if (tab.id === 'effects' && x && x.slot && !fx.some((e) => e.row.id === r.id)) fx.push({ row: readRow(r), slot: x.slot });
    }
  }
  const e = obj(b.equip), g = obj(b.gift), s = obj(b.stamps);
  const equip = e ? { ...Object.fromEntries(EQUIP_KEYS.map((k) => [k, str(e[k])])), mix: e.mix === true } : null;
  const gift = g ? { ready: g.ready === true, amount: Math.max(0, int(g.amount)), next: str(g.next) } : null;
  const stamps = s ? {
    day: Math.min(14, Math.max(1, int(s.day, 1))), stamped: [...new Set((Array.isArray(s.stamped) ? s.stamped : []).map((d) => int(d)).filter((d) => d >= 1 && d <= 14))].sort((x, y) => x - y),
    couponAt: Math.max(1, int(s.couponAt, 6)), couponReady: s.couponReady === true, couponUsed: s.couponUsed === true,
  } : null;
  const goal = 'goal' in b ? str(b.goal) : undefined;   // undefined = no saving-for on this host; null = nothing picked yet
  const ledger = Array.isArray(b.ledger) ? b.ledger.filter((l) => obj(l) && str(l.t) && Number.isFinite(Number(l.n))).slice(0, 6).map((l) => ({ t: l.t, n: int(l.n) })) : null;
  const fresh = Array.isArray(b.fresh) ? b.fresh.filter(str) : [];
  return { preview: b.preview === true, fx, extras, places, fresh, equip, gift, stamps, goal, ledger,
    front: fresh.length > 0 || !!gift || !!stamps || goal !== undefined || !!ledger };
}

export const placesOf = (pv, id) => (pv && pv.places.get(id)) || PLACES;
/** A host that does not say where it runs is the desktop app (WPF sends platform 'desktop'). */
export const platformOf = (init) => (PLACES.includes(init && init.platform) ? init.platform : 'desktop');
export const isHere = (places, platform) => places.includes(platform);

/** The place mark: { all, places, text } in plain words ("Everywhere", "Desktop app only", "Desktop app + Website"). */
export function placeMark(places, t) {
  const all = places.length === PLACES.length;
  const name = (p) => t('shop_sheet_place_' + p);
  const text = all ? t('shop_sheet_everywhere') : places.length === 1 ? t('shop_sheet_only', name(places[0])) : places.map(name).join(' + ');
  return { all, places, text };
}
/** "Shows in the desktop app, not on the website." */
export function awayLine(places, platform, t) {
  const where = (p) => t('shop_sheet_where_' + p);
  return t('shop_sheet_away', places.map(where).join(t('shop_sheet_and')), where(platform));
}

/** The stamp card's coupon: a ready coupon makes any unowned, buyable 120 tag free. */
export const couponOn = (pv, row) => !!(pv && pv.stamps && pv.stamps.couponReady && row && !row.owned && row.sale === 'on'
  && row.needs !== 'prime' && row.priceSp === COUPON_PRICE);
