// dev/shots.mjs - screenshot tour for look reviews over CDP (Edge or Chrome), no npm deps (Node 22+ WebSocket).
// Run: node dev/shots.mjs [--out <dir>] [--only webgpu|webgl2] [--quality high|mid|phone] [--size 1280x720]
//        [--tier] [--pick <substring>] [--opts '{"showFps":true}'] [--eval <page expression>] [--flags '&gentle=1'] [--browser <exe>]
// Opens ?bot=1 (the AI drives the player), shoots the start grid, then racing moments (a boost, a bump,
// mid-race), then holds the dev peek camera (boot.js window.__glassroad.peek) at fixed spots on the lap:
// each road section, the spiral gates, a billboard, the sky halo, the screen tunnel, the colossi and the
// floating props. --tier shoots only the start grid and a mid-race shot (quality tier checks).
// Screenshots only (Page.captureScreenshot), never a pixel readback. Kills only the processes it started.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const OUT = arg('out', 'C:/wt-glassroad-run/shots/p3');
const ONLY = arg('only', null);
const QUALITY = arg('quality', 'high');
const [W, H] = arg('size', '1280x720').split('x').map(Number);
const FLAGS = arg('flags', '');
const TIER = process.argv.includes('--tier');
const EVAL = arg('eval', '');   // dev: a page expression run before the tour (window.__glassroad.world is the world under ?bot=1)
const OPTS = arg('opts', '');   // the options record (hud/options.js), e.g. '{"showFps":true}'
const PICK = arg('pick', '');   // only the tour shots whose name has this substring, no racing moments
const BROWSER = arg('browser', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
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

/** The peek tour: name and peek record from the page's own layout numbers. */
function tour(st) {
  const L = st.lengthM, out = [];
  for (const sec of st.sections || []) {
    const len = ((sec.toS - sec.fromS) + L) % L || L;
    for (const k of [0.2, 0.55, 0.85]) out.push(['road-' + sec.palette + '-' + sec.id + '-' + Math.round(k * 100), { s: sec.fromS + len * k, backM: 7, upM: 2.6, aheadM: 30, liftM: 0 }]);
  }
  (st.gates || []).forEach((g, i) => {
    out.push(['gate-' + i + '-approach', { s: g.s, backM: 32, upM: 2.4, aheadM: 32, liftM: 3 }]);
    out.push(['gate-' + i + '-through', { s: g.s, backM: 4, upM: 2.2, aheadM: 30, liftM: 2 }]);
  });
  out.push(['billboard-020', { s: 0.2 * L, backM: 34, upM: 2.6, aheadM: 34, liftM: 4 }]);
  out.push(['billboard-064', { s: 0.64 * L, backM: 34, upM: 2.6, aheadM: 34, liftM: 4 }]);
  out.push(['sky-halo', { s: 0, backM: 46, upM: 5, aheadM: 46, liftM: 20 }]);
  out.push(['tunnel-approach', { s: 0.55 * L, backM: 28, upM: 2.4, aheadM: 30, liftM: 2 }]);
  out.push(['tunnel-inside', { s: 0.575 * L, backM: 3, upM: 2.4, aheadM: 30, liftM: 2 }]);
  for (const c of st.colossi || []) {
    if (!/king|knight/.test(c.id)) continue;
    out.push(['colossus-' + c.id.replace('-lod0', ''), { s: c.s, backM: 150, upM: 6, aheadM: 150, liftM: 35 }]);
    out.push(['colossus-' + c.id.replace('-lod0', '') + '-near', { s: c.s, backM: 75, upM: 2.6, aheadM: 75, liftM: 28 }]);
  }
  out.push(['props-high', { s: 0.33 * L, backM: 20, upM: 14, aheadM: 90, liftM: 2 }]);
  out.push(['props-low', { s: 0.75 * L, backM: 10, upM: 2.6, aheadM: 40, liftM: 6 }]);
  return out;
}

async function run(backend, httpPort) {
  const dbg = await freePort();
  const profile = mkdtempSync(join(tmpdir(), 'glassroad-shots-'));
  const browser = spawn(BROWSER, ['--headless=new', '--mute-audio', '--remote-debugging-port=' + dbg, '--user-data-dir=' + profile,
    '--enable-unsafe-webgpu', '--ignore-gpu-blocklist', '--window-size=' + W + ',' + H, '--no-first-run',
    '--no-default-browser-check', '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
  const report = { backend, quality: QUALITY, pid: browser.pid, errors: [], shots: [], seen: {} };
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
      } else if (m.method === 'Runtime.consoleAPICalled' && (m.params.type === 'error' || m.params.type === 'assert')) {
        report.errors.push('console: ' + m.params.args.map((a) => a.value !== undefined ? String(a.value) : (a.description || a.type)).join(' '));
      } else if (m.method === 'Log.entryAdded' && m.params.entry.level === 'error') {
        report.errors.push('log: ' + m.params.entry.text + (m.params.entry.url ? ' ' + m.params.entry.url : ''));
      }
    });
    await c.send('Runtime.enable', {}, sid);
    await c.send('Log.enable', {}, sid);
    await c.send('Page.enable', {}, sid);
    await c.send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false }, sid);
    await c.send('Emulation.setFocusEmulationEnabled', { enabled: true }, sid);
    if (OPTS) await c.send('Page.addScriptToEvaluateOnNewDocument', { source: `try { localStorage.setItem('glassroad.options.v1', ${JSON.stringify(OPTS)}); } catch (e) {}` }, sid);
    const flag = backend === 'webgl2' ? '&renderer=webgl2' : '';
    await c.send('Page.navigate', { url: 'http://127.0.0.1:' + httpPort + '/?bot=1&seed=7&quality=' + QUALITY + flag + FLAGS }, sid);
    const evalJs = (expression) => c.send('Runtime.evaluate', { expression, returnByValue: true }, sid);
    const state = async () => JSON.parse((await evalJs('JSON.stringify(window.__glassroad ? { ...window.__glassroad, now: performance.now() } : null)')).result.value || 'null');
    const tag = backend + (QUALITY !== 'high' ? '-' + QUALITY : '');
    const shot = async (name) => {
      const r = await c.send('Page.captureScreenshot', { format: 'png' }, sid);
      const file = join(OUT, tag + '-' + name + '.png').replace(/\\/g, '/');
      writeFileSync(file, Buffer.from(r.data, 'base64'));
      report.shots.push(file);
    };
    const until = async (fn, ms) => {
      const end = Date.now() + ms;
      while (Date.now() < end) { const s = await state(); if (s && fn(s)) return s; await sleep(80); }
      return null;
    };
    const st0 = await until((s) => s.phase === 'countdown', 60000);
    if (!st0) throw new Error('no countdown');
    await sleep(400); await shot('start-grid');
    await until((s) => s.phase === 'racing', 10000);
    // racing moments: a boost, a bump and a mid-race view (props in the chase view)
    let boost = false, bump = false, mid = false;
    const end = Date.now() + (TIER ? 16000 : 45000);
    while (!PICK && Date.now() < end && !(mid && (TIER || (boost && bump)))) {
      const s = await state();
      if (!s) { await sleep(100); continue; }
      if (!boost && !TIER && s.boost > 0 && s.timeS > 2) { boost = true; await sleep(180); await shot('boost'); continue; }
      if (!bump && !TIER && s.bumpMeAt && s.now - s.bumpMeAt < 250) { bump = true; report.bumpDv = s.bumpDv; await sleep(90); await shot('bump'); continue; }
      if (!mid && s.timeS > (TIER ? 10 : 14)) { mid = true; await shot('mid-race'); continue; }
      await sleep(60);
    }
    report.seen = { boost, bump, mid };
    if (!TIER) {
      const st = await state();
      report.layout = { lengthM: st.lengthM, gates: st.gates, sections: st.sections, colossi: st.colossi, art: st.art, mediaSlots: st.mediaSlots };
      if (EVAL) report.eval = (await c.send("Runtime.evaluate", { expression: EVAL, returnByValue: true, awaitPromise: true }, sid)).result;
      for (const [name, pk] of tour(st).filter(([n]) => !PICK || n.includes(PICK))) {
        await evalJs('window.__glassroad.peek = ' + JSON.stringify(pk) + ', true');
        await sleep(700);
        await shot(name);
      }
      await evalJs('window.__glassroad.peek = null, true');
    }
    const fin = await state();
    report.final = fin && { fps: fin.fps, tier: fin.tier, drawCalls: fin.drawCalls, triangles: fin.triangles, art: fin.art };
    try { await c.send('Browser.close'); } catch (e) { /* closing */ }
  } catch (e) {
    report.errors.push('harness: ' + e.message);
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
