/* render/mediaMath.js - the pure half of the MEDIA lane (no three.js, so node --test runs it):
 * slot placement against the road (trackside billboards, the sky halo over the start/finish, the
 * screen tunnel), source fitting (cover and contain crops, the spin-safe crop for Loom spirals),
 * the shared canvas upload budget, the url guard and the colour reader for render/spectrum.js.
 *
 * Frames are the road frames of track.frameAt: p on the centreline, t along, n up from the surface,
 * b to the right of travel. A slot is a rectangle: centre C, axes X (right on the picture), Y (up on
 * the picture), Z (the face normal, toward the viewer), size w x h in metres.
 */
import { LOOK } from './look.js';

/** MEDIA lane numbers; render/look.js `LOOK.media` overrides any key (same names). */
export const MEDIA_DEFAULTS = Object.freeze({
  // per tier: canvas edge in px (loomPx for the Loom spirals), how many placeholder programs, their
  // redraw rate (0 = drawn once, the shader moves them: a canvas upload costs about 1 ms of frame time on
  // WebGPU, measured), the redraw rates of real canvas and video sources, the shared upload cap per
  // frame, how far a slot still asks for redraws, tunnel spacing multiplier, halo count share
  tiers: Object.freeze({
    high: Object.freeze({ canvasPx: 256, loomPx: 512, programs: 6, placeholderFps: 0, sourceFps: 10, videoFps: 24, maxUploads: 3, drawM: 900, tunnelStepMul: 1, haloShare: 1, phoneOnly: false }),
    mid: Object.freeze({ canvasPx: 192, loomPx: 384, programs: 6, placeholderFps: 0, sourceFps: 8, videoFps: 20, maxUploads: 2, drawM: 700, tunnelStepMul: 1, haloShare: 1, phoneOnly: false }),
    phone: Object.freeze({ canvasPx: 128, loomPx: 256, programs: 4, placeholderFps: 0, sourceFps: 6, videoFps: 15, maxUploads: 1, drawM: 450, tunnelStepMul: 2, haloShare: 0.67, phoneOnly: true }),
  }),
  aspect: Object.freeze({ portrait: 9 / 16, landscape: 16 / 9, square: 1 }),
  // the frame: a soft glow border, a dark back plate, the credit strip under the picture
  borderM: 1.6, borderGain: 1.6, borderGentle: 0.5, borderHueRadps: 0.18,
  stripShare: 0.085, stripAlpha: 0.62, stripColour: '#120a1e', backColour: '#1a1030', backAlpha: 0.9,
  // the picture: brightness, hue drift of the shared placeholders, slow breathing zoom between redraws
  screenGain: 1.05, screenHueRadps: 0.12, breatheAmp: 0.04, breatheHz: 0.07,
  // glass stalks under the billboards: radius, length down into the void, alpha, fade share
  stalkR: 0.32, stalkDownM: 38, stalkAlpha: 0.4, stalkColour: '#cfe8ff',
  // tunnel glass shell and ribs
  shellAlpha: 0.13, shellColour: '#9fd8ff', shellStepM: 2, shellSegs: 18, ribM: 0.45, ribGain: 1.1, ribGentle: 0.45,
  // clearance against every road stretch (a point clashes inside w/2 + sideM across, underM below to clearM above)
  sideM: 3, clearM: 9, underM: 6, stepOutM: 4, fitTries: 14, checkStepM: 4,
  // Loom spirals: never more than maxBandHz lit bands past a point per second (photosafe, 3 Hz law)
  maxBandHz: 1.2,
});

export function mediaLook() {
  const o = (LOOK && LOOK.media) || {};
  return { ...MEDIA_DEFAULTS, ...o, tiers: { ...MEDIA_DEFAULTS.tiers, ...(o.tiers || {}) } };
}

export function tierOf(name, M = MEDIA_DEFAULTS) {
  return M.tiers[name] || M.tiers.high;
}

const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = (a) => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
export const V3 = Object.freeze({ sub, add, mul, dot, cross, norm });

