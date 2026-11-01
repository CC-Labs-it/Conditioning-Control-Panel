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
    const waits = [], timers = [];
    const button = { click: () => clicks++ };
    const document = {
        querySelector: selector => {
            assert.equal(selector, 'button.play-action');
            return ++queries > availableAfter ? button : null;
        },
        querySelectorAll: () => [],
    };
    const window = {};
    const context = { window, document, setTimeout: (f, ms) => { waits.push(ms); manual ? timers.push(f) : f(); } };
    return { run: () => runInNewContext(playlist, context), stop: () => runInNewContext(panic, context),
        timers, waits, counts: () => [queries, clicks] };
}

test('clicks the playlist play button once after hydration', async () => {
    const p = page(2);
    await p.run();
    assert.deepEqual(p.counts(), [3, 1]);
    assert.deepEqual(p.waits, [250, 250]);
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
