// The 2D counter sheet on the fake DOM: buy asks twice and waits for the receipt, stray keys buy nothing, sheet mode
// drops the wallet line, and the page speaks today's frames (close { reason: 'back' } from Back and the Esc ladder).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createMockServer } from '../mock/server.js';
import { asHost, createHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const tick = () => new Promise((r) => setTimeout(r, 0));
const fakeWin = () => {
  const ls = {};
  return { addEventListener: (k, f) => (ls[k] ||= []).push(f), removeEventListener() {}, fire: (k, e) => (ls[k] || []).forEach((f) => f(e)) };
};

async function rig(init = {}) {
  const server = createMockServer(SAMPLE), calls = [], held = [];
  let hold = false, n = 0, T = 0;
  const host = asHost({
    init: { sp: 1850, motion: 'full', tab: 'desk', ...init },
    request: (op, b, idem) => { calls.push({ type: 'request', op, idem }); return hold ? new Promise((r) => held.push(() => r(server.handle(op, b, idem)))) : server.handle(op, b, idem); },
    wallet: (sp) => calls.push({ type: 'wallet', sp }), close: (reason) => calls.push({ type: 'close', reason }),
    wear: (id) => calls.push({ type: 'wear', id }), log() {},
  });
  const doc = fakeDocument(), win = fakeWin(), root = doc.createElement('main');
  const page = await mountSheet(host, { root, doc, win, now: () => T, frame: (f) => f(), later() {}, loadIndex: () => null,
    newIdem: () => `idem-${String(++n).padStart(12, '0')}` });
  const btn = (key) => page.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  const click = (key, detail = 1) => page.view.root.dispatch('click', { target: btn(key), detail });
  const key = (k = ' ', extra = {}) => {
    const ev = { key: k, code: k === ' ' ? 'Space' : k, repeat: false, target: doc.activeElement, ...extra };
    win.fire('keydown', { preventDefault() {}, ...ev });
    if (!extra.noUp) win.fire('keyup', { preventDefault() {}, ...ev });
  };
  return { page, doc, win, calls, btn, click, key, server, buys: () => calls.filter((c) => c.op === 'shop-buy'),
    wait: (ms) => { T += ms; }, hold: (v) => { hold = v; }, release: () => held.splice(0).forEach((f) => f()) };
}

test('opens on the heading with nothing picked; the tabs are the state shelves plus Owned', async () => {
  const r = await rig();
  assert.equal(r.doc.activeElement, r.page.view.title);
  assert.equal(r.page.shop.card(), null);
  assert.deepEqual(r.page.view.root.all().filter((x) => x.dataset.key && x.dataset.key.startsWith('tab:')).map((x) => x.dataset.key),
    ['tab:kart', 'tab:chess', 'tab:profile', 'tab:desk', 'tab:prime', 'tab:backroom', 'tab:owned']);
  r.click('tab:owned');
  assert.ok(r.btn('it:jackpot_remix'));
  assert.equal(r.btn('it:desk.hat.party'), null);
});

test('buy needs two presses and nothing moves until the receipt', async () => {
  const r = await rig();
  r.click('it:desk.hat.party');
  assert.equal(r.page.shop.card().itemId, 'desk.hat.party');
  r.click('act:buy');                              // too fresh: the step has not been up 400 ms
  assert.ok(r.btn('act:buy'));
  r.wait(500); r.click('act:buy');
  assert.ok(r.btn('act:yes') && r.btn('act:cancel'));
  assert.equal(r.buys().length, 0);
  r.click('act:yes');                              // the Yes step is new: a click at once does nothing
  assert.equal(r.buys().length, 0);
  r.hold(true); r.wait(500); r.click('act:yes');
  await tick();
  assert.equal(r.buys().length, 1);
  assert.equal(r.btn('act:yes').disabled, true);
  assert.match(r.page.view.room.text(), /Ringing it up/);
  assert.equal(r.calls.filter((c) => c.type === 'wallet').length, 0);
  assert.doesNotMatch(r.btn('it:desk.hat.party').text(), /Owned/);
  r.release(); await tick(); await tick();
  assert.deepEqual(r.calls.filter((c) => c.type === 'wallet'), [{ type: 'wallet', sp: 1490 }]);
  assert.match(r.btn('it:desk.hat.party').text(), /Owned/);
  assert.ok(r.btn('act:wear'));
  r.wait(500); r.click('act:wear');
  assert.deepEqual(r.calls.at(-1), { type: 'wear', id: 'desk.hat.party' });
});

test('a stray key never buys: synthetic clicks, repeats, a press too soon after focus or a new step', async () => {
  const r = await rig();
  r.click('it:desk.hat.party');
  r.btn('act:buy').focus(); r.key(' ');           // the window has had focus for 0 ms
  assert.ok(r.btn('act:buy'));
  r.wait(1000);
  r.key(' ', { repeat: true });                   // a held key's repeat
  r.click('act:buy', 0);                          // a key's synthetic click
  assert.ok(r.btn('act:buy'));
  r.key('Enter');                                 // a deliberate press does open the Yes step
  assert.ok(r.btn('act:yes'));
  assert.notEqual(r.doc.activeElement, r.btn('act:yes'));   // focus goes to the fitting room heading, never the action
  r.btn('act:yes').focus(); r.key(' ');           // the Yes step is 0 ms old
  r.key(' ', { noUp: true }); r.win.fire('blur', {}); r.wait(1000); r.win.fire('keyup', { key: ' ', code: 'Space', preventDefault() {} });
  assert.equal(r.buys().length, 0);
  r.win.fire('focus', {}); r.wait(700); r.key(' ');   // focus back for a while, then a deliberate press
  await tick();
  assert.equal(r.buys().length, 1);
});

test('sheet: true hides the wallet line and slides in; standalone draws it', async () => {
  const sheet = await rig({ sheet: true });
  assert.equal(sheet.page.view.wallet.hidden, true);
  assert.ok(sheet.page.view.root.classList.contains('from-wallet') && sheet.page.view.root.classList.contains('open'));
  const alone = await rig();
  assert.equal(alone.page.view.wallet.hidden, false);
  assert.match(alone.page.view.wallet.text(), /1850/);
  assert.equal(alone.page.view.wallet.getAttribute('aria-label'), '1850 Sparkles');
});

test('Esc ladder: the Yes step, the fitting room, then close { reason: back }; Back closes too', async () => {
  const r = await rig();
  r.click('it:desk.hat.party'); r.wait(500); r.click('act:buy');
  r.key('Escape'); assert.ok(r.btn('act:buy'));
  r.key('Escape'); assert.equal(r.page.shop.card(), null);
  assert.equal(r.calls.filter((c) => c.type === 'close').length, 0);
  r.key('Escape'); r.click('close');
  assert.deepEqual(r.calls.filter((c) => c.type === 'close'), [{ type: 'close', reason: 'back' }, { type: 'close', reason: 'back' }]);
});

test('standalone frames: ready, the shop-state request, and close over chrome.webview', async () => {
  const sent = [], server = createMockServer(SAMPLE);
  let hear = null;
  const win = fakeWin();
  win.chrome = { webview: { addEventListener: (k, f) => { hear = f; }, postMessage: (m) => {
    sent.push(m);
    if (m.type === 'ready') setTimeout(() => hear({ data: { type: 'init', sp: 1850, sheet: true } }));
    if (m.type === 'request') Promise.resolve(server.handle(m.op, m.body, m.idem)).then((body) => hear({ data: { type: 'reply', reqId: m.reqId, body } }));
  } } };
  const doc = fakeDocument(), root = doc.createElement('main');
  const page = await mountSheet(createHost(win), { root, doc, win: fakeWin(), frame: (f) => f(), loadIndex: () => null });
  assert.equal(page.shop.status(), 'ready');
  page.view.close.onpress();
  assert.deepEqual(sent.map((m) => m.type), ['ready', 'request', 'close']);
  assert.equal(sent[1].op, 'shop-state');
  assert.deepEqual(sent[2], { type: 'close', reason: 'back' });
});

test('signed out shows the sign-in button, offline says so', async () => {
  const doc = fakeDocument(), root = doc.createElement('main'), asked = [];
  await mountSheet(asHost({ init: {}, request: () => ({ ok: false, reason: 'signed_out' }), signIn: () => asked.push(1) }), { root, doc, win: fakeWin(), loadIndex: () => null });
  const b = root.all().find((x) => x.dataset.key === 'signin');
  assert.ok(b); b.onpress(); assert.equal(asked.length, 1);
  const doc2 = fakeDocument(), root2 = doc2.createElement('main');
  await mountSheet(asHost({ init: {}, request: () => ({ ok: false, reason: 'offline' }) }), { root: root2, doc: doc2, win: fakeWin(), loadIndex: () => null });
  assert.match(root2.text(), /did not answer/);
});
