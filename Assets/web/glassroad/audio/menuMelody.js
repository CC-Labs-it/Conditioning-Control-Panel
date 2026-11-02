// Original twelve-bar instrumental: a bright glass hook, bouncing chords and a soft pocket beat.
// One reusable sample loop on the existing context, without a timer or live oscillator bank.
export const MENU_MELODY = Object.freeze({ bpm: 120, beats: 48, sampleRate: 24000, fadeInS: 1.2, fadeOutS: .5 });
const CHORDS = [[62, 66, 69, 76], [62, 66, 69, 73], [59, 62, 66, 69],
  [61, 64, 69, 74], [59, 62, 66, 69], [61, 64, 69, 74]];
const ROOTS = [50, 47, 43, 45, 43, 45];
const HOOK_BEATS = [0, .75, 1.5, 2, 2.5, 3.5, 4, 4.75, 5.5, 6.5, 7, 7.5];
const HOOKS = [[74, 78, 81, 78, 76, 74, 76, 78, 81, 76, 74, 69],
  [71, 74, 78, 81, 78, 74, 71, 74, 76, 78, 74, 69],
  [71, 74, 79, 78, 76, 74, 71, 74, 76, 74, 69, 71],
  [73, 76, 81, 78, 76, 73, 69, 74, 76, 79, 78, 76],
  [74, 78, 81, 78, 76, 74, 76, 78, 81, 78, 76, 74],
  [73, 76, 81, 76, 78, 76, 73, 74, 76, 78, 73, 69]];
const hz = midi => 440 * 2 ** ((midi - 69) / 12);

/** Circular tails give the last bar a natural join into the first. No random or remote assets. */
export function renderMenuMelody(sampleRate = MENU_MELODY.sampleRate) {
  const rate = Math.max(8000, Math.min(48000, Math.round(sampleRate) || MENU_MELODY.sampleRate));
  const beatS = 60 / MENU_MELODY.bpm, length = Math.round(MENU_MELODY.beats * beatS * rate);
  const left = new Float32Array(length), right = new Float32Array(length);
  function note(midi, at, duration, level, pan, chord = false) {
    const frequency = hz(midi), start = Math.round(at * rate), count = Math.ceil(duration * rate);
    const l = Math.sqrt((1 - pan) / 2), r = Math.sqrt((1 + pan) / 2);
    for (let i = 0; i < count; i++) {
      const t = i / rate, phase = 2 * Math.PI * frequency * t;
      const attack = 1 - Math.exp(-t / (chord ? .014 : .011));
      const release = Math.min(1, (duration - t) / (chord ? .12 : .18));
      const envelope = attack * release * release * Math.exp(-t / (chord ? .17 : .29));
      const body = Math.sin(phase) + (chord ? .09 : .19) * Math.sin(phase * 2) * Math.exp(-t / .12);
      const sample = body * envelope * level, index = (start + i) % length;
      left[index] += sample * l; right[index] += sample * r;
    }
  }
  let seed = 17039;
  function percussion(at, kick) {
    const start = Math.round(at * rate), duration = kick ? .13 : .035, count = Math.ceil(duration * rate);
    let previous = 0;
    for (let i = 0; i < count; i++) {
      const t = i / rate, release = Math.min(1, (duration - t) / .015);
      seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
      const noise = seed / 2147483648 - 1;
      const phase = 2 * Math.PI * (48 * t + 67 * .022 * (1 - Math.exp(-t / .022)));
      const body = kick ? Math.sin(phase) : (noise - previous) * .5;
      const envelope = (1 - Math.exp(-t / .002)) * Math.exp(-t / (kick ? .035 : .009)) * release;
      const sample = body * envelope * (kick ? .030 : .007), index = (start + i) % length;
      left[index] += sample; right[index] += sample; previous = noise;
    }
  }
  for (let phrase = 0; phrase < CHORDS.length; phrase++) {
    const base = phrase * 8;
    for (let beat = 0; beat < 8; beat++) {
      const at = (base + beat) * beatS;
      note(ROOTS[phrase] + (beat % 4 === 2 ? 7 : 0), at, .31, .041, 0, true);
      CHORDS[phrase].forEach((midi, voice) => note(midi, at + beatS * .5, .28, .014, (voice - 1.5) * .2, true));
      percussion(at, true); percussion(at + beatS * .5, false);
    }
    HOOKS[phrase].forEach((midi, i) => {
      const at = (base + HOOK_BEATS[i]) * beatS, pan = i % 2 ? .22 : -.22;
      note(midi, at, .85, .076, pan);
      note(midi, at + beatS * .75, .65, .013, -pan);
    });
  }
  return { sampleRate: rate, duration: length / rate, channels: [left, right] };
}

export function createMenuMelody(ctx, out) {
  let buffer = null, voice = null, dead = false;
  const retiring = new Set();
  function drop(v) {
    v.source.disconnect(); v.gain.disconnect(); retiring.delete(v);
    if (voice === v) voice = null;
  }
  function stop() {
    if (!voice) return;
    const v = voice; voice = null; retiring.add(v);
    if (retiring.size > 1) {
      const previous = retiring.values().next().value;
      previous.source.onended = null;
      try { previous.source.stop(); } catch { /* ended */ } drop(previous);
    }
    const now = ctx.currentTime;
    v.gain.gain.cancelScheduledValues?.(now);
    v.gain.gain.setTargetAtTime(0, now, MENU_MELODY.fadeOutS / 4);
    v.source.stop(now + MENU_MELODY.fadeOutS);
  }
  function start() {
    if (dead || voice || ctx.state !== 'running') return;
    if (!buffer) {
      const rendered = renderMenuMelody();
      buffer = ctx.createBuffer(2, rendered.channels[0].length, rendered.sampleRate);
      rendered.channels.forEach((data, ch) => buffer.getChannelData(ch).set(data));
    }
    const source = ctx.createBufferSource(), gain = ctx.createGain();
    source.buffer = buffer; source.loop = true; source.connect(gain); gain.connect(out);
    const v = { source, gain }; voice = v;
    gain.gain.value = 0;
    gain.gain.setTargetAtTime(1, ctx.currentTime, MENU_MELODY.fadeInS / 3);
    source.onended = () => drop(v); source.start();
  }
  return {
    update(on) { if (on) start(); else stop(); },
    get playing() { return !!voice; },
    get inspect() { return { playing: !!voice, cached: !!buffer, voices: (voice ? 1 : 0) + retiring.size }; },
    dispose() {
      if (dead) return; dead = true;
      const all = [...retiring]; if (voice) all.push(voice);
      voice = null;
      for (const v of all) { v.source.onended = null; try { v.source.stop(); } catch { /* ended */ } drop(v); }
      buffer = null;
    },
  };
}
