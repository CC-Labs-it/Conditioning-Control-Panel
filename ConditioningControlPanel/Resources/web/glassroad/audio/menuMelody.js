// Original eight-bar instrumental: rounded glass plucks over warm suspended chords.
// One reusable sample loop on the existing context, without a timer or live oscillator bank.
export const MENU_MELODY = Object.freeze({ bpm: 80, beats: 32, sampleRate: 24000, fadeInS: 1.2, fadeOutS: .5 });
const CHORDS = [[50, 57, 61, 64], [47, 54, 57, 62], [55, 59, 62, 69], [57, 62, 64, 69]];
const MELODY = [[0, 74], [1.5, 78], [3, 76], [4.5, 73], [6, 69],
  [8, 71], [10, 74], [11.5, 78], [13, 76], [14.5, 74],
  [16, 71], [17.5, 69], [19, 74], [21, 78], [22.5, 76],
  [24, 76], [26, 74], [27.5, 69], [29, 73], [30, 76]];
const hz = midi => 440 * 2 ** ((midi - 69) / 12);

/** Circular tails give the last bar a natural join into the first. No random or remote assets. */
export function renderMenuMelody(sampleRate = MENU_MELODY.sampleRate) {
  const rate = Math.max(8000, Math.min(48000, Math.round(sampleRate) || MENU_MELODY.sampleRate));
  const beatS = 60 / MENU_MELODY.bpm, length = Math.round(MENU_MELODY.beats * beatS * rate);
  const left = new Float32Array(length), right = new Float32Array(length);
  function note(midi, at, duration, level, pan, pad = false) {
    const frequency = hz(midi), start = Math.round(at * rate), count = Math.ceil(duration * rate);
    const l = Math.sqrt((1 - pan) / 2), r = Math.sqrt((1 + pan) / 2);
    for (let i = 0; i < count; i++) {
      const t = i / rate, phase = 2 * Math.PI * frequency * t;
      const attack = 1 - Math.exp(-t / (pad ? .65 : .022));
      const release = Math.min(1, (duration - t) / (pad ? 1.7 : .55));
      const envelope = attack * release * release * (pad ? 1 : Math.exp(-t / .95));
      const body = Math.sin(phase) + (pad ? .045 : .16) * Math.sin(phase * (pad ? 3 : 2)) * Math.exp(-t / .7);
      const sample = body * envelope * level, index = (start + i) % length;
      left[index] += sample * l; right[index] += sample * r;
    }
  }
  for (let bar = 0; bar < CHORDS.length; bar++) {
    CHORDS[bar].forEach((midi, voice) => note(midi, bar * 8 * beatS, 8 * beatS + 1.7, .0105, (voice - 1.5) * .22, true));
  }
  MELODY.forEach(([beat, midi], i) => {
    const pan = i % 2 ? .28 : -.28;
    note(midi, beat * beatS, 2.7, .052, pan);
    note(midi, beat * beatS + .375, 2.2, .012, -pan);
  });
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
