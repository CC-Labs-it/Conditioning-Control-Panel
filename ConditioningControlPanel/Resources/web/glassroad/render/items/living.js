/* render/items/living.js - the living effect bubbles (lifted from dtrh/game/bubbleLife.js): each
 * art/fx icon is drawn into one cell of a shared canvas atlas and its middle is redrawn moved (the
 * spiral turns, the drop bobs, the static slides, copies leave the middle). Only cells drawn this
 * frame are redrawn, at the tier's icon fps, so all bubbles share one texture and one draw call.
 * Slow, nothing strobes; Gentle = half the speed and smaller moves.
 * Polish 6: fps may be a function (read each tick: webgl2 caps it, render/items/fxmath.js iconFps); a cell
 * animates only while a live (on screen, big enough) bubble wears it, a far one gets its first still drawing
 * once, and nothing is drawn or uploaded while no cell needs it.
 */
import { CanvasTexture, SRGBColorSpace } from 'three/webgpu';
import { ITEMS } from '../../sim/itemTable.js';

const S = 256, C = 128, TAU = Math.PI * 2, GRID = 5;   // 5 x 5: 17 items + the box fit
/** Atlas cells: the items (the toys kit after the first twelve), then the plain bubble used by boxes and pickups. */
export const CELLS = ['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter', 'surge', 'glass', 'echo', 'golden',
  'pendulum', 'disc', 'tether', 'snap', 'bigSpiral', 'box'];
export const cellOf = (id) => { const i = CELLS.indexOf(id); return i < 0 ? CELLS.length - 1 : i; };
export const ATLAS_GRID = GRID;

// How each icon lives: spin rad/s, beat (scale pulse), tilt (slosh), bob px, slide (copies run down), rings
const LIFE = {
  flash: { rays: 1 }, spiral: { spin: 0.55, pulse: 0.035 }, brainDrain: { slide: 1 }, freeze: { spin: 0.14, pulse: 0.025, flakes: 1 },
  blackout: { beat: 0.08 }, pink: { tilt: 0.17 }, melt: { bob: 4, drip: 1 }, shatter: { spin: 0.28 }, surge: { bob: 3.5 },
  glass: {}, echo: { rings: 1 }, golden: { spin: 0.2, pulse: 0.03 }, box: {},
  pendulum: { tilt: 0.3 }, disc: { spin: 1.2 }, tether: { beat: 0.06 }, snap: { rings: 1 }, bigSpiral: { spin: 0.7, pulse: 0.04 },
};
const rnd = (i) => { const x = Math.sin(i * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); };
const FILM = ['rgba(255,120,200,1)', 'rgba(120,220,255,0)', 'rgba(170,255,190,0.8)', 'rgba(255,230,140,0)', 'rgba(190,140,255,0.9)', 'rgba(255,120,200,1)'];

