// One loaded page, many opens (CONTRACT.md "Reuse"): the WPF host's { type:'reset' } drops the open that ended, the
// page mounts again and says ready, and the next init starts it over cleanly (Featured, nothing picked, no query, the
// heading focused), with no handler counted twice and nothing of the old mount still listening.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createMockServer } from '../mock/server.js';
import { createHost } from '../host.js';
import { runSheet } from '../sheet/reuse.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const tick = async (n = 6) => { for (let i = 0; i < n; i++) await new Promise((r) => setTimeout(r, 0)); };

/** A WPF webview window whose host answers every request from the mock server; counts live window listeners. */
function rig() {
  const server = createMockServer(SAMPLE), posted = [], subs = [], ls = {};
  const answer = (m) => { if (m.type === 'request') Promise.resolve(server.handle(m.op, m.body, m.idem)).then((body) => deliver({ type: 'reply', reqId: m.reqId, body })); };
  const win = {
    chrome: { webview: { postMessage: (m) => { posted.push(m); if (auto) answer(m); }, addEventListener: (t, f) => subs.push(f) } },
    addEventListener: (k, f) => (ls[k] ||= new Set()).add(f), removeEventListener: (k, f) => ls[k] && ls[k].delete(f),
    fire: (k, e) => [...(ls[k] || [])].forEach((f) => f(e)),
  };
  let auto = true;
  const deliver = (m) => subs.forEach((f) => f({ data: m }));
  const doc = fakeDocument(), root = doc.createElement('main');
  return { win, doc, root, posted, deliver, listeners: (k) => (ls[k] ? ls[k].size : 0), hold: (v) => { auto = !v; },
    readies: () => posted.filter((m) => m.type === 'ready').length };
}

const INIT = { type: 'init', protocol: 1, sp: 1850, motion: 'full', sheet: true };

test('a reset drops the open, mounts again and says ready; the next init starts over at Featured with nothing picked', async () => {
  const r = rig();
  const host = createHost(r.win, { timeoutMs: 1000 });
  const pages = [];
  const ctl = runSheet(host, { root: r.root, doc: r.doc, win: r.win, frame: (f) => f(), later() {}, loadIndex: () => null }, async (h, o) => {
    const { mountSheet } = await import('../sheet/main.js');
    const p = await mountSheet(h, o); pages.push(p); return p;
  });
  ctl.start();
  await tick();
  assert.equal(r.readies(), 1);
  r.deliver(INIT);
  await tick(20);
  const first = pages[0];
  assert.ok(first && r.root.children.length === 1, 'the first open mounted');
  const homeTab = first.ui.tab;
  first.ui.query = 'hat';
  first.pick('desk.hat.party');
  assert.equal(first.shop.card().itemId, 'desk.hat.party');
  assert.equal(r.listeners('keydown'), 1);

  r.deliver({ type: 'reset' });
  await tick();
  assert.equal(r.root.children.length, 0, 'between opens the page is empty');
  assert.equal(r.listeners('keydown'), 0, 'the old mount stopped listening');
  assert.equal(r.readies(), 2, 'it said ready again');

  r.deliver({ ...INIT, motion: 'less' });
  await tick(20);
  const second = pages[1];
  assert.ok(second && second !== first);
  assert.equal(r.root.children.length, 1);
  assert.equal(second.ui.tab, homeTab, 'back on the first tab');
  assert.equal(second.ui.query, '');
  assert.equal(second.shop.card(), null, 'nothing picked');
  assert.equal(r.doc.activeElement, second.view.title, 'only the heading has focus');
  assert.equal(r.root.dataset.motion, 'less', 'the new init counts');
  assert.equal(r.listeners('keydown'), 1, 'one live key handler, never two');
  assert.equal(ctl.mounts, 2);
});

test('answers and listeners of the ended open are dropped: a late reply or worn frame never reaches the new mount', async () => {
  const r = rig();
  const host = createHost(r.win, { timeoutMs: 1000 });
  let late = null;
  const p = (r.hold(true), host.request('shop-state'));
  p.then((b) => { late = b; });
  const worn = [];
  host.onWorn((m) => worn.push(m));
  host.ready();
  r.deliver(INIT);
  const req = r.posted.find((m) => m.type === 'request');
  let resets = 0;
  host.onReset(() => resets++);
  r.deliver({ type: 'reset' });
  r.deliver({ type: 'reply', reqId: req.reqId, body: { ok: true } });
  r.deliver({ type: 'worn', id: 'x', ok: true });
  await tick();
  assert.equal(late, null, 'the old answer never resolves into a gone mount');
  assert.deepEqual(worn, [], 'its worn listener is gone');
  assert.equal(resets, 1);
  const again = host.ready();
  assert.equal(r.posted.filter((m) => m.type === 'ready').length, 2, 'ready() asks again after a reset');
  r.deliver({ ...INIT, tab: 'kart' });
  assert.equal((await again).tab, 'kart');
});

test('a reset before the first init abandons that mount: only the new one ever mounts', async () => {
  const r = rig();
  const host = createHost(r.win, { timeoutMs: 1000 });
  const { mountSheet } = await import('../sheet/main.js');
  let mounted = 0;
  const ctl = runSheet(host, { root: r.root, doc: r.doc, win: r.win, frame: (f) => f(), later() {}, loadIndex: () => null },
    (h, o) => mountSheet(h, o).then((pg) => { mounted++; return pg; }));
  ctl.start();
  await tick();
  r.deliver({ type: 'reset' });
  await tick();
  r.deliver(INIT);
  await tick(20);
  assert.equal(mounted, 1);
  assert.equal(r.root.children.length, 1);
  assert.equal(r.listeners('keydown'), 1);
});

test('dispose releases the window listeners, the juice observer and the sound, and a late paint does nothing', async () => {
  const r = rig();
  const host = createHost(r.win, { timeoutMs: 1000 });
  const { mountSheet } = await import('../sheet/main.js');
  const timers = [];
  const ready = mountSheet(host, { root: r.root, doc: r.doc, win: r.win, frame: (f) => f(), later: (f) => timers.push(f), loadIndex: () => null });
  r.deliver(INIT);
  const page = await ready;
  for (const k of ['keydown', 'keyup', 'focus', 'blur']) assert.equal(r.listeners(k), 1, k);
  page.dispose();
  for (const k of ['keydown', 'keyup', 'focus', 'blur']) assert.equal(r.listeners(k), 0, k);
  assert.equal(r.root.children.length, 0);
  timers.forEach((f) => f());   // late nudges and stamps
  assert.equal(r.root.children.length, 0, 'nothing paints back in');
  page.dispose();   // twice is harmless
});
