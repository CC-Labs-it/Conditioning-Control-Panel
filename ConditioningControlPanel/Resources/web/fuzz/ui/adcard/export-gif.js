import { GIFEncoder, quantize, applyPalette } from '../../vendor/gifenc.esm.js';

// Same palette and frame timing as the original exporter, with one frame in flight.
export function createGifEncoder() {
  const encoder = GIFEncoder();
  return {
    frame({ data, width, height, delay }) {
      const palette = quantize(data, 256);
      encoder.writeFrame(applyPalette(data, palette), width, height, { palette, delay });
    },
    finish() { encoder.finish(); return encoder.bytes(); },
  };
}
