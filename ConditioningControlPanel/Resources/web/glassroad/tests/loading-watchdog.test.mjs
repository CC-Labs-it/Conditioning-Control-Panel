// iPhone Safari 2026-10-28: iOS killed the GPU process at "Getting the lights ready" and the cover froze at 58%
// forever (progress is monotonic and nothing noticed). A visible cover whose bar has not moved for LOAD_STALL_MS
// now calls onStall once; boot records a 'stall' failure and fails the cover with Retry.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createLoading, LOAD_STALL_MS, LOAD_TICK_MS } from '../hud/loading.js';
import { failureLine, describeFailure } from '../core/failure.js';

function cover(t) {
  t.mock.timers.enable({ apis: ['setInterval', 'setTimeout'] });
  let clock = 0;
  const nodes = { '.boot-stage': { textContent: '' }, '.boot-fill': { style: {} }, '.boot-percent': { textContent: '' }, '[role=progressbar]': { setAttribute() {} },
    '.boot-reason': { textContent: '', hidden: true }, '.boot-retry': { hidden: true, addEventListener() {}, focus() {} } };
  const root = { hidden: false, querySelector: (s) => nodes[s], style: { setProperty() {} }, classList: { add() {}, remove() {}, toggle() {} } };
  const doc = { hidden: false, getElementById: () => root, body: { dataset: { boot: 'loading' } } };
  const stalls = [];
  const loading = createLoading(doc, { now: () => clock });
  loading.onStall((s) => stalls.push(s));
  const pass = (ms) => { for (let i = 0; i < ms / LOAD_TICK_MS; i++) { clock += LOAD_TICK_MS; t.mock.timers.tick(LOAD_TICK_MS); } };
  return { loading, doc, nodes, stalls, pass, jump: (ms) => { clock += ms; } };
}

test('loading watchdog: a bar that stops for LOAD_STALL_MS stalls once, naming the stage', (t) => {
  const { loading, stalls, pass, nodes } = cover(t);
  assert.equal(LOAD_STALL_MS, 20000);
  loading.start(); loading.set('Getting the lights ready', 0.58);
  pass(LOAD_STALL_MS - LOAD_TICK_MS);
  assert.equal(stalls.length, 0);
  pass(LOAD_TICK_MS);
  assert.equal(stalls.length, 1);
  assert.equal(stalls[0].stage, 'Getting the lights ready'); assert.equal(stalls[0].progress, 0.58);
  pass(LOAD_STALL_MS * 2);
  assert.equal(stalls.length, 1, 'once per load');
  loading.fail('x'); loading.set('Late', 0.9);
  assert.equal(nodes['.boot-stage'].textContent, 'Could not open the garage.', 'a failed cover keeps its reason');
  loading.start(); pass(LOAD_STALL_MS);
  assert.equal(stalls.length, 2, 'the next load is watched again');
});

test('loading watchdog: a slow load that keeps moving never stalls; a finished cover stops watching', (t) => {
  const { loading, stalls, pass } = cover(t);
  loading.start();
  for (let i = 1; i <= 12; i++) { loading.set('Bringing the racers in', i / 100); pass(LOAD_STALL_MS - 2 * LOAD_TICK_MS); }
  loading.set('Setting the course', 0.12);   // a new stage with the same value is movement too
  pass(LOAD_STALL_MS - 2 * LOAD_TICK_MS);
  loading.set('Setting the course', 0.12);   // the same stage and value is not
  pass(2 * LOAD_TICK_MS);
  assert.equal(stalls.length, 1);
  const raf = globalThis.requestAnimationFrame; globalThis.requestAnimationFrame = () => {};   // the finish never needs to paint here
  t.after(() => { globalThis.requestAnimationFrame = raf; });
  loading.start(); loading.set('Ready to roll', 1); void loading.finish();
  pass(LOAD_STALL_MS * 2);
  assert.equal(stalls.length, 1, 'no watch once the cover is finishing');
});

test('a new loading phase resets the visible bar instead of keeping the previous 100%', (t) => {
  const { loading, nodes } = cover(t);
  loading.start(); loading.set('Ready to roll', 1);
  loading.start();
  assert.equal(nodes['.boot-percent'].textContent, '0%');
  assert.equal(nodes['.boot-fill'].style.transform, 'scaleX(0)');
  loading.set('Getting the lights ready', .24);
  loading.set('Getting the lights ready', .20);
  assert.equal(nodes['.boot-percent'].textContent, '24%', 'real progress remains monotonic inside a phase');
  loading.dispose();
});

test('loading watchdog: the clock pauses while the tab is hidden, and one long blocked task counts two ticks at most', (t) => {
  const { loading, doc, stalls, pass, jump } = cover(t);
  loading.start(); loading.set('Getting the lights ready', 0.3);
  pass(LOAD_STALL_MS / 2);
  doc.hidden = true; pass(LOAD_STALL_MS * 3); doc.hidden = false;
  pass(LOAD_STALL_MS / 2 - LOAD_TICK_MS);
  assert.equal(stalls.length, 0, 'hidden time never counts');
  loading.set('Getting the lights ready', 0.31);
  jump(15000); pass(LOAD_TICK_MS);   // a 15 s synchronous compile held the main thread
  pass(LOAD_STALL_MS - 4 * LOAD_TICK_MS);
  assert.equal(stalls.length, 0, 'busy is not dead');
  pass(2 * LOAD_TICK_MS);
  assert.equal(stalls.length, 1);
});

test('boot.js: a stall is recorded and fails the cover with Retry; phones wait for the first clips only', () => {
  const line = failureLine(describeFailure(new Error('stuck at Getting the lights ready (58%) for 20 s'), { kind: 'stall', course: 'mainframe', backend: 'webgpu', tier: 'phone', stage: 'garage' }));
  assert.equal(line, 'loading stalled after garage (mainframe, webgpu, phone): stuck at Getting the lights ready (58%) for 20 s');
  assert.doesNotMatch(line, /[!–—]/);
  const b = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  const st = b.slice(b.indexOf('loading.onStall('), b.indexOf('/** ?botFall=1'));
  assert.match(st, /const rec = record\('stall', new Error\(/);
  assert.match(st, /LOAD_STALL_MS/);
  assert.match(st, /showFailure\(rec\);/);
  assert.match(b, /function showFailure\(rec\) \{[\s\S]*?loading\.fail\([\s\S]*?loading\.onRetry\(\(\) => void retry\(rec\.course\)\);/);
  assert.match(b, /createCardMedia\(world\.media, \{ bridge,\s*readyCount: renderer\.device === 'phone' \? PHONE_READY_CLIPS : Infinity, \.\.\.\(deviceOrigin \? \{ local: false \} : null\) \}\)/);   // the DEVICE, whatever Quality is pinned
  assert.match(b, /if \(cardMedia\) await cardMedia\.ready;/);
});
