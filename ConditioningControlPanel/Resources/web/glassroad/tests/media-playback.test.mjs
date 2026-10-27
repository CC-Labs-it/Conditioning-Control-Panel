import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { createMediaPlayback, createMediaSlots } = await import('../render/media.js');
const { LinearFilter } = await import('three/webgpu');
hooks.deregister();

const settle = () => new Promise(resolve => setImmediate(resolve));
function video({ blocked = false } = {}) {
  return { currentTime: 0, duration: 10, loop: true, paused: true, ended: false, readyState: 2,
    videoWidth: 640, videoHeight: 360, seeking: false, blocked, plays: 0, pauses: 0, loads: 0,
    play() { this.plays++; if (this.blocked) return Promise.reject({ name: 'NotAllowedError' }); this.paused = false; return Promise.resolve(); },
    pause() { this.pauses++; this.paused = true; }, load() { this.loads++; }, removeAttribute() {},
    addEventListener() {}, removeEventListener() {},
  };
}
function harness() {
  let clock = 0;
  const listeners = new Map(), target = {
    addEventListener: (name, callback) => listeners.set(name, callback),
    removeEventListener: name => listeners.delete(name),
  };
  const playback = createMediaPlayback({ target, now: () => clock });
  return { playback, listeners, watch: dt => { clock += dt; playback.tick(); },
    tick: dt => { clock += dt; }, gesture: trusted => listeners.get('pointerdown')?.({ isTrusted: trusted }) };
}

function trackClock(clip, ignoreSeek = false) {
  let seconds = clip.currentTime;
  const seeks = [];
  Object.defineProperty(clip, 'currentTime', { get: () => seconds, set: value => {
    seeks.push(value); if (!ignoreSeek) seconds = value;
  } });
  return { seeks, nativeTime: value => { seconds = value; } };
}

test('eight borrowed card slots issue two initial play requests and retry each blocked decoder once per trusted gesture', async () => {
  const h = harness(), clips = [video({ blocked: true }), video({ blocked: true })];
  for (let i = 0; i < 8; i++) h.playback.retain(clips[i % 2]);
  await settle();
  assert.deepEqual(clips.map(v => v.plays), [1, 1]);
  assert.ok(h.playback.snapshot().every(v => v.slots === 4 && v.playError === 'NotAllowedError' && !v.advancing));
  h.gesture(false); assert.deepEqual(clips.map(v => v.plays), [1, 1]);
  clips.forEach(v => { v.blocked = false; });
  h.gesture(true); h.gesture(true); await settle();
  assert.deepEqual(clips.map(v => v.plays), [2, 2], 'pending requests are not repeated for sibling cards or gestures');
  assert.ok(h.playback.snapshot().every(v => !v.paused && !v.playError && !v.advancing), 'play success alone is not frame advancement');
  h.tick(.5); clips.forEach(v => { v.currentTime = .5; });
  assert.ok(h.playback.snapshot().every(v => v.advancing && v.advancedS === .5));
  h.playback.release(clips[0]);
  assert.equal(h.playback.snapshot()[0].slots, 3);
  assert.deepEqual(clips.map(v => v.pauses), [0, 0], 'borrowed ownership remains with provider');
  h.playback.dispose(); assert.equal(h.listeners.size, 0);
});

test('persistent autoplay rejection has a bounded retry count and detached sources lose gesture listeners', async () => {
  const h = harness(), clip = video({ blocked: true });
  h.playback.retain(clip); await settle();
  for (let i = 0; i < 8; i++) { h.gesture(true); await settle(); }
  assert.equal(clip.plays, 4, 'one initial request and three gesture retries');
  assert.equal(h.playback.snapshot()[0].playError, 'NotAllowedError');
  h.playback.release(clip); assert.equal(h.listeners.size, 0);
  assert.deepEqual(h.playback.snapshot(), []); assert.equal(clip.pauses, 0);
});

test('advancement survives a loop boundary but detects a frozen, paused or failed source', async () => {
  const h = harness(), clip = video(); clip.currentTime = 9.8;
  h.playback.retain(clip); await settle();
  h.tick(.4); clip.currentTime = .2;
  let s = h.playback.snapshot()[0];
  assert.ok(s.advancing && Math.abs(s.advancedS - .4) < 1e-6);
  h.tick(2); assert.equal(h.playback.snapshot()[0].advancing, false, 'first decoded frame cannot mask a stalled clock');
  h.tick(.1); clip.currentTime = .3; clip.paused = true;
  assert.equal(h.playback.snapshot()[0].advancing, false);
  clip.paused = false; clip.error = { code: 3 };
  s = h.playback.snapshot()[0]; assert.equal(s.error, 3); assert.equal(s.advancing, false);
  h.playback.dispose();
});

