import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(process.env.CCP_CUT_SOURCE || new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = source.split('internal const string VideoAutoPlayScript = """')[1].split('""";')[0];
const listeners = {};
const messages = [];
let seeks = [];
const video = { duration: 120, readyState: 0, addEventListener: (name, fn) => listeners[name] = fn,
  set currentTime(value) { seeks.push(value); }, play: () => Promise.resolve() };
const window = { chrome: { webview: { postMessage: m => messages.push(JSON.parse(m)) } } };
vm.runInNewContext(script, { window, document: { querySelector: () => video }, Number, setTimeout });
assert.equal(messages.length, 0);
assert.deepEqual(seeks, []);
listeners.loadedmetadata();
assert.equal(messages.length, 1);
assert.equal(messages[0].dur, 120);
assert.deepEqual(seeks, []);
window.__ccpSetCutStart(12);
assert.deepEqual(seeks, [12]);
