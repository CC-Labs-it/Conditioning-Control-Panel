/* ============================================================================
 * fx/spiral.js - the background spiral. Every spiral in CCP comes from the
 * Loom, imported, never forked (owner rule): this file only drives
 * arcademy/engine/loom/loomSpiral.js on a canvas, 30 fps, and holds still when
 * Gentle, photosafe or OS reduced motion is on.
 * ==========================================================================*/

import { drawSpiral, normalizeParams } from '../../arcademy/engine/loom/loomSpiral.js';

export const FPS_CAP = 30;

export function createSpiral(canvas, { reduced = false } = {}) {
  const ctx = canvas.getContext('2d', { alpha: false });
  let params = normalizeParams({ arms: 4, turns: 2, style: 'log', duty: 0.5, speed: 2, colors: ['#ff69b4'], bg: '#14060f' });
  let on = false, still = reduced, phase = 0, speed = 1, pulse = 0, last = 0, size = 0;

  function fit() {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const s = Math.ceil(Math.max(window.innerWidth, window.innerHeight) * 1.1 * dpr);
    if (s === size) return;
    size = s;
    canvas.width = canvas.height = s;
  }
  window.addEventListener('resize', fit);
  fit();

  function frame(now) {
    if (!on) return;
    requestAnimationFrame(frame);
    if (now - last < 1000 / FPS_CAP) return;
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    if (!still) phase = (phase + dt * 0.08 * speed) % 1;
    pulse = Math.max(0, pulse - dt * 2);
    const drawSize = size * (1 + pulse * 0.03);
    ctx.save();
    ctx.translate((size - drawSize) / 2, (size - drawSize) / 2);
    drawSpiral(ctx, drawSize, params, phase);
    ctx.restore();
  }

  return {
    show() { if (on) return; on = true; last = performance.now(); canvas.classList.add('on'); requestAnimationFrame(frame); },
    hide() { on = false; canvas.classList.remove('on'); },
    faster(mul) { speed = mul; },
    beat() { pulse = 1; },
    setStill(v) { still = !!v; },
    recolour(colors) { params = normalizeParams({ ...params, colors }); },
    get on() { return on; },
  };
}
