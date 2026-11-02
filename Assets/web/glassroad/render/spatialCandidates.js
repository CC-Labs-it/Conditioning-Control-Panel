// Loading-time broad phase. Exact sphere queries preserve the original ordered candidates.
// Very large bodies stay in a short overflow list rather than filling the grid.
export function spatialCandidates(rows, cellM = 64) {
  const cells = new Map(), overflow = [], marks = new Uint32Array(rows.length);
  let generation = 0;
  const key = (x, y, z) => x + ',' + y + ',' + z;
  const range = (p, r) => [Math.floor((p[0] - r) / cellM), Math.floor((p[0] + r) / cellM),
    Math.floor((p[1] - r) / cellM), Math.floor((p[1] + r) / cellM),
    Math.floor((p[2] - r) / cellM), Math.floor((p[2] + r) / cellM)];
  for (let i = 0; i < rows.length; i++) {
    const row = rows[i], b = range(row.p, row.radius);
    if ((b[1] - b[0] + 1) * (b[3] - b[2] + 1) * (b[5] - b[4] + 1) > 216) { overflow.push(i); continue; }
    for (let x = b[0]; x <= b[1]; x++) for (let y = b[2]; y <= b[3]; y++) for (let z = b[4]; z <= b[5]; z++) {
      const k = key(x, y, z), bucket = cells.get(k);
      if (bucket) bucket.push(i); else cells.set(k, [i]);
    }
  }
  return (p, radius) => {
    generation = (generation + 1) >>> 0;
    if (!generation) { marks.fill(0); generation = 1; }
    const found = [], b = range(p, radius);
    const visit = i => {
      if (marks[i] === generation) return;
      marks[i] = generation;
      const row = rows[i];
      if (Math.hypot(row.p[0] - p[0], row.p[1] - p[1], row.p[2] - p[2]) <= row.radius + radius) found.push(i);
    };
    // Huge queries use the linear path, bounding work even for future oversized landmarks.
    if ((b[1] - b[0] + 1) * (b[3] - b[2] + 1) * (b[5] - b[4] + 1) > 4096) {
      for (let i = 0; i < rows.length; i++) visit(i);
    } else {
      for (const i of overflow) visit(i);
      for (let x = b[0]; x <= b[1]; x++) for (let y = b[2]; y <= b[3]; y++) for (let z = b[4]; z <= b[5]; z++) {
        const bucket = cells.get(key(x, y, z));
        if (bucket) for (const i of bucket) visit(i);
      }
    }
    found.sort((a, b) => a - b);
    return found.map(i => rows[i]);
  };
}
