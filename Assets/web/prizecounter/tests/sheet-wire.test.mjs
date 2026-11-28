// The preview wire (BRIEF "The preview wire"): every field optional, the effect shelves, places, the fitting room for
// an effect row, the Featured page model, and the mock preview server's ops.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readState } from '../state/shop.js';
import { sheetT } from '../sheet/strings.js';
import { readPreview, readPlaces, placesOf, isHere, placeMark, awayLine, couponOn, platformOf } from '../sheet/wire.js';
import { sheetTabs, sections, statusOf, fxRoom, locate, frontModel, badgeCount, labelOf, ledgerText, FEATURED, EFFECTS, OWNED } from '../sheet/model.js';
import { createPreviewServer, previewSample } from '../mock/preview.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const t = sheetT({});
const ids = (m) => m.actions.map((a) => a.id);
async function preview() {
  const server = createPreviewServer(SAMPLE);
  const body = await server.handle('shop-state');
  return { server, body, st: readState(body, { rain: false }), pv: readPreview(body) };
}

test('the real server body reads as no preview: every part stays hidden and the tabs are slice 1', () => {
  const pv = readPreview(SAMPLE);
  assert.equal(pv.preview, false); assert.equal(pv.front, false);
  assert.deepEqual([pv.fx, pv.fresh, pv.equip, pv.gift, pv.stamps, pv.goal, pv.ledger], [[], [], null, null, null, undefined, null]);
  assert.deepEqual(placesOf(pv, 'desk.hat.party'), ['desktop', 'web', 'phone']);
  assert.deepEqual(sheetTabs(readState(SAMPLE, { rain: false }).tabs.map((x) => x.id), pv), ['kart', 'chess', 'accessories', 'prime', 'backroom', OWNED]);
  assert.equal(readPreview(null).front, false);
  assert.equal(readPreview({ ok: false, preview: true }).preview, false);
});

test('the preview body: Featured and Screen effects first, effect rows by slot, places, equip, stamps', async () => {
  const { st, pv } = await preview();
  assert.equal(pv.preview, true);
  assert.deepEqual(sheetTabs(st.tabs.map((x) => x.id), pv), [FEATURED, 'kart', 'chess', EFFECTS, 'accessories', 'prime', 'backroom', OWNED]);
  const fx = sections(st, { tab: EFFECTS, t, pv });
  assert.deepEqual(fx.map((s) => s.title), ['Bubble skins', 'Card frames', 'Words behind the card', 'Two-tone fog', 'Word styles']);
  assert.deepEqual(fx[0].places, ['desktop']);
  assert.equal(fx[0].planks[0].length, 10);
  assert.equal(pv.equip.bubble, 'fx.bubble.soap');
  assert.deepEqual(pv.stamps.stamped, [1, 2, 4]);
  assert.equal(pv.goal, 'fx.bubble.spiral');
  assert.equal(labelOf(locate(st, 'fx.bubble.heart', pv), t), 'Heart bubbles');
  assert.equal(locate(st, 'fx.bubble.rose', pv), null, 'the tint skins are gone');
  assert.equal(ledgerText(st, pv, t, 'desk.toy.rubber-duck'), 'The Suggestible Duck');
  assert.equal(ledgerText(st, pv, t, 'Won at chess'), 'Won at chess');
});

test('new chess boards and Emi show desktop availability without restricting existing chess items', async () => {
  const { st, pv } = await preview();
  for (const id of ['chess.board.observatory','chess.board.conservatory','chess.board.theatre','chess.board.arcade','chess.companion.emi']) {
    assert.ok(locate(st,id,pv), id + ' appears in the catalogue');
    assert.deepEqual(placesOf(pv,id), ['desktop']);
    assert.equal(isHere(placesOf(pv,id),'web'), false);
    assert.equal(isHere(placesOf(pv,id),'phone'), false);
    assert.equal(placeMark(placesOf(pv,id),t).text, 'Desktop app only');
  }
  assert.deepEqual(placesOf(pv,'chess.banner.crest'), ['desktop','web','phone']);
});

