/* Floating image panels use the existing media owners and a fitted, bounded flight envelope. */
import { placeBillboard, rectPoints, tubeClash } from './mediaMath.js';

export const PANEL_FLIGHT = Object.freeze({ swayM: 3.5, bobM: 2, radps: .24 });

/** Includes the luminous frame and every point reached by the drifting picture. */
export function motionEnvelope(slot, M) {
  return { ...slot, C: slot.home || slot.C,
    w: slot.w + 2 * ((slot.flight ? PANEL_FLIGHT.swayM : 0) + M.borderM),
    h: slot.h + 2 * ((slot.flight ? PANEL_FLIGHT.bobM : 0) + M.borderM) };
}

export function fitMainframeBillboard(spec, track, samples, M) {
  if (!spec.float) return placeBillboard(spec, track, samples, M);
  for (let attempt = 0; attempt < 12; attempt++) {
    const slot = placeBillboard({ ...spec, offM: (spec.offM ?? 7) + attempt * 4 }, track, samples, M);
    if (!slot) continue;
    slot.flight = true; slot.home = [...slot.C];
    slot.phase = slot.s * .013;
    delete slot.stalk;
    if (!tubeClash(rectPoints(motionEnvelope(slot, M), 6), samples, M)) return slot;
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
