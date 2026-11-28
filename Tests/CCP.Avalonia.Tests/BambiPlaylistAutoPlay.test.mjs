import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

const source = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = source.match(/internal const string PlaylistAutoPlayScript = """\s*([\s\S]*?)\s*""";/)?.[1];
assert.ok(script);
const media = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostMedia.cs', import.meta.url), 'utf8');
const panicBody = media.split('internal const string PanicPauseScript =')[1]?.split(/;\r?\n/)[0];
const panic = [...panicBody.matchAll(/"([^"\n]*)"/g)].map(m => m[1]).join('');

function page({ availableAfter = 0, carrier = 'webkit' } = {}) {
    let queries = 0, clicks = 0, observer;
    const messages = [], timers = [], audios = [];
    const button = { click: () => clicks++ };
    const window = carrier === 'webkit'
        ? { invokeCSharpAction: json => messages.push(JSON.parse(json)) }
        : { chrome: { webview: { postMessage: message => messages.push(message) } } };
    const document = {
        body: {}, querySelector: () => ++queries > availableAfter ? button : null,
        querySelectorAll: selector => selector === 'audio' ? audios : [],
    };
    const context = { window, document, JSON, setTimeout: (fn, ms) => timers.push({ fn, ms }),
        MutationObserver: class { constructor(fn) { observer = fn; } observe() {} } };
    function addAudio() {
        const events = {};
        const audio = { addEventListener: (name, fn) => { events[name] = fn; }, pause() {} };
        audios.push(audio);
        return { audio, events };
    }
    return { run: () => runInNewContext(script, context), stop: () => runInNewContext(panic, context),
        addAudio, changed: () => observer(), timers, messages, clicks: () => clicks };
}

test('clicks the hydrated playlist, binds present and later audio, and posts lifecycle', async () => {
    const p = page({ carrier: 'windows' });
    const first = p.addAudio();
    await p.run();
    assert.equal(p.clicks(), 1);
    first.events.playing();
    const next = p.addAudio();
    p.changed();
    next.events.playing();
    next.events.ended();
    p.timers.shift().fn();
    assert.deepEqual(p.messages.map(m => m.type), ['videoStarted', 'videoEnded']);
    assert.equal(p.messages[1].reason, 'ended');
});

test('polls for hydration, reports missing button, and uses the WebKit carrier', async () => {
    const p = page({ availableAfter: Infinity });
    const running = p.run();
    for (let i = 0; i < 20; i++) {
        assert.equal(p.timers[0].ms, 250);
        p.timers.shift().fn();
        await Promise.resolve();
    }
    await running;
    assert.equal(p.clicks(), 0);
    assert.equal(p.messages[0].type, 'videoEnded');
    assert.equal(p.messages[0].reason, 'noPlayButton');
});

test('panic cancels a pending click and suppresses later callbacks', async () => {
    const pending = page({ availableAfter: 1 });
    const running = pending.run();
    pending.stop();
    pending.timers.shift().fn();
    await running;
    assert.equal(pending.clicks(), 0);
    assert.equal(pending.messages.length, 0);

    const playing = page();
    const audio = playing.addAudio();
    await playing.run();
    playing.stop();
    audio.events.playing();
    audio.events.ended();
    playing.timers.shift().fn();
    assert.equal(playing.messages.length, 0);
});

test('fallback reports a start once when no audio playing event arrives', async () => {
    const p = page();
    await p.run();
    assert.equal(p.timers[0].ms, 3000);
    p.timers.shift().fn();
    assert.equal(p.messages.length, 1);
    assert.equal(p.messages[0].type, 'videoStarted');
});

test('clicks after the play button appears during hydration', async () => {
    const p = page({ availableAfter: 1 });
    const running = p.run();
    assert.equal(p.clicks(), 0);
    p.timers.shift().fn();
    await running;
    assert.equal(p.clicks(), 1);
});
