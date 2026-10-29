// Owner report 2026-10-26 (a phone, the live web build): Mainframe never ran once ("Could not open the garage.
// Reload to try again." with the real reason hidden in the console), and the 3D world froze while the music
// played on after a rotation or an option change. A failure now names its reason on the cover with a Retry, is
// kept under localStorage glassroad.lastFailure, a WebGPU init that refuses its device falls back to WebGL2 on a
// fresh canvas, and a lost device, a stalled loop or a loop that keeps throwing rebuilds the graphics behind the
// cover instead of staying dead.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { FAILURE_KEY, reasonOf, describeFailure, failureLine, rememberFailure, readLastFailure, isStalled, blockedTick, STALL_MS, WATCH_MS, FRAME_THROWS } from '../core/failure.js';
import { createLoading } from '../hud/loading.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

test('failure record: the reason, the stage, the course and backend, remembered and read back', () => {
  assert.equal(reasonOf(new Error('track 404')), 'track 404');
  assert.equal(reasonOf(new TypeError('x is not a function')), 'TypeError: x is not a function');
  assert.equal(reasonOf('plain text'), 'plain text');
  assert.equal(reasonOf(null), 'unknown');
  const rec = describeFailure(new Error('no WebGL2 context'), { kind: 'boot', course: 'mainframe', backend: 'webgpu', tier: 'phone', stage: 'garage', at: 5, ua: 'Mozilla/5.0 (Linux; Android 14)' });
  assert.equal(rec.v, 1); assert.equal(rec.course, 'mainframe'); assert.equal(rec.stage, 'garage'); assert.equal(rec.at, 5);
  assert.equal(failureLine(rec), 'boot failed after garage (mainframe, webgpu, phone): no WebGL2 context');
  assert.equal(failureLine({ ...rec, kind: 'lost', stage: null }), 'graphics device lost (mainframe, webgpu, phone): no WebGL2 context');
  assert.equal(failureLine({ ...rec, kind: 'stalled' }).slice(0, 14), 'frames stopped');
  assert.equal(failureLine({ ...rec, kind: 'frame' }).slice(0, 16), 'frame loop threw');
  // a pinned Quality says so on the line (owner's iPhone 2026-10-28: High pinned on a phone, "loading stalled after water")
  assert.equal(rec.pinned, false);
  const pinned = describeFailure(new Error('stuck at water (58%) for 20 s'), { kind: 'stall', course: 'threshold', backend: 'webgpu', tier: 'high', pinned: true, stage: 'water' });
  assert.equal(pinned.pinned, true);
  assert.equal(failureLine(pinned), 'loading stalled after water (threshold, webgpu, high pinned): stuck at water (58%) for 20 s');
  assert.equal(failureLine({ ...pinned, tier: null }), 'loading stalled after water (threshold, webgpu): stuck at water (58%) for 20 s');
  assert.equal(describeFailure(new Error('x'), { pinned: 'yes' }).pinned, false, 'only a real true counts');
  assert.match(src('boot.js'), /tier: shared\.renderer\?\.tier \|\| null, pinned: !!shared\.renderer\?\.pinned/, 'every record carries the pin');
  assert.doesNotMatch(failureLine(rec), /[!–—]/);
  const items = {};
  const storage = { setItem: (k, v) => { items[k] = v; }, getItem: (k) => items[k] ?? null };
  assert.ok(rememberFailure(rec, storage));
  assert.ok(items[FAILURE_KEY].includes('"course":"mainframe"'));
  assert.deepEqual(readLastFailure(storage), rec);
  assert.equal(readLastFailure({ getItem: () => '{bad json' }), null);
  assert.equal(rememberFailure(rec, { setItem() { throw new Error('blocked'); } }), false, 'blocked storage never throws');
  assert.equal(readLastFailure(null), null);
});

test('stall rule: a visible, uncovered, idle page with no frame for STALL_MS is dead; hidden, covered, busy or just shown is not', () => {
  const base = { nowMs: 20000, lastFrameMs: 10000, visible: true, covered: false, busy: false };
  assert.ok(STALL_MS >= 4000 && STALL_MS <= 10000);
  assert.ok(FRAME_THROWS >= 2);
  assert.equal(isStalled(base), true);
  assert.equal(isStalled({ ...base, lastFrameMs: 20000 - STALL_MS + 1 }), false);
  assert.equal(isStalled({ ...base, visible: false }), false, 'a hidden tab draws nothing');
  assert.equal(isStalled({ ...base, covered: true }), false, 'the warm pauses frames behind the cover');
  assert.equal(isStalled({ ...base, busy: true }), false, 'a course is opening');
  assert.equal(isStalled({ ...base, visibleSinceMs: 19000 }), false, 'just back from hidden: the loop has not had its chance');
  assert.equal(isStalled({ ...base, lastFrameMs: 0 }), false, 'no frame yet is a boot, not a stall');
  // one long synchronous task (a first-use pipeline compile, a GC) held the main thread: the watchdog tick itself
  // came late, the loop was busy, not dead (review of #3002)
  assert.equal(blockedTick({ nowMs: 20000, lastTickMs: 18000 }), false, 'a tick on time');
  assert.equal(blockedTick({ nowMs: 20000, lastTickMs: 15900 }), true, 'a tick more than two intervals late');
  assert.equal(blockedTick({ nowMs: 20000, lastTickMs: 0 }), false, 'the first tick has no previous one');
  assert.equal(isStalled({ ...base, lastTickMs: 12000 }), false, 'a 6 s compile is not a dead loop');
  assert.equal(isStalled({ ...base, lastTickMs: 18000 }), true, 'ticks on time and still no frame: dead');
  assert.equal(WATCH_MS * 2 < STALL_MS, true, 'two ticks fit inside the stall window');
});

