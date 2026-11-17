// The server's rules on the sheet (CCP-Server shop-stamps.js, shop-ledger.js, shop-routes.js): ONE stamp card per
// account that ends with done, a coupon that follows tier S, the ledger as fixed codes, a line for every refusal reason,
// and every 7.3 field (equip, gift, stamps, goal, ledger) may be absent (its flag off) with the page hiding that part.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { readState } from '../state/shop.js';
import { sheetT, refusalKey, SHEET_LEX } from '../sheet/strings.js';
import { readPreview, couponOn, cardGone } from '../sheet/wire.js';
import { frontModel, badgeCount, ledgerText, locate, sections, EFFECTS } from '../sheet/model.js';
import { createPreviewServer } from '../mock/preview.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const t = sheetT({});
const fakeWin = () => ({ addEventListener() {}, removeEventListener() {} });
async function mountOn(server, init = {}) {
  const host = asHost({ init: { motion: 'full', ...init }, request: (op, b, idem) => server.handle(op, b, idem), wallet() {}, close() {}, wear() {} });
  const doc = fakeDocument(), root = doc.createElement('main');
  const page = await mountSheet(host, { root, doc, win: fakeWin(), now: () => 1e6, frame: (f) => f(), later() {}, loadIndex: () => null });
  const btn = (key) => page.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  return { page, btn, text: () => page.view.list.text() };
}
/** A preview server whose shop-state drops the named fields, as a server with those flags off. */
function without(...fields) {
  const server = createPreviewServer(SAMPLE), handle = server.handle;
  server.handle = async (op, b, i) => { const res = await handle(op, b, i); if (res && res.ok) for (const f of fields) delete res[f]; return res; };
  return server;
}
const buy = (server, itemId, idem, coupon = false) => server.handle('shop-buy', { itemId, catalogVersion: SAMPLE.catalogVersion, week: SAMPLE.week, ...(coupon ? { coupon: true } : {}) }, idem);

test('one card per account: done after day 14, stamping answers done, an earned coupon still works, then the card goes', async () => {
  const server = createPreviewServer(SAMPLE);
  for (const d of [5, 6, 7]) { server.preview.stamps.day = d; assert.equal((await server.handle('shop-stamp', {})).ok, true); }
  assert.equal((await server.handle('shop-stamp', {})).reason, 'stamped');
  for (let i = 0; i < 9; i++) server.nextDay();   // past day 14
  const s = readPreview(await server.handle('shop-state')).stamps;
  assert.deepEqual([s.done, s.day, s.couponReady, s.stamped], [true, 14, true, [1, 2, 4, 5, 6, 7]]);
  assert.equal((await server.handle('shop-stamp', {})).reason, 'done');
  assert.equal(cardGone(s), false);

  const st = readState(await server.handle('shop-state'), { rain: false }), pv = readPreview(await server.handle('shop-state'));
  assert.ok(frontModel(st, { sp: 410, pv }).stamps, 'a finished card stays while its coupon waits');
  assert.equal(badgeCount(st, pv, 0), 2, 'the coupon waits, and no stamp is owed on a finished card');

  const r = await mountOn(server);
  assert.match(r.text(), /Stamp card Finished/);
  assert.match(r.text(), /Card done\. Your free item still works\./);
  assert.match(r.text(), /Ready\. Pick one\./);
  assert.equal(r.btn('stamp'), null, 'no Stamp button on a finished card');

  assert.equal((await buy(server, 'fx.card.lace', 'idem-lace-coupon-01', true)).reason, 'unavailable');   // tier M
  assert.equal((await buy(server, 'fx.card.ink', 'idem-ink-coupon-001', true)).ok, true);
  assert.equal((await buy(server, 'fx.card.check', 'idem-chk-coupon-001', true)).reason, 'not_ready');
  const after = readPreview(await server.handle('shop-state'));
  assert.equal(cardGone(after.stamps), true);
  assert.equal(frontModel(readState(await server.handle('shop-state'), { rain: false }), { sp: 410, pv: after }).stamps, null);
  const gone = await mountOn(server);
  assert.doesNotMatch(gone.text(), /Stamp card (Finished|Your)|1 free item/);
  assert.match(gone.text(), /Monday gift/);
});

test('a finished card that never earned its coupon leaves the front page at once', async () => {
  const body = await createPreviewServer(SAMPLE).handle('shop-state');
  body.stamps = { ...body.stamps, done: true, stamped: [1, 2], couponReady: false, couponUsed: false };
  const pv = readPreview(body);
  assert.equal(cardGone(pv.stamps), true);
  assert.equal(frontModel(readState(body, { rain: false }), { sp: 0, pv }).stamps, null);
});

test('the coupon follows tier S, never a number; its price on the card comes from the shelf', async () => {
  const body = await createPreviewServer(SAMPLE).handle('shop-state');
  const pv0 = readPreview(body);
  assert.equal(pv0.couponPrice, 120);
  const ready = { ...pv0, stamps: { ...pv0.stamps, couponReady: true } };
  assert.equal(couponOn(ready, { priceSp: 150, tier: 'S', sale: 'on', owned: null }), true, 'a moved evening moves the coupon with it');
  assert.equal(couponOn(ready, { priceSp: 120, tier: 'M', sale: 'on', owned: null }), false);
  assert.equal(couponOn(ready, { priceSp: 120, tier: null, sale: 'on', owned: null }), true, 'no letter: the tier\'s price');
  assert.equal(couponOn(ready, { priceSp: 120, tier: 'S', sale: 'on', owned: null, needs: 'prime' }), false);
  // the card's terms name the shelf's price, and a body with no tier S row says it without one
  for (const tab of body.tabs) for (const sh of tab.shelves) for (const row of sh.rows) if (row && row.tier === 'S') row.priceSp = 150;
  const r = await mountOn({ handle: async (op) => (op === 'shop-state' ? body : { ok: false, reason: 'bad_input' }) });
  assert.match(r.text(), /any 150 tag, at 6 stamps/);
  assert.equal(t('shop_sheet_coupon_terms_any', 6), 'one small item, at 6 stamps');
});

