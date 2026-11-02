/* A picture is the front aperture of a cabinet, with its volume behind the image. */
export function screenBodySize(slot) {
  const depth = Math.max(.28, Math.min(slot.frame === 'card' ? .65 : 1.65, Math.min(slot.w, slot.h) * .13));
  return { w: slot.w * 1.08, h: slot.h * 1.08, depth, front: depth * .06, back: -depth };
}

export function screenBodyBox(slot) {
  const s = screenBodySize(slot), offset = (s.front + s.back) / 2;
  return { p: slot.C.map((v, i) => v + slot.Z[i] * offset), size: [s.w, s.h, s.front - s.back],
    axes: [slot.X, slot.Y, slot.Z] };
}
