import test from 'node:test';
import assert from 'node:assert/strict';
import { bump, read } from '../ui/meta-count.js';

const key = 'tc:end:dr_end';
function fixture(t, url, fetcher) {
  const data = new Map();
  for (const [name, value] of Object.entries({
    location: new URL(url),
    localStorage: { getItem: k => data.get(k) ?? null, setItem: (k, v) => data.set(k, v) },
    fetch: fetcher,
  })) {
    const before = Object.getOwnPropertyDescriptor(globalThis, name);
    Object.defineProperty(globalThis, name, { configurable: true, writable: true, value });
    t.after(() => {
      if (before) Object.defineProperty(globalThis, name, before);
      else delete globalThis[name];
    });
  }
}

test('two real endings use the returned counts, and a batch read does not increment', async t => {
  let total = 18;
  fixture(t, 'https://tc.cclabs.app/fuzz/?host=web', async (url, options) => {
    assert.equal(options.credentials, 'omit');
    if (options.method === 'POST') {
      assert.equal(url, 'https://codebambi-proxy.vercel.app/v2/tc/count');
      assert.deepEqual(JSON.parse(options.body), { key });
      return Response.json({ key, n: ++total });
    }
    assert.equal(new URL(url).searchParams.get('keys'), key);
    return Response.json({ [key]: total });
  });
  assert.equal(await bump(key), 19);
  assert.deepEqual(await read([key, key]), { [key]: 19 });
  assert.equal(await bump(key), 20);
});

test('offline or malformed answers use the last confirmed number without compounding guesses', async t => {
  let reply = { key, n: 34 };
  fixture(t, 'https://tc.cclabs.app/fuzz/', async () => {
    if (!reply) throw new TypeError('offline');
    return Response.json(reply);
  });
  assert.equal(await bump(key), 34);
  reply = { key: 'wrong', n: 90 };
  assert.equal(await bump(key), 35);
  reply = null;
  assert.equal(await bump(key), 35);
  assert.deepEqual(await read([key]), { [key]: 34 });
  localStorage.getItem = () => { throw new Error('disabled'); };
  assert.equal(await bump(key), null);
});

test('local, development and preview pages never contact the global counter', async t => {
  let calls = 0;
  fixture(t, 'http://127.0.0.1/fuzz/', async () => { calls++; return Response.json({ key, n: 1 }); });
  for (const url of ['http://127.0.0.1/fuzz/', 'https://branch.pages.dev/fuzz/',
    'https://tc.cclabs.app/fuzz/dev/play-ending.html?end=dr_end', 'https://tc.cclabs.app/fuzz/?buy=dr_end',
    'https://tc.cclabs.app/fuzz/?preview=journey']) {
    globalThis.location = new URL(url);
    assert.equal(await bump(key), null);
    assert.deepEqual(await read([key]), { [key]: null });
  }
  assert.equal(calls, 0);
});