/** Road samples every stepM metres (plain copies of frameAt, so callers can keep them). */
export function roadSamples(track, stepM = 4) {
  const n = Math.max(8, Math.ceil(track.lengthM / stepM)), out = [];
  for (let i = 0; i < n; i++) {
    const s = (i * track.lengthM) / n, f = track.frameAt(s);
    out.push({ s, p: [...f.p], t: [...f.t], n: [...f.n], b: [...f.b], w: f.w });
  }
  out.stepM = track.lengthM / n;
  return out;
}

const frameCopy = (f) => ({ p: [...f.p], t: [...f.t], n: [...f.n], b: [...f.b], w: f.w });

/** Slot size from a kind and a height: { w, h } in metres. */
export function sizeOf(kind, heightM, M = MEDIA_DEFAULTS) {
  const a = M.aspect[kind] || 1;
  return { w: heightM * a, h: heightM };
}

/** Orthonormal picture axes from a face normal Z and a wanted up: { X, Y, Z }. */
export function axesFrom(Z, upRef) {
  Z = norm(Z);
  let Y = sub(upRef, mul(Z, dot(upRef, Z)));
  if (Math.hypot(Y[0], Y[1], Y[2]) < 1e-6) Y = Math.abs(Z[1]) < 0.9 ? [0, 1, 0] : [0, 0, 1];
  Y = norm(Y);
  const X = norm(cross(Y, Z));
  return { X, Y: norm(cross(Z, X)), Z };
}

/** The rectangle's corners and a grid of interior points (for clearance tests). */
export function rectPoints(slot, k = 3) {
  const out = [];
  for (let i = 0; i <= k; i++) for (let j = 0; j <= k; j++) {
    const u = i / k - 0.5, v = j / k - 0.5;
    out.push(add(add(slot.C, mul(slot.X, u * slot.w)), mul(slot.Y, v * slot.h)));
  }
  return out;
}

/**
 * Does any point sit in the road tube of any sample? skip(f) excludes samples (the tunnel's own road).
 * Returns the first clash { s, l, h } or null.
 */
export function tubeClash(points, samples, M = MEDIA_DEFAULTS, skip = null) {
  const half = (samples.stepM || M.checkStepM) / 2;
  for (const P of points) {
    for (const f of samples) {
      if (skip && skip(f)) continue;
      const r = sub(P, f.p), a = dot(r, f.t);
      if (Math.abs(a) > half) continue;
      const l = dot(r, f.b), h = dot(r, f.n);
      if (Math.abs(l) < f.w / 2 + M.sideM && h > -M.underM && h < M.clearM) return { s: f.s, l, h };
    }
  }
  return null;
}

/**
 * A trackside billboard on a glass stalk. spec: { id, f, side (-1 left, +1 right), kind, heightM, offM
 * (gap from the road edge to the near board edge), liftM (board bottom over the surface), angleDeg (the
 * face turned from straight back down the road toward the road) }. The fitter steps offM out by
 * stepOutM until the board and its stalk clear every road stretch. Returns the slot or null.
 */
export function placeBillboard(spec, track, samples, M = MEDIA_DEFAULTS) {
  const authored = ((spec.f % 1) + 1) % 1 * (track.courseShape?.sourceLengthM ?? track.lengthM);
  const s = track.courseShape?.mapS?.(authored,track) ?? authored;
  const f = frameCopy(track.frameAt(s));
  const side = spec.side < 0 ? -1 : 1;
  const { w, h } = sizeOf(spec.kind, spec.heightM, M);
  const a = (spec.angleDeg ?? 30) * Math.PI / 180;
  const Z = norm(add(mul(f.t, -Math.cos(a)), mul(f.b, -side * Math.sin(a))));
  const ax = axesFrom(Z, f.n);
  for (let k = 0; k <= M.fitTries; k++) {
    const off = (spec.offM ?? 10) + k * M.stepOutM;
    // the near edge sits off metres past the road edge; the turned board reaches w/2 cos(a) across
    const lat = side * (f.w / 2 + off + (w / 2) * Math.abs(dot(ax.X, f.b)));
    const C = add(add(f.p, mul(f.b, lat)), mul(f.n, (spec.liftM ?? 3) + h / 2));
    const slot = { id: spec.id, frame: 'billboard', kind: spec.kind, s, side, w, h, C, ...ax, offM: off, phone: spec.phone !== false, program: spec.program };
    const foot = sub(C, mul(ax.Y, h / 2));
    slot.stalk = { top: foot, bottom: sub(foot, mul(f.n, M.stalkDownM)) };
    const stalkPts = [0.25, 0.5, 0.75, 1].map((u) => add(foot, mul(f.n, -M.stalkDownM * u)));
    if (!tubeClash([...rectPoints(slot), ...stalkPts], samples, M)) return slot;
  }
  return null;
}

