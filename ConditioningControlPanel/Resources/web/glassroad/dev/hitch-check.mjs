// dev/hitch-check.mjs - frame HITCH check over a full ?bot=1 race: every frame time, not the average.
// Run (from glassroad/): node dev/hitch-check.mjs [--browser firefox|edge|chrome|<exe>] [--renderer webgl2|default]
//        [--quality high] [--port 8825] [--chart <url>|0] [--max-seconds 330] [--size 1280x720] [--label before]
//        [--out file.json] [--flags '&x=1']
// Firefox runs over WebDriver BiDi (media.volume_scale 0, timer precision unclamped); Edge / Chrome over CDP with
// --mute-audio. The browser keeps its vsync, so a smooth frame is one display interval and a spike is a hitch.
// A probe injected before the page loads wraps requestAnimationFrame and times every animation frame (gap since
// the last one, work inside the callbacks), and wraps the WebGL2 calls that stall: program links (linkProgram,
// compileShader, the LINK_STATUS / COMPILE_STATUS queries), texture and buffer uploads, draws. The page runs with
// ?hitch=1, so boot.js fills window.__hitch with the frame's section times (sim, fx, audio, cam, world, render,
// hud). Chrome also reads the JS heap per frame (a drop is a GC inside that frame).
// Output: p50/p95/p99/max gap, frames over 25 and 50 ms (racing and countdown apart), and a timeline of every
// spike over 25 ms with what it held: phase, lap, s, song time, tier, sections, GL buckets, GC, tier steps.
// Kills only the processes it started. Serves glassroad on --port unless a server already answers there.
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { summarize, spikesOf, attribute } from './hitchMath.mjs';

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const BROWSERS = {
  firefox: 'C:/Program Files/Mozilla Firefox/firefox.exe',
  edge: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  chrome: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
};
const B = arg('browser', 'firefox');
const EXE = BROWSERS[B] || B;
const FIREFOX = /firefox/i.test(EXE);
const RENDERER = arg('renderer', 'webgl2');
const QUALITY = arg('quality', '');
const PORT = Number(arg('port', 8825));
const CHART = arg('chart', '/private-audio/pink-road/chart.json');
const MAXS = Number(arg('max-seconds', 330));
const [W, H] = arg('size', '1280x720').split('x').map(Number);
const LABEL = arg('label', '');
const OUT = arg('out', '');
const FLAGS = arg('flags', '');
const HEAP = process.argv.includes('--heap');   // Chromium: sampled allocations over the race, top sites by bytes
const HERE = fileURLToPath(new URL('.', import.meta.url));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// the watchdog: a hung socket or browser never outlives the run (the shared browser lock waits on this process)
let spawned = null;
const dog = setTimeout(() => { console.error('hitch-check: watchdog, killing the browser'); if (spawned) killTree(spawned.pid); process.exit(2); }, (MAXS + 150) * 1000);

