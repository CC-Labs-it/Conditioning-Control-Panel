/* audio/rivals.js - quiet engines for the nearest rival karts (ai.audibleKarts voices).
 *
 * Each voice is one saw and one detuned triangle through a low-pass, a gain and a panner.
 * Voices are made on the first rivals update and kept; allocVoices (audio/mix.js) gives each of
 * the nearest bots a slot and lets a bot keep its slot while it stays near. A racer's detune is
 * fixed, so each bot sounds like itself. Every change glides; a freed voice fades to silence.
 */
import { allocVoices, rivalVoice, mixGet } from './mix.js';

export function createRivals(ctx, out, feel) {
  let voices = [], slots = [];

  function make() {
    const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.Q.value = 0.7;
    lp.frequency.value = mixGet(feel, 'rivalCutLowHz');
    const g = ctx.createGain(); g.gain.value = 0;
    let pan = null;
    if (typeof ctx.createStereoPanner === 'function') { pan = ctx.createStereoPanner(); pan.pan.value = 0; }
    const a = ctx.createOscillator(); a.type = 'sawtooth';
    const b = ctx.createOscillator(); b.type = 'triangle';
    for (const o of [a, b]) { o.frequency.value = mixGet(feel, 'rivalIdleHz'); o.connect(lp); o.start(); }
    lp.connect(g);
    if (pan) { g.connect(pan); pan.connect(out); } else g.connect(out);
    return { a, b, lp, g, pan, who: -1 };
  }

  const glide = (param, v, tc) => {
    try { param.setTargetAtTime(v, ctx.currentTime, tc); } catch (e) { param.value = v; }
  };

  /** rivals = racers.nearby(n, 80). n = feel ai.audibleKarts. */
  function update(rivals, n) {
    n = Math.max(0, n | 0);
    if (!n || (!voices.length && !(rivals && rivals.length))) return;
    while (voices.length < n) { voices.push(make()); slots.push(-1); }
    allocVoices(slots, rivals, Math.min(n, voices.length));
    const tc = mixGet(feel, 'rivalGlideS');
    voices.forEach((v, k) => {
      const i = k < n ? slots[k] : -1;
      const r = i >= 0 ? rivals.find((x) => x.i === i) : null;
      if (!r) { glide(v.g.gain, 0, 0.12); v.who = -1; return; }
      const t = rivalVoice(r, feel);
      const jump = v.who !== i;                   // a new racer on this voice: set pitch at once, fade in
      v.who = i;
      for (const o of [v.a, v.b]) {
        if (jump) { try { o.frequency.setValueAtTime(t.hz, ctx.currentTime); } catch (e) { o.frequency.value = t.hz; } }
        else glide(o.frequency, t.hz, tc);
      }
      try { v.a.detune.value = t.detune; v.b.detune.value = t.detune - 7; } catch (e) { /* no detune param */ }
      glide(v.lp.frequency, t.cutHz, tc);
      glide(v.g.gain, t.gain, jump ? 0.15 : tc);
      if (v.pan) glide(v.pan.pan, t.pan, tc);
    });
  }

  function silence() { for (const v of voices) glide(v.g.gain, 0, 0.05); slots.fill(-1); voices.forEach((v) => { v.who = -1; }); }

  function dispose() {
    for (const v of voices) {
      for (const o of [v.a, v.b]) { try { o.stop(); } catch (e) { /* stopped */ } }
      try { v.g.disconnect(); } catch (e) { /* gone */ }
    }
    voices = []; slots = [];
  }

  return { update, silence, dispose, get slots() { return slots.slice(); } };
}