test('places: absent = everywhere, the mark reads plainly, the away line says where it does show', () => {
  assert.deepEqual(readPlaces(undefined), ['desktop', 'web', 'phone']);
  assert.deepEqual(readPlaces(['phone', 'desktop', 'moon']), ['desktop', 'phone']);
  assert.equal(platformOf({}), 'desktop'); assert.equal(platformOf({ platform: 'web' }), 'web');
  assert.equal(placeMark(['desktop'], t).text, 'Desktop app only');
  assert.equal(placeMark(['desktop', 'web', 'phone'], t).text, 'Everywhere');
  assert.equal(placeMark(['desktop', 'web'], t).text, 'Desktop app + Website');
  assert.equal(awayLine(['desktop'], 'web', t), 'Shows in the desktop app, not on the website.');
  assert.equal(awayLine(['desktop', 'web'], 'phone', t), 'Shows in the desktop app and on the website, not on the phone.');
  assert.equal(isHere(['desktop'], 'phone'), false);
});

test('an effect row wears one status by the equip, and its fitting room asks twice', async () => {
  const { st, pv } = await preview();
  const ctx = { sp: 410, tier: 0, pv };
  assert.equal(statusOf(locate(st, 'fx.bubble.soap', pv), ctx).kind, 'wearing');
  assert.equal(statusOf(locate(st, 'fx.bubble.gum', pv), ctx).kind, 'todo');
  assert.deepEqual(statusOf(locate(st, 'fx.bubble.spiral', pv), ctx), { kind: 'price', price: 730, tier: 't3', short: true });
  const heart = locate(st, 'fx.bubble.heart', pv);
  assert.deepEqual(ids(fxRoom(heart, { sp: 410, t, pv })), ['buy', 'back']);
  assert.equal(fxRoom(heart, { sp: 410, t, pv }).lines[0].text, 'Leaves you 50');
  const ask = fxRoom(heart, { sp: 410, t, pv, asking: true, away: true });
  assert.deepEqual(ask.actions.map((a) => a.label), ['Yes, buy for 360', 'Cancel']);
  assert.equal(ask.ask, 'Spend 360 Sparkles on this? It shows in the desktop app.');
  assert.ok(fxRoom(heart, { sp: 410, t, pv, asking: true, pending: true }).actions.every((a) => a.disabled));
  assert.deepEqual(ids(fxRoom(locate(st, 'fx.bubble.spiral', pv), { sp: 410, t, pv })), ['saving', 'back']);
  assert.deepEqual(ids(fxRoom(locate(st, 'fx.bubble.disco', pv), { sp: 410, t, pv })), ['save', 'back']);
  assert.deepEqual(ids(fxRoom(locate(st, 'fx.bubble.soap', pv), { sp: 410, t, pv })), ['default', 'back']);
  assert.deepEqual(ids(fxRoom(locate(st, 'fx.bubble.gum', pv), { sp: 410, t, pv })), ['todo', 'back']);
  assert.equal(fxRoom(locate(st, 'fx.card.ink', pv), { sp: 410, t, pv, away: true }).actions[0].label, 'Buy');
});

test('the Featured page: the spotlight works here first, gift and stamps wait, You can buy now fits the wallet', async () => {
  const { st, pv } = await preview();
  const desk = frontModel(st, { sp: 410, tier: 0, pv, platform: 'desktop' });
  assert.equal(desk.spot.id, 'fx.bubble.marble');
  assert.deepEqual(desk.also.map((e) => e.id), ['fx.bubble.heart', 'fx.bubble.spiral', 'fx.behind.whisper', 'kart.outfit.ringmaster']);
  assert.equal(desk.hostNote, false);
  assert.ok(desk.can.length > 0 && desk.can.length <= 6 && desk.can.every((e) => e.row.priceSp <= 410 && !e.row.owned));
  assert.deepEqual([desk.goal.have, desk.goal.price, desk.goal.left], [410, 730, 320]);
  const web = frontModel(st, { sp: 410, tier: 0, pv, platform: 'web' });
  assert.equal(web.spot.id, 'kart.outfit.ringmaster');
  assert.equal(web.hostNote, true);
  assert.equal(frontModel(st, { sp: 410, pv, platform: 'web', hereOnly: true }).also.length, 0);
  assert.equal(badgeCount(st, pv, 410), 2);   // the gift and today's stamp
  assert.equal(badgeCount(st, pv, 800), 3);   // and the goal is reached
  const bare = frontModel(readState(SAMPLE), { sp: 0, pv: readPreview(SAMPLE) });
  assert.deepEqual([bare.spot, bare.gift, bare.stamps, bare.goal, bare.ledger], [null, null, null, undefined, null]);
});

