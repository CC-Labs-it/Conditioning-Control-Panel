// rt-linux-spike/electron-main.js - one URL in a bare Electron window: the "our own Chromium for the game" candidate
// (download on first race). `electron electron-main.js <url>`; SPIKE_WEBGPU=1 adds the Linux WebGPU switches,
// SPIKE_SIZE=1920x1080, SPIKE_INFO=<file> gets the engine facts. Quits when the page title says SPIKE-DONE.
const { app, BrowserWindow } = require('electron');
const fs = require('fs');

const url = process.argv.find((a) => a.startsWith('http://127.0.0.1'));
const [w, h] = (process.env.SPIKE_SIZE || '1920x1080').split('x').map(Number);
app.commandLine.appendSwitch('ignore-gpu-blocklist');
app.commandLine.appendSwitch('autoplay-policy', 'no-user-gesture-required');
if (process.env.SPIKE_WEBGPU === '1') {
  app.commandLine.appendSwitch('enable-unsafe-webgpu');
  app.commandLine.appendSwitch('enable-features', 'Vulkan,WebGPU');
}
const info = { electron: process.versions.electron, chrome: process.versions.chrome, webgpuSwitches: process.env.SPIKE_WEBGPU === '1',
  session: process.env.XDG_SESSION_TYPE || null, renderGone: null, timedOut: false };
let done = false;
function finish() {
  if (done) return; done = true;
  try { if (process.env.SPIKE_INFO) fs.writeFileSync(process.env.SPIKE_INFO, JSON.stringify(info, null, 1)); } catch (e) { /* best effort */ }
  app.quit();
}
app.whenReady().then(async () => {
  info.gpuFeatures = app.getGPUFeatureStatus();
  try { const g = await app.getGPUInfo('basic'); info.gpuDevices = g.gpuDevice; info.glRenderer = g.auxAttributes && g.auxAttributes.glRenderer; } catch (e) { info.gpuInfo = String(e); }
  const win = new BrowserWindow({ width: w, height: h, x: 0, y: 0, backgroundColor: '#0b0a12', title: 'Racing Thoughts spike (Electron)',
    webPreferences: { backgroundThrottling: false, contextIsolation: true, sandbox: true } });
  win.webContents.setAudioMuted(true);   // every test browser starts muted (owner rule)
  win.webContents.on('page-title-updated', (_e, t) => { if (t === 'SPIKE-DONE') setTimeout(finish, 500); });
  win.webContents.on('render-process-gone', (_e, d) => { info.renderGone = d; finish(); });
  setTimeout(() => { info.timedOut = true; finish(); }, Number(process.env.SPIKE_TIMEOUT || 600) * 1000);
  win.loadURL(url);
});
app.on('window-all-closed', finish);
