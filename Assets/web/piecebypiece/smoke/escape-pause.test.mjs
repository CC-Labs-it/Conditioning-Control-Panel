import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { createHostEscape, HOST_ESCAPE } from '../ui/host-escape.js';

const source = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
const start = source.indexOf('  const pauseCard =');
const code = source.slice(start, source.indexOf('  // --- Q:', start));
assert.ok(start > 0 && code.includes('onHostMessage('));

function page(mode = 'local') {
  let time = 0, timer, click, message, dragging = false, menu = false, over = false;
  const listeners = [], transitions = [], clock = [], exits = [], events = [];
  const window = { PBP: { game: {
    isWatch: mode === 'watch', isOver: () => over,
    current: mode === 'online' ? { offerDraw() {} } : {},
    clock: { pause: () => clock.push('pause'), resume: () => clock.push('resume') },
  }, door: { isUp: () => menu } }, addEventListener: (_, fn) => listeners.push(fn) };
  const dispatchEvent = e => listeners.forEach(fn => fn(e));
  const event = (repeat = false) => ({ key: 'Escape', repeat });
  const nodes = new Map();
  const card = { querySelector(selector) {
    if (!nodes.has(selector)) nodes.set(selector, { focus() {} });
    return nodes.get(selector);
  }, setAttribute() {}, addEventListener: (_, fn) => { click = fn; } };
  const document = { body: { dispatchEvent, appendChild() {} }, createElement: () => card };
  const hostEscape = createHostEscape({ win: window, doc: document, now: () => time,
    later: fn => { timer = fn; return 1; }, cancel: () => { timer = null; }, makeEvent: event });
  vm.runInNewContext(code, { window, document, hostEscape, HOST_ESCAPE,
    onHostMessage: fn => { message = fn; }, postToHost: m => exits.push(m.type),
    bus: { on() {}, emit: name => events.push(name) }, board: {},
    createPauseHush: () => ({ set: p => transitions.push(p), returning: () => false }),
    pictureChoice: { available: () => false }, presentation: () => ({}),
    drag: { isDragging: () => dragging, drop: () => { dragging = false; } },
  });
  return { window, card, transitions, clock, exits, events,
    real: repeat => dispatchEvent(event(repeat)), kept: () => message({ type: HOST_ESCAPE }),
    flush() { const pending = timer; timer = null; pending?.(); },
    later() { time += 2500; }, drag() { dragging = true; },
    menu() { menu = true; }, finish() { over = true; },
    click(action) { click({ target: { closest: () => ({ dataset: { pause: action } }) } }); },
  };
}

for (const mode of ['local', 'online', 'watch']) {
  for (const order of ['host-only', 'real-first', 'real-last']) {
    test(`${mode}, ${order}: kept Escape pauses once; Escape on the card resumes, never leaves`, () => {
      const p = page(mode);
      if (order === 'real-first') p.real();
      p.kept(); p.kept();
      if (order === 'real-last') p.real();
      p.flush();
      assert.equal(p.card.hidden, false);
      assert.deepEqual(p.transitions, [true]);
      p.real(true);
      assert.deepEqual(p.transitions, [true], 'a held key cannot resume');
      p.later();
      if (order === 'real-first') p.real();
      p.kept();
      if (order === 'real-last') p.real();
      p.flush();
      assert.equal(p.card.hidden, true);
      assert.deepEqual(p.transitions, [true, false], 'resume still goes through pause hush');
      assert.deepEqual(p.clock, mode === 'local' ? ['pause', 'resume'] : []);
      assert.deepEqual(p.exits, []);
      assert.deepEqual(p.events, []);
    });
  }
}

test('Leave remains an explicit button for players and spectators', () => {
  for (const mode of ['local', 'watch']) {
    const p = page(mode); p.real(); p.click('leave');
    assert.deepEqual(p.exits, mode === 'local' ? ['pbp:exit'] : []);
    assert.deepEqual(p.events, mode === 'watch' ? ['watch-leave'] : []);
    assert.deepEqual(p.transitions, mode === 'local' ? [true] : [true, false]);
  }
});

test('Escape on the menu or after a game never leaves', () => {
  for (const state of ['menu', 'finish']) for (const mode of ['local', 'watch']) {
    const p = page(mode); p[state](); p.real();
    assert.deepEqual(p.exits, []);
    assert.deepEqual(p.events, []);
    assert.deepEqual(p.transitions, []);
  }
});

test('Escape still drops a dragged piece and pauses', () => {
  const p = page(); p.drag(); p.real();
  assert.deepEqual(p.transitions, [true]);
  assert.deepEqual(p.exits, []);
});
