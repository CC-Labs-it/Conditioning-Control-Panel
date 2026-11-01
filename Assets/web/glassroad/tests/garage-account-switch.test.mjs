import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createOwned, OWNED_KEY } from '../meta/owned.js';
import { createProfileSync } from '../meta/profileSync.js';
import { createBridge } from '../host/bridge.js';
import { hostTransport } from '../net/lobbyApi.js';

test('host account switches preserve caches and reject an old profile reply', async () => {
  const data = new Map([[OWNED_KEY, JSON.stringify({ uid: 'a', grants: ['cos.kart.body.petal'] })]]);
  const storage = { getItem: k => data.get(k) ?? null, setItem: (k, v) => data.set(k, String(v)) };
  const sent = [];
  let deliver;
  const win = { location: { search: '' }, matchMedia: () => ({ matches: false }),
    setInterval: () => 1, clearInterval() {}, setTimeout: () => 1, clearTimeout() {},
    chrome: { webview: { addEventListener: (_, fn) => { deliver = m => fn({ data: m }); }, postMessage: m => sent.push(m) } } };
  const bridge = createBridge({ win });
  const owned = createOwned({ storage });
  owned.setAccount('a');
  bridge.on('owned', m => owned.receive(m));
  deliver({ type: 'owned', accountId: 'a', accountVersion: 0 }); // arrives before transport setup
  const request = hostTransport(bridge, { timers: win });
  const sameAccount = request('GET', '/v2/race/profile');
  const firstId = sent.at(-1).id;
  deliver({ type: 'owned', accountId: 'a', accountVersion: 0, grants: ['cos.kart.body.petal'] });
  deliver({ type: 'net-reply', id: firstId, status: 200, body: '{}' });
  assert.equal((await sameAccount).status, 200);
  const sync = createProfileSync({ request, storage, onGrants: g => owned.adopt(g) });
  const pending = sync.pull();
  const oldId = sent.at(-1).id;
  deliver({ type: 'owned', accountId: null, accountVersion: 1 });
  assert.deepEqual(owned.grants, []);
  deliver({ type: 'owned', accountId: 'b', accountVersion: 2 });
  deliver({ type: 'owned', accountId: 'b', accountVersion: 2, grants: ['cos.kart.body.trinket'] });
  // Even a late success queued before the host noticed the switch cannot reach B.
  deliver({ type: 'net-reply', id: oldId, status: 200, body: { record: { v: 1 }, grants: ['cos.kart.body.petal'] } });
  await pending;
  assert.deepEqual(owned.grants, ['cos.kart.body.trinket']);
  deliver({ type: 'owned', accountId: 'a', accountVersion: 0, grants: ['cos.kart.body.petal'] });
  assert.deepEqual(owned.grants, ['cos.kart.body.trinket']);
  deliver({ type: 'owned', accountId: 'a', accountVersion: 3 });
  assert.deepEqual(owned.grants, ['cos.kart.body.petal']);
  deliver({ type: 'owned', accountId: 'b', accountVersion: 4 });
  assert.deepEqual(owned.grants, ['cos.kart.body.trinket']);
  bridge.dispose();
});