// runs in the page before any module
const PROBE = `(() => {
  const now = () => performance.now();
  const F = window.__hf = { on: false, rows: [], last: 0, t: -1, gl: null, cur: null };
  const blank = () => ({ link: 0, linkN: 0, up: 0, upN: 0, upBytes: 0, draw: 0, drawN: 0, other: 0, big: [] });
  F.gl = blank();
  const raf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = (cb) => raf((t) => {
    const a = now();
    if (t !== F.t) {
      F.t = t;
      if (F.on && F.last) {
        // close the previous frame: its gap ends here (the GL work inside it already counted)
        const r = F.cur;
        if (r) { r.gap = a - F.last; F.rows.push(r); }
      }
      F.last = a;
      if (F.on) {
        const d = window.__glassroad || {}, h = window.__hitch || null, m = performance.memory;
        F.cur = { at: a, work: 0, phase: d.phase, lap: d.lap, s: d.s, song: d.song ? d.song.t : null, mr: d.song ? [d.song.section, d.song.calm, d.song.helix, d.song.big, d.song.shatter] : null, tier: d.tier, scale: d.renderScale,
          heap: m ? m.usedJSHeapSize : 0, gl: F.gl = blank(), h: null, hr: h };
      }
    }
    cb(t);
    if (F.on && F.cur) {
      F.cur.work += now() - a;
      const h = window.__hitch;
      if (h) F.cur.h = { sim: h.sim, fx: h.fx, audio: h.audio, cam: h.cam, world: h.world, render: h.render, hud: h.hud, steps: h.steps };
    }
  });
  F.errs = [];
  window.addEventListener('error', (e) => { if (F.errs.length < 20) F.errs.push('error: ' + String(e.message).slice(0, 300)); });
  const ce = console.error.bind(console);
  console.error = (...a) => { if (F.errs.length < 20) F.errs.push('console: ' + a.map(String).join(' ').slice(0, 300)); ce(...a); };
  F.start = () => { F.on = true; F.rows = []; F.last = 0; F.cur = null; };
  F.take = (from) => F.rows.slice(from || 0).map((r) => ({ ...r, hr: undefined }));
  const P = window.WebGL2RenderingContext && WebGL2RenderingContext.prototype;
  const kinds = {
    link: ['linkProgram', 'compileShader', 'getProgramParameter', 'getShaderParameter', 'validateProgram'],
    up: ['texImage2D', 'texSubImage2D', 'texImage3D', 'texSubImage3D', 'compressedTexImage2D', 'compressedTexSubImage2D', 'texStorage2D', 'texStorage3D', 'bufferData', 'bufferSubData', 'generateMipmap', 'copyTexSubImage2D'],
    draw: ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced', 'drawRangeElements', 'blitFramebuffer', 'clear', 'readPixels', 'clientWaitSync', 'finish', 'flush', 'invalidateFramebuffer'],
  };
  const frames = (n) => { const st = String(new Error().stack || '').split(String.fromCharCode(10)); const out = []; for (const l of st) { if (out.length >= n) break; const h = l.indexOf('://'); if (h < 0) continue; const p = l.indexOf('/', h + 3); if (p < 0) continue; const tail = l.slice(p + 1); if (tail.startsWith('vendor') || tail.startsWith('dev/') || tail.startsWith('arcademy')) continue; out.push(tail.split(')').join('')); } return out.join(' < '); };
  const who = () => '';
  const born = new WeakMap(), mk = Document.prototype.createElement;
  Document.prototype.createElement = function (t, ...r) { const el = mk.call(this, t, ...r); if (String(t).toLowerCase() === 'canvas') born.set(el, frames(2)); return el; };
  const srcOf = (a) => { for (const x of a) if (x && typeof x === 'object' && born.has(x)) return ' canvas@' + born.get(x); return ''; };
  F.links = [];
  const linkInfo = (gl, p) => { try { const sh = gl.getAttachedShaders(p) || []; const src = sh.map((x) => gl.getShaderSource(x) || ''); const fr = src.find((x) => /out (highp |mediump )?vec4/.test(x) && /gl_Position/.test(x) === false) || src[1] || ''; const vs = src.find((x) => /gl_Position/.test(x)) || src[0] || ''; return { at: Math.round(now()), phase: (window.__glassroad || {}).phase, lap: (window.__glassroad || {}).lap, flen: fr.length, vlen: vs.length, frag: (fr.match(/uniform [^;]+;/g) || []).slice(0, 14).join(' '), vin: (vs.match(/(in|attribute) [^;]+;/g) || []).slice(0, 10).join(' '), main: fr.slice(fr.indexOf('void main')).replace(/[ ]+/g, ' ').slice(0, 300), who: who() }; } catch (e) { return { err: String(e) }; } };
  const dims = (a) => { for (const x of a) if (x && typeof x === 'object' && ('width' in x)) return x.width + 'x' + x.height; const n = a.filter((x) => typeof x === 'number'); return n.length > 4 ? n[3] + 'x' + n[4] : ''; };
  const bytes = (a) => { for (const x of a) { if (x && x.byteLength) return x.byteLength; if (x && x.width && x.height) return x.width * x.height * 4; } return 0; };
  if (P) for (const k of Object.keys(kinds)) for (const name of kinds[k]) {
    const f = P[name]; if (!f) continue;
    P[name] = function (...a) {
      if (!F.on) return f.apply(this, a);
      const t0 = now(); const r = f.apply(this, a); const dt = now() - t0;
      const g = F.gl; g[k] += dt; g[k + 'N']++;
      if (k === 'up') g.upBytes += bytes(a);
      const b = k === 'up' ? bytes(a) : 0;
      if ((dt > 3 || b > 1048576) && g.big.length < 6) g.big.push(name + ' ' + dt.toFixed(1) + (k === 'up' ? ' ' + dims(a) + ' ' + Math.round(b / 1024) + 'k' + srcOf(a) : '') + who());
      if (name === 'linkProgram' && F.links.length < 60) F.links.push(linkInfo(this, a[0]));
      return r;
    };
  }
})();`;

