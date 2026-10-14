import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {carrier, desktopHosted} from '../carrier.js';

// fyp/main.js read chrome.webview alone, so on WebKitGTK it never said ready (dev-mode empty state).
test('the string carrier posts out as a string and delivers host frames as message events', () => {
  const sent = [], got = [];
  const g = { invokeCSharpAction: s => sent.push(s) };
  const b = carrier(g);
  assert.equal(desktopHosted(g), true);
  b.addEventListener('message', e => got.push(e.data));
  b.postMessage({ type: 'ready' });
  assert.deepEqual(sent.map(s => JSON.parse(s).type), ['ready']);
  g.__ccpRnPush(JSON.stringify({ type: 'init', assets: [] }));
  g.__ccpRnPush('not json');
  assert.deepEqual(got.map(m => m.type), ['init']);
});

test('WebView2 is returned unchanged and a plain browser has no carrier', () => {
  const webview = { postMessage() {}, addEventListener() {} };
  assert.equal(carrier({ chrome: { webview } }), webview);
  assert.equal(carrier({}), null);
  assert.equal(desktopHosted({}), false);
});

test('the For You page takes its bridge from the shared carrier', () => {
  const main = readFileSync(new URL('../../fyp/main.js', import.meta.url), 'utf8');
  assert.match(main, /import \{ carrier \} from '\.\.\/shared\/carrier\.js';/);
  assert.match(main, /const bridge = carrier\(window\);/);
});
