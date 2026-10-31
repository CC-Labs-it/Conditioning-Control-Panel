// Rain V2 is postponed: a host that does not send init.rainV2 true never shows her (website, phone). The desktop's host
// strips her too (PrizeCounterHostService.HideRain); this is what covers the hosts with no strip of their own.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readState, createShop } from '../state/shop.js';

const row = (id, owned = null) => ({ id, tier: 'S', priceSp: 10, grants: ['cos.' + id], owned, sale: 'on' });
const tab = (id, rows) => ({ id, shelves: [{ rows }] });
const body = () => ({
  ok: true, sp: 50, tier: 1, week: '2026-W44', catalogVersion: 'cv', prizes: { revision: 1, grants: [] },
  tabs: [
    tab('rain-clothes', [row('rain.dress.scarlet-bell', { at: 'T', paidSp: 10 })]),
    tab('rain-hair', []),
    tab('kart', [row('kart.body.checkmate')]),
    tab('profile', [row('profile.frame.lace'), row('profile.avatar.rain-portrait', { at: 'T', paidSp: 10 }), row('profile.avatar.emi-pixel')]),
  ],
});

test('rain false: her tabs go, even for an owner and even empty, and her rows become holes', () => {
  const st = readState(body(), { rain: false });
  assert.deepEqual(st.tabs.map((t) => t.id), ['kart', 'profile']);
  const rows = st.tabs[1].shelves[0].rows;
  assert.equal(rows[0].id, 'profile.frame.lace');
  assert.equal(rows[1], null, 'the slot stays a hole, so the layout does not shift');
  assert.equal(rows[2].id, 'profile.avatar.emi-pixel');
});

test('rain true, and the default, keep her: a desktop with the gate on is untouched', () => {
  for (const st of [readState(body(), { rain: true }), readState(body())]) {
    assert.deepEqual(st.tabs.map((t) => t.id), ['rain-clothes', 'rain-hair', 'kart', 'profile']);
    assert.equal(st.tabs[3].shelves[0].rows[1].id, 'profile.avatar.rain-portrait');
  }
});

test('a shop with rain false opens on the first real tab, also when the door asked for a Rain tab', async () => {
  for (const startTab of [null, 'rain-clothes']) {
    const shop = createShop({ request: async () => body(), newIdem: () => 'idem-000000000001', tab: startTab, rain: false });
    await shop.load();
    assert.equal(shop.tabId(), 'kart');
  }
});

test('a refusal that carries a fresh state stays free of her too', async () => {
  let n = 0;
  const shop = createShop({
    request: async (op) => (op === 'shop-state' ? body() : { ok: false, reason: 'stock_changed', state: body(), n: ++n }),
    newIdem: () => 'idem-000000000001', rain: false,
  });
  await shop.load();
  shop.intent({ type: 'pick', bay: 0, slot: 0 });
  if (!shop.card()) shop.intent({ type: 'select' });
  await shop.intent({ type: 'select' })?.then?.(() => {});
  assert.equal(shop.tabId(), 'kart');
});

test('main.js wires the gate from init.rainV2, strictly', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /rain: init\.rainV2 === true/);
});
