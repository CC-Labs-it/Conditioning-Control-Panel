/* ui/unlocks/extras/outside-show.js - the Go Outside ending show (handoff-1109/TC-GO-OUTSIDE-ENDING-BRIEF.md, round 2 in
 * TC-GO-OUTSIDE-OFFICE.md, lane S2). It runs as the ending row's ending(ctx), after the white fade.
 *
 *   1. The white fade clears onto a LIVE blocky meadow (lane W's createMeadow, ./outside-meadow.js). The player can look
 *      around and walk for LIVE_MS. A small paper note says how.
 *   2. The controls are cut and the camera settles (lockControls + autopilot). Then the pull back: a rendered clip
 *      (assets/outside/office-pull.mp4) whose frame 0 is the monitor's screen, pulling back to an after-hours cubicle.
 *      The live meadow canvas stays mapped onto the screen quad of the frame on show (office-track.json, CSS matrix3d),
 *      so the meadow keeps playing on that monitor. Both are cover-fitted the same way; a tall phone eases out to keep
 *      the whole screen in view.
 *   3. The hold: office-hold.jpg (the clip's last frame) with the canvas on the last quad, a static crackle every 3-7 s
 *      (the screen dips, the whole image flickers faintly), the end card low over the dark office. No way out.
 *
 * Sound: every other page sound is off for the whole show (ctx.audio.solo); only its birds, bed, room tone and crackle play.
 * Input: during the live part a press belongs to the meadow, never a skip. The Skip button or Enter jumps to the hold
 * (never past it). Escape stays the game's pause; a panic suspend ends the show at once. Gentle / reduced motion: no live
 * scene, no zoom, no flicker: meadow.jpg cross-fades to office-hold.jpg with the still in the screen.
 * Missing pieces never break it: createMeadow rejects -> a canvas painting meadow.jpg -> a flat painted sky; the clip
 * fails -> a cross-fade to the hold still; no track -> a centred 16:9 screen; no still -> a plain dark crayon room. */

import { FONT, h } from './kit.js';
import { LITE } from '../../perf.js';

export const ASSETS = Object.freeze({
  atlas: 'assets/outside/meadow-atlas.png',
  atlasJson: 'assets/outside/meadow-atlas.json',
  still: 'assets/outside/meadow.jpg',
  video: 'assets/outside/office-pull.mp4',
  hold: 'assets/outside/office-hold.jpg',
  track: 'assets/outside/office-track.json',
  /* The lo-fi radio's own tracks (CC Labs owned). Machine-local in some builds: missing = birds alone. */
  beds: ['assets/lofi/audio/breathe-in.mp3', 'assets/lofi/audio/idle-cycle.mp3', 'assets/lofi/audio/dose.mp3'],
});

/** The clip's frame (and the hold still's), and the plain fallback room's. */
export const FRAME = Object.freeze([1280, 720]);
export const ROOM_SIZE = Object.freeze([1200, 896]);
export const SKY = '#9fd3f5';

/* Beat lengths in game milliseconds from the start of the show (the white fade before it is the engine's). */
export const BEATS = Object.freeze({
  veilMs: 1200,        // the white clears onto the meadow
  liveMs: 30000,        // controls on
  settleMs: 900,       // controls cut, camera eases to the calm view
  pullMs: 5000,        // the planned pull back (the clip's own length decides; the track's frame count plans it)
  pullCapMs: 4000,     // a stalled clip: the hold starts anyway this long after its planned end
  videoWaitMs: 1500,   // the clip not ready at the pull: wait this long (the meadow goes on), then the cross-fade
  cardLeadMs: 900,     // hold -> first card line
  lineGapMs: 900,      // between card lines
  tailMs: 1700,        // let the last office line land before the poster prints
  stillMs: 3600,       // reduced: the meadow still holds this long
  fadeMs: 1600,        // reduced and fallback: the cross-fade to the hold
  crackleMinMs: 3000,
  crackleMaxMs: 7000,
});
export const MEADOW_WAIT_MS = 3500;   // createMeadow slower than this -> the stub; a late scene is stopped unused
export const LOAD_WAIT_MS = 3000;     // the track or the hold still slower than this -> the fallbacks

/** The card lines after a hold that starts at `holdAt`: result, credits, closing, then the show ends. */
export function holdBeats(holdAt, credits = 0) {
  const lines = [{ at: holdAt + BEATS.cardLeadMs, kind: 'result' }];
  for (let i = 0; i < credits; i++) lines.push({ at: holdAt + BEATS.cardLeadMs + (i + 1) * BEATS.lineGapMs, kind: 'credit', i });
  const closingAt = holdAt + BEATS.cardLeadMs + (credits + 1) * BEATS.lineGapMs + 400;
  lines.push({ at: closingAt, kind: 'closing' });
  return { holdAt, lines, closingAt, finishAt: closingAt + BEATS.tailMs };
}

/** The whole plan. Reduced: no live part, no settle, the "pull" is a cross-fade. */
export function outsideTimeline({ reduced = false, credits = 0, pullMs = BEATS.pullMs, liveMs = BEATS.liveMs } = {}) {
  if (reduced) {
    const pullAt = BEATS.veilMs + BEATS.stillMs;
    const holdAt = pullAt + BEATS.fadeMs;
    return { reduced: true, liveAt: null, lockAt: null, pullAt, pullEnd: holdAt, ...holdBeats(holdAt, credits) };
  }
  const liveAt = Math.round(BEATS.veilMs / 2);
  const lockAt = liveAt + liveMs;
  const pullAt = lockAt + BEATS.settleMs;
  const holdAt = pullAt + Math.max(1, pullMs);
  return { reduced: false, liveAt, lockAt, pullAt, pullEnd: holdAt, ...holdBeats(holdAt, credits) };
}

const creditsOf = (tl) => tl.lines.filter((l) => l.kind === 'credit').length;
/** The hold starts now (the clip ended early or late): the card is planned from here. */
export const holdFrom = (tl, t) => ({ ...tl, pullEnd: t, ...holdBeats(t, creditsOf(tl)) });

/** A skip at time t: everything before the hold is dropped and the hold starts now. A skip at or after the hold changes nothing. */
export function skipTo(tl, t) {
  if (t >= tl.holdAt) return tl;
  return { ...holdFrom(tl, t), lockAt: tl.lockAt == null ? null : Math.min(tl.lockAt, t), pullAt: Math.min(tl.pullAt, t), skipped: true };
}

/** Which phase a moment is in. */
export function phaseAt(tl, t) {
  if (t >= tl.finishAt) return 'done';
  if (t >= tl.holdAt) return 'hold';
  if (t >= tl.pullAt) return 'pull';
  if (tl.lockAt != null && t >= tl.lockAt) return 'settle';
  if (tl.liveAt != null && t >= tl.liveAt) return 'live';
  return 'veil';
}

/* ---- maths (pure) ---- */

