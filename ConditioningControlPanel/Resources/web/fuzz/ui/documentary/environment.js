import { mergeGeometries, mergeVertices } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { trackPosition, trackStyle, trackYaw } from './track.js';

const BAY_LENGTH = 8;
const HALF_WIDTH = 4.6;
const ROOM_ENDS = [130, 180, 340, 400, 520];
const ROOM_IDS = ['intake', 'connector', 'reflex', 'descent', 'wakeup', 'theatre'];
const curatedArt = [[36, -1], [66, 1], [104, -1], [194, -1], [210, 1], [226, -1], [298, 1], [312, -1], [418, -1], [504, -1]];
const physicalExhibits = [[49, -1], [68, 1], [100, -1], [196, -1], [225, 1], [260, 1], [284, -1], [306, -1], [436, 1], [470, 1], [500, 1]];
const nichePositions = new Map();
for (let distance = 4; distance <= 548; distance += BAY_LENGTH) for (const side of [-1, 1]) {
  if (distance === 12 || distance === 20) continue;
  if (curatedArt.some(([at, wall]) => wall === side && Math.abs(distance - at) < 4.1)) continue;
  if ([248, 264, 280, 300].some(at => Math.abs(distance - at) < 4.1)) continue;
  if (physicalExhibits.some(([at, wall]) => wall === side && Math.abs(distance - at) < 3.8)) continue;
  nichePositions.set(`${distance}/${side}`, { distance, side });
}
for (const [distance, side, collectionAt] of [[244, -1, 248], [244, 1, 248], [268, -1, 264], [268, 1, 264], [276, -1, 280], [276, 1, 280], [300, -1, 300], [292, 1, 300]]) {
  nichePositions.set(`${distance}/${side}`, { distance, side, collectionAt });
}
export const SCREEN_NICHES = Object.freeze([...nichePositions.values()].sort((a, b) => a.distance - b.distance || a.side - b.side).map(spec => {
  const { distance, side } = spec, lateral = 4.54;
  const center = trackPosition(distance, side * lateral), before = trackPosition(distance - .05, side * lateral), after = trackPosition(distance + .05, side * lateral);
  const dx = after.x - before.x, dy = after.y - before.y, dz = after.z - before.z, length = Math.hypot(dx, dy, dz);
  const direction = { x: dx / length, y: dy / length, z: dz / length };
  const extent = sign => {
    const edge = trackPosition(distance + sign * 2.45, side * lateral);
    return Math.abs((edge.x - center.x) * direction.x + (edge.y - center.y) * direction.y + (edge.z - center.z) * direction.z);
  };
  // Tight inner turns have shorter walls. Size the flat frame to that actual pocket.
  let width = Math.min(4.45, 2 * Math.min(extent(-1), extent(1)) - .24);
  const roll = -side * Math.atan2(dy, Math.hypot(dx, dz));
  const yaw = trackYaw(distance) + (side < 0 ? Math.PI / 2 : -Math.PI / 2);
  const ceiling = { intake: 3.8, connector: 4.05, reflex: 3.9, descent: 4.05, wakeup: 3.7, theatre: 3.8 }[roomAt(distance)] - .18;
  const fits = candidate => {
    for (const x of [-1, 1]) for (const y of [-1, 1]) for (const depth of [-.167, .082]) {
      const localX = x * (candidate / 2 + .082), localY = y * (candidate * 9 / 32 + .082);
      const horizontal = localX * Math.cos(roll) - localY * Math.sin(roll);
      const corner = {
        x: center.x + Math.cos(yaw) * horizontal + Math.sin(yaw) * depth,
        y: center.y + 2.18 + localX * Math.sin(roll) + localY * Math.cos(roll),
        z: center.z - Math.sin(yaw) * horizontal + Math.cos(yaw) * depth,
      };
      const along = at => {
        const point = trackPosition(at), angle = trackYaw(at);
        return (corner.x - point.x) * -Math.sin(angle) + (corner.z - point.z) * -Math.cos(angle);
      };
      let at = distance;
      for (let iteration = 0; iteration < 5; iteration++) {
        const derivative = (along(at + .02) - along(at - .02)) / .04;
        if (Math.abs(derivative) < .05) return false;
        at = Math.max(distance - 3.4, Math.min(distance + 3.4, at - along(at) / derivative));
      }
      const point = trackPosition(at), angle = trackYaw(at);
      const wallDistance = Math.abs((corner.x - point.x) * Math.cos(angle) - (corner.z - point.z) * Math.sin(angle));
      const elevation = corner.y - point.y;
      if (Math.abs(at - distance) > 2.42 || wallDistance > 5.99 || elevation < .86 || elevation > ceiling) return false;
    }
    return true;
  };
  for (let attempt = 0; attempt < 24 && !fits(width); attempt++) width *= .96;
  return Object.freeze({ ...spec, lateral, elevation: 2.18, width, height: width * 9 / 16, roll });
}));
const screenSides = new Map();
for (const { distance, side } of SCREEN_NICHES) screenSides.set(distance, screenSides.has(distance) ? 2 : side);

function roomAt(distance) {
  const index = ROOM_ENDS.findIndex(end => distance < end);
  return ROOM_IDS[index < 0 ? 5 : index];
}

