/* The retained dense Afterimage atlas, 29 October 2026. Background art only. */
export const AFTERIMAGE_STYLES = Object.freeze([
  ['velvet-aurora', 'Velvet aurora', '#ff5bc8', '#3fd7ff', '#9b54f2'],
  ['mercury-tides', 'Mercury tides', '#f278ed', '#46efce', '#419edb'],
  ['neon-mycelium', 'Neon mycelium', '#3dffe0', '#ac7bff', '#7652ed'],
  ['moire-drift', 'Moire drift', '#d479ff', '#70c5ff', '#f16bd9'],
  ['spectral-shear', 'Spectral shear', '#48edb5', '#75ffe1', '#6488f4'],
  ['phase-textile', 'Phase textile', '#ff6dc8', '#85adff', '#d268ee'],
  ['chromatic-turbulence', 'Chromatic turbulence', '#25d9f1', '#ff4aa7', '#8b72ff'],
  ['caustic-drift', 'Caustic drift', '#c67dff', '#49dbd5', '#9067eb'],
  ['curl-diffusion', 'Curl diffusion', '#4df0bd', '#ed8ed2', '#26b8dd'],
  ['granular-current', 'Granular current', '#7cdbff', '#f1a4ef', '#7c9ee5'],
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
