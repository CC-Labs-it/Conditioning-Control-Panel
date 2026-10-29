/* audio/samples.js - the recorded sound player on the shared context (WebAudio, no three).
 *
 * load() fetches and decodes every file in audio/sfx-bank.js once; a file that fails is left out
 * and its cues stay on the synth (audio/sfxmap.js resolvePlan). play() fires a one-shot through a
 * gain (and a panner for a bot's cue) into the bus it was given; loop() starts a seamless loop
 * between the bank's loopStartS and loopEndS and hands back its gain and rate params, so the
 * engine glides them. Every level is SFX_GAIN[id] times the caller's level. No speech: the files
 * are designed effects with no voices.
 */
import { SFX_BANK } from './sfx-bank.js';
import { SFX_GAIN } from './sfxmap.js';

const BASE = (() => { try { return new URL('./sfx/', import.meta.url).href; } catch (e) { return 'audio/sfx/'; } })();

/**
 * @param {AudioContext} ctx
 * @param {AudioNode} out
 * @param {{bank?: object, base?: string, fetchFn?: Function}} [o]
 */
export function createSamples(ctx, out, { bank = SFX_BANK, base = BASE, fetchFn = globalThis.fetch } = {}) {
  const buffers = new Map();
  const failed = new Set();
  let loading = null;

  function decode(ab) {
    return new Promise((res, rej) => {
      try {
        const p = ctx.decodeAudioData(ab, res, rej);
        if (p && typeof p.then === 'function') p.then(res, rej);
      } catch (e) { rej(e); }
    });
  }

  function load() {
    if (loading) return loading;
    if (typeof fetchFn !== 'function' || typeof ctx.decodeAudioData !== 'function') {
      for (const id of Object.keys(bank)) failed.add(id);
      return (loading = Promise.resolve(0));
    }
    loading = Promise.all(Object.entries(bank).map(async ([id, b]) => {
      try {
        const r = await fetchFn(base + b.file);
        if (!r || !r.ok) throw new Error('fetch ' + b.file);
        buffers.set(id, await decode(await r.arrayBuffer()));
      } catch (e) { failed.add(id); }
    })).then(() => buffers.size);
    return loading;
  }

  const has = (id) => buffers.has(id);
  const level = (id) => (SFX_GAIN[id] ?? 0.5);

  /** One-shot. at = delay in s from now. @returns {boolean} false when the file is not loaded */
  function play(id, { vol = 1, rate = 1, pan = 0, at = 0, dest = out } = {}) {
    const buf = buffers.get(id);
    if (!buf) return false;
    const t = ctx.currentTime + Math.max(0, at);
    const s = ctx.createBufferSource(), g = ctx.createGain();
    s.buffer = buf;
    try { s.playbackRate.value = rate; } catch (e) { /* no rate param */ }
    g.gain.value = Math.max(0, vol) * level(id);
    s.connect(g);
    if (pan && typeof ctx.createStereoPanner === 'function') {
      const pn = ctx.createStereoPanner(); pn.pan.value = Math.max(-1, Math.min(1, pan));
      g.connect(pn); pn.connect(dest);
    } else g.connect(dest);
    s.start(t);
    try { s.onended = () => { try { g.disconnect(); } catch (e) { /* gone */ } }; } catch (e) { /* none */ }
    return true;
  }

  /** A held seamless loop, silent until its gain is raised. @returns {{gain: AudioParam, rate: AudioParam, stop(): void}|null} */
  function loop(id, dest = out) {
    const buf = buffers.get(id), b = bank[id];
    if (!buf || !b) return null;
    const s = ctx.createBufferSource(), g = ctx.createGain();
    s.buffer = buf; s.loop = true;
    const dur = buf.duration || b.durS;
    const a = b.loopStartS ?? 0, e = b.loopEndS ?? dur;
    s.loopStart = a; s.loopEnd = e;
    g.gain.value = 0;
    s.connect(g); g.connect(dest);
    // each loop starts at a different point of its cycle so layers never phase together
    const off = a + ((id.length * 0.37) % 1) * (e - a);
    s.start(ctx.currentTime, off);
    return {
      gain: g.gain, rate: s.playbackRate, level: level(id),
      stop() { try { s.stop(); } catch (er) { /* stopped */ } try { g.disconnect(); } catch (er) { /* gone */ } },
    };
  }

  return { load, has, play, loop, get loaded() { return buffers.size; }, get failed() { return [...failed]; } };
}