test('loading cover: fail shows the reason in small text and a Retry that runs the handler; start clears both', () => {
  const nodes = { '.boot-stage': { textContent: '' }, '.boot-fill': { style: {} }, '.boot-percent': { textContent: '' }, '[role=progressbar]': { setAttribute(k, v) { this[k] = v; } },
    '.boot-reason': { textContent: '', hidden: true }, '.boot-retry': { hidden: true, focused: 0, listeners: {}, addEventListener(t, fn) { this.listeners[t] = fn; }, focus() { this.focused++; } } };
  const classes = new Set();
  const root = { hidden: false, querySelector: (s) => nodes[s], style: { setProperty() {} }, classList: { add: (...cs) => cs.forEach((c) => classes.add(c)), remove: (...cs) => cs.forEach((c) => classes.delete(c)), toggle() {} } };
  const doc = { getElementById: () => root, body: { dataset: { boot: 'ready' } } };
  const cover = createLoading(doc);
  let retried = 0;
  cover.onRetry(() => { retried++; });
  cover.fail('boot failed after garage (mainframe, webgpu, phone): no WebGL2 context');
  assert.equal(nodes['.boot-stage'].textContent, 'Could not open the garage.');
  assert.equal(nodes['.boot-reason'].textContent, 'boot failed after garage (mainframe, webgpu, phone): no WebGL2 context');
  assert.equal(nodes['.boot-reason'].hidden, false);
  assert.equal(nodes['.boot-retry'].hidden, false);
  assert.equal(nodes['.boot-retry'].focused, 1);
  assert.equal(doc.body.dataset.boot, 'loading', 'the cover owns the page again');
  assert.ok(classes.has('boot-failed'));
  nodes['.boot-retry'].listeners.click();
  assert.equal(retried, 1);
  cover.start();
  assert.equal(nodes['.boot-reason'].hidden, true); assert.equal(nodes['.boot-retry'].hidden, true);
  assert.ok(!classes.has('boot-failed'));
  cover.fail('');
  assert.equal(nodes['.boot-reason'].hidden, true, 'no reason, no empty line');
  assert.equal(nodes['.boot-retry'].hidden, false, 'Retry is always there');
});

test('index.html: the cover carries the reason line and the Retry button; the css hides them until a failure', () => {
  const html = src('index.html');
  assert.match(html, /<p class="boot-reason" hidden><\/p><button type="button" class="boot-retry" hidden>Retry<\/button>/);
  const css = src('hud/loading.css');
  assert.match(css, /\.boot-reason\[hidden\],\.boot-retry\[hidden\]\{display:none\}/);
  assert.match(css, /\.boot-failed \.boot-fill[^\n]*\{animation:none\}/);
});

test('boot.js: a failure is recorded with the stage and shown on the cover with Retry; no bare dead end', () => {
  const b = src('boot.js');
  const fn = b.slice(b.indexOf('function fail(e) {'), b.indexOf('function teardownGpu('));
  assert.match(fn, /const rec = record\('boot', e\)/);
  assert.match(fn, /loading\.fail\(failureLine\(rec\)\)/);
  assert.match(fn, /loading\.onRetry\(\(\) => void retry\(rec\.course\)\)/);
  const rc = b.slice(b.indexOf('function record(kind, e) {'), b.indexOf('function fail(e) {'));
  assert.match(rc, /stage: ms && ms\.length \? ms\[ms\.length - 1\]\.stage : null/);
  assert.match(rc, /rememberFailure\(rec\)/);
  assert.match(rc, /d\.lastFailure = rec/);
  // Retry and recovery rebuild on a clean GPU slate: every shared GPU object dropped, a fresh canvas next time
  const td = b.slice(b.indexOf('function teardownGpu('), b.indexOf('function retry('));
  for (const k of ['garage', 'kartPool.current', 'artLibrary', 'renderer']) assert.ok(td.includes('shared.' + k + '?.dispose()'), k + ' disposed');
  assert.match(td, /shared\.garage = null; shared\.kartPool\.current = null; shared\.artLibrary = null; shared\.garageArt = null; shared\.renderer = null;/);
  assert.match(td, /freshCanvas: true/);
  assert.match(b, /createRenderer\(canvas, \{ forceRenderer: init\.caps\.forceRenderer, options: hud\.options, platform: init\.caps\.platform, \.\.\.\(shared\.rendererOpts \|\| \{\}\) \}\)/);
  assert.match(b, /canvas = renderer\.canvas;/);
  assert.match(b, /^let canvas = document\.getElementById\('view'\);/m);
});

