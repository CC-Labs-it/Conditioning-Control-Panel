import {test} from 'node:test';
import assert from 'node:assert/strict';

// The Avalonia head (WebKitGTK) has no chrome.webview: it injects window.invokeCSharpAction (string) and
// answers through window.__ccpRnPush(json). Without this carrier the page is unhosted on Linux, never says
// ready, never reads the demo limit from init, and its exit never reaches the host.
test('the string carrier hosts the page: ready goes out, init comes back, held frames flush', async () => {
  const sent = [];
  globalThis.window = { invokeCSharpAction: s => sent.push(JSON.parse(s)) };
  const b = await import('../../bridge.js');
  assert.equal(b.isHosted, true);
  b.announceReady();
  assert.deepEqual(sent.map(m => m.type), ['ready']);
  b.send({ type: 'station-open', station: 'breakout' });   // held until init
  assert.equal(sent.length, 1);
  let init = null;
  b.bridge.on('init', m => { init = m; });
  window.__ccpRnPush(JSON.stringify({ type: 'init', breakout: { storyLimit: 3, endless: false, demo: true } }));
  assert.equal(init?.breakout?.storyLimit, 3);
  b.markInitialized();
  b.send({ type: 'exit-done' });
  assert.deepEqual(sent.map(m => m.type), ['ready', 'station-open', 'exit-done']);
});
