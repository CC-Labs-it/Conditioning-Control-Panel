import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

// Execute the actual injected script and exit cleanup with a small event target, not a copy of the logic.
const source = readFileSync(new URL('../../CCP.Avalonia/Views/Windows/MainShellWindow.BrowserFullscreen.cs', import.meta.url), 'utf8');
const install = source.match(/BrowserFullscreenExitScript = @"([\s\S]*?)";/)[1];
const cleanup = source.match(/BrowserFullscreenCleanupScript = "([^"\n]+)";/)[1];
function target() {
  const handlers = new Map();
  return {
    addEventListener(type, fn) { const list = handlers.get(type) ?? new Set(); list.add(fn); handlers.set(type, list); },
    removeEventListener(type, fn) { handlers.get(type)?.delete(fn); },
    emit(type, event = {}) { for (const fn of [...(handlers.get(type) ?? [])]) fn(event); },
    count(type) { return handlers.get(type)?.size ?? 0; },
  };
}
test('double-click exits a forced host and cleanup restores normal clicks', () => {
  const document = target(), window = target(), messages = [];
  window.invokeCSharpAction = message => messages.push(message);
  const context = { document, window };
  runInNewContext(install, context);
  runInNewContext(install, context);
  window.emit('click', {});
  assert.deepEqual(messages, []);
  let prevented = false, stopped = false;
  // HTML fullscreen has already been lost: the shell still owns the fullscreen popout.
  window.emit('dblclick', { preventDefault() { prevented = true; }, stopImmediatePropagation() { stopped = true; } });
  assert.deepEqual(messages, ['ccp_browser_fullscreen_off']);
  assert.equal(window.count('dblclick'), 1);
  assert.ok(prevented && stopped);
  runInNewContext(cleanup, context);
  assert.equal(window.count('dblclick'), 0);
  assert.equal(document.count('dblclick'), 0);
  assert.equal(document.count('fullscreenchange'), 0);
  window.emit('dblclick', {});
  assert.equal(messages.length, 1);
});
