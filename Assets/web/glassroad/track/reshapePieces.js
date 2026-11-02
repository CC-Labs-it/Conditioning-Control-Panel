// Re-author horizontal road shapes without moving the stable node identities.
const RAD = Math.PI / 180;
const smooth = u => u * u * (3 - 2 * u);
export const pieceLength = p => p.turn ? Math.abs(p.turn) * RAD * (p.r + (p.rEnd || p.r)) / 2 : p.len;

export function walkPieces(pieces) {
  let x = 0, z = 0, heading = 0;
  const starts = [];
  for (const p of pieces) {
    starts.push({ x, z, heading });
    const a = (p.turn || 0) * RAD, sign = Math.sign(a), next = heading + a;
    const grow = sign * ((p.rEnd || p.r) - p.r);
    if (p.turn) {
      x += sign * p.r * (Math.sin(next) - Math.sin(heading)) + grow * Math.sin(next);
      z += sign * p.r * (Math.cos(heading) - Math.cos(next)) - grow * Math.cos(next);
    } else { x += p.len * Math.cos(heading); z += p.len * Math.sin(heading); }
    heading = next;
  }
  return { x, z, starts };
}

function point(p, start, u) {
  const a = (p.turn || 0) * RAD, sign = Math.sign(a), h = start.heading + a * u;
  const grow = sign * ((p.rEnd || p.r) - p.r) * smooth(u);
  return p.turn ? [start.x + sign * p.r * (Math.sin(h) - Math.sin(start.heading)) + grow * Math.sin(h),
    start.z + sign * p.r * (Math.cos(start.heading) - Math.cos(h)) - grow * Math.cos(h)]
    : [start.x + pieceLength(p) * u * Math.cos(h), start.z + pieceLength(p) * u * Math.sin(h)];
}

/** Closure grows an ordinary straight facing the missing distance, never reverses a short connector. */
export function broadPieces(source, overrides = {}, radiusM = 145) {
  const pieces = source.map(p => ({ ...p, ...(p.turn ? { r: Math.max(radiusM, p.r),
    ...(p.rEnd ? { rEnd: Math.max(radiusM + 40, p.rEnd) } : {}) } : {}), ...overrides[p.id] }));
  for (const axis of ['x', 'z']) {
    const plan = walkPieces(pieces), missing = plan[axis];
    if (Math.abs(missing) < 1e-6) continue;
    const candidates = pieces.map((p, i) => ({ p, i, direction: axis === 'x' ? Math.cos(plan.starts[i].heading) : Math.sin(plan.starts[i].heading) }))
      .filter(({ p, direction }) => !p.turn && !p.jump && !p.field && !p.kick && !p.wFrom && !p.widenTo && !p.profile && !(p.w >= 72) && Math.abs(direction) > .999);
    const selected = candidates.find(p => p.direction * missing < 0);
    if (!selected) throw Error('No forward closure straight for ' + axis);
    selected.p.len -= missing / selected.direction;
  }
  const end = walkPieces(pieces);
  if (Math.hypot(end.x, end.z) > .001) throw Error('Broad course does not close');
  return pieces;
}

export function reshapePiecesNodes(nodes, source, pieces, count, { residualMul = .35, lockedNodes = [], sampleU = (p, u) => u } = {}) {
  const before = nodes.map(n => n.p.slice()), oldPlan = walkPieces(source), plan = walkPieces(pieces);
  let index = 0;
  const fixedCrossings = new Set(lockedNodes);
  for (let i = 0; i < source.length; i++) {
    const original = source[i], n = count(original);
    if (original.kick || original.jump) for (let k = index - 3; k < index + n + 3; k++) fixedCrossings.add((k + nodes.length) % nodes.length);
    for (let k = 0; k < n; k++, index++) {
      const a = point(original, oldPlan.starts[i], k / n), b = point(pieces[i], plan.starts[i], sampleU(pieces[i], k / n));
      const node = nodes[index];
      if (!node) throw Error('Missing stable course node ' + index);
      const residual = original.profile?.weave ? residualMul : 1;
      node.p[0] = b[0] + (node.p[0] - a[0]) * residual;
      node.p[2] = b[1] + (node.p[2] - a[1]) * residual;
    }
  }
  if (index !== nodes.length) throw Error('Course node identities changed');
  // Unequal old node intervals otherwise make tiny tight hooks at the new arc joins.
  const locked = nodes.map((n, i) => fixedCrossings.has(i) || [-3, -2, -1, 0, 1, 2, 3].some(d => nodes[(i + d + nodes.length) % nodes.length].flags.includes('jump')));
  for (let pass = 0; pass < 4; pass++) {
    const positions = nodes.map(n => n.p.slice());
    nodes.forEach((n, i) => {
      if (locked[i]) return;
      for (const k of [0, 2]) n.p[k] = .25 * positions[(i - 1 + nodes.length) % nodes.length][k] + .5 * positions[i][k] + .25 * positions[(i + 1) % nodes.length][k];
    });
  }
  const after = nodes.map(n => n.p.slice());
  const mapPoint = p => {
    let closest = Infinity, best = 0, blend = 0;
    for (let i = 0; i < before.length; i++) {
      const a = before[i], b = before[(i + 1) % before.length];
      const dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
      const u = Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy + (p[2] - a[2]) * dz) / Math.max(.001, dx * dx + dy * dy + dz * dz)));
      const distance = (p[0] - a[0] - u * dx) ** 2 + (p[1] - a[1] - u * dy) ** 2 + (p[2] - a[2] - u * dz) ** 2;
      if (distance < closest) { closest = distance; best = i; blend = u; }
    }
    const next = (best + 1) % before.length, out = p.slice();
    for (const k of [0, 2]) out[k] += (after[best][k] - before[best][k]) * (1 - blend) + (after[next][k] - before[next][k]) * blend;
    return out;
  };
  return { pieces, mapPoint, nodePoint: i => after[i].slice() };
}
