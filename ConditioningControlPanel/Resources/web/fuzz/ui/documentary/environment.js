import { mergeGeometries, mergeVertices } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { trackPosition, trackStyle, trackYaw } from './track.js';

const BAY_LENGTH = 8;
const HALF_WIDTH = 4.6;
const ROOM_ENDS = [130, 180, 340, 400, 520];
const ROOM_IDS = ['intake', 'connector', 'reflex', 'descent', 'wakeup', 'theatre'];
const SCREEN_NICHES = new Map([[44, 1], [84, -1], [116, 1], [148, -1], [164, 1], [204, 1], [236, -1], [356, 1], [372, -1], [388, 1], [420, 1], [452, -1], [492, 1]]);

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
  const cubeGeometry = new THREE.BoxGeometry(1, 1, 1);
  const cylinderGeometry = new THREE.CylinderGeometry(1, 1, 1, lite ? 12 : 20);
  const sphereGeometry = new THREE.SphereGeometry(1, lite ? 12 : 20, lite ? 8 : 12);
  const smallSphereGeometry = new THREE.SphereGeometry(1, 8, 4);
  const smallCylinderGeometry = new THREE.CylinderGeometry(1, 1, 1, 8);
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

  function makeKit(id, variant = 0, screenSide = 0) {
    const batches = new Map();
    const solidParts = [];
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
      if (uv) for (let i = 0; i < positions.count; i++) {
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
      rounded('lacquerBlack', side * x, y, z, 0.24, 0.66, 0.40, 0.045);
      for (const [dy, radius] of [[-0.12, 0.11], [0.18, 0.055]]) {
        cylinder(names.accent, side * (x - 0.13), y + dy, z, radius + 0.018, 0.025, 0, 0, Math.PI / 2);
        cylinder('lacquerBlack', side * (x - 0.15), y + dy, z, radius, 0.03, 0, 0, Math.PI / 2);
        sphere('iron', side * (x - 0.18), y + dy, z, radius * 0.38);
      }
      for (let i = 0; i < 5; i++) box('iron', side * (x - 0.2), y - 0.25 + i * 0.11, z, 0.012, 0.009, 0.30);
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
        for (const z of side === screenSide ? [] : [-1.8, 0, 1.8]) {
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
      if (variant && screenSide !== -1) archiveShelf(-1);
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
        if (side === screenSide) {
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
      for (const side of [-1, 1].filter(side => side !== screenSide)) for (const z of [-2, 2]) {
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
        box('chrome', side * 5.73, 0.49, 0, 0.65, 0.98, 3.6);
        for (const z of [-1.25, -0.42, 0.42, 1.25]) {
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
      instrumentRack(screenSide ? -screenSide : variant ? 1 : -1);
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
        box('pearl', side * 5.12, 0.34, 0, 1.2, 0.68, 3.6);
        box(side === screenSide ? 'wood' : 'dark', side * 5.12, 0.71, 0, 1.0, 0.1, 3.4);
        box('brass', side * 4.48, 0.68, 0, 0.045, 0.04, 3.64);
        for (const z of side === screenSide ? [] : [-1.6, 0, 1.6]) {
          panel(side, 5.97, 2.64, z, 1.66, 1.42, 'pearl');
          for (const dy of [-0.45, 0, 0.45]) box('mint', side * 5.78, 2.64 + dy, z, 0.045, 0.028, 1.2);
          box('mint', side * 5.76, 2.64, z, 0.045, 1.43, 0.035);
        }
        for (const z of [-3.18, 3.18]) panel(side, 4.47, 1.65, z, 2.8, 0.89, 'pearl');
        grille(side, 4.33, 0.58, variant ? 3.18 : -3.18, 0.62, 0.34);
        for (const z of side === screenSide ? [] : [-1.4, -0.65, 0.1, 0.85, 1.6]) {
          cylinder('mint', side * 5.12, 1.18, z, 0.045, 0.95);
          for (let leaf = 0; leaf < 3; leaf++) {
            const leafGeometry = new THREE.SphereGeometry(1, 10, 6);
            temporary.add(leafGeometry);
            add(leafGeometry, 'mint', side * (5.12 + (leaf - 1) * 0.19), 1.15 + leaf * 0.25 + Math.sin(z + variant) * 0.12, z, 0.35, 0.085, 0.16, 0, leaf * 0.7 + variant, side * 0.5);
          }
        }
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
      for (const z of [-0.25, 1.85]) rounded('pearl', benchX, 0.36, z, 0.65, 0.72, 0.22, 0.04);
      rounded('wood', benchX, 0.76, 0.8, 0.72, 0.14, 2.9, 0.045);
      for (const x of [-0.22, 0, 0.22]) box('lacquerBlack', benchX + x, 0.835, 0.8, 0.013, 0.005, 2.79);
    } else {
      for (const side of [-1, 1]) {
        box('plaster', side * 4.73, 1.9, 0, 0.26, 3.8, BAY_LENGTH, true);
        box('pearl', side * 4.57, 0.12, 0, 0.08, 0.24, BAY_LENGTH);
        box('iron', side * 4.52, 0.25, 0, 0.025, 0.025, BAY_LENGTH);
        box('pearl', side * 4.57, 3.69, 0, 0.12, 0.14, BAY_LENGTH);
        if (variant && side < 0) grille(side, 4.54, 3.16, -1.1, 0.8, 0.28);
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
    return { meshes, solid };
  }

  for (const id of ROOM_IDS) for (let variant = 0; variant < 2; variant++) templates.set(`${id}/${variant}/0`, makeKit(id, variant));
  for (const [center, side] of SCREEN_NICHES) {
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
  const bays = Array.from({ length: lite ? 9 : 11 }, () => ({ node: new THREE.Group(), rails: new THREE.Group(), index: null, occluder: null }));
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
  }
  function buildEntry(entry, index) {
    clearEntry(entry);
    entry.index = index;
    const center = index * BAY_LENGTH + BAY_LENGTH / 2;
    const id = roomAt(center);
    const template = templates.get(`${id}/${Math.abs(index) % 2}/${SCREEN_NICHES.get(center) || 0}`);
    entry.node.name = `Museum ${id} ${index}`;
    for (const item of template.meshes) {
      const mesh = new THREE.Mesh(deform(item.geometry, center), materials[item.name]);
      mesh.castShadow = !item.name.endsWith('Light'); mesh.receiveShadow = true;
      entry.node.add(mesh);
    }
    entry.occluder = new THREE.Mesh(deform(template.solid, center), occlusionMaterial);
    entry.occluder.updateMatrixWorld(true);
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
  function update(distance) {
    const first = Math.floor(distance / BAY_LENGTH) - 3;
    for (let i = 0; i < bays.length; i++) {
      const index = first + i;
      const entry = bays[((index % bays.length) + bays.length) % bays.length];
      if (entry.index !== index) buildEntry(entry, index);
      entry.node.visible = entry.rails.visible = index * BAY_LENGTH < 608;
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
      finalWallGeometry.dispose(); occlusionMaterial.dispose(); group.removeFromParent();
    },
  };
}
