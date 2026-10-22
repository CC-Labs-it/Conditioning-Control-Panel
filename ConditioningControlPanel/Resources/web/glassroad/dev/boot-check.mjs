// dev/boot-check.mjs - headless boot check over CDP (Edge or Chrome), no npm deps (Node 22+ WebSocket).
// Run: node dev/boot-check.mjs [--out <dir>] [--seconds 60] [--browser <exe>] [--only webgpu|webgl2] [--finish] [--cards] [--flags '&gentle=1']
// For each backend it serves this tree, opens ?bot=1&botFall=1, collects console errors and
// exceptions, logs fps / draw calls / triangles from window.__glassroad, and saves screenshots:
// start, mid-race, during the rescue after the bot's planned drop, and the end. Screenshots only
// (Page.captureScreenshot), never a pixel readback. Kills only the processes it started, by PID.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const OUT = arg('out', 'C:/wt-glassroad-run/shots');
const SECONDS = Number(arg('seconds', 60));
const BROWSER = arg('browser', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const ONLY = arg('only', null);
const FLAGS = arg('flags', '');   // extra url flags, e.g. '&gentle=1&feel=1'
const CARDS = process.argv.includes('--cards');     // no bot: start card, Enter, Escape pause card, resume
const FINISH = process.argv.includes('--finish');   // run on to the finish card (3 laps, about 2.5 min)
const HERE = fileURLToPath(new URL('.', import.meta.url));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
mkdirSync(OUT, { recursive: true });

function freePort() {
  return new Promise((res, rej) => {
    const s = createServer(); s.unref(); s.on('error', rej);
    s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); });
  });
}
function alive(pid) {
  try { return execFileSync('tasklist', ['/FI', 'PID eq ' + pid, '/NH'], { encoding: 'utf8' }).includes(' ' + pid + ' '); }
  catch (e) { return false; }
}
function killTree(pid) {
  if (!alive(pid)) return;
  try { execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' }); } catch (e) { /* gone */ }
}

async function cdp(wsUrl) {
  const ws = new WebSocket(wsUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = () => rej(new Error('cdp socket')); });
  let id = 0;
  const wait = new Map(), subs = [];
  ws.onmessage = (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && wait.has(m.id)) { const w = wait.get(m.id); wait.delete(m.id); m.error ? w.rej(new Error(m.error.message)) : w.res(m.result); }
    else if (m.method) for (const fn of subs) fn(m);
  };
  return {
    send(method, params = {}, sessionId) {
      const msg = { id: ++id, method, params };
      if (sessionId) msg.sessionId = sessionId;
      ws.send(JSON.stringify(msg));
      return new Promise((res, rej) => wait.set(msg.id, { res, rej }));
    },
    on(fn) { subs.push(fn); },
    close() { try { ws.close(); } catch (e) { /* closed */ } },
  };
}

