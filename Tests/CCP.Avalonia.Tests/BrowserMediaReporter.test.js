// Run with node from the repo root. Exercise the actual injected script with a small DOM clock.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('CCP.Avalonia/Views/Tabs/SettingsTabView.BrowserMedia.cs', 'utf8');
const script = source.match(/BrowserMediaReporter = """\s*([\s\S]*?)\s*""";/)[1];
let now = 0, next = 1;
const timers = new Map(), messages = [], media = [], windowEvents = {};
function schedule(fn, delay, repeat) { const id = next++; timers.set(id, { fn, at: now + delay, delay, repeat }); return id; }
function advance(ms) {
    const end = now + ms;
    for (;;) {
        const due = [...timers].filter(([, t]) => t.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
        if (!due) break;
        const [id, timer] = due;
        now = timer.at;
        if (timer.repeat) timer.at += timer.delay; else timers.delete(id);
        timer.fn();
    }
    now = end;
}
const context = {
    window: { invokeCSharpAction: s => messages.push(JSON.parse(s)), addEventListener: (n, fn) => windowEvents[n] = fn },
    document: { readyState: 'complete', documentElement: {}, querySelectorAll: () => media },
    MutationObserver: class { constructor(fn) { this.fn = fn; } observe() {} },
    setInterval: (fn, ms) => schedule(fn, ms, true), clearInterval: id => timers.delete(id),
    setTimeout: (fn, ms) => schedule(fn, ms, false), clearTimeout: id => timers.delete(id)
};
function element(tagName, width, muted = false) {
    const events = {};
    const e = { tagName, offsetWidth: width, muted, paused: true, ended: false, readyState: 4,
        addEventListener: (n, fn) => events[n] = fn,
        emit: n => events[n]({ target: e }) };
    media.push(e);
    return e;
}
const preview = element('VIDEO', 100), muted = element('VIDEO', 400, true), video = element('VIDEO', 400);
vm.runInNewContext(script, context);
preview.paused = false; preview.emit('playing');
muted.paused = false; muted.emit('playing');
assert.equal(messages.length, 0, 'previews and muted media must not hold Takeover');
video.duration = 600; video.paused = false; video.emit('playing');
assert.equal(messages.at(-1).state, 'playing');
assert.equal(messages.at(-1).dur, 600, 'playing reports the finite duration');
video.duration = Infinity;
video.played = { length: 1, start: () => 2, end: () => 9 };
advance(1000);
assert.equal(messages.at(-1).state, 'progress');
assert.equal(messages.at(-1).dur, 0, 'a live stream reports unknown duration');
assert.equal(messages.at(-1).played, 7, 'progress reports played time');
video.paused = true; video.emit('pause');
advance(1999);
assert.notEqual(messages.at(-1).state, 'stopped');
video.paused = false; video.emit('playing');
advance(2);
assert.notEqual(messages.at(-1).state, 'stopped', 'resumed playback remains active');
video.paused = true; video.emit('pause');
advance(2000);
assert.equal(messages.at(-1).reason, 'paused');
// WPF mutes the web engine, but this head mutes individual elements.
context.window.__ccpMuted = true;
video.muted = true; video.paused = false; video.emit('playing');
assert.equal(messages.at(-1).state, 'playing', 'Home mute must not release Takeover');
video.ended = true; video.emit('ended');
context.window.__ccpMuted = false;
const audio = element('AUDIO', 0);
advance(3000); // late-added element is scanned
audio.paused = false; audio.emit('playing');
assert.equal(messages.at(-1).state, 'playing');
audio.ended = true; audio.emit('ended');
assert.equal(messages.at(-1).reason, 'ended');
windowEvents.pagehide();
assert.equal(messages.at(-1).reason, 'navigated');
console.log('BrowserMediaReporter: filter, heartbeat, pause/resume, audio, end and navigation OK');

// WPF queue recovery pauses both media kinds and exits page fullscreen.
const recoveryDeclaration = source.match(/internal const string BrowserMediaRecoveryScript =([\s\S]*?);\s*\n\s*private void CancelBrowserMediaRecovery/)[1];
const recovery = [...recoveryDeclaration.matchAll(/"(?:[^"\\]|\\.)*"/g)].map(m => JSON.parse(m[0])).join('');
const paused = [];
let fullscreenExited = 0;
const recoveryContext = {
    window: {},
    document: {
        fullscreenElement: {},
        querySelectorAll: () => [{ pause: () => paused.push('video') }, { pause: () => paused.push('audio') }],
        exitFullscreen: () => { fullscreenExited++; return Promise.resolve(); }
    }
};
vm.runInNewContext(recovery, recoveryContext);
assert.deepEqual(paused, ['video', 'audio']);
assert.equal(fullscreenExited, 1);
assert.equal(recoveryContext.window._ccpForcedFs, false);
assert.equal(recoveryContext.window._ccpTakeoverStopped, true);
console.log('BrowserMediaRecovery: pauses media and exits fullscreen');
