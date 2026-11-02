import { CanvasTexture, SRGBColorSpace } from 'three/webgpu';
import { vec2, floor, mod, clamp } from 'three/tsl';
import { createFieldRenderer, composeFrame, drawFallbackFrame } from '../../arcademy/engine/loom/loomField.js';
import { loomVariation } from './loomVariety.js';

// Six actual Loom fields, baked behind the loading card. Rotation stays on the GPU.
// The shared atlas has one upload and no live canvas/context per pad, effect or wheel.
let shared = null, refs = 0;
export function acquireLoomAtlas(px = 256) {
  if (!shared) {
    const make = (w, h) => typeof document !== 'undefined'
      ? Object.assign(document.createElement('canvas'), { width: w, height: h }) : new OffscreenCanvas(w, h);
    const canvas = make(px * 3, px * 2), ctx = canvas.getContext('2d'), source = make(px, px), tile = make(px, px), g = tile.getContext('2d');
    let field = null;
    try { field = createFieldRenderer(source); } catch { /* Real Loom fallback. */ }
    for (let i = 0; i < 6; i++) {
      const params = loomVariation(i); params.layer2.enabled = false;
      const palettes = [['#ff4fae','#fff3fc','#812b9b'], ['#7affdc','#e6ffff','#2471bb'], ['#ffd97c','#fff6e4','#ca5b3c'],
        ['#e28aff','#f9eaff','#635bda'], ['#90ee94','#f3ffe2','#22866a'], ['#ff90c8','#e9b8ff','#5759b1']];
      params.layer.colors = palettes[i];
      if (field) composeFrame(g, field, params, 0, px, px); else drawFallbackFrame(g, params, 0, px, px);
      ctx.drawImage(tile, i % 3 * px, Math.floor(i / 3) * px);
    }
    field?.dispose?.();
    shared = new CanvasTexture(canvas); shared.colorSpace = SRGBColorSpace;
  }
  refs++;
  const texture = shared;
  let live = true;
  return { texture, dispose() { if (!live) return; live = false; if (--refs === 0) { shared.dispose(); shared = null; } } };
}

export function loomAtlasUv(at, style) {
  const p = clamp(at, .003, .997);
  return vec2(p.x.add(mod(style, 3)).div(3), p.y.add(floor(style.div(3))).div(2));
}
