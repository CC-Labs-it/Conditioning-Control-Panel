// Run with node. Exercises the actual document-start and parent scripts with isolated frame globals.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('CCP.Avalonia/Views/Tabs/SettingsTabView.BrowserCard.cs', 'utf8');
const script = name => {
  const match = source.match(new RegExp('internal const string ' + name + ' = """([\\s\\S]*?)""";'));
  assert.ok(match, name);
  return match[1];
};
const reports = [], rootEvents = {}, rootTimers = [];
const root = {
  addEventListener: (event, handler) => { rootEvents[event] = handler; },
  invokeCSharpAction: message => reports.push(JSON.parse(message))
};
root.top = root;
const rootDocument = {
  querySelectorAll: query => query === 'iframe' ? [{ offsetWidth: 400, contentDocument: null }] : [],
  addEventListener: () => {}
};
vm.runInNewContext(script('RemixMediaScript').replace('__CCP_TOKEN__', '"current"'), {
  window: root, document: rootDocument, AbortController, Date,
  setInterval: f => { rootTimers.push(f); return 1; }, clearInterval: () => {}
});
assert.equal(reports.at(-1).unknown, true);
assert.equal(reports.at(-1).interrupting, false);
let now = 10000;
const Clock = { now: () => now };
function frame() {
  const events = {}, timers = [];
  const player = { tagName: 'VIDEO', offsetWidth: 400, paused: true, ended: false, muted: false };
  const child = { top: { postMessage: data => rootEvents.message({ source: child, data }) } };
  const document = { querySelectorAll: () => [player], addEventListener: (name, f) => { events[name] = f; } };
  vm.runInNewContext(script('FrameMediaScript'), { window: child, document, Date: Clock, Math, setInterval: f => timers.push(f) });
  return { player, events, timers };
}
const first = frame();
assert.equal(reports.at(-1).active, false);
assert.equal(reports.at(-1).id, 'current');
first.player.paused = false;
first.events.playing({ type: 'playing' });
const firstId = reports.at(-1).frame;
assert.equal(reports.at(-1).active, true);
const second = frame();
assert.notEqual(reports.at(-1).frame, firstId);
assert.equal(reports.at(-1).active, false);
first.player.paused = true;
first.events.pause({ type: 'pause' });
assert.equal(reports.at(-1).active, true); // WPF's two-second pause debounce
now += 2100;
first.timers[0]();
assert.equal(reports.at(-1).active, false);
first.player.paused = false; first.player.muted = true;
first.events.playing({ type: 'playing' });
assert.equal(reports.at(-1).active, false);
console.log('frame reporter: idle, playing, concurrent frames, pause and mute passed');
