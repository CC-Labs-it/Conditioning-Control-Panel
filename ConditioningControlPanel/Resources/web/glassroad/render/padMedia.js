/* Small pictures inset into alternating flat boosts. The shared host owns every clip. */
export function padMediaSlots(track) {
  const slots = [];
  for (const [i, pad] of (track.boosts || []).entries()) {
    if (i % 2) continue;
    const h = Math.min(5, pad.len * .65), w = Math.min(4, pad.w * .72);
    if (h < 1 || w < 1) continue;
    const s = pad.s + pad.len / 2, f = track.frameAt(s);
    // A plane cannot follow a bank transition, bend or curved canal. Keep those arrows only.
    const ends = [track.frameAt(s - h / 2), track.frameAt(s + h / 2)];
    if (ends.some(e => e.n.reduce((v, n, k) => v + n * f.n[k], 0) < .999
      || e.t.reduce((v, n, k) => v + n * f.t[k], 0) < .999
      || Math.abs(e.p.reduce((v, n, k) => v + (n - f.p[k]) * f.n[k], 0)) > .012)) continue;
    if (track.canalProfile && [s - h / 2, s, s + h / 2].some(at => track.canalProfile(at, pad.x).blend > 0)) continue;
    slots.push({ id: 'boost-media-' + i, frame: 'boostPad', s, w, h,
      C: f.p.map((v, k) => v + f.b[k] * pad.x + f.n[k] * .035),
      X: [...f.b], Y: [...f.t], Z: [...f.n], phone: true, program: 0 });
  }
  const count = Math.min(12, slots.length);
  return slots.filter((_, i) => Math.ceil((i + 1) * count / slots.length) > Math.ceil(i * count / slots.length));
}
