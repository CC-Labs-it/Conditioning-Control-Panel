import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync('Assets/web/remix/jackpot.js', 'utf8').replace(/^import .*;\r?\n/gm, '');

for (const native of [false, true]) test(`remix bridge ${native ? 'WebView2' : 'WebKit'}`, () => {
    const messages = [];
    const webview = new EventTarget();
    webview.postMessage = message => messages.push(message);
    const window = { addEventListener() {} };
    if (!native) window.invokeCSharpAction = message => messages.push(JSON.parse(message));
    const context = { window, EventTarget, AbortController, performance, URLSearchParams,
        location: { search: '' }, stopDecoder() {}, stopEncoder() {}, console };
    if (native) context.chrome = { webview };
    vm.runInNewContext(source, context);
    assert.equal(messages[0].type, 'ready');
    window.__ccpRemixPush({ type: 'build', id: 'test', media: [] });
    const reply = messages.find(message => message.type === 'built');
    assert.equal(reply.id, 'test');
    assert.equal(reply.ok, false);
    assert.equal(reply.error, 'no media');
});
