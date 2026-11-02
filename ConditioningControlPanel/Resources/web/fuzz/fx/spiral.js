/* ============================================================================
 * fx/spiral.js - the background spiral. Every spiral in CCP comes from the
 * Loom, imported, never forked (owner rule): this file only drives
 * arcademy/engine/loom/loomSpiral.js on a canvas. The Loom's motion is a pure
 * rotation, so it draws one frame and turns the canvas, 30 fps, and holds still
 * when Gentle, photosafe or OS reduced motion is on. Redrawing every frame cost
 * more than the rest of the late game's scripts together.
 *
 * Pass ctx and it follows the run: ctx.palette()'s colours, Spiral Colours and
 * Spiral Shapes, all through fx/spiral-bus.js, which paints this same canvas
 * only while something is actually changing.
 * ==========================================================================*/

import { drawSpiral, loopSpanRad, normalizeParams } from '../../arcademy/engine/loom/loomSpiral.js';
import { spiralBus } from './spiral-bus.js';

export const FPS_CAP = 30;
const BACKING_MAX = 1024;   // canvas pixels: it is stripes, and a phone pays for every one
const DRAW_SIZE = 2048;     // the Loom's rings follow the size it is told, so a small canvas still gets fine rings
const COVER = 1.42;         // a turning square still covers the page

/** manual: the caller drives it from ctx.frame with step(dt), so the page keeps one shared rAF and pause stops it. */
export function createSpiral(canvas, { reduced = false, manual = false, ctx = null } = {}) {
  const g = canvas.getContext('2d', { alpha: false });
  let params = normalizeParams({ arms: 4, turns: 2, style: 'log', duty: 0.5, speed: 2, colors: ['#ff69b4'], bg: '#14060f' });
  let on = false, still = reduced, phase = 0, speed = 1, pulse = 0, last = 0, size = 0, spent = 0, shown = '';
  const base = canvas.style.transform || '';
  canvas.style.willChange = 'transform';

  function paint(at = 0) {
    if (!size) return;
    if (canvas.width !== size) canvas.width = canvas.height = size;
    if (site) {
      const s = site.still();
      params = normalizeParams({ ...params, colors: s.colors, bg: s.bg });
      if (site.stillOnField(at)) return;
    }
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.scale(size / DRAW_SIZE, size / DRAW_SIZE);
    drawSpiral(g, DRAW_SIZE, { ...params, direction: 1 }, at);
    g.setTransform(1, 0, 0, 1, 0, 0);
  }

  const site = ctx ? spiralBus.site(ctx, canvas, g, {
    label: 'page',
    warm: true,
    ground: null,
    rates: { morph: 30, hold: 24, drift: 8, fade: 15 },
    /* The copy out of the field is what costs, and it goes with the area: start near the page's own size, at most 768. The site steps down if a paint runs long. */
    liveMax: Math.min(768, Math.ceil(Math.max(window.innerWidth, window.innerHeight) * 0.8 * Math.min(2, window.devicePixelRatio || 1))),
    paintStill: (at) => paint(at),
  }) : null;

  function fit() {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const s = Math.min(BACKING_MAX, Math.ceil(Math.max(window.innerWidth, window.innerHeight) * 1.1 * dpr));
    if (s === size) return;
    size = s;
    if (site?.live) return;
    canvas.width = canvas.height = s;
    paint(site ? ((site.phase % 1) + 1) % 1 : 0);
  }
  window.addEventListener('resize', fit);
  fit();

  function draw(dt) {
    if (!still) phase = (phase + dt * 0.08 * speed) % 1;
    pulse = Math.max(0, pulse - dt * 2);
    const rot = (params.direction ?? 1) * phase * loopSpanRad(params);
    const next = `${base} rotate(${rot.toFixed(4)}rad) scale(${(COVER * (1 + pulse * 0.03)).toFixed(4)})`;
    if (next !== shown) { shown = next; canvas.style.transform = next; }
    if (site) { site.speed = speed; site.frame(dt); }
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
    /** With ctx the palette holds the colours, so this is only for a spiral made without one. */
    recolour(colors) { if (site) return; params = normalizeParams({ ...params, colors }); paint(); },
    dispose() { on = false; window.removeEventListener('resize', fit); site?.remove(); },
    get on() { return on; },
  };
}
