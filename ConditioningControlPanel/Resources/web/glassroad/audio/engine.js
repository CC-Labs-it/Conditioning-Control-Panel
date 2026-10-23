/* audio/engine.js - the synthesised engine and the section pad (WebAudio only).
 *
 * Engine: two detuned saws through a low-pass plus band-passed noise. speedPct, the one
 * variable, drives the engine pitch, the engine and pad low-pass and the noise colour.
 * Drift charge lifts the pitch juice.driftPitchSemis (a fifth) over three tiers; a boost
 * opens the low-pass and it eases back over boost.tailTcS. The duck pulls engine and pad
 * down to a floor while the rescue holds its one tone. Every change is a setTargetAtTime
 * glide, never a step.
 *
 * Recorded engine (useSamples): once the three loops (idle hum, cruise, high-speed air) decode,
 * they take over and the saws fall silent; the saws stay as the fallback when a file fails.
 * The loops crossfade by speedPct and their pitch travels at most audio.engPitchTravelSemis.
 */
import { engineLayers, enginePitchSemis, engineCutHz, semisToRate } from './sfxmap.js';
import { mixGet } from './mix.js';

// feel gap: sound design numbers have no feel.json group yet (reported).
export const SOUND = {
  idleHz: 55, rangeOct: 1.5, detuneCents: 9, airSemis: 2,
  cutLowHz: 380, cutHighHz: 1900, boostCutMul: 2.4,
  noiseLowHz: 300, noiseHighHz: 2800, noiseGain: [0.015, 0.06],
  gain: [0.07, 0.13], duckFloor: 0.12, glideS: 0.05,
  padGain: 0.035, padLowHz: 260, padHighHz: 2400, rootHz: 220,
};
/** Section chords, semitones from rootHz (one per section, wraps). */
export const CHORDS = [[0, 3, 7], [-4, 0, 3], [-2, 2, 5]];

const hz = (semis) => SOUND.rootHz * Math.pow(2, semis / 12);
const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);

export function noiseBuffer(ctx) {
  const len = Math.floor(ctx.sampleRate || 44100);
  const buf = ctx.createBuffer(1, len, ctx.sampleRate || 44100);
  const d = buf.getChannelData(0);
  for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
  return buf;
}

