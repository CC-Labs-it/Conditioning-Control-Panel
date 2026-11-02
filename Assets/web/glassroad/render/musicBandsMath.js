/* Song-beat waveform paint. These distances never move the road mesh or its collision surface. */
import { smoothstep } from './roadAaMath.js';

export const MUSIC_BANDS = Object.freeze({
  laneShare: 0.54, amplitudeShare: 0.18, amplitudeM: 1.65,
  wavelengthM: 24, beatTurns: 0.25, gentleBarTurns: 0.125,
  vocalShape: 0.22, maxSlope: 1.65 * 6 * 1.22 / 24,
  beatRise: 0.12, beatDecay: 0.62, gentleAmplitude: 0.28,
  lineM: 0.17, backingM: 0.32, backingDark: 0.68, coverage: 0.92,
  nearM: [5, 12], farM: [110, 170], aheadM: [-5, 8],
  shoulderM: [1.5, 2.15], centreM: [0.65, 1.25], pixelCapM: 0.8,
  dropWidthM: 6, dropCoverage: 0.46,
});
// Constant, bounded colour: the beat changes the wave's shape, never its brightness.
export const BAND_COLOURS = Object.freeze([
  Object.freeze([0.65, 0.30, 0.48]), Object.freeze([0.18, 0.48, 0.60]),
]);
export const DROP_COLOUR = Object.freeze([0.67, 0.58, 0.50]);
const clamp = (x, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, x));
const fract = (x) => x - Math.floor(x);

/** A smooth onset into the listed beat, then decay. Both sides of a beat boundary meet at one. */
export function musicBandBeat(beat) {
  const phase = fract(beat), L = MUSIC_BANDS;
  return Math.max(smoothstep(1 - L.beatRise, 1, phase), 1 - smoothstep(0, L.beatDecay, phase));
}

/** Smooth periodic cubic, with a bounded slope for cheap analytic antialiasing. */
export function musicBandCurve(phase, vocals = 0) {
  const triangle = 1 - Math.abs(fract(phase + 0.25) * 4 - 2);
  const rounded = triangle * (1.5 - triangle * triangle * 0.5);
  return rounded * (1 + (1 - rounded * rounded) * clamp(vocals) * MUSIC_BANDS.vocalShape);
}

/** Reference of the shader's waveform, driven by beat/bar positions from musicFrame and stem energy. */
export function musicBandShape({ beat = 0, bar = 0, gentle = false, bass = 0, vocals = 0, halfWidth = 8, ahead = 0 } = {}) {
  const L = MUSIC_BANDS, pulse = gentle ? 0.35 : musicBandBeat(beat);
  const clock = gentle ? bar * L.gentleBarTurns : beat * L.beatTurns;
  const phase = ahead / L.wavelengthM - clock;
  const amplitude = Math.min(L.amplitudeM, halfWidth * L.amplitudeShare)
    * (0.32 + pulse * 0.68) * (0.72 + clamp(bass) * 0.28) * (gentle ? L.gentleAmplitude : 1);
  const curve = musicBandCurve(phase, vocals) * amplitude;
  return { pulse, amplitude, lateral: halfWidth * L.laneShare + curve };
}

/** Conservative pixel footprint uses only the road UV derivatives, independent of the energy texture. */
export function musicBandFootprint(pixelX, pixelS) {
  return Math.max(0, pixelX) + Math.max(0, pixelS) * MUSIC_BANDS.maxSlope;
}

/** Preserve the true shoulder and centre line, and soften detail beneath the camera and in the distance. */
export function musicBandMask({ on = true, x = 0, halfWidth = 8, distance = 25, ahead = 25 } = {}) {
  const L = MUSIC_BANDS;
  return (on ? 1 : 0) * smoothstep(...L.nearM, distance) * (1 - smoothstep(...L.farM, distance))
    * smoothstep(...L.aheadM, ahead) * smoothstep(...L.shoulderM, halfWidth - Math.abs(x))
    * smoothstep(...L.centreM, Math.abs(x));
}

/** Broad enough to resolve at speed, with a capped pixel footprint at crests. */
export function musicBandLine(distance, width, pixel = 0) {
  const L = MUSIC_BANDS, px = Math.max(0, pixel);
  const w = Math.max(width, Math.min(px, L.pixelCapM));
  const fade = Math.min(1, L.pixelCapM / Math.max(px, 1e-6)) ** 2;
  return (1 - smoothstep(w * 0.35, w, Math.abs(distance))) * width / w * fade;
}

/** The existing drop pressure front is restored only where solid tiles hid the original road layer. */
export function musicBandDrop({ tileMask = 0, waveD = 0, waveAmp = 0, ...ctx } = {}) {
  const L = MUSIC_BANDS;
  const wave = Math.exp(-(((ctx.ahead - waveD) / L.dropWidthM) ** 2)) * clamp(waveAmp);
  return wave * clamp(tileMask) * L.dropCoverage * musicBandMask(ctx);
}
