/* audio/cues.js - the cue registry: event name -> (syn, payload) => void.
 *
 * Short synthesised cues only (oscillators and noise). No speech of any kind: a line
 * with no recorded clip stays silent. A lane adds a sound by adding a key here, never by
 * editing bus.js. syn = { ctx, tone, noise, hold, release, isHeld, semis, engine, feel, volume, pitch, pan, me }.
 * B1 cues (items, contact, standings, menu) live in audio/cues-b1.js on the same synth.
 * Pitched cues sit on the root of audio/engine.js; impacts play now and are never quantised.
 */
import { SOUND, CHORDS, noiseBuffer } from './engine.js';
import { ladderSemis } from './mix.js';
import { playPhraseGate } from './phraseGates.js';
import { playPressThud } from './pressThud.js';
import { trapTickGain } from '../render/trapTelegraph.js';
import { playAirWordHop, playAirWordLand } from './airWordHop.js';

export function makeSynth(ctx, out, feel, engine) {
  let nbuf = null;
  const held = new Map();
  // syn.pan (-1..1) places a cue left or right (a bot's event); 0 goes straight to out
  const dest = () => {
    if (!syn.pan || typeof ctx.createStereoPanner !== 'function') return out;
    const p = ctx.createStereoPanner(); p.pan.value = Math.max(-1, Math.min(1, syn.pan)); p.connect(out);
    return p;
  };
  const syn = {
    ctx, feel, engine, volume: 1, pitch: 1, pan: 0, me: true,
    semis: (n) => SOUND.rootHz * Math.pow(2, n / 12),
    /** One enveloped oscillator. glide = end frequency; at = start delay in s. */
    tone(freq, { type = 'triangle', dur = 0.15, vol = 0.15, attack = 0.005, glide = 0, at = 0 } = {}) {
      const t = ctx.currentTime + Math.max(0, at), o = ctx.createOscillator(), g = ctx.createGain();
      o.type = type; o.frequency.setValueAtTime(freq * syn.pitch, t);
      if (glide) o.frequency.exponentialRampToValueAtTime(glide * syn.pitch, t + dur);
      g.gain.setValueAtTime(0, t);
      g.gain.linearRampToValueAtTime(vol * syn.volume, t + attack);
      g.gain.setTargetAtTime(0, t + attack, Math.max(dur / 4, 0.01));
      o.connect(g); g.connect(dest()); o.start(t); o.stop(t + attack + dur + 0.1);
    },
    /** Filtered noise burst; sweep = end centre frequency; at = start delay in s. */
    noise({ dur = 0.2, vol = 0.1, freq = 1200, q = 1, sweep = 0, at = 0 } = {}) {
      const t = ctx.currentTime + Math.max(0, at);
      if (!nbuf) nbuf = noiseBuffer(ctx);
      const s = ctx.createBufferSource(), f = ctx.createBiquadFilter(), g = ctx.createGain();
      s.buffer = nbuf; f.type = 'bandpass'; f.Q.value = q; f.frequency.setValueAtTime(freq, t);
      if (sweep) f.frequency.exponentialRampToValueAtTime(sweep, t + dur);
      g.gain.setValueAtTime(vol * syn.volume, t);
      g.gain.setTargetAtTime(0, t + dur * 0.3, Math.max(dur / 3, 0.01));
      s.connect(f); f.connect(g); g.connect(dest()); s.start(t); s.stop(t + dur + 0.2);
    },
    /** A held tone under a key until release(key). lfoHz/lfoCents wobble the pitch; lowpassHz darkens it. */
    hold(key, freq, { type = 'sine', vol = 0.08, attack = 0.15, lfoHz = 0, lfoCents = 0, lowpassHz = 0 } = {}) {
      if (held.has(key)) return;
      const t = ctx.currentTime, o = ctx.createOscillator(), g = ctx.createGain();
      o.type = type; o.frequency.value = freq; g.gain.setValueAtTime(0, t);
      g.gain.setTargetAtTime(vol * syn.volume, t, attack / 3);
      const extra = [];
      let head = o;
      if (lowpassHz > 0) {
        const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = lowpassHz;
        o.connect(f); head = f;
      }
      if (lfoHz > 0 && lfoCents > 0 && o.detune) {
        const l = ctx.createOscillator(), lg = ctx.createGain();
        l.type = 'sine'; l.frequency.value = lfoHz; lg.gain.value = lfoCents;
        l.connect(lg); lg.connect(o.detune); l.start(t); extra.push(l);
      }
      head.connect(g); g.connect(dest()); o.start(t);
      held.set(key, { o, g, extra });
    },
    /** Held band-passed noise under a key (slipstream air) until release(key). */
    holdNoise(key, { freq = 1800, q = 0.7, vol = 0.03, attack = 0.2 } = {}) {
      if (held.has(key)) return;
      const t = ctx.currentTime;
      if (!nbuf) nbuf = noiseBuffer(ctx);
      const s = ctx.createBufferSource(), f = ctx.createBiquadFilter(), g = ctx.createGain();
      s.buffer = nbuf; s.loop = true; f.type = 'bandpass'; f.Q.value = q; f.frequency.value = freq;
      g.gain.setValueAtTime(0, t);
      g.gain.setTargetAtTime(vol * syn.volume, t, attack / 3);
      s.connect(f); f.connect(g); g.connect(dest()); s.start(t);
      held.set(key, { o: s, g, extra: [] });
    },
    release(key, s = 0.3) {
      const h = held.get(key);
      if (!h) return;
      held.delete(key);
      const t = ctx.currentTime;
      h.g.gain.cancelScheduledValues(t);
      h.g.gain.setTargetAtTime(0, t, Math.max(s / 3, 0.01));
      h.o.stop(t + s + 0.2);
      for (const x of h.extra || []) { try { x.stop(t + s + 0.2); } catch (e) { /* stopped */ } }
    },
    isHeld: (key) => held.has(key),
    releaseAll(s = 0.1) { for (const k of [...held.keys()]) syn.release(k, s); },
  };
  return syn;
}

