import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { createHostEscape } from './shell/host-escape.js';
test('kept-escape plays once, with real key before or after the frame', () => {
  let at = 2000, pending, key, plays = 0;
  const h = createHostEscape({ win: { addEventListener: (_, f) => key = f },
    doc: { body: { dispatchEvent: e => { key(e); plays++; } } }, now: () => at,
    later: f => { pending = f; return 1; }, cancel: () => pending = null, makeEvent: () => ({ key: 'Escape' }) });
  assert.equal(h.kept(), true); assert.equal(h.kept(), false); pending(); assert.equal(plays, 1);
  key({ key: 'Escape' }); assert.equal(h.kept(), false);
  at += 2000; h.kept(); key({ key: 'Escape' }); assert.equal(pending, null); assert.equal(plays, 1); h.dispose();
});
test('Escape on the class pause card resumes without leaving', () => {
  const code = readFileSync(new URL('./shell/shell.js', import.meta.url), 'utf8');
  const start = code.indexOf('      if (active && !active.paused)');
  const end = code.indexOf('      return false;', start);
  const active = { paused: false }; let left = 0;
  const step = vm.runInNewContext('(function(){' + code.slice(start, end) + '})', { active, pauseClass: on => active.paused = on, showBoard: () => left++ });
  step(); assert.equal(active.paused, true); step(); assert.equal(active.paused, false); assert.equal(left, 0);
  const boot = readFileSync(new URL('./boot.js', import.meta.url), 'utf8');
  assert.match(boot, /bridge.on\(HOST_ESCAPE, \(\) => hostEscape.kept\(\)\)/); assert.doesNotMatch(boot, /shutdown\('hold-esc'\)/);
});