export function createLivingIcons({ px = 256, fps = 30 } = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = px * GRID;
  const ctx = canvas.getContext('2d');
  const off = document.createElement('canvas'); off.width = off.height = S;
  const o = off.getContext('2d');
  const tex = new CanvasTexture(canvas);
  tex.colorSpace = SRGBColorSpace;
  const imgs = CELLS.map((id) => {
    const img = new Image(), file = id === 'box' ? 'art/fx/bubble.png' : ITEMS[id].icon;
    const e = { img, ready: false };
    img.onload = () => { e.ready = true; }; img.onerror = () => { e.ready = false; };
    img.src = new URL('../../' + file, import.meta.url).href;
    return e;
  });
  const want = new Set(), still = new Uint8Array(CELLS.length);
  let acc = 0;

  // the sprite's middle, redrawn moved: darken the still copy, add the moved one as light
  function inner(img, R, draw, dim = 0.9, feather = 0.72) {
    o.setTransform(1, 0, 0, 1, 0, 0); o.globalCompositeOperation = 'source-over'; o.globalAlpha = 1;
    o.clearRect(0, 0, S, S); o.save(); draw(o); o.restore();
    o.globalAlpha = 1; o.globalCompositeOperation = 'destination-in';
    let g = o.createRadialGradient(C, C, R * feather, C, C, R);
    g.addColorStop(0, 'rgba(0,0,0,1)'); g.addColorStop(1, 'rgba(0,0,0,0)');
    o.fillStyle = g; o.fillRect(0, 0, S, S); o.globalCompositeOperation = 'source-over';
    g = ctx.createRadialGradient(C, C, R * feather, C, C, R);
    g.addColorStop(0, `rgba(7,5,12,${dim})`); g.addColorStop(1, 'rgba(7,5,12,0)');
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(C, C, R, 0, TAU); ctx.fill();
    ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.drawImage(off, 0, 0); ctx.restore();
  }
  const spun = (img, a, s = 1, dx = 0, dy = 0) => (c) => { c.translate(C + dx, C + dy); c.rotate(a); c.scale(s, s); c.drawImage(img, -C, -C, S, S); };
  function ring(r, a, col, w = 1.5) {
    if (a <= 0.01) return;
    ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = a; ctx.strokeStyle = col; ctx.lineWidth = w;
    ctx.beginPath(); ctx.arc(C, C, r, 0, TAU); ctx.stroke(); ctx.restore();
  }
  function film(t, a = 0.16, r = 88) {
    if (!ctx.createConicGradient) return;
    const g = ctx.createConicGradient(t * 0.35, C, C);
    FILM.forEach((c, i) => g.addColorStop(i / (FILM.length - 1), c));
    ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = a; ctx.strokeStyle = g; ctx.lineWidth = 7;
    ctx.beginPath(); ctx.arc(C, C, r, 0, TAU); ctx.stroke(); ctx.restore();
  }
  function dots(t, n, col, seed, fall) {
    ctx.save(); ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < n; i++) {
      const q = (t * (0.07 + rnd(i + seed) * 0.06) + rnd(i * 2.2 + seed)) % 1;
      ctx.fillStyle = `rgba(${col},${Math.sin(q * Math.PI) * 0.7})`;
      ctx.beginPath(); ctx.arc(52 + rnd(i * 5.1 + seed) * 152, fall ? 40 + q * 176 : C + Math.sin(i + t) * 50, 1 + rnd(i * 9) * 1.4, 0, TAU); ctx.fill();
    }
    ctx.restore();
  }

  function draw(id, img, t, m) {
    const L = LIFE[id] || {};
    ctx.drawImage(img, 0, 0, S, S);
    if (L.rays) {
      ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.translate(C, C); ctx.rotate(-t * 0.22);
      for (let k = 0; k < 4; k++) {
        const len = 74 + 14 * Math.sin(t * 1.1 + k * 1.7), g = ctx.createLinearGradient(0, 0, len, 0);
        g.addColorStop(0, 'rgba(255,225,240,0.55)'); g.addColorStop(1, 'rgba(255,200,230,0)');
        ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(0, -1.6); ctx.lineTo(len, 0); ctx.lineTo(0, 1.6); ctx.fill(); ctx.rotate(TAU / 4);
      }
      ctx.restore();
    }
    if (L.spin) inner(img, 70, spun(img, t * L.spin, 1 + L.pulse * Math.sin(t * 0.9)));
    if (L.beat) { const ph = (t / 1.9) % 1; inner(img, 66, spun(img, 0, 1 + L.beat * m * (Math.exp(-ph * 9) + 0.6 * Math.exp(-(((ph - 0.22) * 9) ** 2))))); }
    if (L.tilt) inner(img, 60, spun(img, (Math.sin(t * 1.15) * L.tilt + Math.sin(t * 2.7) * 0.04) * m));
    if (L.bob) inner(img, 40, spun(img, 0, 1 + (L.drip ? 0.04 * Math.sin(t * 0.8) : 0), 0, Math.sin(t * 1.3) * L.bob * m), 0.9, 0.6);
    if (L.slide) {
      inner(img, 88, (c) => {
        c.globalCompositeOperation = 'lighter';
        for (let k = 0; k < 4; k++) {
          const q = (t * 0.22 * (0.5 + m * 0.5) + k / 4) % 1;
          c.globalAlpha = Math.sin(q * Math.PI) * 0.5; c.drawImage(img, Math.sin(t * 0.8 + k) * 3, -14 + q * 46, S, S * (1 + q * 0.08));
        }
      }, 0.92, 0.8);
    }
    if (L.flakes) dots(t, 12, '200,240,255', 3, true);
    if (L.drip) dots(t * 0.6, 6, '255,140,210', 17, true);
    if (L.rings) for (let k = 0; k < 3; k++) { const q = (t / 2.8 + k / 3) % 1; ring(34 + q * 50 * (0.5 + m * 0.5), (1 - q) * 0.45, 'rgba(205,195,240,1)'); }
    film(t, id === 'glass' || id === 'box' ? 0.36 : 0.16);
  }

  return {
    texture: tex,
    /** Mark a cell as worn this frame; live = false (far or behind) only asks for its first still drawing. */
    use(id, live = true) { const i = cellOf(id); if (live || !still[i]) want.add(i); },
    /** Redraw the marked cells at the tier fps. timeS already runs at the Gentle rate (the caller
     *  slows its clock), so gentle only shrinks the moves here. */
    tick(dt, timeS, gentle) {
      acc += dt;
      const rate = typeof fps === 'function' ? fps() : fps;
      if (acc < 1 / rate || !want.size) { want.clear(); return; }
      acc = 0;
      const m = gentle ? 0.4 : 1, t = timeS, k = px / S;
      let drew = false;
      for (const i of want) {
        const e = imgs[i];
        if (!e.ready) continue;
        ctx.setTransform(k, 0, 0, k, (i % GRID) * px, Math.floor(i / GRID) * px);
        ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-over';
        ctx.clearRect(0, 0, S, S);
        ctx.save(); ctx.beginPath(); ctx.rect(0, 0, S, S); ctx.clip();
        try { draw(CELLS[i], e.img, t + i * 3.7, m); } catch (_) { /* the still sprite stays */ }
        ctx.restore(); drew = true; still[i] = 1;
      }
      want.clear();
      if (drew) tex.needsUpdate = true;
    },
    dispose() { tex.dispose(); },
  };
}