/**
 * The sky halo over the start/finish: count screens on a ring of radiusM, upM above the road at f,
 * each facing the ring's axis and tipped down by tiltDeg so a kart below and behind reads them.
 * orbit (radians) turns the whole ring. Returns slots (C, X, Y, Z, w, h).
 */
export function haloSlots(spec, frame, count, orbit = 0, M = MEDIA_DEFAULTS) {
  const f = frame, { w, h } = sizeOf(spec.kind || 'landscape', spec.heightM || 6, M);
  const centre = add(f.p, mul(f.n, spec.upM ?? 30)), tilt = (spec.tiltDeg ?? 35) * Math.PI / 180;
  const out = [];
  for (let i = 0; i < count; i++) {
    const th = orbit + (i / count) * Math.PI * 2;
    const radial = add(mul(f.t, Math.cos(th)), mul(f.b, Math.sin(th)));
    const C = add(centre, mul(radial, spec.radiusM ?? 26));
    const Z = norm(add(mul(radial, -Math.cos(tilt)), mul(f.n, -Math.sin(tilt))));
    out.push({ id: 'halo-' + i, frame: 'halo', kind: spec.kind || 'landscape', s: 0, w, h, C, ...axesFrom(Z, f.n), phone: true, program: i });
  }
  out.centre = centre;
  return out;
}

/** A point on the tunnel's arch profile at angle th (0 = right foot, PI = left foot), and its inward normal. */
export function archAt(f, th, halfW, heightM, sinkM = 0) {
  const c = Math.cos(th), s = Math.sin(th);
  const P = add(add(f.p, mul(f.b, c * halfW)), mul(f.n, s * heightM - sinkM * (1 - s)));
  const inward = norm(add(mul(f.b, -c / halfW), mul(f.n, -s / heightM)));
  return { P, inward };
}

/**
 * The screen tunnel from f = spec.from to spec.to: rings every stepM (times the tier's tunnelStepMul),
 * each with a left wall, right wall and ceiling screen facing in and turned toward oncoming karts by
 * facingDeg. Returns { fromS, toS, halfW, heightM, rings: [s], slots, shell, ribs } where shell and
 * ribs are plain mesh arrays ({ positions, uvs, indices } and { positions, hues, indices }).
 */
