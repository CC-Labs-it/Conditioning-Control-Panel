// The preview ops on the page (fake DOM): an effect buy asks twice and waits for the receipt, Wear / Take off / Switch
// on go through shop-equip, the gift, the stamp, the coupon and Save for this go through their ops, a stray key never
// spends, claims or stamps, and preview Sparkles never reach the host's wallet.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createPreviewServer } from '../mock/preview.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const tick = () => new Promise((r) => setTimeout(r, 0));
const settle = async () => { for (let i = 0; i < 8; i++) await tick(); };
async function rig(init = {}, tweak = () => {}) {
  const server = createPreviewServer(SAMPLE), calls = [], held = [];
  tweak(server);
  let hold = false, T = 1e6, n = 0;
  const host = asHost({ init: { motion: 'full', ...init },
    request: (op, b, idem) => { calls.push({ op, b, idem }); return hold ? new Promise((r) => held.push(() => r(server.handle(op, b, idem)))) : server.handle(op, b, idem); },
    wallet: (sp) => calls.push({ type: 'wallet', sp }), close: (reason) => calls.push({ type: 'close', reason }), wear: (id) => calls.push({ type: 'wear', id }) });
  const doc = fakeDocument(), root = doc.createElement('main'), ls = {};
  const win = { addEventListener: (k, f) => (ls[k] ||= []).push(f), removeEventListener() {}, fire: (k, e) => (ls[k] || []).forEach((f) => f(e)) };
  const page = await mountSheet(host, { root, doc, win, now: () => T, frame: (f) => f(), later() {}, loadIndex: () => null, newIdem: () => `idem-${String(++n).padStart(12, '0')}` });
  const btn = (key) => page.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  const click = (key) => page.view.root.dispatch('click', { target: btn(key), detail: 1 });
  const key = (k = ' ') => { const ev = { key: k, code: k === ' ' ? 'Space' : k, repeat: false, target: doc.activeElement, preventDefault() {} }; win.fire('keydown', ev); win.fire('keyup', ev); };
  return { page, server, calls, btn, click, key, doc, ops: (op) => calls.filter((c) => c.op === op), wait: (ms) => { T += ms; }, hold: (v) => { hold = v; }, release: () => held.splice(0).forEach((f) => f()) };
}

test('an effect buy asks twice, nothing moves until the receipt, then it is owned and worn; no wallet frame in the preview', async () => {
  const r = await rig({ tab: 'effects' });
  r.click('it:fx.bubble.heart'); r.wait(500);
  r.click('act:buy'); r.wait(500);
  assert.ok(r.btn('act:yes') && r.btn('act:cancel'));
  assert.match(r.page.view.room.text(), /Spend 360 Sparkles on this\?/);
  r.hold(true); r.click('act:yes'); await tick();
  assert.equal(r.ops('shop-buy').length, 1);
  assert.equal(r.btn('act:yes').disabled, true);
  assert.match(r.page.view.room.text(), /Ringing it up/);
  assert.doesNotMatch(r.btn('it:fx.bubble.heart').text(), /Wearing|Owned/);
  r.release(); await settle();
  assert.match(r.btn('it:fx.bubble.heart').text(), /Wearing/);
  assert.equal(r.page.ui.sold, 'fx.bubble.heart');
  assert.equal(r.page.shop.sp(), 50);
  assert.equal(r.calls.filter((c) => c.type === 'wallet').length, 0);
  assert.match(r.btn('act:takeoff').text(), /Take off/);
});

