import { beatOf } from '../audio/chart.js';
import { screenAmounts } from './items/fxmath.js';
import { createAtmosphereClock } from './screen-atmosphere.js';

const sat = v => Math.max(0, Math.min(1, Number.isFinite(v) ? v : 0));
const smooth = (a, b, dt, tau) => a + (b - a) * (1 - Math.exp(-dt / tau));
const fract = v => v - Math.floor(v);
const colour = (v, fallback) => /^#[a-f\d]{6}$/i.test(v || '') ? v : fallback;
const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));

// One envelope on the existing music tap. Beat phase moves the edges, never the camera.
export function createMusicEdgeState({ photosafe = null } = {}) {
  const frame = { level: 0, shakePx: 0, pulse: 0, hueMix: .5, clock: 0, particleCount: 0, visibility: 1 };
  const amounts = {}, atmosphere = createAtmosphereClock();
  const beat = { index: 0, phase: 0, lenS: .5 }, grid = { bpm: 96, beats: [], downbeatS: 0 };
  let bar = null, granted = false, lastSongS = NaN, previousSong = null;
  function update(dt, view = {}, accentKind = 'music-edges') {
    if (view.enabled === false) {
      Object.assign(frame, { level: 0, shakePx: 0, pulse: 0, hueMix: .5, clock: 0, particleCount: 0, visibility: 1 });
      atmosphere.update(null, 0, 0); bar = null; granted = false; lastSongS = NaN; previousSong = null;
      return frame;
    }
    const a = view.audioReactive;
    if (view.paused || a?.frozen || !(dt > 0)) return frame;
    const step = Math.min(dt, .1), quiet = !!(view.gentle || view.reducedMotion);
    const screen = screenAmounts(view.screen, amounts);
    const fog = atmosphere.update(view.screen, screen.pink, step, screen.timeMul);
    frame.visibility = 1 - Math.max(screen.blackout, fog * .95);
    frame.clock += step;
    const song = view.beatSong, time = view.songS;
    const active = !!a?.active && Number.isFinite(time);
    const timed = active && Number.isFinite(song?.bpm) && song.bpm > 0;
    if (timed) {
      grid.bpm = song.bpm; grid.beats = song.beats || []; grid.downbeatS = song.downbeatS || 0;
      beatOf(grid, time, beat);
      const nextBar = Math.floor(beat.index / 4);
      // A seek or loop starts a fresh accent, without accumulating missed beats.
      if (song !== previousSong || time < lastSongS) bar = null;
      if (!quiet && nextBar !== bar) {
        bar = nextBar;
        granted = !photosafe || photosafe.request(accentKind);
      }
      previousSong = song; lastSongS = time;
    }
    // Brief recorded rests retain the bar's decision without spending the shared budget again.
    const allowed = timed && granted && !quiet;
    // The shared tap includes listening volume; lift its useful range without a minimum effect.
    const energy = active ? sat((sat(a.energy) * .7 + sat(a.bass) * .3) * 1.8) : 0;
    const target = energy * (quiet ? .35 : allowed ? 1 : .22);
    frame.level = smooth(frame.level, target, step, target > frame.level ? .4 : .65);
    if (frame.level < .0001 && target === 0) frame.level = 0;
    frame.pulse = timed && allowed && !quiet ? (.5 + .5 * Math.cos(beat.phase * Math.PI * 2)) ** 3 : 0;
    const drive = Math.pow(frame.level, 1.2);
    frame.shakePx = timed && allowed && !quiet
      ? Math.sin(beat.phase * Math.PI * 4) * frame.pulse * drive * 4 : 0;
    // feel wave: the trauma border shake (fx.state.borderShakePx, already smooth noise) rides on top, 6 px at most
    const tp = quiet ? 0 : Number(view.traumaPx) || 0;
    if (tp) frame.shakePx = Math.max(-6, Math.min(6, frame.shakePx + tp));
    frame.particleCount = active && allowed && !quiet ? Math.min(48, Math.floor(drive * (28 + sat(a.treble) * 20))) : 0;
    // Slow movement between the two actual course colours, not a rainbow sweep.
    const hue = timed && !quiet ? .5 + .35 * Math.sin((beat.index + beat.phase) * Math.PI / 16) : .5;
    frame.hueMix = smooth(frame.hueMix, hue, step, quiet ? 1.2 : .8);
    return frame;
  }
  return { update, frame };
}