const goodQuad = (q) => Array.isArray(q) && q.length === 4 && q.every((p) => Array.isArray(p) && p.length === 2 && p.every(Number.isFinite));
const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const clamp01 = (v) => Math.max(0, Math.min(1, v));
const lerp = (a, b, t) => a + (b - a) * t;

/** office-track.json checked: { fps, size, frames } or null. */
export function readTrack(json) {
  if (!json || json.version !== 1 || !(json.fps > 0) || !Array.isArray(json.frames) || json.frames.length < 2) return null;
  const size = Array.isArray(json.size) && json.size.length === 2 && json.size.every((v) => v > 0) ? json.size : FRAME;
  if (!json.frames.every(goodQuad)) return null;
  return { fps: json.fps, size: [size[0], size[1]], frames: json.frames.map((q) => q.map(([x, y]) => [x, y])) };
}

/** No track: a straight eased zoom-out of the 16:9 frame onto a centred screen, the last half second still. */
export function fallbackTrack({ frames = 150, fps = 30, hold = 15, end = 0.42, size = FRAME } = {}) {
  const [w, hh] = size;
  const out = [];
  for (let i = 0; i < frames; i++) {
    const z = lerp(1, end, ease(clamp01(i / Math.max(1, frames - 1 - hold))));
    const x0 = (w * (1 - z)) / 2, y0 = (hh * (1 - z)) / 2;
    out.push([[x0, y0], [w - x0, y0], [w - x0, hh - y0], [x0, hh - y0]]);
  }
  return { fps, size: [w, hh], frames: out, fallback: true };
}

/** The frame on show at `seconds` into the clip (a frame shows from i/fps until the next one). */
export function frameAt(track, seconds) {
  const n = track.frames.length;
  return Math.max(0, Math.min(n - 1, Math.floor((Number.isFinite(seconds) ? seconds : 0) * track.fps + 1e-3)));
}
export const trackMs = (track) => Math.round((track.frames.length / track.fps) * 1000);

/** The picture covers the view, centred: { s, ox, oy } maps picture pixels to the view. */
export function coverFit(vw, vh, size = FRAME) {
  const [w, hh] = size, s = Math.max(vw / w, vh / hh);
  return { s, ox: (vw - w * s) / 2, oy: (vh - hh * s) / 2 };
}

/** The hold's fit: the cover fit when the whole screen (with a margin) shows in it, else the picture shrinks just enough
 *  (never below contain) and centres on the screen, edge to edge where it can. A tall phone gets dark above and below. */
export function holdFit(vw, vh, quad, size = FRAME) {
  const c = coverFit(vw, vh, size), [w, hh] = size;
  const m = Math.max(12, Math.min(vw, vh) * 0.04);
  const inside = (f) => quad.every(([x, y]) => {
    const X = f.ox + x * f.s, Y = f.oy + y * f.s;
    return X >= m - 1e-6 && X <= vw - m + 1e-6 && Y >= m - 1e-6 && Y <= vh - m + 1e-6;
  });
  if (inside(c)) return c;
  const xs = quad.map((p) => p[0]), ys = quad.map((p) => p[1]);
  const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
  const s = Math.max(Math.min(vw / w, vh / hh), Math.min(c.s, (vw - 2 * m) / Math.max(1, x1 - x0), (vh - 2 * m) / Math.max(1, y1 - y0)));
  const place = (v, len, mid) => (len >= v ? Math.min(0, Math.max(v - len, v / 2 - mid * s)) : (v - len) / 2);
  return { s, ox: place(vw, w * s, (x0 + x1) / 2), oy: place(vh, hh * s, (y0 + y1) / 2) };
}

/** The quad pushed out by `px` view pixels from its middle: the live canvas overlaps the rendered screen's edge, so no
 *  sub-pixel seam of the screen under it shows round the picture. */
export function grow(quad, px) {
  const cx = quad.reduce((t, p) => t + p[0], 0) / 4, cy = quad.reduce((t, p) => t + p[1], 0) / 4;
  return quad.map(([x, y]) => { const d = Math.hypot(x - cx, y - cy) || 1; return [x + ((x - cx) / d) * px, y + ((y - cy) / d) * px]; });
}
export const OVERSCAN_PX = 1.5, MOTION_CAP_PX = 6;

export const fitAt = (a, b, p) => ({ s: lerp(a.s, b.s, p), ox: lerp(a.ox, b.ox, p), oy: lerp(a.oy, b.oy, p) });
export const mapQuad = (quad, f) => quad.map(([x, y]) => [f.ox + x * f.s, f.oy + y * f.s]);

/** CSS matrix3d (column major, transform-origin 0 0) that maps a w x h box onto the quad tl, tr, br, bl. */
export function homography(w, h, quad) {
  const [[x0, y0], [x1, y1], [x2, y2], [x3, y3]] = quad;
  const dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
  const dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
  let a, b, c, d, e, f, g, k;
  if (Math.abs(dx3) < 1e-9 && Math.abs(dy3) < 1e-9) {
    a = x1 - x0; b = x3 - x0; c = x0; d = y1 - y0; e = y3 - y0; f = y0; g = 0; k = 0;
  } else {
    const det = dx1 * dy2 - dx2 * dy1 || 1e-9;
    g = (dx3 * dy2 - dx2 * dy3) / det;
    k = (dx1 * dy3 - dx3 * dy1) / det;
    a = x1 - x0 + g * x1; b = x3 - x0 + k * x3; c = x0;
    d = y1 - y0 + g * y1; e = y3 - y0 + k * y3; f = y0;
  }
  a /= w; d /= w; g /= w; b /= h; e /= h; k /= h;
  return [a, d, 0, g, b, e, 0, k, 0, 0, 1, 0, c, f, 0, 1];
}

/** Where a matrix3d sends the box point (x, y): the check the tests use. */
export function applyMatrix(m, x, y) {
  const X = m[0] * x + m[4] * y + m[12], Y = m[1] * x + m[5] * y + m[13], W = m[3] * x + m[7] * y + m[15];
  return [X / W, Y / W];
}
export const matrixCss = (m) => `matrix3d(${m.map((v) => +v.toFixed(9)).join(',')})`;

/** Map a cropped camera view through the same screen plane as the full cover image. */
export function cropProjection(full, crop) {
  return homography(crop.width, crop.height, [
    applyMatrix(full, crop.x, crop.y), applyMatrix(full, crop.x + crop.width, crop.y),
    applyMatrix(full, crop.x + crop.width, crop.y + crop.height), applyMatrix(full, crop.x, crop.y + crop.height),
  ]);
}

/** One frame of the pull: the fit eases from cover to the hold's fit over the clip, the canvas sits on that frame's quad
 *  mapped through it, the glass fades up over the first half. Frame 0: the canvas is the cover rectangle of the view. */
