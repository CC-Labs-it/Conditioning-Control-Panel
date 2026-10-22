/* render/mediaPlaceholders.js - the animated placeholders every media slot shows until a real source
 * arrives (owner 2026-10-22: "i dont see where the medias from scroller would show, we can use
 * placeholders"). Procedural 2D canvases only: nothing is downloaded and nothing asks Scrolller.
 *
 * Programs (each a shared small canvas, many slots show one):
 *   loom-a, loom-b  a Loom spiral, drawn ONCE through the Loom's own renderer
 *                   (arcademy/engine/loom/loomField.js, imported, never forked); the turn happens in
 *                   the slot shader, so it is smooth at any redraw rate. Drawn once (loomSwapS
 *                   can step its colours, off by default: a whole-canvas swap is a large step);
 *                   the shader's hue drift gives the variety.
 *   field           a slow lava field of saturated colour blobs
 *   shapes          nested turning polygons and orbiting rings
 *   mark            the Racing Thoughts mark: a road ribbon running into a spiral eye
 *   kaleido         mirrored wedges of drifting colour
 * Photosafe: every program moves position and hue only, slowly; no frame brightens or darkens a large
 * area at once (the 3 Hz law), and the Loom turn is capped in mediaMath.safeSpin. Gentle passes half
 * the time (render/media.js) and the slot shader halves contrast.
 * Three-free (a plain 2D context), so the node suite can draw every program into a stub context.
 */
import { safeSpin } from './mediaMath.js';


// The Loom, from its own tree (phone pack: ship arcademy/engine/loom/ beside glassroad/, see MEDIA.md).
// Missing, the Loom programs fall back to the field program (never a forked spiral).
let LOOM = null;
try { LOOM = await import('../../arcademy/engine/loom/loomField.js'); } catch { LOOM = null; }
export const loomReady = () => !!LOOM;

const TAU = Math.PI * 2;
const css = (c) => 'rgb(' + Math.round(c[0] * 255) + ',' + Math.round(c[1] * 255) + ',' + Math.round(c[2] * 255) + ')';
const cssA = (c, a) => 'rgba(' + Math.round(c[0] * 255) + ',' + Math.round(c[1] * 255) + ',' + Math.round(c[2] * 255) + ',' + a + ')';
const hex = (c) => '#' + c.map((v) => Math.round(Math.min(1, Math.max(0, v)) * 255).toString(16).padStart(2, '0')).join('');
const pick = (cols, i) => cols[((i % cols.length) + cols.length) % cols.length];
/** A colour between ring entries: x = 2.3 is 30 percent of the way from entry 2 to 3 (smooth, never a step). */
const at = (cols, x) => { const i = Math.floor(x), f = x - i, a = pick(cols, i), b = pick(cols, i + 1); return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f]; };

/** Loom params for a placeholder: v2 schema through the Loom's own normalizer. */
export function loomParams(kind, cols, step = 0) {
  if (!LOOM) return null;
  const a = pick(cols, step), b = pick(cols, step + 3), bg = pick(cols, step + 6);
  const dark = bg.map((v) => v * 0.18);
  const layer = kind === 'loom-b'
    ? { arms: 6, turns: 1.5, duty: 0.45, style: 'tunnel', direction: 1, colors: [hex(a), hex(b)], bandMode: 'hard' }
    : { arms: 4, turns: 2.5, duty: 0.5, style: 'log', direction: 1, colors: [hex(a), hex(b)], bandMode: 'hard' };
  return LOOM.normalizeParams2({ schema: 2, format: 'square', speed: 2, bg: { kind: 'solid', color: hex(dark) }, layer,
    layer2: { enabled: false }, glow: 0, centerpiece: { kind: 'none' } });
}

