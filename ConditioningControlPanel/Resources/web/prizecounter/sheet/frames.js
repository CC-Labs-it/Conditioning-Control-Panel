/* sheet/frames.js - the profile frames, drawn the way the desktop draws them. buildFrame() is a line-for-line port of
 * CCP.Core/Frames/ProfileFrames.cs (Build): the same steps in canvas units (0..1, the avatar circle centred with radius
 * WINDOW). Both sides are pinned to tests/fixtures/frame-probes.json (the page in tests/sheet-frames.test.mjs, the
 * desktop in ProfileFrameTests). drawFrame() paints the steps on a 2D canvas; drawFramed() adds the round portrait. */

export const LACE = 'profile.frame.lace', INK_STAMP = 'profile.frame.ink-stamp', GILT = 'profile.frame.gilt',
  POLAROID = 'profile.frame.polaroid', ROOM_HOST = 'prime.frame.room-host';
export const FRAME_IDS = Object.freeze([LACE, INK_STAMP, GILT, POLAROID, ROOM_HOST]);
export const C = 0.5, WINDOW = 0.39, KEEP = 0.365, INK = '#2B1136', K = 0.018;
const TAU = Math.PI * 2;

export const isFrame = (id) => FRAME_IDS.includes(id);
/** Detail by drawn canvas size in pixels: 0 small (list rows), 1 medium (profile card), 2 large (shop stage). */
export const detailFor = (px) => (px <= 48 ? 0 : px < 150 ? 1 : 2);
/** Motion level 'full' | 'less' | 'off' (the page's init.motion) and Gentle, as the desktop's FrameMotion.Of. */
export function frameMotion(level, gentle) {
  let s = level === 'off' ? 0 : level === 'less' ? 0.5 : 1;
  const a = s;
  if (gentle) s = Math.min(s, 0.5);
  return { speed: s, amp: a, gentle: !!gentle };
}

const clamp = (x, lo, hi) => Math.min(hi, Math.max(lo, x));
const Px = (r, a) => C + r * Math.cos(a);
const Py = (r, a) => C + r * Math.sin(a);
function place(local, ox, oy, rot, s) {
  const o = new Array(local.length), c = Math.cos(rot), si = Math.sin(rot);
  for (let i = 0; i < local.length; i += 2) {
    const x = local[i] * s, y = local[i + 1] * s;
    o[i] = ox + x * c - y * si;
    o[i + 1] = oy + x * si + y * c;
  }
  return o;
}
function starPts(x, y, r, rot) {
  const pts = new Array(20);
  for (let i = 0; i < 10; i++) {
    const a = rot - Math.PI / 2 + i * Math.PI / 5, rr = i % 2 === 0 ? r : r * 0.48;
    pts[2 * i] = x + rr * Math.cos(a); pts[2 * i + 1] = y + rr * Math.sin(a);
  }
  return pts;
}
const heartPts = (x, y, s) => place([0, 0.9, -0.55, 0.35, -0.95, -0.1, -0.8, -0.65, -0.35, -0.8, 0, -0.45, 0.35, -0.8, 0.8, -0.65, 0.95, -0.1, 0.55, 0.35], x, y, 0, s);
function roundRect(x0, y0, x1, y1, r, seg) {
  const pts = [];
  const arc = (cx, cy, a0) => { for (let i = 0; i <= seg; i++) { const a = a0 + i * (Math.PI / 2) / seg; pts.push(cx + r * Math.cos(a), cy + r * Math.sin(a)); } };
  arc(x1 - r, y0 + r, -Math.PI / 2); arc(x1 - r, y1 - r, 0); arc(x0 + r, y1 - r, Math.PI / 2); arc(x0 + r, y0 + r, Math.PI);
  return pts;
}
const smooth01 = (x) => { x = clamp(x, 0, 1); return x * x * (3 - 2 * x); };