export function pullLayout(track, i, vw, vh) {
  const n = track.frames.length, last = track.frames[n - 1];
  const p = clamp01(i / Math.max(1, n - 1));
  const fit = fitAt(coverFit(vw, vh, track.size), holdFit(vw, vh, last, track.size), ease(p));
  const at = Math.max(0, Math.min(n - 1, i)), quad = mapQuad(track.frames[at], fit);
  /* How far a corner moves to the next frame, in view px: the overscan grows by it while the camera moves, so a frame the
   * compositor shows a beat apart from its quad never opens a gap onto the rendered screen. Capped; 0 in the hold. */
  const next = mapQuad(track.frames[Math.min(n - 1, at + 1)], fit);
  const motion = Math.min(MOTION_CAP_PX, Math.max(...quad.map((q, k) => Math.hypot(next[k][0] - q[0], next[k][1] - q[1]))));
  return { fit, quad, glass: ease(clamp01(p / 0.5)), p, motion };
}

/** Milliseconds to the next crackle, 3 to 7 s. */
export const crackleGap = (r = Math.random()) => Math.round(BEATS.crackleMinMs + clamp01(r) * (BEATS.crackleMaxMs - BEATS.crackleMinMs));

/* ---- pictures ---- */

function loadImage(doc, url) {
  return new Promise((res) => {
    const img = doc.createElement('img');
    img.decoding = 'async';
    img.onload = () => res(img.naturalWidth ? img : null);
    img.onerror = () => res(null);
    img.src = url;
  });
}

/** The painted sky the meadow falls back to when meadow.jpg is gone too: blocky ground, a square sun, flat clouds. */
function paintSky(g, w, h, t = 0) {
  const sky = g.createLinearGradient(0, 0, 0, h);
  sky.addColorStop(0, '#6fb8f0'); sky.addColorStop(0.62, '#bfe4fb'); sky.addColorStop(1, '#d9f0fd');
  g.fillStyle = sky; g.fillRect(0, 0, w, h);
  const b = Math.max(8, Math.round(Math.min(w, h) / 22));
  g.fillStyle = '#fff7c2'; g.fillRect(Math.round(w * 0.74), Math.round(h * 0.1), b * 2, b * 2);
  g.fillStyle = 'rgba(255,255,255,.92)';
  for (let i = 0; i < 4; i++) {
    const x = ((i * 0.31 + t * 0.004) % 1.2 - 0.1) * w, y = h * (0.12 + 0.07 * (i % 2));
    g.fillRect(Math.round(x), Math.round(y), b * (4 + (i % 3)), b);
  }
  const ground = Math.round(h * 0.62);
  for (let x = 0; x < w; x += b) {
    const bump = Math.round(Math.sin(x / (b * 7)) * 1.4) * b;
    g.fillStyle = '#5fae47'; g.fillRect(x, ground + bump, b, b);
    g.fillStyle = '#4c9a3a'; g.fillRect(x, ground + bump + b, b, h);
    g.fillStyle = '#7a5532'; g.fillRect(x, ground + bump + b * 3, b, h);
  }
  for (const tx of [0.14, 0.3, 0.83]) {
    const x = Math.round(w * tx / b) * b;
    g.fillStyle = '#6b4a2b'; g.fillRect(x, ground - b * 3, b, b * 3);
    g.fillStyle = '#3f8a32'; g.fillRect(x - b * 2, ground - b * 6, b * 5, b * 3);
  }
}

/** The stand-in for lane W's scene: the same interface, a canvas that paints meadow.jpg (drifting slowly, the mouse
 *  looks a little) or the painted sky. Used when createMeadow is missing, rejects or is too slow. */
export function stubMeadow(doc, host, { still = null, frame = null } = {}) {
  const canvas = doc.createElement('canvas');
  canvas.dataset.meadow = 'stub';
  host.appendChild(canvas);
  const g = canvas.getContext('2d');
  let raf = 0, off = null, clock = 0, t0 = 0, locked = false, look = 0, lookTo = 0, drift = false;
  const win = doc.defaultView;
  const onMove = (e) => { if (!locked) lookTo = (e.clientX / Math.max(1, win.innerWidth) - 0.5) * 2; };
  const size = () => {
    const r = { width: canvas.clientWidth, height: canvas.clientHeight };   // the layout box: the transform shrinks the rect
    const dpr = Math.min(win.devicePixelRatio || 1, 1.5);
    canvas.width = Math.max(2, Math.round((r.width || win.innerWidth) * dpr));
    canvas.height = Math.max(2, Math.round((r.height || win.innerHeight) * dpr));
  };
  const draw = (now) => {
    const t = (now - (t0 ||= now)) / 1000, w = canvas.width, hh = canvas.height;
    look += (lookTo - look) * 0.06;
    if (still) {
      const s = Math.max(w / still.naturalWidth, hh / still.naturalHeight) * 1.12;
      const dw = still.naturalWidth * s, dh = still.naturalHeight * s;
      const pan = (drift ? Math.sin(t * 0.15) * 0.5 : look) * (dw - w) / 2;
      g.drawImage(still, (w - dw) / 2 - pan, (hh - dh) / 2 + Math.sin(t * 1.7) * (locked ? 0 : 2), dw, dh);
    } else paintSky(g, w, hh, t * 60);
    if (!frame) raf = win.requestAnimationFrame(draw);
  };
  return {
    canvas,
    /* On the game clock when the show passes ctx.frame (freezes on pause), else its own rAF. */
    start() { size(); win.addEventListener('pointermove', onMove); if (frame) off = frame((sec) => draw(clock += sec * 1000)); else raf = win.requestAnimationFrame(draw); },
    lockControls() { locked = true; lookTo = 0; },
    autopilot(on) { drift = !!on; },
    resize: size,
    stop() { off?.(); win.cancelAnimationFrame(raf); win.removeEventListener('pointermove', onMove); canvas.remove(); },
    still() { try { return canvas.toDataURL('image/jpeg', 0.8); } catch { return null; } },
  };
}

