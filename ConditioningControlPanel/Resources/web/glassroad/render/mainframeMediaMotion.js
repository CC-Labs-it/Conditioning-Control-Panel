/* Floating image panels use the existing media owners and a fitted, bounded flight envelope. */
import { placeBillboard, rectPoints, tubeClash } from './mediaMath.js';
import { mainframeStationFraction } from './mainframeMediaStations.js';
import { mainframePictureVisible } from './mainframeMediaSightlines.js';

export const PANEL_FLIGHT = Object.freeze({ swayM: 3.5, bobM: 2, radps: .24 });

/** Includes the luminous frame and every point reached by the drifting picture. */
export function motionEnvelope(slot, M) {
  return { ...slot, C: slot.home || slot.C,
    w: slot.w + 2 * ((slot.flight ? PANEL_FLIGHT.swayM : 0) + M.borderM),
    h: slot.h + 2 * ((slot.flight ? PANEL_FLIGHT.bobM : 0) + M.borderM) };
}

export function fitMainframeBillboard(spec, track, samples, M) {
  spec = { ...spec, f: mainframeStationFraction(spec.f, track) };
  const shifts = track.courseShape ? [0, -4, 4, -8, 8] : [0, -4, 4, -8, 8, -12, 12, -16, 16, -20, 20, -24, 24];
  const sides = track.courseShape ? [spec.side, -spec.side] : [spec.side];
  if (!spec.float) {
    // A raised ramp can rotate a fixed screen into another deck. Keep the landmark nearby.
    for (const shift of shifts) for (const side of sides) for (const lift of track.courseShape ? [0, -4, -8, 4, 8, 12] : [0]) for (const extra of track.courseShape ? [0, 8, 16, 24, 32] : [0]) {
      const slot = placeBillboard({ ...spec, side, liftM: Math.max(2.5, (spec.liftM ?? 3) + lift), offM: (spec.offM ?? 10) + extra, f: spec.f + shift / track.lengthM }, track, samples, M);
      if (slot && mainframePictureVisible(slot, track)) return slot;
    }
    return null;
  }
  for (const shift of shifts) for (const side of sides) for (const lift of track.courseShape ? [0, -4, -8, 4, 8, 12, 20] : [0]) for (let attempt = 0; attempt < 12; attempt++) {
    const slot = placeBillboard({ ...spec, side, f: spec.f + shift / track.lengthM,
      offM: (spec.offM ?? 7) + attempt * 4, liftM: Math.max(2.5, (spec.liftM ?? 3) + lift) }, track, samples, M);
    if (!slot) continue;
    slot.flight = true; slot.home = [...slot.C];
    slot.phase = slot.s * .013;
    delete slot.stalk;
    if (!tubeClash(rectPoints(motionEnvelope(slot, M), 6), samples, M) && mainframePictureVisible(motionEnvelope(slot, M), track)) return slot;
  }
  return null;
}

/** Allocation-free. The caller's clock already respects pause and Gentle. */
export function moveMainframePanel(slot, clock) {
  const phase = slot.phase + clock * PANEL_FLIGHT.radps;
  const x = Math.sin(phase) * PANEL_FLIGHT.swayM;
  const y = Math.cos(phase * .79) * PANEL_FLIGHT.bobM;
  for (let i = 0; i < 3; i++) slot.C[i] = slot.home[i] + slot.X[i] * x + slot.Y[i] * y;
  return slot;
}