test('the coupon makes any unowned 120 tag free, never a Prime row', async () => {
  const { st, pv } = await preview();
  const ready = { ...pv, stamps: { ...pv.stamps, couponReady: true } };
  assert.equal(couponOn(ready, locate(st, 'fx.card.ink', pv).row), true);
  assert.equal(couponOn(ready, locate(st, 'fx.card.lace', pv).row), false);
  assert.equal(couponOn(pv, locate(st, 'fx.card.ink', pv).row), false);
  assert.equal(statusOf(locate(st, 'desk.hat.party', pv), { sp: 0, pv: ready }).kind, 'price');
  assert.equal(statusOf(locate(st, 'profile.frame.lace', pv), { sp: 0, pv: ready }).kind, 'free');
  assert.deepEqual(ids(fxRoom(locate(st, 'fx.card.ink', pv), { sp: 0, t, pv: ready })), ['claim', 'back']);
  assert.equal(fxRoom(locate(st, 'fx.card.ink', pv), { sp: 0, t, pv: ready, asking: true }).actions[0].id, 'yesclaim');
});

test('the mock preview ops: equip refuses what you do not own, one gift a week, one stamp a day, the coupon at 6', async () => {
  const { server } = await preview();
  assert.equal((await server.handle('shop-equip', { slot: 'bubble', itemId: 'fx.bubble.heart' })).reason, 'not_owned');
  assert.equal(readPreview(await server.handle('shop-equip', { slot: 'fog2', itemId: 'fx.fog.mint' })).equip.fog2, 'fx.fog.mint');
  const g = await server.handle('shop-gift', {});
  assert.equal(g.sp, 435); assert.equal(g.gift.ready, false); assert.deepEqual([g.ledger[0].code, g.ledger[0].n, typeof g.ledger[0].at], ['gift', 25, 'string']);
  assert.equal((await server.handle('shop-gift', {})).ok, false);
  assert.deepEqual((await server.handle('shop-stamp', {})).stamps.stamped, [1, 2, 4, 5]);
  assert.equal((await server.handle('shop-stamp', {})).ok, false);
  server.nextDay(); await server.handle('shop-stamp', {});
  server.nextDay();
  const six = await server.handle('shop-stamp', {});
  assert.equal(six.stamps.couponReady, true);
  const b = await server.handle('shop-buy', { itemId: 'fx.card.ink', catalogVersion: SAMPLE.catalogVersion, week: SAMPLE.week, coupon: true }, 'idem-coupon-000001');
  assert.equal(b.ok, true); assert.equal(b.paidSp, 0); assert.equal(b.sp, 435);
  const after = readPreview(await server.handle('shop-state'));
  assert.equal(after.stamps.couponUsed, true); assert.equal(after.equip.card, 'fx.card.ink');
  assert.equal(readPreview(await server.handle('shop-goal', { itemId: null })).goal, null);
  const heart = await server.handle('shop-buy', { itemId: 'fx.bubble.heart', catalogVersion: SAMPLE.catalogVersion, week: SAMPLE.week }, 'idem-heart-0000001');
  assert.equal(heart.sp, 75);
  const s = await server.handle('shop-state');
  assert.equal(readPreview(s).equip.bubble, 'fx.bubble.heart');
  assert.deepEqual(readPreview(s).ledger[0], { code: 'buy', n: -360, id: 'fx.bubble.heart' });
  assert.equal(previewSample(SAMPLE).tabs[0].id, 'effects');
});
