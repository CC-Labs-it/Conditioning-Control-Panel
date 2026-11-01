'use strict';

// contextBridge cannot add webview to Chromium's existing window.chrome.
const { contextBridge, ipcRenderer } = require('electron');
const listeners = new Set();
ipcRenderer.on('ccp:host-message', (_event, data) => {
  for (const listener of listeners) listener({ data });
});
contextBridge.exposeInMainWorld('ccpHost', {
  postMessage: message => ipcRenderer.send('ccp:message', message),
  addEventListener: (type, listener) => {
    if (type === 'message' && typeof listener === 'function') { listeners.add(listener); ipcRenderer.send('ccp:bridge-ready'); }
  },
  removeEventListener: (type, listener) => { if (type === 'message') listeners.delete(listener); },
  audioUnlocked: () => ipcRenderer.send('ccp:audio-unlocked'),
});
