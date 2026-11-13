import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const cs = readFileSync('CCP.Avalonia/Views/Tabs/SettingsTabView.BrowserCard.cs', 'utf8');
const source = cs.match(/internal const string RemixMediaScript = """([\s\S]*?)""";/)[1].replace('__CCP_TOKEN__', '"fixture"');

for (const native of [false, true]) test(`browser playback monitor ${native ? 'WebView2' : 'WebKit'}`, () => {
    const messages = [], events = new Map();
    let videos = [], frames = [], tick, now = 10000;
    const document = {
        querySelectorAll: selector => selector === 'iframe' ? frames : selector === 'video,audio' ? videos : [],
        addEventListener: (name, handler) => events.set(name, handler)
    };
    const window = native ? { chrome: { webview: { postMessage: m => messages.push(JSON.parse(m)) } } }
        : { invokeCSharpAction: m => messages.push(JSON.parse(m)) };
    vm.runInNewContext(source, { window, document, AbortController,
        setInterval: fn => { tick = fn; return 1; }, clearInterval() {}, Date: { now: () => now } });
    const last = () => messages.at(-1);
    assert.equal(last().active, false);
    videos = [{ tagName: 'VIDEO', paused: false, ended: false, muted: false, offsetWidth: 250 }];
    events.get('playing')();
    assert.equal(last().active, true);
    assert.equal(last().id, 'fixture');
    videos[0].paused = true;
    events.get('pause')();
    assert.equal(last().active, true); // WPF holds transient pauses for two seconds
    now += 2100; tick();
    assert.equal(last().active, false);
    videos[0] = { tagName: 'VIDEO', paused: false, ended: false, muted: true, offsetWidth: 250 };
    tick(); assert.equal(last().active, true); // browser mute must not hide an active video
    videos[0].offsetWidth = 100;
    now += 2100; tick(); assert.equal(last().active, false); // small muted previews are ignored
    assert.equal(last().unknown, false);
    frames = [{ offsetWidth: 300, contentDocument: null }];
    tick(); assert.equal(last().active, false); assert.equal(last().unknown, true); // the head asks the engine
    frames = [{ offsetWidth: 300, get contentDocument() { throw new Error('SecurityError'); } }];
    tick(); assert.equal(last().unknown, true);
    frames = [{ offsetWidth: 300, contentDocument: { querySelectorAll: s => s === 'video,audio' ? [{ tagName: 'AUDIO', paused: false, ended: false, muted: false }] : [] } }];
    tick(); assert.equal(last().active, true); assert.equal(last().unknown, false); // readable frames are inspected
});
