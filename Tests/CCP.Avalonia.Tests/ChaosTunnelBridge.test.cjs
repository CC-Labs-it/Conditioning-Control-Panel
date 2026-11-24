// Exercise the actual page startup bridge with a carrier arriving before or after world creation.
const { readFileSync } = require('node:fs');
const { strict: assert } = require('node:assert');
const page = readFileSync('Assets/web/tunnel/main.js', 'utf8');
const bridge = page.slice(page.indexOf('let wv ='), page.indexOf('const sfxBridge'))
  + page.slice(page.indexOf('let world ='), page.indexOf('// ---- world'));
function start(win) {
  return new Function('window', bridge + `
    return {
      build: handle => { world = {handle}; for (const d of preBuffer) world.handle(d); preBuffer.length = 0; post({type:'ready'}); },
      send: post
    };`)(win);
}
for (const timing of ['early', 'before-build', 'after-build']) {
  const win = new EventTarget();
  const carrier = new EventTarget();
  const outbound = [];
  const inbound = [];
  carrier.postMessage = m => outbound.push(m);
  if (timing === 'early') win.chrome = { webview: carrier };
  const pageBridge = start(win);
  if (timing === 'before-build') {
    win.chrome = { webview: carrier };
    win.dispatchEvent(new Event('ccp-tunnel-connect'));
  }
  if (timing !== 'after-build')
    carrier.dispatchEvent(new MessageEvent('message', {data:{type:'zone-hint'}}));
  pageBridge.build(m => inbound.push(m));
  if (timing === 'after-build') {
    assert.equal(outbound.length, 0);
    win.chrome = { webview: carrier };
    win.dispatchEvent(new Event('ccp-tunnel-connect'));
  }
  win.dispatchEvent(new Event('ccp-tunnel-connect'));
  if (timing !== 'after-build') assert.deepEqual(inbound.shift(), {type:'zone-hint'});
  assert.deepEqual(outbound, [{type:'ready'}]);
  carrier.dispatchEvent(new MessageEvent('message', {data:{type:'streak', combo:4}}));
  win.dispatchEvent(new MessageEvent('ccp-tunnel-message', {data:{type:'run-start'}}));
  assert.deepEqual(inbound, [{type:'streak', combo:4}, {type:'run-start'}]);
  pageBridge.send({type:'exit-done'});
  assert.equal(outbound[1].type, 'exit-done');
}
console.log('Tunnel page bridge: early and late carrier passed');
