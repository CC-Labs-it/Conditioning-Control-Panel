/* Authored physical route choices. Endpoints retain the main course's node identities. */
const round = v => +v.toFixed(3) || 0;
const unit = a => { const d = Math.hypot(...a) || 1; return a.map(v => v / d); };
const dot = (a, b) => a.reduce((n, v, i) => n + v * b[i], 0);
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const smooth = u => u * u * (3 - 2 * u);

export function makeBoudoirForks(track, node) {
  const forks = [];
  function fork(id, label, kind, from, to, width, interior, side = 1) {
    const a = track.frameAt(track.nodeS[from]), b = track.frameAt(track.nodeS[to]);
    function mouth(start, entering) {
      return [0, .2, .4, .6, .8, 1].map(u => {
        const along = entering ? u : 1 - u, s = start + (entering ? along : -along) * 60;
        const f = track.frameAt(s), lateral = side * (f.w / 2 + width / 2 + 7) * smooth(along);
        const surface = track.surfaceAt(s, lateral);
        return { p: surface.p, w: width + (1 - along) * 6, targetNormal: surface.n };
      });
    }
    const controls = [...mouth(track.nodeS[from], true), ...interior, ...mouth(track.nodeS[to], false)];
    const points = controls.map((p, i) => {
      let roll = p.roll || 0;
      if (p.targetNormal) {
        const here = p.p, prev = controls[i - 1]?.p || a.p.map((v, k) => v - a.t[k] * 12);
        const next = controls[i + 1]?.p || b.p.map((v, k) => v + b.t[k] * 12);
        const d0 = Math.sqrt(Math.hypot(...here.map((v, k) => v - prev[k]))), d1 = Math.sqrt(Math.hypot(...next.map((v, k) => v - here[k])));
        const tangent = unit(here.map((v, k) => (v - prev[k]) / d0 - (next[k] - prev[k]) / (d0 + d1) + (next[k] - v) / d1));
        const right = unit(cross(tangent, [0, 1, 0])), up = cross(right, tangent);
        roll = Math.atan2(dot(p.targetNormal, right), dot(p.targetNormal, up)) * 180 / Math.PI;
      }
      return { p: p.p.map(round), w: p.w ?? width, roll: round(roll), surface: 'grip', flags: ['rail'] };
    });
    forks.push({ id, label, kind, from, to, points });
  }

  const galleryFrom = node('shelf-weave-in'), galleryTo = node('upper-gallery');
  const ga = track.frameAt(track.nodeS[galleryFrom]).p, gb = track.frameAt(track.nodeS[galleryTo]).p;
  fork('gallery-chord', 'Gallery cut', 'narrow', galleryFrom, galleryTo, 12, [
    { p: [ga[0] - 35, ga[1] - 4, (ga[2] + gb[2]) / 2], w: 10 },
  ]);

  const bathFrom = node('bath-weave-out'), bathTo = node('compacts-approach', .57);
  const ba = track.frameAt(track.nodeS[bathFrom]).p, bb = track.frameAt(track.nodeS[bathTo]).p;
  fork('bath-dry-bridge', 'Dry bridge', 'dry', bathFrom, bathTo, 14, [
    { p: [205, 31, 1165], roll: -3 },
    { p: [140, 43, 1145], w: 12 },
    { p: [30, 40, 1100], roll: 5 },
    { p: [-80, 25, 1060], w: 16 },
    { p: [bb[0] + 65, 0, bb[2] + 113], roll: -5 },
  ]);

  const wardrobeFrom = node('wardrobe-descent'), wardrobeTo = node('wardrobe-out', .58);
  const core = track.frameAt(track.nodeS[node('wardrobe-out')]).p;
  fork('wardrobe-velvet-descent', 'Velvet descent', 'slalom', wardrobeFrom, wardrobeTo, 13, [
    { p: [core[0] + 97, 92, core[2] + 45], roll: -5 },
    { p: [core[0] + 147, 80, core[2] + 105], roll: -8 },
    { p: [core[0] + 117, 64, core[2] + 180], w: 12, roll: -6 },
    { p: [core[0] + 37, 50, core[2] + 210], roll: 8 },
    { p: [core[0] - 38, 35, core[2] + 165], w: 15, roll: 8 },
    { p: [core[0] - 48, 24, core[2] + 95], roll: -6 },
    { p: [core[0] + 65, 20, core[2] + 78], w: 14, roll: -4 },
    { p: [core[0] + 50, 14, core[2] + 41], w: 13, roll: -3 },
  ], -1);
  return forks;
}
