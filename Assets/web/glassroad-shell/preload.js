'use strict';
const { contextBridge, ipcRenderer } = require('electron');
const listeners = new Set();
ipcRenderer.on('ccp:host-message', (_event, data) => {
  for (const listener of listeners) listener({ data });
});
contextBridge.exposeInMainWorld('ccpRaceBridge', {
  postMessage: message => ipcRenderer.send('ccp:message', message),
  addEventListener: (type, listener) => {
    if (type === 'message' && typeof listener === 'function') {
      listeners.add(listener); ipcRenderer.send('ccp:bridge-ready');
    }
  },
  removeEventListener: (type, listener) => { if (type === 'message') listeners.delete(listener); },
});
// Chromium already owns window.chrome; extend it in the main world.
contextBridge.executeInMainWorld({ func: () => { window.chrome.webview = window.ccpRaceBridge; } });
