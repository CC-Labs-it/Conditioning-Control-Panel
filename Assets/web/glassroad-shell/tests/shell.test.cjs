'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const { launchUrl, allowedRequest, allowedPermission, start } = require('../main.js');

const url = 'http://127.0.0.1:4567/glassroad/index.html?ccp_t=0123456789ABCDEF0123456789ABCDEF&join=room';
const origin = 'http://127.0.0.1:4567';
const turn = () => new Promise(resolve => setImmediate(resolve));

test('launch requires the exact loopback page and one real server token', () => {
  assert.equal(launchUrl(url).origin, origin);
  for (const bad of [url.replace('127.0.0.1', 'localhost'), url.replace('127.0.0.1', '127.0.0.2'),
    url.replace('http:', 'https:'), url.replace(':4567', ''), url.replace('/glassroad/index.html', '/intake/index.html'),
    url.replace('0123456789ABCDEF0123456789ABCDEF', 'x'), url + '&ccp_t=x', url + '#x',
    url.replace('127.0.0.1', 'user@127.0.0.1'), 'file:///glassroad/index.html', null]) assert.equal(launchUrl(bad), null, String(bad));
});

test('requests stay on the asset server except local blobs, signed-song origin and race socket', () => {
  for (const good of [url, origin + '/glassroad/boot.js', 'blob:' + origin + '/id', 'data:image/png;base64,AA==',
    'wss://glassroad-room.cclabs-race.workers.dev/room/1', 'https://glassroad-room.cclabs-race.workers.dev/song/id/chart.json?sig=x'])
    assert.equal(allowedRequest(good, origin), true, good);
  for (const bad of ['https://scrolller.com/a', 'http://127.0.0.1:4568/a', 'file:///tmp/a',
    'https://glassroad-room.cclabs-race.workers.dev.evil.test/song/x', 'https://glassroad-room.cclabs-race.workers.dev/other',
    'wss://other.test/', 'blob:http://other.test/id', 'https://user@glassroad-room.cclabs-race.workers.dev/song/x'])
    assert.equal(allowedRequest(bad, origin), false, bad);
});

test('only the race page gets gamepad and fullscreen permission', () => {
  for (const permission of ['gamepad', 'fullscreen']) {
    assert.equal(allowedPermission(permission, origin, url), true);
    assert.equal(allowedPermission(permission, origin, 'https://other.test/'), false);
    assert.equal(allowedPermission(permission, origin, ''), false);
  }
  for (const permission of ['media', 'geolocation', 'notifications', 'pointerLock', ''])
    assert.equal(allowedPermission(permission, origin, url), false);
});

function fixture({ ready = Promise.resolve(), failLoad = false } = {}) {
  const app = new EventEmitter(); app.whenReady = () => ready; app.quit = () => { app.quits++; }; app.quits = 0;
  const ipcMain = new EventEmitter(), stdin = new PassThrough(), stdout = new PassThrough();
  let output = ''; stdout.on('data', chunk => { output += chunk; });
  const ses = { webRequest: { onBeforeRequest: (_filter, cb) => { ses.request = cb; } },
    setPermissionRequestHandler: cb => { ses.permit = cb; }, setPermissionCheckHandler: cb => { ses.check = cb; } };
  const windows = [];
  class Window extends EventEmitter {
    constructor(options) {
      super(); this.options = options; this.dead = false; this.sent = []; this.muted = [];
      this.webContents = new EventEmitter(); this.webContents.mainFrame = {};
      this.webContents.setAudioMuted = value => this.muted.push(value);
      this.webContents.setWindowOpenHandler = cb => { this.openHandler = cb; };
      this.webContents.send = (channel, message) => this.sent.push([channel, message]);
      this.webContents.getURL = () => url;
      windows.push(this);
    }
    isDestroyed() { return this.dead; }
    destroy() { this.dead = true; this.emit('closed'); }
    async loadURL(value) { this.url = value; if (failLoad) throw new Error("load failed"); }
  }
  start({ app, BrowserWindow: Window, ipcMain, session: { fromPartition: () => ses } }, { stdin, stdout });
  return { app, ipcMain, stdin, stdout, ses, windows, output: () => output.trim().split('\n').filter(Boolean).map(JSON.parse),
    input: m => stdin.write(JSON.stringify(m) + '\n') };
}

