// Run with node. Exercise the script with a small DOM and a WebHost message carrier.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../../CCP.Core/Services/Browser/BrowserAudioSyncScript.cs'), 'utf8')
    .split('public const string Source = """\n')[1].split('\n""";')[0];
new vm.Script(source); // Also fails if the ported JavaScript is syntactically invalid.
const messages = [], listeners = {}, frames = [];
let plays = 0;
const overlay = { style: {}, innerHTML: '' }, progress = { style: {} }, status = { textContent: '' };
const video = {
    currentSrc: 'https://example.org/video.mp4', currentTime: 12.5, duration: 90, paused: false,
    parentElement: { style: {}, appendChild() {} },
    querySelector() { return null; },
    addEventListener(name, handler) { listeners[name] = handler; },
    play() { plays++; return Promise.resolve(); }
};
const document = {
    body: { appendChild() {} },
    querySelector(selector) { return selector === 'video' ? video : null; },
    createElement() { return overlay; },
    getElementById(id) { return id === 'hapticProgress' ? progress : id === 'hapticStatus' ? status : null; }
};
const window = { invokeCSharpAction(message) { messages.push(JSON.parse(message)); } };
const context = { window, document, console: { log() {} }, requestAnimationFrame(fn) { frames.push(fn); },
    MutationObserver: class { observe() {} }, HTMLMediaElement: { prototype: { play() { plays++; return Promise.resolve(); } } } };
vm.runInNewContext(source, context);
const intercepted = video.play;
window.__hapticReady = true;
vm.runInNewContext(source, context); // Injection is idempotent.
assert.equal(video.play, intercepted);
assert.equal(window.__hapticReady, true);
window.__hapticReady = false;
video.play();
assert.equal(plays, 0);
assert.equal(overlay.style.display, 'flex');
window.__hapticUpdateProgress(37, 'Working');
assert.equal(progress.style.width, '37%');
assert.equal(status.textContent, 'Working');
assert.deepEqual(messages.shift(), { type: 'audioSyncVideoDetected', url: video.currentSrc, duration: 90 });
window.__hapticSignalReady();
assert.equal(plays, 1);
assert.equal(overlay.style.display, 'none');
window.__hapticShowOverlay('Loading');
assert.equal(overlay.style.display, 'flex');
assert.equal(status.textContent, 'Loading');
window.__hapticHideOverlay();
assert.equal(overlay.style.display, 'none');
frames.shift()(100);
assert.deepEqual(messages.shift(), { type: 'audioSyncState', currentTime: 12.5, paused: false, duration: 90 });
listeners.pause();
assert.deepEqual(messages.shift(), { type: 'audioSyncState', currentTime: 12.5, paused: true });
listeners.seeked();
assert.deepEqual(messages.shift(), { type: 'audioSyncSeek', currentTime: 12.5 });
listeners.ended();
assert.deepEqual(messages.shift(), { type: 'audioSyncEnded' });
window.__hapticRearm();
assert.deepEqual(messages.shift(), { type: 'audioSyncVideoDetected', url: video.currentSrc, duration: 90 });
console.log('BrowserAudioSyncScript: parse, interception, ready, messages and rearm passed');