export function createEngine(ctx, out, feel) {
  const glide = (param, v, tc = SOUND.glideS) => {
    try { param.setTargetAtTime(v, ctx.currentTime, tc); } catch (e) { param.value = v; }
  };
  const duck = ctx.createGain(); duck.connect(out);
  const gain = ctx.createGain(); gain.gain.value = 0; gain.connect(duck);
  const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = SOUND.cutLowHz; lp.Q.value = 0.8; lp.connect(gain);
  const oscs = [-1, 1].map((sign) => {
    const o = ctx.createOscillator(); o.type = 'sawtooth';
    o.frequency.value = SOUND.idleHz; o.detune.value = sign * SOUND.detuneCents;
    o.connect(lp); o.start(); return o;
  });
  const nz = ctx.createBufferSource(); nz.buffer = noiseBuffer(ctx); nz.loop = true;
  const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = SOUND.noiseLowHz; bp.Q.value = 0.9;
  const nzGain = ctx.createGain(); nzGain.gain.value = 0;
  nz.connect(bp); bp.connect(nzGain); nzGain.connect(gain); nz.start();

  // the pad: three soft voices on the section chord, behind a speed-driven low-pass
  const padLp = ctx.createBiquadFilter(); padLp.type = 'lowpass'; padLp.frequency.value = SOUND.padLowHz; padLp.connect(duck);
  const padGain = ctx.createGain(); padGain.gain.value = 0; padGain.connect(padLp);
  const voices = CHORDS[0].map((s) => {
    const o = ctx.createOscillator(); o.type = 'triangle'; o.frequency.value = hz(s);
    o.connect(padGain); o.start(); return o;
  });

  // the recorded engine: three loops through one low-pass into the same duck
  let rec = null;
  const LAYERS = ['idle', 'cruise', 'air'];
  function useSamples(samples) {
    if (rec || !samples || !LAYERS.every((k) => samples.has('engine-' + k))) return false;
    const lp2 = ctx.createBiquadFilter(); lp2.type = 'lowpass'; lp2.Q.value = 0.5; lp2.frequency.value = mixGet(feel, 'engLpLowHz');
    const g2 = ctx.createGain(); g2.gain.value = mixGet(feel, 'engSampleGain');
    lp2.connect(g2); g2.connect(duck);
    const v = {};
    for (const k of LAYERS) v[k] = samples.loop('engine-' + k, lp2);
    if (!LAYERS.every((k) => v[k])) { for (const k of LAYERS) v[k]?.stop(); try { g2.disconnect(); } catch (e) { /* gone */ } return false; }
    rec = { lp: lp2, v };
    glide(gain.gain, 0, 0.2);                      // the saws step aside (they keep running as the fallback)
    return true;
  }

  let on = false;
  const layersOut = { idle: 0, cruise: 0, air: 0 };   // engineLayers fills it in place every frame
  function update(speedPct, flags = {}) {
    const maxPct = feel.get('speed.maxPct');
    const p = Math.max(0, Math.min(speedPct || 0, maxPct));
    if (!on) { on = true; glide(padGain.gain, SOUND.padGain, 0.8); }
    if (rec) {
      const tc = mixGet(feel, 'engGlideS');
      const w = engineLayers(p, flags, feel, layersOut), rate = semisToRate(enginePitchSemis(p, flags, feel));
      const road = flags.onRoad === false ? 0.7 : 1;
      for (const k of LAYERS) { glide(rec.v[k].gain, w[k] * rec.v[k].level * road, tc); glide(rec.v[k].rate, rate, tc); }
      glide(rec.lp.frequency, engineCutHz(p, flags, feel), flags.boosting ? feel.get('boost.riseS') : feel.get('boost.tailTcS'));
    }
    let semis = 0;
    if (flags.drifting && flags.driftTier > 0) semis += (Math.min(flags.driftTier, 3) / 3) * feel.get('juice.driftPitchSemis');
    if (flags.airborne) semis += SOUND.airSemis;
    const f = SOUND.idleHz * Math.pow(2, p * SOUND.rangeOct + semis / 12);
    for (const o of oscs) glide(o.frequency, f);
    const open = flags.boosting ? SOUND.boostCutMul : 1;
    const tc = flags.boosting ? feel.get('boost.riseS') : feel.get('boost.tailTcS');
    glide(lp.frequency, (SOUND.cutLowHz + (SOUND.cutHighHz - SOUND.cutLowHz) * clamp01(p)) * open, tc);
    glide(padLp.frequency, (SOUND.padLowHz + (SOUND.padHighHz - SOUND.padLowHz) * clamp01(p)) * (flags.boosting ? 1.5 : 1), tc);
    glide(bp.frequency, SOUND.noiseLowHz + (SOUND.noiseHighHz - SOUND.noiseLowHz) * clamp01(p));
    const road = flags.onRoad === false ? 0.5 : 1;
    glide(nzGain.gain, (SOUND.noiseGain[0] + (SOUND.noiseGain[1] - SOUND.noiseGain[0]) * clamp01(p)) * road);
    glide(gain.gain, rec ? 0 : SOUND.gain[0] + (SOUND.gain[1] - SOUND.gain[0]) * clamp01(p));
    glide(duck.gain, 1 - clamp01(flags.duck || 0) * (1 - SOUND.duckFloor));
  }

  /** Move the pad to a section chord over ~0.4 s. */
  function chord(idx) {
    const c = CHORDS[((idx % CHORDS.length) + CHORDS.length) % CHORDS.length];
    voices.forEach((o, i) => glide(o.frequency, hz(c[i]), 0.15));
  }

  function dispose() {
    for (const o of [...oscs, ...voices, nz]) { try { o.stop(); } catch (e) { /* already stopped */ } }
    if (rec) for (const k of LAYERS) rec.v[k].stop();
    try { duck.disconnect(); } catch (e) { /* gone */ }
  }

  return { update, chord, dispose, useSamples, get recorded() { return !!rec; } };
}