/** The frame's steps at time t seconds (see ProfileFrames.Build). Unknown ids give []. */
export function buildFrame(id, t, m = frameMotion('full', false), detail = 1) {
  const tt = t * m.speed, amp = m.amp, gentle = !!m.gentle, d = clamp(detail, 0, 2), out = [];
  const E = (x, y, rx, ry, rot, fill, stroke, w, alpha, live) => { out.push({ kind: 'e', x, y, rx, ry, rot, fill, stroke, w, alpha, live }); };
  const P = (pts, closed, smooth, fill, stroke, w, alpha, live) => { const p = { kind: 'p', pts, closed, smooth, hole: 0, fill, fill2: null, g0: 0, g1: 0, stroke, w, alpha, live }; out.push(p); return p; };
  const ring = (r, width, colour, live) => { E(C, C, r, r, 0, null, INK, width + 2 * K, 1, live); E(C, C, r, r, 0, null, colour, width, 1, live); };
  const sparkle = (x, y, s, alpha, live) => {
    const pts = new Array(16);
    for (let i = 0; i < 8; i++) { const a = i * Math.PI / 4 - Math.PI / 2, r = i % 2 === 0 ? s : s * 0.3; pts[2 * i] = x + r * Math.cos(a); pts[2 * i + 1] = y + r * Math.sin(a); }
    P(pts, true, false, '#FFFBE6', null, 0, alpha, live);
  };

  if (id === LACE) {
    const pink = '#FF7EB9', lace = '#FFF7FB', eyelet = '#FF9FCB', ribbon = '#FF3D8B', ribbon2 = '#D81E6C';
    const n = d === 0 ? 12 : d === 1 ? 22 : 26, rr = d === 0 ? 0.432 : 0.44, rs = d === 0 ? 0.048 : d === 1 ? 0.044 : 0.04;
    for (let i = 0; i < n; i++) {
      const a = i * TAU / n - Math.PI / 2, w = Math.sin(TAU * (tt / 3.2) - i * TAU * 2 / n), r = rs * (1 + 0.12 * amp * w);
      E(Px(rr + 0.004 * amp * w, a), Py(rr + 0.004 * amp * w, a), r, r, 0, lace, INK, K * 0.8, 1, true);
      if (d > 0) E(Px(rr + 0.012, a), Py(rr + 0.012, a), r * 0.3, r * 0.3, 0, eyelet, null, 0, 1, true);
    }
    ring(0.414, 0.05, pink, false);
    if (d > 0) {
      const mm = Math.trunc(n / 2);
      for (let i = 0; i < mm; i++) { const a = (i + 0.5) * TAU / mm - Math.PI / 2; E(Px(0.414, a), Py(0.414, a), 0.024, 0.009, a * 180 / Math.PI + 90, lace, null, 0, 1, false); }
    }
    E(Px(0.414, -1.95), Py(0.414, -1.95), 0.05, 0.012, -1.95 * 180 / Math.PI + 90, '#FFFFFF', null, 0, 0.75, false);
    const sway = amp * 0.16 * Math.sin(TAU * tt / 2.6);
    const bx = Px(0.47, Math.PI / 4), by = Py(0.47, Math.PI / 4), rot = -Math.PI / 4 + sway, s = d === 0 ? 1.15 : d === 1 ? 1.2 : 1.1;
    const flutter = amp * 0.012 * Math.sin(TAU * tt / 1.3);
    if (d > 0) {
      P(place([-0.01, 0.01, -0.035, 0.06, -0.05, 0.105 + flutter, -0.03, 0.095, -0.012, 0.11 - flutter, 0.0, 0.03], bx, by, rot, s), true, false, ribbon2, INK, K * 0.8, 1, true);
      P(place([0.01, 0.01, 0.035, 0.06, 0.05, 0.105 - flutter, 0.03, 0.095, 0.012, 0.11 + flutter, 0.0, 0.03], bx, by, rot, s), true, false, ribbon2, INK, K * 0.8, 1, true);
    }
    P(place([0, 0, -0.06, -0.055, -0.125, -0.035, -0.125, 0.035, -0.065, 0.045], bx, by, rot, s), true, true, ribbon, INK, K, 1, true);
    P(place([0, 0, 0.06, -0.055, 0.125, -0.035, 0.125, 0.035, 0.065, 0.045], bx, by, rot, s), true, true, ribbon, INK, K, 1, true);
    const g = place([-0.085, -0.025, 0.085, -0.025], bx, by, rot, s);
    E(g[0], g[1], 0.024 * s, 0.009 * s, rot * 180 / Math.PI - 20, '#FFFFFF', null, 0, 0.8, true);
    E(g[2], g[3], 0.024 * s, 0.009 * s, rot * 180 / Math.PI + 20, '#FFFFFF', null, 0, 0.8, true);
    E(bx, by, 0.028 * s, 0.03 * s, rot * 180 / Math.PI, ribbon2, INK, K, 1, true);
  } else if (id === INK_STAMP) {
    const paper = '#FFF3D6', paper2 = '#F6DFAE', field = '#7FE7C9', field2 = '#3FBF9C', postInk = '#3B1A5C', red = '#FF4F7B';
    const n = d === 0 ? 6 : d === 1 ? 9 : 11, x0 = 0.035, x1 = 0.965, L = x1 - x0, rb = L / n * 0.3, pts = [];
    const side = (sx, sy, dx, dy, nx, ny) => {
      pts.push(sx, sy);
      for (let k = 0; k < n; k++) {
        const cx = sx + dx * (k + 0.5) * L / n, cy = sy + dy * (k + 0.5) * L / n;
        for (let u = 0; u <= 6; u++) { const a = u * Math.PI / 6; pts.push(cx - rb * Math.cos(a) * dx + rb * Math.sin(a) * nx, cy - rb * Math.cos(a) * dy + rb * Math.sin(a) * ny); }
      }
    };
    side(x0, x0, 1, 0, 0, 1); side(x1, x0, 0, 1, -1, 0); side(x1, x1, -1, 0, 0, -1); side(x0, x1, 0, -1, 1, 0);
    Object.assign(P(pts, true, false, paper, INK, K, 1, false), { hole: WINDOW, fill2: paper2, g0: 0, g1: 1 });
    Object.assign(P(roundRect(0.095, 0.095, 0.905, 0.905, 0.04, d === 0 ? 2 : 5), true, false, field, INK, K * 0.8, 1, false), { hole: WINDOW, fill2: field2, g0: 0.1, g1: 0.9 });
    if (d > 0) {
      P(starPts(0.165, 0.165, 0.035, 0), true, false, '#FFFFFF', INK, K * 0.6, 1, false);
      E(0.2, 0.86, 0.045, 0.012, -45, '#FFFFFF', null, 0, 0.55, false);
    }
    const beat = Math.pow(Math.max(0, Math.sin(TAU * tt / 2.8)), 6), hs = (d === 0 ? 0.075 : 0.055) * (1 + 0.16 * amp * beat);
    P(heartPts(0.16, 0.835, hs), true, true, red, INK, K * 0.8, 1, true);
    E(0.14, 0.81, hs * 0.28, hs * 0.16, -35, '#FFFFFF', null, 0, 0.8, true);
    const pcx = 0.845, pcy = 0.155, pr = d === 0 ? 0.1 : 0.105;
    E(pcx, pcy, pr * 0.62, pr * 0.62, 0, null, postInk, K * 0.7, 0.85, false);
    const dashes = d === 0 ? 8 : 14, turn = amp > 0 ? TAU * tt / 16 : 0;
    for (let i = 0; i < dashes; i++) { const a = i * TAU / dashes + turn; E(pcx + pr * 0.86 * Math.cos(a), pcy + pr * 0.86 * Math.sin(a), pr * 0.16, pr * 0.07, a * 180 / Math.PI + 90, postInk, null, 0, 0.85, true); }
    if (d > 0) {
      const drift = amp * 0.02 * Math.sin(TAU * tt / 5);
      for (let j = -1; j <= 1; j++) {
        const line = new Array(14);
        for (let u = 0; u < 7; u++) { line[2 * u] = pcx - pr * 0.45 + u * pr * 0.15; line[2 * u + 1] = pcy + j * pr * 0.28 + 0.008 * Math.sin(u * 1.6 + TAU * tt / 2.2 * (amp > 0 ? 1 : 0)) + drift * 0.2; }
        P(line, false, true, null, postInk, K * 0.45, 0.85, true);
      }
    }
  } else if (id === GILT) {
    const gold = '#FFD75A', gold2 = '#E0931A', vein = '#A8600E', ruby = '#FF3D6E', ruby2 = '#B0123F';
    const n = d === 0 ? 5 : d === 1 ? 9 : 11, gap = 0.42, len = d === 0 ? 0.12 : d === 1 ? 0.1 : 0.094, wid = d === 0 ? 0.06 : d === 1 ? 0.05 : 0.046;
    E(C, C, 0.418, 0.418, 0, null, INK, 0.022 + 2 * K * 0.8, 1, false);
    E(C, C, 0.418, 0.418, 0, null, gold2, 0.022, 1, false);
    const tips = [], mids = [];
    for (let sd = -1; sd <= 1; sd += 2) {
      for (let k = 0; k < n; k++) {
        const u = (k + 0.5) / n, a = Math.PI / 2 - sd * u * (Math.PI - gap / 2), r = 0.448 + (k % 2 === 0 ? 0.004 : -0.004);
        const cx = Px(r, a), cy = Py(r, a), dir = Math.atan2(-Math.cos(a) * sd, Math.sin(a) * sd) + (k % 2 === 0 ? 0.42 : -0.18) * sd;
        const leaf = new Array(28);
        for (let i = 0; i < 14; i++) {
          const q = i < 7 ? -1 + i / 3.0 : 1 - (i - 7) / 3.0, half = wid / 2 * Math.pow(Math.max(0, 1 - q * q), 0.7) * (i < 7 ? 1 : -1), lx = q * len / 2, ly = half;
          leaf[2 * i] = cx + lx * Math.cos(dir) - ly * Math.sin(dir); leaf[2 * i + 1] = cy + lx * Math.sin(dir) + ly * Math.cos(dir);
        }
        Object.assign(P(leaf, true, false, gold, INK, K * 0.75, 1, false), { fill2: gold2, g0: cy - len / 2, g1: cy + len / 2 });
        if (d > 0) P([cx - len * 0.38 * Math.cos(dir), cy - len * 0.38 * Math.sin(dir), cx + len * 0.3 * Math.cos(dir), cy + len * 0.3 * Math.sin(dir)], false, false, null, vein, 0.006, 0.8, false);
        tips.push(cx + len * 0.32 * Math.cos(dir), cy + len * 0.32 * Math.sin(dir));
        mids.push(cx, cy, u, dir);
      }
    }
    const ph = tt % 5 / 2.4 * 1.5 - 0.25;
    for (let i = 0; i < mids.length; i += 4) {
      const u = mids[i + 2], lit = amp > 0 ? amp * Math.exp(-Math.pow((u - ph) / 0.13, 2)) : 0.35 * u;
      E(mids[i] - 0.004, mids[i + 1] - 0.006, len * 0.26, wid * 0.17, mids[i + 3] * 180 / Math.PI, '#FFFBE0', null, 0, 0.25 + 0.75 * lit, true);
    }
    P([0.45, 0.88, 0.5, 0.93, 0.55, 0.88, 0.535, 0.975, 0.5, 0.945, 0.465, 0.975], true, false, gold2, INK, K * 0.75, 1, false);
    E(0.5, 0.915, 0.04, 0.036, 0, ruby, INK, K, 1, false);
    if (d > 0) E(0.5, 0.925, 0.026, 0.018, 0, ruby2, null, 0, 0.6, false);
    E(0.488, 0.902, 0.014, 0.008, -30, '#FFFFFF', null, 0, 0.9, false);
    const spin = amp * 0.25 * Math.sin(TAU * tt / 6);
    Object.assign(P(starPts(0.5, 0.062, d === 0 ? 0.05 : 0.042, spin), true, false, gold, INK, K * 0.8, 1, true), { fill2: gold2, g0: 0.02, g1: 0.1 });
    const tw = tt / 1.7, which = (Math.floor(tw) * 5 + 3) % (tips.length / 2), fade = amp > 0 ? Math.pow(Math.sin(Math.PI * (tw - Math.floor(tw))), 2) : 0;
    sparkle(tips[2 * which], tips[2 * which + 1], d === 0 ? 0 : 0.034, fade, true);
  } else if (id === POLAROID) {
    const card = '#FFFEF8', card2 = '#EFE6D2', photo = '#8FD8FF', photo2 = '#C7B5FF', tape = '#FF9EC7', marker = '#FF4F8B';
    Object.assign(P(roundRect(0.05, 0.035, 0.95, 0.985, 0.03, d === 0 ? 2 : 4), true, false, card, INK, K, 1, false), { hole: WINDOW, fill2: card2, g0: 0, g1: 1 });
    Object.assign(P([0.1, 0.085, 0.9, 0.085, 0.9, 0.9, 0.1, 0.9], true, false, photo, INK, K * 0.6, 1, false), { hole: WINDOW, fill2: photo2, g0: 0.085, g1: 0.9 });
    if (d > 0) {
      E(0.165, 0.79, 0.038, 0.038, 0, '#FFE36B', INK, K * 0.55, 1, false);
      P([0.75, 0.16, 0.79, 0.13, 0.84, 0.14, 0.87, 0.17, 0.83, 0.19, 0.77, 0.19], true, true, '#FFFFFF', INK, K * 0.5, 1, false);
    }
    const ws = amp > 0 ? 1 - amp * (1 - smooth01(tt % 6 / 2.2)) : 1, sn = d === 0 ? 6 : 12, shown = Math.max(2, Math.floor(sn * ws + 0.5));
    const scr = new Array(shown * 2);
    for (let i = 0; i < shown; i++) { scr[2 * i] = 0.13 + i * 0.36 / (sn - 1); scr[2 * i + 1] = 0.94 + (i % 2 === 0 ? -0.012 : 0.012); }
    if (d > 0) P(scr, false, true, null, marker, 0.012, 1, true);
    else P(scr, false, false, null, marker, 0.02, 1, true);
    const t4 = tt % 2.4, beat = amp * (Math.pow(Math.max(0, Math.sin(TAU * t4 / 0.5)), 2) * (t4 < 1 ? 1 : 0)), hs = (d === 0 ? 0.042 : 0.034) * (1 + 0.18 * beat);
    P(heartPts(0.79, 0.945, hs), true, true, marker, INK, K * 0.7, 1, true);
    Object.assign(P(starPts(0.875, 0.94, d === 0 ? 0.04 : 0.033, amp * TAU * tt / 14), true, false, '#FFD75A', INK, K * 0.7, 1, true), { fill2: '#F2A51F', g0: 0.9, g1: 0.98 });
    for (let sd = -1; sd <= 1; sd += 2) {
      const lift = amp * 0.07 * Math.sin(TAU * tt / 3.4 + (sd > 0 ? 1.3 : 0)), tx = 0.5 + sd * 0.37, ty = 0.11, tr = sd * 0.72 + lift * sd;
      const strip = [-0.11, -0.035, -0.095, -0.02, -0.11, -0.005, -0.095, 0.01, -0.11, 0.025, -0.1, 0.035, 0.11, 0.035, 0.095, 0.02, 0.11, 0.005, 0.095, -0.01, 0.11, -0.025, 0.1, -0.035];
      P(place(strip, tx, ty, tr, 1), true, false, tape, INK, K * 0.6, 0.92, true);
      if (d > 0) for (let j = -1; j <= 1; j++) P(place([j * 0.05 - 0.012, -0.03, j * 0.05 + 0.006, -0.03, j * 0.05 + 0.018, 0.03, j * 0.05, 0.03], tx, ty, tr, 1), true, false, '#FFFFFF', null, 0, 0.55, true);
    }
  } else if (id === ROOM_HOST) {
    const velvet = '#6A23A0', velvet2 = '#3A1160', gold = '#FFD24A', gold2 = '#E0931A', bulb = '#FFF6C2', dim = '#C79A3E', glow = '#FFD65A', gem = '#FF4FA0';
    const rays = d === 0 ? 0 : d === 1 ? 12 : 16, spin = amp > 0 ? TAU * tt / 30 : 0;
    for (let i = 0; i < rays; i++) {
      const a = i * TAU / rays + spin, edge = 0.5 / Math.max(Math.abs(Math.cos(a)), Math.abs(Math.sin(a))) - 0.012;
      const tip = Math.min(i % 2 === 0 ? 0.66 : 0.57, edge), a0 = a - 0.12, a1 = a + 0.12;
      P([Px(0.43, a0), Py(0.43, a0), Px(tip, a), Py(tip, a), Px(0.43, a1), Py(0.43, a1)], true, false, i % 2 === 0 ? '#FFE27A' : '#FF8FC8', INK, K * 0.5, 0.9, true);
    }
    E(C, C, 0.44, 0.44, 0, null, INK, 0.075 + 2 * K, 1, false);
    E(C, C, 0.44, 0.44, 0, null, velvet, 0.075, 1, false);
    if (d > 0) E(C, C, 0.425, 0.425, 0, null, velvet2, 0.02, 0.6, false);
    E(C, C, 0.4035, 0.4035, 0, null, gold, 0.011, 1, false);
    E(C, C, 0.4765, 0.4765, 0, null, gold, 0.011, 1, false);
    const nb = d === 0 ? 8 : d === 1 ? 14 : 18, contrast = gentle ? 0.4 : 0.8;
    for (let i = 0; i < nb; i++) {
      const a = i * TAU / nb - Math.PI / 2 + Math.PI / nb, bx = Px(0.44, a), by = Py(0.44, a);
      const raw = 0.5 + 0.5 * Math.cos(TAU * (i * 3.0 / nb - tt / 2.4)), g = 1 - contrast * amp * (1 - raw), br = d === 0 ? 0.026 : 0.018;
      if (d > 0) E(bx, by, br * 2, br * 2, 0, glow, null, 0, 0.5 * g, true);
      E(bx, by, br, br, 0, dim, INK, K * 0.55, 1, false);
      E(bx, by, br * 0.85, br * 0.85, 0, bulb, null, 0, g, true);
    }
    const tilt = amp * 0.07 * Math.sin(TAU * tt / 3.6), bob = amp * 0.006 * Math.sin(TAU * tt / 1.8), kx = 0.5, ky = 0.092 + bob, ks = 0.92;
    Object.assign(P(place([-0.1, 0.035, -0.105, -0.045, -0.055, -0.005, 0, -0.065, 0.055, -0.005, 0.105, -0.045, 0.1, 0.035], kx, ky, tilt, ks), true, false, gold, INK, K, 1, true), { fill2: gold2, g0: ky - 0.06, g1: ky + 0.04 });
    P(place([-0.098, 0.012, 0.098, 0.012, 0.1, 0.035, -0.1, 0.035], kx, ky, tilt, ks), true, false, gold2, INK, K * 0.6, 1, true);
    const pearls = place([-0.105, -0.05, 0, -0.072, 0.105, -0.05], kx, ky, tilt, ks);
    for (let i = 0; i < 6; i += 2) E(pearls[i], pearls[i + 1], 0.013 * ks, 0.013 * ks, 0, '#FFFFFF', INK, K * 0.5, 1, true);
    const gp = place([0, 0.012], kx, ky, tilt, ks);
    E(gp[0], gp[1], 0.019 * ks, 0.016 * ks, 0, gem, INK, K * 0.6, 1, true);
    E(gp[0] - 0.005, gp[1] - 0.005, 0.006, 0.004, -30, '#FFFFFF', null, 0, 0.9, true);
    const tw = amp > 0 ? Math.pow(Math.max(0, Math.sin(TAU * tt / 3.1)), 2) : 0;
    sparkle(0.655, 0.05, d === 0 ? 0 : 0.03, tw, true);
    sparkle(0.35, 0.94, d === 0 ? 0 : 0.026, amp > 0 ? Math.pow(Math.max(0, Math.sin(TAU * tt / 3.1 + 2.2)), 2) : 0, true);
  }
  return out;
}

