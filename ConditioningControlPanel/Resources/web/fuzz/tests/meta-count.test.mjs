import test from 'node:test';
import assert from 'node:assert/strict';
import { bump, read, community } from '../ui/meta-count.js';

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

test('community permits read-only playtests, isolates malformed rows and forwards cancellation', async t => {
  const asOf = new Date().toISOString();
  const ccp = { active: 0, windowSeconds: 180, asOf };
  let reply = { discord: { online: 914, members: -1, asOf }, ccp }, calls = 0, captured;
  fixture(t, 'http://127.0.0.1:8817/fuzz/?buy=go_pals', async (url, options) => {
    assert.equal(url, 'https://codebambi-proxy.vercel.app/v2/tc/community');
    assert.equal(options.method, 'GET');
    assert.equal(options.credentials, 'omit');
    calls++;
    captured = options.signal;
    return Response.json(reply);
  });
  const controller = new AbortController();
  assert.deepEqual(await community(controller.signal), { discord: null, ccp });
  controller.abort();
  assert.ok(captured.aborted);
  assert.equal(await bump(key), null);
  globalThis.location = new URL('https://tc.cclabs.app/fuzz/?preview=x');
  reply = { discord: { online: 0, members: 0, asOf }, ccp: { ...ccp, asOf: null } };
  assert.deepEqual(await community(), { discord: reply.discord, ccp: null });
  globalThis.location = new URL('http://127.0.0.1:8818/fuzz/');
  assert.equal(await community(), null);
  assert.equal(calls, 2);
});
