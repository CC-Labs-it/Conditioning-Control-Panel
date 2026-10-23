/* ============================================================================
 * audio/react-cues.js - one short sound per crowd reaction (owner, 2026-10-14:
 * "add sfx to the reactions").
 *
 * Drawn like the rest of the kit (audio/sfx.js): oscillators, the shared noise
 * buffer, an envelope. No files, no speech, no voice. Each cue is a plan of
 * voices first (pure, so a smoke can read it), then played on the game's
 * master through audio/sfx.js `react` { id, soft, pan }:
 *
 *   fire    a whoosh rising through a few crackles
 *   wow     a slide whistle up and back down a little
 *   laugh   four bouncy blips stepping down
 *   clap    three quick claps
 *   heart   a soft lub-dub under a warm two-note chime
 *   eyes    two bright plings, a peek
 *
 * Quiet on purpose: a reaction lands while somebody thinks about a move.
 * `soft` (what the room sends) plays at REACT_SOUND.softGain of a press.
 * ==========================================================================*/

export const REACT_SOUND = Object.freeze({
  softGain: 0.6,     // the room's reactions, against your own press
  maxGain: 0.13,     // no single voice louder than this
  maxSec: 0.6,       // and no cue longer than this, tail included
});

const r01 = (rand) => { const v = Number(rand()); return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0.5; };

/**
 * The voices one reaction plays. Pure (rand is injectable).
 * A voice: { wave: 'sine'|'triangle'|'square'|'sawtooth'|'noise', hz, to, at, sec, gain, filter, filterHz, attack }
 * @returns {Array<object>} empty for an unknown id
 */
export function reactCuePlan(id, rand = Math.random) {
  const v = [];
  const add = (o) => v.push({ to: o.hz, at: 0, attack: 0.008, filter: null, filterHz: null, ...o });
  switch (id) {
    case 'fire': {
      add({ wave: 'noise', hz: 320, to: 2600, sec: 0.38, gain: 0.07, filter: 'bandpass', attack: 0.12 });
      add({ wave: 'triangle', hz: 110, to: 220, sec: 0.3, gain: 0.03, filter: 'lowpass', filterHz: 600 });
      for (let i = 0; i < 5; i++) {
        const hz = 3000 + r01(rand) * 2500;
        add({ wave: 'noise', hz, at: 0.05 + i * 0.06 + r01(rand) * 0.03, sec: 0.02, gain: 0.03 + r01(rand) * 0.02, filter: 'highpass', attack: 0.002 });
      }
      break;
    }
    case 'wow':
      add({ wave: 'sine', hz: 420, to: 780, sec: 0.24, gain: 0.06, attack: 0.04 });
      add({ wave: 'sine', hz: 780, to: 640, at: 0.2, sec: 0.3, gain: 0.05, attack: 0.03 });
      add({ wave: 'triangle', hz: 280, to: 520, sec: 0.24, gain: 0.02, filter: 'lowpass', filterHz: 900, attack: 0.04 });
      break;
    case 'laugh': {
      const steps = [880, 784, 698, 622];
      steps.forEach((hz, i) => add({ wave: 'triangle', hz, to: hz * 0.9, at: i * 0.085, sec: 0.07, gain: 0.05, filter: 'lowpass', filterHz: 2400, attack: 0.005 }));
      break;
    }
    case 'clap':
      for (const at of [0, 0.12, 0.24]) {
        const j = r01(rand) * 0.012;
        add({ wave: 'noise', hz: 1100 + r01(rand) * 900, at: at + j, sec: 0.05, gain: 0.08, filter: 'bandpass', attack: 0.002 });
        add({ wave: 'noise', hz: 2600, at: at + j + 0.004, sec: 0.03, gain: 0.03, filter: 'highpass', attack: 0.002 });
      }
      break;
    case 'heart':
      add({ wave: 'sine', hz: 80, to: 55, sec: 0.14, gain: 0.12, filter: 'lowpass', filterHz: 200, attack: 0.01 });
      add({ wave: 'sine', hz: 72, to: 50, at: 0.17, sec: 0.14, gain: 0.09, filter: 'lowpass', filterHz: 200, attack: 0.01 });
      add({ wave: 'sine', hz: 659, at: 0.05, sec: 0.45, gain: 0.025, attack: 0.03 });
      add({ wave: 'sine', hz: 831, at: 0.09, sec: 0.45, gain: 0.02, attack: 0.03 });
      break;
    case 'eyes':
      add({ wave: 'sine', hz: 1320, to: 1400, sec: 0.09, gain: 0.035, attack: 0.005 });
      add({ wave: 'sine', hz: 1760, to: 1700, at: 0.13, sec: 0.14, gain: 0.03, attack: 0.005 });
      add({ wave: 'noise', hz: 6000, at: 0.1, sec: 0.12, gain: 0.012, filter: 'highpass', attack: 0.03 });
      break;
    default:
      return [];
  }
  return v;
}

/**
 * The player: renders a plan on `master`. `noise` is sfx.js's shared buffer.
 *   createReactCues({ ctx, master, noise, rand }) -> { play(id, { soft, pan }), cancel() }
 */
export function createReactCues({ ctx, master, noise, rand = Math.random }) {
  const sources = new Set();
  function voice(o, scale, pan) {
    const t0 = ctx.currentTime + o.at;
    const noiseVoice = o.wave === 'noise';
    const src = noiseVoice ? ctx.createBufferSource() : ctx.createOscillator();
    if (noiseVoice) { src.buffer = noise; src.loop = true; }
    else {
      src.type = o.wave;
      src.frequency.setValueAtTime(o.hz, t0);
      if (o.to !== o.hz) src.frequency.exponentialRampToValueAtTime(Math.max(1, o.to), t0 + o.sec);
    }
    let head = src;
    if (noiseVoice || o.filter) {
      const f = ctx.createBiquadFilter();
      f.type = o.filter || 'bandpass';
      if (noiseVoice) {
        f.frequency.setValueAtTime(o.hz, t0);
        if (o.to !== o.hz) f.frequency.exponentialRampToValueAtTime(Math.max(1, o.to), t0 + o.sec);
      } else f.frequency.value = o.filterHz || o.hz * 2;
      src.connect(f);
      head = f;
    }
    const env = ctx.createGain();
    const peak = Math.max(0.0002, Math.min(REACT_SOUND.maxGain, o.gain) * scale);
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(peak, t0 + Math.min(o.attack, o.sec * 0.5));
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + o.sec);
    head.connect(env);
    if (pan && typeof ctx.createStereoPanner === 'function') {
      const p = ctx.createStereoPanner();
      p.pan.value = Math.max(-1, Math.min(1, pan));
      env.connect(p); p.connect(master);
    } else env.connect(master);
    sources.add(src);
    src.onended = () => sources.delete(src);
    src.start(t0);
    src.stop(t0 + o.sec + 0.02);
  }
  return {
    /** Play one reaction. Returns how many voices started (0 for an unknown id). */
    play(id, { soft = false, pan = 0 } = {}) {
      const plan = reactCuePlan(id, rand);
      const scale = soft ? REACT_SOUND.softGain : 1;
      for (const o of plan) voice(o, scale, pan);
      return plan.length;
    },
    cancel() { for (const s of sources) { try { s.stop(); } catch { /* already ended */ } } sources.clear(); },
  };
}

export default createReactCues;
