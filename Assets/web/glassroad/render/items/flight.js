/* Shared distance samples keep projectile ribbons continuous at low frame rates.
 * All writers reuse caller-owned buffers; no meshes or materials are created during travel. */

export function advanceRibbon(h, x, y, z, stepM, jumpM = 20) {
  const cap = h.pts.length / 3;
  const append = (px, py, pz) => {
    if (h.n >= cap) { h.pts.copyWithin(0, 3); h.n = cap - 1; }
    const j = h.n++ * 3;
    h.pts[j] = px; h.pts[j + 1] = py; h.pts[j + 2] = pz;
  };
  if (!cap || !(stepM > 0)) return h.n;
  if (!h.n) { append(x, y, z); return h.n; }
  const j = (h.n - 1) * 3, ax = h.pts[j], ay = h.pts[j + 1], az = h.pts[j + 2];
  const dx = x - ax, dy = y - ay, dz = z - az, d = Math.hypot(dx, dy, dz);
  if (d > jumpM) { h.n = 0; append(x, y, z); return h.n; }
  const steps = Math.floor(d / stepM);
  // A long frame may cross more samples than fit. Keep only the visible tail.
  for (let i = Math.max(1, steps - cap + 1); i <= steps; i++) {
    const q = i * stepM / d;
    append(ax + dx * q, ay + dy * q, az + dz * q);
  }
  return h.n;
}

/** A corkscrew spark behind the head, oriented to real travel including a homing turn. */
export function flightSpark(i, n, time, seed, velocity, radius, out) {
  let vx = velocity?.[0] ?? velocity?.x ?? 0;
  let vy = velocity?.[1] ?? velocity?.y ?? 0;
  let vz = velocity?.[2] ?? velocity?.z ?? -1;
  const speed = Math.hypot(vx, vy, vz) || 1;
  vx /= speed; vy /= speed; vz /= speed;
  let ux = -vz, uy = 0, uz = vx, ul = Math.hypot(ux, uz);
  if (ul < 0.001) { ux = 1; uz = 0; ul = 1; }
  ux /= ul; uz /= ul;
  const wx = vy * uz, wy = vz * ux - vx * uz, wz = -vy * ux;
  const q = ((i / Math.max(1, n) + time * 0.8) % 1 + 1) % 1;
  const a = q * Math.PI * 4 + seed, r = radius * (0.9 + q * 0.65), back = q * radius * 4;
  const c = Math.cos(a) * r, s = Math.sin(a) * r;
  out[0] = ux * c + wx * s - vx * back;
  out[1] = uy * c + wy * s - vy * back;
  out[2] = uz * c + wz * s - vz * back;
  return (1 - q) * 0.85;
}

/** A short expanding release cone, anchored at launch and flowing opposite the actual throw. */
export function launchSpark(i, n, age, seed, velocity, out) {
  if (!(age > 0) || age >= .48) return 0;
  const q = age / .48, ring = (i + .5) / Math.max(1, n);
  flightSpark(i, n, 0, seed, velocity, .6 + ring * .45, out);
  const spread = .25 + q * 2.7;
  for (let k = 0; k < 3; k++) out[k] *= spread;
  return Math.min(1, age / .06) * (1 - q) ** 2;
}
