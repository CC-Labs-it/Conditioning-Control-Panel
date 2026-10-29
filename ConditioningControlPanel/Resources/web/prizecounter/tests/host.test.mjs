import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHost, asHost } from '../host.js';

/** A window with a WPF webview: `posted` is what the page sent, `deliver(m)` is a host frame. */
function fakeWebview() {
  const posted = [], subs = [];
  const win = { chrome: { webview: { postMessage: (m) => posted.push(m), addEventListener: (t, f) => subs.push(f) } }, addEventListener() {} };
  return { win, posted, deliver: (m) => subs.forEach((f) => f({ data: m })) };
}

test('standalone over the webview: ready, init, a correlated request, wallet and close frames', async () => {
  const w = fakeWebview();
  const host = createHost(w.win, { timeoutMs: 30 });
  const ready = host.ready();
  assert.deepEqual(w.posted[0], { type: 'ready', protocol: 1 });
  w.deliver({ type: 'init', sp: 5, tab: 'kart' });
  assert.equal((await ready).tab, 'kart');
  const p = host.request('shop-buy', { itemId: 'a' }, 'idem-1');
  const frame = w.posted[1];
  assert.deepEqual([frame.type, frame.op, frame.idem, frame.body.itemId], ['request', 'shop-buy', 'idem-1', 'a']);
  w.deliver({ type: 'reply', reqId: 'someone-else', body: { ok: true, wrong: 1 } });
  w.deliver({ type: 'reply', reqId: frame.reqId, body: { ok: true, itemId: 'a' } });
  assert.deepEqual(await p, { ok: true, itemId: 'a' });
  assert.deepEqual(await host.request('shop-state'), { ok: false, reason: 'offline' }, 'no reply: offline after the timeout');
  host.wallet(12); host.close('back');
  assert.deepEqual(w.posted.slice(-2), [{ type: 'wallet', sp: 12 }, { type: 'close', reason: 'back' }]);
});

test('standalone with no host answers offline at once; a mounted object never rejects', async () => {
  const lone = createHost({ addEventListener() {} });
  assert.equal(lone.hosted, false);
  assert.deepEqual(await lone.ready(), {});
  assert.deepEqual(await lone.request('shop-state'), { ok: false, reason: 'offline' });
  const seen = [];
  const host = asHost({ init: { tab: 'desk' }, request: () => { throw new Error('boom'); }, wallet: (sp) => seen.push(sp) });
  assert.equal((await host.ready()).tab, 'desk');
  assert.deepEqual(await host.request('shop-buy', {}, 'i'), { ok: false, reason: 'offline' });
  host.wallet(7); host.close(); host.log('x');
  assert.deepEqual(seen, [7]);
});