/** A bounded 2D layer, clipped to the outer 16% on either side. No extra audio or GPU context. */
export function createMusicEdges({ parent = globalThis.document?.body, doc = globalThis.document,
  photosafe = null, palette = () => ['#c9eceb', '#bca5e9'], tier = 'high' } = {}) {
  // perf (Firefox, 2026-10-27): the side wash is a horizontal gradient, the same down every row, so it is painted
  // into a ONE-row canvas stretched to the screen (a full-height gradient fillRect was half of this layer's cost);
  // the ribbons and particles keep the full canvas above it. Both sit in one layer whose opacity is the old
  // canvas opacity (group opacity: the same blend as one canvas).
  const state = createMusicEdgeState({ photosafe }), layer = doc?.createElement('div'), canvas = doc?.createElement('canvas');
  const washCanvas = doc?.createElement('canvas');
  const ctx = canvas?.getContext('2d'), wctx = washCanvas?.getContext('2d');
  let disposed = false, lastDraw = -Infinity, primary = '#c9eceb', accent = '#bca5e9';
  const colours = [rgb(primary), rgb(accent)];
  if (!layer || !canvas || !ctx || !wctx || !parent) return { update() {}, dispose() {}, canvas: null, get stats() { return { ...state.frame }; } };
  layer.className = 'music-edges'; layer.setAttribute('aria-hidden', 'true');
  Object.assign(layer.style, { position: 'fixed', zIndex: '1', pointerEvents: 'none', left: '0', top: '0',
    width: '100%', height: '100%', display: 'none' });
  for (const c of [washCanvas, canvas]) {
    Object.assign(c.style, { position: 'absolute', zIndex: '1', pointerEvents: 'none', left: '0', top: '0', width: '100%', height: '100%' });
    layer.appendChild(c);
  }
  parent.appendChild(layer);
  function update(dt, view = {}) {
    if (disposed) return;
    if (doc.hidden) { layer.style.display = 'none'; return; }
    if (dt > 0 && !view.paused && !view.audioReactive?.frozen && view.enabled !== false) {
      let pair; try { pair = palette(); } catch { pair = []; }
      const nextPrimary = colour(pair?.[0], primary), nextAccent = colour(pair?.[1], accent);
      if (nextPrimary !== primary) { primary = nextPrimary; colours[0] = rgb(primary); }
      if (nextAccent !== accent) { accent = nextAccent; colours[1] = rgb(accent); }
    }
    const red = colours.some(([r, g]) => r > 100 && r > g * 1.5);
    const f = state.update(dt, view, red ? 'red' : 'music-edges');
    if (!(f.level > .0001) || !(f.visibility > .001) || view.enabled === false) {
      layer.style.display = 'none'; lastDraw = -Infinity; return;
    }
    if (view.paused || view.audioReactive?.frozen || !(dt > 0)) return;
    layer.style.display = 'block'; layer.style.opacity = String(f.visibility);
    const w = Math.max(1, doc.defaultView?.innerWidth || parent.clientWidth || 1280);
    const h = Math.max(1, doc.defaultView?.innerHeight || parent.clientHeight || 720);
    const limit = tier === 'phone' ? 480 : tier === 'mid' ? 768 : 960, scale = Math.min(1, limit / Math.max(w, h));
    const bw = Math.max(1, Math.round(w * scale)), bh = Math.max(1, Math.round(h * scale));
    const resized = canvas.width !== bw || canvas.height !== bh;
    if (!resized && f.clock - lastDraw < 1 / 30) return;
    if (resized) { canvas.width = bw; canvas.height = bh; washCanvas.width = bw; washCanvas.height = 1; }
    lastDraw = f.clock;
    ctx.clearRect(0, 0, bw, bh); wctx.clearRect(0, 0, bw, 1);
    const quiet = view.gentle || view.reducedMotion;
    const band = bw * (.045 + f.level * .045), shake = f.shakePx * scale;
    const mixed = colours[0].map((v, i) => Math.round(v + (colours[1][i] - v) * f.hueMix));
    for (let side = 0; side < 2; side++) {
      ctx.save();
      ctx.beginPath(); ctx.rect(side ? bw * .84 : 0, 0, bw * .16, bh); ctx.clip();
      if (side) { ctx.translate(bw, 0); ctx.scale(-1, 1); }
      wctx.save();
      wctx.beginPath(); wctx.rect(side ? bw * .84 : 0, 0, bw * .16, 1); wctx.clip();
      if (side) { wctx.translate(bw, 0); wctx.scale(-1, 1); }
      const wash = wctx.createLinearGradient(0, 0, bw * .16, 0);
      wash.addColorStop(0, `rgba(${mixed},${.05 + f.level * .18})`);
      wash.addColorStop(.4, `rgba(${colours[side]},${f.level * .075})`);
      wash.addColorStop(1, `rgba(${mixed},0)`);
      wctx.fillStyle = wash; wctx.globalAlpha = Math.min(1, f.level * 4); wctx.fillRect(0, 0, bw * .16, 1);
      wctx.restore();
      // Layered gauzy ribbons carry the flex. The driving image and HUD stay still.
      for (let layer = 0; layer < 3; layer++) {
        ctx.beginPath(); ctx.moveTo(-8, 0);
        for (let j = 0; j <= 24; j++) {
          const y = j / 24, wave = quiet ? 0 : Math.sin(y * 12 + f.clock * .65 + layer * 1.7 + side) * band * .13;
          const x = band * (.25 + layer * .23) + wave + shake * (1 + layer * .5) + f.pulse * band * .1;
          ctx.lineTo(Math.max(0, x), y * bh);
        }
        ctx.lineTo(-8, bh); ctx.closePath(); ctx.fillStyle = `rgb(${colours[layer % 2]})`;
        ctx.globalAlpha = f.level * (.035 + layer * .012); ctx.fill();
      }
      const count = tier === 'phone' ? Math.min(12, f.particleCount) : f.particleCount;
      for (let i = side; i < count; i += 2) {
        const seed = fract(Math.sin(i * 91.17 + 8) * 43758.54), life = 2.8 + seed * 2.4;
        const age = fract(f.clock / life + seed), fade = Math.sin(Math.PI * age) ** 2;
        const x = bw * (.012 + seed * .09 + age * .018) + shake;
        const y = fract(seed * 7.13 - age * .22) * bh, r = Math.max(.7, (1 + seed * 1.8) * scale);
        ctx.fillStyle = `rgb(${colours[i % 2]})`; ctx.globalAlpha = f.level * fade * .6;
        ctx.beginPath(); ctx.moveTo(x, y - r * 2); ctx.lineTo(x + r, y); ctx.lineTo(x, y + r * 2); ctx.lineTo(x - r, y); ctx.closePath(); ctx.fill();
      }
      ctx.restore();
    }
  }
  return { update, canvas, get stats() { return { ...state.frame, primary, accent }; },
    get shiftU() { return state.frame.shakePx * state.frame.visibility / Math.max(1, doc.defaultView?.innerWidth || parent.clientWidth || 1280); },
    dispose() { if (disposed) return; disposed = true; layer.remove(); } };
}
