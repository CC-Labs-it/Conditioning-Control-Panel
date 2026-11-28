import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

const source = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = name => source.match(new RegExp(`internal const string ${name} = """\\s*([\\s\\S]*?)\\s*""";`))[1];

function page({ stopped = false, ended = false, fullscreen = false } = {}) {
    const posts = [], listeners = {};
    let requests = 0;
    const video = {
        isConnected: true, ended, muted: false,
        addEventListener: (event, cb) => { listeners[event] = cb; },
        play: () => Promise.resolve(),
        requestFullscreen: () => { requests++; return Promise.reject({ name: 'TypeError', message: 'no activation' }); }
    };
    const document = { fullscreenElement: fullscreen ? video : null, visibilityState: 'visible',
        hasFocus: () => false, querySelector: () => video, addEventListener: () => {} };
    const window = { _ccpTakeoverStopped: stopped, invokeCSharpAction: s => posts.push(JSON.parse(s)) };
    const context = { window, document, navigator: { userActivation: { isActive: false } } };
    return { posts, window, video, run: name => runInNewContext(script(name), context), count: () => requests };
}

test('first refusal reports the ask, activation and error', async () => {
    const p = page();
    await p.run('VideoAutoPlayScript');
    await Promise.resolve();
    assert.equal(p.posts[0].type, 'fsAsk');
    assert.equal(p.posts[1].type, 'fsDenied');
    assert.equal(p.posts[1].activation, false);
    assert.match(p.posts[1].reason, /TypeError: no activation/);
});

test('recheck reports the second refusal without making another ask', async () => {
    const p = page();
    p.run('FullscreenRecheckScript');
    await Promise.resolve();
    assert.equal(p.count(), 1);
    assert.equal(p.posts[0].type, 'fsDenied');
    assert.match(p.posts[0].reason, /^recheck TypeError/);
    assert.equal(p.posts[0].focus, false);
});

test('recheck refuses stopped, ended or fullscreen pages', () => {
    for (const state of [{ stopped: true }, { ended: true }, { fullscreen: true }]) {
        const p = page(state);
        p.run('FullscreenRecheckScript');
        assert.equal(p.count(), 0);
    }
});