test('normal native loops cross the end without a watchdog seek or extra play request', async () => {
  const h = harness(), clip = video(), clock = trackClock(clip);
  h.playback.retain(clip); await settle();
  clock.nativeTime(9.98); h.watch(10);
  h.watch(.1); clock.nativeTime(.12); h.watch(.02);
  assert.deepEqual(clock.seeks, []); assert.equal(clip.plays, 1);
  assert.equal(h.playback.snapshot()[0].loopRecoveries, 0);
  h.playback.dispose();
});

test('a stalled shared end seeks and plays once, then requires real progress before another wrap recovery', async () => {
  const h = harness(), clip = video(), clock = trackClock(clip);
  for (let i = 0; i < 5; i++) h.playback.retain(clip);
  await settle();
  clock.nativeTime(10); h.watch(10);
  h.watch(.34); assert.deepEqual(clock.seeks, []);
  h.watch(.02); await settle();
  assert.deepEqual(clock.seeks, [0]); assert.equal(clip.plays, 2);
  let s = h.playback.snapshot()[0];
  assert.equal(s.loopRecoveries, 1); assert.equal(s.loopRecoveryPending, true);
  assert.equal(s.advancing, false); assert.equal(s.advancedS, 0, 'the recovery seek never counts as motion');
  h.watch(1); assert.deepEqual(clock.seeks, [0]);
  clock.nativeTime(.2); h.watch(.2); s = h.playback.snapshot()[0];
  assert.equal(s.loopRecoveryPending, false); assert.equal(s.advancing, true); assert.equal(s.advancedS, .2);
  clock.nativeTime(10); h.watch(9.8); h.watch(.36); await settle();
  assert.deepEqual(clock.seeks, [0, 0]); assert.equal(clip.plays, 3);
  assert.equal(h.playback.snapshot()[0].loopRecoveries, 2);
  h.playback.dispose();
});

test('a decoder that ignores the recovery seek is not repeatedly sought or played', async () => {
  const h = harness(), clip = video(), clock = trackClock(clip, true);
  h.playback.retain(clip); await settle();
  clock.nativeTime(10); h.watch(10); h.watch(.36); await settle();
  for (let i = 0; i < 10; i++) h.watch(1);
  assert.deepEqual(clock.seeks, [0]); assert.equal(clip.plays, 2);
  const s = h.playback.snapshot()[0]; assert.equal(s.loopRecoveryPending, true); assert.equal(s.advancing, false);
  h.playback.dispose();
});

test('non-looping, seeking, errored, paused, ended or undecoded videos are never loop-recovered', async () => {
  for (const state of [{ loop: false }, { seeking: true }, { error: { code: 3 } }, { paused: true }, { ended: true }, { readyState: 1 }, { duration: Infinity }]) {
    const h = harness(), clip = video(), clock = trackClock(clip);
    h.playback.retain(clip); await settle(); Object.assign(clip, state);
    clock.nativeTime(10); h.watch(10); h.watch(1);
    assert.deepEqual(clock.seeks, [], JSON.stringify(state)); assert.equal(clip.plays, 1);
    h.playback.dispose();
  }
});

test('last borrower release clears the end watchdog without touching host-owned playback', async () => {
  const h = harness(), clip = video(), clock = trackClock(clip);
  h.playback.retain(clip); await settle(); clock.nativeTime(10); h.watch(10); h.watch(.2);
  h.playback.release(clip); h.watch(1);
  assert.deepEqual(clock.seeks, []); assert.deepEqual(h.playback.snapshot(), []);
  assert.equal(clip.pauses, 0); assert.equal(clip.loads, 0); assert.equal(h.listeners.size, 0);
  h.playback.retain(clip); await settle(); h.watch(.2);
  assert.deepEqual(clock.seeks, [], 'a new borrower starts a fresh stall interval');
  h.watch(.16); await settle(); assert.deepEqual(clock.seeks, [0]);
  h.playback.dispose();
});

