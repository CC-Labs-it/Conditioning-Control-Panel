import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { createHostEscape } from './host-escape.js';
test('kept-escape plays once, with real key before or after the frame', () => {
  let at = 2000, pending, key, plays = 0;
  const h = createHostEscape({ win: { addEventListener: (_, f) => key = f },
    doc: { body: { dispatchEvent: e => { key(e); plays++; } } }, now: () => at,
    later: f => { pending = f; return 1; }, cancel: () => pending = null, makeEvent: () => ({ key: 'Escape' }) });
  assert.equal(h.kept(), true); assert.equal(h.kept(), false); pending(); assert.equal(plays, 1);
  key({ key: 'Escape' }); assert.equal(h.kept(), false);
  at += 2000; h.kept(); key({ key: 'Escape' }); assert.equal(pending, null); assert.equal(plays, 1); h.dispose();
});
test('Escape closes local options without leaving or conceding', () => {
  const boot = readFileSync(new URL('./boot.js', import.meta.url), 'utf8');
  const start = boot.indexOf('function onKeyDown('), end = boot.indexOf('/* ---', start);
  const options = { isOpen: false, toggle() { this.isOpen = !this.isOpen; } };
  const key = vm.runInNewContext(boot.slice(start, end) + '; onKeyDown', { options, sheets: { isOpen: false } });
  const e = { key: 'Escape', preventDefault() {} }; key(e); assert.equal(options.isOpen, true);
  key({ ...e, repeat: true }); assert.equal(options.isOpen, true); key(e); assert.equal(options.isOpen, false);
  assert.match(boot, /bridge.on\(HOST_ESCAPE, \(\) => hostEscape.kept\(\)\)/); assert.doesNotMatch(boot, /requestExit\('hold-escape'\)/);
});
