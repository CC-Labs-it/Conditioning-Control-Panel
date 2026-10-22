/* audio/bus.js - ONE AudioContext for the whole page (the voice rules of dtrh/engine/audioBus.js).
 *
 * One key, one clock, one variable: every voice routes through one master gain on one
 * context, the sim's beat clock is the only clock, and speedPct is the only variable the
 * mix follows (audio/engine.js). The context is made on the first user gesture (or the
 * Start click) and resumed on every later gesture, so nothing plays before the player
 * touched the page. Cues come from the audio/cues.js registry, subscribed by event name.
 * WebAudio oscillators only: no spoken words, no TTS, ever. reset() lets go of every held tone
 * and puts the pad back on a section chord (Restart).
 */
import { createEngine } from './engine.js';
import { AUDIO_CUES, makeSynth } from './cues.js';

const GESTURES = ['pointerdown', 'touchstart', 'keydown'];
const MASTER_GAIN = 0.8; // feel gap: audio.masterGain

/** @returns {{start(): Promise<void>, update(speedPct: number, flags?: object): void,
 *   cue(name: string, opts?: {volume?: number, pitch?: number}): void, reset(sectionIdx?: number): void,
 *   setPaused(on: boolean): void, dispose(): void}} */
export function createAudio(feel, bus, { gentle, win = globalThis.window, makeCtx = null } = {}) {
  let ctx = null, engine = null, syn = null, paused = false, dead = false;

  function ensure() {
    if (ctx || dead) return ctx;
    const AC = win && (win.AudioContext || win.webkitAudioContext);
    if (!makeCtx && !AC) { dead = true; return null; }
    try { ctx = makeCtx ? makeCtx() : new AC(); } catch (e) { dead = true; return null; }
    const master = ctx.createGain();
    master.gain.value = MASTER_GAIN;
    master.connect(ctx.destination);
    const sfx = ctx.createGain();
    sfx.connect(master);
    engine = createEngine(ctx, master, feel);
    syn = makeSynth(ctx, sfx, feel, engine);
    return ctx;
  }

  const unlock = () => {
    if (paused || !ensure()) return;
    if (ctx.state === 'suspended') ctx.resume().catch(() => {});
  };
  for (const ev of GESTURES) win?.addEventListener?.(ev, unlock, { passive: true });

  function play(name, payload, { volume = 1, pitch = 1 } = {}) {
    const fn = AUDIO_CUES[name];
    if (!fn || !ctx || paused || ctx.state !== 'running') return;
    syn.volume = volume; syn.pitch = pitch;
    try { fn(syn, payload || {}); } catch (e) { console.warn('[audio] cue', name, e); }
    syn.volume = 1; syn.pitch = 1;
  }

  const offs = Object.keys(AUDIO_CUES).map((name) => bus.on(name, (p) => play(name, p)));

  return {
    /** From the Start click: make or resume the context. */
    async start() {
      if (!ensure()) return;
      if (ctx.state === 'suspended') { try { await ctx.resume(); } catch (e) { /* stays silent */ } }
    },
    update(speedPct, flags = {}) {
      if (!engine || paused) return;
      engine.update(speedPct, flags);
    },
    cue(name, opts) { play(name, {}, opts); },
    /** Restart: drop held tones (edge hum, rescue tone) and move the pad to the spawn section. */
    reset(sectionIdx = 0) {
      syn?.releaseAll(0.1);
      engine?.chord(sectionIdx);
    },
    setPaused(on) {
      paused = !!on;
      if (!ctx) return;
      if (paused) ctx.suspend?.().catch?.(() => {});   // suspend freezes held tones too
      else ctx.resume?.().catch?.(() => {});
    },
    dispose() {
      offs.forEach((off) => off());
      for (const ev of GESTURES) win?.removeEventListener?.(ev, unlock);
      engine?.dispose();
      if (ctx) { try { ctx.close(); } catch (e) { /* gone */ } }
      ctx = engine = syn = null;
    },
    get state() { return ctx ? ctx.state : (dead ? 'unavailable' : 'not created'); },
  };
}
