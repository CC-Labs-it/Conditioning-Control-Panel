import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = source.split('internal const string PlaylistAutoPlayScript = """')[1].split('""";')[0]
  .replace('__CCP_TOKEN__', '"request-token"');
async function run({ cancel = false, missing = false, webkit = true } = {}) {
  let clicks = 0, calls = 0;
  const messages = [];
  const send = message => messages.push(typeof message === 'string' ? JSON.parse(message) : message);
  const window = webkit ? { invokeCSharpAction: send } : { chrome: { webview: { postMessage: send } } };
  const document = { querySelector(selector) {
    assert.equal(selector, 'button.play-action');
    return !missing && ++calls > 1 ? { click() { clicks++; } } : null;
  }};
  const timers = [];
  const result = vm.runInNewContext(script, { window, document, setTimeout: fn => timers.push(fn) });
  if (cancel) window._ccpTakeoverStopped = true;
  while (timers.length) { timers.shift()(); await Promise.resolve(); }
  await result;
  return { clicks, messages };
}
assert.equal((await run()).clicks, 1);
assert.deepEqual(await run({ cancel: true }), { clicks: 0, messages: [] });
for (const webkit of [true, false]) {
  const result = await run({ missing: true, webkit });
  assert.equal(result.clicks, 0);
  assert.deepEqual(JSON.parse(JSON.stringify(result.messages)), [{ type: 'ccpPlaylistNoButton', token: 'request-token' }]);
}
