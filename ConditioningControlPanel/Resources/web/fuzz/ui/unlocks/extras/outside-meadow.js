/* ui/unlocks/extras/outside-meadow.js - the Go Outside meadow: a small live voxel field in plain three.js.
 *
 * createMeadow(host, { atlasUrl, atlasJsonUrl, reduced, lite, seed, frame }) -> Promise<api>
 *   api: { canvas, start(), lockControls(), autopilot(on), resize(), stop(), still(quality) }
 *   `frame` (optional) is a frame subscription such as ctx.frame: fn(dtSeconds) -> cancel. Without it the meadow runs
 *   its own requestAnimationFrame (the dev page). WebGL failure rejects (the show falls back to meadow.jpg).
 *
 * Own code only (contract in handoff-1109/TC-GO-OUTSIDE-LANES.md, lane W). The pure parts (heightmap, world, meshing,
 * walking, autopilot) need no three and no DOM, so tests/outside-meadow.test.mjs runs them in node; three is imported
 * only inside createMeadow. Tiles come from assets/outside/meadow-atlas.png/.json; any tile it lacks (or the whole
 * atlas, until lane M delivers) is painted here as a flat placeholder keyed by the same name. */

export const MEADOW = {
  size: 48, pad: 16, padLite: 10, height: 24, eye: 1.62, radius: 0.28,
  walkSpeed: 3.4, autoSpeed: 0.7, plants: 280, plantsLite: 110, cows: 3, cowsLite: 2,
  fogNear: 20, fogFar: 46, fogNearLite: 15, fogFarLite: 36,
};

export const TILE_NAMES = ['grass_top', 'grass_side', 'dirt', 'log_side', 'log_top', 'leaves', 'flower_a', 'flower_b',
  'flower_c', 'tall_grass', 'cloud', 'sun', 'cow_head', 'cow_body', 'cow_leg'];

export const AIR = 0, GRASS = 1, DIRT = 2, LOG = 3, LEAVES = 4, CLOUD = 5;
const PLANT_KINDS = ['tall_grass', 'flower_a', 'flower_b', 'flower_c'];

/* ---------- seeded noise ---------- */

