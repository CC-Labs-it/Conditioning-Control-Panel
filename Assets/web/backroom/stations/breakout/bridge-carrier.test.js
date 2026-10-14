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

// Review 2 (games-host): station.js read "hosted" from chrome.webview alone, so on WebKitGTK the five
// big-beat effects went to the host (each waiting out its 4 s fx timeout) and the pause menu's Exit did nothing.
test('the string carrier counts as a desktop host for the in-window effects and the pause Exit', async () => {
  const { desktopHosted, routeFx } = await import('./page-fx.js');
  const carrier = { invokeCSharpAction() {} };
  assert.equal(desktopHosted(carrier), true);
  assert.equal(desktopHosted({ chrome: { webview: {} } }), true);   // WPF WebView2, unchanged
  assert.equal(desktopHosted({}), false);                            // the web / dev.html
  const toHost = [], toPage = [];
  const fx = routeFx({ hosted: desktopHosted(carrier), fx: id => toHost.push(id), page: { fire: id => toPage.push(id) } });
  fx('fx.wash'); fx('fx.gif_storm');
  assert.deepEqual(toPage, ['fx.wash']);
  assert.deepEqual(toHost, ['fx.gif_storm']);
  const { readFileSync } = await import('node:fs');
  const station = readFileSync(new URL('./station.js', import.meta.url), 'utf8');
  assert.match(station, /const hosted = desktopHosted\(\);/);
  assert.match(station, /if\(action==='exit'&&desktopHosted\(\)\)back\(\);/);
  // Review 3: the F2 dev panel was gated on chrome.webview alone, so WebKitGTK opened dev controls in the shipped demo.
  assert.match(station, /if\(e\.key!=='F2' \|\| desktopHosted\(\)\)return;/);
  assert.doesNotMatch(station, /e\.key!=='F2' \|\| globalThis\.chrome\?\.webview/);
});
