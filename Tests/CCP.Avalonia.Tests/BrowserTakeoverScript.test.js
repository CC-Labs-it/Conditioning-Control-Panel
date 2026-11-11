// Run with node from the repository root. Exercise the actual injected script.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('CCP.Avalonia/Views/Tabs/SettingsTabView.BrowserMedia.cs', 'utf8');
const script = source.match(/BrowserTakeoverScript = """\s*([\s\S]*?)\s*""";/)[1];
(async () => {
    const events = {}, docEvents = {}, messages = [];
    let plays = 0, fullscreen = 0, exits = 0;
    const video = {
        muted: true,
        addEventListener: (name, fn) => events[name] = fn,
        play: () => { plays++; return Promise.resolve(); },
        requestFullscreen: () => { fullscreen++; document.fullscreenElement = video; docEvents.fullscreenchange(); return Promise.resolve(); }
    };
    const document = {
        fullscreenElement: null,
        querySelector: () => video,
        addEventListener: (name, fn) => docEvents[name] = fn,
        exitFullscreen: () => { exits++; document.fullscreenElement = null; docEvents.fullscreenchange(); return Promise.resolve(); }
    };
    const window = { invokeCSharpAction: msg => messages.push(JSON.parse(msg)) };
    await vm.runInNewContext(script, { window, document, setTimeout, Promise });
    await Promise.resolve();
    assert.equal(plays, 1);
    assert.equal(fullscreen, 1);
    assert.equal(video.muted, false);
    events.ended();
    assert.equal(exits, 1);
    assert.equal(messages.length, 1);
    assert.equal(messages[0].type, 'ccpFsExit');
    docEvents.fullscreenchange();
    assert.equal(messages.length, 1, 'exit reported only once');

    let resume;
    const pendingWindow = {};
    let queries = 0;
    const pendingDocument = { querySelector: () => ++queries === 1 ? null : video, addEventListener: () => {} };
    const task = vm.runInNewContext(script, {
        window: pendingWindow, document: pendingDocument, Promise,
        setTimeout: fn => { resume = fn; }
    });
    pendingWindow._ccpTakeoverStopped = true;
    resume();
    await task;
    assert.equal(plays, 1, 'a cancelled lookup cannot start playback');
    console.log('BrowserTakeoverScript: playback, fullscreen, exit and cancellation passed');
})().catch(error => { console.error(error); process.exitCode = 1; });
