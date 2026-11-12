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
    window.addEventListener = () => {};
    vm.runInNewContext(source, { window, document, URL, AbortController,
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
    frames = [{ offsetWidth: 300, contentDocument: null }];
    tick(); assert.equal(last().active, true);
    now += 11000; tick(); assert.equal(last().active, true); // unknown playback remains conservative
});

for (const [host, src, idle, playing] of [
    ['Vimeo', 'https://player.vimeo.com/video/123', { method: 'getPaused', value: true }, { method: 'getPaused', value: false }],
    ['YouTube', 'https://www.youtube.com/embed/123?enablejsapi=1', { event: 'onStateChange', info: 2 }, { event: 'onStateChange', info: 1 }]
]) test(`${host} embedded playback stays quiet only when idle`, () => {
    let now = 10000, tick, onMessage, sent = [], messages = [];
    const child = { postMessage: (message, origin) => sent.push({ message, origin }) };
    const frame = { offsetWidth: 300, src, contentDocument: null, contentWindow: child };
    const document = { baseURI: 'https://example.test/', querySelectorAll: s => s === 'iframe' ? [frame] : [], addEventListener() {} };
    const window = { invokeCSharpAction: m => messages.push(JSON.parse(m)), addEventListener: (name, fn) => { if (name === 'message') onMessage = fn; } };
    vm.runInNewContext(source, { window, document, URL, AbortController, setInterval: fn => { tick = fn; return 1; }, clearInterval() {}, Date: { now: () => now } });
    const last = () => messages.at(-1).active;
    const origin = new URL(src).origin;
    assert.equal(last(), true);
    assert.ok(sent.length > 0);
    if (host === 'YouTube') {
        assert.ok(sent.some(x => JSON.parse(x.message).func === 'addEventListener'));
        onMessage({ source: child, origin: new URL(src).origin, data: JSON.stringify({ event: 'onReady' }) });
        tick();
        const count = sent.filter(x => JSON.parse(x.message).func === 'addEventListener').length;
        onMessage({ source: child, origin: new URL(src).origin, data: JSON.stringify({ event: 'onReady' }) });
        tick();
        assert.equal(sent.filter(x => JSON.parse(x.message).func === 'addEventListener').length, count);
    }
    onMessage({ source: {}, origin, data: JSON.stringify(idle) });
    onMessage({ source: child, origin: 'https://evil.test', data: JSON.stringify(idle) });
    tick(); assert.equal(last(), true); // another frame or origin cannot clear the gate
    onMessage({ source: child, origin, data: JSON.stringify(idle) });
    now += 2100; tick(); assert.equal(last(), false);
    for (let i = 0; i < 30; i++) { now += 500; onMessage({ source: child, origin, data: JSON.stringify(idle) }); tick(); }
    assert.equal(last(), false); // a responsive idle player remains quiet
    onMessage({ source: child, origin, data: JSON.stringify(playing) });
    for (let i = 0; i < 30; i++) { now += 500; onMessage({ source: child, origin, data: JSON.stringify(playing) }); tick(); }
    assert.equal(last(), true);
    onMessage({ source: child, origin, data: JSON.stringify(idle) });
    now += 2100; tick(); assert.equal(last(), false);
    now += 4000; tick(); assert.equal(last(), true); // lost player state fails closed
});
