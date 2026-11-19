// The shop frame lane's page side: the host's Disco hues and the wallet seam (CONTRACT.md "Sheet additions").
import test from 'node:test';
import assert from 'node:assert/strict';
import { DISCO, setDiscoPalette } from '../sheet/skins.js';
import { asHost } from '../host.js';

test('init.disco replaces the Disco palette, anything unreadable keeps the fallback', () => {
  assert.deepEqual(setDiscoPalette(['#ff0080', '#00FF00']), [0xff0080, 0x00ff00]);
  assert.deepEqual(setDiscoPalette(['#ff0080', 'red', 7, '#12345']), [0xff0080]);
  assert.deepEqual(setDiscoPalette(['#111111', '#222222', '#333333', '#444444', '#555555', '#666666', '#777777']).length, 6);
  assert.equal(setDiscoPalette(undefined), DISCO);
  assert.equal(setDiscoPalette([]), DISCO);
  assert.equal(setDiscoPalette(['nope']), DISCO);
});

test('a mounted host passes the wallet seam through, and a host without it is a no-op', () => {
  const heard = [];
  const h = asHost({ walletLand: (k) => heard.push(k), onWalletAt: (f) => f({ x: 1, y: -2 }) });
  h.walletLand(0.5);
  let at = null;
  h.onWalletAt((m) => { at = m; });
  assert.deepEqual(heard, [0.5]);
  assert.deepEqual(at, { x: 1, y: -2 });
  const bare = asHost({});
  assert.doesNotThrow(() => { bare.walletLand(1); bare.onWalletAt(() => {}); });
});
