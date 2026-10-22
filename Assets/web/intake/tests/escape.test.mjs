import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { installPause, pauseClockNow } from '../ui/pause.js';
import { createHostEscape } from '../host-escape.js';

test('Escape pauses the real intake clock and sound; Escape on the card resumes', () => {
  const oldDate = Date.now, oldPerf = globalThis.performance;
  let at = 1000, id = 0, closed = 0, muted = false;
  const timers = new Map();
  class Element {
    constructor() {
      this.children = []; this.attrs = {}; this.events = {}; this.style = { setProperty() {} };
      const names = new Set(); this.classList = { add: x => names.add(x), remove: x => names.delete(x), contains: x => names.has(x), toggle() {} };
    }
    setAttribute(k, v) { this.attrs[k] = v; }
    getAttribute(k) { return this.attrs[k]; }
    addEventListener(k, fn) { (this.events[k] ||= []).push(fn); }
    removeEventListener(k, fn) { this.events[k] = (this.events[k] || []).filter(x => x !== fn); }
    appendChild(n) { this.children.push(n); n.parentNode = this; n.isConnected = true; return n; }
    removeChild(n) { this.children = this.children.filter(x => x !== n); n.parentNode = null; n.isConnected = false; }
    contains(n) { return n === this || this.children.some(c => c.contains(n)); }
    querySelector(s) { return this.children.flatMap(c => [c, c.querySelector(s)]).find(c => c && (s.startsWith('.') ? c.className?.split(' ').includes(s.slice(1)) : s === `[data-act="${c.attrs['data-act']}"]`)) || null; }
    querySelectorAll() { return []; }
    focus() { document.activeElement = this; }
    dispatchEvent(e) { if (e.key && this !== win && this !== doc) return escape(); e.target ||= this; for (const fn of this.events[e.type] || []) { fn(e); if (e.stopped) break; } }
  }
  const win = new Element(), doc = new Element();
  Object.assign(doc, { body: new Element(), head: new Element(), createElement: () => new Element(), getElementById: () => null });
  Object.assign(win, { setTimeout: fn => { timers.set(++id, fn); return id; }, clearTimeout: n => timers.delete(n), setInterval: () => ++id, clearInterval() {}, close: () => closed++ });
  globalThis.window = win; globalThis.document = doc; globalThis.performance = { now: () => at };
  doc.activeElement = doc.body;
  const escape = () => { const e = { type: 'keydown', key: 'Escape', preventDefault() {}, stopImmediatePropagation() { this.stopped = true; } }; win.dispatchEvent(e); if (!e.stopped) doc.dispatchEvent(e); };
  doc.body.dispatchEvent = escape;
  const host = createHostEscape({ win, doc, now: () => at, later: win.setTimeout, cancel: win.clearTimeout, makeEvent: () => ({ key: 'Escape' }) });
  const pause = installPause({ audio: { setPaused: on => { muted = on; } } });
  try {
    assert.equal(host.kept(), true); assert.equal(host.kept(), false);
    [...timers.values()][0](); timers.clear();
    assert.equal(pause.isPaused(), true); assert.equal(muted, true);
    const frozen = pauseClockNow(); at += 5000;
    assert.equal(pauseClockNow(), frozen, 'time spent on the pause card is held');
    escape();
    assert.equal(pause.isPaused(), false); assert.equal(muted, false); assert.equal(closed, 0);
    assert.equal(host.kept(), false, 'the real Escape already arrived');
    at += 2000; host.kept(); escape();
    assert.equal(timers.size, 0, 'a real key arriving after the frame cancels playback');
    assert.equal(pause.isPaused(), true);
    at += 5000; host.kept(); [...timers.values()][0](); timers.clear();
    assert.equal(pause.isPaused(), false, 'kept Escape resumes while the intake clock is frozen');
  } finally {
    pause.dispose(); host.dispose(); Date.now = oldDate; globalThis.performance = oldPerf;
    delete globalThis.window; delete globalThis.document;
  }
});

test('the boot bridge wires kept-escape before the timing shim', () => {
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.match(boot, /shim\.on\(HOST_ESCAPE, \(\) => hostEscape\.kept\(\)\)/);
  assert.ok(boot.indexOf('createHostEscape();') < boot.indexOf('pmod.installPause('));
});
