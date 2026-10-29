import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readState, createShop, TAB_ORDER, windowShelves, WINDOW_BAYS } from '../state/shop.js';
import { createMockServer } from '../mock/server.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const row = (id, extra = {}) => ({ id, tier: 'S', priceSp: 10, grants: [`cos.${id}`], owned: null, sale: 'on', ...extra });
const body = (tabs) => ({ ...structuredClone(SAMPLE), tabs });

/** A shop on the mock. `hold(true)` parks requests until `release()`. */
function rig(sample = SAMPLE, opts = {}) {
  const server = createMockServer(sample);
  const sent = [], held = [];
  let n = 0, hold = false;
  const request = (op, b, idem) => {
    sent.push({ op, body: b, idem });
    if (!hold) return server.handle(op, b, idem);
    return new Promise((res) => held.push(() => res(server.handle(op, b, idem))));
  };
  const shop = createShop({ request, newIdem: () => `idem-${String(++n).padStart(12, '0')}`, now: () => 'T', ...opts });
  return { server, shop, sent, buys: () => sent.filter((s) => s.op === 'shop-buy'), hold: (v) => { hold = v; }, release: () => held.splice(0).forEach((f) => f()) };
}
const goTab = (shop, id) => { while (shop.tabId() !== id) shop.intent({ type: 'tabNext' }); };
const at = (shop) => { const f = shop.focus(); return f && [f.bay, f.slot, f.row.id]; };

test('readState: house tab order, unknown tabs dropped, bad rows kept as holes, 4 bays x 9 slots', () => {
  const st = readState({ ...SAMPLE, tabs: [...SAMPLE.tabs].reverse().concat([{ id: 'mystery', shelves: [] }]) });
  assert.deepEqual(st.tabs.map((t) => t.id), TAB_ORDER);
  assert.equal(st.sp, 1850); assert.equal(st.tier, 1); assert.equal(st.week, '2026-W44'); assert.equal(st.catalogVersion, 'cv-sample-1');
  const wide = readState(body([{ id: 'kart', shelves: Array.from({ length: 6 }, () => ({ rows: [row('a'), { nope: 1 }, ...Array.from({ length: 10 }, (_, i) => row(`x${i}`))] })) }]));
  assert.equal(wide.tabs[0].shelves.length, 4);
  assert.equal(wide.tabs[0].shelves[0].rows.length, 9);
  assert.equal(wide.tabs[0].shelves[0].rows[1], null);
  assert.deepEqual(wide.tabs[0].shelves[0].rows[0], { id: 'a', tier: 'S', priceSp: 10, grants: ['cos.a'], needs: null, owned: null, sale: 'on' });
  assert.equal(readState({ ok: false, reason: 'busy' }), null);
  assert.equal(readState({ ok: true }), null);
});

test('focus walk: columns, rows, the empty last slot, edges stay put', async () => {
  const { shop } = rig();
  await shop.load();
  assert.equal(shop.tabId(), 'rain-clothes');
  assert.deepEqual(at(shop), [0, 0, 'rain.dress.midnight-cocktail']);
  assert.deepEqual(shop.intent({ type: 'left' }), {}, 'no bay to the left');
  assert.deepEqual(shop.intent({ type: 'up' }), {});
  shop.intent({ type: 'right' }); shop.intent({ type: 'right' });
  assert.deepEqual(at(shop).slice(0, 2), [0, 2]);
  assert.deepEqual(shop.intent({ type: 'right' }), {});
  shop.intent({ type: 'down' });
  assert.deepEqual(at(shop).slice(0, 2), [0, 5]);
  assert.deepEqual(shop.intent({ type: 'down' }), {}, 'slot r2c2 is empty: down stays');
  shop.intent({ type: 'left' }); shop.intent({ type: 'down' });
  assert.deepEqual(at(shop), [0, 7, 'rain.suit.hazmat-yellow']);
  shop.intent({ type: 'right' });
  assert.deepEqual(at(shop).slice(0, 2), [0, 5], 'r2c2 empty: the nearest filled row of column 2');
  assert.deepEqual(shop.intent({ type: 'right' }), {}, 'no bay to the right');
});