test('actual card renderer reports eight decoded faces separately from advancing sources and never unloads borrowed clips', async () => {
  const gradient = { addColorStop() {} };
  const context = new Proxy({}, { get: (o, k) => k in o ? o[k]
    : k === 'createLinearGradient' || k === 'createRadialGradient' ? () => gradient : () => {},
  });
  const make = (width, height) => ({ width, height, getContext: () => context });
  const extraSlots = Array.from({ length: 8 }, (_, i) => ({ id: 'card-' + i, frame: 'card', program: 1,
    C: [0, 8, -i * 55], X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1], w: 21.5, h: 9.4, s: i * 55 }));
  const media = createMediaSlots(null, { id: 'test' }, { layout: {}, samples: [], extraSlots, make, tier: 'high' });
  const clips = [video(), video()];
  for (let i = 0; i < 8; i++) media.setSource('card-' + i, { kind: 'video', el: clips[i % 2], borrowed: true, credit: 'scrolller' });
  await settle();
  assert.equal(media.stats().readyCardVideos, 0, 'decoded video waits for its first budgeted upload');
  media.update(1 / 60, { eye: [0, 8, 0] });
  const s = media.stats();
  assert.equal(s.readyCardVideos, 8); assert.equal(s.cardVideoDecoders, 2);
  assert.deepEqual(s.cardVideoPlayback.map(v => v.slots), [4, 4]);
  assert.ok(s.cardVideoPlayback.every(v => !v.advancing));
  assert.deepEqual(clips.map(v => v.plays), [1, 1]);
  media.setSource('card-0', null); assert.equal(media.stats().readyCardVideos, 7);
  media.dispose();
  assert.deepEqual(clips.map(v => [v.pauses, v.loads]), [[0, 0], [0, 0]]);
});

