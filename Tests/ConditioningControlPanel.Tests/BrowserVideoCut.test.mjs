import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = path => readFileSync(new URL('../../ConditioningControlPanel/' + path, import.meta.url), 'utf8');
const reporter = source('Services/Browser/BrowserService.cs');
const marker = reporter.indexOf('if (window.__ccpMediaBound)');
const script = reporter.slice(reporter.lastIndexOf('(function()', marker), reporter.indexOf('})();', marker) + 5);
const page = source('MainWindow/MainWindow.Browser.cs');
const metadata = page.slice(page.indexOf('window.__ccpSetCutStart ='), page.indexOf('// NOTE: playback lifecycle'));

function fixture(duration = 1200) {
    const messages = [], timers = [], handlers = {};
    const video = { tagName: 'VIDEO', muted: false, offsetWidth: 400, paused: true, ended: false,
        readyState: 4, currentTime: 0, duration, played: { length: 0 },
        addEventListener: (name, fn) => { handlers[name] = fn; } };
    const context = vm.createContext({ video, Number, isFinite,
        window: { chrome: { webview: { postMessage: value => messages.push(JSON.parse(value)) } }, addEventListener() {} },
        document: { readyState: 'complete', querySelectorAll: () => [video], documentElement: {} },
        MutationObserver: class { observe() {} },
        setInterval: (fn, ms) => { timers.push({ fn, ms }); return timers.length; }, clearInterval() {},
        setTimeout() {}, clearTimeout() {} });
    return { video, messages, timers, handlers, context };
}

test('takeover metadata seeks only when the host supplies an offset', () => {
    const f = fixture();
    vm.runInContext(metadata, f.context);
    assert.deepEqual(f.messages, [{ type: 'ccpMedia', state: 'metadata', dur: 1200 }]);
    assert.equal(f.video.currentTime, 0);
    f.context.window.__ccpSetCutStart(960);
    assert.equal(f.video.currentTime, 960);
});

test('unknown stream duration never requests a random start', () => {
    for (const duration of [0, NaN, Infinity]) {
        const f = fixture(duration);
        vm.runInContext(metadata, f.context);
        assert.equal(f.messages.length, 0);
        assert.equal(f.video.currentTime, 0);
    }
});

test('reported played time excludes seek gaps and pauses', () => {
    const f = fixture();
    vm.runInContext(script, f.context);
    f.video.paused = false;
    f.handlers.playing({ target: f.video });
    const beat = f.timers.find(timer => timer.ms === 1000).fn;
    f.video.currentTime = 964;
    f.video.played = { length: 2, start: i => [0, 960][i], end: i => [1, 964][i] };
    beat();
    assert.equal(f.messages.at(-1).played, 5);
    f.video.currentTime = 1100;
    beat();
    assert.equal(f.messages.at(-1).played, 5);
    f.video.paused = true;
    const count = f.messages.length;
    beat();
    assert.equal(f.messages.length, count);
});