test('focus walk: crosses bays, skips holes and an empty bay, single-item tab never moves', async () => {
  const { shop } = rig(body([
    { id: 'kart', shelves: [{ rows: [row('a'), null, row('c'), row('d')] }, { rows: [] }, { rows: [row('e'), row('f')] }] },
    { id: 'desk', shelves: [{ rows: [] }, { rows: [row('solo')] }] },
  ]));
  await shop.load();
  shop.intent({ type: 'right' });
  assert.deepEqual(at(shop), [0, 2, 'c'], 'hole at slot 1 skipped');
  shop.intent({ type: 'right' });
  assert.deepEqual(at(shop), [2, 0, 'e'], 'empty bay 1 skipped');
  shop.intent({ type: 'left' });
  assert.deepEqual(at(shop), [0, 2, 'c'], 'back into column 2 of bay 0');
  shop.intent({ type: 'left' }); shop.intent({ type: 'down' });
  assert.deepEqual(at(shop), [0, 3, 'd']);
  shop.intent({ type: 'right' });
  assert.deepEqual(at(shop), [0, 2, 'c'], 'r1 has nothing right: nearest row of the next filled column');
  const ch = shop.intent({ type: 'tabNext' });
  assert.equal(ch.tab.id, 'desk');
  assert.deepEqual(at(shop), [1, 0, 'solo'], 'tab change lands on the first item, past an empty bay');
  for (const type of ['left', 'right', 'up', 'down']) assert.deepEqual(shop.intent({ type }), {});
  assert.equal(shop.intent({ type: 'tabNext' }).tab.id, 'kart', 'tabs wrap');
});

test('pick: a tap focuses, a tap on the focused item opens its card, an empty slot does nothing', async () => {
  const { shop } = rig();
  await shop.load();
  assert.deepEqual(shop.intent({ type: 'pick', bay: 0, slot: 8 }), {});
  assert.equal(shop.intent({ type: 'pick', bay: 0, slot: 3 }).focus.row.id, 'rain.dress.plum-asymmetric');
  assert.equal(shop.intent({ type: 'pick', bay: 0, slot: 3 }).card.itemId, 'rain.dress.plum-asymmetric');
  assert.deepEqual(shop.intent({ type: 'right' }), {}, 'the card is modal');
});

test('every card face: buy, owned (wear), short, prime, soon', async () => {
  const { shop, server } = rig();
  await shop.load();
  const open = (tab, slot) => { goTab(shop, tab); shop.intent({ type: 'pick', bay: 0, slot }); const c = shop.card() || shop.intent({ type: 'select' }).card; shop.intent({ type: 'back' }); return c; };
  assert.deepEqual([open('rain-clothes', 0).face, open('rain-clothes', 0).action], ['buy', 'buy']);
  assert.deepEqual([open('rain-clothes', 1).face, open('rain-clothes', 1).action], ['owned', 'wear']);
  assert.deepEqual([open('rain-clothes', 7).face, open('rain-clothes', 7).short], ['short', 690]);
  const p = open('prime', 0);
  assert.deepEqual([p.face, p.action, p.reasonKey], ['prime', 'prime', 'shop_prime_reason']);
  const s = open('backroom', 1);
  assert.deepEqual([s.itemId, s.face, s.action], ['rt_demo', 'soon', null]);
  server.setTier(2); await shop.load();
  assert.equal(open('prime', 0).face, 'buy', 'tier 2 buys Prime rows');
  goTab(shop, 'rain-clothes'); shop.intent({ type: 'pick', bay: 0, slot: 1 }); shop.intent({ type: 'select' });
  assert.deepEqual(shop.intent({ type: 'select' }), { wear: { itemId: 'rain.dress.rose-flare' } });
});