test('Wear, Take off and the two-tone switch go through shop-equip; the second colour is worn once Two-tone is owned', async () => {
  const r = await rig({ tab: 'effects' }, (s) => { s.state().sp = 2000; });
  r.click('it:fx.card.ink'); r.wait(500); r.click('act:buy'); r.wait(500); r.click('act:yes'); await settle();
  r.wait(500); r.click('act:takeoff'); await settle();
  assert.deepEqual(r.ops('shop-equip').at(-1).b, { slot: 'card', itemId: null });
  assert.match(r.btn('it:fx.card.plain').text(), /Wearing/);
  r.wait(500); r.click('act:fxwear'); await settle();
  assert.deepEqual(r.ops('shop-equip').at(-1).b, { slot: 'card', itemId: 'fx.card.ink' });
  r.click('it:fx.fog.twotone'); r.wait(500); r.click('act:buy'); r.wait(500); r.click('act:yes'); await settle();
  assert.match(r.btn('it:fx.fog.twotone').text(), /Switched on/);
  r.click('fog2:mint'); await settle();
  assert.deepEqual(r.ops('shop-equip').at(-1).b, { slot: 'fog2', itemId: 'fx.fog.mint' });
  r.wait(500); r.click('act:mixoff'); await settle();
  assert.deepEqual(r.ops('shop-equip').at(-1).b, { slot: 'mix', itemId: null });
  assert.match(r.btn('it:fx.fog.twotone').text(), /Owned/);
});

test('the gift, the stamp and the coupon: one op each, the coupon claims a 120 tag free, Save for this sets the goal', async () => {
  const r = await rig();
  r.wait(500); r.click('gift'); await settle();
  assert.equal(r.ops('shop-gift').length, 1);
  assert.equal(r.page.shop.sp(), 435);
  assert.equal(r.btn('gift').disabled, true);
  assert.match(r.page.view.list.text(), /Monday gift \+25/);
  r.click('stamp'); await settle();
  assert.equal(r.ops('shop-stamp').length, 1);
  assert.equal(r.btn('stamp').disabled, true);
  r.click('stamp'); await settle();
  assert.equal(r.ops('shop-stamp').length, 1, 'a stamped day sends nothing');
  r.server.preview.stamps.couponReady = true;
  await r.page.shop.load(); r.page.pick('fx.card.ink');
  assert.ok(r.btn('act:claim'));
  r.wait(500); r.click('act:claim'); r.wait(500);
  assert.match(r.page.view.room.text(), /Use your free item on this\?/);
  r.click('act:yesclaim'); await settle();
  assert.equal(r.ops('shop-buy').at(-1).b.coupon, true);
  assert.equal(r.page.shop.sp(), 435);
  assert.equal(r.page.pv().equip.card, 'fx.card.ink');
  assert.equal(r.page.pv().stamps.couponUsed, true);
  r.page.pick('fx.bubble.disco'); r.wait(500); r.click('act:save'); await settle();
  assert.deepEqual(r.ops('shop-goal').at(-1).b, { itemId: 'fx.bubble.disco' });
  assert.equal(r.page.pv().goal, 'fx.bubble.disco');
});

test('a stray key never buys, claims, stamps or opens the gift', async () => {
  const r = await rig();
  r.btn('gift').focus(); r.key(' ');               // the window had focus for 0 ms
  r.btn('stamp').focus(); r.key('Enter');
  r.page.pick('fx.bubble.heart'); r.page.act('buy', true, true);
  r.btn('act:yes').focus(); r.key(' ');           // the Yes step is 0 ms old
  await settle();
  assert.equal(r.ops('shop-gift').length + r.ops('shop-stamp').length + r.ops('shop-buy').length, 0);
});

test('a refusal shows one plain line; the gift answered not_ready leaves the wallet alone', async () => {
  const r = await rig({}, (s) => { const h = s.handle; s.handle = async (op, b, i) => (op === 'shop-gift' ? { ok: false, reason: 'not_ready' } : h(op, b, i)); });
  r.wait(500); r.click('gift'); await settle();
  assert.equal(r.page.ui.nudge.text, 'That did not go through.');
  assert.match(r.page.view.nudge.text(), /That did not go through/);
  assert.equal(r.page.shop.sp(), 410);
  r.click('nudge-x');
  assert.equal(r.page.view.nudge.hidden, true);
});

test('a gift that reaches the goal leaves a nudge in the page; View opens the goal', async () => {
  const r = await rig({}, (s) => { s.state().sp = 710; });
  r.wait(500); r.click('gift'); await settle();
  assert.equal(r.page.ui.nudge.text, 'Saved enough for Spiral bubbles.');
  r.click('nudge-view');
  assert.equal(r.page.ui.fx, 'fx.bubble.spiral');
  assert.equal(r.page.ui.tab, 'effects');
  assert.ok(r.btn('act:buy'));
});
