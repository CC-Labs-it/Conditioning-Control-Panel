/* mock/preview.js - the preview wire on the mock (BRIEF "The preview wire"): what the DEBUG desktop backend
 * (CCP_SHOP_PREVIEW=1) answers, for mock/sheet.html and the tests. NOT that backend: same body shapes, same ops.
 *   previewSample(sample)   today's sample plus the effects tab, places on every row, 410 Sparkles, the rubber duck owned
 *   createPreviewServer(s)  createMockServer plus shop-equip, shop-gift, shop-stamp, shop-goal and the coupon buy; a buy
 *                           answers its receipt AND the fresh state (as the desktop backend does);
 *                           shop-state carries preview, fresh, equip, gift, stamps, goal and ledger */

import { createMockServer } from './server.js';

const ALL = ['desktop', 'web', 'phone'];
const FX = {
  bubble: [['soap', 0, { how: 'the default' }], ['rose', 120, { how: 'tint' }], ['mint', 120, { how: 'tint' }], ['lilac', 120, { how: 'tint' }],
    ['marble', 360, { isNew: true }], ['heart', 360, { isNew: true }], ['champagne', 360, {}], ['spiral', 730, { isNew: true }], ['disco', 730, {}],
    ['frost', 360, {}], ['gum', 360, { todo: true, name: 'Bubblegum' }], ['latex', 730, { todo: true }], ['oil', 730, { todo: true, name: 'Oil slick' }]],
  card: [['plain', 0, {}], ['ink', 120, {}], ['check', 120, {}], ['lace', 360, {}]],
  behind: [['none', 0, { name: 'Nothing' }], ['whisper', 360, { how: 'a new word each flip', isNew: true }]],
  fog: [['pink', 0, {}], ['lilac', 120, {}], ['mint', 120, {}], ['amber', 360, {}], ['twotone', 730, { special: true, how: 'two colours swirl', name: 'Two-tone' }]],
  word: [['classic', 0, { how: 'fades' }], ['type', 120, { how: 'typewriter', name: 'Typed' }], ['chalk', 360, { how: 'written on' }],
    ['drift', 360, { how: 'letters rise' }], ['echo', 360, { how: 'ghost copies' }], ['marquee', 730, { how: 'lights up' }]],
};
const cap = (s) => s[0].toUpperCase() + s.slice(1);
const OWNED0 = { at: '2026-11-01T00:00:00Z', paidSp: 0 };
const PLACES = { desk: ['desktop'], 'rain-clothes': ['desktop'], 'rain-hair': ['desktop'], backroom: ['desktop', 'web'] };
const ROW_PLACES = { 'prime.paint.host-gold': ['desktop', 'web'] };

export function previewSample(sample) {
  const s = structuredClone(sample);
  for (const tab of s.tabs) for (const sh of tab.shelves) for (const r of sh.rows) {
    if (!r) continue;
    r.places = ROW_PLACES[r.id] || PLACES[tab.id] || ALL;
    if (r.id === 'kart.outfit.ringmaster') r.isNew = true;
    if (r.id === 'desk.toy.rubber-duck') r.owned = { at: '2026-11-10T09:00:00Z', paidSp: 120 };
  }
  const shelves = Object.entries(FX).map(([slot, list]) => ({ rows: list.map(([k, price, x]) => ({
    id: `fx.${slot}.${k}`, tier: null, priceSp: price, grants: [], needs: null, owned: price === 0 ? { ...OWNED0 } : null,
    sale: x.todo ? 'soon' : 'on', slot, name: x.name || cap(k), places: ['desktop'], ...x,
  })) }));
  s.tabs.unshift({ id: 'effects', shelves });
  s.sp = 410;
  return s;
}

