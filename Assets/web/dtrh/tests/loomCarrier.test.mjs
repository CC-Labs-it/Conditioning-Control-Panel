import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const html = readFileSync(new URL('../loom.html', import.meta.url), 'utf8');
const bootstrap = /<script>([\s\S]*?)<\/script>/.exec(html)?.[1] ?? '';

test('Loom installs a two-way carrier before bridge module evaluation', () => {
  const sent = [];
  const window = { invokeCSharpAction: body => sent.push(JSON.parse(body)) };
  vm.runInNewContext(bootstrap, { window, EventTarget });
  assert.ok(window.chrome?.webview, 'carrier must exist before bridge.js imports');
  window.chrome.webview.postMessage({ type: 'ready' });
  assert.deepEqual(sent, [{ type: 'ready' }]);
  let received;
  window.chrome.webview.addEventListener('message', e => { received = e.data; });
  window.chrome.webview.dispatchEvent(new MessageEvent('message', { data: { type: 'loom-list' } }));
  assert.deepEqual(received, { type: 'loom-list' });
});

test('Loom preserves the native WebView2 carrier and tolerates standalone browser', () => {
  const carrier = {};
  const window = { chrome: { webview: carrier }, invokeCSharpAction: () => assert.fail() };
  vm.runInNewContext(bootstrap, { window, EventTarget });
  assert.equal(window.chrome.webview, carrier);
  vm.runInNewContext(bootstrap, { window: {}, EventTarget });
});
