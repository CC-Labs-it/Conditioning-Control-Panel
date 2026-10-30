/* Six-sided cable bodies retain the existing helix clock and open driving corridor. */
export function helixBodyData(track, look) {
  const segments = Math.max(8, Math.ceil(track.lengthM / 2)), sides = 6, radius = .55;
  const attrs = { position: [], aB: [], aN: [], aT: [], aP: [], aDepth: [] }, index = [];
  for (let strand = 0; strand < 2; strand++) {
    const base = attrs.aDepth.length;
    for (let i = 0; i <= segments; i++) {
      const s = i / segments * track.lengthM, frame = track.frameAt(s % track.lengthM);
      const radial = Math.hypot(frame.w / 2 + look.outM, look.liftM);
      for (let side = 0; side < sides; side++) {
        const angle = side / sides * Math.PI * 2;
        attrs.position.push(...frame.p.map((v, axis) => v + frame.n[axis] * look.liftM));
        attrs.aB.push(...frame.b); attrs.aN.push(...frame.n); attrs.aT.push(...frame.t);
        attrs.aP.push(s, s / look.pitchM * Math.PI * 2 + strand * Math.PI, Math.cos(angle) * radius, radial);
        attrs.aDepth.push(Math.sin(angle) * radius);
        if (i < segments) {
          const a = base + i * sides + side, b = base + i * sides + (side + 1) % sides;
          index.push(a, b, a + sides, b, b + sides, a + sides);
        }
      }
    }
  }
  return { attrs, index, radius, sides, count: attrs.aDepth.length };
}
