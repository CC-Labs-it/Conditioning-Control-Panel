/* ui/unlocks/extras/outside-show.js - the Go Outside ending show (handoff-1109/TC-GO-OUTSIDE-ENDING-BRIEF.md and
 * TC-GO-OUTSIDE-LANES.md, lane S). It runs as the ending row's ending(ctx), after the white fade.
 *
 *   1. The white fade clears onto a LIVE blocky meadow (lane W's createMeadow, ./outside-meadow.js). The player can look
 *      around and walk for LIVE_MS. A small paper note says how.
 *   2. The controls are cut and the camera settles (lockControls + autopilot). Then the pull back: the meadow canvas
 *      shrinks into the CRT screen of the player's own lo-fi desk drawing (assets/outside/room-<niche>.webp, the screen
 *      punched out). It sits behind the drawing and is mapped onto the hole's quad (assets/outside/screens.json) with a
 *      CSS matrix3d homography.
 *   3. The hold: a dim room lit by the screen, a static crackle every 3-7 s, the end card low on the screen. No way out.
 *
 * Input: during the live part a press belongs to the meadow, never a skip. The Skip button or Enter jumps to the hold
 * (never past it). Escape stays the game's pause; a panic suspend ends the show at once. Gentle / reduced motion: no live
 * scene, no zoom, no dip: meadow.jpg cross-fades to the room with the still in the screen.
 * Missing pieces never break it: createMeadow rejects -> a canvas painting meadow.jpg -> a flat painted sky; no room
 * drawing or screens.json -> a plain dark crayon room. Every sound is synthesised here except the optional lo-fi bed. */

import { FONT, h } from './kit.js';
import { LITE } from '../../perf.js';

export const ASSETS = Object.freeze({
  atlas: 'assets/outside/meadow-atlas.png',
  atlasJson: 'assets/outside/meadow-atlas.json',
  still: 'assets/outside/meadow.jpg',
  screens: 'assets/outside/screens.json',
  room: (niche) => `assets/outside/room-${niche}.webp`,
  /* The lo-fi radio's own tracks (CC Labs owned). Machine-local in some builds: missing = birds alone. */
  beds: ['assets/lofi/audio/breathe-in.mp3', 'assets/lofi/audio/idle-cycle.mp3', 'assets/lofi/audio/dose.mp3'],
});

export const ROOM_SIZE = Object.freeze([1200, 896]);
export const NICHES = Object.freeze(['sissy', 'bimbo', 'goon', 'chastity', 'drone', 'trance', 'infection', 'censored']);
export const SKY = '#9fd3f5';

/* Beat lengths in game milliseconds from the start of the show (the white fade before it is the engine's). */
export const BEATS = Object.freeze({
  veilMs: 1200,        // the white clears onto the meadow
  liveMs: 6500,        // controls on
  settleMs: 900,       // controls cut, camera eases to the calm view
  pullMs: 4000,        // the pull back into the monitor
  cardLeadMs: 900,     // hold -> first card line
  lineGapMs: 900,      // between card lines
  tailMs: 6500,        // the last line stays this long, then the credits
  stillMs: 3600,       // reduced: the meadow still holds this long
  fadeMs: 1600,        // reduced: the cross-fade to the room
  hintInMs: 2000,
  crackleMinMs: 3000,
  crackleMaxMs: 7000,
});
export const MEADOW_WAIT_MS = 3500;   // createMeadow slower than this -> the stub; a late scene is stopped unused
export const ROOM_WAIT_MS = 3000;     // the drawing or screens.json slower than this -> the plain room

/** The card lines after a hold that starts at `holdAt`: result, credits, closing, then the show ends. */
export function holdBeats(holdAt, credits = 0) {
  const lines = [{ at: holdAt + BEATS.cardLeadMs, kind: 'result' }];
  for (let i = 0; i < credits; i++) lines.push({ at: holdAt + BEATS.cardLeadMs + (i + 1) * BEATS.lineGapMs, kind: 'credit', i });
  const closingAt = holdAt + BEATS.cardLeadMs + (credits + 1) * BEATS.lineGapMs + 400;
  lines.push({ at: closingAt, kind: 'closing' });
  return { holdAt, lines, closingAt, finishAt: closingAt + BEATS.tailMs };
}