export function createPreviewServer(sample, opts = {}) {
  const base = createMockServer(previewSample(sample), opts), st = base.state();
  const pv = {
    fresh: ['fx.bubble.marble', 'fx.bubble.heart', 'fx.bubble.spiral', 'fx.behind.whisper', 'kart.outfit.ringmaster'],
    equip: { bubble: 'fx.bubble.soap', card: 'fx.card.plain', behind: 'fx.behind.none', fog: 'fx.fog.pink', word: 'fx.word.classic', fog2: 'fx.fog.lilac', mix: false },
    gift: { ready: true, amount: 25, next: 'Monday' },
    stamps: { day: 5, stamped: [1, 2, 4], couponAt: 6, couponReady: false, couponUsed: false },
    goal: 'fx.bubble.spiral',
    ledger: [{ t: 'Won at chess', n: 20 }, { t: 'Session finished', n: 12 }, { t: 'desk.toy.rubber-duck', n: -120 }],
  };
  const booked = new Set();
  const rows = () => st.tabs.flatMap((t) => t.shelves.flatMap((s) => s.rows)).filter(Boolean);
  const row = (id) => rows().find((r) => r.id === id) || null;
  const note = (t, n) => { pv.ledger.unshift({ t, n }); pv.ledger.length = Math.min(pv.ledger.length, 6); };
  const full = () => ({ ...structuredClone(st), preview: true, ...structuredClone(pv) });
  const no = (reason) => ({ ok: false, reason });
  const fxSlot = (id) => { const r = row(id); return r && r.slot ? r.slot : null; };

  function equip({ slot, itemId = null } = {}) {
    if (slot === 'mix') {
      if (itemId !== null && !(row(itemId) && row(itemId).owned && row(itemId).special)) return no('not_owned');
      pv.equip.mix = itemId !== null; return full();
    }
    if (slot === 'fog2') {   // the second colour of the two-tone fog: any fog colour, the two-tone row itself excepted
      if (fxSlot(itemId) !== 'fog' || row(itemId).special) return no('bad_input');
      pv.equip.fog2 = itemId; return full();
    }
    if (!(slot in pv.equip)) return no('bad_input');
    if (itemId === null) { pv.equip[slot] = (rows().find((r) => r.slot === slot && r.priceSp === 0) || {}).id || null; return full(); }
    const r = row(itemId);
    if (!r || r.slot !== slot || r.special) return no('bad_input');
    if (!r.owned) return no('not_owned');
    pv.equip[slot] = itemId; return full();
  }
  function wearOnBuy(r) {   // the preview puts a bought look on at once, as the mockup does
    if (!r || !r.slot) return;
    if (r.special) pv.equip.mix = true; else pv.equip[r.slot] = r.id;
  }

  async function handle(op, body = {}, idem) {
    if (op === 'shop-state') { const res = await base.handle(op, body, idem); return res.ok ? full() : res; }
    if (op === 'shop-equip') return equip(body);
    if (op === 'shop-gift') { if (!pv.gift.ready) return no('gift_opened'); pv.gift.ready = false; st.sp += pv.gift.amount; note('Monday gift', pv.gift.amount); return full(); }
    if (op === 'shop-stamp') {
      const s = pv.stamps;
      if (s.stamped.includes(s.day)) return no('stamped_today');
      s.stamped.push(s.day); s.stamped.sort((a, b) => a - b);
      if (s.stamped.length >= s.couponAt && !s.couponUsed) s.couponReady = true;
      return full();
    }
    if (op === 'shop-goal') { const id = body.itemId ?? null; if (id !== null && !row(id)) return no('not_in_stock'); pv.goal = id; return full(); }
    if (op === 'shop-buy' && body.coupon === true) {
      const r = row(body.itemId);
      if (!pv.stamps.couponReady) return no('no_coupon');
      if (!r || r.priceSp !== 120 || r.owned || r.sale !== 'on' || r.needs === 'prime') return no('bad_input');
      r.owned = { at: new Date().toISOString(), paidSp: 0 };
      pv.stamps.couponReady = false; pv.stamps.couponUsed = true;
      note(r.id, 0); wearOnBuy(r); if (pv.goal === r.id) pv.goal = null;
      return { ...full(), idem, itemId: r.id, paidSp: 0, spBefore: st.sp };
    }
    const res = await base.handle(op, body, idem);
    if (op === 'shop-buy' && res.ok && !booked.has(idem)) {
      booked.add(idem); note(res.itemId, -res.paidSp); wearOnBuy(row(res.itemId)); if (pv.goal === res.itemId) pv.goal = null;
    }
    return op === 'shop-buy' && res.ok ? { ...full(), ...res } : res;   // as the desktop preview backend: the receipt and the fresh state
  }

  return { ...base, handle, preview: pv,
    /** Mock only: the next day of the stamp card. */
    nextDay() { pv.stamps.day = Math.min(14, pv.stamps.day + 1); },
  };
}
