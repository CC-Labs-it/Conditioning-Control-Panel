/* One construction plan owns both the furniture and its mounted moving pictures. */
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { axesFrom, roadSamples, V3 } from './mediaMath.js';

const { add, sub, mul, dot, norm } = V3, TAU = Math.PI * 2;
const plans = new WeakMap();
const WORLD = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
const at = (f, x, y, z = 0) => add(add(add(f.p, mul(f.b, x)), mul(f.n, y)), mul(f.t, z));
const basisAt = f => [[...f.b], [...f.n], f.t.map(v => -v)];
const yawBasis = a => [[Math.cos(a), 0, -Math.sin(a)], [0, 1, 0], [Math.sin(a), 0, Math.cos(a)]];
const bound = row => row.size.map(v => v / 2);

/** Slab test for a sightline against the same oriented bounds used for road clearance. */
export function boudoirSegmentBlocked(from, to, row) {
  const d = sub(to, from), origin = sub(from, row.p), axes = row.axes || WORLD;
  let near = 0, far = 1;
  for (let i = 0; i < 3; i++) {
    const p = dot(origin, axes[i]), velocity = dot(d, axes[i]), half = row.size[i] / 2;
    if (Math.abs(velocity) < 1e-8) { if (Math.abs(p) > half) return false; continue; }
    const a = (-half - p) / velocity, b = (half - p) / velocity;
    near = Math.max(near, Math.min(a, b)); far = Math.min(far, Math.max(a, b));
    if (near > far) return false;
  }
  return far > .001 && near < .999;
}

/** Conservative oriented-box separation, including surfaces beneath a stacked crossing. */
export function boudoirBoxesIntersect(a, b, margin = 0) {
  const A = a.axes || WORLD, B = b.axes || WORLD, ah = bound(a), bh = bound(b), d = sub(b.p, a.p);
  if (Math.hypot(...d) > Math.hypot(...ah) + Math.hypot(...bh) + margin) return false;
  const cross = (x, y) => [x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]];
  for (const axis of [...A, ...B, ...A.flatMap(x => B.map(y => cross(x, y)))]) {
    const length = Math.hypot(...axis); if (length < 1e-7) continue;
    const reach = A.reduce((n, x, i) => n + Math.abs(dot(x, axis)) * ah[i], 0)
      + B.reduce((n, x, i) => n + Math.abs(dot(x, axis)) * bh[i], 0);
    if (Math.abs(dot(d, axis)) > reach + margin * length) return false;
  }
  return true;
}

