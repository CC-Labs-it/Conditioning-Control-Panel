// dev/perf-check.mjs - headless frame COST check over CDP (Edge or Chrome), no npm deps (Node 22+ WebSocket).
// Run: node dev/perf-check.mjs [--seconds 20] [--quality high] [--only webgpu|webgl2] [--size 1280x720]
//        [--opts '{"renderScale":1}'] [--browser <exe>] [--flags '&art=0'] [--label before]
// The browser runs with vsync and the frame rate limit off, so fps is uncapped (GPU plus CPU bound).
// A wrapper around requestAnimationFrame (injected before the page loads) times every frame callback:
// CPU frame ms is the work inside the callback, the gap between callbacks gives the uncapped fps.
// ?bot=1 with 8 karts on the given quality tier; the check presses the Race door, records the countdown
// (worst gap and worst 250 ms window: the first second hitch), then measures `--seconds` of racing.
// Draw calls and triangles come from window.__glassroad (renderer.stats(); per frame when it says so,
// otherwise the delta of the running totals over the frames between two reads). --opts writes the
// options record (hud/options.js) into localStorage before boot. Kills only the processes it started.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const SECONDS = Number(arg('seconds', 20));
const QUALITY = arg('quality', 'high');
const ONLY = arg('only', null);
const [W, H] = arg('size', '1280x720').split('x').map(Number);
const OPTS = arg('opts', '');
const FLAGS = arg('flags', '');
const LABEL = arg('label', '');
const PROFILE = process.argv.includes('--profile');   // top self-time functions over the race window
const BROWSER = arg('browser', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const HERE = fileURLToPath(new URL('.', import.meta.url));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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

// runs in the page before any module: times every rAF callback, summed per animation frame (every
// callback of one frame gets the same timestamp, so the HUD's own rAF users fold into their frame)
const PROBE = `(() => {
  const raf = window.requestAnimationFrame.bind(window);
  const P = window.__perf = { on: false, t: -1, last: 0, work: [], gaps: [], at: [] };
  window.requestAnimationFrame = (cb) => raf((t) => {
    const a = performance.now();
    if (t !== P.t) {
      P.t = t;
      if (P.on) { if (P.last) { P.gaps.push(a - P.last); P.at.push(a); } P.work.push(0); }
      P.last = a;
    }
    cb(t);
    if (P.on && P.work.length) P.work[P.work.length - 1] += performance.now() - a;
  });
  P.start = () => { P.on = true; P.last = 0; P.work = []; P.gaps = []; P.at = []; };
  P.take = () => { const r = { work: P.work, gaps: P.gaps, at: P.at }; P.on = false; P.work = []; P.gaps = []; P.at = []; return r; };
})();`;

const pct = (a, p) => { if (!a.length) return 0; const s = [...a].sort((x, y) => x - y); return s[Math.min(s.length - 1, Math.ceil(s.length * p) - 1)]; };
const r1 = (x) => Math.round(x * 100) / 100;

/** Worst window of `ms` (lowest fps) over gap timestamps. */
function worstWindow(gaps, at, ms) {
  let worst = Infinity, j = 0;
  for (let i = 0; i < at.length; i++) {
    while (at[i] - at[j] > ms) j++;
    if (at[i] - at[0] < ms) continue;
    const n = i - j + 1, span = at[i] - at[j] + gaps[j];
    worst = Math.min(worst, n * 1000 / span);
  }
  return Number.isFinite(worst) ? Math.round(worst) : 0;
}

/** Self time per function (ms), the top 25, from a CDP sampling profile. */
function topSelf(p) {
  const self = new Map(), byId = new Map(p.nodes.map((n) => [n.id, n]));
  const dts = p.timeDeltas || [];
  for (let i = 0; i < p.samples.length; i++) {
    const n = byId.get(p.samples[i]); if (!n) continue;
    const cf = n.callFrame, key = (cf.functionName || '(anon)') + ' ' + (cf.url || '').split('/').slice(-2).join('/') + ':' + (cf.lineNumber + 1);
    self.set(key, (self.get(key) || 0) + (dts[i] || 0) / 1000);
  }
  return [...self].sort((x, y) => y[1] - x[1]).slice(0, 25).map(([k, v]) => k + ' ' + r1(v));
}

async function run(backend, httpPort) {
  const dbg = await freePort();
  const profile = mkdtempSync(join(tmpdir(), 'glassroad-perf-'));
  const browser = spawn(BROWSER, ['--headless=new', '--remote-debugging-port=' + dbg, '--user-data-dir=' + profile,
    '--enable-unsafe-webgpu', '--ignore-gpu-blocklist', '--disable-gpu-vsync', '--disable-frame-rate-limit',
    '--window-size=' + W + ',' + H, '--no-first-run', '--no-default-browser-check', '--autoplay-policy=no-user-gesture-required',
    'about:blank'], { stdio: 'ignore' });
  const rep = { backend, label: LABEL, quality: QUALITY, size: W + 'x' + H, opts: OPTS || null, errors: [] };
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
        rep.errors.push('exception: ' + ((d.exception && d.exception.description) || d.text));
      } else if (m.method === 'Runtime.consoleAPICalled' && (m.params.type === 'error' || m.params.type === 'assert')) {
        rep.errors.push('console: ' + m.params.args.map((a) => a.value !== undefined ? String(a.value) : (a.description || a.type)).join(' '));
      }
    });
    await c.send('Runtime.enable', {}, sid);
    await c.send('Page.enable', {}, sid);
    await c.send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false }, sid);
    await c.send('Emulation.setFocusEmulationEnabled', { enabled: true }, sid);
    let probe = PROBE;
    if (OPTS) probe += `\ntry { localStorage.setItem('glassroad.options.v1', ${JSON.stringify(OPTS)}); } catch (e) {}`;
    await c.send('Page.addScriptToEvaluateOnNewDocument', { source: probe }, sid);
    const flag = backend === 'webgl2' ? '&renderer=webgl2' : '';
    const t0 = Date.now();
    await c.send('Page.navigate', { url: 'http://127.0.0.1:' + httpPort + '/?bot=1&seed=7&quality=' + QUALITY + flag + FLAGS }, sid);
    const ev = async (expr) => (await c.send('Runtime.evaluate', { expression: expr, returnByValue: true }, sid)).result.value;
    const state = async () => JSON.parse((await ev('JSON.stringify(window.__glassroad || null)')) || 'null');
    let st = null;
    for (let end = Date.now() + 40000; Date.now() < end; await sleep(150)) { st = await state(); if (st && (st.phase === 'menu' || st.phase === 'failed')) break; }
    if (!st || st.phase !== 'menu') throw new Error('never reached the menu (' + (st && st.phase) + ')');
    rep.bootS = r1((Date.now() - t0) / 1000);
    await sleep(1500);
    // the countdown: from the Race door to 3 s into the race
    if (PROFILE) { await c.send('Profiler.enable', {}, sid); await c.send('Profiler.setSamplingInterval', { interval: 200 }, sid); await c.send('Profiler.start', {}, sid); }
    await ev('window.__perf.start(), true');
    await ev("(document.querySelector('#card [data-act=race]') || {}).click && document.querySelector('#card [data-act=race]').click(), true");
    for (let end = Date.now() + 15000; Date.now() < end; await sleep(100)) { st = await state(); if (st.phase === 'racing' && st.timeS > 1) break; }
    const cd = await ev('window.__perf.take()');
    if (PROFILE) rep.profileCountdown = topSelf((await c.send('Profiler.stop', {}, sid)).profile);
    // the worst gaps of the countdown, with the work inside the frame that ended them
    rep.countdownWorst = cd.gaps.map((g, i) => [r1(g), r1(cd.work[i + 1] || 0), r1(cd.at[i] - (cd.at[0] || 0))]).sort((a, b) => b[0] - a[0]).slice(0, 6);
    rep.countdown = { frames: cd.gaps.length, worstGapMs: r1(Math.max(0, ...cd.gaps)), worst250msFps: worstWindow(cd.gaps, cd.at, 250),
      worst1sFps: worstWindow(cd.gaps, cd.at, 1000), p95WorkMs: r1(pct(cd.work, 0.95)), maxWorkMs: r1(Math.max(0, ...cd.work)) };
    await sleep(2000);
    // the race: SECONDS of measured frames, draw stats every second
    if (PROFILE) { await c.send('Profiler.enable', {}, sid); await c.send('Profiler.setSamplingInterval', { interval: 200 }, sid); await c.send('Profiler.start', {}, sid); }
    await ev('window.__perf.start(), true');
    const stats = [];
    let prev = null;
    for (let end = Date.now() + SECONDS * 1000; Date.now() < end;) {
      await sleep(1000);
      const s = await state();
      const n = await ev('window.__perf.gaps.length');
      if (s) stats.push({ dc: s.drawCalls, tri: s.triangles, n, gpu: s.gpu, tier: s.tier, scale: s.renderScale, phase: s.phase });
      if (s && s.phase === 'failed') break;
      prev = s;
    }
    const m = await ev('window.__perf.take()');
    if (PROFILE) rep.profile = topSelf((await c.send('Profiler.stop', {}, sid)).profile);
    // three's info counters reset per render pass chain, so each read is one frame
    const dc = pct(stats.map((x) => x.dc || 0), 0.5), tri = pct(stats.map((x) => x.tri || 0), 0.5);
    const fpsGap = pct(m.gaps, 0.5);
    rep.race = {
      frames: m.work.length, cpuMedianMs: r1(pct(m.work, 0.5)), cpuP95Ms: r1(pct(m.work, 0.95)),
      gapMedianMs: r1(fpsGap), gapP95Ms: r1(pct(m.gaps, 0.95)), fpsUncapped: fpsGap ? Math.round(1000 / fpsGap) : 0,
      fpsMean: m.gaps.length ? Math.round(m.gaps.length * 1000 / m.gaps.reduce((x, y) => x + y, 0)) : 0,
      drawCalls: Math.round(dc), triangles: Math.round(tri), gpu: stats.length ? stats[stats.length - 1].gpu : null, tier: prev && prev.tier, renderScale: prev && prev.renderScale, phase: prev && prev.phase,
    };
    try { await c.send('Browser.close'); } catch (e) { /* closing */ }
  } catch (e) {
    rep.errors.push('harness: ' + e.message);
  } finally {
    if (c) c.close();
    await sleep(800);
    killTree(browser.pid);
    await sleep(300);
    try { rmSync(profile, { recursive: true, force: true }); } catch (e) { /* still held */ }
  }
  return rep;
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
console.log(JSON.stringify(reports, null, 1));
process.exitCode = reports.some((r) => r.errors.length) ? 1 : 0;