test('one window, locked navigation, IPC, Escape and mute follow the real handlers', async () => {
  const f = fixture();
  f.input({ type: 'launch', url }); f.input({ type: 'init', v: 2 }); await turn();
  assert.equal(f.windows.length, 1);
  const w = f.windows[0], web = w.webContents, event = { sender: web, senderFrame: web.mainFrame };
  assert.equal(w.url, url); assert.deepEqual(w.muted, [true]);
  assert.equal(w.options.webPreferences.sandbox, true); assert.equal(w.options.webPreferences.nodeIntegration, false);
  assert.equal(w.options.webPreferences.contextIsolation, true); assert.equal(w.options.webPreferences.devTools, false);
  assert.deepEqual(w.openHandler({ url: 'https://other.test' }), { action: 'deny' });
  for (const name of ['will-navigate', 'will-frame-navigate', 'will-redirect', 'will-attach-webview']) {
    let prevented = false; web.emit(name, { preventDefault: () => { prevented = true; } }); assert.equal(prevented, true, name);
  }
  let denied; f.ses.request({ url: 'https://scrolller.com/x' }, result => { denied = result.cancel; }); assert.equal(denied, true);
  let allowed; f.ses.request({ url: origin + '/glassroad/boot.js' }, result => { allowed = !result.cancel; }); assert.equal(allowed, true);
  let permission; f.ses.permit(web, 'gamepad', result => { permission = result; }, { requestingUrl: url }); assert.equal(permission, true);
  f.ses.permit(web, 'media', result => { permission = result; }, { requestingUrl: url }); assert.equal(permission, false);
  assert.equal(f.ses.check(web, 'fullscreen', origin), true); assert.equal(f.ses.check({}, 'fullscreen', origin), false);
  assert.deepEqual(w.sent, []); web.emit('did-finish-load');
  assert.deepEqual(w.sent, [['ccp:host-message', { type: 'init', v: 2 }]]);
  f.input({ type: 'ping', t: 1 }); assert.deepEqual(w.sent.at(-1), ['ccp:host-message', { type: 'ping', t: 1 }]);
  f.ipcMain.emit('ccp:message', event, { type: 'ready' });
  f.ipcMain.emit('ccp:message', { ...event, senderFrame: {} }, { type: 'bad' });
  f.ipcMain.emit('ccp:audio-unlocked', { ...event, senderFrame: {} }); assert.deepEqual(w.muted, [true]);
  f.ipcMain.emit('ccp:audio-unlocked', event); assert.deepEqual(w.muted, [true, false]);
  let blocked = 0; const key = { preventDefault: () => blocked++ };
  web.emit('before-input-event', key, { key: 'Escape', type: 'keyDown', isAutoRepeat: false });
  web.emit('before-input-event', key, { key: 'Escape', type: 'keyDown', isAutoRepeat: true });
  web.emit('before-input-event', key, { key: 'Escape', type: 'keyUp' });
  web.emit('before-input-event', key, { key: 'A', type: 'keyDown' });
  assert.equal(blocked, 3); assert.deepEqual(f.output(), [{ type: 'ready' }, { type: 'escape' }]);
  f.input({ type: 'launch', url }); assert.equal(f.windows.length, 1);
  f.input({ type: 'shell-close' }); assert.equal(w.dead, true); assert.equal(f.app.quits, 1);
});

test('invalid launch, lost host, renderer failure and oversized input stop the shell', async () => {
  let f = fixture(); f.input({ type: 'launch', url: 'https://evil.test/' }); await turn();
  assert.equal(f.windows.length, 0); assert.equal(f.app.quits, 1);
  f = fixture(); f.input({ type: 'launch', url }); await turn(); f.stdin.end(); await turn();
  assert.equal(f.windows[0].dead, true); assert.equal(f.app.quits, 1);
  f = fixture(); f.input({ type: 'launch', url }); await turn(); f.windows[0].webContents.emit('render-process-gone');
  assert.equal(f.windows[0].dead, true);
  f = fixture(); f.input({ type: 'launch', url }); await turn(); f.stdin.write('x'.repeat(1024 * 1024 + 1));
  assert.equal(f.windows[0].dead, true);
});


test('preload exposes only the page bridge and forwards host frames', () => {
  const vm = require('node:vm'), fs = require('node:fs'), path = require('node:path');
  const ipc = new EventEmitter(), sent = []; ipc.send = (...args) => sent.push(args);
  let name, bridge;
  const source = fs.readFileSync(path.join(__dirname, '../preload.js'), 'utf8');
  vm.runInNewContext(source, { require: () => ({ ipcRenderer: ipc, contextBridge: { exposeInMainWorld: (n, b) => { name = n; bridge = b; } } }) });
  assert.equal(name, 'ccpHost');
  let received; const listener = e => { received = e.data; };
  bridge.addEventListener('message', listener); ipc.emit('ccp:host-message', {}, { type: 'init' });
  assert.deepEqual(received, { type: 'init' });
  bridge.removeEventListener('message', listener); ipc.emit('ccp:host-message', {}, { type: 'other' });
  assert.deepEqual(received, { type: 'init' });
  bridge.postMessage({ type: 'ready' }); bridge.audioUnlocked();
  assert.deepEqual(sent, [['ccp:message', { type: 'ready' }], ['ccp:audio-unlocked']]);
});


test('host loss before startup, failed load, output failure and a full queue cannot leave a race running', async () => {
  let release; const ready = new Promise(resolve => { release = resolve; });
  let f = fixture({ ready }); f.input({ type: 'launch', url }); f.stdin.end(); await turn(); release(); await turn();
  assert.equal(f.windows.length, 0); assert.equal(f.app.quits, 1);
  f = fixture({ failLoad: true }); f.input({ type: 'launch', url }); await turn();
  assert.equal(f.windows[0].dead, true); assert.equal(f.app.quits, 1);
  f = fixture(); f.input({ type: 'launch', url }); await turn(); f.stdout.emit('error', new Error('pipe lost'));
  assert.equal(f.windows[0].dead, true); assert.equal(f.app.quits, 1);
  f = fixture(); f.input({ type: 'launch', url }); await turn();
  for (let i = 0; i < 257; i++) f.input({ type: 'ping', i });
  assert.equal(f.windows[0].dead, true); assert.equal(f.app.quits, 1);
});