/** A plain dark room in crayon strokes, with the screen hole cut out: the fallback when there is no drawing. */
export function plainRoom(doc) {
  const [W, H] = ROOM_SIZE;
  const c = doc.createElement('canvas');
  c.width = W; c.height = H;
  const g = c.getContext('2d');
  let seed = 7;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const crayon = (fn, colour, width, passes = 3) => {
    for (let i = 0; i < passes; i++) {
      g.save(); g.translate((rnd() - 0.5) * 3, (rnd() - 0.5) * 3);
      g.strokeStyle = colour; g.lineWidth = width * (0.8 + rnd() * 0.4); g.globalAlpha = 0.55 + rnd() * 0.3;
      g.lineCap = 'round'; g.lineJoin = 'round'; g.beginPath(); fn(); g.stroke(); g.restore();
    }
  };
  g.fillStyle = '#26222e'; g.fillRect(0, 0, W, H);
  g.fillStyle = '#2f2a38'; g.fillRect(0, 610, W, H - 610);
  crayon(() => { g.moveTo(40, 612); g.lineTo(1160, 606); }, '#4a4357', 8);
  const quad = [[420, 236], [780, 236], [780, 466], [420, 466]];
  g.fillStyle = '#35303f'; g.beginPath(); g.roundRect(388, 204, 424, 294, 34); g.fill();
  crayon(() => g.roundRect(388, 204, 424, 294, 34), '#4f4860', 7);
  g.fillStyle = '#35303f'; g.fillRect(560, 498, 80, 70); g.fillRect(480, 568, 240, 40);
  crayon(() => { g.moveTo(480, 568); g.lineTo(720, 568); }, '#4f4860', 6);
  g.fillStyle = '#1e1b25';
  g.beginPath(); g.arc(940, 430, 58, 0, Math.PI * 2); g.fill();
  g.beginPath(); g.roundRect(860, 486, 170, 150, [70, 70, 12, 12]); g.fill();
  crayon(() => { g.moveTo(170, 600); g.lineTo(190, 380); g.lineTo(250, 340); }, '#3d3748', 9);
  crayon(() => { g.moveTo(215, 330); g.lineTo(285, 330); g.lineTo(270, 300); g.lineTo(230, 300); g.closePath(); }, '#3d3748', 7);
  g.save(); g.beginPath(); g.roundRect(420, 236, 360, 230, 14); g.clip(); g.clearRect(0, 0, W, H); g.restore();
  crayon(() => g.roundRect(418, 234, 364, 234, 15), '#151219', 5, 2);
  return { el: c, quad };
}

/* ---- sound (all synthesised, quiet) ---- */

function noiseBuffer(ac, seconds, brown = false) {
  const n = Math.floor(ac.sampleRate * seconds), buf = ac.createBuffer(1, n, ac.sampleRate), d = buf.getChannelData(0);
  let last = 0;
  for (let i = 0; i < n; i++) {
    const w = Math.random() * 2 - 1;
    if (brown) { last = (last + 0.02 * w) / 1.02; d[i] = last * 3.2; } else d[i] = w;
  }
  return buf;
}

/* A few birds: each a voice with its own pitch, pan and habit, singing short phrases of chirps. */
const SONGS = [
  (b) => Array.from({ length: 3 + Math.floor(Math.random() * 4) }, (_, i) => ({ dt: i * 0.13, f0: 4300 * b, f1: 2700 * b, dur: 0.075 })),
  (b) => Array.from({ length: 8 + Math.floor(Math.random() * 7) }, (_, i) => ({ dt: i * 0.045, f0: (i % 2 ? 3700 : 3100) * b, f1: (i % 2 ? 3300 : 3500) * b, dur: 0.03 })),
  (b) => [{ dt: 0, f0: 2300 * b, f1: 2700 * b, dur: 0.24 }, { dt: 0.32, f0: 2900 * b, f1: 1950 * b, dur: 0.3 }],
  (b) => [{ dt: 0, f0: 5200 * b, f1: 5600 * b, dur: 0.05 }, { dt: 0.09, f0: 5300 * b, f1: 3900 * b, dur: 0.12 }, { dt: 0.4, f0: 5200 * b, f1: 3800 * b, dur: 0.12 }],
];

function startBirds(io) {
  const ac = io.ac, bus = ac.createGain(), hp = ac.createBiquadFilter(), lp = ac.createBiquadFilter();
  hp.type = 'highpass'; hp.frequency.value = 40; lp.type = 'lowpass'; lp.frequency.value = 15000;
  bus.gain.value = 0;
  bus.connect(hp).connect(lp).connect(io.node);
  bus.gain.setTargetAtTime(1, ac.currentTime, 0.6);
  const voices = [
    { pitch: 1, pan: -0.55, songs: [0, 2], next: ac.currentTime + 0.6, gap: [1.4, 3.6], gain: 0.22 },
    { pitch: 0.82, pan: 0.6, songs: [1, 2], next: ac.currentTime + 1.7, gap: [2.2, 5], gain: 0.16 },
    { pitch: 1.15, pan: 0.15, songs: [3, 0], next: ac.currentTime + 2.9, gap: [3, 6.5], gain: 0.12 },
  ];
  const chirp = (t, n, v) => {
    const o = ac.createOscillator(), g = ac.createGain(), p = ac.createStereoPanner ? ac.createStereoPanner() : null;
    o.type = 'sine';
    o.frequency.setValueAtTime(n.f0, t);
    o.frequency.exponentialRampToValueAtTime(Math.max(200, n.f1), t + n.dur);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(v.gain, t + Math.min(0.012, n.dur / 3));
    g.gain.exponentialRampToValueAtTime(0.0001, t + n.dur);
    if (p) { p.pan.value = v.pan; o.connect(g).connect(p).connect(bus); } else o.connect(g).connect(bus);
    o.start(t); o.stop(t + n.dur + 0.02);
  };
  let alive = true;
  return {
    /* Called often (every ~200 ms): schedules whatever falls inside the next second. */
    tick() {
      if (!alive) return;
      const now = ac.currentTime;
      for (const v of voices) {
        if (v.next > now + 1) continue;
        const at = Math.max(v.next, now + 0.05);
        const song = SONGS[v.songs[Math.floor(Math.random() * v.songs.length)]](v.pitch * (0.96 + Math.random() * 0.08));
        for (const n of song) chirp(at + n.dt, n, v);
        v.next = at + song[song.length - 1].dt + v.gap[0] + Math.random() * (v.gap[1] - v.gap[0]);
      }
    },
    /* Into the monitor's small speaker: no lows, no highs, quieter. */
    tinny(ms) {
      const t = ac.currentTime, tc = ms / 3000;
      hp.frequency.setTargetAtTime(750, t, tc); lp.frequency.setTargetAtTime(3600, t, tc); bus.gain.setTargetAtTime(0.5, t, tc);
    },
    stop(ms = 400) {
      alive = false;
      try { bus.gain.setTargetAtTime(0, ac.currentTime, ms / 3000); } catch { /* gone */ }
      setTimeout(() => io.stop(), ms + 200);
    },
  };
}

function startRoomTone(io) {
  const ac = io.ac, src = ac.createBufferSource(), lp = ac.createBiquadFilter(), g = ac.createGain();
  src.buffer = noiseBuffer(ac, 3, true); src.loop = true;
  lp.type = 'lowpass'; lp.frequency.value = 170;
  g.gain.value = 0;
  src.connect(lp).connect(g).connect(io.node);
  src.start();
  return {
    up(ms) { g.gain.setTargetAtTime(0.5, ac.currentTime, ms / 3000); },
    stop(ms = 300) { try { g.gain.setTargetAtTime(0, ac.currentTime, ms / 3000); } catch { /* gone */ } setTimeout(() => { try { src.stop(); } catch { /* gone */ } io.stop(); }, ms + 200); },
  };
}