/** The whole plan. Reduced: no live part, no settle, the "pull" is a cross-fade. */
export function outsideTimeline({ reduced = false, credits = 0 } = {}) {
  if (reduced) {
    const pullAt = BEATS.veilMs + BEATS.stillMs;
    const holdAt = pullAt + BEATS.fadeMs;
    return { reduced: true, liveAt: null, lockAt: null, pullAt, pullEnd: holdAt, ...holdBeats(holdAt, credits) };
  }
  const liveAt = Math.round(BEATS.veilMs / 2);
  const lockAt = liveAt + BEATS.liveMs;
  const pullAt = lockAt + BEATS.settleMs;
  const holdAt = pullAt + BEATS.pullMs;
  return { reduced: false, liveAt, lockAt, pullAt, pullEnd: holdAt, ...holdBeats(holdAt, credits) };
}

/** A skip at time t: everything before the hold is dropped and the hold starts now. A skip at or after the hold changes nothing. */
export function skipTo(tl, t) {
  if (t >= tl.holdAt) return tl;
  return { ...tl, lockAt: tl.lockAt == null ? null : Math.min(tl.lockAt, t), pullAt: t, pullEnd: t, skipped: true, ...holdBeats(t, tl.lines.filter((l) => l.kind === 'credit').length) };
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

/** The theme's drawing: Locked draws as chastity, no theme is trance. */
export function nicheFor(pick) {
  const n = pick === 'locked' ? 'chastity' : pick;
  return NICHES.includes(n) ? n : 'trance';
}

const goodQuad = (q) => Array.isArray(q) && q.length === 4 && q.every((p) => Array.isArray(p) && p.length === 2 && p.every(Number.isFinite));

/** { niche, quad } from screens.json for this niche, else trance's, else null (the plain room). */
export function roomChoice(screens, niche) {
  const all = screens && screens.screens;
  if (!all || typeof all !== 'object') return null;
  for (const n of [niche, 'trance']) if (goodQuad(all[n]?.quad)) return { niche: n, quad: all[n].quad.map(([x, y]) => [x, y]) };
  return null;
}

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

/** The drawing covers the view (on a tall phone, a little less, with dark above and below), centred on its screen. */
export function roomFit(vw, vh, quad, size = ROOM_SIZE) {
  const [iw, ih] = size;
  const portrait = vh > vw * 1.2;
  const s = Math.max(vw / iw, (vh / ih) * (portrait ? 0.72 : 1));
  const cx = quad.reduce((t, p) => t + p[0], 0) / 4, cy = quad.reduce((t, p) => t + p[1], 0) / 4;
  const clamp = (v, lo, hi) => (lo > hi ? (lo + hi) / 2 : Math.min(hi, Math.max(lo, v)));
  /* A tall phone looks halfway between the screen and the middle of the drawing, so more of the person shows, while the
   * whole screen stays at least 12 px inside the view. */
  const xs = quad.map((p) => p[0]);
  const fx = portrait ? (cx + iw / 2) / 2 : cx;
  const ox = clamp(clamp(vw / 2 - fx * s, 12 - Math.min(...xs) * s, vw - 12 - Math.max(...xs) * s), vw - iw * s, 0);
  const oy = clamp(vh / 2 - cy * s, vh - ih * s, 0);
  return { s, ox, oy, quad: quad.map(([x, y]) => [ox + x * s, oy + y * s]) };
}

const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const clamp01 = (v) => Math.max(0, Math.min(1, v));
const lerp = (a, b, t) => a + (b - a) * t;
const centre = (q) => [q.reduce((t, p) => t + p[0], 0) / 4, q.reduce((t, p) => t + p[1], 0) / 4];

/** The zoom the room starts at: its screen (roughly) fills the view. */
export function startZoom(vw, vh, quad) {
  const xs = quad.map((p) => p[0]), ys = quad.map((p) => p[1]);
  const qw = Math.max(1, Math.max(...xs) - Math.min(...xs)), qh = Math.max(1, Math.max(...ys) - Math.min(...ys));
  return Math.max(vw / qw, vh / qh) * 1.08;
}

/** One frame of the pull back at progress p (0..1): the room's zoom about the screen centre, where the canvas corners sit,
 *  and how far the room has faded up. p = 0: canvas = the view. p = 1: canvas = the hole exactly, zoom 1. */
export function pullFrame(p, vw, vh, quad, z0 = startZoom(vw, vh, quad)) {
  p = clamp01(p);
  const z = lerp(z0, 1, ease(p));
  const [qx, qy] = centre(quad);
  const zoomed = quad.map(([x, y]) => [qx + (x - qx) * z, qy + (y - qy) * z]);
  const view = [[0, 0], [vw, 0], [vw, vh], [0, vh]];
  const w = ease(clamp01(p / 0.62));
  const corners = view.map(([x, y], i) => [lerp(x, zoomed[i][0], w), lerp(y, zoomed[i][1], w)]);
  return { z, centre: [qx, qy], corners, room: ease(clamp01(p / 0.55)) };
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
    const r = canvas.getBoundingClientRect();
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

/** The ending show. Resolves when the card has stood a while, or at once on a panic suspend. */
export function outsideShow(ctx, E, { createMeadow: create = null } = {}) {
  return new Promise((done) => {
    const doc = ctx.layer('ending').ownerDocument || document;
    const win = doc.defaultView;
    const reduced = !!(ctx.gentle || ctx.reduced);
    const touch = !!win.matchMedia?.('(pointer: coarse)').matches;
    const tlBase = outsideTimeline({ reduced, credits: E.credits.length });
    let tl = tlBase, t = 0, over = false, phase = 'veil';
    let vw = win.innerWidth, vh = win.innerHeight;

    const root = ctx.el('div', `position:absolute;inset:0;overflow:hidden;overflow:clip;background:#0c0b12;pointer-events:auto;touch-action:none;user-select:none;-webkit-user-select:none;`, null, 'ending');
    root.dataset.outside = 'show';
    /* overflow clip, not hidden: the room is laid out at 1200 x 896 before its transform, and a focus or tap could scroll a
     * hidden overflow sideways (seen at 390 px). */
    const stage = h('div', `position:absolute;left:0;top:0;width:${vw}px;height:${vh}px;overflow:hidden;overflow:clip;`, null, root);
    const meadowHost = h('div', 'position:absolute;inset:0;', null, stage);
    /* The glass over the screen: a sheen and a vignette. No scanlines: shrunk by the homography they alias into rings. */
    const screenFx = h('div', `position:absolute;left:0;top:0;width:${vw}px;height:${vh}px;transform-origin:0 0;pointer-events:none;opacity:0;background:linear-gradient(160deg,rgba(255,255,255,.10),transparent 38%),radial-gradient(ellipse at center,transparent 55%,rgba(0,0,0,.45));`, null, stage);
    const dip = h('div', 'position:absolute;inset:0;background:#05060a;opacity:0;', null, screenFx);
    const roomWrap = h('div', 'position:absolute;left:0;top:0;transform-origin:0 0;opacity:0;pointer-events:none;', null, stage);
    const veil = h('div', 'position:absolute;inset:0;background:#fff;pointer-events:none;', null, root);
    const card = h('div', `position:absolute;left:0;right:0;bottom:7%;padding:0 22px;text-align:center;color:#ece6f2;font:600 17px/1.35 ${FONT};text-shadow:0 2px 10px rgba(0,0,0,.85);pointer-events:none;`, null, root);

    /* ---- the meadow ---- */
    let meadow = null, still = null, stillImg = null;
    const stillReady = loadImage(doc, ASSETS.still).then((img) => (stillImg = img));
    const meadowReady = reduced ? stillReady.then(() => null) : (async () => {
      let make = create;
      if (!make) { try { make = (await import('./outside-meadow.js')).createMeadow; } catch { make = null; } }
      if (typeof make !== 'function') return null;
      const pending = Promise.resolve().then(() => make(meadowHost, { atlasUrl: ASSETS.atlas, atlasJsonUrl: ASSETS.atlasJson, reduced, lite: LITE || touch, frame: ctx.frame }));
      const late = await Promise.race([pending.catch(() => null), new Promise((r) => setTimeout(() => r('late'), MEADOW_WAIT_MS))]);
      if (late === 'late') { pending.then((m) => m?.stop?.(), () => {}); return null; }
      return late;
    })().catch(() => null);

    const placeCanvas = (c) => {
      if (!c) return;
      c.style.cssText = `position:absolute;left:0;top:0;width:${vw}px;height:${vh}px;transform-origin:0 0;display:block;`;
    };

    /* ---- the room ---- */
    const niche = nicheFor(ctx.pick?.('f5'));
    let room = null;   // { el, quad (source px), fit }
    const roomLoad = (async () => {
      let screens = null;
      try { const r = await fetch(ASSETS.screens); if (r.ok) screens = await r.json(); } catch { screens = null; }
      const choice = roomChoice(screens, niche);
      const img = choice ? await loadImage(doc, ASSETS.room(choice.niche)) : null;
      if (img) { img.style.cssText = 'position:absolute;left:0;top:0;display:block;'; img.width = ROOM_SIZE[0]; img.height = ROOM_SIZE[1]; img.dataset.room = choice.niche; return { el: img, quad: choice.quad }; }
      return null;
    })();
    const plain = () => { const p = plainRoom(doc); p.el.style.cssText = 'position:absolute;left:0;top:0;display:block;'; p.el.dataset.room = 'plain'; return p; };
    const roomReady = Promise.race([roomLoad.catch(() => null), new Promise((r) => setTimeout(() => r(null), ROOM_WAIT_MS))])
      .then((r) => (room = r || plain()));

    const buildRoom = () => {
      if (!room || roomWrap.childElementCount) return;
      const [W, H] = ROOM_SIZE;
      roomWrap.style.width = W + 'px'; roomWrap.style.height = H + 'px';
      room.el.style.filter = 'brightness(.36) saturate(.75) contrast(1.05)';
      roomWrap.appendChild(room.el);
      const [cx, cy] = centre(room.quad);
      /* The screen's light on the room, and the dark it fades into. */
      h('div', `position:absolute;left:0;top:0;width:${W}px;height:${H}px;background:radial-gradient(circle at ${cx}px ${cy}px,rgba(150,205,255,.22),rgba(150,205,255,.08) 28%,transparent 52%);mix-blend-mode:screen;`, null, roomWrap).dataset.glow = '1';
      h('div', `position:absolute;left:0;top:0;width:${W}px;height:${H}px;box-shadow:inset 0 0 120px 60px #0c0b12;`, null, roomWrap);
    };
    const fitNow = () => (room ? roomFit(vw, vh, room.quad) : null);

    /* One layout of the room and the screen at pull progress p. */
    const layout = (p) => {
      buildRoom();
      const fit = fitNow();
      const canvas = meadow?.canvas || stillPic;
      if (!fit) return;
      const f = pullFrame(reduced ? 1 : p, vw, vh, fit.quad);
      const [qx, qy] = f.centre;
      roomWrap.style.transform = `translate(${qx}px,${qy}px) scale(${f.z}) translate(${-qx}px,${-qy}px) translate(${fit.ox}px,${fit.oy}px) scale(${fit.s})`;
      if (!reduced) roomWrap.style.opacity = String(f.room);
      const m = matrixCss(homography(vw, vh, f.corners));
      if (canvas) canvas.style.transform = m;
      screenFx.style.transform = m;
      screenFx.style.opacity = String(reduced ? 1 : f.room);
    };

    /* ---- sound ---- */
    const birdsIo = ctx.audio.input?.({ bus: 'ambient', gain: 1.5, sourceLabel: 'Birds' });
    const birds = birdsIo ? startBirds(birdsIo) : null;
    let bed = null, tone = null, staticIo = null;
    findBed(ASSETS.beds).then((url) => {
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
    const hint = h('div', `position:absolute;left:50%;bottom:12%;transform:translateX(-50%) rotate(-1.5deg);padding:7px 14px;background:#fffdf4;color:#2b2530;border-radius:3px;box-shadow:0 3px 12px rgba(0,0,0,.25);font:600 15px/1.2 ${FONT};opacity:0;pointer-events:none;white-space:nowrap;`, touch ? 'Drag to look. Hold to walk.' : 'Look around. Walk.', root);
    const skipBtn = h('button', `position:absolute;left:14px;top:14px;padding:6px 14px;border:0;border-radius:3px;background:#fffdf4;color:#2b2530;box-shadow:0 2px 8px rgba(0,0,0,.25);font:600 14px ${FONT};cursor:pointer;pointer-events:auto;opacity:.85;`, 'Skip', root);
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
      for (const e of [stage, screenFx]) { e.style.width = vw + 'px'; e.style.height = vh + 'px'; }
      placeCanvas(meadow?.canvas); placeCanvas(stillPic);
      meadow?.resize?.();
      if (phase === 'pull' || phase === 'hold') layout(phase === 'hold' ? 1 : pullP());
    };
    win.addEventListener('resize', onResize);

    let stillPic = null;   // reduced and fallback: the meadow as a picture (img or painted canvas) mapped like the canvas
    const showStill = () => {
      if (stillImg) { stillPic = stillImg; stillImg.style.objectFit = 'cover'; meadowHost.appendChild(stillImg); }
      else {
        const c = doc.createElement('canvas'); c.width = 640; c.height = 360; paintSky(c.getContext('2d'), 640, 360);
        stillPic = c; meadowHost.appendChild(c);
      }
      stillPic.dataset.meadow = 'still';
      placeCanvas(stillPic);
    };

    /* ---- beats ---- */
    const pullP = () => clamp01((t - tl.pullAt) / Math.max(1, tl.pullEnd - tl.pullAt));
    let nextCrackle = Infinity, started = false, locked = false;

    const lock = () => {
      if (locked) return;
      locked = true;
      meadow?.lockControls?.();
      ctx.anim(hint, [{ opacity: +getComputedStyle(hint).opacity || 0 }, { opacity: 0 }], { duration: 400, fill: 'forwards' });
    };
    const enterHold = () => {
      phase = 'hold';
      lock();
      buildRoom();
      layout(1);
      roomWrap.style.opacity = '1';
      if (reduced) { meadowHost.style.opacity = '1'; }
      meadow?.autopilot?.(true);
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
      enterHold();
    }

    const begin = () => {
      started = true;
      if (reduced || !meadow) showStill();
      if (meadow) { placeCanvas(meadow.canvas); meadow.start?.(); }
      if (!reduced && meadow) ctx.anim(hint, [{ opacity: 0 }, { opacity: 1 }], { duration: BEATS.hintInMs, fill: 'forwards', easing: 'ease-out' });
      ctx.anim(veil, [{ opacity: 1 }, { opacity: 0 }], { duration: BEATS.veilMs, easing: 'ease-in-out', fill: 'forwards' });
      birdsTick();
    };
    const birdsTick = () => birds?.tick();
    ctx.every(220, birdsTick);

    /* The meadow (or its stand-in) must be there before the veil clears: the show clock waits for it, capped. */
    let ready = false;
    Promise.all([meadowReady, stillReady, roomReady]).then(([m]) => {
      if (over) { m?.stop?.(); return; }
      meadow = m;
      if (!reduced && !meadow) { meadow = stubMeadow(doc, meadowHost, { still: stillImg, frame: ctx.frame }); }
      root.dataset.meadow = reduced ? 'still' : (m ? 'live' : 'stub');
      ready = true;
      if (!started) begin();
    });

    ctx.frame((sec) => {
      if (over || !ready) return;
      t += sec * 1000;
      const next = phaseAt(tl, t);
      if (phase === 'hold' || next === 'hold' || next === 'done') {
        if (phase !== 'hold') enterHold();
        if (t >= nextCrackle) {
          const ms = crackle(staticIo);
          if (!reduced && ms) {
            ctx.anim(dip, [{ opacity: 0 }, { opacity: 0.55, offset: 0.25 }, { opacity: 0.1, offset: 0.6 }, { opacity: 0 }], { duration: Math.max(160, ms), easing: 'linear' });
            const glow = roomWrap.querySelector('[data-glow]');
            if (glow) ctx.anim(glow, [{ opacity: 1 }, { opacity: 0.35, offset: 0.3 }, { opacity: 1 }], { duration: Math.max(160, ms) });
          }
          nextCrackle = t + crackleGap();
        }
        if (next === 'done') finish();
        return;
      }
      if (next === 'settle' || next === 'pull') lock();
      if (next === 'pull') {
        if (phase !== 'pull') { phase = 'pull'; meadow?.autopilot?.(true); buildRoom(); toTinny(BEATS.pullMs); if (reduced) startFade(); }
        if (!reduced) layout(pullP());
        return;
      }
      phase = next;
    });

    /* Reduced: no zoom. The room (still in the screen) fades in over the full-view still. */
    function startFade() {
      buildRoom();
      layout(1);
      const big = stillPic?.cloneNode?.(true);
      if (big) { big.style.transform = 'none'; big.dataset.meadow = 'cover'; root.insertBefore(big, veil); ctx.anim(big, [{ opacity: 1 }, { opacity: 0 }], { duration: BEATS.fadeMs, fill: 'forwards' }); }
      if (big && stillPic instanceof win.HTMLCanvasElement) big.getContext('2d').drawImage(stillPic, 0, 0);
      ctx.anim(roomWrap, [{ opacity: 0 }, { opacity: 1 }], { duration: BEATS.fadeMs, fill: 'forwards' });
    }

    function finish() {
      if (over) return;
      over = true;
      stopSound(500);
      done();
    }
    ctx.on('suspend', (s) => { if (s?.on && s.reason === 'panic') { root.dataset.aborted = '1'; stopSound(0); finish(); } });
    ctx.cleanup(() => {
      over = true;
      doc.removeEventListener('keydown', onKey);
      win.removeEventListener('resize', onResize);
      if (bed) ctx.audio.muffle('outside-bed', false);
      stopSound(0);
      meadow?.stop?.();
      meadowReady.then((m) => { if (m && m !== meadow) m.stop?.(); });
      root.remove();
    });
    /* Test hooks: the phase and the live timeline. */
    root.__outside = { get phase() { return phase; }, get t() { return t; }, get timeline() { return tl; }, skip, roomReady };
  });
}
