/* tests/sim-fixture-p6.mjs - a small stacked course for the polish 6 SIM tests (not a test).
 *
 * Plan view (x east, z south is -z): a start straight at y 0 runs east into a vertical 360 loop (R 15 m,
 * exit shifted 16 m to the right), on along the lower straight (z -16), a 270 deg left arc that climbs to
 * y 14, the upper straight south at x 110 that crosses OVER the lower straight (open on its right, +x),
 * then a wide 270 deg right arc that comes down to the start. Falling off the upper straight's open side
 * at the crossing lands on the lower straight, EARLIER in the lap.
 * reverse: the same course driven the other way; then the lower straight comes LATER and the fall off the
 * (now left) open side is registered as a route.
 */

const W = 16, LOOP_R = 15, LOOP_SHIFT = 16, UP_Y = 14;

function nodesForward() {
  const out = [];
  const add = (x, y, z, flags) => out.push({ p: [r2(x), r2(y), r2(z)], flags });
  // start straight, a boost pad on node 1
  for (let x = 0; x < 30; x += 10) add(x, 0, 0, x === 0 ? ['start', 'rail'] : ['rail']);
  // the loop: forward R sin th, up R (1 - cos th), shifted right (-z) smoothly
  const K = 10;
  for (let k = 0; k < K; k++) {
    const th = 2 * Math.PI * k / K;
    add(30 + LOOP_R * Math.sin(th), LOOP_R * (1 - Math.cos(th)), -LOOP_SHIFT * (th - Math.sin(th)) / (2 * Math.PI), ['rail', 'magnet']);
  }
  // the lower straight
  for (let x = 30; x < 150; x += 10) add(x, 0, -16, ['rail']);
  // 270 deg left arc around (150, 24), climbing
  const arc = (cx, cz, r, a0, sweep, y0, y1, n, flags) => {
    for (let k = 0; k < n; k++) {
      const f = k / n, a = a0 + sweep * f, e = f * f * (3 - 2 * f);
      add(cx + r * Math.cos(a), y0 + (y1 - y0) * e, cz + r * Math.sin(a), flags);
    }
  };
  arc(150, 24, 40, -Math.PI / 2, 1.5 * Math.PI, 0, UP_Y, 19, ['rail']);
  // the upper straight south at x 110: open on the right (+x) over the crossing (z 4 .. -36)
  for (let z = 24; z > -110; z -= 10) add(110, UP_Y, z, z <= 4 && z >= -36 ? ['railL'] : ['rail']);
  // 270 deg right arc around (0, -110) back down to the start
  arc(0, -110, 110, 0, -1.5 * Math.PI, UP_Y, 0, 52, ['rail']);
  return out;
}

const r2 = (v) => Math.round(v * 100) / 100;

function base(nodes) {
  const N = nodes.length;
  return {
    format: 'glassroad-track', version: 1, id: 'p6-fixture', name: { en: 'P6 fixture' }, loop: true, laps: 3, units: 'm',
    nodes: nodes.map((n) => ({ p: n.p, roll: 0, w: W, flags: n.flags, speed: 1, surface: 'glass' })),
    sections: [
      { id: 'a', act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6, from: 0, to: Math.floor(N / 3) },
      { id: 'b', act: 'deepening', palette: 'deep', sky: 'spiral-deep', bandSpeed: 1, from: Math.floor(N / 3), to: Math.floor(2 * N / 3) },
      { id: 'c', act: 'release', palette: 'afterglow', sky: 'spiral-wide', bandSpeed: 1, from: Math.floor(2 * N / 3), to: N },
    ],
    checkpoints: [0, Math.floor(N / 4), Math.floor(N / 2), Math.floor(3 * N / 4)],
    spawn: { node: 0, grid: [[-4, -5], [4, -9], [-4, -13], [4, -17], [-4, -21], [4, -25], [-4, -29], [4, -33]] },
    items: [], boosts: [], jumps: [], hazards: [], media: [],
    ai: { lines: [], pace: [1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -30, edgeGraceM: 1.2, edgeGraceS: 0.8 },
  };
}

/** The course JSON; reverse drives it the other way (with a registered route for the fall). */
export function fixtureJson({ reverse = false } = {}) {
  let nodes = nodesForward();
  if (!reverse) {
    const j = base(nodes);
    j.boosts.push({ node: 1, x: 0, w: 4, len: 6 });
    return j;
  }
  // reverse: node 0 stays the start; the order runs backwards, left and right swap
  const swap = (f) => f.map((x) => (x === 'railL' ? 'railR' : x === 'railR' ? 'railL' : x)).filter((x) => x !== 'start');
  nodes = [nodes[0], ...nodes.slice(1).reverse()].map((n, i) => ({ p: n.p, flags: i === 0 ? n.flags : swap(n.flags) }));
  // the loop nodes in reverse: the magnet run must cover the segments that make the loop, so the run shifts by one
  const mag = nodes.map((n) => n.flags.includes('magnet'));
  nodes.forEach((n, i) => {
    const want = mag[(i + 1) % nodes.length];
    n.flags = n.flags.filter((x) => x !== 'magnet');
    if (want) n.flags.push('magnet');
  });
  const j = base(nodes);
  const open = nodes.map((n, i) => (n.flags.includes('railR') ? i : -1)).filter((i) => i >= 0);
  const lowFrom = nodes.findIndex((n, i) => i > open[0] && n.p[1] === 0 && n.p[2] === -16);
  const low = nodes.map((n, i) => (i >= lowFrom && n.p[1] === 0 && n.p[2] === -16 ? i : -1)).filter((i) => i >= 0);
  j.routes = [{ id: 'fixture-fall', kind: 'drop', side: 'L', from: open[0], to: open[open.length - 1] + 1,
    land: { from: low[0], to: low[low.length - 1] - 1 }, pad: { node: low[Math.floor(low.length / 2)], x: 0 } }];
  return j;
}