test('confirm: one idem per card, a press while pending sends nothing, the receipt moves the wallet', async () => {
  const r = rig();
  await r.shop.load();
  r.shop.intent({ type: 'select' });
  r.hold(true);
  const first = r.shop.confirm();
  assert.equal(r.shop.card().pending, true);
  assert.equal(await r.shop.confirm(), null);
  assert.deepEqual(r.shop.intent({ type: 'select' }), {}, 'select while pending: the card shows no buy');
  r.release();
  const out = await first;
  assert.equal(r.buys().length, 1);
  assert.deepEqual(r.buys()[0].body, { itemId: 'rain.dress.midnight-cocktail', catalogVersion: 'cv-sample-1', week: '2026-W44' });
  assert.deepEqual(out.effects, [{ type: 'wallet', sp: 1120 }]);
  assert.equal(out.card.face, 'owned');
  assert.equal(r.shop.sp(), 1120);
  assert.deepEqual(r.shop.focus().row.owned, { at: 'T', paidSp: 730 });
  assert.ok(r.shop.state().prizes.grants.includes('cos.rain.dress.midnight-cocktail'));
  r.shop.intent({ type: 'back' }); r.shop.intent({ type: 'right' }); r.shop.intent({ type: 'right' }); r.shop.intent({ type: 'select' });
  r.hold(false);
  await r.shop.confirm();
  assert.notEqual(r.buys()[1].idem, r.buys()[0].idem, 'a new card mints a new idem');
});

test('a lost reply retries with the same idem and the server replays the receipt', async () => {
  const r = rig();
  await r.shop.load();
  r.shop.intent({ type: 'select' });
  r.server.fail('offline', { apply: true });
  const lost = await r.shop.confirm();
  assert.deepEqual([lost.card.retry, lost.card.noteKey, lost.card.face, lost.effects], [true, 'shop_note_retry', 'buy', undefined]);
  assert.equal(r.shop.sp(), 1850, 'echo law: nothing moves before a receipt');
  const ok = await r.shop.confirm();
  assert.equal(r.buys()[0].idem, r.buys()[1].idem);
  assert.deepEqual(ok.effects, [{ type: 'wallet', sp: 1120 }]);
  assert.equal(r.server.state().sp, 1120, 'debited once');
});

test('every refusal maps to a card state', async () => {
  const cases = {
    busy: { face: 'buy', retry: true, noteKey: 'shop_note_retry' },
    offline: { face: 'buy', retry: true, noteKey: 'shop_note_retry' },
    signed_out: { face: 'buy', retry: true, noteKey: 'shop_note_signed_out' },
    owned: { face: 'owned', action: 'wear' },
    needs_prime: { face: 'prime', action: 'prime' },
    not_in_stock: { face: 'gone', action: null },
    unavailable: { face: 'soon', action: null },
    insufficient: { face: 'short', action: null },
    bad_input: { face: 'buy', noteKey: 'shop_note_try_again', fresh: true },
    idem_mismatch: { face: 'buy', noteKey: 'shop_note_try_again', fresh: true },
    stock_changed: { face: 'buy', noteKey: 'shop_note_changed', fresh: true },
    catalog_changed: { face: 'buy', noteKey: 'shop_note_changed', fresh: true },
  };
  for (const [reason, want] of Object.entries(cases)) {
    const r = rig();
    await r.shop.load();
    r.shop.intent({ type: 'select' });
    r.server.fail(reason);
    const { card } = await r.shop.confirm();
    for (const [k, v] of Object.entries(want)) if (k !== 'fresh') assert.equal(card[k], v, `${reason}.${k}`);
    if (want.fresh) {
      await r.shop.confirm();
      assert.notEqual(r.buys()[1].idem, r.buys()[0].idem, `${reason}: a fresh card mints a fresh idem`);
    }
  }
});

test('insufficient adopts the server balance; a reprice reopens the card at the new price', async () => {
  const r = rig();
  await r.shop.load();
  r.shop.intent({ type: 'select' });
  r.server.setSp(100);
  const { card } = await r.shop.confirm();
  assert.deepEqual([card.face, card.short, r.shop.sp()], ['short', 630, 100]);
  r.shop.intent({ type: 'back' }); r.server.setSp(5000);
  await r.shop.load();
  r.shop.intent({ type: 'select' });
  r.server.reprice('rain.dress.midnight-cocktail', 900);
  const again = await r.shop.confirm();
  assert.deepEqual([again.card.priceSp, again.card.noteKey, again.card.pending], [900, 'shop_note_changed', false]);
  assert.equal(r.buys().length, 2, 'never buys on its own');
  const done = await r.shop.confirm();
  assert.deepEqual(done.effects, [{ type: 'wallet', sp: 4100 }]);
});

