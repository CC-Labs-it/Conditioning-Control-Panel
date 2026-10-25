import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { createHostEscape } from '../../shared/host-escape.js';

test('room Escape toggles Options; the kept press plays once in either delivery order', () => {
  const source = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  const start = source.indexOf("  window.addEventListener('keydown', (e) => {", source.indexOf('function wireExits()'));
  const code = source.slice(start, source.indexOf('\n  }, true);', start) + 12);
  assert.match(source, /bridge\.on\(HOST_ESCAPE, \(\) => hostEscape\.kept\(\)\)/);
  for (const order of ['host', 'real-first', 'real-last']) {
    let time = 0, timer, key;
    const listeners = [], win = { addEventListener: (_, fn) => listeners.push(fn) };
    const dispatchEvent = e => listeners.forEach(fn => fn(e));
    const event = () => ({ key: 'Escape', preventDefault() {} });
    const host = createHostEscape({ win, doc: { body: { dispatchEvent } }, now: () => time,
      later: fn => { timer = fn; return 1; }, cancel: () => { timer = null; }, makeEvent: event });
    const hud = { optionsOpen: false, openOptions() { this.optionsOpen = true; }, closeOptions() { this.optionsOpen = false; }, closeStations() {} };
    vm.runInNewContext(code, { window: { addEventListener: (_, fn) => { key = fn; listeners.push(fn); } },
      scene: { documents: {}, freeLook() {} }, welcome: null, loader: null, visiting: false, hud,
      back: () => assert.fail('Escape must not leave') });
    if (order === 'real-first') dispatchEvent(event());
    host.kept(); host.kept();
    if (order === 'real-last') dispatchEvent(event());
    timer?.(); timer = null;
    assert.equal(hud.optionsOpen, true, order);
    key({ ...event(), repeat: true }); assert.equal(hud.optionsOpen, true);
    time = 2500; host.kept(); timer?.();
    assert.equal(hud.optionsOpen, false, 'Escape on Options resumes the room');
  }
});
