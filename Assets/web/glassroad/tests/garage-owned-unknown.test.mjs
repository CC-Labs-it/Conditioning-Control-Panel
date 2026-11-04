import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createOwned, OWNED_KEY } from '../meta/owned.js';
import { createProfileSync } from '../meta/profileSync.js';

test('a fresh page keeps remembered karts if the host has no snapshot and profile fails', async () => {
  const saved = new Map([[OWNED_KEY, JSON.stringify({ uid: 'u', grants: ['cos.kart.body.petal'] })]]);
  const storage = { getItem: key => saved.get(key) ?? null, setItem: (key, value) => saved.set(key, value) };
  const owned = createOwned({ storage });
  const init = { identity: { unifiedId: 'u' } }; // the host omits grants until it has a snapshot
  owned.setAccount(init.identity.unifiedId);
  if (Array.isArray(init.identity.grants)) owned.adopt(init.identity.grants);
  const sync = createProfileSync({ request: async () => { throw Error('offline'); }, storage, onGrants: list => owned.adopt(list) });
  await sync.pull();
  assert.deepEqual(owned.grants, ['cos.kart.body.petal']);
  assert.deepEqual(JSON.parse(saved.get(OWNED_KEY)).grants, ['cos.kart.body.petal']);
});