test('boot.js: device loss, a stalled loop and a loop that keeps throwing all rebuild behind the cover, back into the race', () => {
  const b = src('boot.js');
  assert.match(b, /lifetime\.defer\(renderer\.onLost\(\(lost\) => \{\s*if \(isCurrent\(\)\) void recover\('lost'/);
  const rec = b.slice(b.indexOf('async function recover('), b.indexOf('shared.recover = recover'));
  assert.match(rec, /if \(shared\.closed \|\| shared\.recovering\) return false;/);
  assert.match(rec, /teardownGpu\(\{ lowerTier: kind === 'lost' \}\)/, 'a lost device rebuilds one tier lower');
  assert.match(rec, /selectCourse\(rec\.course, \{ force: true, replace: true, race \}\)/);
  const wd = b.slice(b.indexOf('const watchdog = setInterval('), b.indexOf('function frame(now) {'));
  assert.match(wd, /const blocked = blockedTick\(\{ nowMs, lastTickMs \}\);\s*lastTickMs = nowMs;\s*if \(blocked\) \{ lastFrameMs = lastDrawMs = nowMs; return; \}/, 'a late tick moves the stamps and judges nothing');
  assert.match(wd, /\}, WATCH_MS\);/);
  assert.match(wd, /isStalled\(\{ nowMs, lastFrameMs, visible, visibleSinceMs, covered, busy \}\)/);
  assert.match(wd, /racing\(\) && !paused && isStalled\(\{ nowMs, lastFrameMs: lastDrawMs,/, 'a race that draws nothing is as dead');
  assert.match(wd, /covered = document\.body\.dataset\.boot !== 'ready', busy = rewarmBusy \|\| controller\.busy/);
  assert.match(wd, /recover\('stalled'/);
  assert.match(b, /lifetime\.defer\(\(\) => clearInterval\(watchdog\)\)/);
  const fr = b.slice(b.indexOf('function frame(now) {'), b.indexOf('function frameBody(now) {'));
  assert.match(fr, /frameId = requestAnimationFrame\(frame\);\s*lastFrameMs = now;/);
  assert.match(fr, /try \{ frameBody\(now\); frameThrows = 0; if \(renderer\.three\.info\.render\.drawCalls > 0\) lastDrawMs = now; \}/);
  assert.match(fr, /if \(frameThrows >= FRAME_THROWS\) \{ cancelAnimationFrame\(frameId\); void recover\('frame', e, \{ race: racing\(\) \}\); \}/);
  assert.match(b, /const racing = \(\) => !online && \(state === 'intro' \|\| state === 'race'\);/);
});

test('renderer.js: a WebGPU init failure falls back to WebGL2 on a fresh canvas; onLost hears the device loss once', () => {
  const r = src('render/renderer.js');
  const init = r.slice(r.indexOf('let three = new WebGPURenderer('), r.indexOf('trackSceneResources(three);'));
  assert.match(init, /try \{\s*await three\.init\(\);\s*\} catch \(e\) \{/);
  assert.match(init, /if \(webgl2 \|\| force === 'webgpu'\) throw e;/, 'a forced backend keeps its own failure');
  assert.match(init, /canvas = replaceCanvas\(canvas\);/);
  assert.match(init, /three = new WebGPURenderer\(\{ canvas, alpha: true, antialias: false, forceWebGL: true, powerPreference: 'high-performance', context \}\);\s*await three\.init\(\);/);
  assert.match(r, /function replaceCanvas\(canvas\) \{\s*const fresh = canvas\.cloneNode\(false\);\s*if \(canvas\.parentNode\) canvas\.replaceWith\(fresh\);/);
  assert.match(r, /three\.onDeviceLost = \(info\) => \{\s*if \(lost\) return;/);
  assert.match(r, /onLost\(fn\) \{ lostFns\.add\(fn\); if \(lost\) \{ try \{ fn\(lost\); \}/);
  assert.match(r, /get canvas\(\) \{ return canvas; \}/);
  assert.match(r, /if \(freshCanvas\) canvas = replaceCanvas\(canvas\);/);
  // the tier ceiling of a rebuild caps both the first pick and the gpu-aware pick, never a pinned Quality
  assert.match(r, /setTier\(pinOf\(\) \|\| capTier\(pickTier\(\{ override, coarse, webgl2: webgl2 && !firefox \}\)\)\)/);
  assert.match(r, /const autoTier = capTier\(pickTier\(/);
  // dispose survives a dead device: each step on its own
  assert.match(r, /for \(const step of \[\(\) => post && post\.dispose\(\), \(\) => readout\(false\), \(\) => disposeSceneResources\(three\), \(\) => three\.dispose\(\)\]\)/);
});
