/* hud/minimap.js - the small top-down map: the track loop and one dot per racer.
 *
 * fitMap / projectInto / mapPoints are pure (tested in tests/hud-minimap.test.mjs). The draw
 * side is a 2D canvas: the loop is stroked once into a cached layer whenever the size or track
 * changes, then each frame clears and draws the layer plus the dots. World x maps to the right,
 * world z downward (seen from above), one scale for both axes so the shape keeps its look.
 */

/**
 * Fit a closed loop of world (x, z) pairs into a w x h box with `pad` px on every side.
 * @param {Float32Array|number[]} xz
 * @returns {{ s: number, ox: number, oy: number, minX: number, minZ: number }} px = ox + (x - minX) * s
 */
export function fitMap(xz, w, h, pad = 6) {
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (let i = 0; i + 1 < xz.length; i += 2) {
    const x = xz[i], z = xz[i + 1];
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
  }
  if (!(maxX >= minX)) return { s: 1, ox: w / 2, oy: h / 2, minX: 0, minZ: 0 };
  const spanX = Math.max(maxX - minX, 1e-6), spanZ = Math.max(maxZ - minZ, 1e-6);
  const s = Math.max(0, Math.min((w - 2 * pad) / spanX, (h - 2 * pad) / spanZ));
  return { s, ox: (w - spanX * s) / 2, oy: (h - spanZ * s) / 2, minX, minZ };
}

/** World (x, z) -> map px written into out[0], out[1]. */
export function projectInto(fit, x, z, out) {
  out[0] = fit.ox + (x - fit.minX) * fit.s;
  out[1] = fit.oy + (z - fit.minZ) * fit.s;
  return out;
}

/** Every (x, z) pair of src projected into a new Float32Array of px pairs. */
export function mapPoints(fit, src) {
  const out = new Float32Array(src.length - (src.length % 2)), p = [0, 0];
  for (let i = 0; i < out.length; i += 2) {
    projectInto(fit, src[i], src[i + 1], p);
    out[i] = p[0]; out[i + 1] = p[1];
  }
  return out;
}

/** Thin a long loop to at most `max` points (the map is ~120 px wide). */
export function thinLoop(xz, max = 400) {
  const n = Math.floor(xz.length / 2);
  if (n <= max) return xz;
  const out = new Float32Array(max * 2);
  for (let k = 0; k < max; k++) { const i = Math.floor(k * n / max) * 2; out[k * 2] = xz[i]; out[k * 2 + 1] = xz[i + 1]; }
  return out;
}

const INK = 'rgba(253, 247, 255, 0.78)', SHADOW = 'rgba(14, 10, 22, 0.55)', BOT = 'rgba(253, 247, 255, 0.9)';
const PINK = '#ff5fa8', GOLD = '#ffd36b';

/** @param {HTMLCanvasElement} canvas */
export function createMinimap(canvas, { doc = globalThis.document } = {}) {
  const ctx = canvas.getContext && canvas.getContext('2d');
  let loop = null, fit = null, layer = null, w = 0, h = 0, dpr = 1;
  const p = [0, 0];

  function size() {
    const cw = canvas.clientWidth || 120, ch = canvas.clientHeight || 96;
    const d = Math.min(2, globalThis.devicePixelRatio || 1);
    if (cw === w && ch === h && d === dpr && layer) return;
    w = cw; h = ch; dpr = d;
    canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
    bake();
  }
  function bake() {
    layer = null;
    if (!loop || !ctx) return;
    fit = fitMap(loop, w, h, 7);
    const pts = mapPoints(fit, loop);
    layer = doc.createElement('canvas');
    layer.width = canvas.width; layer.height = canvas.height;
    const g = layer.getContext('2d');
    g.scale(dpr, dpr); g.lineJoin = 'round'; g.lineCap = 'round';
    const path = () => { g.beginPath(); g.moveTo(pts[0], pts[1]); for (let i = 2; i < pts.length; i += 2) g.lineTo(pts[i], pts[i + 1]); g.closePath(); };
    path(); g.strokeStyle = SHADOW; g.lineWidth = 6; g.stroke();
    path(); g.strokeStyle = INK; g.lineWidth = 2.5; g.stroke();
  }
  function dot(x, y, r, fill) {
    ctx.beginPath(); ctx.arc(x, y, r + 1.5, 0, Math.PI * 2); ctx.fillStyle = SHADOW; ctx.fill();
    ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fillStyle = fill; ctx.fill();
  }
  return {
    setTrack({ xz } = {}) { loop = xz && xz.length >= 6 ? thinLoop(xz) : null; w = 0; size(); },
    /** @param {Float32Array} map x, z per racer (racer 0 = you) @param {number} leader racer index in 1st */
    draw(map, leader = -1) {
      if (!ctx) return;
      size();
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      if (!layer || !fit) return;
      ctx.drawImage(layer, 0, 0);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      if (!map) return;
      for (let i = map.length - 2; i >= 0; i -= 2) {   // you last, on top
        const k = i / 2;
        projectInto(fit, map[i], map[i + 1], p);
        if (k === 0) dot(p[0], p[1], 4.5, PINK);
        else dot(p[0], p[1], 3, k === leader ? GOLD : BOT);
      }
    },
  };
}
