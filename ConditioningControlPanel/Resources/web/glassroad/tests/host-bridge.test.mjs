import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createBridge, defaultInit, mergeInit, songCharts } from '../host/bridge.js';
import { createHostEscape } from '../host/hostEscape.js';
import { formatTime } from '../hud/hud.js';
import { applyKept, rangeFor } from '../hud/feelSheet.js';
import { createFeel } from '../core/feel.js';

function fakeWin(hosted) {
  const posted = [], listeners = [], timers = [];
  const win = {
    location: { search: '?renderer=webgl2' },
    setInterval: (fn) => { timers.push(fn); return timers.length; }, clearInterval() {},
    setTimeout: (fn) => { timers.push(fn); return timers.length; }, clearTimeout() {},
  };
  if (hosted) win.chrome = { webview: { postMessage: (m) => posted.push(m), addEventListener: (_t, fn) => listeners.push(fn) } };
  const deliver = (m) => listeners.forEach((fn) => fn({ data: m }));
  return { win, posted, deliver, timers };
}

test('unhosted: init resolves with the browser defaults', async () => {
  const { win } = fakeWin(false);
  const b = createBridge({ win });
  assert.equal(b.hosted, false); assert.equal(b.protocol, 2);
  const init = await b.init;
  assert.equal(init.v, 2); assert.equal(init.caps.platform, 'web'); assert.equal(init.caps.forceRenderer, 'webgl2');
  assert.equal(init.gentle.on, false); assert.equal(init.identity, null);
  assert.equal(defaultInit({ search: '?gentle=1' }).gentle.on, true);
});

test('hosted: ready, pong, heartbeat, buffered early messages, init merged over defaults', async () => {
  const { win, posted, deliver, timers } = fakeWin(true);
  const b = createBridge({ win });
  b.ready();
  assert.deepEqual(posted[0], { type: 'ready', protocol: 2 });
  deliver({ type: 'pause', on: true });
  deliver(JSON.stringify({ type: 'init', v: 2, gentle: { on: true }, caps: { platform: 'desktop' } }));
  deliver({ type: 'ping', t: 5 });
  assert.deepEqual(posted[1], { type: 'pong', t: 5 });
  const init = await b.init;
  assert.equal(init.gentle.on, true); assert.equal(init.gentle.riseMs, 300); assert.equal(init.caps.platform, 'desktop');
  const seen = [];
  b.on('pause', (m) => seen.push(m.on));
  assert.deepEqual(seen, [true]);
  timers[0]();
  assert.equal(posted.at(-1).type, 'heartbeat');
  b.runEnded({ timeS: 61.5, laps: 3 });
  assert.deepEqual(posted.at(-1), { type: 'run-ended', timeS: 61.5, laps: 3 });
});

test('host escape: a kept press plays once, dropped when the real key came first', () => {
  let t = 0; const later = []; const played = [];
  const listeners = [];
  const win = { addEventListener: (_n, fn) => listeners.push(fn), removeEventListener() {} };
  const doc = { body: { dispatchEvent: (e) => played.push(e) } };
  const he = createHostEscape({ win, doc, now: () => t, later: (fn) => { later.push(fn); return later.length; }, cancel() {}, makeEvent: () => ({ key: 'Escape' }) });
  assert.equal(he.kept(), true);
  t = 400; later.shift()();
  assert.equal(played.length, 1);
  t = 5000; listeners[0]({ key: 'Escape' });
  t = 5100;
  assert.equal(he.kept(), false);
  he.dispose();
});

test('hud and feel sheet helpers', () => {
  assert.equal(formatTime(0), '0:00.00'); assert.equal(formatTime(75.256), '1:15.26');
  const feel = createFeel({ speed: { topMps: 30 }, gentle: { on: false } });
  assert.equal(applyKept(feel, { speed: { topMps: 33, nope: 1 }, gentle: { on: true } }), 1);
  assert.equal(feel.get('speed.topMps'), 33); assert.equal(feel.get('gentle.on'), false);
  assert.deepEqual(rangeFor(30), { min: 0, max: 90, step: 1 });
  assert.equal(rangeFor(0.15).max, 1);
});

test('caps.songs: default empty, host list validated, query and dev host win', () => {
  assert.deepEqual(defaultInit().caps.songs, []);
  const web = mergeInit(defaultInit(), { caps: { platform: 'web', songs: ['/glassroad-audio/pink-road/chart.json'] } });
  assert.deepEqual(songCharts({ songs: web.caps.songs }), ['/glassroad-audio/pink-road/chart.json']);
  assert.deepEqual(songCharts({ songs: mergeInit(defaultInit(), { caps: { platform: 'desktop' } }).caps.songs }), []);
  assert.deepEqual(songCharts({ songs: undefined }), []);
  assert.deepEqual(songCharts({ songs: 'x.json' }), []);
  const bad = ['https://evil.test/a.json', '//evil.test/a.json', '/a/../b.json', '/a.mp3', 'a.json', '/a b.json', 7, null, '/ok/c.json'];
  assert.deepEqual(songCharts({ songs: bad }), ['/ok/c.json']);
  assert.equal(songCharts({ songs: Array.from({ length: 12 }, (_, i) => `/s/${i}.json`) }).length, 8);
  assert.deepEqual(songCharts({ search: '?chart=0', songs: ['/a.json'] }), []);
  assert.deepEqual(songCharts({ search: '?chart=/x.json', songs: ['/a.json'] }), ['/x.json']);
  assert.equal(songCharts({ devHost: true, songs: ['/a.json'] })[0], '/private-audio/pink-road/chart.json');
});
