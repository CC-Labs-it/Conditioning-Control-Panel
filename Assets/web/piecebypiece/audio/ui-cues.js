/* ============================================================================
 * audio/ui-cues.js - menu and screen sounds for the front door, the ladder and
 * the pictures picker (2026-10-14 polish pass; owner: "0 juice or feel here,
 * and 0 sfx").
 *
 * CONTRACT (fixed): the door lane calls these names only. Every call is safe
 * before the audio context exists and while sound is off (a no-op), never
 * throws, never speaks.
 *   hover()        a soft pluck when the pointer lands on a button; a sweep
 *                  across buttons climbs a little tune, a pause starts it over
 *   press()        a button going down: a wooden knock and a click
 *   open()         a screen arrives: a swish up and two notes rising
 *   back()         Back / Escape leaves a screen: the same swish down, softer
 *   select()       a choice chip turns on: a bubble pop and a note
 *   tick()         one step of a number roll; a run of ticks climbs the scale
 *   stamp()        a stamp lands (+N, new best, a result word): thud, slap, chime
 *   error()        a refused action: two soft notes stepping down, muffled
 *   setVolume(v)   0..1; follows the page's sfx volume by itself (onPresentation)
 *
 * The house sound rules (docs/JUICE-PLAYBOOK.md section 5): ONE KEY (every note
 * is C5 pentatonic, so nothing the player triggers can clash), bodies play at
 * once, quiet and short (no voice above UI_SOUND.maxGain, none longer than
 * UI_SOUND.maxSec), variants so a repeat never grates (small pitch and colour
 * jitter, a rotating rung), hover and tick rate-limited. Synthesised on the
 * spot like audio/sfx.js and audio/react-cues.js: oscillators, one noise buffer,
 * an envelope. No files, no speech.
 *
 * The plan (uiCuePlan) is pure, so smoke/ui-cues-smoke.mjs reads every cue's
 * voices in node. The player owns one small AudioContext of its own, made on
 * the first pointerdown / keydown (browsers refuse one any other way); the menu
 * keeps sounding while the board's own sfx are hushed by the Esc pause. A
 * hidden tab plays nothing.
 * ==========================================================================*/

import { onPresentation } from '../game/preferences.js';

export const UI_SOUND = Object.freeze({
  rootHz: 523.25,     // C5, the house root (Breakout, the launcher melody)
  maxGain: 0.11,      // no voice louder than this
  maxSec: 0.45,       // and none longer, tail included
  volume: 0.6,        // until the page says otherwise
  hoverGapMs: 55,     // hover: at most one pluck per this
  hoverRunMs: 650,    // a pause this long starts the hover tune over
  tickGapMs: 26,      // tick: at most one per this
  tickRunMs: 380,     // a pause this long starts the climb over
  jitter: 0.025,      // pitch variation per voice, each way (2.5 percent)
});

/** Pentatonic degrees of the octave: no semitone in it, so no clash. */
export const PENTA = Object.freeze([0, 2, 4, 7, 9]);
/** Rung n of C5 pentatonic (0 = C5, 5 = C6, -1 = A4). */
export function pentaHz(n) {
  const k = Math.round(Number(n) || 0);
  const oct = Math.floor(k / 5), deg = ((k % 5) + 5) % 5;
  return UI_SOUND.rootHz * 2 ** ((12 * oct + PENTA[deg]) / 12);
}
/** The hover tune: a sweep across a row of buttons walks it, up and back. */
export const HOVER_RUNGS = Object.freeze([2, 3, 4, 5, 6, 5, 4, 3]);

const r01 = (rand) => { const v = Number(rand()); return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0.5; };

/**
 * The voices one cue plays. Pure: `step` is the run position (hover, tick,
 * select), `rand` is injectable.
 * A voice: { wave: 'sine'|'triangle'|'noise', hz, to, at, sec, gain, filter, filterHz, attack }
 *   noise voices take hz/to as their filter sweep.
 * @returns {Array<object>} empty for an unknown name
 */