export function tunnelLayout(spec, track, M = MEDIA_DEFAULTS, stepMul = 1) {
  const L = track.courseShape?.sourceLengthM ?? track.lengthM;
  const fromS = track.courseShape?.mapS?.(spec.from*L,track) ?? spec.from*L;
  const toS = track.courseShape?.mapS?.(spec.to*L,track) ?? spec.to*L;
  const f0 = track.frameAt(fromS), halfW = f0.w / 2 + (spec.padM ?? 3.5), H = spec.heightM ?? 11;
  const step = (spec.stepM ?? 9) * stepMul, rings = [], slots = [];
  const face = (spec.facingDeg ?? 28) * Math.PI / 180, wallTh = (spec.wallDeg ?? 30) * Math.PI / 180;
  for (let s = fromS + step / 2; s < toS - step / 4; s += step) rings.push(s);
  const inset = 0.5;
  rings.forEach((s, ri) => {
    const f = frameCopy(track.frameAt(s)), hw = f.w / 2 + (spec.padM ?? 3.5);
    const places = [];
    if (spec.wall !== false) places.push(['wall-r', wallTh, spec.wallKind || 'landscape', spec.wallHeightM ?? 3.6], ['wall-l', Math.PI - wallTh, spec.wallKind || 'landscape', spec.wallHeightM ?? 3.6]);
    if (spec.ceiling !== false) places.push(['ceil', Math.PI / 2, spec.ceilKind || 'landscape', spec.ceilHeightM ?? 4.2]);
    places.forEach(([tag, th, kind, hM], k) => {
      const { P, inward } = archAt(f, th, hw, H);
      const { w, h } = sizeOf(kind, hM, M);
      const Z = norm(add(mul(inward, Math.cos(face)), mul(f.t, -Math.sin(face))));
      const up = tag === 'ceil' ? f.t : f.n;
      slots.push({ id: 'tunnel-' + ri + '-' + tag, frame: 'tunnel', kind, s, w, h, C: add(P, mul(inward, inset + 0.3)), ...axesFrom(Z, up), phone: true, program: ri * 3 + k });
    });
  });
  // glass shell: an arch swept along s, uv = (arch share, metres along)
  const segs = M.shellSegs, nS = Math.max(2, Math.ceil((toS - fromS) / M.shellStepM) + 1);
  const pos = [], uvs = [], idx = [];
  for (let i = 0; i < nS; i++) {
    const s = fromS + (toS - fromS) * i / (nS - 1), f = track.frameAt(s), hw = f.w / 2 + (spec.padM ?? 3.5);
    for (let j = 0; j <= segs; j++) {
      const { P } = archAt(f, (j / segs) * Math.PI, hw, H, 1);
      pos.push(P[0], P[1], P[2]); uvs.push(j / segs, s - fromS);
    }
  }
  for (let i = 0; i < nS - 1; i++) for (let j = 0; j < segs; j++) {
    const a = i * (segs + 1) + j, b = a + segs + 1;
    idx.push(a, b, a + 1, a + 1, b, b + 1);
  }
  // ribs: a thin glowing arch band at every ring and halfway between, each with its own hue slot
  const rp = [], rh = [], ri2 = [], ribS = [];
  rings.forEach((s, i) => { ribS.push(s - step / 2); if (i === rings.length - 1) ribS.push(s + step / 2); });
  ribS.forEach((s, r) => {
    for (const e of [-M.ribM / 2, M.ribM / 2]) {
      const f = track.frameAt(s + e), hw = f.w / 2 + (spec.padM ?? 3.5) - 0.15;
      for (let j = 0; j <= segs; j++) {
        const { P } = archAt(f, (j / segs) * Math.PI, hw, H - 0.15, 1);
        rp.push(P[0], P[1], P[2]); rh.push(r);
      }
    }
    const base = r * 2 * (segs + 1);
    for (let j = 0; j < segs; j++) {
      const a = base + j, b = a + segs + 1;
      ri2.push(a, b, a + 1, a + 1, b, b + 1);
    }
  });
  return { fromS, toS, halfW, heightM: H, rings, slots, ribCount: ribS.length,
    shell: { positions: pos, uvs, indices: idx }, ribs: { positions: rp, hues: rh, indices: ri2 } };
}

/**
 * Crop rectangles in texture space (0..1, y up) for a source of aspect srcA shown on a slot of aspect
 * dstA: { cx, cy, sx, sy } = centre and full extents of the window sampled.
 * cover fills the slot and crops the long side; contain shows all of it (sx or sy > 1 leaves bars).
 */
export function fitCover(srcA, dstA) {
  if (!(srcA > 0) || !(dstA > 0)) return { cx: 0.5, cy: 0.5, sx: 1, sy: 1 };
  return dstA < srcA ? { cx: 0.5, cy: 0.5, sx: dstA / srcA, sy: 1 } : { cx: 0.5, cy: 0.5, sx: 1, sy: srcA / dstA };
}
export function fitContain(srcA, dstA) {
  if (!(srcA > 0) || !(dstA > 0)) return { cx: 0.5, cy: 0.5, sx: 1, sy: 1 };
  return dstA > srcA ? { cx: 0.5, cy: 0.5, sx: dstA / srcA, sy: 1 } : { cx: 0.5, cy: 0.5, sx: 1, sy: srcA / dstA };
}
/**
 * The cover crop of a square canvas that stays inside it at every rotation (a Loom spiral turns in the
 * shader): the window's half diagonal never passes 0.5.
 */
export function spinSafeCrop(dstA) {
  const c = fitCover(1, dstA), k = 1 / Math.hypot(c.sx, c.sy);
  return { cx: 0.5, cy: 0.5, sx: c.sx * k, sy: c.sy * k };
}

