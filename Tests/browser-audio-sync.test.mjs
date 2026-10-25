import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';
const script = readFileSync(new URL('../CCP.Avalonia/Views/Controls/BrowserAudioSync.js', import.meta.url), 'utf8');
function page(url) {
  let plays = 0, frames = 0, tick;
  const messages = [];
  const video = { currentSrc: url, duration: 12, parentElement: { style: {}, appendChild() {} },
    play() { plays++; return Promise.resolve(); }, querySelector() {}, addEventListener() {} };
  const window = { chrome: { webview: { postMessage: m => messages.push(JSON.parse(m)) } } };
  const document = { body: {}, querySelector: () => video, getElementById: () => null,
    createElement: () => ({ style: {} }), addEventListener() {} };
  runInNewContext(script, { window, document, MutationObserver: class { observe() {} },
    requestAnimationFrame(cb) { frames++; tick = cb; }, HTMLMediaElement: { prototype: { play: video.play } } });
  return { window, video, messages, plays: () => plays, frames: () => frames, reattach: () => runInNewContext(script, { window }), tick: t => tick(t) };
}
test('first play reports URL and duration, ready resumes once', async () => {
  const p = page('https://example.invalid/video.mp4');
  await p.video.play();
  assert.equal(p.plays(), 0);
  assert.deepEqual(p.messages[0], { type: 'audioSyncVideoDetected', url: p.video.currentSrc, duration: 12 });
  p.window.__hapticSignalReady();
  assert.equal(p.plays(), 1);
  assert.equal(p.frames(), 1);
});
test('unsupported URL and a deliberate play after panic remain playable', async () => {
  const p = page('blob:unsupported');
  await p.video.play();
  assert.equal(p.plays(), 1);
  p.window.__hapticStopSync(); p.window.__hapticDisabled = true;
  await p.video.play();
  assert.equal(p.plays(), 2);
  assert.equal(p.frames(), 0);
});
test('reattaching re-enables first-play detection without autoplay', async () => {
  const p = page('https://example.invalid/video.mp4');
  p.window.__hapticDisabled = true; p.reattach();
  assert.equal(p.plays(), 0);
  await p.video.play();
  assert.equal(p.messages.length, 1);
});
test('a paused sync tick keeps reporting loop alive without attempting play', () => {
  const p = page('https://example.invalid/video.mp4');
  p.window.__hapticSignalReady(); p.video.paused = true;
  p.tick(100);
  assert.equal(p.plays(), 1);
  assert.equal(p.frames(), 2);
});
