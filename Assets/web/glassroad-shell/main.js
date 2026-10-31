'use strict';

// The first stdin line is {type:'launch', url: WebAssetServer.Url(...)}. Later lines
// are page frames, except {type:'shell-close'}. stdout carries page frames and
// {type:'escape'}; diagnostics go to stderr. EOF closes the window.
const path = require('node:path');
const ROOM = 'glassroad-room.cclabs-race.workers.dev';
const MAX_LINE = 1024 * 1024;

function launchUrl(raw) {
  if (typeof raw !== 'string') return null;
  try {
    const u = new URL(raw);
    const tokens = u.searchParams.getAll('ccp_t');
    return u.protocol === 'http:' && u.hostname === '127.0.0.1' && u.port &&
      !u.username && !u.password && !u.hash && u.pathname === '/glassroad/index.html' &&
      tokens.length === 1 && /^[A-Fa-f0-9]{32}$/.test(tokens[0]) ? u : null;
  } catch { return null; }
}

function allowedRequest(raw, origin) {
  try {
    const u = new URL(raw);
    if (u.origin === origin && u.protocol === 'http:') return true;
    // The page uses local blob pictures and signed room songs; online races use
    // the room's WebSocket (NET.md sections 1 and 14). Neither is a navigation.
    if (u.protocol === 'blob:') return raw.startsWith('blob:' + origin + '/');
    if (u.protocol === 'data:') return true;
    return !u.username && !u.password && !u.port && u.hostname === ROOM &&
      (u.protocol === 'wss:' || (u.protocol === 'https:' && u.pathname.startsWith('/song/')));
  } catch { return false; }
}

function allowedPermission(permission, origin, requestingUrl) {
  if (permission !== 'gamepad' && permission !== 'fullscreen') return false;
  try { return new URL(requestingUrl).origin === origin; } catch { return false; }
}

function start({ app, BrowserWindow, ipcMain, session }, io = process) {
  let win = null, origin = null, launched = false, loaded = false, ended = false;
  let pending = [], buffer = '';
  const send = message => {
    if (!ended && message && typeof message === 'object' && !Array.isArray(message)) {
      try { io.stdout.write(JSON.stringify(message) + '\n'); } catch { stop(); }
    }
  };
  const stop = () => {
    if (ended) return;
    ended = true; pending = [];
    if (win && !win.isDestroyed()) win.destroy();
    app.quit();
  };
  const fromPage = e => win && !win.isDestroyed() && e.sender === win.webContents &&
    e.senderFrame === win.webContents.mainFrame;
  ipcMain.on('ccp:message', (e, message) => { if (fromPage(e)) send(message); });
  ipcMain.on('ccp:audio-unlocked', e => { if (fromPage(e)) win.webContents.setAudioMuted(false); });
  io.stdin.setEncoding('utf8');
  io.stdin.on('error', stop);
  io.stdout.on('error', stop);
  io.stdin.on('end', stop);
  app.on('window-all-closed', stop);

  async function open(url) {
    await app.whenReady();
    if (ended) return;
    const ses = session.fromPartition('glassroad-race', { cache: false });
    ses.setPermissionRequestHandler((contents, permission, callback, details) =>
      callback(contents === win?.webContents && allowedPermission(permission, origin, details?.requestingUrl || contents.getURL())));
    ses.setPermissionCheckHandler((contents, permission, requestingOrigin) =>
      contents === win?.webContents && allowedPermission(permission, origin, requestingOrigin));
    ses.webRequest.onBeforeRequest({ urls: ['<all_urls>'] }, (details, callback) =>
      callback({ cancel: !allowedRequest(details.url, origin) }));
    win = new BrowserWindow({
      width: 1280, height: 800, show: true,
      webPreferences: { session: ses, preload: path.join(__dirname, 'preload.js'),
        contextIsolation: true, sandbox: true, nodeIntegration: false, webviewTag: false, devTools: false },
    });
    const web = win.webContents;
    web.setAudioMuted(true);
    web.setWindowOpenHandler(() => ({ action: 'deny' }));
    for (const event of ['will-navigate', 'will-frame-navigate', 'will-redirect'])
      web.on(event, e => e.preventDefault());
    web.on('will-attach-webview', e => e.preventDefault());
    web.on('render-process-gone', stop);
    web.on('before-input-event', (e, input) => {
      if (input.key !== 'Escape') return;
      e.preventDefault();
      if (input.type === 'keyDown' && !input.isAutoRepeat) send({ type: 'escape' });
    });
    web.on('did-finish-load', () => {
      loaded = true;
      for (const message of pending) web.send('ccp:host-message', message);
      pending = [];
    });
    win.on('closed', stop);
    try { await win.loadURL(url.href); } catch { stop(); }
  }

  function line(raw) {
    let message;
    try { message = JSON.parse(raw); } catch { return; }
    if (!message || typeof message !== 'object' || Array.isArray(message)) return;
    if (!launched) {
      const url = message.type === 'launch' && launchUrl(message.url);
      if (!url) { stop(); return; }
      launched = true; origin = url.origin;
      open(url).catch(stop);
    } else if (message.type === 'shell-close') stop();
    else if (typeof message.type === 'string') {
      if (loaded && win && !win.isDestroyed()) win.webContents.send('ccp:host-message', message);
      else if (pending.length < 256) pending.push(message);
      else stop();
    }
  }
  io.stdin.on('data', chunk => {
    buffer += chunk;
    let i;
    while (!ended && (i = buffer.indexOf('\n')) !== -1) {
      if (i > MAX_LINE) { stop(); return; }
      const raw = buffer.slice(0, i); buffer = buffer.slice(i + 1); line(raw);
    }
    if (buffer.length > MAX_LINE) stop();
  });
}

if (require.main === module) start(require('electron'));
module.exports = { launchUrl, allowedRequest, allowedPermission, start };