function stagedRenderer(count = 8) {
  const canvases = [], copies = [], gradient = { addColorStop() {} };
  const make = (width, height) => {
    const canvas = { width, height, pixels: null }, context = new Proxy({
      fillRect() { canvas.pixels = null; },
      drawImage(source, x, y, w, h) {
        if (source.copyError) throw { name: 'InvalidStateError' };
        if (source.videoWidth) { canvas.pixels = source; copies.push({ canvas, source, w, h }); }
      },
    }, { get: (o, k) => k in o ? o[k] : k === 'createLinearGradient' || k === 'createRadialGradient' ? () => gradient : () => {} });
    canvas.getContext = () => context; canvases.push(canvas); return canvas;
  };
  const extraSlots = Array.from({ length: count }, (_, i) => ({ id: 'card-' + i, frame: 'card', program: 1,
    C: [0, 8, -i * 3], X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1], w: 21.5, h: 9.4, s: i * 3 }));
  const media = createMediaSlots(null, { id: 'test' }, { layout: {}, samples: [], extraSlots, make, tier: 'high' });
  return { media, canvases, copies };
}
function texturesOf(media) {
  const textures = new Set();
  // Every own screen shares one material; the picture it shows is the mesh's userData.mediaTex.
  for (const o of media.object.children) if (o.name.startsWith('media-card-source') && o.userData.mediaTex) textures.add(o.userData.mediaTex);
  return [...textures];
}
test('Shared video textures take the decoded <video> directly, bounded and budgeted across eight faces', async () => {
  const h = stagedRenderer(), clips = [video(), video()];
  Object.assign(clips[0], { videoWidth: 1920, videoHeight: 1080 });
  Object.assign(clips[1], { videoWidth: 720, videoHeight: 1280 });
  const canvasCount = h.canvases.length, uploads = () => h.media.stats().sharedVideoUploadsTotal;
  for (let i = 0; i < 8; i++) h.media.setSource('card-' + i, { kind: 'video', el: clips[i % 2], borrowed: true });
  await settle(); assert.equal(uploads(), 0, 'decoded is not uploaded until budget allows');
  const textures = texturesOf(h.media);
  assert.equal(textures.length, 2);
  for (const texture of textures) assert.ok(h.canvases.includes(texture.image) && texture.image.width === 2, 'dark placeholder before the first frame');
  const versions = textures.map(t => t.version);
  h.media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(uploads(), 2, 'one upload per owner, not eight per face');
  for (const [k, texture] of textures.entries()) {
    // No 2D canvas hop (Firefox read every frame back to the CPU): render/videoUploads.js hands the video to WebGL.
    assert.ok(clips.includes(texture.image)); assert.ok(texture.version > versions[k]);
    assert.equal(texture.isVideoTexture, undefined, 'a regular Texture keeps the warmed sRGB sampler graph');
    assert.equal(texture.generateMipmaps, false); assert.equal(texture.minFilter, LinearFilter);
  }
  assert.equal(h.copies.length, 0, 'no drawImage of a video'); assert.equal(h.canvases.length, canvasCount, 'no per-face canvases');
  const before = uploads(); h.media.update(0, { eye: [0, 8, 0] });
  assert.equal(uploads(), before, 'paused upload budget makes no uploads');
  for (let i = 0; i < 120; i++) {
    h.media.update(1 / 60, { eye: [0, 8, 0] });
    assert.ok(h.media.stats().sharedVideoUploads <= h.media.stats().maxMediaUploads);
  }
  assert.ok(uploads() <= 2 + 2 * 2 * h.media.stats().videoUploadFps + 2, 'video cadence stays capped');
  // A clip whose size changes releases the old-size GPU allocation once, then keeps uploading.
  const disposed = []; textures.forEach(t => t.addEventListener('dispose', () => disposed.push(t)));
  Object.assign(clips[0], { videoWidth: 1280, videoHeight: 720 });
  for (let i = 0; i < 30; i++) h.media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(disposed.length, 1); assert.ok(clips.includes(disposed[0].image));
  h.media.setSource('card-0', null); assert.ok(textures.every(t => clips.includes(t.image)), 'other borrowers retain the clip');
  for (let i = 1; i < 8; i++) h.media.setSource('card-' + i, null);
  assert.ok(textures.every(t => h.canvases.includes(t.image) && t.image.width === 2 && t.image.height === 2), 'last release drops the decoder');
  assert.deepEqual(clips.map(c => c.pauses), [0, 0], 'borrowed decoders remain host owned');
  h.media.dispose();
});
test('Undecoded or failed clips keep the placeholder and retry within the same upload budget', async () => {
  const h = stagedRenderer(1), clip = video();
  h.media.setSource('card-0', { kind: 'video', el: clip, borrowed: true }); await settle();
  clip.readyState = 1; h.media.update(1 / 60, { eye: [0, 8, 0] });
  assert.equal(h.media.stats().readyCardVideos, 0); assert.equal(h.media.stats().sharedVideoUploadsTotal, 0);
  assert.deepEqual(h.media.stats().sharedVideoCopyErrors, [], 'a frame still decoding is not an error');
  clip.readyState = 2; clip.error = { code: 3 }; h.media.update(.1, { eye: [0, 8, 0] });
  assert.equal(h.media.stats().readyCardVideos, 0); assert.deepEqual(h.media.stats().sharedVideoCopyErrors, ['MediaError:3']);
  const texture = texturesOf(h.media)[0]; assert.ok(h.canvases.includes(texture.image), 'no black frame from a failed decoder');
  clip.error = null; h.media.update(.1, { eye: [0, 8, 0] });
  assert.equal(h.media.stats().readyCardVideos, 1); assert.deepEqual(h.media.stats().sharedVideoCopyErrors, []);
  assert.equal(texture.image, clip);
  // A later loop seek (readyState 1) skips the upload and keeps the last frame bound.
  clip.readyState = 1; const total = h.media.stats().sharedVideoUploadsTotal;
  h.media.update(.1, { eye: [0, 8, 0] });
  assert.equal(texture.image, clip); assert.equal(h.media.stats().sharedVideoUploadsTotal, total);
  h.media.dispose();
  assert.ok(h.canvases.includes(texture.image)); assert.equal(texture.image.width, 2);
});
test('Renderer-owned video uses the same direct path and releases its decoder on source off', async () => {
  const h = stagedRenderer(1), clip = video();
  h.media.setSource('card-0', { kind: 'video', el: clip }); await settle();
  h.media.update(.1, { eye: [0, 8, 0] });
  const texture = texturesOf(h.media)[0]; assert.equal(texture.image, clip);
  h.media.setSource('card-0', null);
  assert.equal(clip.pauses, 1); assert.equal(clip.loads, 1); assert.ok(h.canvases.includes(texture.image));
  h.media.dispose();
});
