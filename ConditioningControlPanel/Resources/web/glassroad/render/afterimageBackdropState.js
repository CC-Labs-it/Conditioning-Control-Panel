/* The retained dense Afterimage atlas, 29 October 2026. Background art only. */
export const AFTERIMAGE_STYLES = Object.freeze([
  ['velvet-aurora', 'Velvet aurora', '#e9a0ce', '#82cfeb', '#8a74d1'],
  ['mercury-tides', 'Mercury tides', '#dfb0df', '#91e8de', '#72a1bb'],
  ['neon-mycelium', 'Neon mycelium', '#8debe5', '#a7a5fa', '#6659be'],
  ['moire-drift', 'Moire drift', '#d2b5ed', '#a5d2fa', '#b37ada'],
  ['spectral-shear', 'Spectral shear', '#86dfcd', '#b1e1d9', '#7190ce'],
  ['phase-textile', 'Phase textile', '#e5a6d8', '#9eb9e9', '#bc88de'],
  ['chromatic-turbulence', 'Chromatic turbulence', '#34c2dc', '#df5199', '#7786e8'],
  ['caustic-drift', 'Caustic drift', '#b7a2e5', '#76c2cf', '#7267ae'],
  ['curl-diffusion', 'Curl diffusion', '#85dace', '#b3a0bb', '#41a9b4'],
  ['granular-current', 'Granular current', '#a9cad8', '#d1c3d9', '#708eaa'],
].map(([id, name, ...colours]) => Object.freeze({ id, name, colours: Object.freeze(colours) })));

const sat = value => Math.max(0, Math.min(1, Number.isFinite(value) ? value : 0));
const BANDS = Object.freeze([['bass', 'bass'], ['mid', 'mid'], ['high', 'treble']]);

export function pickAfterimageStyle(random = Math.random, requested) {
  const explicit = AFTERIMAGE_STYLES.find(style => style.id === requested);
  if (explicit) return explicit;
  const sample = sat(random());
  return AFTERIMAGE_STYLES[Math.min(AFTERIMAGE_STYLES.length - 1, Math.floor(sample * AFTERIMAGE_STYLES.length))];
}

/** Ambient drift and lowpass bands share the caller's pause-aware clock. No synthetic beat. */
export function createAfterimageState() {
  const frame = { time: 7, bass: 0, mid: 0, high: 0, gentle: 0 };
  return {
    frame,
    update(dt, view = {}) {
      if (view.paused || view.audioReactive?.frozen || !(dt > 0)) return frame;
      const step = Math.min(dt, .1), gentle = !!view.gentle;
      if (view.reducedMotion) {
        frame.bass = frame.mid = frame.high = 0; frame.gentle = 1;
        return frame;
      }
      frame.time += step * (gentle ? .2 : 1);
      const audio = view.audioReactive, active = audio?.active && !view.reducedMotion;
      // The whole sky never hits with the floor: at least 450 ms to brighten, then a long release.
      for (const [key, source] of BANDS) {
        const target = active ? sat(audio[source]) * (gentle ? .18 : .65) : 0;
        const tau = target > frame[key] ? (gentle ? 1.2 : .45) : .9;
        frame[key] += (target - frame[key]) * (1 - Math.exp(-step / tau));
        if (frame[key] < .00001 && target === 0) frame[key] = 0;
      }
      frame.gentle = gentle || view.reducedMotion ? 1 : 0;
      return frame;
    },
    reset() { Object.assign(frame, { time: 7, bass: 0, mid: 0, high: 0, gentle: 0 }); },
  };
}
