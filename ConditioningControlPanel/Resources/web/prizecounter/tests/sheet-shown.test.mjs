// A bought thing is never invisible (owner 2026-11-17, "did I just buy nothing?"): once worn, the fitting room says
// where it shows and offers a "take me there" the host lists in init.goes; init.worn marks what is worn from the first
// paint, so the Owned tab and the fitting room agree with the profile.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createMockServer } from '../mock/server.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';
import { sheetT } from '../sheet/strings.js';
import { readState } from '../state/shop.js';
import { readPreview } from '../sheet/wire.js';
import { fxRoom, locate, statusOf } from '../sheet/model.js';
import { SHOWN, shownKeys, shownOf } from '../sheet/shown.js';
import { createPreviewServer } from '../mock/preview.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const t = sheetT({});
const fakeWin = () => {
  const ls = {};
  return { addEventListener: (k, f) => (ls[k] ||= []).push(f), removeEventListener() {}, fire: (k, e) => (ls[k] || []).forEach((f) => f(e)) };
};
/** The host's place keys (Services/PrizeCounter/ShownPlaces.cs All). */
const HOST_PLACES = ['profile', 'desk', 'race', 'chess', 'bubbles', 'flash', 'subliminal', 'pinkfilter'];
const owned = { at: '2026-11-17T10:00:00Z', paidSp: 120 };

async function rig(init = {}, sample = SAMPLE) {
  const server = createMockServer(sample), calls = [];
  let T = 0, wornHear = null;
  const host = asHost({
    init: { sp: 1850, motion: 'full', tab: 'profile', wears: ['desk', 'chess', 'profile'], ...init },
    request: (op, b, idem) => server.handle(op, b, idem),
    wallet() {}, close: (reason) => calls.push({ type: 'close', reason }),
    wear: (id) => calls.push({ type: 'wear', id }), go: (place, id) => calls.push({ type: 'go', place, id }),
    onWorn: (f) => { wornHear = f; }, log() {},
  });
  const doc = fakeDocument(), win = fakeWin(), root = doc.createElement('main');
  let n = 0;
  const page = await mountSheet(host, { root, doc, win, now: () => T, frame: (f) => f(), later() {}, loadIndex: () => null,
    newIdem: () => `idem-${String(++n).padStart(12, '0')}` });
  const all = () => page.view.root.all();
  const btn = (key) => all().find((x) => x.dataset && x.dataset.key === key) || null;
  const click = (key) => page.view.root.dispatch('click', { target: btn(key), detail: 1 });
  const shown = () => all().find((x) => String(x.className || '').split(' ').includes('d-shown')) || null;
  return { page, calls, btn, click, shown, wait: (ms) => { T += ms; }, worn: (id, ok = true) => wornHear && wornHear({ id, ok }) };
}

const textOf = (n) => (n ? n.all().map((x) => x.textContent || '').join(' ') : '');

test('every family with a home names one of the host places; Rain and Back Room prizes have none', () => {
  for (const [fam, k] of Object.entries(SHOWN)) {
    assert.ok(HOST_PLACES.includes(k.go), `${fam} -> ${k.go}`);
    assert.notEqual(t(k.line), k.line, `${fam} line has English`);
    assert.notEqual(t(k.btn), k.btn, `${fam} button has English`);
  }
  const at = (id, fx = null, tab = 'x') => ({ id, fx, tab });
  assert.equal(shownKeys(at('profile.frame.lace')).go, 'profile');
  assert.equal(shownKeys(at('prime.frame.halo')).go, 'profile');
  assert.equal(shownKeys(at('profile.avatar.emi-pixel')).go, 'profile');
  assert.equal(shownKeys(at('desk.toy.rubber-duck')).go, 'desk');
  assert.equal(shownKeys(at('kart.body.zoom')).go, 'race');
  assert.equal(shownKeys(at('chess.board.arcade')).go, 'chess');
  assert.equal(shownKeys(at('fx.bubble.heart', 'bubble')).go, 'bubbles');
  assert.equal(shownKeys(at('fx.card.ink', 'card')).go, 'flash');
  assert.equal(shownKeys(at('fx.behind.whisper', 'behind')).go, 'flash');
  assert.equal(shownKeys(at('fx.word.chalk', 'word')).go, 'subliminal');
  assert.equal(shownKeys(at('fx.fog.twotone', 'fog')).go, 'pinkfilter');
  assert.equal(shownKeys(at('rain.dress.sun')), null);
  assert.equal(shownKeys(at('flashes_v2', null, 'backroom')), null);
});

test('the slip: only once owned and worn (a kart once owned), the button only for a place in init.goes', () => {
  const frame = { id: 'profile.frame.lace', fx: null, tab: 'profile', row: { id: 'profile.frame.lace', owned } };
  assert.equal(shownOf({ ...frame, row: { id: frame.id, owned: null } }, t, { on: true, goes: ['profile'] }), null);
  assert.equal(shownOf(frame, t, { on: false, goes: ['profile'] }), null);
  assert.deepEqual(shownOf(frame, t, { on: true, goes: ['profile'] }),
    { text: 'On your profile, the friends list and lobby rows.', go: 'profile', label: 'See it on your profile' });
  assert.deepEqual(shownOf(frame, t, { on: true, goes: [] }), { text: 'On your profile, the friends list and lobby rows.' });
  assert.deepEqual(shownOf(frame, t, { on: true }), { text: 'On your profile, the friends list and lobby rows.' });
  const kart = { id: 'kart.body.zoom', fx: null, tab: 'kart', row: { id: 'kart.body.zoom', owned } };
  assert.equal(shownOf(kart, t, { on: false, goes: ['race'] }).go, 'race');
});

