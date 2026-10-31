// Execute the production raw JS body without a native browser or font dependency.
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
const source = readFileSync(process.env.CCP_BROWSER_SCRIPT_SOURCE || new URL('../../CCP.Avalonia/Platform/BrowserVideoSurface.cs', import.meta.url), 'utf8');
const script = source.slice(source.indexOf('"""') + 3, source.lastIndexOf('"""'));

function page(play = () => Promise.resolve()) {
  let now = 0, nextId = 0;
  const timers = new Map(), messages = [], listeners = new Map();
  const video = { paused: false, ended: false, currentTime: 0, play };
  const schedule = (f, ms, repeat) => {
    const id = ++nextId;
    timers.set(id, { f, due: now + ms, period: repeat ? ms : 0 });
    return id;
  };
  new Function('c', 'document', 'window', 'performance', 'setTimeout', 'setInterval', 'clearInterval', script)(
    { url: 'http://localhost/video.mp4', volume: 0.5, id: 'test' },
    { body: { replaceChildren() {} }, createElement: () => video },
    { invokeCSharpAction: s => messages.push(JSON.parse(s)), addEventListener: (type, listener) => listeners.set(type, listener) },
    { now: () => now }, (f, ms) => schedule(f, ms, false), (f, ms) => schedule(f, ms, true), id => timers.delete(id));
  return { video, messages, listeners, advance(ms) {
    const end = now + ms;
    for (;;) {
      const due = [...timers].filter(([, t]) => t.due <= end).sort((a, b) => a[1].due - b[1].due)[0];
      if (!due) break;
      const [id, t] = due;
      now = t.due;
      if (t.period) t.due += t.period; else timers.delete(id);
      t.f();
    }
    now = end;
  } };
}

test('no first frame reports file error 101 at ten seconds', () => {
  const p = page();
  p.advance(9999);
  assert.deepEqual(p.messages, []);
  p.advance(1);
  assert.deepEqual(p.messages, [{ type: 'error', code: 101, session: 'test' }]);
});

test('advancing frames keep playback healthy; a ten-second stall reports 100 once', () => {
  const p = page();
  p.video.onplaying();
  for (let frame = 1; frame <= 15; frame++) {
    p.video.currentTime = frame;
    p.advance(1000);
  }
  assert.deepEqual(p.messages.map(m => m.type), ['playing']);
  p.advance(9999);
  assert.equal(p.messages.length, 1);
  p.advance(1);
  assert.equal(p.messages.at(-1).code, 100);
  p.advance(30000);
  assert.equal(p.messages.length, 2);
});

test('pause and end do not count as stalls; resume gets a fresh deadline', () => {
  const p = page();
  p.video.onplaying();
  p.video.paused = true;
  p.advance(30000);
  p.video.paused = false;
  p.video.onplaying();
  p.advance(9999);
  assert.equal(p.messages.filter(m => m.type === 'error').length, 0);
  p.video.ended = true;
  p.video.onended();
  p.advance(30000);
  assert.equal(p.messages.at(-1).type, 'ended');
});

test('media errors retain their code; a rejected start without MediaError stays unclassified', async () => {
  const p = page();
  p.video.error = { code: 4 };
  p.video.onerror();
  assert.equal(p.messages.at(-1).code, 4);
  const rejected = page(() => Promise.reject(new Error('environmental failure')));
  await Promise.resolve();
  assert.equal(rejected.messages.at(-1).code, 0);
});

test('context menu is cancelled before native playback controls appear', () => {
  const p = page();
  let cancelled = false;
  p.listeners.get('contextmenu')({ preventDefault() { cancelled = true; } });
  assert.equal(cancelled, true);
});
