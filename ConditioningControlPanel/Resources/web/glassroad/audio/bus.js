/* audio/bus.js - ONE AudioContext for the whole page (the voice rules of dtrh/engine/audioBus.js).
 *
 * One key, one clock, one variable: every voice routes through one master gain on one
 * context, the sim's beat clock is the only clock, and speedPct is the only variable the
 * mix follows (audio/engine.js). The context is made on the first user gesture (or the
 * Start click) and resumed on every later gesture, so nothing plays before the player
 * touched the page. Cues come from the audio/cues.js (Phase A) and audio/cues-b1.js (B1)
 * registries, subscribed by event name. WebAudio oscillators and noise only: no spoken words,
 * no synthetic speech, ever. reset() lets go of every held tone and puts the pad back on a
 * section chord (Restart).
 *
 * Graph: master (volume master) <- sfx (volume sfx) <- { bed (effect duck) <- engine, rivals; cues }
 *        master <- music (volume music) <- musicDuck (effect duck) <- audio/music.js
 *        master -> underwater lowpass -> underwater gain -> destination
 * B1 events are heard when they name racer 0 (full) or one of the nearest rivals from the last
 * update (quieter with distance, panned); far ones stay silent.
 *
 * Recorded sounds (audio/samples.js, files in audio/sfx/): the context's first ensure() starts
 * loading them. A cue with a sample plan (audio/sfxmap.js sfxPlan) plays its files when every one
 * decoded, else its synth figure plays as before; the engine and rival loops take over from the
 * saws once they decode. Samples ride the same sfx bus, levels and pans as the synth cues.
 */
import { createEngine } from './engine.js';
import { AUDIO_CUES, makeSynth } from './cues.js';
import { B1_CUES, UI_CUES } from './cues-b1.js';
import { createRivals } from './rivals.js';
import { createMusic } from './music.js';
import { mixGet, eventLevel } from './mix.js';
import { createSamples } from './samples.js';
import { sfxPlan, resolvePlan, jitterRate } from './sfxmap.js';
import { createUnderwater } from './underwater.js';
import { createWaterTravel } from './water-travel.js';

const GESTURES = ['pointerdown', 'touchstart', 'keydown'];
const clamp01 = (x) => (x > 0 ? (x < 1 ? x : 1) : 0);
// the local player's own screen and result events: always heard (their `by` names the thrower)
const LOCAL = new Set(['effect-start', 'effect-end', 'race-result']);

/** @returns {{start(): Promise<void>, update(speedPct: number, flags?: object, rivals?: object[]): void,
 *   cue(name: string, opts?: {volume?: number, pitch?: number}): void, reset(sectionIdx?: number): void,
 *   setPaused(on: boolean): void, setVolumes(v: {master?: number, music?: number, sfx?: number}): void,
 *   readonly music: object|null, dispose(): void}} */
