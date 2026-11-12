/* ui/guide-sea.js - the sea under the Guide map (owner 2026-11-08: "have fun with the background, it's super plain").
 * The islands float on a paper sea that gets deeper the further down the run goes: sunlit shallows at the gate, lilac in the
 * middle, ink-dark water at the Drop, where the trunk road ends in a slow maelstrom that swallows the run's own words. Crayon
 * wave marks, shore foam, boats and gulls in the shallows, whirlpools in the middle, bubbles in the deep, a compass whose four
 * points all say DOWN. Everything is drawn on one viewport-sized canvas behind the map (never a world-sized one), only what is
 * in view, and it line-boils in stop motion at 9 fps like the Golden Button star. Gentle and reduced motion hold frame 0 and
 * draw only when the view moves. The Drop's maelstrom uses the theme's spiral colours once a theme is picked. */

const TAU = Math.PI * 2;
const BOIL_MS = 111;   // 9 fps, the same step as the crayon star: line wobble only
const FRAME_MS = 33;   // motion (swell, boats, gulls, drift) is drawn at 30 fps on the real clock
const hash = (a, b = 0, c = 0) => {
  let x = Math.imul(a | 0, 374761393) ^ Math.imul(b | 0, 668265263) ^ Math.imul(c | 0, 2246822519);
  x = Math.imul(x ^ (x >>> 13), 1274126177);
  x ^= x >>> 16;
  return (x >>> 0) / 4294967296;
};
const hexRgb = (h) => { const n = parseInt(h.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; };
const mix = (a, b, k) => { const p = hexRgb(a), q = hexRgb(b); return `rgb(${p.map((v, i) => Math.round(v + (q[i] - v) * k)).join(',')})`; };
const rgba = (h, a) => { const [r, g, b] = hexRgb(h); return `rgba(${r},${g},${b},${a})`; };

/* Depth stops from the gate (0) to the Drop (1). Past DARK, marks switch from ink to foam. */
const DEPTH = [[0, '#fff4e6'], [0.1, '#fde3ea'], [0.3, '#efd8f2'], [0.5, '#cdb8ee'], [0.68, '#8e77d2'], [0.84, '#4b3590'], [1, '#1c1036']];
const DARK = 0.6;
const INK = '#2a2031', FOAM = '#fff8f0';
/** Names for the water at each depth, shown in the corner as the player pans down. */
export const DEPTH_NAMES = Object.freeze(['Shallows', 'Ankle deep', 'Waist deep', 'Over your head', 'Deeper', 'Much deeper', 'Gone']);
export const depthName = (d) => DEPTH_NAMES[Math.min(DEPTH_NAMES.length - 1, Math.max(0, Math.floor(d * DEPTH_NAMES.length)))];
/** The depth at a world height: one name per section of the map (the gate is the Shallows, the long road down is Gone). */
export function depthLabel(world, y) {
  const secs = world.sections || [];
  let k = 0;
  for (let i = 0; i < secs.length; i++) if (secs[i].y0 <= y) k = i;
  return DEPTH_NAMES[Math.min(DEPTH_NAMES.length - 1, k)];
}

/** Spots of open water, away from every island and the road: where boats, whirlpools and words can go. */
export function openWater(world, { step = 90, margin = 40 } = {}) {
  const out = [];
  for (let y = 120; y < world.H - 60; y += step) {
    for (let x = 60; x < world.W - 60; x += step) {
      const jx = x + (hash(x, y, 1) - 0.5) * step * 0.6, jy = y + (hash(x, y, 2) - 0.5) * step * 0.6;
      if (Math.abs(jx - world.CX) < 180) continue;
      let room = Infinity;
      for (const i of world.islands) {
        const dx = Math.max(i.x - jx, 0, jx - (i.x + i.w)), dy = Math.max(i.y - 20 - jy, 0, jy - (i.y + i.h));
        room = Math.min(room, Math.hypot(dx, dy));
      }
      room = Math.min(room, Math.abs(jx - world.CX) - 150);
      if (room > margin) out.push({ x: jx, y: jy, room, d: jy / world.H, r: hash(jx | 0, jy | 0, 3) });
    }
  }
  return out;
}

/** Pick decor from open water: each kind has a depth band, a size, a spacing and a cap. */
export function placeDecor(world) {
  const spots = openWater(world).sort((a, b) => a.r - b.r);
  const taken = [];
  const kinds = [
    { kind: 'compass', from: 0, to: 0.06, room: 110, gap: 400, max: 1 },
    { kind: 'boat', from: 0.02, to: 0.22, room: 60, gap: 520, max: 4 },
    { kind: 'gulls', from: 0, to: 0.3, room: 70, gap: 600, max: 5 },
    { kind: 'serpent', from: 0.42, to: 0.66, room: 150, gap: 900, max: 2 },
    { kind: 'jelly', from: 0.56, to: 0.97, room: 80, gap: 420, max: 9 },
    { kind: 'fish', from: 0.2, to: 0.74, room: 110, gap: 520, max: 8 },
    { kind: 'whirl', from: 0.18, to: 0.84, room: 80, gap: 560, max: 9 },
    { kind: 'kelp', from: 0.48, to: 0.97, room: 60, gap: 380, max: 12 },
    { kind: 'bottle', from: 0.25, to: 0.6, room: 50, gap: 800, max: 2 },
    { kind: 'bubbles', from: 0.5, to: 1, room: 50, gap: 300, max: 14 },
    { kind: 'word', from: 0, to: 1, room: 70, gap: 340, max: 40 },
  ];
  const out = [];
  for (const k of kinds) {
    let n = 0;
    for (const s of spots) {
      if (n >= k.max) break;
      if (s.d < k.from || s.d > k.to || s.room < k.room) continue;
      if (taken.some((t) => Math.hypot(t.x - s.x, t.y - s.y) < Math.max(k.gap * (t.kind === k.kind ? 1 : 0.35), t.room0 + k.room))) continue;
      const it = { kind: k.kind, x: s.x, y: s.y, d: s.d, seed: Math.floor(s.r * 1e6), size: Math.min(s.room, k.room * 1.6), room0: k.room };
      taken.push(it); out.push(it); n++;
    }
  }
  return out;
}

/**
 * createSea(doc, host, world, view, opts) -> { paint, start, stop, dirty, destroy }
 * view() answers { left, top, scale, w, h } in scroll pixels; opts.still() freezes motion; opts.theme() answers the picked
 * theme palette (or null); opts.words() answers the words the water carries; opts.font is the page font family.
 */
export function createSea(doc, host, world, view, opts = {}) {
  const win = doc.defaultView;
  const canvas = doc.createElement('canvas');
  canvas.className = 'gr-sea';
  canvas.setAttribute('aria-hidden', 'true');
  host.prepend(canvas);
  const g = canvas.getContext('2d');
  const decor = placeDecor(world);
  const maelstrom = world.maelstrom || { x: world.CX, y: world.H - 260, r: 200 };
  let gradKey = '', grad = null, raf = 0, dirty = true, lastDraw = 0, running = false;

  const still = () => Boolean(opts.still?.());
  const tint = () => opts.theme?.()?.a || '#ff6fb5';
  function gradient() {
    const key = tint();
    if (key === gradKey && grad) return grad;
    gradKey = key;
    grad = g.createLinearGradient(0, 0, 0, world.H);
    for (const [at, c] of DEPTH) grad.addColorStop(at, at < 0.15 ? c : mix(c, key, 0.12));
    return grad;
  }
  const markColour = (y, a) => (y / world.H > DARK ? rgba(FOAM, a * 1.1) : rgba(INK, a));

  /* A crayon stroke: the path twice, the second pass thinner and nudged, so the edge reads waxy, not vector. */
  function crayon(path, colour, width) {
    g.strokeStyle = colour; g.lineWidth = width; g.lineCap = 'round'; g.lineJoin = 'round';
    g.beginPath(); path(0); g.stroke();
    g.globalAlpha *= 0.55; g.lineWidth = width * 0.55;
    g.beginPath(); path(0.9); g.stroke();
    g.globalAlpha /= 0.55;
  }
  const jit = (seed, i, f, amt) => (hash(seed, i, f + 7) - 0.5) * 2 * amt;

  function grain(v, f) {
    if (v.scale < 0.5) return;
    const cell = 46;
    g.lineWidth = 1.3; g.lineCap = 'round';
    for (let cy = Math.floor(v.y0 / cell); cy <= v.y1 / cell; cy++) {
      for (let cx = Math.floor(v.x0 / cell); cx <= v.x1 / cell; cx++) {
        const r = hash(cx, cy, 11);
        if (r > 0.7) continue;
        const x = cx * cell + hash(cx, cy, 12) * cell, y = cy * cell + hash(cx, cy, 13) * cell, l = 7 + r * 10;
        g.strokeStyle = markColour(y, 0.05 + r * 0.05);
        g.beginPath(); g.moveTo(x, y); g.lineTo(x + l * 0.8, y - l * 0.55); g.stroke();
      }
    }
  }

  /* Watercolour washes: big soft blotches, lighter and darker, so no stretch of water reads as one flat colour. */
  function washes(v, t) {
    const cell = 520;
    for (let cy = Math.floor(v.y0 / cell) - 1; cy <= v.y1 / cell + 1; cy++) {
      for (let cx = Math.floor(v.x0 / cell) - 1; cx <= v.x1 / cell + 1; cx++) {
        const r0 = hash(cx, cy, 21);
        const x = cx * cell + hash(cx, cy, 22) * cell + Math.sin(t * 0.09 + r0 * 11) * 46, y = cy * cell + hash(cx, cy, 23) * cell + Math.cos(t * 0.07 + r0 * 7) * 28, r = (220 + r0 * 260) * (1 + Math.sin(t * 0.2 + r0 * 5) * 0.08);
        const dark = y / world.H > DARK;
        const c = r0 > 0.55 ? (dark ? rgba(tint(), 0.16) : 'rgba(255,255,255,.34)') : (dark ? 'rgba(6,2,20,.22)' : rgba(INK, 0.05));
        const wash = g.createRadialGradient(x, y, 0, x, y, r);
        wash.addColorStop(0, c); wash.addColorStop(1, 'rgba(0,0,0,0)');
        g.fillStyle = wash;
        g.beginPath(); g.ellipse(x, y, r, r * 0.7, hash(cx, cy, 24) * Math.PI, 0, TAU); g.fill();
      }
    }
  }
  /* Sunlight in the shallows: slanted beams and a boiling net of caustic light that fades out by Waist deep. */
  function shallows(v, f, t) {
    const end = world.H * 0.3;
    if (v.y0 > end) return;
    for (let k = 0; k < 9; k++) {
      const bx = (k / 9) * world.W + hash(k, 1, 31) * 200 + Math.sin(t * 0.22 + k * 1.7) * 60;
      if (bx < v.x0 - 600 || bx > v.x1 + 300) continue;
      const len = 900 + hash(k, 2, 31) * 900, wdt = 60 + hash(k, 3, 31) * 90;
      const beam = g.createLinearGradient(bx, 0, bx - len * 0.35, len);
      beam.addColorStop(0, `rgba(255,255,255,${(0.28 + 0.2 * (0.5 + 0.5 * Math.sin(t * 0.6 + k * 2.3))).toFixed(3)})`); beam.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = beam;
      g.beginPath(); g.moveTo(bx, 0); g.lineTo(bx + wdt, 0); g.lineTo(bx + wdt - len * 0.35, len); g.lineTo(bx - len * 0.35 - wdt * 0.6, len); g.closePath(); g.fill();
    }
    if (v.scale < 0.45) return;
    const cell = 64;
    g.lineWidth = 2.2; g.lineJoin = 'round';
    for (let cy = Math.floor(v.y0 / cell); cy <= Math.min(v.y1, end) / cell; cy++) {
      const a = 0.42 * (1 - (cy * cell) / end);
      if (a <= 0.02) continue;
      g.strokeStyle = `rgba(255,255,255,${a.toFixed(3)})`;
      for (let cx = Math.floor(v.x0 / cell); cx <= v.x1 / cell; cx++) {
        const ox = cx * cell + (cy % 2) * cell * 0.5 + Math.sin(t * 0.55 + cy * 0.7) * 12, oy = cy * cell + Math.cos(t * 0.45 + cx * 0.6) * 7;
        g.beginPath();
        for (let k = 0; k <= 6; k++) {
          const ang = (k / 6) * TAU, rr = cell * (0.36 + hash(cx, cy, k % 6) * 0.14) + jit(cx * 13 + cy, k % 6, f, 2.4) + Math.sin(t * 1.6 + cx * 0.9 + cy * 1.3 + k) * 4;
          const px = ox + Math.cos(ang) * rr, py = oy + Math.sin(ang) * rr * 0.8;
          if (k) g.lineTo(px, py); else g.moveTo(px, py);
        }
        g.stroke();
      }
    }
  }
  /* Chart contours: a dotted depth line where each section of the map begins, named at both margins. */
  function contours(v, f, t) {
    const secs = world.sections || [];
    for (let i = 1; i < secs.length; i++) {
      const y = secs[i].y0 - 30;
      if (y < v.y0 - 40 || y > v.y1 + 40) continue;
      const deep = y / world.H > DARK;
      g.save();
      g.setLineDash([2, 11]);
      crayon((o) => {
        g.moveTo(v.x0 - 10, y);
        for (let x = Math.floor(v.x0 / 80) * 80; x <= v.x1 + 80; x += 80) g.quadraticCurveTo(x + 40, y + Math.sin(x * 0.013 + i + t * 0.7) * 16 + o, x + 80, y + Math.sin((x + 80) * 0.004 + t * 0.5 + i) * 5 + jit(i, x, f, 1.2));
      }, deep ? rgba(FOAM, 0.55) : rgba(INK, 0.4), 3);
      g.restore();
      const name = DEPTH_NAMES[Math.min(DEPTH_NAMES.length - 1, i)].toUpperCase();
      g.font = `700 15px ${opts.font}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      for (const lx of [60 + 70, world.W - 130]) {
        if (lx < v.x0 - 120 || lx > v.x1 + 120) continue;
        const tw = g.measureText(name).width + 22;
        g.save(); g.translate(lx, y); g.rotate(-0.03);
        g.fillStyle = deep ? '#2b1d55' : '#fffaf2';
        g.beginPath(); g.roundRect?.(-tw / 2, -13, tw, 26, 6); g.fill();
        crayon(() => g.roundRect?.(-tw / 2, -13, tw, 26, 6), deep ? FOAM : INK, 2);
        g.fillStyle = deep ? FOAM : INK; g.fillText(name, 0, 1);
        g.restore();
      }
    }
  }

  function waves(v, f, t) {
    const cw = 150, ch = 112;
    for (let cy = Math.floor(v.y0 / ch) - 1; cy <= v.y1 / ch + 1; cy++) {
      for (let cx = Math.floor(v.x0 / cw) - 1; cx <= v.x1 / cw + 1; cx++) {
        const seed = cx * 7919 + cy * 104729;
        if (hash(seed, 1) > 0.78) continue;
        const bx = cx * cw + hash(seed, 2) * cw, by = cy * ch + hash(seed, 3) * ch;
        if (Math.abs(bx - world.CX) < 60) continue;
        if (world.islands.some((i) => bx > i.x - 30 && bx < i.x + i.w + 30 && by > i.y - 34 && by < i.y + i.h + 26)) continue;
        const humps = 2 + (hash(seed, 4) > 0.6 ? 1 : 0), hw = 13 + hash(seed, 5) * 8;
        const ph = hash(seed, 6) * TAU, sp = 0.7 + hash(seed, 7) * 0.6;
        const drift = Math.sin(t * 0.55 * sp + ph) * 18 + t * 6 * (hash(seed, 8) - 0.5);
        const bob = Math.sin(t * 1.3 * sp + ph) * 5;
        g.globalAlpha = 0.45 + 0.55 * (0.5 + 0.5 * Math.sin(t * 0.45 * sp + ph * 2));
        crayon((o) => {
          const x0 = bx + drift - (humps * hw) / 2;
          g.moveTo(x0 + jit(seed, 0, f, 1.2) + o, by + bob + jit(seed, 1, f, 1.2));
          for (let k = 0; k < humps; k++) {
            const xa = x0 + k * hw, xb = xa + hw;
            const lift = hw * (0.25 + 0.55 * (0.5 + 0.5 * Math.sin(t * 2.4 * sp - k * 1.4 + ph)));
            g.quadraticCurveTo(xa + hw / 2 + jit(seed, k + 2, f, 1.5), by + bob - lift + jit(seed, k + 9, f, 1.4) + o, xb + jit(seed, k + 5, f, 1), by + bob + jit(seed, k + 12, f, 1));
          }
        }, markColour(by, 0.26), 2.6);
        g.globalAlpha = 1;
      }
    }
  }

  /* Shore foam: two broken crayon rings around each island, the inner one denser. */
  function shores(v, f, t, shutOf) {
    for (const [n, i] of world.islands.entries()) {
      if (i.x + i.w + 40 < v.x0 || i.x - 40 > v.x1 || i.y + i.h + 40 < v.y0 || i.y - 40 > v.y1) continue;
      const deep = (i.y + i.h / 2) / world.H > DARK;
      const faint = shutOf(i) ? 0.45 : 1;
      for (const [ring, off, a, w] of [[0, 10, 0.85, 3.2], [1, 21, 0.45, 2.2]]) {
        const lap = off + Math.sin(t * 1.3 + n * 0.9 + ring * 1.6) * (ring ? 5 : 3);
        const pts = rimPoints(i.x - lap, i.y - lap, i.w + lap * 2, i.h + lap * 2, 28 + lap, 15);
        g.strokeStyle = deep ? rgba(FOAM, a * 0.8 * faint) : ring ? rgba(INK, 0.24 * faint) : rgba('#ffffff', a * faint);
        g.lineWidth = w; g.lineCap = 'round';
        g.beginPath();
        const dash = ring ? 3 : 6, gap = ring ? 2 : 1;
        for (let k = 0; k < pts.length; k++) {
          const p = pts[k], q = (k + n * 3) % (dash + gap);
          const px = p[0] + jit(n * 31 + ring, k, f, 1.6), py = p[1] + jit(n * 37 + ring, k, f, 1.6);
          if (q === 0) g.moveTo(px, py); else if (q < dash) g.lineTo(px, py);
        }
        g.stroke();
      }
    }
  }
  function rimPoints(x, y, w, h, r, step) {
    r = Math.min(r, w / 2, h / 2);
    const pts = [];
    const seg = (ax, ay, bx, by) => { const l = Math.hypot(bx - ax, by - ay), n = Math.max(1, Math.round(l / step)); for (let k = 0; k < n; k++) pts.push([ax + (bx - ax) * k / n, ay + (by - ay) * k / n]); };
    const arc = (cx, cy, a0) => { const n = Math.max(2, Math.round((Math.PI / 2) * r / step)); for (let k = 0; k < n; k++) { const a = a0 + (Math.PI / 2) * k / n; pts.push([cx + Math.cos(a) * r, cy + Math.sin(a) * r]); } };
    seg(x + r, y, x + w - r, y); arc(x + w - r, y + r, -Math.PI / 2);
    seg(x + w, y + r, x + w, y + h - r); arc(x + w - r, y + h - r, 0);
    seg(x + w - r, y + h, x + r, y + h); arc(x + r, y + h - r, Math.PI / 2);
    seg(x, y + h - r, x, y + r); arc(x + r, y + r, Math.PI);
    return pts;
  }

  function spiral(x, y, r, turn, arms, colours, width, alpha) {
    for (let a = 0; a < arms; a++) {
      g.globalAlpha = alpha;
      const c = colours[a % colours.length];
      crayon((o) => {
        const steps = 70;
        for (let k = 0; k <= steps; k++) {
          const s = k / steps, ang = turn + (a / arms) * TAU + s * TAU * 2.6, rr = r * s;
          const px = x + Math.cos(ang) * rr + o * 0.6, py = y + Math.sin(ang) * rr * 0.78;
          if (k) g.lineTo(px, py); else g.moveTo(px, py);
        }
      }, c, width * (0.6 + 0.4 * (1 - a / arms)));
    }
    g.globalAlpha = 1;
  }

  function drawDecor(it, f, t, words, theme) {
    const { x, y, seed } = it;
    const dark = it.d > DARK;
    const ink = dark ? FOAM : INK;
    switch (it.kind) {
      case 'whirl': {
        const r = Math.min(70, 34 + it.size * 0.3);
        const cols = dark && theme ? theme.spiral.map((c) => rgba(c, 0.85)) : [rgba(ink, 0.32)];
        spiral(x, y, r, (t * 0.9 + seed) % TAU, dark && theme ? 2 : 1, cols, 2.6, 1);
        g.globalAlpha = 0.5;
        crayon(() => g.ellipse(x, y, r + 8 + jit(seed, 1, f, 1.5), (r + 8) * 0.78, 0, 0, TAU), rgba(dark ? FOAM : '#ffffff', 0.9), 2.4);
        g.globalAlpha = 1;
        break;
      }
      case 'boat': {
        const reach = Math.min(150, 40 + it.size), ph = (seed % 997) / 997 * TAU, sp = 0.13 + (seed % 7) * 0.012;
        const bx = x + Math.sin(t * sp + ph) * reach, by = y + Math.sin(t * 1.5 + ph) * 4;
        const dir = Math.cos(t * sp + ph) >= 0 ? 1 : -1, speed = Math.abs(Math.cos(t * sp + ph));
        const rock = Math.sin(t * 1.9 + ph) * 0.16;
        g.globalAlpha = 0.6 * speed;
        for (let k = 1; k <= 4; k++) crayon(() => { const wx = bx - dir * (24 + k * 16); g.moveTo(wx, by + 13 - k * 2); g.quadraticCurveTo(wx - dir * 8, by + 13, wx, by + 13 + k * 2); }, '#ffffff', 2.2);
        g.globalAlpha = 1;
        g.fillStyle = rgba(INK, 0.14);
        g.beginPath(); g.ellipse(bx + 7, y + 17, 30, 7, 0, 0, TAU); g.fill();   // the shadow stays flat: one lamp
        g.save(); g.translate(bx, by); g.rotate(rock); g.scale(dir, 1);
        g.fillStyle = '#fffdf8';
        g.beginPath(); g.moveTo(-30, -2); g.lineTo(30, -2); g.lineTo(20, 12); g.lineTo(-20, 12); g.closePath(); g.fill();
        g.beginPath(); g.moveTo(-2, -2); g.lineTo(-2, -40); g.lineTo(20, -6); g.closePath(); g.fill();
        g.globalAlpha = 1;
        crayon((o) => { g.moveTo(-30 + o, -2); g.lineTo(30, -2 + o); g.lineTo(20, 12); g.lineTo(-20, 12); g.closePath(); g.moveTo(-2, -2); g.lineTo(-2 + o, -40); g.lineTo(20, -6 + o); g.closePath(); }, INK, 2.6);
        g.fillStyle = tint();
        g.beginPath(); g.arc(-2, -42, 3.5, 0, TAU); g.fill();
        g.restore();
        g.globalAlpha = 0.7;
        crayon(() => { g.moveTo(bx - 40, by + 13); g.quadraticCurveTo(bx - 33, by + 8 + jit(seed, 3, f, 1.5), bx - 26, by + 13); g.moveTo(bx + 27, by + 13); g.quadraticCurveTo(bx + 34, by + 8 + jit(seed, 4, f, 1.5), bx + 41, by + 13); }, '#ffffff', 2.4);
        g.globalAlpha = 1;
        break;
      }
      case 'gulls': {
        for (let k = 0; k < 3; k++) {
          const a = t * (0.35 + k * 0.06) + seed + k * 2.1;
          const gx = x + Math.cos(a) * (70 + k * 14), gy = y + Math.sin(a) * (26 + k * 6) + (k % 2) * 12;
          const flap = Math.sin(t * (7 + k) + k) * 5;
          crayon((o) => { g.moveTo(gx - 10, gy + o * 0.4); g.quadraticCurveTo(gx - 5, gy - 6 - flap, gx, gy); g.quadraticCurveTo(gx + 5, gy - 6 - flap, gx + 10, gy); }, rgba(INK, 0.55), 2.2);
        }
        break;
      }
      case 'compass': {
        const r = 54;
        g.fillStyle = 'rgba(255,253,248,.85)';
        g.beginPath(); g.arc(x, y, r, 0, TAU); g.fill();
        crayon(() => g.arc(x, y, r + jit(seed, 1, f, 1), 0, TAU), INK, 2.6);
        g.globalAlpha = 0.6; crayon(() => g.arc(x, y, r - 9, 0, TAU), INK, 1.4); g.globalAlpha = 1;
        const swing = Math.sin(t * 0.9 + seed) * 0.35 + Math.sin(t * 2.3) * 0.08;
        g.save(); g.translate(x, y); g.rotate(swing); g.translate(-x, -y);
        g.fillStyle = tint();
        for (let k = 0; k < 4; k++) {
          const a = k * Math.PI / 2 - Math.PI / 2;
          g.beginPath(); g.moveTo(x + Math.cos(a) * (r - 12), y + Math.sin(a) * (r - 12));
          g.lineTo(x + Math.cos(a + 0.5) * 11, y + Math.sin(a + 0.5) * 11); g.lineTo(x + Math.cos(a - 0.5) * 11, y + Math.sin(a - 0.5) * 11); g.closePath(); g.fill();
        }
        crayon(() => { for (let k = 0; k < 4; k++) { const a = k * Math.PI / 2 - Math.PI / 2; g.moveTo(x + Math.cos(a) * (r - 12), y + Math.sin(a) * (r - 12)); g.lineTo(x + Math.cos(a + 0.5) * 11, y + Math.sin(a + 0.5) * 11); g.moveTo(x + Math.cos(a) * (r - 12), y + Math.sin(a) * (r - 12)); g.lineTo(x + Math.cos(a - 0.5) * 11, y + Math.sin(a - 0.5) * 11); } }, INK, 1.8);
        g.restore();
        /* every point of this compass says the same thing */
        g.fillStyle = INK; g.font = `700 13px ${opts.font}`; g.textAlign = 'center'; g.textBaseline = 'middle';
        for (let k = 0; k < 4; k++) {
          const a = k * Math.PI / 2 - Math.PI / 2;
          g.save(); g.translate(x + Math.cos(a) * (r + 15), y + Math.sin(a) * (r + 15)); g.rotate(a + Math.PI / 2); g.fillText('DOWN', 0, 0); g.restore();
        }
        break;
      }
      case 'fish': {
        /* a loose school, all facing the way they swim; turns round at the ends of its reach */
        const reach = Math.min(160, it.size * 1.2), phase = (t * 0.09 + (seed % 1000) / 1000) % 2;
        const dir = phase < 1 ? 1 : -1, along = phase < 1 ? phase : 2 - phase;
        const cx0 = x - reach + along * reach * 2;
        const body = theme ? theme.a : '#ffb36b';
        for (let k = 0; k < 6; k++) {
          const fx = cx0 + (hash(seed, k, 1) - 0.5) * 90, fy = y + (hash(seed, k, 2) - 0.5) * 60 + Math.sin(t * 2 + k) * 3;
          const s = 0.8 + hash(seed, k, 3) * 0.5, wag = ((f + k) % 2 ? 3 : -3) * s;
          g.save(); g.translate(fx, fy); g.scale(dir * s, s);
          g.fillStyle = k % 3 ? body : '#ffe08a';
          g.beginPath(); g.ellipse(0, 0, 11, 6, 0, 0, TAU); g.moveTo(-9, 0); g.lineTo(-18, -6 + wag); g.lineTo(-18, 6 + wag); g.closePath(); g.fill();
          crayon(() => { g.ellipse(0, 0, 11, 6, 0, 0, TAU); g.moveTo(-9, 0); g.lineTo(-18, -6 + wag); g.lineTo(-18, 6 + wag); g.closePath(); }, INK, 1.6 / s);
          g.fillStyle = INK; g.beginPath(); g.arc(5, -1.5, 1.4, 0, TAU); g.fill();
          g.restore();
        }
        break;
      }
      case 'jelly': {
        /* a jellyfish with a spiral painted on its bell: pulses, drifts, glows in the theme colour */
        const pulse = Math.sin(t * 2.2 + seed) * 0.12, rise = Math.sin(t * 0.5 + seed) * 10;
        const jy = y + rise, bw = 26 * (1 + pulse), bh = 22 * (1 - pulse * 0.8);
        const c = theme ? theme.spiral[seed % theme.spiral.length] : (seed % 2 ? '#ff8ed0' : '#a58bff');
        const glow = g.createRadialGradient(x, jy, 4, x, jy, 70);
        glow.addColorStop(0, rgba(c, 0.35)); glow.addColorStop(1, rgba(c, 0));
        g.fillStyle = glow; g.beginPath(); g.arc(x, jy, 70, 0, TAU); g.fill();
        g.globalAlpha = 0.8;
        for (let k = 0; k < 5; k++) {
          const tx = x - bw * 0.7 + k * bw * 0.35;
          crayon(() => { g.moveTo(tx, jy + 2); for (let s = 1; s <= 6; s++) g.lineTo(tx + Math.sin(t * 2.5 + k + s * 0.9 + seed) * 4, jy + 2 + s * 9); }, rgba(FOAM, 0.7), 1.8);
        }
        g.globalAlpha = 1;
        g.fillStyle = rgba(c, 0.85);
        g.beginPath(); g.ellipse(x, jy, bw, bh, 0, Math.PI, 0); g.closePath(); g.fill();
        g.save(); g.beginPath(); g.ellipse(x, jy, bw, bh, 0, Math.PI, 0); g.closePath(); g.clip();
        spiral(x, jy - bh * 0.35, bw * 0.75, t * 1.8 + seed, 1, [rgba(FOAM, 0.85)], 1.8, 1);
        g.restore();
        crayon(() => { g.ellipse(x, jy, bw + jit(seed, 1, f, 0.8), bh, 0, Math.PI, 0); g.closePath(); }, rgba(FOAM, 0.9), 2.2);
        break;
      }
      case 'kelp': {
        const tall = Math.min(90 + (seed % 5) * 22, it.size * 1.3), base = y + 50;
        for (let k = 0; k < 3; k++) {
          const kx = x + (k - 1) * 14;
          crayon(() => {
            g.moveTo(kx, base);
            for (let s = 1; s <= 8; s++) { const sy = base - (tall * s) / 8; g.lineTo(kx + Math.sin(t * 0.9 + s * 0.6 + k + seed) * (s * 1.6), sy); }
          }, rgba(theme ? theme.a : '#5fd3b6', 0.55), 5 - k);
        }
        break;
      }
      case 'serpent': {
        g.save(); g.translate(x, y); g.scale(1.5, 1.5); g.translate(-x, -y);
        const c = theme ? theme.a : '#9fd3c7';
        for (let k = 0; k < 3; k++) {
          const hx = x - 60 + k * 42, hr = 15 - k * 2, hy = y + Math.sin(t * 1.3 + k * 1.1 + seed) * 3;
          g.fillStyle = c;
          g.beginPath(); g.arc(hx, hy, hr, Math.PI, 0); g.closePath(); g.fill();
          crayon(() => g.arc(hx, hy, hr + jit(seed, k, f, 0.8), Math.PI, 0), INK, 2.4);
        }
        const hx = x + 70, hy = y - 14 + Math.sin(t * 1.3 + 3.3 + seed) * 3;
        g.fillStyle = c;
        g.beginPath(); g.moveTo(hx - 14, y); g.quadraticCurveTo(hx - 16, hy - 6, hx, hy - 12); g.quadraticCurveTo(hx + 20, hy - 12, hx + 22, hy); g.quadraticCurveTo(hx + 12, hy + 6, hx + 2, y); g.closePath(); g.fill();
        crayon(() => { g.moveTo(hx - 14, y); g.quadraticCurveTo(hx - 16, hy - 6, hx, hy - 12); g.quadraticCurveTo(hx + 20, hy - 12, hx + 22, hy); g.quadraticCurveTo(hx + 12, hy + 6, hx + 2, y); }, INK, 2.4);
        g.fillStyle = '#fffdf8'; g.beginPath(); g.arc(hx + 7, hy - 4, 5, 0, TAU); g.fill();
        spiral(hx + 7, hy - 4, 4.5, t * 3 + seed, 1, [INK], 1.2, 1);   // the serpent has spiral eyes
        g.globalAlpha = 0.8;
        crayon(() => { for (let k = 0; k < 5; k++) { const wx = x - 82 + k * 40; g.moveTo(wx, y + 1); g.quadraticCurveTo(wx + 6, y - 3 + jit(seed, k + 20, f, 1), wx + 12, y + 1); } }, dark ? FOAM : '#ffffff', 2.2);
        g.globalAlpha = 1;
        g.restore();
        break;
      }
      case 'bottle': {
        const bob = Math.sin(t * 1.2 + seed) * 0.12;
        g.save(); g.translate(x, y); g.rotate(-0.5 + bob);
        g.fillStyle = 'rgba(190,235,220,.75)';
        g.beginPath(); g.roundRect?.(-16, -8, 26, 16, 6); g.rect(10, -4, 8, 8); g.fill();
        crayon(() => { g.roundRect?.(-16, -8, 26, 16, 6); g.rect(10, -4, 8, 8); }, INK, 2);
        g.fillStyle = '#fbe9c8'; g.fillRect(-10, -4, 14, 8);
        g.fillStyle = INK; g.font = `700 6px ${opts.font}`; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText('I AGREE', -3, 0);
        g.restore();
        break;
      }
      case 'bubbles': {
        g.lineWidth = 1.8;
        for (let k = 0; k < 5; k++) {
          const span = 260, p = ((t * 26 + k * (span / 5) + (seed % span)) % span);
          const by = y + 80 - p, bx = x + Math.sin(by / 26 + seed) * 7, br = 2 + (p / span) * 6;
          g.strokeStyle = rgba(FOAM, 0.55 * (1 - p / span) + 0.15);
          g.beginPath(); g.arc(bx, by, br, 0, TAU); g.stroke();
        }
        break;
      }
      case 'word': {
        const w = words[seed % words.length];
        if (!w) break;
        const size = 26 + (seed % 5) * 6;
        g.save();
        g.translate(x + jit(seed, 1, f, 0.8) + Math.sin(t * 0.3 + seed) * 22, y + jit(seed, 2, f, 0.8) + Math.sin(t * 0.7 + seed) * 9);
        g.rotate(((seed % 9) - 4) * 0.03 + Math.sin(t * 0.5 + seed) * 0.06);
        g.globalAlpha = 0.55 + 0.45 * (0.5 + 0.5 * Math.sin(t * 0.4 + seed * 1.3));
        g.font = `700 ${size}px ${opts.font}`; g.textAlign = 'center'; g.textBaseline = 'middle';
        g.fillStyle = dark ? rgba(FOAM, 0.16) : rgba(INK, 0.075);
        g.fillText(w, 0, 0);
        g.globalAlpha = 1;
        g.restore();
        break;
      }
    }
  }

  /* The Drop: the trunk road ends in a maelstrom; the run's words circle it and sink. */
  function drawMaelstrom(f, t, words, theme) {
    const { x, y, r } = maelstrom;
    const cols = (theme ? theme.spiral : ['#ff6fb5', '#a58bff']).map((c) => rgba(c, 0.9));
    g.fillStyle = 'rgba(8,4,20,.35)';
    g.beginPath(); g.ellipse(x, y, r * 1.05, r * 0.8, 0, 0, TAU); g.fill();
    spiral(x, y, r, -(t * 0.35) % TAU, 4, [...cols, rgba(FOAM, 0.55)], 4.2, 0.9);
    g.globalAlpha = 0.5;
    crayon(() => g.ellipse(x, y, r + 14 + jit(3, 1, f, 2), (r + 14) * 0.78, 0, 0, TAU), rgba(FOAM, 0.9), 3);
    g.globalAlpha = 1;
    if (!words.length) return;
    g.font = `700 22px ${opts.font}`; g.textAlign = 'center'; g.textBaseline = 'middle';
    for (let k = 0; k < 6; k++) {
      const life = ((t * 0.05) + k / 6) % 1;
      const rr = r * (1.05 - life), ang = -(t * 0.35) - life * TAU * 1.6 + k;
      g.save();
      g.translate(x + Math.cos(ang) * rr, y + Math.sin(ang) * rr * 0.78);
      g.rotate(ang + Math.PI / 2);
      g.scale(1 - life * 0.7, 1 - life * 0.7);
      g.fillStyle = rgba(FOAM, 0.75 * (1 - life));
      g.fillText(words[k % words.length], 0, 0);
      g.restore();
    }
  }

  function paint() {
    const v = view();
    const dpr = Math.min(2, win.devicePixelRatio || 1);
    const cw = Math.max(1, Math.round(v.w * dpr)), chh = Math.max(1, Math.round(v.h * dpr));
    if (canvas.width !== cw || canvas.height !== chh) { canvas.width = cw; canvas.height = chh; }
    const k = dpr * v.scale;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.fillStyle = DEPTH[DEPTH.length - 1][1];
    g.fillRect(0, 0, cw, chh);
    g.setTransform(k, 0, 0, k, -v.left * dpr, -v.top * dpr);
    const wv = { scale: v.scale, x0: v.left / v.scale, y0: v.top / v.scale, x1: (v.left + v.w) / v.scale, y1: (v.top + v.h) / v.scale };
    const quiet = still();
    const step = quiet ? 0 : Math.floor(win.performance.now() / BOIL_MS);
    const f = step % 4, t = quiet ? 0 : win.performance.now() / 1000;
    const theme = opts.theme?.() || null;
    const words = opts.words?.() || [];
    g.fillStyle = gradient();
    g.fillRect(wv.x0 - 2, wv.y0 - 2, wv.x1 - wv.x0 + 4, wv.y1 - wv.y0 + 4);
    washes(wv, t);
    shallows(wv, f, t);
    grain(wv, f);
    contours(wv, f, t);
    waves(wv, f, t);
    for (const it of decor) {
      const pad = 160;
      if (it.x < wv.x0 - pad || it.x > wv.x1 + pad || it.y < wv.y0 - pad || it.y > wv.y1 + pad) continue;
      drawDecor(it, f, t, words, theme);
    }
    if (maelstrom.y + maelstrom.r > wv.y0 && maelstrom.y - maelstrom.r < wv.y1) drawMaelstrom(f, t, words, theme);
    shores(wv, f, t, opts.shut || (() => false));
  }

  function loop() {
    raf = win.requestAnimationFrame(loop);
    if (doc.hidden) return;
    const now = win.performance.now();
    if (dirty || (!still() && now - lastDraw >= FRAME_MS)) { lastDraw = now; dirty = false; paint(); }
  }
  return {
    canvas, decor, maelstrom,
    paint,
    dirty() { dirty = true; },
    start() { if (running) return; running = true; dirty = true; raf = win.requestAnimationFrame(loop); },
    stop() { running = false; win.cancelAnimationFrame(raf); },
    destroy() { this.stop(); canvas.remove(); },
  };
}
