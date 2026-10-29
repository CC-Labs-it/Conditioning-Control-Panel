/* mock/server.js - a stand-in for /v2/shop/* (CONTRACT.md, B3-SHOP-DECISIONS section 5) for dev.html and the tests.
 * NOT the server (CCP-Server is): same body shapes, a replayed idem answers its receipt byte-identical, every refusal.
 * handle(op, body, idem) answers the body itself, like the host does. fail(reason) forces the next buy's answer. */

const IDEM = /^[A-Za-z0-9_-]{16,64}$/;
const clone = (v) => structuredClone(v);

export function createMockServer(sample, { now = () => new Date().toISOString() } = {}) {
  const st = clone(sample), receipts = new Map(), faults = [], log = [];
  const rows = () => st.tabs.flatMap((t) => t.shelves.flatMap((s) => s.rows));
  const state = () => clone(st);
  const prizes = () => clone(st.prizes);

  function settle(b, idem) {
    const row = rows().find((r) => r.id === b.itemId);
    const spBefore = st.sp;
    st.sp -= row.priceSp;
    for (const r of rows()) if (r.id === row.id) r.owned = { at: now(), paidSp: row.priceSp };
    st.prizes = { revision: st.prizes.revision + 1, grants: [...new Set([...st.prizes.grants, ...row.grants])] };
    const out = { ok: true, idem, itemId: row.id, paidSp: row.priceSp, spBefore, sp: st.sp, catalogVersion: st.catalogVersion, prizes: prizes() };
    receipts.set(idem, { itemId: row.id, body: clone(out) });
    return out;
  }
  const refuse = (reason) => {
    const out = { ok: false, reason };
    if (reason === 'stock_changed' || reason === 'catalog_changed') out.state = state();
    if (reason === 'insufficient') out.sp = st.sp;
    if (reason === 'owned') out.prizes = prizes();
    return out;
  };

  function buy(b, idem) {
    if (typeof idem !== 'string' || !IDEM.test(idem) || typeof b.itemId !== 'string' || b.catalogVersion == null
        || typeof b.week !== 'string') return refuse('bad_input');
    const seen = receipts.get(idem);
    if (seen) return seen.itemId === b.itemId ? clone(seen.body) : refuse('idem_mismatch');
    if (b.catalogVersion !== st.catalogVersion) return refuse('catalog_changed');
    if (b.week !== st.week) return refuse('stock_changed');
    const row = rows().find((r) => r.id === b.itemId);
    if (!row) return refuse('not_in_stock');
    if (row.sale !== 'on') return refuse('unavailable');
    if (row.owned) return refuse('owned');
    if (row.needs === 'prime' && st.tier < 2) return refuse('needs_prime');
    if (st.sp < row.priceSp) return refuse('insufficient');
    return settle(b, idem);
  }

  async function handle(op, body = {}, idem) {
    log.push({ op, idem, body: clone(body) });
    if (op === 'shop-state') return state();
    if (op !== 'shop-buy') return { ok: false, reason: 'bad_input' };
    const f = faults.shift();
    if (!f) return buy(body, idem);
    if (f.apply) buy(body, idem);   // the buy lands, the reply is lost
    return f.reason === 'offline' || f.reason === 'signed_out' ? { ok: false, reason: f.reason } : refuse(f.reason);
  }

  return {
    handle, log, state: () => st,
    /** The next buy answers `reason` (any CONTRACT refusal, or the host's offline / signed_out); apply:true settles it first. */
    fail(reason, { apply = false } = {}) { faults.push({ reason, apply }); },
    setSp(v) { st.sp = v; },
    setTier(v) { st.tier = v; },
    /** A new week of stock: the old week's buys answer stock_changed. */
    setWeek(w, drop = []) { st.week = w; for (const s of st.tabs.flatMap((t) => t.shelves)) s.rows = s.rows.filter((r) => !drop.includes(r.id)); },
    /** A price change bumps catalogVersion (derived on the server). */
    reprice(id, priceSp) { for (const r of rows()) if (r.id === id) r.priceSp = priceSp; st.catalogVersion = `${st.catalogVersion}+`; },
  };
}