export function createAudio(feel, bus, { gentle, win = globalThis.window, makeCtx = null, fetchFn = globalThis.fetch, musicFetchFn = fetchFn, laps = 3, sfxBase } = {}) {
  let ctx = null, engine = null, syn = null, rivals = null, music = null, paused = false, dead = false;
  let samples = null, samplesReady = Promise.resolve(0), plays = 0, lapCount = laps;
  const popSt = { rung: -1, t: -1e9 };
  let master = null, sfx = null, bed = null, musicBus = null, musicDuck = null, underwater = null, waterTravel = null;
  const vol = { master: 1, music: 1, sfx: 1 };
  const near = new Map();
  const g = (k) => mixGet(feel, k);

  const glide = (param, v, tc = g('volTcS')) => {
    try { param.setTargetAtTime(v, ctx.currentTime, tc); } catch (e) { param.value = v; }
  };
  function applyVolumes(now = false) {
    if (!ctx) return;
    const set = (p, v) => (now ? (p.value = v) : glide(p, v));
    set(master.gain, g('masterGain') * vol.master);
    set(sfx.gain, vol.sfx);
    set(musicBus.gain, g('musicGain') * vol.music);
  }

  function ensure() {
    if (ctx || dead) return ctx;
    const AC = win && (win.AudioContext || win.webkitAudioContext);
    if (!makeCtx && !AC) { dead = true; return null; }
    try { ctx = makeCtx ? makeCtx() : new AC(); } catch (e) { dead = true; return null; }
    master = ctx.createGain(); underwater = createUnderwater(ctx, ctx.destination); master.connect(underwater.input);
    sfx = ctx.createGain(); sfx.connect(master);
    bed = ctx.createGain(); bed.gain.value = 1; bed.connect(sfx);
    musicDuck = ctx.createGain(); musicDuck.gain.value = 1; musicDuck.connect(master);
    musicBus = ctx.createGain(); musicBus.connect(musicDuck);
    applyVolumes(true);
    engine = createEngine(ctx, bed, feel);
    waterTravel = createWaterTravel(ctx, bed);
    rivals = createRivals(ctx, bed, feel);
    syn = makeSynth(ctx, sfx, feel, engine);
    syn.duck = duck;
    try { music = createMusic(ctx, musicBus, feel, { fetchFn: musicFetchFn }); } catch (e) { music = null; }
    samples = createSamples(ctx, sfx, { fetchFn, ...(sfxBase ? { base: sfxBase } : {}) });
    samplesReady = samples.load().then((n) => { engine?.useSamples(samples); rivals?.useSamples(samples); return n; }).catch(() => 0);
    return ctx;
  }

  /** A screen effect landed: engine, rivals and music dip, then come back. */
  function duck(durationS = 0) {
    if (!ctx) return;
    const t = ctx.currentTime, inS = g('effectDuckInS');
    const hold = Math.min(Math.max(0, Number(durationS) || 0), g('effectDuckHoldMaxS'));
    for (const p of [bed.gain, musicDuck.gain]) {
      try {
        p.cancelScheduledValues(t);
        p.setTargetAtTime(g('effectDuck'), t, Math.max(inS / 3, 0.005));
        p.setTargetAtTime(1, t + inS + hold, Math.max(g('effectDuckOutS') / 3, 0.01));
      } catch (e) { p.value = 1; }
    }
  }

  const unlock = () => {
    if (paused || !ensure()) return;
    if (ctx.state === 'suspended') ctx.resume().catch(() => {});
  };
  for (const ev of GESTURES) win?.addEventListener?.(ev, unlock, { passive: true });

  /** The recorded plan for a cue, when every file it needs decoded. @returns {boolean} played */
  function sample(name, payload, volume, pitch, pan) {
    if (!samples || !samples.loaded) return false;
    const n = typeof lapCount === 'function' ? lapCount() : lapCount;
    const plan = sfxPlan(name, payload || {}, { feel, laps: n, now: ctx.currentTime, st: popSt });
    if (resolvePlan(plan, samples.has) !== 'sample') return false;
    for (const x of plan) {
      samples.play(x.id, { vol: x.vol * volume, rate: x.rate * pitch * jitterRate(x.id, plays++), pan, at: x.at });
    }
    return true;
  }

  function run(name, fn, payload, volume, pitch, pan, me) {
    if (sample(name, payload, volume, pitch, pan)) return;
    syn.volume = volume; syn.pitch = pitch; syn.pan = pan; syn.me = me;
    try { fn(syn, payload || {}); } catch (e) { console.warn('[audio] cue', name, e); }
    syn.volume = 1; syn.pitch = 1; syn.pan = 0; syn.me = true;
  }

  function play(name, payload, { volume = 1, pitch = 1 } = {}) {
    const fn = AUDIO_CUES[name] || B1_CUES[name];
    if (!fn || !ctx || paused || ctx.state !== 'running') return;
    run(name, fn, payload, volume, pitch, 0, true);
  }

  /** A B1 event: full for the player, by distance for a near bot, silent when far. */
  function playB1(name, payload) {
    if (!ctx || paused || ctx.state !== 'running') return;
    const lv = LOCAL.has(name) ? { me: true, level: 1, pan: 0 } : eventLevel(payload, near, g('nearCueM'), g('botCueMul'));
    if (!(lv.level > 0.01)) return;
    run(name, B1_CUES[name], payload, lv.level, 1, lv.pan, lv.me);
  }

  const offs = [
    ...Object.keys(AUDIO_CUES).map((name) => bus.on(name, (p) => play(name, p))),
    ...Object.keys(B1_CUES).filter((n) => !AUDIO_CUES[n]).map((name) => bus.on(name, (p) => playB1(name, p))),
  ];

  return {
    /** From the Start click: make or resume the context. */
    async start() {
      if (paused) return;
      if (!ensure()) return;
      if (ctx.state === 'suspended') { try { await ctx.resume(); } catch (e) { /* stays silent */ } }
    },
    /** rivals = racers.nearby(ai.audibleKarts, 80): [{ i, distM, pan, speedPct, boosting }] */
    update(speedPct, flags = {}, list = []) {
      if (!engine || paused) return;
      underwater?.update(flags.submerged);
      waterTravel?.update(speedPct, flags);
      engine.update(speedPct, flags);
      near.clear();
      for (const r of list || []) if (r && Number.isInteger(r.i)) near.set(r.i, r);
      let n = 3;
      try { n = feel.get('ai.audibleKarts'); } catch (e) { /* Phase A feel */ }
      rivals?.update(list || [], n);
    },
    /** Any cue by name; menu blips (UI_CUES) also play from a suspended, unpaused context. */
    cue(name, opts = {}) {
      if (UI_CUES[name]) {
        if (paused || !ensure()) return;
        if (ctx.state === 'suspended') ctx.resume?.().catch?.(() => {});
        if (ctx.state === 'closed') return;
        run(name, UI_CUES[name], {}, opts.volume ?? 1, opts.pitch ?? 1, 0, true);
        return;
      }
      play(name, {}, opts);
    },
    /** Master, music and sfx volumes, 0..1 each (the options card). */
    setVolumes({ master: m, music: mu, sfx: s } = {}) {
      if (m !== undefined) vol.master = clamp01(Number(m) || 0);
      if (mu !== undefined) vol.music = clamp01(Number(mu) || 0);
      if (s !== undefined) vol.sfx = clamp01(Number(s) || 0);
      applyVolumes();
    },
    /** Restart: drop held tones (edge hum, rescue tone, effect bed, slipstream air) and move the pad. */
    reset(sectionIdx = 0) {
      syn?.releaseAll(0.1);
      if (syn) syn.b1 = null;
      if (syn) syn.stoneRun = null;
      popSt.rung = -1; popSt.t = -1e9;
      engine?.chord(sectionIdx);
      rivals?.silence();
      near.clear();
      underwater?.reset();
      waterTravel?.reset();
      if (ctx) for (const p of [bed.gain, musicDuck.gain]) { try { p.cancelScheduledValues(ctx.currentTime); } catch (e) { /* none */ } glide(p, 1, 0.05); }
    },
    setPaused(on) {
      paused = !!on;
      if (!ctx) return;
      if (paused) ctx.suspend?.().catch?.(() => {});   // suspend freezes held tones and the music beat too
      else ctx.resume?.().catch?.(() => {});
    },
    get music() { return music; },
    get underwater() { return underwater?.inspect || Object.freeze({ available: false, submerged: 0 }); },
    get waterTravel() { return waterTravel?.inspect || Object.freeze({ available: false, wet: false, depth: 0 }); },
    /** Laps in this race (a number or a getter), so the last lap gets its own sound. */
    setLaps(n) { lapCount = n; },
    /** Resolves when every sound file loaded or failed (the count that loaded). */
    get samplesReady() { return samplesReady; },
    /** Which files loaded and which fell back to the synth. */
    get sfx() { return { loaded: samples ? samples.loaded : 0, failed: samples ? samples.failed : [], engine: !!engine?.recorded }; },
    dispose() {
      dead = true;
      offs.forEach((off) => off());
      for (const ev of GESTURES) win?.removeEventListener?.(ev, unlock);
      music?.dispose();
      rivals?.dispose();
      engine?.dispose();
      waterTravel?.dispose();
      underwater?.dispose();
      master?.disconnect?.();
      if (ctx) { try { ctx.close(); } catch (e) { /* gone */ } }
      ctx = engine = syn = rivals = music = samples = underwater = waterTravel = null;
    },
    get state() { return ctx ? ctx.state : (dead ? 'unavailable' : 'not created'); },
  };
}
