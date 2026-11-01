import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

const source = readFileSync(new URL('../../CCP.Avalonia/Views/Windows/MainShellWindow.Browser.cs', import.meta.url), 'utf8');
const script = source.match(/internal const string VideoAutoPlayScript = """\s*([\s\S]*?)\s*""";/)?.[1];
assert.ok(script, 'production script exists');

function page({ availableAfter = 0, blocked = false, muted = false } = {}) {
    const listeners = {};
    let queries = 0, plays = 0, enters = 0, exits = 0;
    const intervals = [];
    const video = {
        muted: true,
        addEventListener: (event, handler) => { listeners[event] = handler; },
        play: () => { plays++; return blocked ? Promise.reject(new Error('blocked')) : Promise.resolve(); },
        requestFullscreen: () => { enters++; return Promise.resolve(); },
    };
    const document = {
        querySelector: () => ++queries > availableAfter ? video : null,
        fullscreenElement: video,
        exitFullscreen: () => { exits++; },
    };
    const window = { __ccpMuted: muted };
    const run = () => runInNewContext(script, { document, window, setTimeout: (f, ms) => { intervals.push(ms); f(); } });
    return { video, window, run, listeners, intervals, counts: () => ({ queries, plays, enters, exits }) };
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
