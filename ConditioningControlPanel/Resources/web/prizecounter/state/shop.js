/* shop.js - the Prize Counter's state, pure (no DOM, no three). CONTRACT.md is the wire.
 *   readState   the /v2/shop/state body -> tabs in house order, shelves of at most 4 bays x 9 slots
 *   faceOf      which face a confirm card shows
 *   createShop  load, tabs, the focus walk, the confirm card, one idem per card, receipts and refusals
 * intent() and confirm() answer what changed: { tab, focus, card, close, effects, buy, wear, prime }. */

export const TAB_ORDER = ['rain-clothes', 'rain-hair', 'kart', 'chess', 'profile', 'desk', 'prime', 'backroom'];
export const BAYS = 4, SLOTS = 9;
const int = (v, d = 0) => (Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : d);
const obj = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? v : null);
const strs = (v) => (Array.isArray(v) ? v.map(String) : []);

/** One row; anything unusable reads as null and stays a hole, so slots keep the server's layout. */
export function readRow(r) {
  if (!obj(r) || typeof r.id !== 'string' || !r.id) return null;
  const o = obj(r.owned);
  return { id: r.id, tier: typeof r.tier === 'string' ? r.tier : null, priceSp: Math.max(0, int(r.priceSp)), grants: strs(r.grants),
    needs: r.needs === 'prime' ? 'prime' : null, owned: o ? { at: o.at ?? null, paidSp: int(o.paidSp) } : null,
    sale: r.sale === 'on' ? 'on' : 'soon' };
}

/** The state body -> the page's state, or null. Unknown tabs are dropped; known ones come in TAB_ORDER. */
export function readState(body) {
  if (!obj(body) || body.ok !== true || !Array.isArray(body.tabs)) return null;
  const byId = new Map();
  for (const t of body.tabs) {
    if (!obj(t) || !TAB_ORDER.includes(t.id) || byId.has(t.id)) continue;
    const shelves = (Array.isArray(t.shelves) ? t.shelves : []).slice(0, BAYS)
      .map((s) => ({ rows: (obj(s) && Array.isArray(s.rows) ? s.rows : []).slice(0, SLOTS).map(readRow) }));
    byId.set(t.id, { id: t.id, shelves });
  }
  const p = obj(body.prizes) || {};
  return { sp: Math.max(0, int(body.sp)), catalogVersion: body.catalogVersion ?? null, week: String(body.week ?? ''),
    tier: int(body.tier), prizes: { revision: int(p.revision), grants: strs(p.grants) },
    tabs: TAB_ORDER.filter((id) => byId.has(id)).map((id) => byId.get(id)) };
}

/** Owned, then soon, then a Prime row below tier 2, then short by N, then buy. */
export function faceOf(row, sp, tier) {
  if (row.owned) return { face: 'owned', action: 'wear' };
  if (row.sale !== 'on') return { face: 'soon', action: null };
  if (row.needs === 'prime' && tier < 2) return { face: 'prime', action: 'prime', reasonKey: 'shop_prime_reason' };
  const short = row.priceSp - sp;
  return short > 0 ? { face: 'short', action: null, short } : { face: 'buy', action: 'buy' };
}

const at = (shelves, bay, slot) => (shelves[bay] && shelves[bay].rows[slot]) || null;
function firstItem(shelves) {
  for (let b = 0; b < shelves.length; b++) for (let s = 0; s < SLOTS; s++) if (at(shelves, b, s)) return { bay: b, slot: s };
  return null;
}
/** The filled slot in column c of a bay nearest row r (the upper one on a tie), or null. */
function inColumn(shelves, bay, c, r) {
  let best = null;
  for (let rr = 0; rr < 3; rr++) if (at(shelves, bay, rr * 3 + c) && (best === null || Math.abs(rr - r) < Math.abs(best - r))) best = rr;
  return best === null ? null : { bay, slot: best * 3 + c };
}
/** One step of the focus walk, or null (focus stays). Left/right cross into the neighbour bay; up/down stay in the column. */
export function walk(shelves, f, dir) {
  const r = Math.floor(f.slot / 3), c = f.slot % 3;
  if (dir === 'up' || dir === 'down') {
    const d = dir === 'up' ? -1 : 1;
    for (let rr = r + d; rr >= 0 && rr < 3; rr += d) if (at(shelves, f.bay, rr * 3 + c)) return { bay: f.bay, slot: rr * 3 + c };
    return null;
  }
  const d = dir === 'right' ? 1 : -1;
  for (let b = f.bay, cc = c + d; b >= 0 && b < shelves.length;) {
    if (cc < 0 || cc > 2) { b += d; cc = d > 0 ? 0 : 2; continue; }
    const hit = inColumn(shelves, b, cc, r);
    if (hit) return hit;
    cc += d;
  }
  return null;
}

