// Bounded visual envelope shared by every deck and water material. No audio owns a shader.
export const AUDIO_REACTIVE = Object.freeze({ attackS: 0.32, releaseS: 0.65, gentleAttackS: 0.5,
  gentleGain: 0.38, waveAlong: 0.48, waveAcross: 0.72, waveSpeed: 0.65, slopeGain: 0.14 });

const unit = value => Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0;
const BANDS = Object.freeze(['bass', 'mid', 'treble', 'energy']);
// Quiet real FFT bands need display compression. Apply it before the slow visual envelope,
// preserving its attack time and exact silence while leaving the audio measurement untouched.
export const perceptualAudioBand = value => Math.sqrt(unit(value));

/** Mutates one preallocated envelope. Pausing holds both levels and the real song clock. */
export function stepAudioReactive(state, signal, dt, { paused = false, gentle = false } = {}) {
  if (paused || signal?.frozen) return state;
  const elapsed = Number.isFinite(dt) ? Math.max(0, Math.min(dt, 0.1)) : 0;
  const active = signal?.active === true;
  for (const band of BANDS) {
    const before = unit(state[band]), target = active ? (band === 'energy' ? unit(signal[band]) : perceptualAudioBand(signal[band])) : 0;
    const tau = target > before ? (gentle ? AUDIO_REACTIVE.gentleAttackS : AUDIO_REACTIVE.attackS) : AUDIO_REACTIVE.releaseS;
    const next = before + (target - before) * (1 - Math.exp(-elapsed / tau));
    state[band] = next < 1e-5 ? 0 : next;
  }
  for (let i = 0; i < 8; i++) {
    const key = 'note' + i;
    if (!(key in state)) continue;
    const before = unit(state[key]), target = active ? perceptualAudioBand(signal[key]) : 0;
    const tau = target > before ? (gentle ? AUDIO_REACTIVE.gentleAttackS : AUDIO_REACTIVE.attackS) : AUDIO_REACTIVE.releaseS;
    state[key] = before + (target - before) * (1 - Math.exp(-elapsed / tau));
  }
  if (active && Number.isFinite(signal.songS)) state.t = Math.max(0, signal.songS);
  state.gentle = gentle ? 1 : 0;
  return state;
}

/** Numeric companion to the optical wave normal. Existing physical wakes keep their height/contact. */
export function reactiveWaterSlope(x, s, time, bass, mid, gentle = false) {
  const L = AUDIO_REACTIVE, gain = gentle ? L.gentleGain : 1;
  const a = s * L.waveAlong - time * L.waveSpeed + Math.sin(x * 0.19) * 0.5;
  const b = x * L.waveAcross + s * 0.17 + time * 0.43;
  const amp = unit(bass) * L.slopeGain * gain, cross = unit(mid) * L.slopeGain * 0.55 * gain;
  return [Math.cos(a) * 0.095 * Math.cos(x * 0.19) * amp + Math.cos(b) * L.waveAcross * cross,
    Math.cos(a) * L.waveAlong * amp + Math.cos(b) * 0.17 * cross];
}