/** Six room kits deform along the same elevated rail centreline, with a bounded geometry pool. */
export function createMuseum(THREE, materials, { lite = false } = {}) {
  const group = new THREE.Group();
  group.name = 'Gooning Museum rooms';
  const temporary = new Set();
  const templates = new Map();
  const beveledBoxes = new Map();
  const transform = new THREE.Matrix4();
  const quaternion = new THREE.Quaternion();
  const sweptBoxes = new Map();
  const leafShapes = new Map();
  const cubeGeometry = new THREE.BoxGeometry(1, 1, 1);
  const cylinderGeometry = new THREE.CylinderGeometry(1, 1, 1, lite ? 12 : 20);
  const sphereGeometry = new THREE.SphereGeometry(1, lite ? 12 : 20, lite ? 8 : 12);
  const smallSphereGeometry = new THREE.SphereGeometry(1, 8, 4);
  const smallCylinderGeometry = new THREE.CylinderGeometry(1, 1, 1, 8);
  let speaking = false, speakingLevel = 1;
  const coneGeometry = new THREE.LatheGeometry([[0, -.008], [.015, -.010], [.033, -.023], [.044, -.025], [.125, -.004], [.14, 0]].reverse().map(([x, y]) => new THREE.Vector2(x, y)), lite ? 20 : 32);
  coneGeometry.rotateX(Math.PI / 2);
  for (const geometry of [cubeGeometry, cylinderGeometry, sphereGeometry, smallSphereGeometry, smallCylinderGeometry]) temporary.add(geometry);

  function sweptBox(width, depth) {
    const across = Math.max(1, Math.ceil(width / 0.5));
    const along = Math.max(1, Math.ceil(depth / 0.25));
    const key = `${across}/${along}`;
    if (!sweptBoxes.has(key)) {
      const geometry = new THREE.BoxGeometry(1, 1, 1, across, 1, along);
      sweptBoxes.set(key, geometry); temporary.add(geometry);
    }
    return sweptBoxes.get(key);
  }

  function botanicalLeaf(variation) {
    if (leafShapes.has(variation)) return leafShapes.get(variation);
    const rows = lite ? 8 : 10, columns = lite ? 2 : 3, positions = [], uv = [], indices = [];
    for (let row = 0; row <= rows; row++) {
      const t = row / rows;
      const width = Math.pow(Math.sin(Math.PI * t), .72) * (.48 + Math.sin(t * 5 + variation) * .035);
      for (let column = 0; column <= columns; column++) {
        const across = column / columns * 2 - 1;
        const ridge = (1 - Math.abs(across)) * Math.sin(Math.PI * t) * .045;
        positions.push(across * width, t, (.19 + variation * .035) * t * t - Math.abs(across) * .08 * Math.sin(Math.PI * t) + ridge + across * t * .025 * (variation - 1));
        uv.push(column / columns, t);
        if (row < rows && column < columns) {
          const a = row * (columns + 1) + column, b = a + columns + 1;
          indices.push(a, a + 1, b, a + 1, b + 1, b);
        }
      }
    }
    const shape = new THREE.BufferGeometry();
    shape.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    shape.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    shape.setIndex(indices); shape.computeVertexNormals(); shape.userData.preserveUV = true;
    temporary.add(shape); leafShapes.set(variation, shape); return shape;
  }

  function makeKit(id, variant = 0, screenSide = 0) {
    const batches = new Map();
    const solidParts = [];
    const speakers = [];
    const hasScreen = side => screenSide === 2 || screenSide === side;
    const names = {
      intake: { wall: 'lacquerPlum', trim: 'lacquerRose', light: 'pinkLight', accent: 'brass', floor: 'floor' },
      connector: { wall: 'lacquerBlack', trim: 'cyanPanel', light: 'cyanLight', accent: 'chrome', floor: 'lacquerBlack' },
      reflex: { wall: 'lacquerBlack', trim: 'cyanPanel', light: 'amberLight', accent: 'chrome', floor: 'dark' },
      descent: { wall: 'lacquerPlum', trim: 'pinkPanel', light: 'pinkLight', accent: 'chrome', floor: 'lacquerPlum' },
      wakeup: { wall: 'pearl', trim: 'mint', light: 'mintLight', accent: 'brass', floor: 'floor' },
      theatre: { wall: 'plaster', trim: 'pearl', light: 'lamp', accent: 'iron', floor: 'floor' },
    }[id];
    function add(geometry, material, x, y, z, sx = 1, sy = 1, sz = 1, rx = 0, ry = 0, rz = 0, solid = false) {
      quaternion.setFromEuler(new THREE.Euler(rx, ry, rz));
      transform.compose(new THREE.Vector3(x, y, z), quaternion, new THREE.Vector3(sx, sy, sz));
      let copy = geometry.clone().applyMatrix4(transform);
      const positions = copy.attributes.position, normals = copy.attributes.normal, uv = copy.attributes.uv;
      if (uv && !geometry.userData.preserveUV) for (let i = 0; i < positions.count; i++) {
        const ax = Math.abs(normals.getX(i)), ay = Math.abs(normals.getY(i)), az = Math.abs(normals.getZ(i));
        const density = ['stone', 'floor', 'plaster', 'plasterBlue'].includes(material) ? 0.5 : 1;
        if (ay > ax && ay > az) uv.setXY(i, positions.getX(i) * density, positions.getZ(i) * density);
        else if (ax > az) uv.setXY(i, positions.getZ(i) * density, positions.getY(i) * density);
        else uv.setXY(i, positions.getX(i) * density, positions.getY(i) * density);
      }
      if (!copy.index) {
        const indexed = mergeVertices(copy);
        copy.dispose(); copy = indexed;
      }
      if (!batches.has(material)) batches.set(material, []);
      batches.get(material).push(copy);
      if (solid) solidParts.push(copy.clone());
    }
    const box = (material, x, y, z, width, height, depth, solid = false, rx = 0, ry = 0, rz = 0) =>
      add(depth > 1.5 ? sweptBox(width, depth) : cubeGeometry, material, x, y, z, width, height, depth, rx, ry, rz, solid);
    const cylinder = (material, x, y, z, radius, height, rx = 0, ry = 0, rz = 0) =>
      add(radius < 0.075 ? smallCylinderGeometry : cylinderGeometry, material, x, y, z, radius, height, radius, rx, ry, rz);
    const sphere = (material, x, y, z, radius) => add(radius < 0.1 ? smallSphereGeometry : sphereGeometry, material, x, y, z, radius, radius, radius);
    function rounded(material, x, y, z, width, height, depth, radius = 0.035) {
      const r = Math.min(radius, width * 0.23, height * 0.23, depth * 0.23);
      const key = `${width}/${height}/${depth}/${r}`;
      if (!beveledBoxes.has(key)) {
        const w = width / 2 - r, h = height / 2 - r;
        const shape = new THREE.Shape();
        shape.moveTo(-w, -h); shape.lineTo(w, -h); shape.lineTo(w, h); shape.lineTo(-w, h); shape.closePath();
        const geometry = new THREE.ExtrudeGeometry(shape, {
          depth: depth - r * 2, steps: Math.max(1, Math.ceil(depth / 0.3)),
          bevelEnabled: true, bevelThickness: r, bevelSize: r, bevelSegments: 1, curveSegments: 1,
        });
        geometry.translate(0, 0, -depth / 2 + r);
        beveledBoxes.set(key, geometry); temporary.add(geometry);
      }
      add(beveledBoxes.get(key), material, x, y, z);
    }
    function fastener(side, x, y, z, radius = 0.018) {
      cylinder(names.accent, side * x, y, z, radius, 0.014, 0, 0, Math.PI / 2);
      box('lacquerBlack', side * (x - 0.01), y, z, 0.006, 0.005, radius * 1.35);
    }
    function panel(side, x, y, z, height, width, fill = names.wall) {
      rounded(names.trim, side * x, y, z, 0.16, height, width);
      rounded('lacquerBlack', side * (x - 0.09), y, z, 0.055, height - 0.10, width - 0.10, 0.018);
      rounded(fill, side * (x - 0.13), y, z, 0.045, height - 0.18, width - 0.18, 0.015);
      for (const dy of [-1, 1]) for (const dz of [-1, 1]) fastener(side, x - 0.1, y + dy * (height / 2 - 0.055), z + dz * (width / 2 - 0.055), 0.012);
    }
    function grille(side, x, y, z, width = 0.76, height = 0.34) {
      rounded(names.accent, side * x, y, z, 0.11, height + 0.09, width + 0.09, 0.018);
      box('lacquerBlack', side * (x - 0.064), y, z, 0.025, height, width);
      const rows = lite ? 5 : 8;
      for (let i = 0; i < rows; i++) box(names.accent, side * (x - 0.09), y - height * 0.4 + i / (rows - 1) * height * 0.8, z, 0.045, 0.012, width - 0.065, false, 0, 0, side * 0.24);
      for (const dy of [-1, 1]) for (const dz of [-1, 1]) fastener(side, x - 0.065, y + dy * (height / 2 + 0.018), z + dz * (width / 2 + 0.018), 0.012);
    }
    function wallSpeaker(side, x, y, z) {
      rounded('lacquerBlack', side * x, y, z, 0.28, 0.76, 0.48, 0.045);
      rounded('rubber', side * (x - .15), y, z, .045, .69, .405, .015);
      for (const [dy, radius] of [[-.13, .14], [.20, .063]]) {
        const mountingRing = new THREE.TorusGeometry(radius + .017, .009, 6, lite ? 20 : 32);
        temporary.add(mountingRing);
        add(mountingRing, names.accent, side * (x - .195), y + dy, z, 1, 1, 1, 0, Math.PI / 2);
        const surround = new THREE.TorusGeometry(radius + .004, .009, 6, lite ? 20 : 32);
        temporary.add(surround);
        add(surround, 'rubber', side * (x - .218), y + dy, z, 1, 1, 1, 0, Math.PI / 2);
        if (dy < 0) speakers.push({ side, x: x - .22, y: y + dy, z });
        else sphere('speakerCone', side * (x - .22), y + dy, z, radius * .69);
      }
      for (const dy of [-.30, .30]) for (const dz of [-.16, .16]) fastener(side, x - .183, y + dy, z + dz, .012);
      box('iron', side * (x + .10), y + .34, z, .34, .055, .14);
      box('iron', side * (x + .10), y - .34, z, .34, .055, .14);
    }
    function bench(side, z = 0) {
      const x = side * 5.13;
      rounded('woodDark', x, 0.48, z, 0.98, 0.30, 2.52, 0.06);
      for (const dz of [-0.87, 0, 0.87]) {
        rounded('leather', x, 0.72, z + dz, 0.98, 0.19, 0.83, 0.065);
        box(names.accent, x, 0.59, z + dz, 0.99, 0.012, 0.82);
      }
      for (const dx of [-0.35, 0.35]) for (const dz of [-1.02, 1.02]) {
        cylinder('lacquerBlack', x + dx, 0.23, z + dz, 0.045, 0.42);
        cylinder(names.accent, x + dx, 0.055, z + dz, 0.052, 0.08);
      }
      for (const dz of [-1.23, 1.23]) {
        box(names.accent, x, 0.76, z + dz, 0.76, 0.055, 0.055);
        for (const dx of [-0.34, 0.34]) box(names.accent, x + dx, 0.64, z + dz, 0.045, 0.28, 0.055);
      }
    }
    function archiveShelf(side) {
      panel(side, 5.94, 1.7, 0, 2.65, 3.15, 'woodDark');
      for (const y of [0.55, 1.18, 1.81, 2.44]) {
        rounded('wood', side * 5.68, y, 0, 0.58, 0.07, 2.95, 0.018);
        box(names.accent, side * 5.37, y, 0, 0.035, 0.05, 2.97);
      }
      for (let shelf = 0; shelf < 3; shelf++) for (let book = 0; book < (lite ? 7 : 10); book++) {
        const z = -1.28 + book * 0.25, height = 0.35 + ((book + shelf) % 3) * 0.055;
        const material = ['lacquerRose', 'velvet', 'cyanPanel', 'woodDark'][(book + shelf * 2) % 4];
        rounded(material, side * 5.60, 0.60 + shelf * 0.63 + height / 2, z, 0.35, height, 0.19, 0.012);
        box('paper', side * 5.407, 0.60 + shelf * 0.63 + height * 0.73, z, 0.012, 0.08, 0.12);
      }
    }
    function instrumentRack(side) {
      panel(side, 5.88, 2.05, 0, 1.72, 3.55, 'lacquerBlack');
      for (const z of [-1.10, 0, 1.10]) {
        rounded('chrome', side * 5.66, 2.15, z, 0.24, 0.91, 0.93, 0.035);
        rounded('lacquerBlack', side * 5.51, 2.23, z, 0.09, 0.53, 0.73, 0.025);
        box('cyanPanel', side * 5.455, 2.23, z, 0.018, 0.42, 0.62);
        for (let line = 0; line < 5; line++) box('cyanLight', side * 5.44, 2.12 + line * 0.05, z - 0.16 + Math.sin(line + z) * 0.12, 0.012, 0.012, 0.15 + (line % 2) * 0.12);
        for (const dz of [-0.23, 0, 0.23]) {
          cylinder('lacquerBlack', side * 5.48, 1.87, z + dz, 0.055, 0.08, 0, 0, Math.PI / 2);
          box('paper', side * 5.43, 1.884, z + dz, 0.012, 0.035, 0.008);
        }
      }
      box('lacquerBlack', side * 5.5, 1.49, 0, 0.72, 0.13, 3.65);
      box('chrome', side * 5.08, 1.49, 0, 0.065, 0.075, 3.69);
      for (const z of [-1.65, 1.65]) grille(side, 5.35, 0.55, z, 0.50, 0.38);
    }

    function foliage(side, z, variety) {
      const originX = side * 5.12 + Math.sin(z * 3.2 + variety) * .11;
      const stemHeight = 1.40 + Math.sin(z * 4.1 + variety) * .12;
      const leanX = Math.sin(z * 5.3) * .08, leanZ = Math.cos(z * 3.7 + variety) * .09;
      function branch(points, radius) {
        const curve = new THREE.CatmullRomCurve3(points.map(point => new THREE.Vector3(...point)));
        const shape = new THREE.TubeGeometry(curve, lite ? 4 : 5, radius, lite ? 3 : 4, false);
        temporary.add(shape); add(shape, 'plantStem', 0, 0, 0);
      }
      branch([[originX, .68, z], [originX - leanX * .3, 1.02, z], [originX + leanX, stemHeight, z + leanZ]], .013);
      for (let leaf = 0; leaf < (lite ? 6 : 8); leaf++) {
        const fraction = leaf / 8, y = .79 + fraction * (stemHeight - .75);
        const angle = leaf * 2.39996 + z * 1.7 + variety;
        const reach = .10 + (leaf % 3) * .018;
        const baseX = originX + leanX * fraction + Math.cos(angle) * reach;
        const baseZ = z + leanZ * fraction + Math.sin(angle) * reach;
        branch([[originX + leanX * fraction, y - .02, z + leanZ * fraction], [baseX, y + .025, baseZ]], .005);
        const length = .43 + (leaf % 3) * .065 + Math.sin(z + leaf) * .035;
        add(botanicalLeaf(leaf % 3), leaf > 5 ? 'foliageYoung' : 'foliage', baseX, y + .025, baseZ,
          length * .51, length, length, .65 + (leaf % 3) * .17, -angle + Math.PI / 2, Math.sin(angle) * .23);
      }
      add(botanicalLeaf(1), 'foliageYoung', originX + leanX, stemHeight - .035, z + leanZ,
        .10, .29, .29, .25, z, .18);
    }

    function curveShell(points, material, solid = false, depth = BAY_LENGTH) {
      const positions = [], uv = [], indices = [];
      const rows = Math.ceil(depth / 0.25);
      // Faceted shells keep independent vertices at each crease instead of smoothing across it.
      for (let segment = 0; segment < points.length - 1; segment++) {
        const first = positions.length / 3;
        const [a, b] = [points[segment], points[segment + 1]];
        const divisions = Math.max(1, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / 0.5));
        for (let row = 0; row <= rows; row++) for (let i = 0; i <= divisions; i++) {
          const fraction = i / divisions;
          positions.push(a[0] + (b[0] - a[0]) * fraction, a[1] + (b[1] - a[1]) * fraction, row / rows * depth - depth / 2);
          uv.push((segment + fraction) / (points.length - 1) * 8, row / rows * depth);
        }
        for (let row = 0; row < rows; row++) for (let i = 0; i < divisions; i++) {
          const a = first + row * (divisions + 1) + i, b = a + 1, c = a + divisions + 1, d = c + 1;
          indices.push(a, c, b, b, c, d);
        }
      }
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
      geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      geometry.setIndex(indices); geometry.computeVertexNormals();
      temporary.add(geometry);
      add(geometry, material, 0, 0, 0, 1, 1, 1, 0, 0, 0, solid);
    }
    function frame(points, material, radius, z) {
      const vertices = points.map(point => new THREE.Vector3(point[0], point[1], z));
      let curve;
      if (points.length > 8) curve = new THREE.CatmullRomCurve3(vertices);
      else {
        curve = new THREE.CurvePath();
        for (let i = 1; i < vertices.length; i++) curve.add(new THREE.LineCurve3(vertices[i - 1], vertices[i]));
      }
      const geometry = new THREE.TubeGeometry(curve, lite ? 64 : 96, radius, lite ? 6 : 8, false);
      temporary.add(geometry); add(geometry, material, 0, 0, 0);
    }
    function fixture(side, y, z) {
      const x = side * 4.27;
      rounded(names.accent, side * 4.48, y, z, 0.12, 0.52, 0.31, 0.035);
      box('lacquerBlack', side * 4.40, y, z, 0.04, 0.43, 0.22);
      box(names.accent, side * 4.34, y - 0.16, z, 0.28, 0.07, 0.12);
      cylinder('glass', x, y, z, 0.19, 0.46);
      cylinder(names.light, x, y, z, 0.145, 0.43);
      cylinder('lacquerBlack', x, y + 0.29, z, 0.22, 0.075);
      cylinder(names.accent, x, y + 0.25, z, 0.20, 0.04);
      cylinder('lacquerBlack', x, y - 0.29, z, 0.22, 0.075);
      cylinder(names.accent, x, y - 0.25, z, 0.20, 0.04);
      for (let i = 0; i < 4; i++) {
        const a = i * Math.PI / 2;
        cylinder(names.accent, x + Math.cos(a) * 0.195, y, z + Math.sin(a) * 0.195, 0.015, 0.50);
      }
      for (const dy of [-0.2, 0.2]) fastener(side, 4.405, y + dy, z, 0.02);
    }
    function alcove(side, height = 3.5) {
      // The centre of each bay opens into a real two-metre-deep exhibition pocket.
      box(names.wall, side * 4.74, height / 2, -3.3, 0.28, height, 1.4, true);
      box(names.wall, side * 4.74, height / 2, 3.3, 0.28, height, 1.4, true);
      box(names.wall, side * 6.12, height / 2, 0, 0.2, height, 5.2, true);
      for (const z of [-2.65, 2.65]) box(names.trim, side * 5.4, height / 2, z, 1.5, height, 0.18, true);
      box(names.trim, side * 5.4, height - 0.05, 0, 1.55, 0.2, 5.4, true);
      box(names.accent, side * 4.55, 0.14, 0, 0.09, 0.11, BAY_LENGTH);
      box(names.light, side * 4.5, 0.24, 0, 0.045, 0.045, BAY_LENGTH);
      // Layered skirting, shadow gaps and cornice give the pocket an architectural finish.
      box('lacquerBlack', side * 6.0, 0.16, 0, 0.10, 0.30, 5.15);
      box(names.accent, side * 5.94, 0.31, 0, 0.045, 0.045, 5.15);
      box(names.accent, side * 4.62, height - 0.17, 0, 0.10, 0.065, BAY_LENGTH);
      box('lacquerBlack', side * 4.63, height - 0.29, 0, 0.06, 0.035, BAY_LENGTH);
      for (const z of [-2.62, 2.62]) {
        rounded(names.trim, side * 4.59, height / 2, z, 0.25, height, 0.30);
        box(names.accent, side * 4.44, height / 2, z, 0.035, height - 0.15, 0.045);
      }
    }
    box(names.floor, 0, -0.14, 0, 12.5, 0.28, BAY_LENGTH, true);
    // A clean central inlay gives the rails continuity through each change of room.
    box(id === 'wakeup' ? 'pearl' : 'lacquerBlack', 0, 0.022, 0, 1.55, 0.04, BAY_LENGTH);
    for (const side of [-1, 1]) {
      box(names.accent, side * 0.81, 0.045, 0, 0.035, 0.035, BAY_LENGTH);
      if (id !== 'theatre') box(names.light, side * 1.02, 0.035, 0, 0.045, 0.024, BAY_LENGTH);
    }

    if (id === 'intake') {
      for (const side of [-1, 1]) {
        alcove(side, 3.8);
        for (const z of [-3.6, 3.6]) {
          box('lacquerRose', side * 4.4, 1.75, z, 0.3, 3.5, 0.4);
          box('brass', side * 4.21, 1.75, z, 0.035, 3.32, 0.11);
        }
        for (const z of hasScreen(side) ? [] : [-1.8, 0, 1.8]) {
          panel(side, 5.98, 1.56, z, 2.48, 1.62, 'velvet');
          for (let channel = 0; channel < 6; channel++) {
            const channelZ = z - 0.61 + channel * 0.245;
            add(cylinderGeometry, 'velvet', side * 5.77, 1.57, channelZ, 0.10, 2.15, 0.115);
            for (const y of [1.05, 2.08]) sphere('brass', side * 5.665, y, channelZ, 0.018);
          }
        }
        fixture(side, 2.85, -2.9);
        for (const z of [-3.25, 3.25]) panel(side, 4.48, 1.18, z, 1.88, 0.95, 'leather');
        grille(side, 4.46, 0.48, 3.25, 0.68, 0.26);
        wallSpeaker(side, 4.47, 3.06, 3.22);
      }
      const shell = Array.from({ length: 33 }, (_, i) => {
        const angle = i / 32 * Math.PI;
        return [Math.cos(angle) * 4.63, 3.78 + Math.sin(angle) * 1.35];
      });
      curveShell(shell, 'lacquerPlum', true);
      frame(shell, 'lacquerRose', 0.14, -3.9);
      frame(shell.map(([x, y]) => [x * 0.985, y - 0.055]), 'pinkLight', 0.025, -3.76);
      // Recessed square ceiling lanterns have visible housings and attachments.
      for (const x of [-2.7, 2.7]) {
        box('brass', x, 4.71, 0, 0.66, 0.13, 1.15);
        box('lamp', x, 4.62, 0, 0.54, 0.045, 1.02);
      }
      bench(variant ? 1 : -1);
      if (variant && !hasScreen(-1)) archiveShelf(-1);
      else {
        rounded('woodDark', 5.13, 0.59, 0.85, 0.72, 0.10, 0.72, 0.045);
        cylinder('brass', 5.13, 0.30, 0.85, 0.045, 0.55);
        cylinder('woodDark', 5.13, 0.055, 0.85, 0.27, 0.075);
        for (let book = 0; book < 3; book++) rounded(book % 2 ? 'paper' : 'lacquerRose', 5.13, 0.68 + book * 0.045, 0.85, 0.35 + book * 0.03, 0.035, 0.46, 0.01);
      }
    } else if (id === 'connector' || id === 'descent') {
      const color = id === 'connector' ? 'cyanLight' : 'pinkLight';
      const dark = id === 'connector' ? 'cyanPanel' : 'lacquerPlum';
      const outline = [[-4.62, 0], [-4.62, 4.05], [-3.3, 5.45], [3.3, 5.45], [4.62, 4.05], [4.62, 0]];
      curveShell([...outline.slice(1, -1)].reverse(), dark, true);
      for (const z of [-3.85]) {
        frame(outline.map(([x, y]) => [x * 0.97, Math.max(0.1, y - 0.18)]), 'lacquerBlack', 0.18, z);
        frame(outline.map(([x, y]) => [x * 0.968, Math.max(0.1, y - 0.22)]), color, 0.035, z + 0.16);
      }
      for (const side of [-1, 1]) {
        if (hasScreen(side)) {
          alcove(side, 4.05);
          grille(side, 4.38, 0.45, 3.15, 0.68, 0.27);
          continue;
        }
        box(dark, side * 4.75, 2.025, 0, 0.26, 4.05, BAY_LENGTH, true);
        for (const z of [-2.5, 1.5]) {
          panel(side, 4.37, 1.45, z, 1.95, 1.28, 'lacquerBlack');
          box(color, side * 4.27, 1.45, z, 0.035, 1.58, 0.075);
          box(names.accent, side * 4.26, 2.38, z, 0.08, 0.1, 1.27);
          grille(side, 4.34, 0.30, z, 0.88, 0.21);
        }
        box('lacquerBlack', side * 4.48, 0.18, 0, 0.18, 0.34, BAY_LENGTH);
        box(names.accent, side * 4.36, 0.37, 0, 0.055, 0.04, BAY_LENGTH);
        box('lacquerBlack', side * 4.47, 2.66, 0, 0.14, 0.13, BAY_LENGTH);
        wallSpeaker(side, 4.39, 2.31, variant ? -0.35 : 3.2);
        for (const z of [-3.8, 0]) {
          rounded(names.accent, side * 4.38, 0.30, z, 0.35, 0.52, 0.48, 0.04);
          rounded('lacquerBlack', side * 4.17, 0.30, z, 0.08, 0.40, 0.34, 0.025);
          fastener(side, 4.10, 0.39, z - 0.09, 0.025);
          fastener(side, 4.10, 0.21, z + 0.09, 0.025);
        }
      }
      // Repeated asymmetric chevrons provide speed and descent cues on the solid walls.
      for (const side of [-1, 1].filter(side => !hasScreen(side))) for (const z of [-2, 2]) {
        box(color, side * 4.19, 1.15, z, 0.045, 0.10, 0.65, false, side * 0.55);
        box(color, side * 4.19, 1.47, z + 0.28, 0.045, 0.10, 0.65, false, -side * 0.55);
      }
    } else if (id === 'reflex') {
      for (const side of [-1, 1]) {
        alcove(side, 3.9);
        box('cyanPanel', side * 4.44, 1.0, -3.25, 0.25, 1.8, 1.2);
        box('cyanPanel', side * 4.44, 1.0, 3.25, 0.25, 1.8, 1.2);
        box('amberLight', side * 4.26, 1.95, -3.25, 0.055, 0.06, 1.2);
        box('amberLight', side * 4.26, 1.95, 3.25, 0.055, 0.06, 1.2);
        // Closed specimen drawers, controls and inset instrument windows.
        if (!hasScreen(side)) box('chrome', side * 5.73, 0.49, 0, 0.65, 0.98, 3.6);
        for (const z of hasScreen(side) ? [] : [-1.25, -0.42, 0.42, 1.25]) {
          box('lacquerBlack', side * 5.36, 0.62, z, 0.04, 0.74, 0.72);
          cylinder('amberLight', side * 5.31, 0.72, z, 0.055, 0.025, 0, 0, Math.PI / 2);
        }
        fixture(side, 3.0, -2.8);
        for (const z of [-3.25, 3.25]) {
          panel(side, 4.43, 1.02, z, 1.74, 1.1, 'cyanPanel');
          grille(side, 4.28, 0.49, z, 0.80, 0.42);
        }
        wallSpeaker(side, 4.48, 3.28, 3.22);
      }
      if (screenSide !== 2) instrumentRack(screenSide ? -screenSide : variant ? 1 : -1);
      const stoolSide = variant ? -1 : 1;
      cylinder('lacquerBlack', stoolSide * 5.07, 0.12, -0.85, 0.32, 0.09);
      cylinder('chrome', stoolSide * 5.07, 0.35, -0.85, 0.045, 0.45);
      cylinder('leather', stoolSide * 5.07, 0.62, -0.85, 0.34, 0.16);
      box('lacquerBlack', 0, 5.15, 0, 9.4, 0.32, BAY_LENGTH, true);
      for (const x of [-3.1, 3.1]) {
        rounded('lacquerBlack', x, 4.94, 0, 0.98, 0.18, 4.02, 0.04);
        box('chrome', x, 4.89, 0, 0.82, 0.16, 3.8);
        box('cyanLight', x, 4.79, 0, 0.67, 0.045, 3.6);
        for (const dx of [-0.42, 0.42]) box('iron', x + dx, 4.79, 0, 0.045, 0.12, 3.92);
        for (const z of [-1.87, 1.87]) box('iron', x, 4.79, z, 0.89, 0.12, 0.055);
      }
      for (const side of [-1, 1]) box('lacquerBlack', side * 4.7, 4.45, 0, 0.25, 1.15, BAY_LENGTH, true);
      for (const side of [-1, 1]) {
        box('chrome', side * 4.3, 2.5, -3.85, 0.3, 5.0, 0.42);
        box('amberLight', side * 4.12, 2.5, -3.85, 0.045, 4.7, 0.06);
      }
    } else if (id === 'wakeup') {
      for (const side of [-1, 1]) {
        alcove(side, 3.7);
        box('mint', side * 4.4, 1.85, -3.7, 0.35, 3.7, 0.35);
        box('mint', side * 4.4, 1.85, 3.7, 0.35, 3.7, 0.35);
        rounded('planter', side * 5.12, .32, 0, 1.16, .60, 3.58, .045);
        for (const dz of [-1.43, 1.43]) rounded('planter', side * 5.12, .06, dz, .97, .12, .23, .025);
        if (hasScreen(side)) rounded('benchWood', side * 5.12, .68, 0, 1.18, .11, 3.62, .035);
        else {
          box('soil', side * 5.12, .635, 0, .98, .09, 3.36);
          for (const dx of [-.54, .54]) rounded('planter', side * 5.12 + dx, .69, 0, .12, .18, 3.62, .025);
          for (const dz of [-1.75, 1.75]) rounded('planter', side * 5.12, .69, dz, 1.08, .18, .12, .025);
          for (let pebble = 0; pebble < (lite ? 16 : 30); pebble++) {
            const x = side * 5.12 + Math.sin(pebble * 12.71) * .42, z = Math.sin(pebble * 5.31 + variant) * 1.61;
            add(smallSphereGeometry, pebble % 5 ? 'soil' : 'stone', x, .69, z, .015 + pebble % 3 * .007, .009, .025, 0, pebble, 0);
          }
          for (const z of [-1.45, -.70, .10, .86, 1.48]) foliage(side, z, variant);
        }
        box('brass', side * 4.48, 0.68, 0, 0.045, 0.04, 3.64);
        for (const z of hasScreen(side) ? [] : [-1.6, 0, 1.6]) {
          panel(side, 5.97, 2.64, z, 1.66, 1.42, 'pearl');
          for (const dy of [-0.45, 0, 0.45]) box('mint', side * 5.78, 2.64 + dy, z, 0.045, 0.028, 1.2);
          box('mint', side * 5.76, 2.64, z, 0.045, 1.43, 0.035);
        }
        for (const z of [-3.18, 3.18]) panel(side, 4.47, 1.65, z, 2.8, 0.89, 'pearl');
        grille(side, 4.33, 0.58, variant ? 3.18 : -3.18, 0.62, 0.34);
        wallSpeaker(side, 4.43, 3.03, 3.18);
      }
      const roof = [[4.64, 3.72], [0, 6.05], [-4.64, 3.72]];
      curveShell(roof, 'pearl', true);
      frame(roof.map(([x, y]) => [x, y - 0.13]), 'mint', 0.10, -3.86);
      frame(roof.map(([x, y]) => [x, y - 0.13]), 'mint', 0.075, 0);
      // Tall frosted skylight panels alternate with the structural greenhouse ribs.
      for (const side of [-1, 1]) {
        box('mintLight', side * 2.1, 4.99, 0, 3.55, 0.055, 2.7, false, 0, 0, -side * 0.465);
        box('mint', side * 2.1, 5.06, -1.46, 3.7, 0.1, 0.1, false, 0, 0, -side * 0.465);
        box('mint', side * 2.1, 5.06, 1.46, 3.7, 0.1, 0.1, false, 0, 0, -side * 0.465);
      }
      const benchX = variant ? 3.3 : -3.3;
      for (const z of [-.25, 1.85]) rounded('planter', benchX, .34, z, .65, .68, .22, .04);
      for (const dx of [-.24, 0, .24]) {
        rounded('benchWood', benchX + dx, .76, .8, .217, .105, 2.9, .023);
        for (const z of [-.25, 1.85]) cylinder('brass', benchX + dx, .814, z, .014, .006);
      }
    } else {
      for (const side of [-1, 1]) {
        if (hasScreen(side)) alcove(side, 3.8);
        else box('plaster', side * 4.73, 1.9, 0, 0.26, 3.8, BAY_LENGTH, true);
        box('pearl', side * 4.57, 0.12, 0, 0.08, 0.24, BAY_LENGTH);
        box('iron', side * 4.52, 0.25, 0, 0.025, 0.025, BAY_LENGTH);
        box('pearl', side * 4.57, 3.69, 0, 0.12, 0.14, BAY_LENGTH);
        if (variant && side < 0 && !hasScreen(side)) grille(side, 4.54, 3.16, -1.1, 0.8, 0.28);
      }
      box('plaster', 0, 3.92, 0, 9.5, 0.24, BAY_LENGTH, true);
      if (variant) {
        rounded('pearl', -2.8, 3.73, 0, 0.36, 0.13, 1.25, 0.025);
        box('lamp', -2.8, 3.65, 0, 0.24, 0.035, 1.06);
      }
    }
    const meshes = [];
    for (const [name, parts] of batches) {
      const geometry = mergeGeometries(parts, false);
      for (const part of parts) part.dispose();
      if (!geometry) throw new Error(`Museum room merge failed: ${id}/${name}`);
      meshes.push({ name, geometry });
    }
    const solid = mergeGeometries(solidParts, false);
    for (const part of solidParts) part.dispose();
    return { meshes, solid, speakers };
  }

  for (const id of ROOM_IDS) for (let variant = 0; variant < 2; variant++) templates.set(`${id}/${variant}/0`, makeKit(id, variant));
  for (const [center, side] of screenSides) {
    const id = roomAt(center), variant = Math.abs(Math.floor(center / BAY_LENGTH)) % 2;
    const key = `${id}/${variant}/${side}`;
    if (!templates.has(key)) templates.set(key, makeKit(id, variant, side));
  }
  for (const geometry of temporary) geometry.dispose();
  const occlusionMaterial = new THREE.MeshBasicMaterial({ side: THREE.DoubleSide });
  const finalWallGeometry = new THREE.BoxGeometry(9.5, 3.8, 0.24);
  const finalWall = new THREE.Mesh(finalWallGeometry, materials.plaster);
  finalWall.name = 'museum_plain_service_end_wall';
  const wallPoint = trackPosition(587, 0, 1.9);
  finalWall.position.set(wallPoint.x, wallPoint.y, wallPoint.z);
  finalWall.rotation.y = trackYaw(587);
  finalWall.receiveShadow = true;
  group.add(finalWall); finalWall.updateMatrixWorld(true);
  const bays = Array.from({ length: lite ? 9 : 11 }, () => ({ node: new THREE.Group(), rails: new THREE.Group(), speakerCones: [], index: null, occluder: null }));
  for (const entry of bays) group.add(entry.node, entry.rails);
  const lights = Array.from({ length: 3 }, () => {
    const light = new THREE.PointLight(0xffc2df, 25, 18, 2);
    group.add(light); return light;
  });
  const raycaster = new THREE.Raycaster();
  const rayOrigin = new THREE.Vector3(), rayDirection = new THREE.Vector3();

  function deform(base, center) {
    const geometry = base.clone();
    const positions = geometry.attributes.position;
    for (let i = 0; i < positions.count; i++) {
      const point = trackPosition(center - positions.getZ(i), positions.getX(i), positions.getY(i));
      positions.setXYZ(i, point.x, point.y, point.z);
    }
    geometry.computeVertexNormals();
    geometry.computeBoundingBox(); geometry.computeBoundingSphere();
    return geometry;
  }
  function clearEntry(entry) {
    for (const mesh of [...entry.node.children]) { mesh.geometry.dispose(); entry.node.remove(mesh); }
    for (const mesh of [...entry.rails.children]) { mesh.geometry.dispose(); entry.rails.remove(mesh); }
    entry.occluder?.geometry.dispose(); entry.occluder = null;
    for (const cone of entry.speakerCones) cone.removeFromParent();
    entry.speakerCones.length = 0;
  }
  function buildEntry(entry, index) {
    clearEntry(entry);
    entry.index = index;
    const center = index * BAY_LENGTH + BAY_LENGTH / 2;
    const id = roomAt(center);
    const template = templates.get(`${id}/${Math.abs(index) % 2}/${screenSides.get(center) || 0}`);
    entry.node.name = `Museum ${id} ${index}`;
    for (const item of template.meshes) {
      const mesh = new THREE.Mesh(deform(item.geometry, center), materials[item.name]);
      mesh.castShadow = !item.name.endsWith('Light'); mesh.receiveShadow = true;
      entry.node.add(mesh);
    }
    entry.occluder = new THREE.Mesh(deform(template.solid, center), occlusionMaterial);
    entry.occluder.updateMatrixWorld(true);
    for (const driver of template.speakers) {
      const at = center - driver.z, yaw = trackYaw(at);
      const point = trackPosition(at, driver.side * driver.x, driver.y);
      const cone = new THREE.Mesh(coneGeometry, materials.speakerCone);
      cone.name = 'museum_speaker_cone';
      cone.position.set(point.x, point.y, point.z);
      cone.userData.rest = cone.position.clone();
      cone.userData.axis = new THREE.Vector3(-driver.side * Math.cos(yaw), 0, driver.side * Math.sin(yaw));
      cone.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), cone.userData.axis);
      cone.receiveShadow = true; group.add(cone); entry.speakerCones.push(cone);
    }
    for (const side of [-1, 1]) {
      const points = [];
      for (let i = 0; i <= 24; i++) {
        const distance = center - BAY_LENGTH / 2 + i / 24 * BAY_LENGTH;
        const point = trackPosition(distance, side * 0.58, 0.095);
        points.push(new THREE.Vector3(point.x, point.y, point.z));
      }
      const curve = new THREE.CatmullRomCurve3(points);
      const mesh = new THREE.Mesh(new THREE.TubeGeometry(curve, 64, 0.035, lite ? 5 : 8, false), materials.chrome);
      mesh.receiveShadow = true;
      entry.rails.add(mesh);
    }
  }
  function update(distance, { time = 0, gentle = true } = {}) {
    const level = speaking && !gentle ? speakingLevel : 0;
    const pulse = .014 * level * (.62 + .38 * Math.sin(time * 7.3) ** 2) * (.72 * Math.sin(time * 67) + .28 * Math.sin(time * 103));
    const first = Math.floor(distance / BAY_LENGTH) - 3;
    for (let i = 0; i < bays.length; i++) {
      const index = first + i;
      const entry = bays[((index % bays.length) + bays.length) % bays.length];
      if (entry.index !== index) buildEntry(entry, index);
      entry.node.visible = entry.rails.visible = index * BAY_LENGTH < 608;
      for (const cone of entry.speakerCones) {
        cone.visible = entry.node.visible;
        cone.position.copy(cone.userData.rest).addScaledVector(cone.userData.axis, pulse);
      }
    }
    for (let i = 0; i < lights.length; i++) {
      const at = distance - 4 + i * 14;
      const point = trackPosition(at, i % 2 ? -2.8 : 2.8, 3.05);
      lights[i].position.set(point.x, point.y, point.z);
      const style = trackStyle(at);
      lights[i].color.set(at >= 520 ? '#fff0d6' : style?.accent || '#ffb5d5');
      lights[i].intensity = at >= 520 ? 13 : roomAt(at) === 'wakeup' ? 15 : 28;
    }
  }
  update(0);
  return {
    group, update,
    setSpeaking(on, level = 1) {
      speaking = Boolean(on);
      speakingLevel = Number.isFinite(level) ? THREE.MathUtils.clamp(level, 0, 1) : 1;
      if (!speaking || speakingLevel === 0) for (const entry of bays) for (const cone of entry.speakerCones) cone.position.copy(cone.userData.rest);
    },
    debug() {
      let cones = 0, maxDisplacement = 0, foliageBatches = 0;
      for (const entry of bays) {
        if (!entry.node.visible) continue;
        for (const cone of entry.speakerCones) {
          cones++;
          maxDisplacement = Math.max(maxDisplacement, cone.position.distanceTo(cone.userData.rest));
        }
        for (const mesh of entry.node.children) if (mesh.material === materials.foliage || mesh.material === materials.foliageYoung) foliageBatches++;
      }
      return {
        speaking, level: speakingLevel, cones, maxDisplacement, foliageBatches,
        leafMapsReady: Boolean(materials.foliage.map?.image?.data && materials.foliage.normalMap?.image?.data && materials.foliage.roughnessMap?.image?.data),
      };
    },
    occludes(point, origin) {
      rayOrigin.set(origin.x, origin.y, origin.z);
      rayDirection.set(point.x - origin.x, point.y - origin.y, point.z - origin.z);
      const distance = rayDirection.length();
      if (distance < 0.01) return false;
      rayDirection.multiplyScalar(1 / distance);
      raycaster.set(rayOrigin, rayDirection); raycaster.near = 0.05; raycaster.far = distance - 0.08;
      if (raycaster.intersectObject(finalWall, false).length) return true;
      for (const entry of bays) {
        if (entry.node.visible && raycaster.intersectObject(entry.occluder, false).length) return true;
      }
      return false;
    },
    dispose() {
      for (const entry of bays) clearEntry(entry);
      for (const template of templates.values()) {
        for (const item of template.meshes) item.geometry.dispose();
        template.solid.dispose();
      }
      coneGeometry.dispose(); finalWallGeometry.dispose(); occlusionMaterial.dispose(); group.removeFromParent();
    },
  };
}
