/* One bounded music-only analyser. No beat approximation: silence has no reactive energy.
 * The frame, FFT storage and waveform storage are reused for the voice's entire lifetime.
 * Bands use real Hz boundaries, independent of the context's sample rate.
 */
const NONE = Object.freeze({});
export const NOTE_BANDS = Object.freeze(Array.from({ length: 8 }, (_, i) => 250 * 2 ** (i / 2)));
const clamp01 = (v) => Math.max(0, Math.min(1, Number.isFinite(v) ? v : 0));

export function createMusicReactivity(ctx, input) {
  const frame = { active: false, frozen: false, muted: false, silent: false, bass: 0, mid: 0, treble: 0, energy: 0, songS: 0 };
  // The display reads PCM and log-frequency samples from this same tap, without another analyser.
  frame.waveform = new Float32Array(64); frame.spectrum = new Float32Array(64);
  frame.rawBass = frame.rawMid = frame.rawTreble = frame.rawEnergy = 0;
  for (let i = 0; i < 8; i++) frame['note' + i] = 0;
  let analyser = null, spectrum = null, waveform = null;
  let bass0 = 0, bass1 = 0, mid1 = 0, treble1 = 0;
  try {
    if (typeof ctx.createAnalyser === 'function') {
      analyser = ctx.createAnalyser();
      analyser.fftSize = 1024;
      analyser.smoothingTimeConstant = 0; // envelope smoothing below also applies to Gentle
      spectrum = new Float32Array(analyser.frequencyBinCount);
      waveform = new Float32Array(analyser.fftSize);
      const hz = ctx.sampleRate / analyser.fftSize;
      bass0 = Math.max(1, Math.ceil(30 / hz));
      bass1 = Math.min(spectrum.length, Math.ceil(250 / hz));
      mid1 = Math.min(spectrum.length, Math.ceil(2000 / hz));
      treble1 = Math.min(spectrum.length, Math.ceil(16000 / hz));
      input.connect(analyser); // side tap after the voice's gain, before any SFX join
    }
  } catch (e) {
    try { if (analyser) { input.disconnect(analyser); analyser.disconnect(); } } catch { /* unavailable */ }
    analyser = null;
  }

  function clear(songS = 0, muted = false, silent = false) {
    frame.active = false;
    frame.frozen = false;
    frame.muted = !!muted; frame.silent = !!silent;
    frame.bass = frame.mid = frame.treble = frame.energy = 0;
    frame.rawBass = frame.rawMid = frame.rawTreble = frame.rawEnergy = 0;
    frame.waveform.fill(0); frame.spectrum.fill(0);
    for (let i = 0; i < 8; i++) frame['note' + i] = 0;
    frame.songS = Number.isFinite(songS) ? songS : 0;
    return frame;
  }
  function band(from, to) {
    let power = 0;
    for (let i = from; i < to; i++) {
      const db = spectrum[i];
      if (Number.isFinite(db) && db > -100) power += Math.pow(10, db / 10);
    }
    return clamp01(Math.sqrt(power) * 3);
  }
  function smooth(current, target, dt, gentle) {
    const tau = target > current ? (gentle ? 0.35 : 0.12) : (gentle ? 0.65 : 0.35);
    return clamp01(current + (target - current) * (1 - Math.exp(-dt / tau)));
  }

  return {
    frame,
    clear,
    /** sample once per render; options is a caller-owned reusable object. */
    read(dt, opts = NONE) {
      if (!opts.playing || opts.muted || ctx.state === 'closed') return clear(opts.songS, opts.muted);
      if (opts.paused || ctx.state === 'suspended' || ctx.state === 'interrupted') { frame.frozen = true; return frame; }
      if (!analyser || ctx.state !== 'running') return clear(opts.songS);
      frame.frozen = false;
      frame.songS = Number.isFinite(opts.songS) ? opts.songS : 0;
      analyser.getFloatTimeDomainData(waveform);
      let power = 0;
      for (let i = 0; i < waveform.length; i++) {
        const v = waveform[i];
        if (Number.isFinite(v)) power += v * v;
      }
      const rms = Math.sqrt(power / waveform.length);
      // Gate from current samples, never the FFT's residual tail or a synthetic song clock.
      if (!(rms > 0.00002)) return clear(frame.songS, false, true);
      analyser.getFloatFrequencyData(spectrum);
      const step = Math.max(0, Math.min(0.1, Number(dt) || 0));
      const gentle = !!opts.gentle, scale = gentle ? 0.55 : 1;
      frame.active = true;
      frame.muted = frame.silent = false;
      frame.rawBass = band(bass0, bass1); frame.rawMid = band(bass1, mid1);
      frame.rawTreble = band(mid1, treble1); frame.rawEnergy = clamp01(rms * 3);
      frame.bass = smooth(frame.bass, band(bass0, bass1) * scale, step, gentle);
      frame.mid = smooth(frame.mid, band(bass1, mid1) * scale, step, gentle);
      frame.treble = smooth(frame.treble, band(mid1, treble1) * scale, step, gentle);
      frame.energy = smooth(frame.energy, clamp01(rms * 3) * scale, step, gentle);
      // Eight measured half-octaves reveal melodic movement without another analyser or stems.
      const hz = ctx.sampleRate / analyser.fftSize;
      // Trigger the oscilloscope at a rising zero crossing so a steady tone holds a readable shape.
      let start = 0;
      for (let i = 1; i < waveform.length / 2; i++) if (waveform[i - 1] <= 0 && waveform[i] > 0) { start = i; break; }
      for (let i = 0; i < 64; i++) {
        const sample = waveform[start + Math.floor(i * (waveform.length / 2 - 1) / 63)];
        frame.waveform[i] = Number.isFinite(sample) ? Math.max(-1, Math.min(1, sample * 2.5)) : 0;
        const low = 30 * (16000 / 30) ** (i / 64), high = 30 * (16000 / 30) ** ((i + 1) / 64);
        const from = Math.max(1, Math.floor(low / hz)), to = Math.min(spectrum.length, Math.max(from + 1, Math.ceil(high / hz)));
        frame.spectrum[i] = band(from, to);
      }
      for (let i = 0; i < 8; i++) frame['note' + i] = smooth(frame['note' + i],
        band(Math.ceil(NOTE_BANDS[i] / hz), Math.min(spectrum.length, Math.ceil(NOTE_BANDS[i] * Math.SQRT2 / hz))) * scale, step, gentle);
      return frame;
    },
    dispose() {
      clear();
      try { if (analyser) { input.disconnect(analyser); analyser.disconnect(); } } catch { /* gone */ }
      analyser = null;
    },
  };
}