const PROGRAMS = [
  {
    id: 'loom-a', loom: true, spinRadps: 0.9, arms: 4,
    draw(g, w, h, t, cols, st) { drawLoom(g, w, h, 'loom-a', cols, st); },
  },
  {
    id: 'field', warp: 0.035,
    draw(g, w, h, t, cols) {
      g.globalCompositeOperation = 'source-over';
      g.fillStyle = css(pick(cols, 2).map((v) => v * 0.35)); g.fillRect(0, 0, w, h);
      g.globalCompositeOperation = 'lighter';
      for (let i = 0; i < 5; i++) {
        const a = t * (0.11 + i * 0.023) + i * 1.7, r = w * (0.38 + 0.08 * Math.sin(t * 0.13 + i));
        const x = w * (0.5 + 0.32 * Math.cos(a)), y = h * (0.5 + 0.32 * Math.sin(a * 1.3 + i));
        const gr = g.createRadialGradient(x, y, 0, x, y, r), c = pick(cols, i * 2 + 1);
        gr.addColorStop(0, cssA(c, 0.75)); gr.addColorStop(1, cssA(c, 0));
        g.fillStyle = gr; g.fillRect(0, 0, w, h);
      }
      g.globalCompositeOperation = 'source-over';
    },
  },
  {
    id: 'shapes', spinRadps: 0.22, arms: 7,
    draw(g, w, h, t, cols) {
      const cx = w / 2, cy = h / 2, R = Math.min(w, h) * 0.5;
      g.fillStyle = css(pick(cols, 7).map((v) => v * 0.16)); g.fillRect(0, 0, w, h);
      g.lineJoin = 'round';
      for (let k = 7; k >= 1; k--) {
        const sides = 3 + (k % 4), rot = t * 0.25 * (k % 2 ? 1 : -1) + k * 0.4, r = R * (k / 7) * 1.25;
        g.beginPath();
        for (let i = 0; i <= sides; i++) {
          const a = rot + (i / sides) * TAU;
          i ? g.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r) : g.moveTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r);
        }
        g.fillStyle = cssA(pick(cols, k), 0.55); g.fill();
        g.lineWidth = Math.max(1, R * 0.035); g.strokeStyle = css(pick(cols, k + 4)); g.stroke();
      }
      for (let i = 0; i < 6; i++) {
        const a = t * 0.4 + (i / 6) * TAU, x = cx + Math.cos(a) * R * 0.78, y = cy + Math.sin(a) * R * 0.78;
        g.beginPath(); g.arc(x, y, R * 0.09, 0, TAU); g.fillStyle = css(pick(cols, i + 2)); g.fill();
      }
    },
  },
  {
    id: 'loom-b', loom: true, spinRadps: -0.6, arms: 6,
    draw(g, w, h, t, cols, st) { drawLoom(g, w, h, 'loom-b', cols, st); },
  },
  {
    id: 'mark',
    draw(g, w, h, t, cols) {
      const cx = w / 2, cy = h * 0.44, R = Math.min(w, h) * 0.5;
      const gr = g.createLinearGradient(0, 0, w, h);
      gr.addColorStop(0, css(at(cols, t / 6).map((v) => v * 0.3)));
      gr.addColorStop(1, css(at(cols, t / 6 + 5).map((v) => v * 0.3)));
      g.fillStyle = gr; g.fillRect(0, 0, w, h);
      // the eye: rings round a turning centre
      for (let k = 5; k >= 1; k--) {
        g.beginPath(); g.arc(cx, cy, R * 0.09 * k, 0, TAU);
        g.lineWidth = R * 0.035; g.strokeStyle = css(at(cols, k + t * 0.5)); g.stroke();
      }
      g.beginPath(); g.arc(cx, cy, R * 0.07, 0, TAU); g.fillStyle = '#ffffff'; g.fill();
      // the road: two rails converging into the eye, with lanes sliding toward it
      g.lineWidth = R * 0.05; g.strokeStyle = css(pick(cols, 3));
      g.beginPath(); g.moveTo(w * 0.08, h); g.lineTo(cx - R * 0.06, cy + R * 0.1); g.stroke();
      g.strokeStyle = css(pick(cols, 9));
      g.beginPath(); g.moveTo(w * 0.92, h); g.lineTo(cx + R * 0.06, cy + R * 0.1); g.stroke();
      for (let i = 0; i < 5; i++) {
        const u = ((i / 5) + t * 0.08) % 1, y0 = cy + R * 0.12 + (h - cy) * u * u, half = (w * 0.42) * u * u;
        g.fillStyle = cssA(pick(cols, i), 0.85);
        g.fillRect(cx - half * 0.06, y0, half * 0.12, Math.max(1, R * 0.05 * u));
      }
      g.fillStyle = '#ffffff'; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.font = '700 ' + Math.round(R * 0.13) + 'px system-ui, sans-serif';
      g.fillText('RACING THOUGHTS', cx, h * 0.86);
    },
  },
  {
    id: 'kaleido', spinRadps: -0.12, arms: 8,
    draw(g, w, h, t, cols) {
      const cx = w / 2, cy = h / 2, R = Math.hypot(w, h) * 0.55, n = 8;
      g.fillStyle = css(pick(cols, 4).map((v) => v * 0.2)); g.fillRect(0, 0, w, h);
      for (let i = 0; i < n; i++) {
        const a0 = (i / n) * TAU + t * 0.08, a1 = a0 + TAU / n;
        for (let k = 0; k < 4; k++) {
          const r0 = R * (k / 4), r1 = R * ((k + 1) / 4), skew = Math.sin(t * 0.2 + k) * 0.25 * (i % 2 ? 1 : -1);
          g.beginPath();
          g.moveTo(cx + Math.cos(a0 + skew) * r0, cy + Math.sin(a0 + skew) * r0);
          g.lineTo(cx + Math.cos(a0 + skew) * r1, cy + Math.sin(a0 + skew) * r1);
          g.lineTo(cx + Math.cos(a1 - skew) * r1, cy + Math.sin(a1 - skew) * r1);
          g.lineTo(cx + Math.cos(a1 - skew) * r0, cy + Math.sin(a1 - skew) * r0);
          g.closePath();
          g.fillStyle = cssA(at(cols, i + k * 3 + t * 0.1), (i + k) % 2 ? 0.9 : 0.55);
          g.fill();
        }
      }
    },
  },
];
export { PROGRAMS };

