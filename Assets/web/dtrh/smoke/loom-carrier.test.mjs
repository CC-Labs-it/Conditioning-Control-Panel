import { test } from 'node:test';
import assert from 'node:assert/strict';

test('Loom string bridge announces ready, saves, and delivers buffered lists and results', async () => {
  const sent = [], lists = [], results = [];
  globalThis.window = { invokeCSharpAction: json => sent.push(JSON.parse(json)) };
  try {
    const bridge = await import('../bridge.js?loom-string');
    bridge.announceReady();
    assert.deepEqual(sent, [{ type: 'ready', protocol: 1 }]);
    assert.equal(bridge.isHosted, true);
    window.__ccpRnPush(JSON.stringify({ type: 'loom-list', spirals: [{ slug: 'test' }] }));
    bridge.on('loom-list', m => lists.push(m.spirals));
    assert.deepEqual(lists, [[{ slug: 'test' }]]);
    bridge.on('loom-result', m => results.push(m.ok));
    bridge.send({ type: 'loom-save', name: 'test', gifBase64: 'gif' });
    assert.equal(sent[1].type, 'loom-save');
    window.__ccpRnPush(JSON.stringify({ type: 'loom-result', ok: true }));
    assert.deepEqual(results, [true]);
  } finally { delete globalThis.window; }
});

test('Loom keeps the WPF WebView2 carrier and a browser preview stays unhosted', async () => {
  const sent = [];
  let receive;
  globalThis.window = { chrome: { webview: {
    postMessage: m => sent.push(m), addEventListener: (_, fn) => { receive = fn; },
  } } };
  try {
    const bridge = await import('../bridge.js?loom-wpf');
    bridge.announceReady();
    assert.equal(sent[0].type, 'ready');
    let ok;
    bridge.on('loom-result', m => { ok = m.ok; });
    receive({ data: { type: 'loom-result', ok: true } });
    assert.equal(ok, true);
    globalThis.window = {};
    const preview = await import('../bridge.js?loom-preview');
    assert.equal(preview.isHosted, false);
    assert.doesNotThrow(() => preview.announceReady());
  } finally { delete globalThis.window; }
});
