/* ============================================================================
 * fx/spiral.js - the background spiral. Every spiral in CCP comes from the
 * Loom, imported, never forked (owner rule): this file only drives
 * arcademy/engine/loom/loomSpiral.js on a canvas. The Loom's motion is a pure
 * rotation, so it draws one frame and turns the canvas, 30 fps, and holds still
 * when Gentle, photosafe or OS reduced motion is on. Redrawing every frame cost
 * more than the rest of the late game's scripts together.
 * ==========================================================================*/

import { drawSpiral, loopSpanRad, normalizeParams } from '../../arcademy/engine/loom/loomSpiral.js';

export const FPS_CAP = 30;
const BACKING_MAX = 1024;   // canvas pixels: it is stripes, and a phone pays for every one
const DRAW_SIZE = 2048;     // the Loom's rings follow the size it is told, so a small canvas still gets fine rings
const COVER = 1.42;         // a turning square still covers the page

/** manual: the caller drives it from ctx.frame with step(dt), so the page keeps one shared rAF and pause stops it. */
export function createSpiral(canvas, { reduced = false, manual = false } = {}) {
  const ctx = canvas.getContext('2d', { alpha: false });
  let params = normalizeParams({ arms: 4, turns: 2, style: 'log', duty: 0.5, speed: 2, colors: ['#ff69b4'], bg: '#14060f' });
  let on = false, still = reduced, phase = 0, speed = 1, pulse = 0, last = 0, size = 0, spent = 0, shown = '';
  const base = canvas.style.transform || '';
  canvas.style.willChange = 'transform';

  function paint() {
    if (!size) return;
    ctx.save();
    ctx.scale(size / DRAW_SIZE, size / DRAW_SIZE);
    drawSpiral(ctx, DRAW_SIZE, { ...params, direction: 1 }, 0);
    ctx.restore();
  }

  function fit() {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const s = Math.min(BACKING_MAX, Math.ceil(Math.max(window.innerWidth, window.innerHeight) * 1.1 * dpr));
    if (s === size) return;
    size = s;
    canvas.width = canvas.height = s;
    paint();
  }
  window.addEventListener('resize', fit);
  fit();

  function draw(dt) {
    if (!still) phase = (phase + dt * 0.08 * speed) % 1;
    pulse = Math.max(0, pulse - dt * 2);
    const rot = (params.direction ?? 1) * phase * loopSpanRad(params);
    const next = `${base} rotate(${rot.toFixed(4)}rad) scale(${(COVER * (1 + pulse * 0.03)).toFixed(4)})`;
    if (next !== shown) { shown = next; canvas.style.transform = next; }
  }

  function frame(now) {
    if (!on) return;
    requestAnimationFrame(frame);
    if (now - last < 1000 / FPS_CAP) return;
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    draw(dt);
  }

  return {
    show() { if (on) return; on = true; last = performance.now(); canvas.classList.add('on'); if (!manual) requestAnimationFrame(frame); },
    hide() { on = false; canvas.classList.remove('on'); },
    /** One step at most FPS_CAP times a second. */
    step(dt) {
      if (!on) return;
      spent += dt;
      if (spent < 1 / FPS_CAP) return;
      draw(Math.min(0.1, spent));
      spent = 0;
    },
    faster(mul) { speed = mul; },
    beat() { pulse = 1; },
    setStill(v) { still = !!v; },
    recolour(colors) { params = normalizeParams({ ...params, colors }); paint(); },
    dispose() { on = false; window.removeEventListener('resize', fit); },
    get on() { return on; },
  };
}