// One WebGL Loom field renderer, shared by both Loom programs and released once the slots are built
// (the frames are static); without WebGL the Loom's own 2D fallback draws them.
let field = null, fieldTried = false;
function loomField(w, h) {
  if (!fieldTried) {
    fieldTried = true;
    try {
      const cv = typeof document !== 'undefined' ? document.createElement('canvas') : typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(w, h) : null;
      field = cv && LOOM.createFieldRenderer ? LOOM.createFieldRenderer(cv) : null;
    } catch { field = null; }
  }
  if (field && (field.canvas.width !== w || field.canvas.height !== h)) { field.canvas.width = w; field.canvas.height = h; }
  return field;
}
/** Drop the shared Loom WebGL context (render/media.js calls this after the first draws). */
export function releaseLoom() {
  if (field && field.gl) { try { const x = field.gl.getExtension('WEBGL_lose_context'); if (x) x.loseContext(); } catch { /* gone */ } }
  field = null; fieldTried = false;
}

function drawLoom(g, w, h, kind, cols, st) {
  const q = loomParams(kind, cols, st.step || 0);
  if (!q) { PROGRAMS[1].draw(g, w, h, st.t || 0, cols); return; }
  const f = loomField(w, h);
  if (f) LOOM.composeFrame(g, f, q, 0, w, h);
  else LOOM.drawFallbackFrame(g, q, 0, w, h);
}

/** The program a slot index uses on a tier that keeps `count` programs. */
export function programFor(i, count = PROGRAMS.length) {
  return ((i % count) + count) % count;
}

/** The shader turn for a program (rad/s), capped by the band rule; 0 for a Loom program without the Loom. */
export function spinOf(p) {
  const prog = PROGRAMS[p];
  if (!prog || !prog.spinRadps || (prog.loom && !LOOM)) return 0;
  return safeSpin(prog.arms || 1, prog.spinRadps);
}

/** The shader's slow lava warp for a program (share of the picture), 0 for most. */
export function warpOf(p) {
  const prog = PROGRAMS[p];
  return (prog && prog.warp) || 0;
}

/**
 * A placeholder canvas. cols: [[r, g, b], ...] 0..1 (the spectrum). make(w, h) makes the canvas (a
 * stub in tests). Returns { canvas, program, static, draw(t) -> bool } where draw paints the frame for
 * time t (seconds) and says whether anything changed. Static (the default, and always for the Loom):
 * drawn once, the slot shader moves it (turn, warp, breathing zoom, hue drift). animate: true redraws
 * the procedural programs at time t (render/media.js only does so when the tier's placeholderFps > 0:
 * a canvas upload costs about 1 ms of frame time on WebGPU, measured, so it is off by default).
 */
export function createPlaceholder(index, px, cols, { make = null, loomSwapS = Infinity, animate = false } = {}) {
  const prog = PROGRAMS[index] || PROGRAMS[0];
  const canvas = make ? make(px, px) : typeof OffscreenCanvas !== 'undefined' && !globalThis.document
    ? new OffscreenCanvas(px, px)
    : Object.assign(document.createElement('canvas'), { width: px, height: px });
  // animated programs stay on a CPU canvas: copying a GPU 2D canvas into the WebGPU texture each redraw
  // cost about 1 ms of frame time per upload (measured); a CPU canvas uploads as plain pixels
  const g = canvas.getContext('2d', prog.loom ? undefined : { willReadFrequently: true });
  const st = { step: -1, t: 0 }, loom = !!prog.loom && !!LOOM, still = loom || !animate;
  let drawn = false;
  return {
    canvas,
    program: prog.id,
    static: still,
    draw(t) {
      st.t = t;
      if (!loom && still) { if (drawn) return false; drawn = true; }
      if (loom) {
        const step = Math.floor(t / loomSwapS) + index;
        if (step === st.step) return false;
        st.step = step;
      }
      g.save();
      prog.draw(g, px, px, t, cols, st);
      g.restore();
      return true;
    },
  };
}
