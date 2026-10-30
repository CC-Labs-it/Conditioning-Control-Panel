import test from 'node:test';
import assert from 'node:assert/strict';
import {
  BAKE, SHIPPED_IDLE_PALETTE, TUBE_CAMERA, tubeFrustum, frameCount, boxDownscale, medianCut, nearestColor, pixelate, smoothPass, checkReadback, runBake, installBaker
} from '../bake.js';

test('the tube bake is 2x the tube (384x618, same aspect as the shipped 192x309 clips)', () => {
  assert.deepEqual([BAKE.w, BAKE.h], [384, 618]);
  assert.ok(Math.abs(BAKE.w / BAKE.h - 192 / 309) < 0.001);
});

test('smooth pass: full resolution, soft alpha kept, at most 256 colours', () => {
  const frame = synthetic(96, 154);
  const palette = medianCut(frame.data, BAKE.smoothColors);
  assert.ok(palette.length <= 256 && palette.length > 48);
  const out = smoothPass(frame, nearestColor(palette));
  assert.equal(out.width, 96); assert.equal(out.height, 154);
  for (let i = 3; i < frame.data.length; i += 4) assert.equal(out.data[i], frame.data[i]);   // alpha untouched
  assert.ok([...out.data].some((v, i) => i % 4 === 3 && v > 0 && v < 255), 'soft edges survive');
  assert.ok(colours(out).size <= 256);
  // No 2x2 grain: some horizontal neighbour pair differs inside the body.
  let differ = 0;
  for (let i = 0; i < out.data.length - 4; i += 8) if (out.data[i + 3] === 255 && out.data[i + 7] === 255 && out.data[i] !== out.data[i + 4]) differ++;
  assert.ok(differ > 100);
});

test('runBake: the smooth pass by default, the pixel pass only on request', async () => {
  const run = async extra => {
    const seen = [];
    const source = {
      hasClip: () => true, duration: () => 0.1, render: () => synthetic(W * 2, H * 2),
      encode: frame => { seen.push(frame); return 'png'; }
    };
    await runBake({ request: { lookHash: 'q', clips: ['idle'], fps: 15, w: W, h: H, ...extra }, source, post: () => {}, yieldFrame: async () => {} });
    return seen[0];
  };
  const smooth = await run({}), pixel = await run({ pixel: true });
  assert.ok([...smooth.data].some((v, i) => i % 4 === 3 && v > 0 && v < 255), 'smooth keeps soft alpha');
  assert.ok([...pixel.data].every((v, i) => i % 4 !== 3 || v === 0 || v === 255), 'pixel pass cuts alpha hard');
  assert.ok(colours(smooth).size > colours(pixel).size);
});

const W = 192, H = 309;

// A deterministic noisy figure: a coloured gradient body with soft edges on a clear background.
function synthetic(width, height, seed = 7) {
  const data = new Uint8ClampedArray(width * height * 4);
  let s = seed;
  const rnd = () => ((s = (s * 1103515245 + 12345) >>> 0) / 4294967296);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const dx = (x - width / 2) / (width * 0.3), dy = (y - height * 0.55) / (height * 0.4);
    const d = Math.sqrt(dx * dx + dy * dy);
    const a = d < 0.9 ? 255 : d < 1.1 ? Math.round(255 * (1.1 - d) / 0.2) : 0;
    const i = (y * width + x) * 4;
    data[i] = (x * 5 + rnd() * 40) & 255; data[i + 1] = (y * 3 + rnd() * 40) & 255; data[i + 2] = ((x ^ y) + rnd() * 60) & 255; data[i + 3] = a;
  }
  return { data, width, height };
}

const colours = frame => {
  const set = new Set();
  for (let i = 0; i < frame.data.length; i += 4) if (frame.data[i + 3]) set.add((frame.data[i] << 16) | (frame.data[i + 1] << 8) | frame.data[i + 2]);
  return set;
};

test('pixel pass: at most 48 colours, hard alpha, grain 2 blocks pasted bottom-centre', () => {
  const frame = synthetic(W, H);
  const palette = medianCut(frame.data, BAKE.colors);
  assert.ok(palette.length <= 48 && palette.length > 1);
  const out = pixelate(frame, { grain: 2, palette });
  assert.equal(out.width, W); assert.equal(out.height, H);
  assert.ok(colours(out).size <= 48);
  for (let i = 3; i < out.data.length; i += 4) assert.ok(out.data[i] === 0 || out.data[i] === 255, 'alpha is thresholded');
  // 309 rows hold 154 two-pixel rows pasted at the bottom: row 0 stays clear (as in the shipped idle.webp).
  for (let x = 0; x < W; x++) assert.equal(out.data[x * 4 + 3], 0);
  for (let y = 1; y < H; y += 2) for (let x = 0; x < W; x += 2) {
    const at = (xx, yy) => out.data.slice((yy * W + xx) * 4, (yy * W + xx) * 4 + 4).join();
    const a = at(x, y);
    assert.equal(at(x + 1, y), a); assert.equal(at(x, y + 1), a); assert.equal(at(x + 1, y + 1), a);
  }
  assert.ok(checkReadback(out.data).ok);
});

