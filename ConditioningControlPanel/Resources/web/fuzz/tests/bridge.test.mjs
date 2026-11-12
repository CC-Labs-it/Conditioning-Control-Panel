import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as B from '../bridge.js';

test('buffered setting notifications apply in arrival order, including a final revocation', () => {
  B._dispatch({type:'setting', key:'media.remoteConsent', value:true});
  B._dispatch({type:'setting', key:'media.remoteConsent', value:false});
  const values = [];
  const off = B.on('setting', frame => values.push(frame.value));
  assert.deepEqual(values,[true,false]);
  off();
});

test('unhosted: boot resolves at once with the default caps and sends are no-ops', () => {
  assert.equal(B.isHosted, false);
  let init = null;
  B.boot({ onInit: (f) => { init = f; } });
  assert.equal(init.protocol, 1);
  assert.equal(init.hosted, false);
  assert.equal(init.mod, 'default');
  assert.equal(B.send('fuzz-ended', { total: 1 }), false);
});

test('host frames that land before a listener are kept and replayed to the first subscriber', () => {
  B._dispatch({ type: 'scrolller-feed', v: 1, rows: [] });
  const got = [];
  B.on('scrolller-feed', (f) => got.push(f));
  assert.equal(got.length, 1);
  assert.deepEqual(got[0].rows, []);
});

test('the boot allowlist is exactly the five frames the contract names', () => {
  assert.deepEqual([...B.BOOT_ALLOWED].sort(), ['boot-error', 'exit', 'heartbeat', 'log', 'ready']);
});