function alive(pid) {
  try { return execFileSync('tasklist', ['/FI', 'PID eq ' + pid, '/NH'], { encoding: 'utf8' }).includes(' ' + pid + ' '); } catch (e) { return false; }
}
function killTree(pid) {
  if (!pid || !alive(pid)) return;
  try { execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' }); } catch (e) { /* gone */ }
}
function rpc(wsUrl) {
  return new Promise((res, rej) => {
    const ws = new WebSocket(wsUrl);
    let id = 0; const wait = new Map(), subs = [];
    ws.onmessage = (ev) => {
      const m = JSON.parse(ev.data);
      if (m.id && wait.has(m.id)) { const w = wait.get(m.id); wait.delete(m.id); m.error ? w.rej(new Error(w.method + ': ' + String(m.error.message || m.error) + ' ' + (m.message || ''))) : w.res(m.result); }
      else for (const fn of subs) fn(m);
    };
    ws.onerror = () => rej(new Error('socket ' + wsUrl));
    ws.onopen = () => res({
      send(method, params = {}, sessionId) {
        const msg = { id: ++id, method, params }; if (sessionId) msg.sessionId = sessionId;
        ws.send(JSON.stringify(msg));
        return new Promise((r2, j2) => wait.set(msg.id, { res: r2, rej: j2, method }));
      },
      on(fn) { subs.push(fn); },
      close() { try { ws.close(); } catch (e) { /* closed */ } },
    });
  });
}

/** A driver: { goto(url), ev(expr) -> value, errors, close() } for Firefox (BiDi) or Chromium (CDP). */
async function openFirefox() {
  const profile = mkdtempSync(join(tmpdir(), 'glassroad-hitch-ff-'));
  writeFileSync(join(profile, 'user.js'), [
    'user_pref("media.volume_scale", "0.0");', 'user_pref("media.autoplay.default", 0);', 'user_pref("privacy.reduceTimerPrecision", false);',
    'user_pref("browser.shell.checkDefaultBrowser", false);', 'user_pref("datareporting.policy.dataSubmissionEnabled", false);',
    'user_pref("toolkit.telemetry.reportingpolicy.firstRun", false);', 'user_pref("browser.aboutwelcome.enabled", false);'].join('\n'));
  const port = 9400 + Math.floor(Math.random() * 400);
  const proc = spawned = spawn(EXE, ['--headless', '--no-remote', '--profile', profile, '--remote-debugging-port=' + port, '--width=' + W, '--height=' + H], { stdio: ['ignore', 'pipe', 'pipe'] });
  await new Promise((res, rej) => {
    const t = setTimeout(() => rej(new Error('firefox never listened')), 30000);
    const watch = (d) => { if (String(d).includes('WebDriver BiDi listening')) { clearTimeout(t); res(); } };
    proc.stdout.on('data', watch); proc.stderr.on('data', watch);
  });
  const c = await rpc('ws://127.0.0.1:' + port + '/session');
  await c.send('session.new', { capabilities: {} });
  const errors = [];
  c.on((m) => { if (m.method === 'log.entryAdded' && m.params && m.params.level === 'error') errors.push(String(m.params.text).slice(0, 300)); });
  await c.send('script.addPreloadScript', { functionDeclaration: '() => { ' + PROBE + ' }' });
  // a fresh tab of our own (Firefox 158 lists chrome contexts too, which need system access)
  const ctx = (await c.send('browsingContext.create', { type: 'tab' })).context;
  await c.send('browsingContext.setViewport', { context: ctx, viewport: { width: W, height: H } });
  return {
    errors,
    async goto(url) { await c.send('browsingContext.navigate', { context: ctx, url, wait: 'none' }); },
    async ev(expr) {
      const r = await c.send('script.evaluate', { expression: expr, target: { context: ctx }, awaitPromise: false });
      if (r.type === 'exception') throw new Error('eval: ' + JSON.stringify(r.exceptionDetails && r.exceptionDetails.text));
      return r.result && r.result.value;
    },
    async close() { try { await c.send('session.end', {}); } catch (e) { /* ending */ } c.close(); await sleep(500); killTree(proc.pid); await sleep(300); try { rmSync(profile, { recursive: true, force: true }); } catch (e) { /* held */ } },
  };
}
async function openChromium() {
  const profile = mkdtempSync(join(tmpdir(), 'glassroad-hitch-cr-'));
  const dbg = 9800 + Math.floor(Math.random() * 150);
  const proc = spawned = spawn(EXE, ['--headless=new', '--mute-audio', '--remote-debugging-port=' + dbg, '--user-data-dir=' + profile,
    '--enable-unsafe-webgpu', '--ignore-gpu-blocklist', '--enable-precise-memory-info', '--window-size=' + W + ',' + H,
    '--no-first-run', '--no-default-browser-check', '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
  let ver = null;
  for (let i = 0; i < 100 && !ver; i++) { try { ver = await (await fetch('http://127.0.0.1:' + dbg + '/json/version')).json(); } catch (e) { await sleep(150); } }
  if (!ver) { killTree(proc.pid); throw new Error('no devtools endpoint'); }
  const c = await rpc(ver.webSocketDebuggerUrl);
  const { targetId } = await c.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId: sid } = await c.send('Target.attachToTarget', { targetId, flatten: true });
  const errors = [];
  c.on((m) => {
    if (m.sessionId !== sid) return;
    if (m.method === 'Runtime.exceptionThrown') { const d = m.params.exceptionDetails; errors.push('exception: ' + ((d.exception && d.exception.description) || d.text).slice(0, 300)); }
    else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errors.push('console: ' + m.params.args.map((a) => a.value !== undefined ? String(a.value) : (a.description || a.type)).join(' ').slice(0, 300));
  });
  await c.send('Runtime.enable', {}, sid);
  await c.send('Page.enable', {}, sid);
  await c.send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false }, sid);
  await c.send('Emulation.setFocusEmulationEnabled', { enabled: true }, sid);
  await c.send('Page.addScriptToEvaluateOnNewDocument', { source: PROBE }, sid);
  return {
    errors,
    async goto(url) { await c.send('Page.navigate', { url }, sid); },
    async ev(expr) { const r = await c.send('Runtime.evaluate', { expression: expr, returnByValue: true }, sid); return r.result.value; },
    async heapStart() { await c.send('HeapProfiler.enable', {}, sid); await c.send('HeapProfiler.startSampling', { samplingInterval: 16384, includeObjectsCollectedByMajorGC: true, includeObjectsCollectedByMinorGC: true }, sid); },
    async heapStop() { const r = await c.send('HeapProfiler.stopSampling', {}, sid); return topAlloc(r.profile); },
    async close() { try { await c.send('Browser.close'); } catch (e) { /* closing */ } c.close(); await sleep(800); killTree(proc.pid); await sleep(300); try { rmSync(profile, { recursive: true, force: true }); } catch (e) { /* held */ } },
  };
}