export function uiCuePlan(name, { step = 0, rand = Math.random } = {}) {
  const v = [];
  const J = UI_SOUND.jitter;
  const wob = () => 1 + (r01(rand) * 2 - 1) * J;
  const add = (o) => v.push({ to: o.hz, at: 0, attack: 0.006, filter: null, filterHz: null, ...o });
  const s = Math.max(0, step | 0);
  switch (name) {
    case 'hover': {
      const hz = pentaHz(HOVER_RUNGS[s % HOVER_RUNGS.length]);
      add({ wave: 'sine', hz, sec: 0.09, gain: 0.022, attack: 0.004, filter: 'lowpass', filterHz: 2600 });
      add({ wave: 'triangle', hz: hz * 2, sec: 0.045, gain: 0.006, attack: 0.003 });
      break;
    }
    case 'press': {
      const k = wob();
      add({ wave: 'triangle', hz: 250 * k, to: 140 * k, sec: 0.075, gain: 0.075, filter: 'lowpass', filterHz: 1400, attack: 0.002 });
      add({ wave: 'noise', hz: 2100 + r01(rand) * 700, sec: 0.024, gain: 0.05, filter: 'bandpass', attack: 0.001 });
      break;
    }
    case 'select': {
      const k = wob();
      add({ wave: 'sine', hz: 360 * k, to: 820 * k, sec: 0.06, gain: 0.05, attack: 0.003 });
      add({ wave: 'triangle', hz: pentaHz(5 + (s % 3)), at: 0.025, sec: 0.17, gain: 0.038, attack: 0.004 });
      add({ wave: 'noise', hz: 3200, sec: 0.018, gain: 0.025, filter: 'highpass', attack: 0.001 });
      break;
    }
    case 'open': {
      add({ wave: 'noise', hz: 480, to: 2600, sec: 0.19, gain: 0.034, filter: 'bandpass', attack: 0.07 });
      add({ wave: 'triangle', hz: pentaHz(0), at: 0.05, sec: 0.16, gain: 0.04 });
      add({ wave: 'triangle', hz: pentaHz(3), at: 0.12, sec: 0.22, gain: 0.042 });
      add({ wave: 'sine', hz: pentaHz(8), at: 0.12, sec: 0.18, gain: 0.01 });
      break;
    }
    case 'back': {
      add({ wave: 'noise', hz: 2400, to: 520, sec: 0.16, gain: 0.028, filter: 'bandpass', attack: 0.02 });
      add({ wave: 'triangle', hz: pentaHz(3), at: 0.02, sec: 0.12, gain: 0.034, filter: 'lowpass', filterHz: 1800 });
      add({ wave: 'triangle', hz: pentaHz(0), at: 0.085, sec: 0.18, gain: 0.032, filter: 'lowpass', filterHz: 1500 });
      break;
    }
    case 'tick': {
      const hz = pentaHz(3 + Math.min(s, 9));
      add({ wave: 'triangle', hz, sec: 0.035, gain: 0.026, attack: 0.002, filter: 'lowpass', filterHz: 3400 });
      add({ wave: 'noise', hz: 5200, sec: 0.012, gain: 0.012, filter: 'highpass', attack: 0.001 });
      break;
    }
    case 'stamp': {
      const k = wob();
      add({ wave: 'sine', hz: 150 * k, to: 55, sec: 0.15, gain: 0.11, filter: 'lowpass', filterHz: 420, attack: 0.003 });
      add({ wave: 'noise', hz: 1300 * k, to: 480, sec: 0.07, gain: 0.07, filter: 'bandpass', attack: 0.002 });
      add({ wave: 'triangle', hz: pentaHz(5), at: 0.035, sec: 0.32, gain: 0.03, attack: 0.006 });
      add({ wave: 'sine', hz: pentaHz(8), at: 0.055, sec: 0.32, gain: 0.018, attack: 0.006 });
      break;
    }
    case 'error': {
      add({ wave: 'triangle', hz: pentaHz(-1), to: pentaHz(-1) * 0.97, sec: 0.12, gain: 0.04, filter: 'lowpass', filterHz: 900, attack: 0.008 });
      add({ wave: 'triangle', hz: pentaHz(-3), to: pentaHz(-3) * 0.96, at: 0.11, sec: 0.18, gain: 0.036, filter: 'lowpass', filterHz: 760, attack: 0.008 });
      break;
    }
    default:
      return [];
  }
  return v;
}

export const CUE_NAMES = Object.freeze(['hover', 'press', 'open', 'back', 'select', 'tick', 'stamp', 'error']);
// a gesture's own cue may make the audio context; the rest only use one that is there
const GESTURES = new Set(['press', 'select', 'back', 'error']);

const clamp01 = (x) => Math.max(0, Math.min(1, Number(x) || 0));

/**
 * The player. Everything is injectable for a node smoke:
 *   createUiCues({ context: () => AudioContext-like, now: () => ms, doc, win, rand }) -> ui
 */
