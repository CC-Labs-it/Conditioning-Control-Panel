/** Contact basis for the world-space guard shimmer; emitted only for a physical bounce. */
export function railHit(position, frame, side, radius, dv) {
  return {
    dv, side,
    p: position.map((v, i) => v + side * radius * frame.b[i]),
    normal: frame.b.map(v => -side * v),
    tangent: [...frame.t], up: [...frame.n],
  };
}
