import { test } from 'node:test';
import assert from 'node:assert/strict';
import { exportCard } from '../ui/adcard/export.js';
import { GIFEncoder, quantize, applyPalette } from '../vendor/gifenc.esm.js';

function fixture() {
  const canvases = [], times = [];
  let replayed = 0;
  const pixels = new Uint8ClampedArray([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]);
  const win = {
    getComputedStyle: () => ({ backgroundColor: 'white' }),
    html2canvas: async () => {
      assert.ok(canvases.every(c => c.width === 0), 'the prior canvas is released before the next snapshot');
      const canvas = { width: 2, height: 2, getContext: () => ({ getImageData: () => ({ data: pixels.slice() }) }) };
      canvases.push(canvas); return canvas;
    },
  };
  const doc = { defaultView: win };
  const ad = {
    card: { ownerDocument: doc, offsetWidth: 2 },
    build: () => ({ end: 500, tail: 0, anims: [] }),
    sideAt: (_, t) => times.push(t), drawSpiralAt() {}, play() { replayed++; },
  };
  return { ad, canvases, times, pixels, get replayed() { return replayed; } };
}

test('streamed GIF preserves bytes and timing while releasing each snapshot', async () => {
  const f = fixture();
  // Cancel only the final file handoff, leaving the complete encoder path exercised.
  let complete = false;
  const result = await exportCard(f.ad, 'gif', { stop: () => complete, progress(i, total) { complete = i === total; } });
  assert.equal(result.status, 'cancelled');
  assert.equal(f.canvases.length, 7);
  assert.ok(f.canvases.every(c => c.width === 0 && c.height === 0));
  assert.deepEqual(f.times, [250, 250 + 1000 / 12, 250 + 2000 / 12, 500, 500, 500, 500]);
  assert.equal(f.replayed, 1);
});

test('encoder produces the original palette, delays and GIF bytes', async () => {
  const { createGifEncoder } = await import('../ui/adcard/export-gif.js');
  const f = fixture(), old = GIFEncoder(), streamed = createGifEncoder();
  for (let i = 0; i < 7; i++) {
    const palette = quantize(f.pixels, 256), delay = i === 6 ? 2500 : 83;
    old.writeFrame(applyPalette(f.pixels, palette), 2, 2, { palette, delay });
    streamed.frame({ data: f.pixels, width: 2, height: 2, delay });
  }
  old.finish(); assert.deepEqual(streamed.finish(), old.bytes());
});

test('cancellation between snapshots frees the canvas and restores the card', async () => {
  const f = fixture();
  await assert.rejects(exportCard(f.ad, 'gif', { stop: () => f.canvases.length === 2 }), /stopped/);
  assert.equal(f.canvases.length, 2);
  assert.ok(f.canvases.every(c => c.width === 0 && c.height === 0));
  assert.equal(f.replayed, 1);
});