/** A short burst of static through a band-pass: 150-400 ms, a few sharper pops inside it. */
function crackle(io) {
  if (!io) return 0;
  const ac = io.ac, t = ac.currentTime + 0.01, dur = 0.15 + Math.random() * 0.25;
  const src = ac.createBufferSource(), bp = ac.createBiquadFilter(), g = ac.createGain();
  src.buffer = io.noise ||= noiseBuffer(ac, 0.5);
  bp.type = 'bandpass'; bp.frequency.value = 1700 + Math.random() * 1600; bp.Q.value = 0.8;
  g.gain.setValueAtTime(0.0001, t);
  g.gain.exponentialRampToValueAtTime(0.32, t + 0.012);
  const pops = 2 + Math.floor(Math.random() * 3);
  for (let i = 1; i <= pops; i++) {
    const at = t + (dur * i) / (pops + 1);
    g.gain.setValueAtTime(0.1 + Math.random() * 0.12, at);
    g.gain.linearRampToValueAtTime(0.3, at + 0.008);
  }
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  src.connect(bp).connect(g).connect(io.node);
  src.start(t, Math.random() * 0.2); src.stop(t + dur + 0.02);
  return dur * 1000;
}

/** The lo-fi bed: the first of the radio's tracks that exists, streamed through the kit, or null. */
async function findBed(urls) {
  for (const url of urls) {
    try { const r = await fetch(url, { method: 'HEAD' }); if (r.ok) return url; } catch { /* next */ }
  }
  return null;
}


/* ---- the show ---- */

async function fetchJson(url) {
  try { const r = await fetch(url); return r.ok ? await r.json() : null; } catch { return null; }
}
const within = (p, ms, fallback = null) => Promise.race([Promise.resolve(p).catch(() => fallback), new Promise((r) => setTimeout(() => r(fallback), ms))]);

