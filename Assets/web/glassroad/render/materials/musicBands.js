/* Outlined waveform ribbons over glass and tiles. Existing song uniforms, no new draw or frame clock. */
import { float, vec3, fract, abs, min, max, mix, smoothstep, step, fwidth, clamp } from 'three/tsl';
import { MUSIC_BANDS as L, BAND_COLOURS, DROP_COLOUR } from '../musicBandsMath.js';

const oneMinus = (v) => float(1).sub(v);
const line = (distance, width, cappedPixel) => {
  const w = max(float(width), cappedPixel);
  return oneMinus(smoothstep(w.mul(0.35), w, distance)).mul(float(width).div(w));
};

export function createMusicBands({ p, halfWidth, trueEdge, distance, ahead, music: mu, energy, tileMask, wave }) {
  const gentle = mu.tileGentle, beatPhase = fract(mu.tileBeat);
  const onset = max(smoothstep(1 - L.beatRise, 1, beatPhase), oneMinus(smoothstep(0, L.beatDecay, beatPhase)));
  const pulse = mix(onset, 0.35, gentle);
  const clock = mix(mu.tileBeat.mul(L.beatTurns), mu.tileBar.mul(L.gentleBarTurns), gentle);
  const phase = ahead.div(L.wavelengthM).sub(clock);
  const amplitude = min(float(L.amplitudeM), halfWidth.mul(L.amplitudeShare))
    .mul(pulse.mul(0.68).add(0.32)).mul(clamp(energy.b, 0, 1).mul(0.28).add(0.72))
    .mul(mix(1, L.gentleAmplitude, gentle));
  // A cubic triangle has smooth crests without per-fragment trigonometry. Vocals fill its lobes.
  const triangle = oneMinus(abs(fract(phase.add(0.25)).mul(4).sub(2)));
  const rounded = triangle.mul(float(1.5).sub(triangle.mul(triangle).mul(0.5)));
  const curve = rounded.mul(float(1).add(oneMinus(rounded.mul(rounded)).mul(clamp(energy.a, 0, 1)).mul(L.vocalShape))).mul(amplitude);
  const lateral = abs(p.x), d = abs(lateral.sub(halfWidth.mul(L.laneShare)).sub(curve));
  const mask = mu.tileOn.mul(smoothstep(...L.nearM, distance)).mul(oneMinus(smoothstep(...L.farM, distance)))
    .mul(smoothstep(...L.aheadM, ahead)).mul(smoothstep(...L.shoulderM, trueEdge))
    .mul(smoothstep(...L.centreM, lateral));
  // Derive the footprint from raw road coordinates, not the complete texture-driven waveform.
  // The analytic maximum slope covers both lobes, including the vocal shape, at peak amplitude.
  const px = fwidth(p.x).add(fwidth(p.y).mul(L.maxSlope));
  const cappedPixel = min(px, L.pixelCapM), fade = min(float(1), float(L.pixelCapM).div(max(px, 1e-6)));
  const coverage = mask.mul(fade.mul(fade));
  const backing = line(d, L.backingM, cappedPixel).mul(coverage), fill = line(d, L.lineM, cappedPixel).mul(coverage);
  const colour = mix(vec3(...BAND_COLOURS[0]), vec3(...BAND_COLOURS[1]), step(0, p.x));
  // The original road already evaluates this Gaussian. Reuse the exact node across both surfaces.
  const drop = clamp(wave, 0, 1).mul(tileMask).mul(mask).mul(L.dropCoverage);
  return {
    /** Bounded surface mixes retain the song's colour and avoid additive bloom on every beat. */
    apply(surface) {
      const under = mix(surface, vec3(...DROP_COLOUR), drop);
      return mix(under.mul(oneMinus(backing.mul(L.backingDark))), colour, fill.mul(L.coverage));
    },
  };
}
