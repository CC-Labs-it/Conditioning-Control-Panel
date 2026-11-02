/* Closed letter silhouettes from the bundled canvas font. No backing surface is generated. */
import { ShapePath, ExtrudeGeometry, BufferGeometry } from 'three/webgpu';

const area = points => points.reduce((a, p, i) => {
  const q = points[(i + 1) % points.length]; return a + p[0] * q[1] - q[0] * p[1];
}, 0) / 2;
const distance = (p, a, b) => {
  const x = b[0] - a[0], y = b[1] - a[1], l = x * x + y * y;
  const u = l ? Math.max(0, Math.min(1, ((p[0] - a[0]) * x + (p[1] - a[1]) * y) / l)) : 0;
  return Math.hypot(p[0] - a[0] - u * x, p[1] - a[1] - u * y);
};
function simplify(points, epsilon) {
  if (points.length < 5) return points;
  const last = points.length - 1; let far = 0, index = 0;
  for (let i = 1; i < last; i++) { const d = distance(points[i], points[0], points[last]); if (d > far) { far = d; index = i; } }
  if (far <= epsilon) return [points[0], points[last]];
  return [...simplify(points.slice(0, index + 1), epsilon).slice(0, -1), ...simplify(points.slice(index), epsilon)];
}

/** Cell boundaries retain counters (the holes in O, B, e) and disconnected letter islands. */
export function letterContours(alpha, width, height, stride = 4) {
  const occupied = (x, y) => x >= 0 && y >= 0 && x < width && y < height && alpha[(y * width + x) * stride + stride - 1] >= 128;
  const edges = new Map(), unit = width + 1;
  function edge(x, y, xx, yy, dir) {
    const key = y * unit + x, list = edges.get(key) || [];
    list.push({ x: xx, y: yy, dir }); edges.set(key, list);
  }
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) if (occupied(x, y)) {
    if (!occupied(x, y - 1)) edge(x, y, x + 1, y, 0);
    if (!occupied(x + 1, y)) edge(x + 1, y, x + 1, y + 1, 1);
    if (!occupied(x, y + 1)) edge(x + 1, y + 1, x, y + 1, 2);
    if (!occupied(x - 1, y)) edge(x, y + 1, x, y, 3);
  }
  const loops = [];
  while (edges.size) {
    const start = edges.keys().next().value, points = [[start % unit, Math.floor(start / unit)]];
    let key = start, dir = edges.get(start)[0].dir, guard = 4 * width * height;
    do {
      const choices = edges.get(key);
      if (!choices?.length) break;
      // At a diagonal pixel contact follow the same island instead of joining two letters.
      const rank = turn => [1, 0, 3, 2].indexOf((turn - dir + 4) % 4);
      choices.sort((a, b) => rank(a.dir) - rank(b.dir));
      const next = choices.shift(); if (!choices.length) edges.delete(key);
      points.push([next.x, next.y]); dir = next.dir; key = next.y * unit + next.x;
    } while (key !== start && --guard > 0);
    if (key !== start || points.length < 5) continue;
    const middle = Math.floor(points.length / 2);
    const loop = [...simplify(points.slice(0, middle + 1), .55).slice(0, -1), ...simplify(points.slice(middle), .55).slice(0, -1)];
    if (loop.length >= 3 && Math.abs(area(loop)) >= 1) loops.push(loop);
  }
  return loops;
}

/** Unit-width, unit-length solid with the complete top at local y=0. */
export function extrudeLetters(contours, depth = 1.05) {
  if (!contours.length) return new BufferGeometry();
  const points = contours.flat(), xs = points.map(p => p[0]), ys = points.map(p => p[1]);
  const x0 = Math.min(...xs), y0 = Math.min(...ys), w = Math.max(...xs) - x0 || 1, h = Math.max(...ys) - y0 || 1;
  const path = new ShapePath();
  for (const loop of contours) {
    loop.forEach((p, i) => path[i ? 'lineTo' : 'moveTo']((p[0] - x0) / w - .5, .5 - (p[1] - y0) / h));
    path.currentPath.closePath();
  }
  const geometry = new ExtrudeGeometry(path.toShapes(false), {
    depth, steps: 1, curveSegments: 2, bevelEnabled: true, bevelSegments: 2,
    bevelSize: .004, bevelThickness: .055,
  });
  geometry.rotateX(-Math.PI / 2); geometry.computeBoundingBox();
  const b = geometry.boundingBox;
  geometry.userData.letterBounds = [b.min.x, b.max.x, b.min.z, b.max.z];
  geometry.userData.rasterBounds = [x0, y0, w, h];
  geometry.translate(-(b.min.x + b.max.x) / 2, -b.max.y, -(b.min.z + b.max.z) / 2);
  geometry.scale(1 / (b.max.x - b.min.x || 1), 1, 1 / (b.max.z - b.min.z || 1));
  geometry.computeBoundingBox(); geometry.computeBoundingSphere();
  return geometry;
}

/** A few thick stroke centers, in the simulation's normalized lateral/forward coordinates. */
export function letterCatchPoints(alpha, width, height, geometry) {
  if (!geometry.userData.rasterBounds) return [];
  const [x0, y0, w, h] = geometry.userData.rasterBounds;
  const [xmin, xmax, zmin, zmax] = geometry.userData.letterBounds;
  const distance = new Float32Array(width * height); let deepest = 0;
  for (let i = 0; i < distance.length; i++) distance[i] = alpha[i * 4 + 3] >= 128 ? Math.min(width, height) : 0;
  for (let y = 1; y < height; y++) for (let x = 1; x < width; x++) {
    const i = y * width + x;
    distance[i] = Math.min(distance[i], distance[i - 1] + 1, distance[i - width] + 1);
  }
  for (let y = height - 2; y >= 0; y--) for (let x = width - 2; x >= 0; x--) {
    const i = y * width + x;
    distance[i] = Math.min(distance[i], distance[i + 1] + 1, distance[i + width] + 1);
    deepest = Math.max(deepest, distance[i]);
  }
  const clearance = Math.max(1, Math.min(3, deepest * .55)), candidates = [];
  for (let y = y0; y < y0 + h; y++) for (let x = x0; x < x0 + w; x++) {
    if (distance[y * width + x] < clearance) continue;
    const nx = ((x + .5 - x0) / w - .5 - (xmin + xmax) / 2) / (xmax - xmin);
    const nz = -((y + .5 - y0) / h - .5 - (zmin + zmax) / 2) / (zmax - zmin);
    candidates.push({ x: nx, z: nz });
  }
  const points = [];
  for (let column = 0; column < 9; column++) {
    const aim = -.4 + column * .1; let best = null, score = Infinity;
    for (const p of candidates) {
      const s = (p.x - aim) ** 2 + p.z ** 2 * 1.3;
      if (s < score) { score = s; best = p; }
    }
    if (best && !points.some(p => Math.hypot(p.x - best.x, p.z - best.z) < .025)) points.push(best);
  }
  return points;
}