/** The office resolves true after its closing line. A panic or scope cleanup cancels the handoff to the poster. */
export function outsideShow(ctx, E, { createMeadow: create = null, assets = ASSETS } = {}) {
  return new Promise((done) => {
    /* Owner 2026-11-09: nothing else plays underneath, from the first frame of the show to its end. The ending scope's
     * cleanup lets go (run end, panic). Only sources this unlock owns pass: birds, bed, room tone, static. */
    ctx.audio.solo?.(true);
    const doc = ctx.layer('ending').ownerDocument || document;
    const win = doc.defaultView;
    const reduced = !!(ctx.gentle || ctx.reduced);
    const touch = !!win.matchMedia?.('(pointer: coarse)').matches;
    let tl = outsideTimeline({ reduced, credits: E.credits.length }), t = 0, over = false, phase = 'veil';
    let vw = win.innerWidth, vh = win.innerHeight;

    const root = ctx.el('div', `position:absolute;inset:0;overflow:hidden;overflow:clip;background:#0c0b12;pointer-events:auto;touch-action:none;user-select:none;-webkit-user-select:none;`, null, 'ending');
    root.dataset.outside = 'show';
    /* overflow clip, not hidden: the canvas box and the clip are wider than a phone, and a tap could scroll hidden overflow. */
    const stage = h('div', `position:absolute;left:0;top:0;width:${vw}px;height:${vh}px;overflow:hidden;overflow:clip;`, null, root);
    /* The office: the clip, then the hold still (or the plain room), in picture pixels under one cover transform. */
    const media = h('div', 'position:absolute;left:0;top:0;transform-origin:0 0;opacity:0;pointer-events:none;', null, stage);
    const edge = h('div', 'position:absolute;inset:0;opacity:0;pointer-events:none;box-shadow:inset 0 0 70px 34px #0c0b12;z-index:2;', null, media);
    const meadowHost = h('div', 'position:absolute;left:0;top:0;pointer-events:none;', null, stage);
    /* The glass over the screen: a sheen and a vignette. No scanlines: shrunk by the homography they alias into rings. */
    const screenFx = h('div', `position:absolute;left:0;top:0;transform-origin:0 0;pointer-events:none;opacity:0;background:linear-gradient(160deg,rgba(255,255,255,.08),transparent 38%),radial-gradient(ellipse at center,transparent 58%,rgba(0,0,0,.38));`, null, stage);
    const dip = h('div', 'position:absolute;inset:0;background:#05060a;opacity:0;', null, screenFx);
    const flick = h('div', 'position:absolute;inset:0;background:#000;opacity:0;pointer-events:none;', null, stage);
    const veil = h('div', 'position:absolute;inset:0;background:#fff;pointer-events:none;', null, root);
    const card = h('div', `position:absolute;left:0;right:0;bottom:7%;padding:0 22px;text-align:center;color:#ece6f2;font:600 17px/1.35 ${FONT};text-shadow:0 2px 10px rgba(0,0,0,.85);pointer-events:none;z-index:3;`, null, root);

    /* The canvas box: the cover rectangle of a 16:9 frame (frame 0 of the clip is the screen), so the live meadow is
     * already the screen's picture and nothing changes size when the pull starts. */
    let box = { w: FRAME[0], h: FRAME[1] };
    const sizeBox = () => {
      const c = coverFit(vw, vh, FRAME);
      box = { w: FRAME[0] * c.s, h: FRAME[1] * c.s };
      for (const e of [stage]) { e.style.width = vw + 'px'; e.style.height = vh + 'px'; }
      for (const e of [meadowHost, screenFx]) { e.style.width = box.w + 'px'; e.style.height = box.h + 'px'; }
    };
    sizeBox();

    /* ---- loads: the track, the hold still, the clip ---- */
    let track = fallbackTrack(), holdImg = null, video = null, videoBad = reduced, videoEnded = false;
    const trackReady = within(fetchJson(assets.track), LOAD_WAIT_MS).then((j) => { const tr = readTrack(j); if (tr) track = tr; root.dataset.track = tr ? 'office' : 'fallback'; });
    const holdReady = within(loadImage(doc, assets.hold), LOAD_WAIT_MS).then((img) => { holdImg = img; });
    if (!reduced) {
      video = doc.createElement('video');
      video.muted = true; video.defaultMuted = true; video.playsInline = true; video.preload = 'auto';
      video.disablePictureInPicture = true; video.setAttribute('playsinline', ''); video.setAttribute('muted', '');
      video.style.cssText = `position:absolute;left:0;top:0;width:${FRAME[0]}px;height:${FRAME[1]}px;display:block;`;
      video.dataset.outside = 'clip';
      video.addEventListener('error', () => { videoBad = true; });
      video.addEventListener('ended', () => { videoEnded = true; });
      video.src = assets.video;
      media.appendChild(video);
    }

    /* ---- the meadow ---- */
    let meadow = null, stillImg = null, stillPic = null, crop = null;
    let screenScale = 1, pullScaled = false;
    const sizeMeadow = (live = phase !== 'pull' && phase !== 'hold', scale = screenScale) => {
      screenScale = scale;
      crop = live && meadow?.viewCrop ? {
        fullWidth: box.w, fullHeight: box.h, x: (box.w - vw) / 2, y: (box.h - vh) / 2, width: vw, height: vh,
      } : null;
      meadowHost.style.width = (crop?.width || box.w) + 'px';
      meadowHost.style.height = (crop?.height || box.h) + 'px';
      meadow?.resize?.(crop, scale);
      placeCanvas(meadow?.canvas);
    };
    const stillReady = loadImage(doc, assets.still).then((img) => (stillImg = img));
    const meadowReady = reduced ? Promise.resolve(null) : (async () => {
      let make = create;
      if (!make) { try { make = (await import('./outside-meadow.js')).createMeadow; } catch { make = null; } }
      if (typeof make !== 'function') return null;
      const pending = Promise.resolve().then(() => make(meadowHost, {
        atlasUrl: assets.atlas, atlasJsonUrl: assets.atlasJson, reduced, lite: LITE || touch,
        frame: ctx.frame, audio: ctx.audio, paused: () => ctx.paused, hudHost: root,
      }));
      const late = await Promise.race([pending.catch(() => null), new Promise((r) => setTimeout(() => r('late'), MEADOW_WAIT_MS))]);
      if (late === 'late') { pending.then((m) => m?.stop?.(), () => {}); return null; }
      return late;
    })().catch(() => null);

    /* Individual properties: the meadow's own touch-action and pixelated look stay. */
    const placeCanvas = (c) => {
      if (!c) return;
      Object.assign(c.style, { position: 'absolute', left: '0', top: '0', width: (crop?.width || box.w) + 'px', height: (crop?.height || box.h) + 'px', transformOrigin: '0 0', display: 'block', pointerEvents: 'auto' });
      if (c.tagName === 'IMG') c.style.objectFit = 'cover';
    };
    const screenEl = () => meadow?.canvas || stillPic;

    /* ---- layout ---- */
    let scene = null;   // the hold: { el, size, quad (picture px) }
    const putMedia = (fit, size) => {
      media.style.width = size[0] + 'px'; media.style.height = size[1] + 'px';
      media.style.transform = `translate(${fit.ox}px,${fit.oy}px) scale(${fit.s})`;
      const cover = coverFit(vw, vh, size).s;
      edge.style.opacity = String(clamp01((cover - fit.s) / (cover * 0.12)));
    };
    const putScreen = (quad, glass, extra = 0) => {
      const full = homography(box.w, box.h, grow(quad, OVERSCAN_PX + extra));
      const c = screenEl();
      if (c) c.style.transform = matrixCss(crop ? cropProjection(full, crop) : full);
      screenFx.style.transform = matrixCss(full);
      if (glass != null) screenFx.style.opacity = String(glass);
    };
    /* Before the pull: the canvas is the screen of frame 0. */
    const layoutLive = () => { const f = coverFit(vw, vh, FRAME); putMedia(f, FRAME); putScreen(mapQuad(track.frames[0], f), 0); };
    const screenResolution = (q) => Math.min(1, Math.max(
      Math.hypot(q[1][0] - q[0][0], q[1][1] - q[0][1]) / box.w,
      Math.hypot(q[2][0] - q[3][0], q[2][1] - q[3][1]) / box.w,
      Math.hypot(q[3][0] - q[0][0], q[3][1] - q[0][1]) / box.h,
      Math.hypot(q[2][0] - q[1][0], q[2][1] - q[1][1]) / box.h,
    ));
    let vidFrame = 0;
    const layoutPull = () => { const l = pullLayout(track, vidFrame, vw, vh); putMedia(l.fit, track.size); putScreen(l.quad, l.glass, l.motion); };
    const layoutHold = () => {
      if (!scene) return;
      const f = holdFit(vw, vh, scene.quad, scene.size);
      putMedia(f, scene.size); putScreen(mapQuad(scene.quad, f), 1);
    };
    const relayout = () => (phase === 'hold' || mode === 'fade' ? layoutHold() : phase === 'pull' && mode === 'video' ? layoutPull() : layoutLive());

    /* The hold's picture: the still, else the clip's own last frame, else the plain room. */
    const lastQuad = () => track.frames[track.frames.length - 1];
    const buildScene = (useClip) => {
      if (scene) return scene;
      if (holdImg) {
        holdImg.style.cssText = `position:absolute;left:0;top:0;width:${track.size[0]}px;height:${track.size[1]}px;display:block;`;
        holdImg.dataset.outside = 'hold';
        media.insertBefore(holdImg, edge);
        scene = { el: holdImg, size: track.size, quad: lastQuad(), kind: 'office' };
      } else if (useClip && video && !videoBad && video.readyState >= 2) {
        scene = { el: video, size: track.size, quad: lastQuad(), kind: 'clip' };
      } else {
        const p = plainRoom(doc);
        p.el.style.cssText = 'position:absolute;left:0;top:0;display:block;';
        p.el.dataset.outside = 'plain';
        media.insertBefore(p.el, edge);
        scene = { el: p.el, size: ROOM_SIZE, quad: p.quad, kind: 'plain' };
      }
      if (scene.el !== video && video) video.style.visibility = 'hidden';
      root.dataset.scene = scene.kind;
      return scene;
    };

    /* ---- sound ---- */
    const birdsIo = ctx.audio.input?.({ bus: 'ambient', gain: 1.5, sourceLabel: 'Birds' });
    const birds = birdsIo ? startBirds(birdsIo) : null;
    let bed = null, tone = null, staticIo = null;
    findBed(assets.beds).then((url) => {
      if (!url || over) return;
      bed = ctx.audio.stream(url, { bus: 'ambient', gain: 0.45, loop: true, autoplay: true, source: 'outside-bed', sourceLabel: 'Music' });
    });
    const toTinny = (ms) => {
      birds?.tinny(ms);
      if (bed) { ctx.audio.muffle('outside-bed', true); bed.setGain?.(0.3); }
      if (!tone) {
        const io = ctx.audio.input?.({ bus: 'ambient', gain: 0.6, source: 'outside-room', sourceLabel: 'Room' });
        tone = io ? startRoomTone(io) : null;
        tone?.up(ms);
      }
    };
    const stopSound = (ms) => {
      birds?.stop(ms); tone?.stop(ms); bed?.stop(ms);
      if (staticIo) { const io = staticIo; staticIo = null; setTimeout(() => io.stop(), ms + 500); }
    };

    /* ---- input ---- */
    const hint = h('div', `position:absolute;left:50%;top:15%;transform:translateX(-50%) rotate(-1.5deg);padding:7px 14px;background:#fffdf4;color:#2b2530;border-radius:3px;box-shadow:0 3px 12px rgba(0,0,0,.25);font:600 15px/1.2 ${FONT};opacity:0;pointer-events:none;width:max-content;max-width:78%;text-align:center;`, touch ? 'Move with the pad. Drag to look. Hold Dig.' : 'WASD to walk. Hold click to dig. Right click to place. Space to jump.', root);
    const skipBtn = h('button', `position:absolute;left:14px;top:14px;padding:6px 14px;border:0;border-radius:3px;background:#fffdf4;color:#2b2530;box-shadow:0 2px 8px rgba(0,0,0,.25);font:600 14px ${FONT};cursor:pointer;pointer-events:auto;opacity:.85;z-index:4;`, 'Skip', root);
    skipBtn.type = 'button';
    skipBtn.dataset.outside = 'skip';
    skipBtn.addEventListener('pointerdown', (e) => e.stopPropagation());
    skipBtn.addEventListener('click', (e) => { e.stopPropagation(); skip(); });
    const onKey = (e) => {
      if (e.key !== 'Enter' || ctx.paused || over) return;
      e.preventDefault();
      if (phase === 'hold' && t >= tl.closingAt) finish(); else skip();
    };
    doc.addEventListener('keydown', onKey);
    /* After the closing line a press may take the credits; before it, a press at the hold does nothing. */
    root.addEventListener('click', () => { if (phase === 'hold' && t >= tl.closingAt) finish(); });
    const onResize = () => {
      vw = win.innerWidth; vh = win.innerHeight;
      sizeBox();
      placeCanvas(meadow?.canvas); placeCanvas(stillPic);
      if (phase === 'hold' && scene) screenScale = screenResolution(mapQuad(scene.quad, holdFit(vw, vh, scene.quad, scene.size)));
      else if (phase === 'pull' && pullScaled) screenScale = screenResolution(pullLayout(track, vidFrame, vw, vh).quad);
      sizeMeadow();
      relayout();
    };
    win.addEventListener('resize', onResize);

    /* The clip follows the game's pause and a host suspend; it never runs on its own. */
    const clipPlay = () => {
      if (!video || over || mode !== 'video' || phase !== 'pull' || ctx.paused) return;
      try { video.play()?.catch?.(() => { if (!ctx.paused && phase === 'pull') videoBad = true; }); } catch { videoBad = true; }
    };
    const clipPause = () => { try { video?.pause(); } catch { /* gone */ } };
    ctx.on('pause', clipPause);
    ctx.on('resume', clipPlay);
    ctx.on('suspend', (s) => (s?.on ? clipPause() : clipPlay()));

    /* The frame on show: requestVideoFrameCallback hands the presented frame's own time; else currentTime per frame. */
    const rvfc = !!video && typeof video.requestVideoFrameCallback === 'function';
    const onVideoFrame = (_now, meta) => {
      if (over || !video) return;
      vidFrame = frameAt(track, meta?.mediaTime ?? video.currentTime);
      if (phase === 'pull' && mode === 'video') layoutPull();
      video.requestVideoFrameCallback(onVideoFrame);
    };
    if (rvfc) video.requestVideoFrameCallback(onVideoFrame);

    const showStill = () => {
      if (stillImg) { stillPic = stillImg; meadowHost.appendChild(stillImg); }
      else {
        const c = doc.createElement('canvas'); c.width = 640; c.height = 360; paintSky(c.getContext('2d'), 640, 360);
        stillPic = c; meadowHost.appendChild(c);
      }
      stillPic.dataset.meadow = 'still';
      placeCanvas(stillPic);
    };

    /* ---- beats ---- */
    let nextCrackle = Infinity, started = false, locked = false, mode = null, pullStart = 0;
    let fadePending = false, fadeReady = null, fadeUrl = null;

    const lock = () => {
      if (locked) return;
      locked = true;
      if (phase === 'veil') meadow?.start?.();
      meadow?.lockControls?.();
      ctx.anim(hint, [{ opacity: +getComputedStyle(hint).opacity || 0 }, { opacity: 0 }], { duration: 400, fill: 'forwards' });
    };
    /* The pull, once: the clip if it can play, else a cross-fade into the hold (reduced, or a clip that failed). */
    const startPull = () => {
      phase = 'pull';
      sizeMeadow(false, 1);
      layoutLive();
      pullStart = t;
      lock();
      meadow?.autopilot?.(true);
      toTinny(BEATS.pullMs);
      media.style.opacity = '1';
      if (!reduced && video && !videoBad && video.readyState >= 2) {
        mode = 'video';
        root.dataset.pull = 'clip';
        vidFrame = 0;
        layoutPull();
        clipPlay();
        return;
      }
      mode = 'fade';
      root.dataset.pull = 'fade';
      startFade();
    };
    const enterHold = (fromSkip = false) => {
      if (phase === 'hold') return;
      lock();
      phase = 'hold';
      if (fadePending) {
        fadePending = false; fadeReady = null;
        if (fadeUrl) { win.URL.revokeObjectURL(fadeUrl); fadeUrl = null; }
      }
      meadow?.autopilot?.(true);
      if (fromSkip && video && !holdImg && !videoBad && Number.isFinite(video.duration)) { try { video.currentTime = Math.max(0, video.duration - 0.02); } catch { /* fine */ } }
      clipPause();
      buildScene(!reduced && mode === 'video' || fromSkip);
      media.style.opacity = '1';
      const q = mapQuad(scene.quad, holdFit(vw, vh, scene.quad, scene.size));
      sizeMeadow(false, screenResolution(q));
      layoutHold();
      if (reduced || mode === 'fade') meadowHost.style.opacity = '1';
      skipBtn.remove();
      veil.style.display = 'none';
      toTinny(reduced ? 1500 : 600);
      staticIo ||= ctx.audio.input?.({ bus: 'sfx', gain: 1, source: 'outside-static', sourceLabel: 'Static' }) || null;
      nextCrackle = t + 1200 + Math.random() * 1600;
      for (const l of tl.lines) {
        ctx.after(Math.max(0, l.at - t), () => {
          if (over) return;
          const text = l.kind === 'result' ? E.result : l.kind === 'closing' ? E.closing : E.credits[l.i];
          const css = l.kind === 'credit' ? 'margin-top:3px;font-weight:500;font-size:14px;opacity:.78;' : l.kind === 'closing' ? 'margin-top:12px;font-size:19px;' : 'font-size:20px;margin-bottom:6px;';
          const e = h('div', css, ctx.brand.text(text), card);
          e.dataset.line = l.kind;
          ctx.anim(e, [{ opacity: 0, transform: 'translateY(4px)' }, { opacity: 1, transform: 'none' }], { duration: 600, easing: 'ease-out', fill: 'both' });
        });
      }
    };
    function skip() {
      if (over || phase === 'hold' || !ready) return;
      tl = skipTo(tl, t);
      root.dataset.skipped = '1';
      enterHold(true);
    }

    const begin = () => {
      started = true;
      if (reduced || !meadow) showStill();
      if (meadow) sizeMeadow();
      layoutLive();
      if (root.dataset.meadow === 'live') ctx.anim(hint, [
        { opacity: 0 }, { opacity: 1, offset: 0.15 }, { opacity: 1, offset: 0.78 }, { opacity: 0 },
      ], { duration: 5500, fill: 'forwards', easing: 'ease-out' });
      ctx.anim(veil, [{ opacity: 1 }, { opacity: 0 }], { duration: BEATS.veilMs, easing: 'ease-in-out', fill: 'forwards' });
      birdsTick();
    };
    const birdsTick = () => birds?.tick();
    const stopBirdClock = ctx.every(220, birdsTick);

    /* The meadow (or its stand-in) and the track must be there before the veil clears: the show clock waits, capped. */
    let ready = false;
    Promise.all([meadowReady, within(stillReady, LOAD_WAIT_MS), trackReady, holdReady]).then(([m]) => {
      if (over) { m?.stop?.(); return; }
      meadow = m;
      if (!reduced && !meadow) { meadow = stubMeadow(doc, meadowHost, { still: stillImg, frame: ctx.frame }); }
      root.dataset.meadow = reduced ? 'still' : (m ? 'live' : 'stub');
      if (!reduced) tl = outsideTimeline({ reduced, credits: E.credits.length, pullMs: trackMs(track), liveMs: m ? BEATS.liveMs : BEATS.stillMs });
      ready = true;
      if (!started) begin();
    });

    const stopClock = ctx.frame((sec) => {
      if (over || !ready) return;
      t += sec * 1000;
      if (phase === 'hold') {
        if (t >= nextCrackle) {
          const ms = crackle(staticIo);
          if (!reduced && ms) {
            const d = Math.max(160, ms);
            ctx.anim(dip, [{ opacity: 0 }, { opacity: 0.55, offset: 0.25 }, { opacity: 0.1, offset: 0.6 }, { opacity: 0 }], { duration: d, easing: 'linear' });
            ctx.anim(flick, [{ opacity: 0 }, { opacity: 0.1, offset: 0.2 }, { opacity: 0.02, offset: 0.45 }, { opacity: 0.07, offset: 0.6 }, { opacity: 0 }], { duration: d, easing: 'linear' });
          }
          nextCrackle = t + crackleGap();
        }
        if (t >= tl.finishAt) finish();
        return;
      }
      if (phase === 'pull') {
        if (mode === 'video') {
          if (!rvfc) { vidFrame = frameAt(track, video.currentTime); layoutPull(); }
          if (!pullScaled && vidFrame >= track.frames.length / 2) {
            pullScaled = true;
            sizeMeadow(false, screenResolution(pullLayout(track, vidFrame, vw, vh).quad));
            layoutPull();
          }
          /* The clip decides when the hold starts: its end, or a stall past the cap, or a failure (the hold still). */
          if (videoEnded || videoBad || t >= pullStart + trackMs(track) + BEATS.pullCapMs) { tl = holdFrom(tl, t); enterHold(); }
        } else {
          if (fadePending && fadeReady) {
            fadePending = false; pullStart = t;
            showFade(fadeReady); fadeReady = null;
          }
          if (!fadePending && t >= pullStart + BEATS.fadeMs) { tl = holdFrom(tl, t); enterHold(); }
        }
        return;
      }
      const next = phaseAt(tl, t);
      if (next === 'live' && phase === 'veil') meadow?.start?.();
      if (next === 'settle' || next === 'pull' || next === 'hold' || next === 'done') lock();
      if (next === 'pull' || next === 'hold' || next === 'done') {
        /* The clip still loading: the meadow goes on a little longer, then the cross-fade. */
        const waiting = !reduced && video && !videoBad && video.readyState < 2 && t < tl.pullAt + BEATS.videoWaitMs;
        if (!waiting) startPull();
        return;
      }
      phase = next;
    });

    /* Reduced and a failed clip: no zoom. The hold (the meadow in its screen) fades in under the full-view meadow. */
    async function startFade() {
      fadePending = true;
      const big = doc.createElement('img'), src = reduced ? stillPic : null;
      let url = null, owned = false;
      try {
        if (src instanceof win.HTMLImageElement) url = src.src;
        else {
          const canvas = src instanceof win.HTMLCanvasElement ? src : meadow?.canvas;
          const blob = meadow?.stillBlob ? await meadow.stillBlob() : await new Promise((resolve) => {
            if (!canvas?.toBlob) { resolve(null); return; }
            try { canvas.toBlob(resolve, 'image/jpeg', .86); } catch { resolve(null); }
          });
          if (blob) { url = win.URL.createObjectURL(blob); owned = true; }
        }
        if (url) { big.src = url; await big.decode?.().catch(() => {}); }
      } catch { /* The room can fade in without a snapshot. */ }
      if (over || phase !== 'pull' || mode !== 'fade') { if (owned) win.URL.revokeObjectURL(url); return; }
      if (owned) fadeUrl = url;
      // The scoped frame resumes this handoff only when the show itself is running.
      fadeReady = { big, url };
    }
    function showFade({ big, url }) {
      buildScene(false);
      media.style.opacity = '0';
      layoutHold();
      if (url) {
        big.src = url;
        big.dataset.meadow = 'cover';
        const f = coverFit(vw, vh, FRAME);
        big.style.cssText = `position:absolute;left:${f.ox}px;top:${f.oy}px;width:${box.w}px;height:${box.h}px;object-fit:cover;pointer-events:none;`;
        root.insertBefore(big, veil);
        ctx.anim(big, [{ opacity: 1 }, { opacity: 0 }], { duration: BEATS.fadeMs, fill: 'forwards' });
      }
      ctx.anim(media, [{ opacity: 0 }, { opacity: 1 }], { duration: BEATS.fadeMs, fill: 'forwards' });
      ctx.anim(screenFx, [{ opacity: 0 }, { opacity: 1 }], { duration: BEATS.fadeMs, fill: 'forwards' });
    }

    function finish(completed = true) {
      if (over) return;
      over = true;
      stopClock();
      stopBirdClock();
      doc.removeEventListener('keydown', onKey);
      win.removeEventListener('resize', onResize);
      if (bed) ctx.audio.muffle('outside-bed', false);
      stopSound(completed ? 180 : 0);
      if (video) { try { video.pause(); video.removeAttribute('src'); video.load(); } catch { /* gone */ } }
      meadow?.stop?.();
      if (fadeUrl) { win.URL.revokeObjectURL(fadeUrl); fadeUrl = null; }
      fadeReady = null;
      root.remove();
      done(completed);
    }
    ctx.on('suspend', (s) => { if (s?.on && s.reason === 'panic') finish(false); });
    ctx.cleanup(() => finish(false));
    /* Test hooks: the phase, the live timeline, the pull mode and the frame on show. */
    root.__outside = {
      get phase() { return phase; }, get t() { return t; }, get timeline() { return tl; }, get mode() { return mode; },
      get frame() { return vidFrame; }, get track() { return track; }, skip, ready: () => ready,
    };
  });
}
