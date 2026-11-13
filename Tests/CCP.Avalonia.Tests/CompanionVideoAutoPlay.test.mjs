import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

const source = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = source.match(/internal const string VideoAutoPlayScript = """\s*([\s\S]*?)\s*""";/)?.[1];
assert.ok(script, 'production script exists');
// The panic surface runs WebHostMedia.PanicPauseScript, a concatenation of plain C# string literals.
const media = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostMedia.cs', import.meta.url), 'utf8');
const panicBody = media.split('internal const string PanicPauseScript =')[1]?.split(/;\r?\n/)[0];
assert.ok(panicBody, 'production panic script exists');
const stopScript = [...panicBody.matchAll(/"([^"\n]*)"/g)].map(m => m[1]).join('');

function page({ availableAfter = 0, blocked = false, muted = false, manualTimers = false, deferredPlay = null } = {}) {
    const listeners = {};
    let queries = 0, plays = 0, enters = 0, exits = 0;
    const intervals = [], timers = [];
    let videoPauses = 0, audioPauses = 0;
    const audio = { pause: () => audioPauses++ };
    const video = {
        muted: true,
        addEventListener: (event, handler) => { listeners[event] = handler; },
        play: () => { plays++; return deferredPlay ?? (blocked ? Promise.reject(new Error('blocked')) : Promise.resolve()); },
        pause: () => videoPauses++,
        requestFullscreen: () => { enters++; return Promise.resolve(); },
    };
    const document = {
        querySelector: () => ++queries > availableAfter ? video : null,
        querySelectorAll: () => [video, audio],
        fullscreenElement: video,
        exitFullscreen: () => { exits++; },
    };
    const window = { __ccpMuted: muted };
    const context = { document, window, setTimeout: (f, ms) => { intervals.push(ms); if (manualTimers) timers.push(f); else f(); } };
    const run = () => runInNewContext(script, context);
    const stop = () => runInNewContext(stopScript, context);
    return { video, document, window, run, stop, listeners, intervals, timers,
        counts: () => ({ queries, plays, enters, exits }), pauses: () => [videoPauses, audioPauses] };
}

test('waits for video, plays, requests fullscreen and exits on end or double click', async () => {
    const p = page({ availableAfter: 2 });
    await p.run();
    assert.deepEqual(p.counts(), { queries: 3, plays: 1, enters: 1, exits: 0 });
    assert.equal(p.video.muted, false);
    p.listeners.ended();
    let prevented = 0;
    p.listeners.dblclick({ preventDefault: () => prevented++, stopPropagation: () => prevented++ });
    assert.equal(prevented, 2);
    assert.equal(p.counts().exits, 2);
});

test('blocked playback requests fullscreen once the user starts it and keeps the saved mute', async () => {
    const p = page({ blocked: true, muted: true });
    await p.run();
    await Promise.resolve();
    assert.equal(p.counts().enters, 0);
    assert.equal(p.video.muted, true);
    p.listeners.playing();
    assert.equal(p.counts().enters, 1);
});

test('gives up after twenty polls when no player appears', async () => {
    const p = page({ availableAfter: Infinity });
    await p.run();
    assert.equal(p.counts().queries, 21);
    assert.deepEqual(p.intervals, Array(20).fill(500));
    assert.equal(p.counts().plays, 0);
});


test('panic during polling prevents a later player from starting', async () => {
    const p = page({ availableAfter: 1, manualTimers: true });
    const running = p.run();
    assert.equal(p.timers.length, 1);
    p.stop();
    p.timers.shift()();
    await running;
    assert.deepEqual(p.pauses(), [1, 1]);
    assert.equal(p.counts().plays, 0);
    assert.equal(p.counts().enters, 0);
});

test('panic pauses page audio and video and exits active fullscreen', async () => {
    const p = page();
    await p.run();
    assert.equal(p.counts().enters, 1);
    p.stop();
    assert.deepEqual(p.pauses(), [1, 1]);
    assert.equal(p.counts().exits, 1);
    assert.equal(p.window._ccpTakeoverStopped, true);
});

test('panic exits a prefixed WebKit fullscreen too', () => {
    let exits = 0;
    const document = {
        querySelectorAll: () => [],
        webkitFullscreenElement: {},
        webkitExitFullscreen: () => { exits++; },
    };
    runInNewContext(stopScript, { document, window: {} });
    assert.equal(exits, 1);
});

test('panic prevents a late play promise or a later user play event from reopening fullscreen', async () => {
    let resolvePlay;
    const p = page({ deferredPlay: new Promise(resolve => { resolvePlay = resolve; }) });
    await p.run();
    p.stop();
    resolvePlay();
    await Promise.resolve();
    assert.equal(p.counts().enters, 0);

    const blocked = page({ blocked: true });
    await blocked.run();
    await Promise.resolve();
    blocked.stop();
    blocked.listeners.playing();
    assert.equal(blocked.counts().enters, 0);
});
