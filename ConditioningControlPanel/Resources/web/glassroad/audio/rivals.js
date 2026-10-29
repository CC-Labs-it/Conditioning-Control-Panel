/* audio/rivals.js - quiet engines for the nearest rival karts (ai.audibleKarts voices).
 *
 * With the recorded engine loaded (useSamples), each voice is the player's own cruise and air
 * loops, crossfaded by that kart's speedPct, pitched a fixed few cents per racer, through a
 * low-pass, a gain and a panner, quieter than the player's (audio.rivalSampleGain). Until then,
 * or when a file fails, a voice is one saw and one detuned triangle (the synth fallback).
 * Voices are made on the first rivals update and kept; allocVoices (audio/mix.js) gives each of
 * the nearest bots a slot and lets a bot keep its slot while it stays near. A racer's detune is
 * fixed, so each bot sounds like itself. Every change glides; a freed voice fades to silence.
 */
import { allocVoices, rivalVoice, mixGet } from './mix.js';
import { engineLayers, enginePitchSemis, semisToRate } from './sfxmap.js';

export function createRivals(ctx, out, feel) {
  let voices = [], slots = [], samples = null;
  const layersOut = { idle: 0, cruise: 0, air: 0 }, flOut = { boosting: false };   // reused by every voice, every frame
  const rec = () => !!samples && samples.has('engine-cruise') && samples.has('engine-air');

  function make() {
    const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.Q.value = 0.7;
    lp.frequency.value = mixGet(feel, 'rivalCutLowHz');
    const g = ctx.createGain(); g.gain.value = 0;
    let pan = null;
    if (typeof ctx.createStereoPanner === 'function') { pan = ctx.createStereoPanner(); pan.pan.value = 0; }
    lp.connect(g);
    if (pan) { g.connect(pan); pan.connect(out); } else g.connect(out);
    const v = { lp, g, pan, who: -1, a: null, b: null, layers: null };
    if (rec()) {
      v.layers = { cruise: samples.loop('engine-cruise', lp), air: samples.loop('engine-air', lp) };
      if (v.layers.cruise && v.layers.air) return v;
      for (const l of Object.values(v.layers)) l?.stop();
      v.layers = null;
    }
    v.a = ctx.createOscillator(); v.a.type = 'sawtooth';
    v.b = ctx.createOscillator(); v.b.type = 'triangle';
    for (const o of [v.a, v.b]) { o.frequency.value = mixGet(feel, 'rivalIdleHz'); o.connect(lp); o.start(); }
    return v;
  }

  function drop(v) {
    for (const o of [v.a, v.b]) { if (o) { try { o.stop(); } catch (e) { /* stopped */ } } }
    if (v.layers) for (const l of Object.values(v.layers)) l.stop();
    try { v.g.disconnect(); } catch (e) { /* gone */ }
  }

  const glide = (param, v, tc) => {
    try { param.setTargetAtTime(v, ctx.currentTime, tc); } catch (e) { param.value = v; }
  };

  /** rivals = racers.nearby(n, 80). n = feel ai.audibleKarts. */
  function update(rivals, n) {
    n = Math.max(0, n | 0);
    if (!n || (!voices.length && !(rivals && rivals.length))) return;
    // the recorded loops arrived after these voices were made: swap them over
    if (rec()) voices = voices.map((v) => (v.layers ? v : (drop(v), make())));
    while (voices.length < n) { voices.push(make()); slots.push(-1); }
    allocVoices(slots, rivals, Math.min(n, voices.length));
    const tc = mixGet(feel, 'rivalGlideS');
    const w = layersOut, fl = flOut;
    voices.forEach((v, k) => {
      const i = k < n ? slots[k] : -1;
      const r = i >= 0 ? rivals.find((x) => x.i === i) : null;
      if (!r) { glide(v.g.gain, 0, 0.12); v.who = -1; return; }
      const t = rivalVoice(r, feel);
      const jump = v.who !== i;                   // a new racer on this voice: set pitch at once, fade in
      v.who = i;
      if (v.layers) {
        fl.boosting = !!r.boosting;
        engineLayers(r.speedPct, fl, feel, w);
        const rate = semisToRate(enginePitchSemis(r.speedPct, fl, feel) + t.detune / 100);
        for (const key of ['cruise', 'air']) {
          const l = v.layers[key];
          const lw = key === 'cruise' ? Math.max(w.cruise, w.idle) : w.air;
          glide(l.gain, lw * l.level, tc);
          if (jump) { try { l.rate.setValueAtTime(rate, ctx.currentTime); } catch (e) { l.rate.value = rate; } } else glide(l.rate, rate, tc);
        }
        const scale = mixGet(feel, 'rivalSampleGain') / Math.max(1e-6, mixGet(feel, 'rivalGain'));
        glide(v.lp.frequency, t.cutHz * 6, tc);    // the loops are already dark: the synth cut times six
        glide(v.g.gain, t.gain * scale, jump ? 0.15 : tc);
      } else {
        for (const o of [v.a, v.b]) {
          if (jump) { try { o.frequency.setValueAtTime(t.hz, ctx.currentTime); } catch (e) { o.frequency.value = t.hz; } }
          else glide(o.frequency, t.hz, tc);
        }
        try { v.a.detune.value = t.detune; v.b.detune.value = t.detune - 7; } catch (e) { /* no detune param */ }
        glide(v.lp.frequency, t.cutHz, tc);
        glide(v.g.gain, t.gain, jump ? 0.15 : tc);
      }
      if (v.pan) glide(v.pan.pan, t.pan, tc);
    });
  }

  function silence() { for (const v of voices) glide(v.g.gain, 0, 0.05); slots.fill(-1); voices.forEach((v) => { v.who = -1; }); }

  function dispose() {
    for (const v of voices) drop(v);
    voices = []; slots = [];
  }

  /** The recorded loops are ready: new and existing voices use them on the next update. */
  function useSamples(s) { samples = s; }

  return { update, silence, dispose, useSamples, get slots() { return slots.slice(); }, get recorded() { return voices.some((v) => !!v.layers); } };
}