export function rng(seed) {
  let a = (seed >>> 0) || 1;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function hash2(x, z, seed) {
  let h = Math.imul(x | 0, 374761393) ^ Math.imul(z | 0, 668265263) ^ Math.imul(seed | 0, 2147483647);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}
const smooth = (t) => t * t * (3 - 2 * t);
/** Value noise in 0..1; `period` > 0 wraps x and z (clouds drift and repeat seamlessly). */
export function valueNoise(x, z, seed, period = 0) {
  const x0 = Math.floor(x), z0 = Math.floor(z), fx = smooth(x - x0), fz = smooth(z - z0);
  const w = (v) => (period ? ((v % period) + period) % period : v);
  const a = hash2(w(x0), w(z0), seed), b = hash2(w(x0 + 1), w(z0), seed);
  const c = hash2(w(x0), w(z0 + 1), seed), d = hash2(w(x0 + 1), w(z0 + 1), seed);
  return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
}
/** Gently rolling ground: whole blocks, 3..9 high. */
export function heightAt(x, z, seed = 7) {
  const n = 0.56 * valueNoise(x / 14, z / 14, seed) + 0.3 * valueNoise(x / 7, z / 7, seed + 11)
    + 0.14 * valueNoise(x / 3.5, z / 3.5, seed + 23);
  return Math.max(3, Math.min(9, Math.round(2 + n * 7.5)));
}

/* ---------- the world ---------- */

const idx = (w, x, y, z) => (y * w.W + z) * w.W + x;

/** Blocks, trees, plants, cows and the starting view. Deterministic for a seed. */
export function buildWorld({ seed = 7, lite = false } = {}) {
  const size = MEADOW.size, pad = lite ? MEADOW.padLite : MEADOW.pad, W = size + 2 * pad, H = MEADOW.height;
  const r = rng(seed * 7919 + 1);
  const heights = new Uint8Array(W * W), blocks = new Uint8Array(W * H * W);
  const world = { W, H, size, pad, seed, lite, heights, blocks, trees: [], trunks: new Set(), plants: [], cows: [] };
  for (let z = 0; z < W; z += 1) for (let x = 0; x < W; x += 1) {
    const h = heightAt(x, z, seed);
    heights[z * W + x] = h;
    for (let y = 0; y < h; y += 1) blocks[idx(world, x, y, z)] = y === h - 1 ? GRASS : DIRT;
  }
  // Start at the south of the meadow looking north (-z) across it.
  const sx = pad + size / 2, sz = pad + size - 9;
  world.start = { x: sx + 0.5, z: sz + 0.5, yaw: 0, pitch: -0.08 };
  const inMeadow = (x, z, m = 2) => x >= pad + m && x < pad + size - m && z >= pad + m && z < pad + size - m;
  const far = (x, z, d) => world.trees.every((t) => Math.hypot(t.x - x, t.z - z) >= d);
  const anchors = [[-6, -8], [7, -11], [-12, -16], [1, -19], [12, -22], [-5, -27]];
  for (const [dx, dz] of anchors) plantTree(world, sx + dx, sz + dz, 4 + Math.floor(r() * 2), r);
  for (let n = 0; n < 200 && world.trees.length < 9; n += 1) {
    const x = pad + 3 + Math.floor(r() * (size - 6)), z = pad + 3 + Math.floor(r() * (size - 6));
    if (far(x, z, 6) && Math.hypot(x - sx, z - sz) > 5) plantTree(world, x, z, 4 + Math.floor(r() * 2), r);
  }
  if (!lite) {   // a few trees out in the padding give the fog something to swallow
    for (let n = 0; n < 60 && world.trees.length < 13; n += 1) {
      const x = 3 + Math.floor(r() * (W - 6)), z = 3 + Math.floor(r() * (W - 6));
      if (!inMeadow(x, z, -2) && far(x, z, 7)) plantTree(world, x, z, 4 + Math.floor(r() * 2), r);
    }
  }
  // Plants: tall grass everywhere, flowers in small clusters.
  const want = lite ? MEADOW.plantsLite : MEADOW.plants, taken = new Set();
  let last = null;
  for (let n = 0; n < want * 6 && world.plants.length < want; n += 1) {
    let x, z;
    const flower = r() < 0.42;
    if (flower && last && r() < 0.6) { x = last.x + Math.round((r() - 0.5) * 4); z = last.z + Math.round((r() - 0.5) * 4); }
    else { x = pad - 4 + Math.floor(r() * (size + 8)); z = pad - 4 + Math.floor(r() * (size + 8)); }
    if (x < 0 || z < 0 || x >= W || z >= W) continue;
    const key = z * W + x;
    if (taken.has(key) || world.trunks.has(key)) continue;
    if (Math.abs(x + 0.5 - world.start.x) < 1 && Math.abs(z + 0.5 - world.start.z) < 1) continue;
    const y = heights[key];
    if (blocks[idx(world, x, y, z)] !== AIR) continue;
    taken.add(key);
    const kind = flower ? (last?.kind && last.kind !== 'tall_grass' && r() < 0.7 ? last.kind : PLANT_KINDS[1 + Math.floor(r() * 3)]) : 'tall_grass';
    const p = { x: x + 0.5 + (r() - 0.5) * 0.3, z: z + 0.5 + (r() - 0.5) * 0.3, y, kind, rot: r() * Math.PI, s: 0.82 + r() * 0.2 };
    world.plants.push(p);
    if (flower) last = { x, z, kind };
  }
  // Cows in front of the start, never in a trunk.
  const cowAt = [[-1, -6], [4, -8], [-3, -11]].slice(0, lite ? MEADOW.cowsLite : MEADOW.cows);
  for (const [dx, dz] of cowAt) {
    let x = sx + dx + 0.5, z = sz + dz + 0.5;
    for (let k = 0; k < 8 && world.trunks.has(Math.floor(z) * W + Math.floor(x)); k += 1) x += 1;
    world.cows.push({ x, z, yaw: r() * Math.PI * 2 });
  }
  return world;
}

function plantTree(w, x, z, trunk, r) {
  if (x < 2 || z < 2 || x >= w.W - 2 || z >= w.W - 2) return;
  const g = w.heights[z * w.W + x];
  if (g + trunk + 3 >= w.H) return;
  for (let y = g; y < g + trunk; y += 1) w.blocks[idx(w, x, y, z)] = LOG;
  const top = g + trunk;
  for (let dy = -2; dy <= 1; dy += 1) {
    const rad = dy <= -1 ? 2 : 1;
    for (let dz = -rad; dz <= rad; dz += 1) for (let dx = -rad; dx <= rad; dx += 1) {
      if (rad === 2 && Math.abs(dx) === 2 && Math.abs(dz) === 2 && r() < 0.75) continue;
      if (rad === 1 && dy === 1 && Math.abs(dx) + Math.abs(dz) === 2 && r() < 0.6) continue;
      const i = idx(w, x + dx, top + dy, z + dz);
      if (w.blocks[i] === AIR) w.blocks[i] = LEAVES;
    }
  }
  w.trees.push({ x, z, g, trunk });
  w.trunks.add(z * w.W + x);
}

/** A sampler over the world blocks: below the floor reads solid (no bottom faces), outside reads air. */
export function worldVolume(w) {
  return {
    sx: w.W, sy: w.H, sz: w.W,
    get(x, y, z) {
      if (y < 0) return DIRT;
      if (x < 0 || z < 0 || x >= w.W || z >= w.W || y >= w.H) return AIR;
      return w.blocks[idx(w, x, y, z)];
    },
  };
}

/** Which tile a block shows on a face (0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z). */
export function tileFor(b, f) {
  if (b === GRASS) return f === 2 ? 'grass_top' : f === 3 ? 'dirt' : 'grass_side';
  if (b === DIRT) return 'dirt';
  if (b === LOG) return f === 2 || f === 3 ? 'log_top' : 'log_side';
  if (b === LEAVES) return 'leaves';
  return 'cloud';
}

/* ---------- meshing (face culled, per-vertex AO, one buffer per material) ---------- */

// Corners in order bottom-left, bottom-right, top-right, top-left as seen from outside (CCW).
const FACES = [
  { n: [1, 0, 0], c: [[1, 0, 1], [1, 0, 0], [1, 1, 0], [1, 1, 1]], shade: 0.78 },
  { n: [-1, 0, 0], c: [[0, 0, 0], [0, 0, 1], [0, 1, 1], [0, 1, 0]], shade: 0.78 },
  { n: [0, 1, 0], c: [[0, 1, 1], [1, 1, 1], [1, 1, 0], [0, 1, 0]], shade: 1 },
  { n: [0, -1, 0], c: [[0, 0, 0], [1, 0, 0], [1, 0, 1], [0, 0, 1]], shade: 0.55 },
  { n: [0, 0, 1], c: [[0, 0, 1], [1, 0, 1], [1, 1, 1], [0, 1, 1]], shade: 0.88 },
  { n: [0, 0, -1], c: [[1, 0, 0], [0, 0, 0], [0, 1, 0], [1, 1, 0]], shade: 0.88 },
];
const AO = [0.52, 0.68, 0.84, 1];
const lin = (s) => Math.pow(s, 2.2);   // shades are judged in sRGB; vertex colours are linear
const QUAD_UV = [[0, 0], [1, 0], [1, 1], [0, 1]];

/** Atlas json -> { name: [u0, v0, u1, v1] } (v up, half a texel inset so nearest sampling never bleeds). */
export function atlasRects(json) {
  const t = json.tile || 16, [W, H] = json.size, inset = 0.02, out = {};
  for (const [name, [col, row]] of Object.entries(json.tiles || {})) {
    out[name] = [(col * t + inset) / W, 1 - ((row + 1) * t - inset) / H, ((col + 1) * t - inset) / W, 1 - (row * t + inset) / H];
  }
  return out;
}

function hides(b, nb) {
  if (!nb) return false;
  if (nb === LEAVES) return b === LEAVES;
  return true;
}

/** Face-culled mesh of a volume. tile(b, f) -> name; rects from atlasRects. Returns typed arrays + the quad count. */
export function meshVoxels(vol, tile, rects, { ao = true, shade = null } = {}) {
  const pos = [], uv = [], col = [], ind = [];
  let quads = 0;
  const solid = (x, y, z) => { const b = vol.get(x, y, z); return b !== AIR && b !== CLOUD ? 1 : 0; };
  for (let y = 0; y < vol.sy; y += 1) for (let z = 0; z < vol.sz; z += 1) for (let x = 0; x < vol.sx; x += 1) {
    const b = vol.get(x, y, z);
    if (!b) continue;
    for (let f = 0; f < 6; f += 1) {
      const F = FACES[f], [nx, ny, nz] = F.n;
      if (hides(b, vol.get(x + nx, y + ny, z + nz))) continue;
      const rect = rects[tile(b, f)] || [0, 0, 1, 1];
      const base = pos.length / 3, aos = [];
      for (let k = 0; k < 4; k += 1) {
        const c = F.c[k];
        pos.push(x + c[0], y + c[1], z + c[2]);
        uv.push(rect[0] + (rect[2] - rect[0]) * QUAD_UV[k][0], rect[1] + (rect[3] - rect[1]) * QUAD_UV[k][1]);
        let a = 3;
        if (ao) {
          const ax = [0, 1, 2].filter((i) => F.n[i] === 0);
          const o = [x + nx, y + ny, z + nz], s1 = o.slice(), s2 = o.slice();
          s1[ax[0]] += c[ax[0]] ? 1 : -1;
          s2[ax[1]] += c[ax[1]] ? 1 : -1;
          const cr = s1.slice();
          cr[ax[1]] += c[ax[1]] ? 1 : -1;
          const a1 = solid(...s1), a2 = solid(...s2);
          a = a1 && a2 ? 0 : 3 - (a1 + a2 + solid(...cr));
        }
        aos.push(a);
        const v = lin((shade ? shade(f) : F.shade) * AO[a]);
        col.push(v, v, v);
      }
      if (aos[0] + aos[2] < aos[1] + aos[3]) ind.push(base, base + 1, base + 3, base + 1, base + 2, base + 3);
      else ind.push(base, base + 1, base + 2, base, base + 2, base + 3);
      quads += 1;
    }
  }
  const verts = pos.length / 3;
  return {
    positions: new Float32Array(pos), uvs: new Float32Array(uv), colors: new Float32Array(col),
    indices: verts > 65535 ? new Uint32Array(ind) : new Uint16Array(ind), quads, verts,
  };
}

/** Crossed quads for every plant; `sway` is 1 on top vertices (the wind moves only the tips). */
export function plantMesh(world, rects) {
  const pos = [], uv = [], col = [], sway = [], ind = [];
  for (const p of world.plants) {
    const rect = rects[p.kind] || [0, 0, 1, 1], h = p.kind === 'tall_grass' ? p.s : p.s * 0.9, half = 0.45 * p.s;
    const shade = lin(0.9 + (p.rot / Math.PI) * 0.1);
    for (const a of [p.rot, p.rot + Math.PI / 2]) {
      const dx = Math.cos(a) * half, dz = Math.sin(a) * half, base = pos.length / 3;
      const corners = [[-dx, 0, -dz], [dx, 0, dz], [dx, h, dz], [-dx, h, -dz]];
      corners.forEach((c, k) => {
        pos.push(p.x + c[0], p.y + c[1], p.z + c[2]);
        uv.push(rect[0] + (rect[2] - rect[0]) * QUAD_UV[k][0], rect[1] + (rect[3] - rect[1]) * QUAD_UV[k][1]);
        col.push(shade, shade, shade);
        sway.push(k >= 2 ? 1 : 0);
      });
      ind.push(base, base + 1, base + 2, base, base + 2, base + 3);
    }
  }
  return { positions: new Float32Array(pos), uvs: new Float32Array(uv), colors: new Float32Array(col),
    sway: new Float32Array(sway), indices: new Uint16Array(ind), quads: world.plants.length * 2 };
}

/** Flat blocky clouds on a wrapping grid (cells of `cell` blocks), one block thick. */
export function cloudVolume(seed, n = 48) {
  const cells = new Uint8Array(n * n);
  for (let z = 0; z < n; z += 1) for (let x = 0; x < n; x += 1) {
    const v = 0.65 * valueNoise(x / 6, z / 6, seed + 101, n / 6) + 0.35 * valueNoise(x / 3, z / 3, seed + 131, n / 3);
    cells[z * n + x] = v > 0.6 ? CLOUD : AIR;
  }
  const wrap = (v) => ((v % n) + n) % n;   // wraps, so the field repeats with no seam faces
  return { sx: n, sy: 1, sz: n, cells, get(x, y, z) { return y === 0 ? cells[wrap(z) * n + wrap(x)] : AIR; } };
}

/* ---------- walking ---------- */

export function groundAt(w, x, z) {
  const ix = Math.max(0, Math.min(w.W - 1, Math.floor(x))), iz = Math.max(0, Math.min(w.W - 1, Math.floor(z)));
  return w.heights[iz * w.W + ix];
}
export function walkBounds(w) {
  const m = 0.6;
  return { lo: w.pad + m, hi: w.pad + w.size - m };
}
function canStand(w, x, z, feet, obstacles) {
  const r = MEADOW.radius;
  for (const [ox, oz] of [[-r, -r], [r, -r], [r, r], [-r, r]]) {
    const cx = Math.floor(x + ox), cz = Math.floor(z + oz);
    if (w.trunks.has(cz * w.W + cx)) return false;
    if (groundAt(w, x + ox, z + oz) > feet + 1.05) return false;
  }
  for (const o of obstacles || []) if (Math.hypot(o.x - x, o.z - z) < o.r + r) return false;
  return true;
}
/** One walking step: clamped to the meadow, blocked by trunks, steps higher than one block and obstacles; slides. */
export function stepWalk(w, pos, dx, dz, obstacles) {
  const { lo, hi } = walkBounds(w), clamp = (v) => Math.max(lo, Math.min(hi, v));
  let { x, z } = pos;
  const feet = pos.feet ?? groundAt(w, x, z);
  const nx = clamp(x + dx);
  if (canStand(w, nx, z, feet, obstacles)) x = nx;
  const nz = clamp(z + dz);
  if (canStand(w, x, nz, feet, obstacles)) z = nz;
  return { x, z, feet, ground: groundAt(w, x, z) };
}
const wrapAngle = (a) => Math.atan2(Math.sin(a), Math.cos(a));
/** Where the slow drift wants to face: a lazy weave, turned back toward the middle near the edge. */
export function autopilotYaw(w, pos, yaw, t) {
  const c = w.pad + w.size / 2, dx = c - pos.x, dz = c - pos.z, dist = Math.hypot(dx, dz);
  let weave = yaw + 0.12 * Math.sin(t * 0.13);
  // A trunk close ahead: veer to the side it is not on, so the monitor never fills with bark.
  const [fx, fz] = forwardOf(yaw);
  for (const tr of w.trees || []) {
    const ox = tr.x + 0.5 - pos.x, oz = tr.z + 0.5 - pos.z, along = ox * fx + oz * fz, across = ox * -fz + oz * fx;
    if (along > 0 && along < 6 && Math.abs(across) < 2.2) weave = yaw + (across >= 0 ? 0.9 : -0.9);
  }
  if (dist < w.size * 0.3) return weave;
  const home = Math.atan2(-dx, -dz);   // yaw whose forward (-sin, -cos) points at the middle
  const k = Math.min(1, (dist - w.size * 0.3) / (w.size * 0.12));
  return yaw + wrapAngle(home - yaw) * k + (weave - yaw) * (1 - k);
}
export const forwardOf = (yaw) => [-Math.sin(yaw), -Math.cos(yaw)];
/** The nearest heading to `yaw` with no trunk in a 2.2-block corridor for `reach` blocks (the calm view after the lock). */
export function clearYaw(w, pos, yaw, reach = 7) {
  for (const off of [0, 0.35, -0.35, 0.7, -0.7, 1.05, -1.05, 1.4, -1.4]) {
    const [fx, fz] = forwardOf(yaw + off);
    const blocked = (w.trees || []).some((tr) => {
      const ox = tr.x + 0.5 - pos.x, oz = tr.z + 0.5 - pos.z, along = ox * fx + oz * fz;
      return along > 0 && along < reach && Math.abs(ox * -fz + oz * fx) < 2.2;
    });
    if (!blocked) return yaw + off;
  }
  return yaw;
}

/* ---------- placeholder atlas (until lane M's atlas lands; also fills any tile it lacks) ---------- */

const PH = {
  grass_top: (p) => p.fill('#5da83a').speck(['#4c9530', '#73c24b', '#68b443'], 0.45),
  dirt: (p) => p.fill('#8a5a36').speck(['#7a4d2c', '#9c6a42'], 0.4).speck(['#a0a0a0'], 0.03),
  grass_side: (p) => { p.fill('#8a5a36').speck(['#7a4d2c', '#9c6a42'], 0.4); for (let x = 0; x < 16; x += 1) { const d = 3 + Math.floor(p.r() * 3); for (let y = 0; y < d; y += 1) p.px(x, y, p.r() < 0.3 ? '#4c9530' : '#5da83a'); } },
  log_side: (p) => { p.fill('#6e4a2a'); for (let x = 0; x < 16; x += 1) { const c = p.r() < 0.3 ? '#5a3b20' : p.r() < 0.2 ? '#85603a' : null; if (c) for (let y = 0; y < 16; y += 1) if (p.r() < 0.85) p.px(x, y, c); } },
  log_top: (p) => { for (let y = 0; y < 16; y += 1) for (let x = 0; x < 16; x += 1) { const d = Math.max(Math.abs(x - 7.5), Math.abs(y - 7.5)); p.px(x, y, d > 6.6 ? '#5a3b20' : Math.floor(d) % 2 ? '#b58a55' : '#9b7243'); } },
  leaves: (p) => p.fill('#3f8a2c').speck(['#2f6e20', '#57a63c', '#356f24'], 0.5),
  tall_grass: (p) => { for (let b = 0; b < 7; b += 1) { const x0 = 1 + Math.floor(p.r() * 14), hgt = 7 + Math.floor(p.r() * 8), lean = (p.r() - 0.5) * 0.5; for (let k = 0; k < hgt; k += 1) p.px(Math.round(x0 + lean * k), 15 - k, k > hgt - 3 ? '#73c24b' : '#4f9a31'); } },
  flower_a: (p) => flower(p, '#e0403a', '#ffd23c'),
  flower_b: (p) => flower(p, '#f6d23a', '#c58a12'),
  flower_c: (p) => flower(p, '#7f72f2', '#ffffff'),
  cloud: (p) => p.fill('#ffffff').speck(['#eef4fb'], 0.15),
  sun: (p) => { p.fill('#ffd83a'); for (let y = 2; y < 14; y += 1) for (let x = 2; x < 14; x += 1) p.px(x, y, '#ffe866'); for (let y = 4; y < 12; y += 1) for (let x = 4; x < 12; x += 1) p.px(x, y, '#fff6b0'); },
  cow_body: (p) => { p.fill('#f2f2ee').speck(['#e4e4de'], 0.2); patches(p, 3); },
  cow_head: (p) => { p.fill('#f2f2ee'); for (let y = 2; y < 8; y += 1) for (let x = 1; x < 7; x += 1) p.px(x, y, '#2a2a2a'); p.px(4, 5, '#ffffff'); p.px(4, 6, '#ffffff'); p.px(11, 5, '#161616'); p.px(11, 6, '#161616'); for (let y = 10; y < 16; y += 1) for (let x = 2; x < 14; x += 1) p.px(x, y, '#f0a8b0'); p.px(5, 12, '#9c5a62'); p.px(10, 12, '#9c5a62'); },
  cow_leg: (p) => { p.fill('#f2f2ee'); patches(p, 1); for (let y = 12; y < 16; y += 1) for (let x = 0; x < 16; x += 1) p.px(x, y, '#3a2e28'); },
};
function flower(p, petal, mid) {
  for (let y = 7; y < 16; y += 1) p.px(7, y, '#3f8a2c');
  p.px(6, 11, '#4f9a31'); p.px(5, 10, '#4f9a31'); p.px(8, 13, '#4f9a31'); p.px(9, 12, '#4f9a31');
  for (let y = 2; y < 7; y += 1) for (let x = 5; x < 10; x += 1) if (!((x === 5 || x === 9) && (y === 2 || y === 6))) p.px(x, y, petal);
  p.px(7, 4, mid);
}
function patches(p, n) {
  for (let k = 0; k < n; k += 1) {
    const x0 = Math.floor(p.r() * 12), y0 = Math.floor(p.r() * 12), w = 3 + Math.floor(p.r() * 4), h = 3 + Math.floor(p.r() * 3);
    for (let y = y0; y < Math.min(16, y0 + h); y += 1) for (let x = x0; x < Math.min(16, x0 + w); x += 1) if (p.r() < 0.92) p.px(x, y, '#2a2a2a');
  }
}
/** The layout of a placeholder atlas holding `names` (4 per row), same json shape as the contract. */
export function placeholderLayout(names = TILE_NAMES) {
  const cols = 4, rows = Math.ceil(names.length / cols), tiles = {};
  names.forEach((n, i) => { tiles[n] = [i % cols, Math.floor(i / cols)]; });
  return { tile: 16, size: [cols * 16, rows * 16], tiles };
}
function paintTile(g, name, ox, oy, seed) {
  const r = rng(seed + name.length * 131 + name.charCodeAt(0) * 17);
  const p = {
    r,
    px(x, y, c) { g.fillStyle = c; g.fillRect(ox + x, oy + y, 1, 1); return p; },
    fill(c) { g.fillStyle = c; g.fillRect(ox, oy, 16, 16); return p; },
    speck(cs, rate) { for (let y = 0; y < 16; y += 1) for (let x = 0; x < 16; x += 1) if (r() < rate) p.px(x, y, cs[Math.floor(r() * cs.length)]); return p; },
  };
  (PH[name] || ((q) => q.fill('#ff00ff')))(p);
}

async function loadAtlas(atlasUrl, atlasJsonUrl) {
  let json = null, img = null;
  if (atlasUrl && atlasJsonUrl) {
    try {
      const res = await fetch(atlasJsonUrl);
      if (res.ok) {
        json = await res.json();
        img = new Image();
        img.src = atlasUrl;
        await img.decode();
      }
    } catch { json = null; img = null; }
  }
  const missing = TILE_NAMES.filter((n) => !json?.tiles?.[n]);
  if (!missing.length) return { source: img, json, placeholder: [] };
  // Placeholder tiles go in rows under whatever was loaded.
  const t = 16, baseW = img ? json.size[0] : 0, baseH = img ? json.size[1] : 0;
  const cols = Math.max(4, Math.floor(baseW / t)), extraRows = Math.ceil(missing.length / cols);
  const cv = document.createElement('canvas');
  cv.width = Math.max(baseW, cols * t);
  cv.height = baseH + extraRows * t;
  const g = cv.getContext('2d');
  if (img) g.drawImage(img, 0, 0);
  const tiles = { ...(json?.tiles || {}) };
  missing.forEach((n, i) => {
    const c = i % cols, r = Math.floor(baseH / t) + Math.floor(i / cols);
    paintTile(g, n, c * t, r * t, 7);
    tiles[n] = [c, r];
  });
  return { source: cv, json: { tile: t, size: [cv.width, cv.height], tiles }, placeholder: missing };
}

/* ---------- cows ---------- */

const pick = (rects, names) => { for (const n of names) if (rects[n]) return n; return names[names.length - 1]; };

function boxGeo(THREE, w, h, d, ox, oy, oz, faceTile, rects, mirror = -1) {
  const pos = [], uv = [], col = [], ind = [];
  FACES.forEach((F, f) => {
    const rect = rects[faceTile(f)] || [0, 0, 1, 1], base = pos.length / 3, v = lin(F.shade);
    F.c.forEach((c, k) => {
      pos.push(ox + c[0] * w, oy + c[1] * h, oz + c[2] * d);
      const u = f === mirror ? 1 - QUAD_UV[k][0] : QUAD_UV[k][0];   // the +x side shows the head side mirrored, nose forward
      uv.push(rect[0] + (rect[2] - rect[0]) * u, rect[1] + (rect[3] - rect[1]) * QUAD_UV[k][1]);
      col.push(v, v, v);
    });
    ind.push(base, base + 1, base + 2, base, base + 2, base + 3);
  });
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.setIndex(ind);
  return g;
}

function makeCowParts(THREE, rects, mat) {
  const body = pick(rects, ['cow_body']), leg = pick(rects, ['cow_leg', 'cow_body']);
  const bodyTop = pick(rects, ['cow_body_top', 'cow_body']), bodyEnd = pick(rects, ['cow_body_end', 'cow_body']);
  const face = pick(rects, ['cow_face', 'cow_head']), headSide = pick(rects, ['cow_head_side', 'cow_body']);
  const headTop = pick(rects, ['cow_head_top', 'cow_head_side', 'cow_body']);
  const ear = pick(rects, ['cow_ear', 'cow_head_side', 'cow_body']), tail = pick(rects, ['cow_tail', 'cow_leg']);
  const geos = {
    body: boxGeo(THREE, 0.9, 0.8, 1.4, -0.45, 0.62, -0.7, (f) => (f === 2 ? bodyTop : f >= 4 ? bodyEnd : body), rects),
    head: boxGeo(THREE, 0.6, 0.6, 0.55, -0.3, -0.3, 0, (f) => (f === 4 ? face : f === 2 ? headTop : headSide), rects, 0),
    ear: boxGeo(THREE, 0.16, 0.14, 0.08, -0.08, -0.07, -0.04, () => ear, rects),
    tail: boxGeo(THREE, 0.04, 0.72, 0.18, -0.02, -0.72, -0.09, () => tail, rects),
    leg: boxGeo(THREE, 0.26, 0.64, 0.26, -0.13, -0.64, -0.13, () => leg, rects),
  };
  return { geos, mat };
}

function makeCow(THREE, parts, c) {
  const g = new THREE.Group();
  g.add(new THREE.Mesh(parts.geos.body, parts.mat));
  const neck = new THREE.Group();
  neck.position.set(0, 1.12, 0.66);
  neck.add(new THREE.Mesh(parts.geos.head, parts.mat));
  for (const sx of [-1, 1]) {
    const e = new THREE.Mesh(parts.geos.ear, parts.mat);
    e.position.set(sx * 0.38, 0.2, 0.16);
    neck.add(e);
  }
  g.add(neck);
  const tail = new THREE.Mesh(parts.geos.tail, parts.mat);
  tail.position.set(0, 1.36, -0.72);
  tail.rotation.x = 0.12;
  g.add(tail);
  const legs = [[-0.28, 0.5], [0.28, 0.5], [-0.28, -0.5], [0.28, -0.5]].map(([x, z]) => {
    const l = new THREE.Mesh(parts.geos.leg, parts.mat);
    l.position.set(x, 0.66, z);
    g.add(l);
    return l;
  });
  return { g, neck, legs, tail, x: c.x, z: c.z, yaw: c.yaw, y: 0, state: 'graze', t: 1 + Math.random() * 3, tx: c.x, tz: c.z, phase: 0, head: 0.7 };
}

/** Cows keep all four feet on one level: no hanging over a step. */
function level(w, x, z, yaw, g) {
  const fx = Math.sin(yaw) * 0.75, fz = Math.cos(yaw) * 0.75;
  return groundAt(w, x, z) === g && groundAt(w, x + fx, z + fz) === g && groundAt(w, x - fx, z - fz) === g;
}

function stepCow(cow, w, dt, r, player) {
  cow.t -= dt;
  if (cow.state === 'walk') {
    const dx = cow.tx - cow.x, dz = cow.tz - cow.z, dist = Math.hypot(dx, dz);
    const want = Math.atan2(dx, dz), turn = wrapAngle(want - cow.yaw);
    cow.yaw += Math.sign(turn) * Math.min(Math.abs(turn), 1.3 * dt);
    if (Math.abs(turn) < 0.5 && dist > 0.15) {
      const sp = 0.75 * dt, nx = cow.x + Math.sin(cow.yaw) * sp, nz = cow.z + Math.cos(cow.yaw) * sp;
      const { lo, hi } = walkBounds(w);
      const free = !w.trunks.has(Math.floor(nz) * w.W + Math.floor(nx)) && nx > lo && nx < hi && nz > lo && nz < hi
        && level(w, nx, nz, cow.yaw, groundAt(w, cow.x, cow.z)) && Math.hypot(nx - player.x, nz - player.z) > 1.2;
      if (free) { cow.x = nx; cow.z = nz; cow.phase += dt * 6.5; } else cow.t = 0;
    }
    cow.head += (0 - cow.head) * Math.min(1, dt * 3);
    if (dist < 0.15 || cow.t <= 0) { cow.state = 'graze'; cow.t = 2.5 + r() * 4; }
  } else {
    cow.head += (0.75 - cow.head) * Math.min(1, dt * 2.5);
    cow.phase *= 0.9;
    if (cow.t <= 0) {
      cow.state = 'walk';
      cow.t = 6;
      const a = r() * Math.PI * 2, d = 2 + r() * 5;
      cow.tx = cow.x + Math.sin(a) * d;
      cow.tz = cow.z + Math.cos(a) * d;
    }
  }
}

/* ---------- the live scene ---------- */

const SKY_TOP = '#4f9ef0', SKY_HORIZON = '#cfe8f7';

export async function createMeadow(host, opts = {}) {
  const { atlasUrl, atlasJsonUrl, reduced = false, lite = false, seed = 7, frame: frameHook } = opts;
  const THREE = await import('three');
  const canvas = document.createElement('canvas');
  canvas.className = 'outside-meadow';
  Object.assign(canvas.style, { display: 'block', touchAction: 'none', userSelect: 'none', imageRendering: lite ? 'pixelated' : 'auto' });
  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: false, powerPreference: lite ? 'default' : 'high-performance' });
    if (!renderer.getContext()) throw new Error('no context');
  } catch (e) {
    throw new Error(`meadow: WebGL unavailable (${e?.message || e})`);
  }
  host.appendChild(canvas);
  const atlas = await loadAtlas(atlasUrl, atlasJsonUrl);
  const rects = atlasRects(atlas.json);
  const world = buildWorld({ seed, lite });
  const disposables = [];
  const keep = (o) => { disposables.push(o); return o; };

  const tex = keep(new THREE.Texture(atlas.source));
  tex.magFilter = THREE.NearestFilter;
  tex.minFilter = THREE.NearestFilter;
  tex.generateMipmaps = false;
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.needsUpdate = true;

  const scene = new THREE.Scene();
  const horizon = new THREE.Color(SKY_HORIZON);
  scene.fog = new THREE.Fog(horizon, lite ? MEADOW.fogNearLite : MEADOW.fogNear, lite ? MEADOW.fogFarLite : MEADOW.fogFar);
  const camera = new THREE.PerspectiveCamera(70, 16 / 9, 0.08, 320);
  camera.rotation.order = 'YXZ';

  // Sky dome: a direction gradient, follows the camera.
  const skyMat = keep(new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: { top: { value: new THREE.Color(SKY_TOP) }, horizon: { value: horizon.clone() } },
    vertexShader: 'varying vec3 vDir; void main(){ vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: 'uniform vec3 top; uniform vec3 horizon; varying vec3 vDir; void main(){ float h = clamp(vDir.y, 0.0, 1.0); gl_FragColor = vec4(mix(horizon, top, smoothstep(0.0, 0.55, h)), 1.0);\n#include <colorspace_fragment>\n}',
  }));
  const sky = new THREE.Mesh(keep(new THREE.SphereGeometry(250, 24, 12)), skyMat);
  sky.renderOrder = -1;
  scene.add(sky);

  const toGeo = (m, extra) => {
    const g = keep(new THREE.BufferGeometry());
    g.setAttribute('position', new THREE.BufferAttribute(m.positions, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(m.uvs, 2));
    g.setAttribute('color', new THREE.BufferAttribute(m.colors, 3));
    if (extra) for (const [k, v] of Object.entries(extra)) g.setAttribute(k, new THREE.BufferAttribute(v, 1));
    g.setIndex(new THREE.BufferAttribute(m.indices, 1));
    g.computeBoundingSphere();
    return g;
  };
  const solidMat = keep(new THREE.MeshBasicMaterial({ map: tex, vertexColors: true, alphaTest: 0.5 }));
  const terrain = meshVoxels(worldVolume(world), tileFor, rects);
  scene.add(new THREE.Mesh(toGeo(terrain), solidMat));

  // Plants: one double-sided draw, tips sway in a slow wind.
  const timeU = { value: 0 };
  const plantMat = keep(new THREE.MeshBasicMaterial({ map: tex, vertexColors: true, alphaTest: 0.5, side: THREE.DoubleSide }));
  plantMat.onBeforeCompile = (sh) => {
    sh.uniforms.uTime = timeU;
    sh.vertexShader = 'uniform float uTime;\nattribute float aSway;\n' + sh.vertexShader.replace('#include <begin_vertex>',
      '#include <begin_vertex>\ntransformed.x += aSway * sin(uTime * 1.6 + position.x * 0.7 + position.z * 0.5) * 0.07;\ntransformed.z += aSway * cos(uTime * 1.2 + position.x * 0.4) * 0.05;');
  };
  const plants = plantMesh(world, rects);
  scene.add(new THREE.Mesh(toGeo(plants, { aSway: plants.sway }), plantMat));

  // Clouds: two copies of one wrapping field, drifting east.
  const CELL = 5, CN = 48, SPAN = CELL * CN;
  const cloudMesh = meshVoxels(cloudVolume(seed), (b, f) => (f === 3 && rects.cloud_under ? 'cloud_under' : 'cloud'), rects, { ao: false, shade: (f) => (f === 2 ? 1 : f === 3 ? 0.94 : 0.86) });
  const cloudMat = keep(new THREE.MeshBasicMaterial({ map: tex, vertexColors: true, transparent: true, opacity: 0.92, fog: false, depthWrite: false }));
  const cloudGeo = toGeo(cloudMesh);
  const clouds = [0, 1].map((k) => {
    const m = new THREE.Mesh(cloudGeo, cloudMat);
    m.scale.set(CELL, 1.6, CELL);
    m.position.set(world.W / 2 - SPAN / 2 - k * SPAN, 34, world.W / 2 - SPAN / 2);
    scene.add(m);
    return m;
  });

  // Square sun, always the same direction from the eye.
  const sunRect = rects.sun || [0, 0, 1, 1];
  const sunGeo = keep(new THREE.PlaneGeometry(13, 13));
  const suv = sunGeo.attributes.uv;
  for (let i = 0; i < suv.count; i += 1) suv.setXY(i, suv.getX(i) ? sunRect[2] : sunRect[0], suv.getY(i) ? sunRect[3] : sunRect[1]);
  const sun = new THREE.Mesh(sunGeo, keep(new THREE.MeshBasicMaterial({ map: tex, fog: false, depthWrite: false, alphaTest: 0.5 })));
  scene.add(sun);
  const sunDir = new THREE.Vector3(0.3, 0.38, -1).normalize();

  // Cows.
  const parts = makeCowParts(THREE, rects, solidMat);
  Object.values(parts.geos).forEach(keep);
  const cowRng = rng(seed + 991);
  const cows = world.cows.map((c) => { const cow = makeCow(THREE, parts, c); scene.add(cow.g); return cow; });

  /* ---- player ---- */
  const me = { x: world.start.x, z: world.start.z, yaw: world.start.yaw, pitch: world.start.pitch, feet: groundAt(world, world.start.x, world.start.z), bob: 0, walk: 0 };
  let live = false, auto = false, running = false, lost = false, cancel = null, raf = 0, last = 0, t = 0, frames = 0;
  let ease = null;
  const keys = new Set(), pointers = new Map();
  let mouseX = -1, mouseHold = false;

  const onKey = (e) => {
    if (!live) return;
    const k = e.code;
    if (!/^(Key[WASD]|Arrow(Up|Down|Left|Right))$/.test(k)) return;
    if (e.type === 'keydown') keys.add(k); else keys.delete(k);
    if (k.startsWith('Arrow')) e.preventDefault();
  };
  const onBlur = () => { keys.clear(); pointers.clear(); mouseHold = false; };
  const onDown = (e) => {
    if (!live) return;
    if (e.pointerType === 'mouse') { if (e.button === 0) mouseHold = true; return; }
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, sx: e.clientX, sy: e.clientY, at: performance.now(), moved: false });
    canvas.setPointerCapture?.(e.pointerId);
  };
  const onMove = (e) => {
    if (!live) return;
    if (e.pointerType === 'mouse') {
      const r = canvas.getBoundingClientRect();
      mouseX = r.width ? (e.clientX - r.left) / r.width : -1;
      me.yaw -= (e.movementX || 0) * 0.0042;
      me.pitch -= (e.movementY || 0) * 0.0036;
      return;
    }
    const p = pointers.get(e.pointerId);
    if (!p) return;
    if (!p.moved && Math.hypot(e.clientX - p.sx, e.clientY - p.sy) > 12) p.moved = true;
    if (p.moved) { me.yaw += (e.clientX - p.x) * 0.0062; me.pitch += (e.clientY - p.y) * 0.005; }
    p.x = e.clientX; p.y = e.clientY;
  };
  const onUp = (e) => {
    if (e.pointerType === 'mouse') { mouseHold = false; return; }
    pointers.delete(e.pointerId);
  };
  const onLeave = (e) => { if (e.pointerType === 'mouse') { mouseX = -1; mouseHold = false; } };
  const noMenu = (e) => { if (live) e.preventDefault(); };
  addEventListener('keydown', onKey);
  addEventListener('keyup', onKey);
  addEventListener('blur', onBlur);
  canvas.addEventListener('pointerdown', onDown);
  canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp);
  canvas.addEventListener('pointercancel', onUp);
  canvas.addEventListener('pointerleave', onLeave);
  canvas.addEventListener('contextmenu', noMenu);
  canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); lost = true; });

  const PITCH_MAX = 1.25;
  function update(dt) {
    t += dt;
    let fwd = 0, side = 0, speed = MEADOW.walkSpeed;
    if (live) {
      if (keys.has('KeyW') || keys.has('ArrowUp') || mouseHold) fwd += 1;
      if (keys.has('KeyS') || keys.has('ArrowDown')) fwd -= 1;
      if (keys.has('KeyA')) side -= 1;
      if (keys.has('KeyD')) side += 1;
      if (keys.has('ArrowLeft')) me.yaw += 1.9 * dt;
      if (keys.has('ArrowRight')) me.yaw -= 1.9 * dt;
      if (mouseX >= 0 && !reduced) {   // resting near an edge keeps turning: no pointer lock needed
        if (mouseX < 0.06) me.yaw += 1.1 * dt * (1 - mouseX / 0.06);
        else if (mouseX > 0.94) me.yaw -= 1.1 * dt * ((mouseX - 0.94) / 0.06);
      }
      const now = performance.now();
      for (const p of pointers.values()) if (!p.moved && now - p.at > 220) { fwd = Math.max(fwd, 1); break; }
    } else if (ease) {
      ease.t = Math.min(1, ease.t + dt / ease.dur);
      const k = ease.t < 0.5 ? 2 * ease.t * ease.t : 1 - Math.pow(-2 * ease.t + 2, 2) / 2;
      me.yaw = ease.y0 + wrapAngle(ease.y1 - ease.y0) * k;
      me.pitch = ease.p0 + (ease.p1 - ease.p0) * k;
      if (ease.t >= 1) ease = null;
    }
    if (auto && !live) {
      if (!ease) {
        const turn = wrapAngle(autopilotYaw(world, me, me.yaw, t) - me.yaw);
        me.yaw += Math.max(-0.25, Math.min(0.25, turn * 0.5)) * dt;
        me.pitch += (0.06 - me.pitch) * Math.min(1, dt * 0.8);
      }
      fwd = 1;
      speed = MEADOW.autoSpeed;
    }
    me.pitch = Math.max(-PITCH_MAX, Math.min(PITCH_MAX, me.pitch));
    if (fwd || side) {
      const len = Math.hypot(fwd, side), [fx, fz] = forwardOf(me.yaw), s = (speed * dt) / len;
      const dx = (fx * fwd - fz * side) * s, dz = (fz * fwd + fx * side) * s;
      const obstacles = cows.map((c) => ({ x: c.x, z: c.z, r: 0.75 }));
      const next = stepWalk(world, me, dx, dz, obstacles);
      const moved = Math.hypot(next.x - me.x, next.z - me.z);
      if (auto && !live && moved < 0.2 * Math.hypot(dx, dz)) me.yaw += 0.8 * dt;   // stuck on a trunk: turn away
      me.x = next.x; me.z = next.z;
      me.bob += moved * 3.6;
      me.walk = Math.min(1, me.walk + dt * 4);
    } else me.walk = Math.max(0, me.walk - dt * 3);
    const ground = groundAt(world, me.x, me.z);
    me.feet = ground > me.feet ? Math.min(ground, me.feet + 7 * dt) : Math.max(ground, me.feet - 9 * dt);
    const bobAmp = reduced ? 0 : (auto && !live ? 0.025 : 0.055) * me.walk;
    camera.position.set(me.x, me.feet + MEADOW.eye + Math.sin(me.bob) * bobAmp, me.z);
    camera.rotation.set(me.pitch, me.yaw, Math.sin(me.bob * 0.5) * bobAmp * 0.12);

    for (const c of cows) {
      if (!reduced || c.state === 'walk') stepCow(c, world, dt * (reduced ? 0.5 : 1), cowRng, me);
      c.y += (groundAt(world, c.x, c.z) - c.y) * Math.min(1, dt * 6);
      c.g.position.set(c.x, c.y, c.z);
      c.g.rotation.y = c.yaw;
      const swing = Math.sin(c.phase) * 0.5 * (c.state === 'walk' ? 1 : 0);
      c.legs.forEach((l, i) => { l.rotation.x = (i === 0 || i === 3 ? swing : -swing); });
      c.tail.rotation.z = reduced ? 0 : Math.sin(t * 1.7 + c.z) * 0.18;
      c.neck.rotation.x = c.head + (c.state === 'graze' && !reduced ? Math.sin(t * 4.4 + c.x) * 0.04 : 0);
    }
    const drift = reduced ? 0 : 0.55;
    clouds.forEach((m, k) => { m.position.x = world.W / 2 - SPAN / 2 + ((t * drift) % SPAN) - k * SPAN; });
    if (!reduced) timeU.value = t;
    sky.position.copy(camera.position);
    sun.position.copy(camera.position).addScaledVector(sunDir, 150);
    sun.lookAt(camera.position);
  }
  const render = () => { if (!lost) { renderer.render(scene, camera); frames += 1; } };
  const step = (dt) => { if (!running) return; update(Math.min(0.05, Math.max(0, dt))); render(); };
  const loop = (ts) => {
    raf = requestAnimationFrame(loop);
    const dt = last ? (ts - last) / 1000 : 1 / 60;
    last = ts;
    step(dt);
  };

  function resize() {
    const w = host.clientWidth || innerWidth, h = host.clientHeight || innerHeight;
    const dpr = globalThis.devicePixelRatio || 1;
    renderer.setPixelRatio(lite ? Math.min(dpr, 1) * 0.7 : Math.min(dpr, 1.5));
    renderer.setSize(w, h, true);
    camera.aspect = w / Math.max(1, h);
    camera.fov = camera.aspect < 1 ? 82 : 70;
    camera.updateProjectionMatrix();
  }

  resize();
  update(0);
  render();

  let stopped = false;
  const api = {
    canvas,
    start() {
      if (stopped || running) return;
      running = true;
      live = true;
      if (frameHook) cancel = frameHook((dt) => step(dt));
      else { last = 0; raf = requestAnimationFrame(loop); }
    },
    lockControls() {
      if (!live) return;
      live = false;
      keys.clear(); pointers.clear(); mouseHold = false; mouseX = -1;
      ease = { t: 0, dur: reduced ? 0.6 : 1, y0: me.yaw, y1: clearYaw(world, me, me.yaw), p0: me.pitch, p1: 0.06 };
    },
    autopilot(on) { auto = !!on; },
    resize,
    still(quality = 0.86) {
      update(0);
      render();
      return canvas.toDataURL('image/jpeg', quality);
    },
    stop() {
      if (stopped) return;
      stopped = true;
      running = false;
      live = false;
      if (raf) cancelAnimationFrame(raf);
      cancel?.();
      removeEventListener('keydown', onKey);
      removeEventListener('keyup', onKey);
      removeEventListener('blur', onBlur);
      canvas.removeEventListener('pointerdown', onDown);
      canvas.removeEventListener('pointermove', onMove);
      canvas.removeEventListener('pointerup', onUp);
      canvas.removeEventListener('pointercancel', onUp);
      canvas.removeEventListener('pointerleave', onLeave);
      canvas.removeEventListener('contextmenu', noMenu);
      for (const d of disposables) d.dispose?.();
      renderer.dispose();
      renderer.forceContextLoss?.();
      canvas.remove();
    },
    /** For the dev page and drivers: where the eye is and how much has been drawn. */
    debug: () => ({ x: me.x, z: me.z, yaw: me.yaw, pitch: me.pitch, live, auto, frames, quads: terrain.quads, plants: plants.quads,
      cows: cows.map((c) => ({ x: c.x, z: c.z, state: c.state })), placeholder: atlas.placeholder, calls: renderer.info.render.calls }),
  };
  return api;
}
