import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = source.split('internal const string PlaylistAutoPlayScript = """')[1].split('""";')[0];
async function run(cancel) {
  let clicks = 0, calls = 0;
  const window = {};
  const document = { querySelector(selector) {
    assert.equal(selector, 'button.play-action');
    return ++calls > 1 ? { click() { clicks++; } } : null;
  }};
  const timers = [];
  const result = vm.runInNewContext(script, { window, document, setTimeout: fn => timers.push(fn) });
  if (cancel) window._ccpTakeoverStopped = true;
  timers.shift()();
  await result;
  return clicks;
}
assert.equal(await run(false), 1);
assert.equal(await run(true), 0);
