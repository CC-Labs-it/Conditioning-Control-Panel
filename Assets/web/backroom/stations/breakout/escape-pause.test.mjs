import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { createHostEscape } from '../../shared/host-escape.js';

const source = readFileSync(new URL('./station.js', import.meta.url), 'utf8');
const start = source.indexOf("    on(window, 'keydown', (e) => {");
const code = source.slice(start, source.indexOf("    on(window, 'keyup'", start));
assert.ok(start > 0 && code.includes('setPaused(!paused)'));

function page(hosted) {
  let time = 0, timer;
  const listeners = [], transitions = [], focus = [];
  const window = { addEventListener: (_, fn) => listeners.push(fn) };
  const dispatchEvent = e => listeners.forEach(fn => fn(e));
  const event = (repeat = false) => ({ key: 'Escape', repeat, preventDefault() {} });
  const host = createHostEscape({ win: window, doc: { body: { dispatchEvent } }, now: () => time,
    later: fn => { timer = fn; return 1; }, cancel: () => { timer = null; }, makeEvent: event });
  const context = { window, HTMLElement: class {}, options: { hidden: true },
    ctx: { hostKeepsEscape: hosted }, paused: false, menuOpen: false, flavourOpen: false,
    game: { snapshot: () => ({}) },
    setPaused(p) { context.paused = p; transitions.push(p); },
    on: (_, type, fn) => { assert.equal(type, 'keydown'); listeners.push(fn); },
    el: { querySelector: () => ({ focus: () => focus.push('resume') }) },
    canvas: { focus: () => focus.push('canvas') },
    back: () => assert.fail('Escape must never leave'),
  };
  vm.runInNewContext(code, context);
  return { context, transitions, focus, host, real: repeat => dispatchEvent(event(repeat)),
    flush() { const pending = timer; timer = null; pending?.(); }, later() { time += 2500; } };
}

for (const hosted of [false, true]) {
  for (const order of ['host-only', 'real-first', 'real-last']) {
    test(`${hosted ? 'desktop' : 'web'}, ${order}: kept Escape pauses once; Escape on the card resumes, never leaves`, () => {
      const p = page(hosted);
      if (order === 'real-first') p.real();
      p.host.kept(); p.host.kept();
      if (order === 'real-last') p.real();
      p.flush();
      assert.deepEqual(p.transitions, [true]);
      assert.deepEqual(p.focus, ['resume']);
      p.real(true);
      assert.deepEqual(p.transitions, [true], 'a held key cannot resume');
      p.later();
      if (order === 'real-first') p.real();
      p.host.kept();
      if (order === 'real-last') p.real();
      p.flush();
      assert.deepEqual(p.transitions, [true, false]);
      assert.deepEqual(p.focus, ['resume', 'canvas']);
    });
  }
}

test('Escape on the menu never leaves', () => {
  const p = page(true); p.context.menuOpen = true; p.real();
  assert.deepEqual(p.transitions, []);
});

test('Leave button and host kept-escape wiring remain available', () => {
  assert.match(source, /if\(action==='exit'&&globalThis\.chrome\?\.webview\)back\(\)/);
  const play = readFileSync(new URL('./play.html', import.meta.url), 'utf8');
  assert.match(play, /bridge\.on\(HOST_ESCAPE, \(\) => hostEscape\.kept\(\)\)/);
});