/* ---------- drawing ---------- */
function tracePath(g, p, s, x, y) {
  const q = p.pts, n = q.length / 2, X = (i) => x + q[2 * i] * s, Y = (i) => y + q[2 * i + 1] * s;
  if (n < 2) return;
  if (p.smooth && n > 2) {
    if (p.closed) {
      g.moveTo((X(n - 1) + X(0)) / 2, (Y(n - 1) + Y(0)) / 2);
      for (let i = 0; i < n; i++) { const j = (i + 1) % n; g.quadraticCurveTo(X(i), Y(i), (X(i) + X(j)) / 2, (Y(i) + Y(j)) / 2); }
    } else {
      g.moveTo(X(0), Y(0));
      for (let i = 1; i < n - 1; i++) g.quadraticCurveTo(X(i), Y(i), (X(i) + X(i + 1)) / 2, (Y(i) + Y(i + 1)) / 2);
      g.lineTo(X(n - 1), Y(n - 1));
    }
  } else {
    g.moveTo(X(0), Y(0));
    for (let i = 1; i < n; i++) g.lineTo(X(i), Y(i));
  }
  if (p.closed) g.closePath();
}

/** Paints a frame's steps on a 2D context, the canvas square at (x, y) with side s pixels. Strokes keep at least
 * 0.75 px so a small frame stays inked. */
