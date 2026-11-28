import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

// WPF BrowserService.cs:337-460: the always-on reporter, extracted from the production C# raw string.
const source = readFileSync(new URL('../../CCP.Avalonia/Views/Windows/MainShellWindow.BrowserMedia.cs', import.meta.url), 'utf8');
const script = source.match(/BrowserMediaReporterScript = """\s*([\s\S]*?)\s*""";/)?.[1];
assert.ok(script, 'production script exists');

function page(media) {
    const posted = [], timeouts = [], windowListeners = {};
    const window = { invokeCSharpAction: s => posted.push(JSON.parse(s)), addEventListener: (e, f) => { windowListeners[e] = f; } };
    const document = { readyState: 'complete', documentElement: {}, querySelectorAll: () => media };
    const context = {
        window, document, isFinite,
        MutationObserver: class { observe() {} },
        setInterval: () => 1, clearInterval: () => {},
        setTimeout: f => { timeouts.push(f); return timeouts.length; }, clearTimeout: () => {},
    };
    runInNewContext(script, context);
    return { posted, timeouts, windowListeners, run: () => runInNewContext(script, context) };
}

function element(props) {
    const listeners = {};
    return Object.assign({ muted: false, paused: true, ended: false, readyState: 0, duration: 90, currentTime: 0, offsetWidth: 640,
        played: { length: 0 }, addEventListener: (e, f) => { listeners[e] = f; } }, props, { listeners });
}

test('reports audible user playback, a held pause and leaving the page', () => {
    const video = element({ tagName: 'VIDEO' });
    const p = page([video]);
    p.run();   // a second injection binds nothing twice
    video.listeners.playing({ target: video });
    assert.deepEqual(p.posted, [{ type: 'ccpMedia', state: 'playing', pos: 0, dur: 90 }]);
    video.listeners.pause({ target: video });
    p.timeouts.shift()();
    assert.equal(p.posted.at(-1).reason, 'paused');
    p.windowListeners.pagehide();
    assert.deepEqual(p.posted.at(-1), { type: 'ccpMedia', state: 'stopped', reason: 'navigated' });
});

test('ignores muted previews and thumbnail-sized players', () => {
    const muted = element({ tagName: 'VIDEO', muted: true });
    const small = element({ tagName: 'VIDEO', offsetWidth: 120 });
    const p = page([muted, small]);
    muted.listeners.playing({ target: muted });
    small.listeners.playing({ target: small });
    assert.deepEqual(p.posted, []);
});

test('a video already playing when injected is reported', () => {
    const video = element({ tagName: 'VIDEO', paused: false, readyState: 4, duration: Infinity });
    const p = page([video]);
    assert.deepEqual(p.posted, [{ type: 'ccpMedia', state: 'playing', pos: 0, dur: 0 }]);
});
