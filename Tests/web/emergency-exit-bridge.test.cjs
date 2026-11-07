// Run with node. Exercise the actual shared page carrier, including late GTK injection.
const {readFileSync} = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = readFileSync('Assets/web/emergency-exit/shared/bridge.js', 'utf8');
for (const native of [false, true]) {
  const sent = [], timers = [];
  const window = {location: {search: '?game=password&avalonia=1'}};
  if (native) window.chrome = {webview: {postMessage: m => sent.push(m), addEventListener() {}}};
  vm.runInNewContext(source, {window, console, setTimeout: fn => timers.push(fn), encodeURIComponent});
  const bridge = window.EE.bridge;
  assert.equal(bridge.isHosted, true);
  assert.equal(bridge.isMock, false);
  bridge.send({type: 'ready'});
  if (!native) {
    assert.equal(sent.length, 0);
    window.invokeCSharpAction = json => sent.push(JSON.parse(json));
    timers.shift()();
  }
  assert.equal(sent[0].type, 'ready');
  window.__ccpRnPush('{"type":"verdict","outcome":"escape"}');
  let outcome;
  bridge.on('verdict', m => outcome = m.outcome);
  assert.equal(outcome, 'escape');
  bridge.send({type: 'game-finished', result: 'completed'});
  assert.equal(sent[1].result, 'completed');
  assert.equal(timers.length, 0); // no mock verdict timer in hosted mode
}
console.log('Emergency Exit bridge: GTK and WebView2 passed');