test('a profile frame: Buy, then Wear is the main button, then the sheet says where it shows and takes you there', async () => {
  const r = await rig({ goes: ['profile', 'desk'] });
  r.click('it:profile.frame.lace');
  r.wait(500); r.click('act:buy');
  r.wait(500); r.click('act:yes');
  await new Promise((res) => setTimeout(res, 0));
  assert.ok(r.page.shop.card().face === 'owned');
  assert.ok(r.btn('act:wear'), 'Wear after the buy');
  assert.equal(r.btn('act:wear').dataset.main, '1');
  assert.equal(r.shown(), null, 'no slip before it is worn');
  r.wait(500); r.click('act:wear');
  assert.deepEqual(r.calls.filter((c) => c.type === 'wear'), [{ type: 'wear', id: 'profile.frame.lace' }]);
  assert.match(textOf(r.shown()), /On your profile, the friends list and lobby rows\./);
  assert.equal(r.btn('act:go').textContent, 'See it on your profile');
  r.wait(500); r.click('act:go');
  assert.deepEqual(r.calls.filter((c) => c.type === 'go'), [{ type: 'go', place: 'profile', id: 'profile.frame.lace' }]);
});

test('a refused Wear takes the slip away again', async () => {
  const sample = structuredClone(SAMPLE);
  sample.tabs.find((x) => x.id === 'profile').shelves[0].rows.find((x) => x.id === 'profile.frame.lace').owned = owned;
  const r = await rig({ goes: ['profile'] }, sample);
  r.click('it:profile.frame.lace');
  r.wait(500); r.click('act:wear');
  assert.ok(r.shown());
  r.worn('profile.frame.lace', false);
  assert.equal(r.shown(), null);
});

test('init.worn: an owned frame worn elsewhere reads Wearing on the Owned tab and in the room, with its slip', async () => {
  const sample = structuredClone(SAMPLE);
  sample.tabs.find((x) => x.id === 'profile').shelves[0].rows.find((x) => x.id === 'profile.frame.gilt').owned = owned;
  const r = await rig({ worn: ['profile.frame.gilt'], goes: ['profile'] }, sample);
  const st = r.page.shop.state(), pv = r.page.pv();
  assert.equal(statusOf(locate(st, 'profile.frame.gilt', pv), { sp: 1850, wearing: r.page.ui.wearing, pv }).kind, 'wearing');
  r.click('tab:owned');
  r.click('it:profile.frame.gilt');
  assert.equal(r.btn('act:wear'), null, 'already worn: no Wear');
  assert.ok(r.btn('act:go'));
  // the slip slides in once; a repaint (another tab) draws it in place, no second entrance
  assert.match(r.shown().className, /\bfresh\b/);
  r.click('tab:accessories');
  assert.ok(r.shown());
  assert.doesNotMatch(r.shown().className, /\bfresh\b/);
});

test('no init.goes (a game door, the web): the line shows, no button', async () => {
  const sample = structuredClone(SAMPLE);
  sample.tabs.find((x) => x.id === 'profile').shelves[0].rows.find((x) => x.id === 'profile.frame.gilt').owned = owned;
  const r = await rig({ worn: ['profile.frame.gilt'] }, sample);
  r.click('it:profile.frame.gilt');
  assert.ok(r.shown());
  assert.equal(r.btn('act:go'), null);
});

test('a bubble skin: worn in the preview, the room says Bubble Pop and goes to its Studio page', async () => {
  const server = createPreviewServer(SAMPLE);
  await server.handle('shop-state');
  const buy = await server.handle('shop-buy', { itemId: 'fx.bubble.heart', catalogVersion: SAMPLE.catalogVersion, week: SAMPLE.week }, 'idem-heart-0000001');
  assert.equal(buy.ok, true);
  const body = await server.handle('shop-equip', { slot: 'bubble', itemId: 'fx.bubble.heart' });
  const st = readState(body, { rain: false }), pv = readPreview(body);
  const room = fxRoom(locate(st, 'fx.bubble.heart', pv), { sp: 50, t, pv, goes: ['bubbles'] });
  assert.deepEqual(room.shown, { text: 'On every Bubble Pop bubble.', go: 'bubbles', label: 'Open Bubble Pop' });
  assert.equal(fxRoom(locate(st, 'fx.bubble.marble', pv), { sp: 50, t, pv, goes: ['bubbles'] }).shown, null, 'not owned');
  assert.equal(fxRoom(locate(st, 'fx.bubble.soap', pv), { sp: 50, t, pv, goes: ['bubbles'] }).shown, null, 'the free default says nothing');
});