const j = (syn, k) => syn.feel.get('juice.' + k);
const chordOf = (i) => CHORDS[((i % CHORDS.length) + CHORDS.length) % CHORDS.length];

export const AUDIO_CUES = {
  'air-word-launch': playAirWordHop,
  'air-word-hop': playAirWordHop,
  'press-thud': playPressThud,
  'trap-tick'(syn, p = {}) {
    const gain = trapTickGain(p);
    if (gain > .01) syn.tone(syn.semis(3 * (5 - Math.min(4, Math.max(1, p.step || 4)))), { dur: .07, vol: .06 * gain });
  },
  'phrase-gate': playPhraseGate,
  countdown(syn) { syn.tone(syn.semis(0), { dur: 0.12, vol: 0.16 }); },
  go(syn) {
    syn.tone(syn.semis(12), { dur: 0.4, vol: 0.18 });
    syn.tone(syn.semis(19), { dur: 0.4, vol: 0.1 });
  },
  'launch-boost'(syn) {
    syn.noise({ dur: 0.5, vol: 0.12, freq: 500, sweep: 3200 });
    syn.tone(syn.semis(7), { dur: 0.25, vol: 0.1, glide: syn.semis(19) });
  },
  'drift-start'(syn) { syn.noise({ dur: 0.12, vol: 0.06, freq: 2600, q: 2 }); },
  'drift-tier'(syn, { tier = 1 }) { syn.tone(syn.semis(12 + [0, 4, 7][Math.min(tier, 3) - 1]), { dur: 0.1, vol: 0.12 }); },
  'drift-release'(syn, { tier = 0 }) {
    if (tier > 0) syn.tone(syn.semis(12 + 7), { dur: j(syn, 'sparkDieS'), vol: 0.1, glide: syn.semis(7) });
  },
  'drift-cancel'(syn) { syn.tone(syn.semis(-5), { dur: 0.08, vol: 0.06 }); },
  'boost-start'(syn, { kind }) {
    if (kind === 'launch') return;
    if (kind === 'slip') {                         // a slipstream fire: airy, a lift of a fifth
      syn.noise({ dur: 0.4, vol: 0.07, freq: 1400, q: 0.6, sweep: 4200 });
      syn.tone(syn.semis(7), { dur: 0.18, vol: 0.05, glide: syn.semis(14) });
    } else if (kind === 'bubble') {                // a popped track bubble: a small sparkle
      syn.noise({ dur: 0.18, vol: 0.04, freq: 3000, q: 2, sweep: 5000 });
    } else if (kind === 'surge') {                 // Surge: the big whoosh plays on item-fire; this is its tail
      syn.noise({ dur: 0.6, vol: 0.08, freq: 500, sweep: 3600 });
    } else syn.noise({ dur: 0.45, vol: 0.1, freq: 600, sweep: 2800 });
  },
  hop(syn) { syn.tone(syn.semis(7), { dur: 0.08, vol: 0.06, glide: syn.semis(12) }); },
  stone(syn, { id, field = null } = {}) {
    if (id == null) return;
    const time = syn.ctx.currentTime, previous = syn.stoneRun;
    const continuing = previous && previous.field === field && time >= previous.time && time - previous.time < 8;
    if (continuing && previous.id === id) return;
    const rung = continuing ? Math.min(19, previous.rung + 1) : 0;
    syn.stoneRun = { id, field, rung, time };
    // The same pentatonic climb as a Breakout chain, with a rounded mallet and a glass tail.
    const hz = syn.semis(ladderSemis(rung));
    syn.tone(hz, { type: 'triangle', dur: .32, vol: .12, attack: .007 });
    syn.tone(hz * 2, { type: 'sine', dur: .46, vol: .035, attack: .009 });
    syn.tone(hz * 3, { type: 'sine', dur: .13, vol: .012, attack: .004 });
  },
  land(syn, { tier = 0, airWord = false }) {
    if (airWord) { playAirWordLand(syn); return; }
    if (tier < 2) return;
    const loud = tier >= 3 ? 1 : 0.55;
    syn.tone(syn.semis(-24), { type: 'sine', dur: 0.2, vol: 0.3 * loud, glide: syn.semis(-31) });
    syn.noise({ dur: 0.15, vol: 0.12 * loud, freq: 220, q: 0.7 });
  },
  wall(syn, { dv = 0 }) {
    const v = Math.min(1, dv / syn.feel.get('wall.cueFullDvMps'));
    if (v <= 0.02) return;
    syn.tone(syn.semis(-26), { type: 'sine', dur: 0.12, vol: 0.28 * v, glide: syn.semis(-33) });
    syn.noise({ dur: 0.1, vol: 0.1 * v, freq: 400, q: 0.8 });
  },
  'edge-enter'(syn) { syn.hold('edge', syn.semis(-5), { vol: 0.05 }); },
  'edge-exit'(syn) { syn.release('edge', j(syn, 'edgeGlowFadeS')); },
  drop(syn) {
    syn.stoneRun = null;
    syn.release('edge', 0.1);
    syn.hold('rescue', syn.semis(0), { vol: 0.1, attack: 0.2 });
  },
  'rescue-release'(syn) {
    syn.release('rescue', j(syn, 'duckLiftS'));
    syn.tone(syn.semis(12), { dur: 0.3, vol: 0.08 });
  },
  pad(syn) { syn.tone(syn.semis(7), { dur: 0.2, vol: 0.12, glide: syn.semis(19) }); },
  section(syn, { to = 0 }) {
    syn.engine?.chord(to);
    for (const s of chordOf(to)) syn.tone(syn.semis(s + 12), { dur: 0.8, vol: 0.05, attack: 0.08 });
  },
  lap(syn) {
    syn.stoneRun = null;
    syn.tone(syn.semis(12), { dur: 0.15, vol: 0.1 });
    syn.tone(syn.semis(16), { dur: 0.25, vol: 0.08 });
  },
  finish(syn) {
    const swell = j(syn, 'finishSwellS');
    syn.engine?.chord(0);
    for (const s of chordOf(0)) syn.tone(syn.semis(s + 12), { dur: swell, vol: 0.07, attack: swell * 0.6 });
  },
};

export default AUDIO_CUES;
