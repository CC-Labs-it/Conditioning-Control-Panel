import { test } from 'node:test';
import { strict as assert } from 'node:assert';
import { artBase } from '../shared/artBase.js';

test('selects the art origin for each host', () => {
  globalThis.location = { host: 'ccp.game' };
  assert.equal(artBase(), 'https://ccp.art/');
  globalThis.location.host = '127.0.0.1:1234';
  assert.equal(artBase(), '/__art__/');
  delete globalThis.location;
});
