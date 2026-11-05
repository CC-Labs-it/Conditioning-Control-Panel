/* Authored Boudoir jump ramps (owner ask 2026-10-30). Each lip sits `k` nodes into a layout piece; the
 * placement and the landing corridor at 40-66.7 m/s are proven by tests/ramps-boudoir.test.mjs.
 * The wardrobe cuts open on the inside of the right-hand helix, so a straight launch drifts away from
 * their open edge: no ramp feeds them, and no ramp sits in their mouths. */
const RAMPS = [
  { id: 'bdr-ramp-1', piece: 'powder-sweep', k: 7, x: 0, widthM: 6, lenM: 7, heightM: 1.2, upMps: 14, purpose: 'item',
    note: 'wide open powder sweep: the arc threads three floating boxes before the perfume court' },
  { id: 'bdr-ramp-2', piece: 'shelf-approach', k: 5, x: 8, widthM: 6, lenM: 7, heightM: 1.2, upMps: 15, purpose: 'cut',
    note: 'inside of the shelf approach bend: fly the chord across the corner toward the shelf climb' },
  { id: 'bdr-ramp-3', piece: 'upper-weave-cross', k: 0, x: -4, widthM: 7, lenM: 8, heightM: 1.4, upMps: 15, purpose: 'air',
    note: 'vista crest at 106 m, the highest road in the room, with the whole spiral below' },
  { id: 'bdr-ramp-4', piece: 'bath-weave-cross', k: 0, x: 6, widthM: 6, lenM: 7, heightM: 1.2, upMps: 13, purpose: 'cut',
    note: 'bath weave crest: launch off the S inflection and cut the inside of the right-hander' },
];
/** Lifted boxes on bdr-ramp-1's arc: ds after the lip, x drifts with the sweep, liftM above the road. */
const BOXES = [{ ds: 14, x: .5 }, { ds: 22, x: 1 }, { ds: 30, x: 1.5 }];
export const BOUDOIR_RAMP_LIFT_M = 3.5;

/** Track `ramps` rows; pieceStart(id) is the first node of a layout piece. */
export function makeBoudoirRamps(pieceStart) {
  return RAMPS.map(({ piece, k, ...row }) => {
    const { id, ...rest } = row;
    return { id, node: pieceStart(piece) + k, ...rest };
  });
}
/** The floating item boxes on the item ramp's arc (track `items` rows). */
export function makeBoudoirRampBoxes(ramps) {
  const ramp = ramps.find(r => r.purpose === 'item');
  return BOXES.map(({ ds, x }) => ({ node: ramp.node, x, kind: 'box', ds, liftM: BOUDOIR_RAMP_LIFT_M }));
}