async function run(backend, httpPort) {
  const dbg = await freePort();
  const profile = mkdtempSync(join(tmpdir(), 'glassroad-edge-'));
  const browser = spawn(BROWSER, ['--headless=new', '--remote-debugging-port=' + dbg, '--user-data-dir=' + profile,
    '--enable-unsafe-webgpu', '--ignore-gpu-blocklist', '--window-size=1280,720', '--no-first-run',
    '--no-default-browser-check', '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
  const report = { backend, pid: browser.pid, errors: [], warnings: [], samples: [], shots: [] };
  let c = null;
  try {
    let ver = null;
    for (let i = 0; i < 100 && !ver; i++) {
      try { ver = await (await fetch('http://127.0.0.1:' + dbg + '/json/version')).json(); } catch (e) { await sleep(150); }
    }
    if (!ver) throw new Error('no devtools endpoint');
    c = await cdp(ver.webSocketDebuggerUrl);
    const { targetId } = await c.send('Target.createTarget', { url: 'about:blank' });
    const { sessionId: sid } = await c.send('Target.attachToTarget', { targetId, flatten: true });
    c.on((m) => {
      if (m.sessionId !== sid) return;
      if (m.method === 'Runtime.exceptionThrown') {
        const d = m.params.exceptionDetails;
        report.errors.push('exception: ' + ((d.exception && d.exception.description) || d.text));
      } else if (m.method === 'Runtime.consoleAPICalled') {
        const text = m.params.args.map((a) => a.value !== undefined ? String(a.value) : (a.description || a.type)).join(' ');
        if (m.params.type === 'error' || m.params.type === 'assert') report.errors.push('console: ' + text);
        else if (m.params.type === 'warning') report.warnings.push(text);
      } else if (m.method === 'Log.entryAdded' && m.params.entry.level === 'error') {
        report.errors.push('log: ' + m.params.entry.text + (m.params.entry.url ? ' ' + m.params.entry.url : ''));
      }
    });
    await c.send('Runtime.enable', {}, sid);
    await c.send('Log.enable', {}, sid);
    await c.send('Page.enable', {}, sid);
    await c.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false }, sid);
    await c.send('Emulation.setFocusEmulationEnabled', { enabled: true }, sid);
    const flag = backend === 'webgl2' ? '&renderer=webgl2' : '';
    const query = CARDS ? '/?x=1' : '/?bot=1&botFall=1';
    await c.send('Page.navigate', { url: 'http://127.0.0.1:' + httpPort + query + flag + FLAGS }, sid);
    const state = async () => {
      const r = await c.send('Runtime.evaluate', { expression: 'JSON.stringify(window.__glassroad || null)', returnByValue: true }, sid);
      return JSON.parse(r.result.value || 'null');
    };
    const shot = async (name) => {
      const r = await c.send('Page.captureScreenshot', { format: 'png' }, sid);
      const file = join(OUT, backend + '-' + name + '.png').replace(/\\/g, '/');
      writeFileSync(file, Buffer.from(r.data, 'base64'));
      report.shots.push(file);
    };
    const key = async (k, code, vk) => {
      for (const type of ['keyDown', 'keyUp']) await c.send('Input.dispatchKeyEvent', { type, key: k, code, windowsVirtualKeyCode: vk }, sid);
    };
    const until = async (want, ms) => {
      const end = Date.now() + ms;
      while (Date.now() < end) { const s = await state(); if (s && want.includes(s.phase)) return s; await sleep(150); }
      report.errors.push('cards: phase never reached ' + want.join('|'));
      return null;
    };
    if (CARDS) {   // no bot: start card, a key starts, Escape pauses, a key resumes
      if (await until(['ready'], 30000)) await shot('card-start');
      await key('Enter', 'Enter', 13);
      if (await until(['countdown', 'racing'], 5000)) await sleep(2500);
      await key('Escape', 'Escape', 27);
      if (await until(['paused'], 3000)) { await sleep(400); await shot('card-pause'); }
      await key('Enter', 'Enter', 13);
      await until(['racing'], 3000);
      report.final = await state();
      throw null;   // done: skip the bot loop
    }
    const t0 = Date.now();
    let st = null, startShot = false, midShot = false, dropAt = 0, dropShot = false, finishAt = 0;
    while (Date.now() - t0 < SECONDS * 1000) {
      await sleep(250);
      st = await state();
      if (!st) continue;
      if (st.phase === 'failed') break;
      if (!startShot && (st.phase === 'countdown' || st.phase === 'racing')) { startShot = true; await sleep(400); await shot('start'); }
      if (!midShot && st.phase === 'racing' && st.timeS > 12) { midShot = true; await shot('mid-race'); }
      if (FINISH && st.phase === 'finished' && !finishAt) finishAt = Date.now();
      if (finishAt && Date.now() - finishAt > 2500) { await shot('finish'); break; }
      if (!dropAt && st.drops > 0) dropAt = Date.now();
      if (dropAt && !dropShot && Date.now() - dropAt > 700) { dropShot = true; await shot('rescue'); }
      if ((Date.now() - t0) % 5000 < 250 && st.fps) report.samples.push({ t: Math.round((Date.now() - t0) / 1000), fps: st.fps, drawCalls: st.drawCalls, triangles: st.triangles, tier: st.tier, phase: st.phase });
      if (!FINISH && dropShot && st.rescues > 0 && Date.now() - dropAt > 4000) break;
    }
    if (st) await shot('end');
    report.final = st && { phase: st.phase, backend: st.backend, tier: st.tier, fps: st.fps, drawCalls: st.drawCalls,
      triangles: st.triangles, lap: st.lap, s: Math.round(st.s), drops: st.drops, rescues: st.rescues, laps: st.laps, events: st.events };
    try { await c.send('Browser.close'); } catch (e) { /* closing */ }
  } catch (e) {
    if (e) report.errors.push('harness: ' + e.message);
  } finally {
    if (c) c.close();
    await sleep(800);
    killTree(browser.pid);
    await sleep(300);
    report.leftAlive = alive(browser.pid);
    try { rmSync(profile, { recursive: true, force: true }); } catch (e) { /* edge still holds a file */ }
  }
  return report;
}

const httpPort = await freePort();
const server = spawn(process.execPath, [join(HERE, 'serve.mjs'), String(httpPort)], { stdio: 'ignore' });
const reports = [];
try {
  for (let i = 0; i < 40; i++) { try { await fetch('http://127.0.0.1:' + httpPort + '/'); break; } catch (e) { await sleep(100); } }
  for (const b of ['webgpu', 'webgl2']) if (!ONLY || ONLY === b) reports.push(await run(b, httpPort));
} finally {
  server.kill();
  await sleep(200);
  killTree(server.pid);
}
const leftover = [server.pid, ...reports.map((r) => r.pid)].filter(alive);
console.log(JSON.stringify({ reports, leftover }, null, 1));
process.exitCode = reports.some((r) => r.errors.length || r.leftAlive) || leftover.length ? 1 : 0;