export function drawFrame(g, prims, x, y, s) {
  g.save();
  g.lineJoin = 'round'; g.lineCap = 'round';
  for (const p of prims) {
    if (p.alpha <= 0.002) continue;
    g.globalAlpha = Math.min(1, p.alpha);
    g.beginPath();
    if (p.kind === 'e') {
      if (p.rx <= 0 || p.ry <= 0) continue;
      g.ellipse(x + p.x * s, y + p.y * s, p.rx * s, p.ry * s, p.rot * Math.PI / 180, 0, TAU);
    } else {
      tracePath(g, p, s, x, y);
      if (p.hole > 0) { g.moveTo(x + (C + p.hole) * s, y + C * s); g.arc(x + C * s, y + C * s, p.hole * s, 0, TAU, true); g.closePath(); }
    }
    if (p.fill) {
      if (p.fill2) { const gr = g.createLinearGradient(0, y + p.g0 * s, 0, y + p.g1 * s); gr.addColorStop(0, p.fill); gr.addColorStop(1, p.fill2); g.fillStyle = gr; } else g.fillStyle = p.fill;
      g.fill(p.hole > 0 ? 'evenodd' : 'nonzero');
    }
    if (p.stroke && p.w > 0) { g.strokeStyle = p.stroke; g.lineWidth = Math.max(0.75, p.w * s); g.stroke(); }
  }
  g.restore();
}

/** A round portrait (an image, or a soft disc when it has not loaded) inside the frame at time t: the shop's picture of
 * a frame. size = the frame canvas side in pixels; the portrait fills the window circle. */
export function drawFramed(g, id, img, x, y, size, t, m) {
  const cx = x + size / 2, cy = y + size / 2, r = WINDOW * size;
  g.save();
  g.beginPath(); g.arc(cx, cy, r, 0, TAU); g.closePath();
  g.fillStyle = '#3a2350'; g.fill();
  if (img && img.complete !== false && (img.naturalWidth || img.width)) {
    g.clip();
    const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height, k = (2 * r) / Math.min(iw, ih);
    g.drawImage(img, cx - iw * k / 2, cy - ih * k / 2 + r * 0.12, iw * k, ih * k);
  }
  g.restore();
  drawFrame(g, buildFrame(id, t, m, detailFor(size)), x, y, size);
}