export function boudoirWorldPlan(track) {
  const layout = getBoudoirLayout(track); if (!layout) return null;
  if (plans.has(track)) return plans.get(track);
  const road = roadSamples(track, 5), rows = [], mounts = [], heroes = [], interiors = [], particles = [];
  const corridors = road.map(f => ({ p: at(f, 0, 6), size: [f.w + 3, 16, 8], axes: basisAt(f) }));
  // Curved banks rise above their centreline. Check actual shoulder surfaces as well.
  for (const f of road) if (track.frameAt(f.s).surface === 'stream') for (const share of [-.95, -.7, -.35, .35, .7, .95]) {
    const surface = track.surfaceAt(f.s, f.w / 2 * share);
    corridors.push({ p: at(surface, 0, 6), size: [f.w / 5 + 2, 16, 8], axes: basisAt(surface) });
  }
  const clear = row => !corridors.some(c => boudoirBoxesIntersect(row, c));
  let rejected = 0;
  function put(name, material, p, size, { shape = 'box', axes = WORLD, checked = true } = {}) {
    const row = { name, material, p, size, axes, shape };
    if (checked && !clear(row)) { rejected++; return null; }
    rows.push(row); return row;
  }
  function local(name, material, f, x, y, z, size, shape = 'box') {
    return put(name, material, at(f, x, y, z), size, { shape, axes: basisAt(f) });
  }
  const pointBounds = road.reduce((b, f) => {
    f.p.forEach((v, i) => { b.min[i] = Math.min(b.min[i], v - (i === 1 ? 0 : f.w / 2)); b.max[i] = Math.max(b.max[i], v + (i === 1 ? 0 : f.w / 2)); });
    return b;
  }, { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] });
  const center = pointBounds.min.map((v, i) => (v + pointBounds.max[i]) / 2);
  const groundY = pointBounds.min[1] - 55, ceilingY = pointBounds.max[1] + 145;
  const roomW = pointBounds.max[0] - pointBounds.min[0] + 440, roomD = pointBounds.max[2] - pointBounds.min[2] + 440;
  const roomH = ceilingY - groundY;
  // The whole course is inside a real room. Distant wall panels and a coffered ceiling give scale.
  put('dressing-room-parquet', 'floor', [center[0], groundY - 4, center[2]], [roomW, 8, roomD]);
  put('dressing-room-ceiling', 'cream', [center[0], ceilingY + 4, center[2]], [roomW, 8, roomD]);
  for (const side of [-1, 1]) {
    put('dressing-room-wall', 'rose', [center[0] + side * roomW / 2, groundY + roomH / 2, center[2]], [8, roomH, roomD]);
    put('dressing-room-wall', 'rose', [center[0], groundY + roomH / 2, center[2] + side * roomD / 2], [roomW, roomH, 8]);
    for (const y of [groundY + 18, groundY + 76, ceilingY - 17]) {
      put('room-gold-moulding', 'gold', [center[0] + side * (roomW / 2 - 6), y, center[2]], [3, 3, roomD]);
      put('room-gold-moulding', 'gold', [center[0], y, center[2] + side * (roomD / 2 - 6)], [roomW, 3, 3]);
    }
    for (let j = 1; j < 12; j++) {
      const x = center[0] + (j / 12 - .5) * roomW, z = center[2] + (j / 12 - .5) * roomD;
      put('room-wall-flute', 'cream', [x, groundY + roomH / 2, center[2] + side * (roomD / 2 - 7)], [4, roomH - 35, 5]);
      put('room-wall-flute', 'cream', [center[0] + side * (roomW / 2 - 7), groundY + roomH / 2, z], [5, roomH - 35, 4]);
      put('ceiling-coffer', 'gold', [x, ceilingY - 3, center[2]], [1.8, 1.8, roomD]);
      put('ceiling-coffer', 'gold', [center[0], ceilingY - 3, z], [roomW, 1.8, 1.8]);
    }
  }
  // A wardrobe is a shell with open shelves, not a solid tower obstructing the spiral road.
  for (const furniture of layout.furniture || []) {
    const radius = furniture.radiusM || 90, low = furniture.baseY ?? groundY + 5, high = furniture.topY ?? ceilingY - 75;
    const x = furniture.x, z = furniture.z, height = high - low, width = radius * 2 + 130, depth = radius * 2 + 130;
    const wardrobe = /wardrobe/.test(furniture.id), material = wardrobe ? 'lavender' : 'cream';
    for (const side of [-1, 1]) {
      put(furniture.id + '-outer-side', material, [x + side * width / 2, (low + high) / 2, z], [9, height, depth]);
      put(furniture.id + '-gold-pilaster', 'gold', [x + side * (width / 2 - 7), (low + high) / 2, z + depth / 2 + 3], [3, height + 12, 7]);
      put(furniture.id + '-foot', 'gold', [x + side * width * .43, groundY + 20, z + depth * .35], [15, 40, 15], { shape: 'sphere' });
      // Doors splay outward, so the climbing route can be seen from other levels.
      const angle = side * .37, doorX = x + side * (width * .51 + 25);
      put(furniture.id + '-open-door', material, [doorX, (low + high) / 2, z + depth / 2 + 28], [54, height - 4, 8], { axes: yawBasis(angle) });
      put(furniture.id + '-door-mirror', 'mirror', [doorX, (low + high) / 2 + 5, z + depth / 2 + 33], [43, height - 38, 1.5], { axes: yawBasis(angle) });
      put(furniture.id + '-door-knob', 'gold', [doorX - side * 17, low + height * .46, z + depth / 2 + 41], [5, 5, 5], { shape: 'sphere' });
    }
    put(furniture.id + '-back', material, [x, (low + high) / 2, z - depth / 2], [width, height, 8]);
    put(furniture.id + '-crown', 'gold', [x, high + 4, z], [width + 24, 8, depth + 16]);
    put(furniture.id + '-toe-board', material, [x, low - 5, z], [width + 10, 10, depth + 10]);
    const levels = Math.max(3, Math.ceil(height / 48));
    for (let k = 1; k < levels; k++) {
      const y = low + k * height / levels;
      // Short shelf modules are omitted individually wherever any level of road enters them.
      for (let a = -2; a <= 2; a++) for (let b = -2; b <= 2; b++)
        put(furniture.id + '-shelf', material, [x + a * width / 5, y, z + b * depth / 5], [width / 5 + .1, 4, depth / 5 + .1]);
      for (const side of [-1, 1]) {
        put(furniture.id + '-shelf-gold-edge', 'gold', [x + side * (width / 2 - 12), y + 3, z], [4, 3, depth]);
        for (let n = 0; n < 3; n++) {
          const hx = x + side * (width / 2 - 26), hz = z - depth * .27 + n * depth * .22;
          put('wardrobe-folded-satin', n % 2 ? 'rose' : 'lavender', [hx, y + 7, hz], [30, 8, 23]);
          put('wardrobe-folded-satin-trim', 'gold', [hx, y + 11.2, hz], [29, .7, 22]);
        }
      }
    }
    // Hanging gowns occupy the rear margin, with fitted bodies and widening skirts.
    if (wardrobe) for (let n = -2; n <= 2; n++) {
      const px = x + n * width * .16, pz = z - depth * .35, py = high - 30;
      put('wardrobe-garment-rail', 'gold', [x, py + 8, pz], [width * .87, 2.5, 2.5]);
      put('wardrobe-hanger', 'gold', [px, py + 2, pz], [24, 2, 3]);
      put('wardrobe-gown-bodice', n % 2 ? 'rose' : 'lavender', [px, py - 12, pz], [15, 26, 9], { shape: 'sphere' });
      put('wardrobe-gown-skirt', n % 2 ? 'rose' : 'lavender', [px, py - 43, pz], [34, 44, 21], { shape: 'cone' });
    }
    heroes.push({ id: furniture.id, p: [x, low, z], width, depth, height });
  }
  // Long lacquered drawers enclose the racing lane. The route itself cuts every opening.
  for (const hall of layout.halls || []) {
    const span = hall.s1 - hall.s0, count = Math.max(2, Math.ceil(span / 10)), step = span / count;
    interiors.push({ id: hall.id, s0: hall.s0, s1: hall.s1, heightM: hall.heightM || 25 });
    for (let j = 0; j < count; j++) {
      const s = hall.s0 + (j + .5) * step, f = track.frameAt(s), height = hall.heightM || 25;
      for (const side of [-1, 1]) {
        local('drawer-lacquer-wall', 'cream', f, side * (f.w / 2 + 8.5), height / 2 - 2, 0, [6, height, step + .4]);
        local('drawer-velvet-inset', 'rose', f, side * (f.w / 2 + 5.4), 7, 0, [.3, 12, step * .82]);
        local('drawer-gold-skirt', 'gold', f, side * (f.w / 2 + 5.2), 1.8, 0, [.5, 1.5, step + .2]);
        if (j % 3 === 0) local('drawer-fluted-joint', 'gold', f, side * (f.w / 2 + 5), height / 2, 0, [.7, height - 2, .7]);
      }
      local('drawer-underside', 'lavender', f, 0, -5.5, 0, [f.w + 23, 5, step + .3]);
      local('drawer-ceiling', 'cream', f, 0, height + 2, 0, [f.w + 23, 4, step + .3]);
      if (j % 4 === 0) local('drawer-ceiling-inlay', 'gold', f, 0, height - .3, 0, [f.w + 8, .7, 1]);
    }
    for (const s of [hall.s0 + 2, hall.s1 - 2]) {
      const f = track.frameAt(s), height = hall.heightM || 25;
      local('drawer-front-lintel', 'rose', f, 0, height + 7, 0, [f.w + 20, 10, 9]);
      local('drawer-handle-stem', 'gold', f, 0, height + 8, -7, [2, 2, 8]);
      local('drawer-handle', 'gold', f, 0, height + 8, -11, [18, 8, 4], 'torus');
    }
  }
  const landmark = id => layout.landmarks.find(l => l.id === id);
  const fitHero = (id, fraction, side, size) => {
    const l = landmark(id); if (!l) return null;
    const s = l.s0 + (l.s1 - l.s0) * fraction, f = track.frameAt(s);
    for (let k = 0; k < 24; k++) {
      const offset = f.w / 2 + size[0] / 2 + 15 + k * 8;
      const p = at(f, side * offset, size[1] / 2 - 5), row = { p, size, axes: basisAt(f) };
      if (clear(row) && !heroes.some(h => Math.hypot(h.p[0] - p[0], h.p[2] - p[2]) < size[0] / 2 + 18)) {
        heroes.push({ id, s, p, size, axes: row.axes }); return { s, f, offset: side * offset, height: size[1] };
      }
    }
    return null;
  };
  function lipstick(id, fraction, side, height = 65) {
    const h = fitHero(id, fraction, side, [27, height + 10, 27]); if (!h) return;
    const { f, offset: x } = h;
    local('lipstick-case', 'lavender', f, x, height * .2 - 4, 0, [24, height * .4, 24], 'cylinder');
    local('lipstick-gold-collar', 'gold', f, x, height * .43 - 4, 0, [23, height * .08, 23], 'cylinder');
    local('lipstick-colour', 'lip', f, x, height * .69 - 4, 0, [17, height * .45, 17], 'lipstick');
    local('lipstick-foot', 'gold', f, x, -4, 0, [27, 3, 27], 'cylinder');
  }
  function perfume(id, fraction, side, height = 60) {
    const h = fitHero(id, fraction, side, [49, height + 10, 39]); if (!h) return;
    const { f, offset: x } = h;
    local('perfume-flacon', 'glass', f, x, height * .3 - 4, 0, [38, height * .6, 29], 'sphere');
    local('perfume-gold-neck', 'gold', f, x, height * .67 - 4, 0, [15, height * .16, 15], 'cylinder');
    local('perfume-pump', 'gold', f, x, height * .81 - 4, 0, [24, 6, 12]);
    local('perfume-label', 'cream', f, x, height * .31 - 4, -14.8, [22, 15, 1.2]);
    local('perfume-label-medallion', 'gold', f, x, height * .31 - 4, -15.5, [10, 10, 1], 'torus');
    local('atomizer-bulb', 'rose', f, x + side * 20, height * .65 - 4, 0, [15, 11, 11], 'sphere');
    for (let n = 0; n < 10; n++) local('atomizer-cord', 'gold', f, x + side * (7 + n * 1.5), height * .71 - 4 - Math.sin(n / 9 * Math.PI) * 6, 0, [2.1, 2.1, 2.1], 'sphere');
  }
  function mirror(id, fraction, side, height = 85) {
    const h = fitHero(id, fraction, side, [48, height + 10, 22]); if (!h) return;
    const { f, offset: x } = h;
    local('vanity-mirror-foot', 'gold', f, x, -2, 0, [43, 7, 22], 'sphere');
    local('vanity-mirror-stem', 'gold', f, x, 9, 0, [5, 22, 7]);
    local('vanity-mirror-frame', 'gold', f, x, height * .56, 0, [46, height * .78, 5], 'torus');
    local('vanity-mirror-silver', 'mirror', f, x, height * .56, 0, [42, height * .74, 2], 'disc');
    for (let n = 0; n < 22; n++) {
      const a = n / 22 * TAU;
      local('vanity-mirror-bulb', 'pearl', f, x + Math.cos(a) * 22.5, height * .56 + Math.sin(a) * height * .385, -3.5, [2.6, 2.6, 2.6], 'sphere');
    }
    for (const sign of [-1, 1]) local('vanity-bow-loop', 'rose', f, x + sign * 9, height * .99, 0, [19, 10, 8], 'sphere');
    local('vanity-bow-knot', 'gold', f, x, height * .99, -2, [8, 8, 7], 'sphere');
  }
  for (const [id, f, side] of [['vanity-grid', .38, 1], ['powder-return', .65, -1], ['shelf-gallery', .5, 1], ['mirror-approach', .35, -1], ['home-turn', .4, 1]]) mirror(id, f, side);
  for (const [id, f, side] of [['powder-sweep', .55, -1], ['ribbon-weave', .45, 1], ['upper-turn', .55, -1], ['pearl-slalom-cross', .4, 1], ['compacts-exit', .65, 1]]) lipstick(id, f, side);
  for (const [id, f, side] of [['perfume-court', .24, -1], ['perfume-court', .72, 1], ['bath-approach', .6, 1], ['bath-turn', .5, -1], ['wardrobe-out', .6, -1]]) perfume(id, f, side);
  // The start is the top of a complete, oversized vanity, with paneled drawers underneath.
  const vanity = landmark('vanity-grid');
  if (vanity) {
    const f = track.frameAt(vanity.s0 + (vanity.s1 - vanity.s0) * .35), depth = Math.min(160, vanity.s1 - vanity.s0 + 45), width = f.w + 130;
    local('vanity-tabletop', 'cream', f, 0, -8, 0, [width, 9, depth]);
    local('vanity-top-gold-edge', 'gold', f, 0, -3.6, -depth / 2, [width + 2, 1.5, 2]);
    for (const side of [-1, 1]) {
      local('vanity-drawer-cabinet', 'rose', f, side * (width / 2 - 24), -31, 0, [47, 47, depth - 12]);
      for (let level = 0; level < 3; level++) {
        local('vanity-drawer-face', 'cream', f, side * (width / 2 - 24), -14 - level * 15, -depth / 2 + 4, [42, 12, 3]);
        local('vanity-drawer-handle', 'gold', f, side * (width / 2 - 24), -14 - level * 15, -depth / 2, [13, 4, 4], 'torus');
      }
      local('vanity-cabriole-foot', 'gold', f, side * (width / 2 - 19), -60, -depth * .35, [16, 18, 16], 'sphere');
    }
  }
  // Pearl strings follow only railed road edges. Open edges retain their danger and water spills.
  for (let s = 0; s < track.lengthM; s += 12) {
    const f = track.frameAt(s), flags = f.flags || '';
    if (f.surface === 'stream' || f.surface === 'pool' || (track.fields || []).some(a => s >= a.fromS && s <= a.toS)) continue;
    for (const side of [-1, 1]) if (flags.includes('rail') || flags.includes(side < 0 ? 'railL' : 'railR')) {
      local('pearl-road-trim', 'pearl', f, side * (f.w / 2 + 2.6), .8, 0, [2.2, 2.2, 2.2], 'sphere');
      if (Math.floor(s / 12) % 4 === 0) particles.push({ p: at(f, side * (f.w / 2 + 7), 2), phase: s * .17 });
    }
  }
  // Mounted mirrors share one half-media / half-Loom pool. Every face is checked after all solids.
  const picks = layout.landmarks.filter(l => !/gap|ramp|landing|compacts$|chute/.test(l.id));
  for (const l of picks) for (const [j, u] of [0.3, 0.72].entries()) {
    const s = l.s0 + (l.s1 - l.s0) * u, f = track.frameAt(s), side = j ? 1 : -1;
    const inside = (layout.halls || []).some(h => s > h.s0 && s < h.s1);
    const Z = norm(add(mul(f.b, -side), mul(f.t, inside ? 0 : -.65))), axes = axesFrom(Z, f.n), w = inside ? 15 : 21, h = inside ? 8.4 : 12;
    for (let attempt = 0; attempt < (inside ? 1 : 30); attempt++) {
      const C = at(f, side * (f.w / 2 + (inside ? 3 : 10) + attempt % 10 * 5), (inside ? 8 : 11) + Math.floor(attempt / 10) * 10);
      const face = { p: C, size: [w + 2, h + 2, 1.5], axes: [axes.X, axes.Y, axes.Z] };
      if (!clear(face) || rows.some(r => boudoirBoxesIntersect(face, r, .2))) continue;
      const facePoints = [C, ...[-1, 1].flatMap(x => [-1, 1].map(y => add(add(C, mul(axes.X, x * w * .4)), mul(axes.Y, y * h * .4))))];
      const eyes = [30, 45, 60].map(lead => at(track.frameAt(s - lead), 0, 3));
      if (eyes.some(eye => facePoints.some(point => rows.some(row => boudoirSegmentBlocked(eye, point, row))))) continue;
      const mount = { id: 'boudoir-mirror-' + l.id + '-' + j, s, frame: inside ? 'tunnel' : 'billboard', kind: 'landscape', phone: true,
        w, h, C, ...axes, program: (mounts.length >> 1) % 2 ? 3 : 0 };
      mounts.push(mount);
      const back = sub(C, mul(axes.Z, 1));
      put('mounted-mirror-backing', 'lavender', back, [w + 2, h + 2, 1.2], { axes: face.axes });
      for (const sign of [-1, 1]) {
        put('mounted-mirror-gold', 'gold', add(back, mul(axes.X, sign * (w / 2 + .9))), [1, h + 2.5, 2.5], { axes: face.axes });
        put('mounted-mirror-gold', 'gold', add(back, mul(axes.Y, sign * (h / 2 + .9))), [w + 2.5, 1, 2.5], { axes: face.axes });
      }
      const foot = sub(back, mul(axes.Y, h / 2 + 5));
      put('mounted-mirror-stem', 'gold', foot, [1.5, 10, 1.5], { axes: face.axes });
      break;
    }
  }
  // A paired cohort preserves an exact media/Loom split at every quality tier.
  for (const kind of ['billboard', 'tunnel']) {
    const group = mounts.filter(m => m.frame === kind);
    if (group.length % 2) mounts.splice(mounts.indexOf(group.at(-1)), 1);
  }
  const plan = { rows, mounts, heroes, interiors, particles, corridors, rejected, center, groundY, ceilingY, roomW, roomD };
  plans.set(track, plan); return plan;
}

export function boudoirMediaLayout(track) {
  const plan = boudoirWorldPlan(track); if (!plan) return null;
  return { track: 'boudoir', slots: plan.mounts.map(m => ({ ...m, C: [...m.C], X: [...m.X], Y: [...m.Y], Z: [...m.Z] })) };
}
