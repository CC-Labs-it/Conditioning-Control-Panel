// dev/phone-perf.mjs - phone-profile frame probe: Chrome headless at 390x844 touch (isMobile, so ui/perf.js LITE is on), CDP CPU throttle,
// a player tapping 4/s. Needs dev/serve.mjs. node dev/phone-perf.mjs fresh|mid|pile [route]
// Env: PORT (8823) THROTTLE (4) SECS (8) QS (&lite=0 for an A/B) PROFILE=1 (CPU profile + layout-read callers) TRACE=file
// EXP=dev/phone-perf-scopes.js (ms per unlock scope) SHOT=png CDP=http://localhost:9333 (attach to Android Chrome via adb forward).
import { createRequire } from 'node:module';
import { writeFileSync } from 'node:fs';
const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require('playwright');

const scen = process.argv[2] || 'pile';
const route = (process.argv[3] || 'eyes,sweet,slides,sell,trance').split(',');
const PORT = process.env.PORT || 8823;
const base = `http://localhost:${PORT}/fuzz/index.html`;
const THROTTLE = Number(process.env.THROTTLE || 4);
const SECS = Number(process.env.SECS || 8);
const W = Number(process.env.W || 390), H = Number(process.env.H || 844);

const browser = process.env.CDP ? await chromium.connectOverCDP(process.env.CDP) : await chromium.launch({ headless: true, executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', args: ['--mute-audio', '--autoplay-policy=no-user-gesture-required'] });
const ctx = process.env.CDP ? browser.contexts()[0] : await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 3, isMobile: true, hasTouch: true,
  userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Mobile Safari/537.36' });
const page = process.env.CDP ? (ctx.pages()[0] || await ctx.newPage()) : await ctx.newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (m) => { if (m.type() === 'error') errors.push('console: ' + m.text().slice(0, 200)); });
await page.addInitScript(() => {
  window.__lt = [];
  try { new PerformanceObserver((l) => { for (const e of l.getEntries()) window.__lt.push(e.duration); }).observe({ type: 'longtask', buffered: true }); } catch {}
});
const cdp = await ctx.newCDPSession(page);
await cdp.send('Performance.enable');

const t0 = Date.now();
await page.goto(`${base}?pace=1&fresh=1${process.env.QS || ""}`, { waitUntil: 'load' });
await page.waitForFunction(() => !!window.fuzz);
const bootMs = Date.now() - t0;

if (scen !== 'fresh') {
  await page.evaluate(([r, scen]) => {
    const f = window.fuzz;
    ['f1', 'f2', 'f3', 'f4', 'f5'].forEach((k, i) => { f.state.picks[k] = r[i]; });
    f.engine.emit('page-colour', {});
    const items = f.items.filter((it) => it.kind !== 'end' && it.group === 'run');
    const n = scen === 'mid' ? Math.floor(items.length * 0.4) : items.length;
    for (const it of items.slice(0, n)) { try { f.grant(it.id); } catch (e) {} }
    f.add(1e9);
  }, [route, scen]);
}
// clear cards
for (let i = 0; i < 20; i++) {
  const n = await page.locator('#cards .backdrop, .tc').count();
  if (!n) break;
  const b = page.locator('#cards .backdrop:last-child .card-buttons button, .tc [data-pick=none], .tc [data-accept]').last();
  if (await b.count()) await b.click({ timeout: 800, force: true }).catch(() => {});
  await page.waitForTimeout(400);
}
await page.waitForTimeout(2500);
await cdp.send('Emulation.setCPUThrottlingRate', { rate: THROTTLE });
const traceEv = []; if (process.env.TRACE) { cdp.on('Tracing.dataCollected', (d) => traceEv.push(...d.value)); await cdp.send('Tracing.start', { traceConfig: { includedCategories: ['devtools.timeline', 'disabled-by-default-devtools.timeline', 'disabled-by-default-devtools.timeline.stack', 'v8.execute', 'blink.animations'] }, transferMode: 'ReportEvents' }); }
if (process.env.PROFILE) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 500 }); await cdp.send('Profiler.start'); }
const m0 = (await cdp.send('Performance.getMetrics')).metrics;
if (process.env.EXP) { const { readFileSync } = await import('node:fs'); await page.evaluate(readFileSync(process.env.EXP, 'utf8')); await page.waitForTimeout(500); }
if (process.env.ANIMS) console.log('anims', JSON.stringify(await page.evaluate(() => { const m = {}; for (const a of document.getAnimations()) { const t = a.effect?.target; const k = (a.animationName || 'waapi') + ' @' + (t ? (t.id || String(t.className?.baseVal ?? t.className).split(' ')[0] || t.tagName) : '?'); m[k] = (m[k] || 0) + 1; } return Object.entries(m).sort((a, b) => b[1] - a[1]).slice(0, 40); })));
await page.evaluate(() => { window.__lt = []; });
const frames = await page.evaluate((secs) => new Promise((res) => {
  const out = []; let last = performance.now(); const end = last + secs * 1000;
  // keep tapping the button like a player (4/s)
  const btn = document.getElementById('btn');
  const tap = setInterval(() => { try { const r = btn.getBoundingClientRect(); const o = { bubbles: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, pointerType: 'touch', isPrimary: true };
    btn.dispatchEvent(new PointerEvent('pointerdown', o)); btn.dispatchEvent(new PointerEvent('pointerup', o)); btn.dispatchEvent(new MouseEvent('click', o)); } catch {} }, 250);
  function f(t) { out.push(t - last); last = t; if (t < end) requestAnimationFrame(f); else { clearInterval(tap); res(out); } }
  requestAnimationFrame(f);
}), SECS);
const m1 = (await cdp.send('Performance.getMetrics')).metrics;
let prof = null;
if (process.env.PROFILE) prof = (await cdp.send('Profiler.stop')).profile;
if (process.env.TRACE) { const done = new Promise((r) => cdp.once('Tracing.tracingComplete', r)); await cdp.send('Tracing.end'); await done; writeFileSync(process.env.TRACE, JSON.stringify(traceEv)); }
await cdp.send('Emulation.setCPUThrottlingRate', { rate: 1 });
const lt = await page.evaluate(() => window.__lt);
if (process.env.CHURN) console.log('churn', JSON.stringify(await page.evaluate(() => [Object.entries(window.__churn).sort((a, b) => b[1] - a[1]).slice(0, 25), Object.entries(window.__wsets).sort((a, b) => b[1] - a[1]).slice(0, 10)])));
if (process.env.PROPS) console.log('props', JSON.stringify(await page.evaluate(() => window.__props), null, 0));
if (process.env.EXP) console.log('scopes', JSON.stringify(await page.evaluate(() => Object.entries(window.__scopeAcc || {}).sort((a, b) => b[1] - a[1]).slice(0, 30).map(([k, v]) => k + '=' + Math.round(v)))));
const info = await page.evaluate(() => {
  const cv = [...document.querySelectorAll('canvas')].map((c) => ({ w: c.width, h: c.height, id: c.id || c.className || c.parentElement?.className, vis: c.getBoundingClientRect().width > 0 }));
  return {
    nodes: document.getElementsByTagName('*').length,
    anims: document.getAnimations().length,
    runningAnims: document.getAnimations().filter((a) => a.playState === 'running').length,
    canvases: cv.length, canvasPx: cv.reduce((s, c) => s + c.w * c.h, 0), bigCanvases: cv.filter((c) => c.w * c.h > 400000),
    videos: document.querySelectorAll('video').length, imgs: document.querySelectorAll('img').length,
    overflowX: document.documentElement.scrollWidth - innerWidth, overflowY: document.documentElement.scrollHeight - innerHeight,
    filters: [...document.querySelectorAll('*')].filter((e) => { const s = getComputedStyle(e); return s.filter !== 'none' || s.backdropFilter !== 'none'; }).length,
    heapMB: performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1e6) : null,
  };
});
const mm = (arr, k) => (arr.find((x) => x.name === k) || {}).value || 0;
const delta = (k) => +(mm(m1, k) - mm(m0, k)).toFixed(3);
frames.shift();
const sorted = [...frames].sort((a, b) => a - b);
const pct = (p) => +sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))].toFixed(1);
const res = {
  scen, route: route.join(','), throttle: THROTTLE, bootMs,
  fps: +(frames.length / SECS).toFixed(1), p50: pct(0.5), p95: pct(0.95), max: pct(0.999), over50: frames.filter((x) => x > 50).length,
  longTasks: lt.length, longTaskMs: Math.round(lt.reduce((s, x) => s + x, 0)),
  script: delta('ScriptDuration'), layout: delta('LayoutDuration'), style: delta('RecalcStyleDuration'), task: delta('TaskDuration'),
  layouts: delta('LayoutCount'), styles: delta('RecalcStyleCount'),
  ...info, errors: errors.slice(0, 8),
};
const { bigCanvases, errors: er, ...head } = res; console.log(JSON.stringify(head)); console.log('canvases', JSON.stringify(bigCanvases)); console.log('errors', er.length, JSON.stringify(er.filter((e) => !/404/.test(e))));
if (prof) {
  // self time by function+url
  const byId = new Map(prof.nodes.map((n) => [n.id, n]));
  const self = new Map();
  const dt = prof.timeDeltas; const samples = prof.samples;
  for (let i = 0; i < samples.length; i++) { const n = byId.get(samples[i]); const cf = n.callFrame; const key = `${cf.functionName || '(anon)'} ${cf.url.replace(/^.*\/web\//, '')}:${cf.lineNumber + 1}`; self.set(key, (self.get(key) || 0) + (dt[i] || 0)); }
  const top = [...self.entries()].sort((a, b) => b[1] - a[1]).slice(0, 35).map(([k, v]) => `${(v / 1000).toFixed(0).padStart(6)}ms ${k}`);
  console.log(top.join('\n'));
  // by file
  const byFile = new Map();
  for (const [k, v] of self) { const f = k.split(' ').slice(1).join(' ').replace(/:\d+$/, ''); byFile.set(f, (byFile.get(f) || 0) + v); }
  const parent = new Map(); for (const n of prof.nodes) for (const c of (n.children || [])) parent.set(c, n);
  const callers = new Map();
  for (let i = 0; i < samples.length; i++) { const n = byId.get(samples[i]); const nm = n.callFrame.functionName;
    if (!/getBoundingClientRect|getClientRects|getImageData|animate|getComputedStyle|offset/.test(nm)) continue;
    let p2 = parent.get(n.id); const cf = p2 ? p2.callFrame : { functionName: '?', url: '', lineNumber: 0 };
    const key = `${nm} <- ${cf.functionName || '(anon)'} ${cf.url.replace(/^.*\/web\//, '')}:${cf.lineNumber + 1}`; callers.set(key, (callers.get(key) || 0) + (dt[i] || 0)); }
  console.log('--- native callers');
  console.log([...callers.entries()].sort((a, b) => b[1] - a[1]).slice(0, 25).map(([k, v]) => `${(v / 1000).toFixed(0).padStart(6)}ms ${k}`).join('\n'));
  console.log('--- by file');
  console.log([...byFile.entries()].sort((a, b) => b[1] - a[1]).slice(0, 25).map(([k, v]) => `${(v / 1000).toFixed(0).padStart(6)}ms ${k}`).join('\n'));
}
if (process.env.SHOT) await page.screenshot({ path: process.env.SHOT });
if (!process.env.CDP) await browser.close(); else process.exit(0);
