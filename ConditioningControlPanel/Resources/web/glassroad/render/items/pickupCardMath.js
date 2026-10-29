export const PICKUP_CARD = Object.freeze({ height: 2.25, width: 1.6, hover: .32, bob: .08, spin: .52, faces: 4 });

/**
 * Playtest 2026-10-28: the card read as a phone or a billboard. An item box is now a card inside a glass
 * bubble, both wearing one colour no scenery screen uses (chartreuse) with an ink "?" stamp on both faces;
 * a picture stays only as a small inset. cardScale shrinks the card so it sits inside bubbleR.
 */
export const PICKUP_BOX = Object.freeze({
  colour: '#d4ff3a', light: '#f4ffb8', ink: '#1b1024',
  cardScale: .78, liftM: .25, bubbleR: 1.2, rimPow: 2.2, body: .07, pulse: .15, pulseHz: .5,
});

/** Card base lift and bubble centre above the road, for a bob offset in metres. */
export function pickupBoxPose(bob = 0) {
  const lift = PICKUP_CARD.hover + PICKUP_BOX.liftM + bob;
  return { lift, centre: lift + PICKUP_CARD.height * PICKUP_BOX.cardScale / 2 };
}

/** The bubble rim's slow breath (1 = rest); Gentle or reduced motion holds it still. */
export function pickupBoxPulse(t, still = false) {
  return still ? 1 : 1 - PICKUP_BOX.pulse * (.5 + .5 * Math.sin(t * PICKUP_BOX.pulseHz * Math.PI * 2));
}

/** Pause retains the borrowed frame; revocation always paints the fallback immediately. */
export function pickupFaceDue(previous, next, dt, elapsed, initial = false) {
  return initial || previous !== next || (dt > 0 && elapsed >= 1 / 12);
}