/** Bytes allocated per function (self), the top 30, from a sampled heap profile (garbage included). */
function topAlloc(p) {
  const self = new Map();
  const walk = (n) => {
    const cf = n.callFrame, key = (cf.functionName || '(anon)') + ' ' + (cf.url || '').split('/').slice(-2).join('/') + ':' + (cf.lineNumber + 1);
    const b = (n.selfSize || 0); if (b) self.set(key, (self.get(key) || 0) + b);
    for (const c of n.children || []) walk(c);
  };
  walk(p.head);
  return [...self].sort((x, y) => y[1] - x[1]).slice(0, 30).map(([k, v]) => k + ' ' + Math.round(v / 1024) + 'k');
}

let server = null;
async function serverUp() {
  try { await fetch('http://127.0.0.1:' + PORT + '/'); return true; } catch (e) { return false; }
}
if (!(await serverUp())) {
  server = spawn(process.execPath, [join(HERE, 'serve.mjs'), String(PORT)], { stdio: 'ignore' });
  for (let i = 0; i < 50 && !(await serverUp()); i++) await sleep(100);
}

const rep = { label: LABEL, browser: B, renderer: RENDERER, quality: QUALITY || 'auto', size: W + 'x' + H, chart: CHART, errors: [] };
let d = null;
try {
  d = FIREFOX ? await openFirefox() : await openChromium();
  const q = '?bot=1&seed=7&hitch=1' + (RENDERER === 'default' ? '' : '&renderer=' + RENDERER) + (QUALITY ? '&quality=' + QUALITY : '') +
    (CHART ? '&chart=' + encodeURIComponent(CHART) : '') + FLAGS;
  const t0 = Date.now();
  await d.goto('http://127.0.0.1:' + PORT + '/' + q);
  const state = async () => JSON.parse((await d.ev('JSON.stringify(window.__glassroad ? { phase: __glassroad.phase, lap: __glassroad.lap, backend: __glassroad.backend, gpu: __glassroad.gpu, tier: __glassroad.tier, chart: __glassroad.chart, shadowMode: __glassroad.shadowMode } : null)')) || 'null');
  let st = null;
  // the probe records from the menu on: the bot starts the race itself, so the countdown is in the take
  for (let end = Date.now() + 60000; Date.now() < end; await sleep(200)) {
    try { st = await state(); } catch (e) { st = null; }
    if (st && (st.phase === 'menu' || st.phase === 'countdown' || st.phase === 'failed')) break;
  }
  if (!st || st.phase === 'failed' || st.phase === 'boot') throw new Error('never reached the menu (' + (st && st.phase) + ')');
  rep.bootS = Math.round((Date.now() - t0) / 100) / 10;
  await d.ev('window.__hf.start(), true');
  if (HEAP && d.heapStart) await d.heapStart();
  const rows = [];
  let lastPhase = '', raceStart = 0;
  for (let end = Date.now() + MAXS * 1000; Date.now() < end; await sleep(2000)) {
    const chunk = await d.ev('JSON.stringify(window.__hf.take(' + rows.length + '))');
    for (const r of JSON.parse(chunk || '[]')) rows.push(r);
    st = await state();
    if (st && st.phase !== lastPhase) { lastPhase = st.phase; if (st.phase === 'racing' && !raceStart) raceStart = Date.now(); }
    if (st && (st.phase === 'results' || st.phase === 'failed')) break;
  }
  const chunk = await d.ev('JSON.stringify(window.__hf.take(' + rows.length + '))');
  for (const r of JSON.parse(chunk || '[]')) rows.push(r);
  if (HEAP && d.heapStop) rep.alloc = await d.heapStop();
  Object.assign(rep, { backend: st && st.backend, gpu: st && st.gpu, tierEnd: st && st.tier, chartLoaded: st && st.chart, shadowMode: st && st.shadowMode, endPhase: st && st.phase,
    raceS: raceStart ? Math.round((Date.now() - raceStart) / 1000) : 0 });
  try { rep.errors.push(...JSON.parse((await d.ev('JSON.stringify(window.__hf.errs)')) || '[]')); } catch (e) { /* page gone */ }
  try { rep.links = JSON.parse((await d.ev('JSON.stringify(window.__hf.links)')) || '[]'); } catch (e) { /* page gone */ }
  const racing = rows.filter((r) => r.phase === 'racing' || r.phase === 'finished');
  const count = rows.filter((r) => r.phase === 'countdown' || r.phase === 'ready');
  rep.display = summarize(rows).p50;
  rep.racing = summarize(racing);
  rep.countdown = summarize(count);
  rep.spikes = spikesOf(rows, 25).map(attribute);
  rep.causes = {};
  for (const s of rep.spikes) if (s.phase === 'racing' || s.phase === 'finished') rep.causes[s.cause] = (rep.causes[s.cause] || 0) + 1;
} catch (e) {
  rep.errors.push('harness: ' + e.message);
} finally {
  if (d) { rep.errors.push(...d.errors.slice(0, 20)); await d.close(); }
  if (spawned) killTree(spawned.pid);
  if (server) { server.kill(); await sleep(200); killTree(server.pid); }
}
clearTimeout(dog);
if (OUT) writeFileSync(OUT, JSON.stringify(rep, null, 1));
const brief = { ...rep, spikes: undefined };
console.log(JSON.stringify(brief, null, 1));
console.log('spike timeline (racing + countdown, over 25 ms):');
for (const s of (rep.spikes || [])) console.log('  ' + s.line);
process.exit(rep.errors.some((x) => x.startsWith('harness')) ? 1 : 0);