test('pixel pass is deterministic and maps onto the given palette only', () => {
  const frame = synthetic(W, H, 99);
  const one = pixelate(frame, { palette: medianCut(frame.data) });
  const two = pixelate(frame, { palette: medianCut(frame.data) });
  assert.deepEqual(one.data, two.data);
  const shipped = pixelate(frame, { palette: SHIPPED_IDLE_PALETTE });
  const allowed = new Set(SHIPPED_IDLE_PALETTE.map(([r, g, b]) => (r << 16) | (g << 8) | b));
  for (const c of colours(shipped)) assert.ok(allowed.has(c));
  assert.equal(SHIPPED_IDLE_PALETTE.length, 44);
});

test('alpha threshold sits at 128 after the premultiplied grain average', () => {
  const frame = { data: new Uint8ClampedArray(2 * 2 * 4), width: 2, height: 2 };
  // One 2x2 block: two solid red pixels, two clear ones -> average alpha 127.5 -> 128 -> kept, still pure red.
  frame.data.set([255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0]);
  const kept = pixelate(frame, { palette: [[255, 0, 0], [0, 0, 0]] });
  assert.deepEqual([...kept.data.slice(0, 4)], [255, 0, 0, 255], 'no dark fringe from clear neighbours');
  frame.data.set([255, 0, 0, 254, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
  assert.equal(pixelate(frame, { palette: [[255, 0, 0]] }).data[3], 0);
});

test('tube camera keeps the shipped clips\' crop: 192x309 aspect, aim below 1.16 m, slightly right', () => {
  const f = tubeFrustum();
  assert.ok(Math.abs((f.right - f.left) / (f.top - f.bottom) - W / H) < 0.001);
  assert.ok(Math.abs(f.top - f.bottom - 649 * 2.52 / 708) < 1e-9);
  assert.ok(f.top + f.bottom < 0 && f.left + f.right > 0);
  assert.equal(TUBE_CAMERA.yawDeg, -20);
});

test('box downscale averages premultiplied 2x2 blocks', () => {
  const src = new Uint8ClampedArray([10, 20, 30, 255, 30, 40, 50, 255, 0, 0, 0, 0, 0, 0, 0, 0]);
  const out = boxDownscale(src, 2, 2, 2);
  assert.equal(out.width, 1); assert.equal(out.height, 1);
  assert.deepEqual([...out.data], [10, 15, 20, 128]);
});

test('self-check refuses an all-opaque or all-black readback', () => {
  const opaque = new Uint8ClampedArray(16).fill(200);
  assert.equal(checkReadback(opaque).reason, 'all opaque');
  assert.equal(checkReadback(new Uint8ClampedArray(16)).reason, 'all black');
  const black = new Uint8ClampedArray(16); black[3] = 255;
  assert.equal(checkReadback(black).reason, 'all black');
  assert.ok(checkReadback(new Uint8ClampedArray([9, 9, 9, 255, 0, 0, 0, 0])).ok);
});

function fakeSource(durations, { render } = {}) {
  const calls = [];
  const figure = synthetic(W * 2, H * 2);
  return {
    calls,
    disposed: 0,
    hasClip: name => name in durations,
    duration: name => durations[name],
    render(name, t) { calls.push([name, t]); return render ? render(name, t) : figure; },
    encode: frame => `png:${frame.width}x${frame.height}`,
    dispose() { this.disposed++; }
  };
}

test('frame count = round(duration * fps), t = i / fps, missing clips are skipped', async () => {
  assert.equal(frameCount(6, 15), 90); assert.equal(frameCount(2.03, 15), 30); assert.equal(frameCount(0.01, 15), 1);
  const source = fakeSource({ idle: 2, greeting: 1.4, jump: 0.5 });
  const sent = [];
  const result = await runBake({ request: { type: 'rain-bake', lookHash: 'abc', clips: ['idle', 'greeting', 'talk_a', 'jump'], fps: 15, w: W, h: H }, source, post: m => sent.push(m), yieldFrame: async () => {} });
  assert.ok(result.ok);
  assert.deepEqual(result.baked, { idle: 30, greeting: 21, jump: 8 });
  const frames = sent.filter(m => m.type === 'rain-bake-frame');
  assert.equal(frames.length, 30 + 21 + 8);
  assert.deepEqual(frames.filter(m => m.clip === 'greeting').map(m => m.index), [...Array(21).keys()]);
  assert.ok(frames.every(m => m.lookHash === 'abc' && m.png === `png:${W}x${H}`));
  const greet = source.calls.filter(c => c[0] === 'greeting').map(c => c[1]);
  assert.deepEqual(greet, [...Array(21).keys()].map(i => i / 15));
  assert.deepEqual(sent.find(m => m.type === 'rain-bake-done' && m.clip === 'talk_a'), { type: 'rain-bake-done', clip: 'talk_a', frames: 0, skipped: true, lookHash: 'abc' });
  const last = sent.at(-1);
  assert.equal(last.type, 'rain-bake-done'); assert.equal(last.all, true); assert.ok(!last.cancelled);
  const progress = sent.filter(m => m.type === 'rain-bake-progress' && m.clip === 'jump');
  assert.deepEqual(progress.at(-1), { type: 'rain-bake-progress', clip: 'jump', frame: 8, of: 8, lookHash: 'abc' });
  assert.equal(source.disposed, 1);
});

test('cancel stops the stream at the next frame', async () => {
  const source = fakeSource({ idle: 6, greeting: 6 });
  const sent = [];
  let cancelled = false;
  const result = await runBake({
    request: { lookHash: 'x', clips: ['idle', 'greeting'], fps: 15, w: W, h: H }, source,
    post: m => { sent.push(m); if (m.type === 'rain-bake-frame' && m.index === 4) cancelled = true; },
    yieldFrame: async () => {}, cancelled: () => cancelled
  });
  assert.ok(result.cancelled);
  assert.equal(sent.filter(m => m.type === 'rain-bake-frame').length, 5);
  assert.ok(!sent.some(m => m.type === 'rain-bake-done' && m.clip));
  assert.deepEqual(sent.at(-1), { type: 'rain-bake-done', all: true, cancelled: true, lookHash: 'x' });
  assert.equal(source.disposed, 1);
});

test('host messages: rain-bake-cancel and a newer rain-bake stop the running bake', async () => {
  const listeners = [];
  const webview = { addEventListener: (type, fn) => listeners.push(fn) };
  const sent = [];
  const sources = [];
  let tick;
  const baker = installBaker({ post: m => sent.push(m) }, {
    webview,
    makeSource: () => { const s = fakeSource({ idle: 1 }); sources.push(s); return s; }
  });
  const dispatch = data => listeners.forEach(fn => fn({ data }));
  // Drive the yield by hand: cancel lands while frame 2 is out.
  const realSetTimeout = globalThis.setTimeout;
  let frames = 0;
  globalThis.setTimeout = (fn) => { frames++; if (frames === 3) dispatch({ type: 'rain-bake-cancel' }); return realSetTimeout(fn, 0); };
  try {
    tick = baker.start({ type: 'rain-bake', lookHash: 'one', clips: ['idle'], fps: 15, w: W, h: H });
    assert.ok(baker.busy());
    await tick;
  } finally { globalThis.setTimeout = realSetTimeout; }
  assert.equal(sent.filter(m => m.type === 'rain-bake-frame').length, 3);
  assert.equal(sent.at(-1).cancelled, true);
  assert.ok(!baker.busy());
  assert.equal(sources[0].disposed, 1);

  // A newer look replaces the running bake: the old one ends cancelled after at most one frame.
  sent.length = 0;
  const first = baker.start({ lookHash: 'old', clips: ['idle'], w: W, h: H });
  const second = baker.start({ lookHash: 'new', clips: ['idle'], w: W, h: H });
  assert.ok((await first).cancelled);
  assert.ok((await second).ok);
  assert.ok(sent.filter(m => m.lookHash === 'old' && m.type === 'rain-bake-frame').length <= 1);
  assert.equal(sent.filter(m => m.lookHash === 'new' && m.type === 'rain-bake-frame').length, 15);
  assert.deepEqual(sent.find(m => m.lookHash === 'old' && m.all), { type: 'rain-bake-done', all: true, cancelled: true, lookHash: 'old' });
});

test('a broken readback posts boot-error and ends the bake', async () => {
  const source = fakeSource({ idle: 1 }, { render: () => ({ data: new Uint8ClampedArray(W * 2 * H * 2 * 4).fill(255), width: W * 2, height: H * 2 }) });
  const sent = [];
  const result = await runBake({ request: { lookHash: 'h', clips: ['idle'] }, source, post: m => sent.push(m), yieldFrame: async () => {} });
  assert.equal(result.ok, false);
  assert.equal(sent[0].type, 'boot-error');
  assert.match(sent[0].msg, /all opaque/);
  assert.deepEqual(sent[1], { type: 'rain-bake-done', all: true, error: 'all opaque', lookHash: 'h' });
  assert.ok(!sent.some(m => m.type === 'rain-bake-frame'));
});
