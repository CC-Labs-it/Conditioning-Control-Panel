import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

const source = readFileSync(new URL('../../CCP.Avalonia/Views/Windows/MainShellWindow.Browser.cs', import.meta.url), 'utf8');
const extract = name => {
    const script = source.match(new RegExp(`internal const string ${name} = """\\s*([\\s\\S]*?)\\s*""";`))?.[1];
    assert.ok(script, `${name} exists`);
    return script;
};
const playlist = extract('PlaylistAutoPlayScript');
const panic = extract('PanicBrowserMediaScript');

function page(availableAfter, manual = false) {
    let queries = 0, clicks = 0;
    const waits = [], timers = [], messages = [], windowEvents = {}, audioEvents = {}, docEvents = {}, intervals = [];
    let disconnected = 0, cleared = 0;
    const audio = { tagName: 'AUDIO', paused: false, ended: false, addEventListener: (event, handler) => { audioEvents[event] = handler; } };
    const audios = [audio], observers = [];
    const button = { click: () => clicks++ };
    const document = {
        querySelector: selector => {
            assert.equal(selector, 'button.play-action');
            return ++queries > availableAfter ? button : null;
        },
        querySelectorAll: selector => selector === 'audio' ? audios : [],
        body: {},
        addEventListener: (event, handler) => { docEvents[event] = handler; },
        removeEventListener: event => { delete docEvents[event]; },
    };
    const window = { invokeCSharpAction: m => messages.push(m), addEventListener: (event, handler) => { windowEvents[event] = handler; } };
    const context = { window, document,
        MutationObserver: class { constructor(callback) { observers.push(callback); } observe() {} disconnect() { disconnected++; } },
        setTimeout: (f, ms) => { waits.push(ms); if (manual || ms !== 250) timers.push(f); else f(); },
        setInterval: (f, ms) => { assert.equal(ms, 1000); intervals.push(f); return intervals.length; },
        clearInterval: id => { if (id) cleared++; }, clearTimeout: () => {} };
    return { run: (passive = false) => runInNewContext(passive ? playlist.replace('__CCP_PLAYLIST_PASSIVE__', 'yes') : playlist, context), stop: () => runInNewContext(panic, context),
        timers, waits, messages, windowEvents, audioEvents, docEvents, intervals, audio, audios, observers, button, window,
        cleanup: () => [disconnected, cleared], counts: () => [queries, clicks] };
}

test('clicks the playlist play button once after hydration', async () => {
    const p = page(2);
    await p.run();
    assert.deepEqual(p.counts(), [3, 1]);
    assert.deepEqual(p.waits, [250, 250, 3000]);
});

test('gives up after twenty polls if no play button appears', async () => {
    const p = page(Infinity);
    await p.run();
    assert.deepEqual(p.counts(), [21, 0]);
    assert.deepEqual(p.waits, Array(20).fill(250));
});

test('panic during hydration prevents a later click', async () => {
    const p = page(1, true);
    const running = p.run();
    p.stop();
    p.timers.shift()();
    await running;
    assert.deepEqual(p.counts(), [1, 0]);
});

const message = state => `ccp-playlist:__CCP_PLAYLIST_TOKEN__:${state}`;

test('reports playback, heartbeats, end and navigation', async () => {
    const p = page(0);
    await p.run();
    p.audioEvents.playing();
    p.intervals[0]();
    p.audioEvents.ended();
    p.windowEvents.pagehide();
    assert.deepEqual(p.messages, ['playing', 'progress', 'ended', 'navigated'].map(message));
    p.timers[0](); // the three-second fallback must not restart an ended session
    assert.equal(p.messages.length, 4);
});

test('reports a missing play button and assumes a click started after three seconds', async () => {
    const missing = page(Infinity);
    await missing.run();
    assert.deepEqual(missing.messages, [message('failed')]);
    const p = page(0);
    await p.run();
    p.timers[0]();
    assert.deepEqual(p.messages, [message('playing')]);
});

test('reports a pause after two seconds only if audio remains paused', async () => {
    const p = page(0);
    await p.run();
    p.audioEvents.playing();
    p.audio.paused = true;
    p.audioEvents.pause();
    assert.equal(p.waits.at(-1), 2000);
    p.audio.paused = false;
    p.timers[1]();
    assert.deepEqual(p.messages, [message('playing')]);
    p.audio.paused = true;
    p.audioEvents.pause();
    p.timers[2]();
    assert.deepEqual(p.messages, [message('playing'), message('ended')]);
});

test('binds audio added after the play click', async () => {
    const p = page(0), events = {};
    await p.run();
    p.audios.push({ paused: false, ended: false, addEventListener: (event, handler) => { events[event] = handler; } });
    p.observers[0]();
    events.playing();
    assert.deepEqual(p.messages, [message('playing')]);
});

test('reports a failed click and stops reporting after panic or replacement', async () => {
    const failed = page(0);
    failed.button.click = () => { throw new Error('blocked'); };
    await failed.run();
    assert.deepEqual(failed.messages, [message('failed')]);
    assert.deepEqual(failed.cleanup(), [1, 1]);
    const p = page(0);
    await p.run();
    p.stop();
    assert.deepEqual(p.cleanup(), [1, 1]);
    p.timers[0]();
    p.audioEvents.playing();
    p.intervals[0]();
    assert.deepEqual(p.messages, []);
    const replaced = page(0);
    await replaced.run();
    replaced.window.__ccpPlaylistToken = 'new';
    replaced.audioEvents.playing();
    replaced.windowEvents.pagehide();
    assert.deepEqual(replaced.messages, []);
});
test('a later user play reactivates reporting after panic', async () => {
    const p = page(0);
    await p.run();
    p.stop();
    await p.run(true);
    p.docEvents.playing({ target: p.audio });
    assert.deepEqual(p.messages, [message('playing')]);
    p.intervals[1]();
    assert.deepEqual(p.messages, [message('playing'), message('progress')]);
});
