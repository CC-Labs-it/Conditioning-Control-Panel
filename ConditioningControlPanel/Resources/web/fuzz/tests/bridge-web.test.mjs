import { test } from 'node:test';
import assert from 'node:assert/strict';
import { hostOriginAllowed, pickParentOrigin, webHostOrigin, HOST_ORIGINS } from '../bridge.js';

/* A fake framed window: a parent that records what it is sent, and a message listener the test can fire. */
function fakeWindow({ search = '?host=web', hostname = 'tc.cclabs.app', ancestors, referrer = '', framed = true } = {}) {
  const parent = { sent: [], postMessage(frame, origin) { this.sent.push([frame, origin]); } };
  const listeners = [];
  const w = {
    location: { search, hostname, ...(ancestors ? { ancestorOrigins: ancestors } : {}) },
    document: { referrer },
    addEventListener: (type, fn) => { if (type === 'message') listeners.push(fn); },
    fire: (ev) => { for (const fn of listeners) fn(ev); },
  };
  w.parent = framed ? parent : w;
  return w;
}

/** A fresh bridge module bound to `w` (the module reads `window` once, at load). */
let n = 0;
async function bridgeFor(w) {
  const prev = globalThis.window;
  globalThis.window = w;
  try { return await import(`../bridge.js?web=${++n}`); } finally { if (prev === undefined) delete globalThis.window; else globalThis.window = prev; }
}

test('host origins: production list is exact, dev origins only on a local page', () => {
  assert.equal(HOST_ORIGINS[0], 'https://app.cclabs.app');
  assert.ok(HOST_ORIGINS.every((o) => /^https:\/\/[a-z0-9.-]+$/.test(o)), 'exact https origins only, no wildcards');
  assert.ok(hostOriginAllowed('https://app.cclabs.app', 'tc.cclabs.app'));
  for (const bad of ['https://app.cclabs.app.evil.com', 'http://app.cclabs.app', 'https://app.cclabs.app/', 'https://cclabs.app', 'https://tc.cclabs.app', 'null', '', null,
    'https://cclabs-web-git-feat-tc-host-codebambis-projects.vercel.app', 'https://cclabs-web-git-feat-other-codebambis-projects.vercel.app', 'https://evil-cclabs-web-git-feat-tc-host-codebambis-projects.vercel.app',
    'https://cclabs-web-abc-codebambi.vercel.app']) assert.equal(hostOriginAllowed(bad, 'tc.cclabs.app'), false, String(bad));
  assert.equal(hostOriginAllowed('http://localhost:3000', 'tc.cclabs.app'), false);
  assert.ok(hostOriginAllowed('http://localhost:3000', '127.0.0.1'));
  assert.ok(hostOriginAllowed('http://127.0.0.1:3000', 'localhost'));
  assert.equal(hostOriginAllowed('http://127.0.0.1:3001', '127.0.0.1'), false);
});

test('parent origin: ancestorOrigins first, else the referrer origin, else nothing', () => {
  assert.equal(pickParentOrigin(fakeWindow({ ancestors: ['https://app.cclabs.app'], referrer: 'https://x.example/' })), 'https://app.cclabs.app');
  assert.equal(pickParentOrigin(fakeWindow({ referrer: 'https://app.cclabs.app/tc?x=1' })), 'https://app.cclabs.app');
  assert.equal(pickParentOrigin(fakeWindow({})), '');
});

test('web host needs a real parent, ?host=web and an allowed origin', () => {
  assert.equal(webHostOrigin(fakeWindow({ ancestors: ['https://app.cclabs.app'] })), 'https://app.cclabs.app');
  assert.equal(webHostOrigin(fakeWindow({ ancestors: ['https://app.cclabs.app'], search: '' })), '');
  assert.equal(webHostOrigin(fakeWindow({ ancestors: ['https://app.cclabs.app'], framed: false })), '');
  assert.equal(webHostOrigin(fakeWindow({ ancestors: ['https://evil.example'] })), '');
  assert.equal(webHostOrigin(fakeWindow({ referrer: 'https://evil.example/' })), '');
});

test('hosted by the site: frames go to the exact parent origin, never "*", and only that parent is heard', async () => {
  const w = fakeWindow({ ancestors: ['https://app.cclabs.app'] });
  const B = await bridgeFor(w);
  assert.equal(B.isHosted, true);
  assert.equal(B.transport, 'web');
  let init = null;
  B.boot({ onInit: (f) => { init = f; }, initTimeoutMs: 10_000 });
  assert.deepEqual(w.parent.sent[0], [{ type: 'ready', protocol: 1 }, 'https://app.cclabs.app']);
  assert.equal(B.send('scrolller-niches', { niches: [] }), false, 'queued until init');

  const frame = { type: 'init', protocol: 1, caps: { platform: 'web', scrolller: { consent: true } } };
  w.fire({ origin: 'https://evil.example', source: w.parent, data: frame });
  w.fire({ origin: 'https://app.cclabs.app', source: {}, data: frame });
  assert.equal(init, null, 'wrong origin or wrong source is ignored');
  w.fire({ origin: 'https://app.cclabs.app', source: w.parent, data: JSON.stringify(frame) });
  assert.equal(init?.caps.platform, 'web', 'a JSON string from the parent is read');
  assert.equal(B.isHosted, true);
  assert.deepEqual(w.parent.sent.at(-1), [{ type: 'scrolller-niches', niches: [] }, 'https://app.cclabs.app'], 'queue flushed on init');

  B.send('open-url', { url: 'https://cclabs.app/' });
  for (const [, origin] of w.parent.sent) assert.equal(origin, 'https://app.cclabs.app');
  let feed = null;
  B.on('scrolller-feed', (f) => { feed = f; });
  w.fire({ origin: 'https://app.cclabs.app', source: w.parent, data: { type: 'scrolller-feed', v: 1, rows: [] } });
  assert.deepEqual(feed?.rows, []);
});

test('an allowed parent that never answers init falls back to standalone', async () => {
  const w = fakeWindow({ ancestors: ['https://app.cclabs.app'] });
  const B = await bridgeFor(w);
  const init = await new Promise((resolve) => B.boot({ onInit: resolve, initTimeoutMs: 20 }));
  assert.equal(init.hosted, false);
  assert.deepEqual(init.caps, {});
  assert.equal(B.isHosted, false);
  assert.equal(B.transport, null);
  const before = w.parent.sent.length;
  assert.equal(B.send('open-url', { url: 'https://cclabs.app/' }), false, 'standalone sends nothing');
  w.fire({ origin: 'https://app.cclabs.app', source: w.parent, data: { type: 'init', protocol: 1 } });
  assert.equal(w.parent.sent.length, before);
});

test('a parent not on the list leaves the page standalone at once', async () => {
  const w = fakeWindow({ ancestors: ['https://evil.example'] });
  const B = await bridgeFor(w);
  assert.equal(B.isHosted, false);
  let init = null;
  B.boot({ onInit: (f) => { init = f; } });
  assert.equal(init?.hosted, false);
  assert.equal(w.parent.sent.length, 0);
});