test('the ledger reads codes as words, an unknown code as a neutral line, an old English line as itself', async () => {
  const server = createPreviewServer(SAMPLE), body = await server.handle('shop-state');
  const st = readState(body, { rain: false }), pv = readPreview(body);
  const words = (l) => ledgerText(st, pv, t, l);
  assert.equal(words({ code: 'buy', n: -360, id: 'fx.bubble.heart' }), 'Heart bubbles');
  assert.equal(words({ code: 'buy', n: -120, id: 'desk.toy.rubber-duck' }), 'The Suggestible Duck');
  assert.equal(words({ code: 'buy', n: -120, id: 'gone.item' }), 'Bought an item');
  assert.equal(words({ code: 'coupon', n: 0, id: 'fx.card.ink' }), 'Stamp card: Ink card frame');
  assert.equal(words({ code: 'coupon', n: 0 }), 'Stamp card item');
  assert.equal(words({ code: 'gift', n: 25 }), 'Monday gift');
  assert.deepEqual(['level', 'pops', 'play'].map((code) => words({ code, n: 1 })), ['Level up', 'Bubble pops', 'Earned playing']);
  assert.equal(words({ code: 'stake_win', n: 9 }), 'Sparkles moved');
  assert.equal(words({ t: 'Won at chess', n: 20 }), 'Won at chess');
  // the wire keeps codes and drops junk, newest first, at most six
  const read = readPreview({ ...body, ledger: [{ code: 'gift', n: 25, at: 'x' }, { t: 'Old line', n: 3 }, { n: 4 }, { code: 'buy', n: 'x' }, null] }).ledger;
  assert.deepEqual(read, [{ code: 'gift', n: 25, id: null }, { t: 'Old line', n: 3 }]);
  const r = await mountOn(server);
  const receipt = r.text().split('Earned and spent')[1].split('In the wallet')[0];
  assert.doesNotMatch(receipt, /\b(play|buy|coupon|gift|level)\b/);
  assert.match(r.text(), /Earned playing \+20 Bubble pops \+12 The Suggestible Duck -120/);
});

test('every refusal reason the server uses gets its own sensible line', () => {
  const lines = Object.fromEntries(['unavailable', 'stamped', 'done', 'busy', 'not_ready', 'owned', 'needs_prime', 'bad_input', 'not_owned', 'insufficient', 'signed_out', 'zebra']
    .map((reason) => [reason, t(refusalKey({ ok: false, reason }))]));
  assert.equal(lines.stamped, 'Already stamped today.');
  assert.equal(lines.done, 'The stamp card is finished.');
  assert.equal(lines.not_ready, 'Not ready yet.');
  assert.equal(lines.owned, 'You already own this.');
  assert.equal(lines.needs_prime, 'Prime members can buy this one.');
  assert.equal(lines.unavailable, 'Not on offer right now.');
  assert.equal(lines.busy, 'The counter did not answer. Confirm again to retry.');
  assert.equal(lines.bad_input, 'That did not go through.');
  assert.equal(lines.zebra, 'That did not go through.');
  assert.equal(t(refusalKey(null)), 'The counter did not answer. Confirm again to retry.');
  for (const [reason, line] of Object.entries(lines)) assert.doesNotMatch(line, /^shop_/, reason);
  for (const k of Object.keys(SHEET_LEX)) assert.doesNotMatch(SHEET_LEX[k], /[–—!]/, k);
});

test('each 7.3 field may be absent: equip, gift, stamps, goal, ledger each hide their own part', async () => {
  const parts = { gift: /Monday gift/, stamps: /Stamp card/, goal: /Saving for/, ledger: /Earned and spent/ };
  for (const [field, words] of Object.entries(parts)) {
    const r = await mountOn(without(field));
    assert.doesNotMatch(r.text(), words, `${field} absent`);
    for (const [other, w] of Object.entries(parts)) if (other !== field) assert.match(r.text(), w, `${other} stays when ${field} is absent`);
  }
  const all = await mountOn(without('gift', 'stamps', 'goal', 'ledger'));
  assert.doesNotMatch(all.text(), /Gifts and stamps|Saving for|Earned and spent/);
  assert.match(all.text(), /New this week/);
  // equip absent: the shelves still draw, nothing reads as worn, the fitting room still opens
  const server = without('equip'), r = await mountOn(server, { tab: 'effects' });
  const body = await server.handle('shop-state'), pv = readPreview(body);
  assert.equal(pv.equip, null);
  assert.equal(sections(readState(body, { rain: false }), { tab: EFFECTS, t, pv }).length, 5);
  assert.ok(locate(readState(body, { rain: false }), 'fx.bubble.heart', pv));
  r.page.view.root.all().find((x) => x.dataset && x.dataset.key === 'tab:effects').onpress(false);
  assert.match(r.text(), /Bubble skins/);
});
