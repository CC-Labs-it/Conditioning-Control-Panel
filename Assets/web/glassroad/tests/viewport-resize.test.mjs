import test from 'node:test';
import assert from 'node:assert/strict';
import { createViewportResize } from '../render/viewportResize.js';

function fixture(t, delayMs = 120) {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const state = { width: 390, height: 844, ratio: 1.25 }, allocations = [];
  const viewport = createViewportResize({
    renderer: { setDrawingBufferSize: (...args) => allocations.push(args) },
    size: () => state, ratio: () => state.ratio, delayMs,
  });
  viewport.apply();
  return { state, allocations, viewport };
}

test('phone rotation commits only the settled buffer, with no intermediate square allocation', t => {
  const { state, allocations, viewport } = fixture(t);
  state.width = 844; viewport.request();
  t.mock.timers.tick(60);
  state.height = 390; viewport.request();
  t.mock.timers.tick(119);
  assert.deepEqual(allocations, [[390, 844, 1.25]]);
  t.mock.timers.tick(1);
  assert.deepEqual(allocations, [[390, 844, 1.25], [844, 390, 1.25]]);
  state.width = 390; state.height = 844; viewport.request();
  t.mock.timers.tick(120);
  assert.deepEqual(allocations.at(-1), [390, 844, 1.25]);
});

test('duplicate observer notifications allocate nothing; render scale changes still apply', t => {
  const { state, allocations, viewport } = fixture(t);
  viewport.request(); t.mock.timers.tick(120);
  assert.equal(allocations.length, 1);
  state.ratio = 1; viewport.request(); t.mock.timers.tick(120);
  assert.deepEqual(allocations.at(-1), [390, 844, 1]);
  assert.equal(allocations.length, 2);
});

test('a hidden canvas and a disposed renderer never allocate a replacement buffer', t => {
  const { state, allocations, viewport } = fixture(t);
  state.height = 0; viewport.request(); t.mock.timers.tick(120);
  assert.equal(allocations.length, 1);
  state.height = 390; viewport.request(); viewport.dispose(); t.mock.timers.tick(120);
  viewport.request(); viewport.apply(); t.mock.timers.tick(120);
  assert.equal(allocations.length, 1);
});

test('desktop resizes immediately and still skips duplicate dimensions', t => {
  const { state, allocations, viewport } = fixture(t, 0);
  state.width = 1200; viewport.request(); viewport.request();
  assert.deepEqual(allocations, [[390, 844, 1.25], [1200, 844, 1.25]]);
});