/** Spin rate (rad/s) for a Loom spiral of `arms` lit bands: never more than maxHz bands past a point. */
export function safeSpin(arms, wantRadps, maxHz = MEDIA_DEFAULTS.maxBandHz) {
  const cap = (maxHz * Math.PI * 2) / Math.max(1, arms);
  return Math.sign(wantRadps || 1) * Math.min(Math.abs(wantRadps || 0), cap);
}

/**
 * The shared upload budget. Each source has { due (seconds), every (seconds), near (metres to the eye) };
 * pick(now, max, drawM) returns up to max indices: overdue sources within drawM, most overdue first,
 * nearest first on a tie. The caller redraws and uploads those and calls done(i, now).
 */
export function createUploadBudget() {
  const list = [];
  return {
    list,
    add(src) { list.push({ due: 0, every: 0.1, near: 0, on: true, ...src }); return list.length - 1; },
    pick(now, max, drawM = Infinity, out = []) {
      out.length = 0;
      for (let i = 0; i < list.length; i++) {
        const x = list[i];
        if (!x.on || x.near > drawM || now < x.due) continue;
        out.push(i);
      }
      out.sort((a, b) => (list[a].due - list[b].due) || (list[a].near - list[b].near));
      if (out.length > max) out.length = Math.max(0, max);
      return out;
    },
    done(i, now) { const x = list[i]; x.due = Math.max(x.due + x.every, now + x.every * 0.5); },
  };
}

/**
 * Url guard for setSource: only what is already on this device or same origin. A remote media url
 * (Scrolller or anything else) is refused: the host hands a local copy (https://ccp.assets/ on the
 * desktop, the same-origin relay on the web), so nothing uncached is ever streamed from here.
 */
export function urlAllowed(url, pageOrigin = '') {
  if (typeof url !== 'string' || !url) return false;
  if (url.startsWith('blob:') || url.startsWith('data:image/') || url.startsWith('data:video/')) return true;
  let u;
  try { u = new URL(url, pageOrigin || 'http://invalid.local/'); } catch { return false; }
  if (u.protocol === 'https:' && u.host === 'ccp.assets') return true;
  if (pageOrigin && u.origin === pageOrigin) return true;
  return false;
}

/** Credit text for a slot: 'scrolller' or { source: 'scrolller' } -> 'via Scrolller'; a plain string passes; else ''. */
export function creditText(credit) {
  if (!credit) return '';
  const src = typeof credit === 'string' ? credit : credit.source || credit.text || '';
  if (/^scrolller$/i.test(src)) return 'via Scrolller';
  return typeof credit === 'object' && credit.text ? String(credit.text) : /^via /.test(src) ? src : '';
}

/** HSV (h in turns) to [r, g, b] 0..1. */
export function hsv(h, s = 1, v = 1) {
  h = ((h % 1) + 1) % 1;
  const i = Math.floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
  return [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][i % 6];
}

/**
 * Any colour render/spectrum.js may hand back -> [r, g, b] 0..1: '#rrggbb' / '#rgb', [r, g, b] (0..1
 * or 0..255), { r, g, b }, { h, s, v } / { hue }, or a bare hue (0..1 turns, else degrees).
 */
export function colourOf(x, fallback = [1, 0.3, 0.7]) {
  if (x == null) return fallback;
  if (typeof x === 'string') {
    let m = x.trim();
    if (m[0] === '#') m = m.slice(1);
    if (m.length === 3) m = m.split('').map((c) => c + c).join('');
    if (m.length !== 6 || !/^[0-9a-f]{6}$/i.test(m)) return fallback;
    const n = parseInt(m, 16);
    return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
  }
  if (typeof x === 'number' && Number.isFinite(x)) return hsv(x <= 1 ? x : x / 360);
  if (Array.isArray(x) && x.length >= 3) { const k = x.some((v) => v > 1) ? 255 : 1; return [x[0] / k, x[1] / k, x[2] / k]; }
  if (typeof x === 'object') {
    if ('r' in x) { const k = x.r > 1 || x.g > 1 || x.b > 1 ? 255 : 1; return [x.r / k, x.g / k, x.b / k]; }
    if ('h' in x || 'hue' in x) { const h = x.h ?? x.hue; return hsv(h <= 1 ? h : h / 360, x.s ?? 1, x.v ?? 1); }
    if ('hex' in x) return colourOf(x.hex, fallback);
  }
  return fallback;
}