const RETRY = { busy: 'shop_note_retry', offline: 'shop_note_retry', timeout: 'shop_note_retry', signed_out: 'shop_note_signed_out' };

/** deps: { request(op, body, idem) -> body, newIdem(), tab? (init.tab), now? }. request may answer {ok,status,body} too. */
export function createShop({ request, newIdem, tab: startTab = null, now = () => new Date().toISOString() }) {
  let st = null, tab = 0, foc = null, card = null, status = 'idle';
  const send = (op, body, idem) => Promise.resolve().then(() => request(op, body, idem)).catch(() => null)
    .then((res) => (obj(res) && obj(res.body) && 'status' in res ? res.body : res) || { ok: false, reason: 'offline' });
  const shelves = () => (st && st.tabs[tab] ? st.tabs[tab].shelves : []);
  const tabView = () => ({ id: st.tabs[tab] ? st.tabs[tab].id : null, shelves: shelves() });
  const focusView = () => (foc ? { ...foc, row: at(shelves(), foc.bay, foc.slot) } : null);
  const rowsOf = (id) => (st ? st.tabs.flatMap((t) => t.shelves.flatMap((s) => s.rows)).filter((r) => r && r.id === id) : []);
  function find(id) {   // the current tab first, then the rest in order
    const order = st.tabs.map((_, i) => i).sort((a, b) => (a === tab ? -1 : b === tab ? 1 : a - b));
    for (const ti of order) for (const [b, s] of st.tabs[ti].shelves.entries()) {
      const slot = s.rows.findIndex((r) => r && r.id === id);
      if (slot >= 0) return { tab: ti, bay: b, slot };
    }
    return null;
  }
  function cardView() {
    const row = card && rowsOf(card.itemId)[0];
    if (!row) return null;
    const f = card.forced || faceOf(row, st.sp, st.tier);
    return { itemId: row.id, priceSp: row.priceSp, ...f, noteKey: card.noteKey, pending: card.pending, retry: card.retry };
  }
  const openCard = (itemId, noteKey = null) => {
    card = { itemId, idem: newIdem(), catalogVersion: st.catalogVersion, week: st.week, pending: false, retry: false, noteKey, forced: null };
    return cardView();
  };
  /** Adopt a fresh state; focus stays on the same item id when it still exists, else the tab's first item. */
  function replace(next) {
    const keep = foc && at(shelves(), foc.bay, foc.slot);
    const tabId = st ? (st.tabs[tab] || {}).id : startTab;
    st = next;
    tab = Math.max(0, st.tabs.findIndex((t) => t.id === tabId));
    const hit = keep && find(keep.id);
    if (hit) { tab = hit.tab; foc = { bay: hit.bay, slot: hit.slot }; } else foc = firstItem(shelves());
    if (card && !rowsOf(card.itemId).length) card = null;
  }
  const act = () => {
    const v = cardView();
    if (!v || v.pending) return {};
    if (v.action === 'buy') return { buy: shop.confirm() };
    if (v.action === 'wear') return { wear: { itemId: v.itemId } };
    if (v.action === 'prime') return { prime: { itemId: v.itemId } };
    return {};
  };

  const shop = {
    status: () => status, state: () => st, sp: () => (st ? st.sp : 0),
    tabs: () => (st ? st.tabs.map((t) => t.id) : []), tabId: () => (st && st.tabs[tab] ? st.tabs[tab].id : null),
    shelves, focus: focusView, card: cardView,

    async load() {
      status = 'loading';
      const res = await send('shop-state');
      const next = readState(res);
      if (!next) { status = res.reason === 'signed_out' ? 'signed_out' : 'offline'; return { status }; }
      replace(next); status = 'ready';
      return { status, tab: tabView(), focus: focusView(), card: cardView() };
    },

    intent(i) {
      if (!st || !i) return {};
      const type = i.type;
      if (type === 'back') { if (card) { card = null; return { card: null }; } return { close: { reason: 'back' } }; }
      if (type === 'select') { if (card) return act(); return foc && focusView().row ? { card: openCard(focusView().row.id) } : {}; }
      if (card) return {};   // the card is modal: only select and back reach it
      if (type === 'tabPrev' || type === 'tabNext') {
        const n = st.tabs.length;
        if (n < 2) return {};
        tab = (tab + (type === 'tabNext' ? 1 : -1) + n) % n;
        foc = firstItem(shelves());
        return { tab: tabView(), focus: focusView() };
      }
      if (type === 'pick') {
        const f = { bay: int(i.bay, -1), slot: int(i.slot, -1) };
        if (!at(shelves(), f.bay, f.slot)) return {};
        if (foc && foc.bay === f.bay && foc.slot === f.slot) return { card: openCard(focusView().row.id) };
        foc = f;
        return { focus: focusView() };
      }
      const f = foc && walk(shelves(), foc, type);
      if (!f) return {};
      foc = f;
      return { focus: focusView() };
    },

    /** One buy with the card's idem. A press while pending sends nothing; a retry reuses the idem. */
    async confirm() {
      const c = card, v = cardView();
      if (!c || c.pending || !v || v.action !== 'buy') return null;
      c.pending = true; c.retry = false; c.noteKey = null;
      const res = await send('shop-buy', { itemId: c.itemId, catalogVersion: c.catalogVersion, week: c.week }, c.idem);
      c.pending = false;
      const open = card === c, out = {};
      if (res.ok === true && res.itemId === c.itemId) {
        if (Number.isFinite(Number(res.sp))) st.sp = Math.max(0, int(res.sp));
        for (const r of rowsOf(c.itemId)) r.owned = { at: now(), paidSp: int(res.paidSp, r.priceSp) };
        if (obj(res.prizes)) st.prizes = { revision: int(res.prizes.revision), grants: strs(res.prizes.grants) };
        out.effects = [{ type: 'wallet', sp: st.sp }];
      } else {
        const reason = String(res.reason || 'offline');
        const row = rowsOf(c.itemId)[0];
        if (RETRY[reason]) { c.retry = true; c.noteKey = RETRY[reason]; }
        else if (reason === 'owned') { for (const r of rowsOf(c.itemId)) if (!r.owned) r.owned = { at: now(), paidSp: 0 }; }
        else if (reason === 'needs_prime') c.forced = { face: 'prime', action: 'prime', reasonKey: 'shop_prime_reason' };
        else if (reason === 'not_in_stock') c.forced = { face: 'gone', action: null };
        else if (reason === 'unavailable') c.forced = { face: 'soon', action: null };
        else if (reason === 'insufficient') {
          if (Number.isFinite(Number(res.sp))) st.sp = Math.max(0, int(res.sp));
          c.forced = { face: 'short', action: null, short: Math.max(1, row ? row.priceSp - st.sp : 1) };
        } else {   // stock_changed and catalog_changed carry a fresh state; bad_input, idem_mismatch and anything new read it again
          const next = (reason === 'stock_changed' || reason === 'catalog_changed') && readState({ ok: true, ...(obj(res.state) || res) });
          if (next) replace(next); else await shop.load();
          out.tab = tabView(); out.focus = focusView();
          if (open && card) openCard(c.itemId, next ? 'shop_note_changed' : 'shop_note_try_again');
        }
      }
      if (open) out.card = cardView();
      return out;
    },
  };
  return shop;
}
