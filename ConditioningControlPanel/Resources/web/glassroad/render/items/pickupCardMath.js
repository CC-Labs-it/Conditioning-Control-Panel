export const PICKUP_CARD = Object.freeze({ height: 2.25, width: 1.6, hover: .32, bob: .08, spin: .52, faces: 4 });

/** Pause retains the borrowed frame; revocation always paints the fallback immediately. */
export function pickupFaceDue(previous, next, dt, elapsed, initial = false) {
  return initial || previous !== next || (dt > 0 && elapsed >= 1 / 12);
}