/** The full-spectrum fallback until render/spectrum.js exists: pink and violet lead, the rest all show. */
export const FALLBACK_SPECTRUM = Object.freeze(['#ff3fa4', '#c43cff', '#7a4dff', '#00e5ff', '#19d3b0', '#2f7bff', '#9dff3a', '#ffd23f', '#ff8a1f', '#ff3b3b', '#ff5fd2', '#8f5bff']);

/** Slot colour i of n: from spectrum.propColour(i, section) when present, else the fallback ring. */
export function slotColour(i, spectrum = null, section = 'dawn') {
  try {
    if (spectrum && typeof spectrum.propColour === 'function') return colourOf(spectrum.propColour(i, section));
  } catch { /* fallback below */ }
  return colourOf(FALLBACK_SPECTRUM[((i % FALLBACK_SPECTRUM.length) + FALLBACK_SPECTRUM.length) % FALLBACK_SPECTRUM.length]);
}

/** The slots a tier shows: phone keeps billboards flagged phone, a share of the halo, every tunnel slot it built. */
export function tierSlots(slots, tier, M = MEDIA_DEFAULTS) {
  const T = typeof tier === 'string' ? tierOf(tier, M) : tier;
  return slots.filter((s) => !(T.phoneOnly && s.frame === 'billboard' && !s.phone));
}

/**
 * One halo slot's pose for the orbit, written into sl.C, sl.X, sl.Y, sl.Z in place (no allocation: the halo turns
 * every frame). Same numbers as haloSlots(spec, frame, count, orbit)[i]; sl must come from haloSlots.
 */
export function haloPoseInto(spec, frame, count, orbit, i, sl) {
  const f = frame, up = spec.upM ?? 30, R = spec.radiusM ?? 26, tilt = (spec.tiltDeg ?? 35) * Math.PI / 180;
  const th = orbit + (i / count) * Math.PI * 2, ct = Math.cos(th), st = Math.sin(th), cl = Math.cos(tilt), sn = Math.sin(tilt);
  const C = sl.C, Z = sl.Z, Y = sl.Y, X = sl.X;
  for (let k = 0; k < 3; k++) {
    const radial = f.t[k] * ct + f.b[k] * st;
    C[k] = f.p[k] + f.n[k] * up + radial * R;
    Z[k] = -radial * cl - f.n[k] * sn;
  }
  let l = Math.hypot(Z[0], Z[1], Z[2]) || 1;
  Z[0] /= l; Z[1] /= l; Z[2] /= l;
  // axesFrom(Z, f.n): Y = n minus its Z part, X = Y x Z, then Y = Z x X
  const d = f.n[0] * Z[0] + f.n[1] * Z[1] + f.n[2] * Z[2];
  let y0 = f.n[0] - Z[0] * d, y1 = f.n[1] - Z[1] * d, y2 = f.n[2] - Z[2] * d;
  if (Math.hypot(y0, y1, y2) < 1e-6) { if (Math.abs(Z[1]) < 0.9) { y0 = 0; y1 = 1; y2 = 0; } else { y0 = 0; y1 = 0; y2 = 1; } }
  l = Math.hypot(y0, y1, y2) || 1; y0 /= l; y1 /= l; y2 /= l;
  let x0 = y1 * Z[2] - y2 * Z[1], x1 = y2 * Z[0] - y0 * Z[2], x2 = y0 * Z[1] - y1 * Z[0];
  l = Math.hypot(x0, x1, x2) || 1; X[0] = x0 / l; X[1] = x1 / l; X[2] = x2 / l;
  y0 = Z[1] * X[2] - Z[2] * X[1]; y1 = Z[2] * X[0] - Z[0] * X[2]; y2 = Z[0] * X[1] - Z[1] * X[0];
  l = Math.hypot(y0, y1, y2) || 1; Y[0] = y0 / l; Y[1] = y1 / l; Y[2] = y2 / l;
  return sl;
}