test('stock_changed: the fresh state replaces the shelf, focus keeps the same item id', async () => {
  const r = rig();
  await r.shop.load();
  r.shop.intent({ type: 'pick', bay: 0, slot: 3 });
  r.shop.intent({ type: 'select' });
  r.server.setWeek('2026-W45', ['rain.dress.midnight-cocktail']);
  const out = await r.shop.confirm();
  assert.deepEqual(at(r.shop), [0, 2, 'rain.dress.plum-asymmetric']);
  assert.deepEqual(out.focus.slot, 2);
  assert.equal(out.tab.shelves[0].rows.length, 7);
  assert.equal((await r.shop.confirm()).effects[0].sp, 1120, 'the reopened card buys at the new week');
  assert.equal(r.buys()[1].body.week, '2026-W45');
  r.shop.intent({ type: 'back' });
  r.server.setWeek('2026-W46', ['rain.dress.plum-asymmetric']);
  await r.shop.load();
  assert.deepEqual(at(r.shop), [0, 0, 'rain.dress.rose-flare'], 'the focused item left: the first item');
});

test('Esc ladder: card, then preview, then close', async () => {
  const { shop } = rig();
  await shop.load();
  assert.ok(shop.intent({ type: 'select' }).card);
  assert.deepEqual(shop.intent({ type: 'back' }), { card: null });
  assert.ok(shop.focus(), 'the preview (focused item) stays');
  assert.deepEqual(shop.intent({ type: 'back' }), { close: { reason: 'back' } });
});

test('load: the host refusal shows as a status, the init tab is honoured', async () => {
  const off = createShop({ request: async () => ({ ok: false, reason: 'signed_out' }), newIdem: () => 'x' });
  assert.deepEqual(await off.load(), { status: 'signed_out' });
  assert.deepEqual(off.intent({ type: 'select' }), { signIn: true }, 'signed out: select asks for the sign-in');

  const thrown = createShop({ request: () => { throw new Error('gone'); }, newIdem: () => 'x' });
  assert.equal((await thrown.load()).status, 'offline');
  const { shop } = rig(SAMPLE, { tab: 'chess' });
  const first = await shop.load();
  assert.deepEqual([first.tab.id, first.focus.row.id], ['chess', 'chess.set.royal-capes']);
  const wrapped = createShop({ request: async () => ({ ok: true, status: 200, body: structuredClone(SAMPLE) }), newIdem: () => 'x' });
  assert.equal((await wrapped.load()).status, 'ready', 'a relay {ok,status,body} is unwrapped');
});

test('signed out: back still leaves, select asks for the sign-in, the rest does nothing', async () => {
  const off = createShop({ request: async () => ({ ok: false, reason: 'signed_out' }), newIdem: () => 'x' });
  assert.deepEqual(off.intent({ type: 'select' }), {}, 'before the load answers there is nothing to sign in to');
  await off.load();
  assert.deepEqual(off.intent({ type: 'select' }), { signIn: true });
  assert.deepEqual(off.intent({ type: 'right' }), {});
  assert.deepEqual(off.intent({ type: 'back' }), { close: { reason: 'back' } });
  const offline = createShop({ request: async () => ({ ok: false, reason: 'offline' }), newIdem: () => 'x' });
  await offline.load();
  assert.deepEqual(offline.intent({ type: 'select' }), {});
  assert.deepEqual(offline.intent({ type: 'back' }), { close: { reason: 'back' } });
});

test('windowShelves: the art in four bays by family, no Back Room rows, no prices, at most nine a bay', () => {
  const items = JSON.parse(readFileSync(new URL('../assets/manifest.json', import.meta.url), 'utf8')).items;
  const shelves = windowShelves(items);
  assert.equal(shelves.length, 4);
  const fam = new Map(items.map((i) => [i.id, i.family]));
  shelves.forEach((s, bay) => {
    assert.ok(s.rows.length > 0 && s.rows.length <= 9, `bay ${bay}`);
    for (const r of s.rows) {
      assert.ok(WINDOW_BAYS[bay].includes(fam.get(r.id)), r.id);
      assert.deepEqual([r.sale, r.owned, r.priceSp], ['soon', null, 0]);
    }
  });
  assert.deepEqual(shelves[2].rows.map((r) => fam.get(r.id)), ['chess', 'chess', 'chess', 'chess', 'chess', 'chess', 'desk', 'desk', 'desk']);
  assert.deepEqual(windowShelves(null).map((s) => s.rows.length), [0, 0, 0, 0]);
});