export function createUiCues({ context = null, now = () => Date.now(), rand = Math.random,
                               doc = (typeof document !== 'undefined' ? document : null),
                               win = (typeof window !== 'undefined' ? window : null) } = {}) {
  let ctx = null, out = null, noise = null;
  let vol = null;                 // null until setVolume: the page's settings until then
  const runs = { hover: { at: -1e9, step: -1 }, tick: { at: -1e9, step: -1 }, select: { step: -1 } };
  const log = [];
  const settingsVol = () => { const v = win?.PBP?.settings?.sfxVolume; return Number.isFinite(v) ? clamp01(v) : UI_SOUND.volume; };
  const volume = () => (vol === null ? settingsVol() : vol);

  function make() {
    if (ctx) return ctx;
    try {
      if (context) ctx = context();
      else {
        const Ctor = win && (win.AudioContext || win.webkitAudioContext);
        ctx = typeof Ctor === 'function' ? new Ctor() : null;
      }
    } catch { ctx = null; }
    if (!ctx) return null;
    out = ctx.createGain();
    out.gain.value = volume();
    out.connect(ctx.destination);
    const buf = ctx.createBuffer(1, Math.max(1, Math.floor(ctx.sampleRate * 0.5)), ctx.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    noise = buf;
    return ctx;
  }
  function wake() {
    const c = make();
    if (c && c.state === 'suspended' && c.resume) { try { const p = c.resume(); if (p && p.catch) p.catch(() => {}); } catch { /* not yet */ } }
  }

  function voice(o) {
    const t0 = ctx.currentTime + o.at;
    const sec = Math.min(o.sec, UI_SOUND.maxSec);
    const isNoise = o.wave === 'noise';
    const src = isNoise ? ctx.createBufferSource() : ctx.createOscillator();
    if (isNoise) { src.buffer = noise; src.loop = true; }
    else {
      src.type = o.wave;
      src.frequency.setValueAtTime(o.hz, t0);
      if (o.to !== o.hz) src.frequency.exponentialRampToValueAtTime(Math.max(1, o.to), t0 + sec);
    }
    let head = src;
    if (isNoise || o.filter) {
      const f = ctx.createBiquadFilter();
      f.type = o.filter || 'bandpass';
      if (isNoise) {
        f.frequency.setValueAtTime(o.hz, t0);
        if (o.to !== o.hz) f.frequency.exponentialRampToValueAtTime(Math.max(1, o.to), t0 + sec);
      } else f.frequency.value = o.filterHz || o.hz * 2;
      src.connect(f);
      head = f;
    }
    const env = ctx.createGain();
    const peak = Math.max(0.0002, Math.min(UI_SOUND.maxGain, o.gain));
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(peak, t0 + Math.min(o.attack, sec * 0.5));
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + sec);
    head.connect(env);
    env.connect(out);
    src.onended = () => { try { src.disconnect(); env.disconnect(); if (head !== src) head.disconnect(); } catch { /* gone */ } };
    src.start(t0);
    src.stop(t0 + sec + 0.02);
  }

  /** Which step of its run a cue is on, or -1 when the rate limit drops it. */
  function stepOf(name) {
    const t = now();
    if (name === 'hover' || name === 'tick') {
      const r = runs[name];
      const gap = name === 'hover' ? UI_SOUND.hoverGapMs : UI_SOUND.tickGapMs;
      const run = name === 'hover' ? UI_SOUND.hoverRunMs : UI_SOUND.tickRunMs;
      if (t - r.at < gap) return -1;
      r.step = t - r.at > run ? 0 : r.step + 1;
      r.at = t;
      return r.step;
    }
    if (name === 'select') { runs.select.step += 1; return runs.select.step; }
    return 0;
  }

  function play(name) {
    try {
      const step = stepOf(name);
      if (step < 0) return 0;
      if (doc && doc.hidden) return 0;
      if (volume() <= 0) return 0;
      if (GESTURES.has(name)) wake();
      if (!ctx || !out || ctx.state === 'closed') return 0;
      const plan = uiCuePlan(name, { step, rand });
      for (const o of plan) voice(o);
      log.push({ name, step, voices: plan.length, state: ctx.state });
      if (log.length > 32) log.shift();
      return plan.length;
    } catch { return 0; }
  }

  function setVolume(v) {
    vol = clamp01(v);
    if (out && ctx) { try { out.gain.setTargetAtTime(vol, ctx.currentTime, 0.02); } catch { out.gain.value = vol; } }
  }

  if (doc && doc.addEventListener) {
    doc.addEventListener('pointerdown', wake, { capture: true, passive: true });
    doc.addEventListener('keydown', wake, { capture: true, passive: true });
  }

  const api = { setVolume, wake, play, log: () => log.slice(), volume, ready: () => !!ctx };
  for (const n of CUE_NAMES) api[n] = () => { play(n); };
  return api;
}

export const ui = createUiCues();
// the page's volume, and its sound switch (volume 0), follow the Options panel and the host
try { onPresentation((p) => ui.setVolume(p.volume)); } catch { /* no preferences: the settings' volume */ }
export default ui;
