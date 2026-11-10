import { GLTFLoader } from '../../../vendor/three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from '../../../vendor/three/addons/libs/meshopt_decoder.module.js';
import { mergeGeometries } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { trackPoint, trackYaw, trackStyle } from './track.js';

const COLLECTION_ROOT = new URL('../../../backroom/room/assets/customization/', import.meta.url);

/** Museum furniture, the handcar, and the existing Backroom sculpture collection. */
export function createMuseumProps(THREE, materials, { lite = false, onWake = () => {} } = {}) {
  const group = new THREE.Group();
  group.name = 'museum_physical_exhibits';
  const cart = new THREE.Group();
  cart.name = 'museum_handcar';
  const interactive = [];
  const geometries = new Set();
  const ownedMaterials = new Set();
  const importedGeometries = new Set();
  const importedMaterials = new Set();
  const importedTextures = new Set();
  const cache = new Map();
  let disposed = false;

  function material(source, settings) {
    const result = source.clone();
    Object.assign(result, settings);
    ownedMaterials.add(result);
    return result;
  }
  const leather = material(materials.leather || materials.woodDark, { roughness: 0.82, metalness: 0 });
  leather.color.set('#25242a');
  const enamel = material(materials.lacquerBlack || materials.iron, { roughness: 0.28, metalness: 0.3 });
  enamel.color.set('#282933');
  const chrome = material(materials.chrome || materials.brass, { roughness: 0.26, metalness: 0.82 });
  chrome.color.set('#bbc9cf');
  const rubber = material(materials.rubber || materials.dark, { roughness: 0.94, metalness: 0 });
  rubber.color.set('#16191c');
  const ceramic = material(materials.pearl || materials.paper, { roughness: 0.3, metalness: 0.03 });
  ceramic.color.set('#ded5d2');
  const nailMaterial = material(ceramic, { roughness: 0.22 });
  nailMaterial.color.set('#ede4df');
  const pink = new THREE.MeshStandardMaterial({ color: '#ed80bd', emissive: '#d84293', emissiveIntensity: 0.65, roughness: 0.4 });
  const cyan = new THREE.MeshStandardMaterial({ color: '#68c9d9', emissive: '#3292a5', emissiveIntensity: 0.6, roughness: 0.4 });
  const ivory = new THREE.MeshStandardMaterial({ color: '#ded9e1', roughness: 0.47 });
  ownedMaterials.add(pink); ownedMaterials.add(cyan); ownedMaterials.add(ivory);
  const leafMaterial = material(materials.plaster, { roughness: 0.95 });
  leafMaterial.color.set('#536b39');
  const feltMaterial = material(materials.velvet, { roughness: 1 });
  feltMaterial.color.set('#4a304d');
  const buttonMaterial = material(materials.paper, { roughness: 0.75 });
  buttonMaterial.color.set('#e9e6ec');

  function geometry(key, build) {
    if (!cache.has(key)) {
      const value = build();
      geometries.add(value);
      cache.set(key, value);
    }
    return cache.get(key);
  }
  function box(width, height, depth, bevel = 0.025) {
    const radius = Math.min(bevel, width / 5, height / 5, depth / 5);
    return geometry(`box:${width}:${height}:${depth}:${radius}`, () => {
      const shape = new THREE.Shape();
      const x = -width / 2 + radius, y = -height / 2 + radius;
      const w = width - radius * 2, h = height - radius * 2;
      shape.moveTo(x, y); shape.lineTo(x + w, y); shape.lineTo(x + w, y + h);
      shape.lineTo(x, y + h); shape.closePath();
      const result = new THREE.ExtrudeGeometry(shape, {
        depth: depth - radius * 2, bevelEnabled: radius > 0,
        bevelThickness: radius, bevelSize: radius, bevelSegments: lite ? 1 : 3, steps: 1,
      });
      result.translate(0, 0, -depth / 2 + radius);
      return result;
    });
  }
  function cylinder(top, bottom, height, sides = 24) {
    return geometry(`cylinder:${top}:${bottom}:${height}:${sides}`, () => new THREE.CylinderGeometry(top, bottom, height, sides));
  }
  function sphere(radius, width = 16, height = 10) {
    return geometry(`sphere:${radius}:${width}:${height}`, () => new THREE.SphereGeometry(radius, width, height));
  }
  function ring(parent, radius, thickness, surface, y) {
    const circle = geometry(`ring:${radius}:${thickness}`, () => new THREE.TorusGeometry(radius, thickness, 8, lite ? 32 : 56));
    const item = mesh(parent, circle, surface, 0, y, 0);
    item.rotation.x = Math.PI / 2;
    return item;
  }
  function mesh(parent, shape, surface, x = 0, y = 0, z = 0) {
    const item = new THREE.Mesh(shape, surface);
    item.position.set(x, y, z);
    item.castShadow = surface !== materials.glass;
    item.receiveShadow = true;
    parent.add(item);
    return item;
  }
  function beam(parent, a, b, radius, surface) {
    const start = new THREE.Vector3(...a), end = new THREE.Vector3(...b);
    const delta = end.clone().sub(start);
    const item = mesh(parent, cylinder(radius, radius, delta.length(), 12), surface);
    item.position.copy(start).add(end).multiplyScalar(0.5);
    item.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), delta.normalize());
    return item;
  }
  function lathe(points, segments = 32) {
    const result = new THREE.LatheGeometry(points.map(([x, y]) => new THREE.Vector2(x, y)), segments);
    geometries.add(result);
    return result;
  }
  function tube(parent, points, radius, surface, segments = 36) {
    const curve = new THREE.CatmullRomCurve3(points.map(point => new THREE.Vector3(...point)));
    const shape = new THREE.TubeGeometry(curve, lite ? Math.ceil(segments * 0.65) : segments, radius, 6, false);
    geometries.add(shape);
    return mesh(parent, shape, surface);
  }
  function fastener(parent, x, y, z, size = 0.012, axis = 'z') {
    const normal = axis === 'y' ? new THREE.Vector3(0, 1, 0) : axis === 'x' ? new THREE.Vector3(1, 0, 0) : new THREE.Vector3(0, 0, 1);
    const washer = mesh(parent, cylinder(size * 1.45, size * 1.45, size * 0.22, 16), chrome, x, y, z);
    const head = mesh(parent, cylinder(size, size, size * 0.52, 6), chrome, x + normal.x * size * 0.32, y + normal.y * size * 0.32, z + normal.z * size * 0.32);
    for (const part of [washer, head]) part.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), normal);
    const socket = mesh(parent, cylinder(size * 0.43, size * 0.43, size * 0.025, 6), rubber, x + normal.x * size * 0.6, y + normal.y * size * 0.6, z + normal.z * size * 0.6);
    socket.quaternion.copy(head.quaternion);
  }
  function spring(parent, x, y, z, radius, height, turns = 6) {
    const shape = geometry(`spring:${radius}:${height}:${turns}`, () => {
      const points = Array.from({ length: turns * 12 + 1 }, (_, i) => {
        const angle = i / 12 * Math.PI * 2;
        return new THREE.Vector3(Math.cos(angle) * radius, i / (turns * 12) * height, Math.sin(angle) * radius);
      });
      return new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points), turns * (lite ? 8 : 12), 0.006, 5, false);
    });
    return mesh(parent, shape, chrome, x, y, z);
  }
  function bezel(width, height, border, depth, chamfer = 0.045) {
    return geometry(`bezel:${width}:${height}:${border}:${depth}:${chamfer}`, () => {
      function outline(w, h, cut, reverse = false) {
        const points = [[-w / 2 + cut, -h / 2], [w / 2 - cut, -h / 2], [w / 2, -h / 2 + cut], [w / 2, h / 2 - cut], [w / 2 - cut, h / 2], [-w / 2 + cut, h / 2], [-w / 2, h / 2 - cut], [-w / 2, -h / 2 + cut]];
        if (reverse) points.reverse();
        const path = new THREE.Shape(); path.moveTo(...points[0]);
        points.slice(1).forEach(point => path.lineTo(...point)); path.closePath();
        return path;
      }
      const shape = outline(width, height, chamfer);
      shape.holes.push(outline(width - border * 2, height - border * 2, Math.max(0.005, chamfer - border * 0.4), true));
      const result = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: true, bevelThickness: 0.003, bevelSize: 0.003, bevelSegments: lite ? 1 : 2, steps: 1 });
      result.translate(0, 0, -depth / 2);
      return result;
    });
  }
  function loft(key, sections, sides = 24) {
    return geometry(key, () => {
      const positions = [], uv = [], indices = [];
      sections.forEach(([z, width, height, centerY = 0, centerX = 0], row) => {
        for (let i = 0; i <= sides; i++) {
          const angle = i / sides * Math.PI * 2;
          positions.push(centerX + Math.sin(angle) * width, centerY + Math.cos(angle) * height, z);
          uv.push(i / sides, row / (sections.length - 1));
          if (row < sections.length - 1 && i < sides) {
            const a = row * (sides + 1) + i, b = a + sides + 1;
            indices.push(a, b, a + 1, b, b + 1, a + 1);
          }
        }
      });
      const shape = new THREE.BufferGeometry();
      shape.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
      shape.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      shape.setIndex(indices); shape.computeVertexNormals();
      return shape;
    });
  }
  function taperedTube(parent, points, radii, surface) {
    const curve = new THREE.CatmullRomCurve3(points.map(point => new THREE.Vector3(...point)));
    const segments = lite ? 15 : 24, sides = 10;
    const frames = curve.computeFrenetFrames(segments, false);
    const positions = [], uv = [], indices = [];
    for (let i = 0; i <= segments; i++) {
      const t = i / segments, center = curve.getPointAt(t), n = t * (radii.length - 1);
      const radius = THREE.MathUtils.lerp(radii[Math.floor(n)], radii[Math.min(radii.length - 1, Math.floor(n) + 1)], n % 1);
      for (let j = 0; j <= sides; j++) {
        const angle = j / sides * Math.PI * 2;
        const point = center.clone().addScaledVector(frames.normals[i], Math.cos(angle) * radius).addScaledVector(frames.binormals[i], Math.sin(angle) * radius);
        positions.push(point.x, point.y, point.z); uv.push(j / sides, t);
        if (i < segments && j < sides) {
          const a = i * (sides + 1) + j, b = a + sides + 1;
          indices.push(a, a + 1, b, b, a + 1, b + 1);
        }
      }
    }
    const shape = new THREE.BufferGeometry();
    shape.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    shape.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    shape.setIndex(indices); shape.computeVertexNormals(); geometries.add(shape);
    return mesh(parent, shape, surface);
  }
  // One geometry per furniture material preserves UVs while keeping the display pool inexpensive.
  function batch(parent) {
    parent.updateMatrixWorld(true);
    const sets = new Map();
    const inverse = parent.matrixWorld.clone().invert();
    for (const node of [...parent.children]) {
      if (!node.isMesh || node.material.transparent || node.userData.interactive) continue;
      if (!sets.has(node.material)) sets.set(node.material, []);
      const copy = node.geometry.clone().applyMatrix4(new THREE.Matrix4().multiplyMatrices(inverse, node.matrixWorld));
      const flat = copy.index ? copy.toNonIndexed() : copy;
      if (flat !== copy) copy.dispose();
      sets.get(node.material).push(flat);
      node.removeFromParent();
    }
    for (const [surface, parts] of sets) {
      const merged = mergeGeometries(parts, false);
      parts.forEach(part => part.dispose());
      if (!merged) continue;
      geometries.add(merged);
      mesh(parent, merged, surface);
    }
  }

  // The compact enamel dashboard retains a physical pivot, rod and hand grips.
  const hull = new THREE.Group();
  hull.position.set(0, 0.16, -0.52);
  cart.add(hull);
  mesh(hull, box(1.35, 0.09, 0.15, 0.035), enamel, 0, -0.78, -0.98);
  mesh(hull, box(1.39, 0.018, 0.16, 0.008), chrome, 0, -0.717, -0.98);
  mesh(hull, box(1.30, 0.15, 0.04, 0.018), enamel, 0, -0.88, -0.94);
  mesh(hull, box(1.18, 0.009, 0.012, 0.003), cyan, 0, -0.75, -0.883);
  for (const side of [-1, 1]) {
    mesh(hull, box(0.085, 0.12, 0.82, 0.025), enamel, side * 0.65, -0.83, -0.56);
    mesh(hull, box(0.10, 0.012, 0.86, 0.006), chrome, side * 0.65, -0.756, -0.56);
    for (const z of [-0.87, -0.55, -0.27]) {
      fastener(hull, side * 0.65, -0.744, z, 0.012, 'y');
    }
    mesh(hull, box(0.026, 0.012, 0.62, 0.006), rubber, side * 0.65, -0.745, -0.56);
    mesh(hull, box(0.16, 0.055, 0.11, 0.014), chrome, side * 0.54, -0.797, -0.935);
    fastener(hull, side * 0.56, -0.786, -0.873, 0.013);
  }
  mesh(hull, box(0.28, 0.06, 0.18, 0.015), materials.iron, 0, -0.94, -0.73);
  const axle = mesh(hull, cylinder(0.055, 0.055, 0.23), chrome, 0, -0.91, -0.73);
  axle.rotation.z = Math.PI / 2;
  for (const x of [-0.15, 0.15]) mesh(hull, sphere(0.024), pink, x, -0.89, -0.73);
  mesh(hull, box(0.25, 0.105, 0.018, 0.018), materials.dark, -0.4, -0.8, -0.88);
  mesh(hull, bezel(0.274, 0.127, 0.012, 0.008, 0.015), chrome, -0.4, -0.8, -0.863);
  for (let i = 0; i < 5; i++) mesh(hull, box(0.018, 0.018 + i * 0.012, 0.01, 0.004), i < 3 ? cyan : pink, -0.48 + i * 0.04, -0.8, -0.863);
  for (const x of [-0.56, 0.56]) fastener(hull, x, -0.875, -0.913, 0.011);
  mesh(hull, box(0.35, 0.005, 0.008, 0.002), rubber, 0.33, -0.83, -0.914);
  for (let i = 0; i < 7; i++) mesh(hull, box(0.025, 0.035, 0.008, 0.006), rubber, 0.2 + i * 0.04, -0.875, -0.913);
  const lever = new THREE.Group();
  lever.name = 'handcar_lever_pivot';
  lever.position.set(0, -0.75, -1.25);
  cart.add(lever);
  mesh(lever, cylinder(0.019, 0.028, 0.27), materials.iron, 0, 0.135, 0);
  mesh(lever, cylinder(0.036, 0.036, 0.055), chrome, 0, 0.24, 0);
  const crossbar = mesh(lever, cylinder(0.028, 0.028, 0.63), chrome, 0, 0.27, 0);
  crossbar.rotation.z = Math.PI / 2;
  for (const sign of [-1, 1]) {
    const grip = mesh(lever, cylinder(0.042, 0.037, 0.22), leather, sign * 0.205, 0.27, 0);
    grip.rotation.z = Math.PI / 2;
    const cap = mesh(lever, cylinder(0.045, 0.045, 0.025), sign < 0 ? cyan : pink, sign * 0.325, 0.27, 0);
    cap.rotation.z = Math.PI / 2;
    for (let i = 0; i < 11; i++) {
      const offset = -0.09 + i * 0.018;
      const seam = mesh(lever, cylinder(0.041, 0.041, 0.0015, 24), rubber, sign * 0.205 + offset, 0.27, 0);
      seam.rotation.z = Math.PI / 2;
      beam(lever, [sign * 0.205 + offset - 0.004, 0.281, 0.039], [sign * 0.205 + offset + 0.004, 0.287, 0.037], 0.0013, chrome);
    }
    fastener(lever, sign * 0.34, 0.27, 0, 0.017, 'x');
  }
  const pumpGrip = new THREE.Object3D(); pumpGrip.position.set(0, 0.27, 0); lever.add(pumpGrip);
  batch(hull);
  batch(lever);

  function plinth(parent, width = 1.22, depth = 1.05, height = 0.82) {
    mesh(parent, box(width, 0.10, depth, 0.025), enamel, 0, 0.05, 0);
    mesh(parent, box(width - 0.13, height - 0.18, depth - 0.13, 0.04), enamel, 0, height / 2, 0);
    mesh(parent, box(width + 0.05, 0.075, depth + 0.05, 0.025), chrome, 0, height - 0.04, 0);
    mesh(parent, box(width + 0.08, 0.014, depth + 0.08, 0.006), cyan, 0, height + 0.01, 0);
    for (const x of [-width / 2 + 0.12, width / 2 - 0.12]) {
      mesh(parent, box(0.025, height - 0.22, 0.025, 0.008), chrome, x, height / 2, depth / 2 - 0.06);
    }
    // Inset panels establish scale and keep the cabinet legible under grazing light.
    mesh(parent, box(width - 0.35, height - 0.32, 0.028, 0.018), materials.dark, 0, height / 2, depth / 2 - 0.055);
    mesh(parent, box(width - 0.41, 0.018, 0.034, 0.008), pink, 0, height / 2, depth / 2 - 0.034);
    for (const x of [-width / 2 + 0.16, width / 2 - 0.16]) for (const z of [-depth / 2 + 0.16, depth / 2 - 0.16]) {
      mesh(parent, cylinder(0.047, 0.055, 0.035, 16), rubber, x, 0.018, z);
      mesh(parent, cylinder(0.028, 0.028, 0.045, 16), chrome, x, 0.055, z);
    }
    mesh(parent, box(width - 0.2, 0.006, 0.018, 0.002), rubber, 0, 0.14, depth / 2 - 0.053);
    mesh(parent, box(width - 0.2, 0.006, 0.018, 0.002), rubber, 0, height - 0.14, depth / 2 - 0.053);
    for (const y of [0.22, height - 0.23]) {
      mesh(parent, box(0.042, 0.08, 0.019, 0.007), chrome, -width / 2 + 0.14, y, depth / 2 - 0.025);
      mesh(parent, cylinder(0.012, 0.012, 0.095, 12), chrome, -width / 2 + 0.11, y, depth / 2 - 0.014);
    }
    mesh(parent, box(0.065, 0.095, 0.024, 0.012), chrome, width / 2 - 0.17, height * 0.56, depth / 2 - 0.025);
    fastener(parent, width / 2 - 0.17, height * 0.56 + 0.022, depth / 2 - 0.01, 0.009);
    mesh(parent, box(0.018, 0.035, 0.009, 0.004), rubber, width / 2 - 0.17, height * 0.56 - 0.012, depth / 2 - 0.009);
    for (let i = 0; i < 6; i++) mesh(parent, box(0.005, 0.065, 0.008, 0.002), rubber, -0.075 + i * 0.03, 0.22, depth / 2 - 0.034);
  }
  function caseFrame(parent, width, depth, bottom, top) {
    const tube = 0.022;
    for (const x of [-width / 2, width / 2]) for (const z of [-depth / 2, depth / 2]) {
      mesh(parent, box(tube, top - bottom, tube, 0.005), chrome, x, (top + bottom) / 2, z);
      mesh(parent, box(0.065, 0.045, 0.065, 0.01), chrome, x, bottom + 0.012, z);
      mesh(parent, box(0.052, 0.035, 0.052, 0.009), rubber, x, bottom - 0.018, z);
      mesh(parent, box(0.05, 0.042, 0.05, 0.008), chrome, x, top - 0.006, z);
      fastener(parent, x, bottom + 0.013, z + 0.035, 0.01);
    }
    for (const y of [bottom, top]) {
      for (const x of [-width / 2, width / 2]) mesh(parent, box(tube, tube, depth, 0.005), chrome, x, y, 0);
      for (const z of [-depth / 2, depth / 2]) mesh(parent, box(width, tube, tube, 0.005), chrome, 0, y, z);
    }
    // Two side panes and a roof produce a legible case without stacking many transparent faces.
    const pane = box(0.008, top - bottom, depth - 0.035, 0.002);
    for (const side of [-1, 1]) mesh(parent, pane, materials.glass, side * width / 2, (top + bottom) / 2, 0);
    mesh(parent, box(width, 0.008, depth, 0.002), materials.glass, 0, top, 0);
    for (const y of [bottom + 0.2, top - 0.2]) {
      mesh(parent, box(0.075, 0.08, 0.025, 0.01), chrome, -width / 2 + 0.018, y, depth / 2 + 0.014);
      mesh(parent, cylinder(0.015, 0.015, 0.105, 12), chrome, -width / 2 + 0.025, y, depth / 2 + 0.033);
      fastener(parent, -width / 2 + 0.04, y, depth / 2 + 0.029, 0.009);
    }
    mesh(parent, box(0.07, 0.095, 0.027, 0.008), chrome, width / 2 - 0.035, bottom + 0.48, depth / 2 + 0.01);
    mesh(parent, box(0.008, 0.033, 0.005, 0.002), rubber, width / 2 - 0.035, bottom + 0.48, depth / 2 + 0.028);
  }
  function label(parent, text, y = 0.93, z = 0.56, width = 0.46) {
    const holder = mesh(parent, box(width + 0.025, 0.15, 0.025, 0.008), rubber, 0, y, z);
    holder.rotation.x = -Math.PI / 8;
    const frame = mesh(parent, bezel(width + 0.018, 0.14, 0.012, 0.012, 0.018), chrome, 0, y, z + 0.012);
    frame.rotation.x = holder.rotation.x;
    for (const x of [-width / 2 + 0.016, width / 2 - 0.016]) fastener(parent, x, y, z + 0.025, 0.007);
    const canvas = document.createElement('canvas');
    canvas.width = 512; canvas.height = 144;
    const paint = canvas.getContext('2d');
    const metal = paint.createLinearGradient(0, 0, 0, 144); metal.addColorStop(0, '#c5c9cc'); metal.addColorStop(0.5, '#eee9e3'); metal.addColorStop(1, '#a4abb1');
    paint.fillStyle = metal; paint.fillRect(0, 0, canvas.width, canvas.height);
    paint.fillStyle = '#66747c30'; for (let i = 0; i < 144; i += 3) paint.fillRect(0, i, 512, 0.5);
    paint.fillStyle = '#52616c'; paint.font = '600 14px Arial'; paint.textAlign = 'center'; paint.textBaseline = 'middle';
    paint.fillText('COLLECTION / OBJECT RECORD', 256, 34, 440);
    paint.font = '600 36px Arial'; paint.fillStyle = '#ffffffa0'; paint.fillText(text, 257, 82, 450);
    paint.fillStyle = '#25333f'; paint.fillText(text, 256, 81, 450);
    const map = new THREE.CanvasTexture(canvas);
    map.colorSpace = THREE.SRGBColorSpace;
    importedTextures.add(map);
    const surface = new THREE.MeshStandardMaterial({ map, roughness: 0.54, metalness: 0.45 });
    ownedMaterials.add(surface);
    const face = mesh(parent, geometry(`label-plane:${width}`, () => new THREE.PlaneGeometry(width - 0.02, 0.11)), surface, 0, y, z + 0.018);
    face.rotation.x = holder.rotation.x;
  }
  function stanchion(parent, x, z) {
    const profile = [[0, 0], [0.115, 0], [0.135, 0.025], [0.135, 0.04], [0.07, 0.075], [0.025, 0.12], [0.022, 0.62], [0.044, 0.65], [0.044, 0.68], [0.025, 0.71], [0, 0.71]];
    mesh(parent, lathe(profile, lite ? 16 : 28), chrome, x, 0, z);
    mesh(parent, sphere(0.052), pink, x, 0.745, z);
  }
  function rope(parent, a, b) {
    const curve = new THREE.CatmullRomCurve3([
      new THREE.Vector3(...a), new THREE.Vector3((a[0] + b[0]) / 2, Math.min(a[1], b[1]) - 0.14, (a[2] + b[2]) / 2), new THREE.Vector3(...b),
    ]);
    const tube = new THREE.TubeGeometry(curve, lite ? 8 : 14, 0.023, 6, false);
    geometries.add(tube);
    mesh(parent, tube, feltMaterial);
  }
  // These displays are authored diagrams, not remote media surfaces.
  function screen(parent, heading, caption, kind, x = 0, y = 1.78, z = -0.52) {
    const stand = new THREE.Group();
    stand.position.set(x, 0, z);
    parent.add(stand);
    mesh(stand, box(0.72, 0.095, 0.54, 0.035), enamel, 0, 0.067, -0.015);
    mesh(stand, box(0.69, 0.012, 0.51, 0.025), chrome, 0, 0.115, -0.015);
    for (const sx of [-0.25, 0.25]) for (const sz of [-0.18, 0.16]) {
      mesh(stand, cylinder(0.04, 0.045, 0.025, 12), rubber, sx, 0.015, sz);
      fastener(stand, sx, 0.125, sz, 0.014, 'y');
    }
    mesh(stand, cylinder(0.035, 0.045, y - 0.1), chrome, 0, y / 2, -0.025);
    mesh(stand, box(0.105, 0.3, 0.12, 0.025), enamel, 0, 0.28, -0.025);
    mesh(stand, cylinder(0.06, 0.06, 0.028, 24), chrome, 0, 0.45, -0.025);
    beam(stand, [0, y - 0.31, -0.025], [0, y - 0.12, -0.17], 0.04, chrome);
    const tilt = mesh(stand, cylinder(0.075, 0.075, 0.19, 20), enamel, 0, y - 0.1, -0.16); tilt.rotation.z = Math.PI / 2;
    fastener(stand, 0.1, y - 0.1, -0.16, 0.022, 'x');
    mesh(stand, box(1.16, 0.8, 0.16, 0.045), enamel, 0, y, -0.04);
    mesh(stand, bezel(1.184, 0.824, 0.018, 0.014, 0.055), chrome, 0, y, -0.012);
    mesh(stand, bezel(1.16, 0.80, 0.06, 0.055, 0.05), enamel, 0, y, 0.043);
    mesh(stand, box(1.035, 0.007, 0.018, 0.003), kind === 'pump' ? pink : cyan, 0, y - 0.365, 0.064);
    for (const sx of [-0.55, 0.55]) for (const sy of [-0.35, 0.35]) fastener(stand, sx, y + sy, 0.072, 0.008);
    for (let i = 0; i < 11; i++) mesh(stand, box(0.038, 0.11, 0.005, 0.008), rubber, -0.35 + i * 0.07, y + 0.15, -0.123);
    mesh(stand, box(0.27, 0.14, 0.055, 0.018), chrome, 0, y - 0.11, -0.13);
    for (const sx of [-0.1, 0.1]) for (const sy of [-0.04, 0.04]) fastener(stand, sx, y - 0.11 + sy, -0.165, 0.008);
    for (const sx of [-0.43, -0.38, -0.33]) mesh(stand, cylinder(0.011, 0.011, 0.013, 12), rubber, sx, y - 0.407, 0.005);
    mesh(stand, sphere(0.008, 8, 6), cyan, 0.49, y - 0.365, 0.078);
    tube(stand, [[0.1, y - 0.26, -0.13], [0.12, y - 0.43, -0.15], [0.07, y - 0.49, -0.085], [0.065, 0.22, -0.085], [0.1, 0.16, -0.15]], 0.009, rubber, 26);
    const canvas = document.createElement('canvas');
    canvas.width = 1024; canvas.height = 640;
    const paint = canvas.getContext('2d');
    paint.fillStyle = '#131420'; paint.fillRect(0, 0, 1024, 640);
    paint.strokeStyle = '#39404f'; paint.lineWidth = 2;
    for (let i = 0; i < 11; i++) { paint.beginPath(); paint.moveTo(54 + i * 92, 190); paint.lineTo(54 + i * 92, 520); paint.stroke(); }
    for (let i = 0; i < 5; i++) { paint.beginPath(); paint.moveTo(54, 190 + i * 82); paint.lineTo(970, 190 + i * 82); paint.stroke(); }
    paint.fillStyle = kind === 'pump' ? '#ff90cd' : '#89e7ed';
    paint.font = '600 23px Arial'; paint.fillText('EXHIBIT / ' + kind.toUpperCase(), 58, 59);
    paint.fillStyle = '#f3f0f4'; paint.font = '600 58px Arial'; paint.fillText(heading, 54, 137, 920);
    if (kind === 'pump') {
      paint.strokeStyle = '#ff90cd'; paint.lineWidth = 10; paint.beginPath();
      for (let i = 0; i <= 100; i++) {
        const px = 58 + i * 9.05, py = 345 - Math.sin(i * Math.PI / 18) * 100;
        if (i === 0) paint.moveTo(px, py); else paint.lineTo(px, py);
      }
      paint.stroke();
      paint.fillStyle = '#9be6e6'; paint.font = '600 25px Arial'; paint.fillText('INPUT', 67, 491); paint.fillText('MOVEMENT', 767, 491);
    } else if (kind === 'reflex') {
      const dots = [[180, 330], [500, 330], [820, 330]];
      paint.strokeStyle = '#80dae3'; paint.lineWidth = 7; paint.beginPath(); paint.moveTo(180, 330); paint.lineTo(820, 330); paint.stroke();
      dots.forEach(([cx, cy], i) => {
        paint.fillStyle = i === 1 ? '#ff95cf' : '#83e0e7'; paint.beginPath(); paint.arc(cx, cy, 52, 0, Math.PI * 2); paint.fill();
        paint.fillStyle = '#14151e'; paint.font = '600 47px Arial'; paint.textAlign = 'center'; paint.fillText(String(i + 1), cx, cy + 16);
      });
      paint.fillStyle = '#e7e4ee'; paint.font = '600 27px Arial'; paint.fillText('NOTICE', 180, 455); paint.fillText('CLICK', 500, 455); paint.fillText('REPEAT', 820, 455); paint.textAlign = 'left';
    } else {
      paint.fillStyle = '#ff9dce'; paint.beginPath(); paint.moveTo(460, 255); paint.lineTo(610, 345); paint.lineTo(460, 435); paint.closePath(); paint.fill();
      paint.strokeStyle = '#8de5e6'; paint.lineWidth = 6; paint.beginPath(); paint.arc(520, 345, 142, 0, Math.PI * 2); paint.stroke();
    }
    paint.fillStyle = '#b9b9c9'; paint.font = '26px Arial'; paint.fillText(caption, 57, 585, 920);
    const map = new THREE.CanvasTexture(canvas); map.colorSpace = THREE.SRGBColorSpace;
    importedTextures.add(map);
    const surface = new THREE.MeshBasicMaterial({ map, toneMapped: false });
    ownedMaterials.add(surface);
    mesh(stand, geometry('exhibit-screen', () => new THREE.PlaneGeometry(1.035, 0.647)), surface, 0, y, 0.055);
    batch(stand);
    return stand;
  }
  const fixedExhibits = [];
  const kineticUpdates = [];
  function exhibit(name, at, side) {
    const station = new THREE.Group();
    station.name = name;
    positionAt(station, at, side, side > 0 ? -Math.PI / 3 : Math.PI / 3);
    group.add(station);
    fixedExhibits.push({ station, at });
    return station;
  }
  const pumpStation = exhibit('museum_pumping_research', 49, -2.7);
  plinth(pumpStation, 1.62, 1.15, 0.84);
  ring(pumpStation, 0.7, 0.014, pink, 0.105);
  mesh(pumpStation, box(1.18, 0.08, 0.7, 0.035), ivory, 0, 0.93, 0);
  mesh(pumpStation, box(1.25, 0.025, 0.77, 0.022), chrome, 0, 0.975, 0);
  for (const x of [-0.53, 0.53]) for (const z of [-0.26, 0.26]) fastener(pumpStation, x, 0.992, z, 0.018, 'y');
  const pistons = [];
  for (const side of [-1, 1]) {
    const x = side * 0.34;
    mesh(pumpStation, lathe([[0, 0], [0.12, 0], [0.12, 0.025], [0.092, 0.04], [0.092, 0.19], [0.107, 0.20], [0.107, 0.225], [0.075, 0.235], [0, 0.235]], lite ? 20 : 32), enamel, x, 0.995, 0);
    for (const y of [1.025, 1.195]) mesh(pumpStation, cylinder(0.099, 0.099, 0.023), chrome, x, y, 0);
    mesh(pumpStation, cylinder(0.074, 0.074, 0.016), rubber, x, 1.235, 0);
    for (const dx of [-0.074, 0.074]) for (const dz of [-0.074, 0.074]) {
      mesh(pumpStation, cylinder(0.007, 0.007, 0.19, 10), chrome, x + dx, 1.115, dz);
      fastener(pumpStation, x + dx, 1.218, dz, 0.012, 'y');
    }
    mesh(pumpStation, box(0.055, 0.06, 0.02, 0.006), side < 0 ? pink : cyan, x, 1.1, 0.098);
    const rod = mesh(pumpStation, cylinder(0.024, 0.024, 1, 16), chrome, x, 1.32, 0); rod.userData.interactive = true;
    const returnSpring = spring(pumpStation, x, 1.237, 0, 0.047, 0.16, 6); returnSpring.userData.interactive = true;
    pistons.push({ side, rod, returnSpring });
  }
  beam(pumpStation, [-0.16, 1.0, -0.18], [0, 1.43, -0.09], 0.027, chrome);
  beam(pumpStation, [0.16, 1.0, -0.18], [0, 1.43, -0.09], 0.027, chrome);
  const researchLever = new THREE.Group(); researchLever.position.set(0, 1.43, 0); pumpStation.add(researchLever);
  mesh(researchLever, box(1.03, 0.055, 0.105, 0.018), chrome);
  const pivotBearing = mesh(researchLever, cylinder(0.075, 0.075, 0.14, 24), enamel); pivotBearing.rotation.x = Math.PI / 2;
  fastener(researchLever, 0, 0, 0.079, 0.028);
  for (const side of [-1, 1]) {
    const grip = mesh(researchLever, cylinder(0.037, 0.037, 0.145), leather, side * 0.46, 0, 0); grip.rotation.z = Math.PI / 2;
    for (const x of [side * 0.34, side * 0.54]) fastener(researchLever, x, 0, 0.06, 0.014);
  }
  const flywheel = new THREE.Group(); flywheel.position.set(0, 1.205, 0.3); pumpStation.add(flywheel);
  const rim = ring(flywheel, 0.19, 0.028, chrome, 0); rim.rotation.x = 0;
  const belt = ring(flywheel, 0.215, 0.008, rubber, 0); belt.rotation.x = 0;
  const hub = mesh(flywheel, cylinder(0.055, 0.055, 0.075), enamel); hub.rotation.x = Math.PI / 2;
  for (let i = 0; i < 6; i++) {
    const angle = i / 6 * Math.PI * 2;
    beam(flywheel, [Math.cos(angle) * 0.05, Math.sin(angle) * 0.05, 0], [Math.cos(angle) * 0.18, Math.sin(angle) * 0.18, 0], 0.011, chrome);
  }
  fastener(flywheel, 0, 0, 0.045, 0.019);
  beam(pumpStation, [0, 1.205, -0.03], [0, 1.205, 0.3], 0.031, chrome);
  mesh(pumpStation, box(0.1, 0.17, 0.1, 0.018), enamel, 0, 1.075, 0.2);
  tube(pumpStation, [[-0.34, 1.04, 0.1], [-0.4, 1.04, 0.27], [-0.25, 1.02, 0.33], [0.25, 1.02, 0.33], [0.4, 1.04, 0.27], [0.34, 1.04, 0.1]], 0.012, rubber, 28);
  for (const x of [-0.34, 0.34]) { const coupling = mesh(pumpStation, cylinder(0.027, 0.027, 0.06, 6), chrome, x, 1.04, 0.115); coupling.rotation.x = Math.PI / 2; }
  const gaugeBody = mesh(pumpStation, cylinder(0.075, 0.075, 0.045, 32), chrome, 0.57, 1.14, 0.12); gaugeBody.rotation.x = Math.PI / 2;
  const gaugeFace = mesh(pumpStation, cylinder(0.064, 0.064, 0.003, 32), ivory, 0.57, 1.14, 0.145); gaugeFace.rotation.x = Math.PI / 2;
  for (let i = 0; i < 9; i++) {
    const angle = -Math.PI * 0.75 + i * Math.PI * 1.5 / 8;
    beam(pumpStation, [0.57 + Math.sin(angle) * 0.046, 1.14 + Math.cos(angle) * 0.046, 0.148], [0.57 + Math.sin(angle) * 0.057, 1.14 + Math.cos(angle) * 0.057, 0.148], 0.002, rubber);
  }
  beam(pumpStation, [0.57, 1.14, 0.15], [0.59, 1.175, 0.15], 0.003, pink);
  beam(pumpStation, [0.57, 0.995, 0.12], [0.57, 1.075, 0.12], 0.018, chrome);
  kineticUpdates.push(angle => {
    const theta = angle * 0.5; researchLever.rotation.z = theta; flywheel.rotation.z = angle * 1.6;
    for (const { side, rod, returnSpring } of pistons) {
      const bottom = new THREE.Vector3(side * 0.34, 1.20, 0);
      const top = new THREE.Vector3(side * 0.34 * Math.cos(theta), 1.43 + side * 0.34 * Math.sin(theta), 0);
      const delta = top.clone().sub(bottom);
      rod.position.copy(top).add(bottom).multiplyScalar(0.5); rod.scale.y = delta.length();
      rod.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), delta.normalize());
      returnSpring.scale.y = Math.max(0.3, (top.y - 1.255) / 0.16);
    }
  });
  kineticUpdates[0](0);
  label(pumpStation, 'Pumping research', 0.8, 0.63, 0.84);
  screen(pumpStation, 'Keep it moving', 'A small input. A very committed machine.', 'pump', 0, 2.05, -0.59);
  batch(researchLever); batch(flywheel); batch(pumpStation);

  const reflexStation = exhibit('museum_reflex_lab_hand_and_mouse', 225, 2.65);
  plinth(reflexStation, 1.62, 1.28, 0.86);
  mesh(reflexStation, box(1.27, 0.055, 0.94, 0.04), feltMaterial, 0, 0.925, 0);
  ring(reflexStation, 0.69, 0.018, cyan, 0.12);
  const mouseShape = loft('mouse-sculpted-shell', [[-0.26, 0.001, 0.001], [-0.23, 0.085, 0.045], [-0.16, 0.145, 0.08], [-0.05, 0.165, 0.095], [0.08, 0.155, 0.077], [0.19, 0.14, 0.052], [0.24, 0.1, 0.035], [0.26, 0.001, 0.001]], 32);
  const mouseBase = mesh(reflexStation, mouseShape, rubber, 0, 1.01, 0.2); mouseBase.scale.y = 0.38;
  mesh(reflexStation, mouseShape, enamel, 0, 1.055, 0.2);
  const buttonShape = loft('mouse-split-button', [[-0.13, 0.001, 0.001], [-0.115, 0.058, 0.012], [-0.03, 0.064, 0.017], [0.08, 0.055, 0.012], [0.12, 0.001, 0.001]], 16);
  for (const side of [-1, 1]) {
    const button = mesh(reflexStation, buttonShape, enamel, side * 0.075, 1.132, 0.31); button.rotation.x = 0.12;
    mesh(reflexStation, box(0.007, 0.018, 0.09, 0.003), chrome, side * 0.16, 1.075, 0.12);
  }
  mesh(reflexStation, box(0.015, 0.012, 0.22, 0.004), rubber, 0, 1.139, 0.33);
  const scrollWheel = mesh(reflexStation, cylinder(0.032, 0.032, 0.027, 24), rubber, 0, 1.154, 0.25); scrollWheel.rotation.z = Math.PI / 2;
  for (const x of [-0.015, 0.015]) { const sidewall = mesh(reflexStation, cylinder(0.026, 0.026, 0.003, 24), chrome, x, 1.154, 0.25); sidewall.rotation.z = Math.PI / 2; }
  for (let i = 0; i < 14; i++) {
    const angle = i * Math.PI * 2 / 14;
    const ridge = mesh(reflexStation, box(0.028, 0.003, 0.005, 0.001), chrome, 0, 1.154 + Math.cos(angle) * 0.032, 0.25 + Math.sin(angle) * 0.032); ridge.rotation.x = angle;
  }
  const cablePoints = [[0, 1.055, 0.463], [0.025, 0.98, 0.5], [0.28, 0.965, 0.43], [0.48, 0.965, 0.18], [0.43, 0.965, -0.18], [0.57, 0.965, -0.32]];
  tube(reflexStation, cablePoints, 0.008, rubber, 50);
  const cableCurve = new THREE.CatmullRomCurve3(cablePoints.map(point => new THREE.Vector3(...point)));
  const cableFrames = cableCurve.computeFrenetFrames(100, false);
  for (const phase of [0, Math.PI]) {
    const braid = [];
    for (let i = 0; i <= 100; i++) {
      const point = cableCurve.getPointAt(i / 100), angle = i * 0.98 + phase;
      point.addScaledVector(cableFrames.normals[i], Math.cos(angle) * 0.008).addScaledVector(cableFrames.binormals[i], Math.sin(angle) * 0.008);
      braid.push(point.toArray());
    }
    tube(reflexStation, braid, 0.0014, chrome, 100);
  }
  mesh(reflexStation, box(0.075, 0.035, 0.06, 0.009), rubber, 0.57, 0.975, -0.32);
  mesh(reflexStation, box(0.055, 0.019, 0.038, 0.004), chrome, 0.61, 0.975, -0.32);
  const hand = new THREE.Group(); hand.position.set(0, 1.23, -0.2); reflexStation.add(hand);
  const palmShape = loft('hand-palm-wrist', [[-0.41, 0.001, 0.001], [-0.4, 0.079, 0.045], [-0.29, 0.087, 0.048], [-0.19, 0.125, 0.057, -0.005, -0.012], [-0.08, 0.18, 0.067, 0, -0.012], [0.045, 0.19, 0.06], [0.135, 0.171, 0.047], [0.18, 0.143, 0.025], [0.195, 0.001, 0.001]], 32);
  mesh(hand, palmShape, ceramic);
  const thumbPad = mesh(hand, sphere(0.092, 20, 12), ceramic, -0.102, -0.02, -0.02); thumbPad.scale.set(0.9, 0.67, 1.45); thumbPad.rotation.y = -0.3;
  for (let i = 0; i < 4; i++) {
    const x = -0.13 + i * 0.087, tip = [0.43, 0.46, 0.43, 0.36][i], radius = [0.034, 0.037, 0.034, 0.028][i];
    taperedTube(hand, [[x, 0.015, 0.105], [x, 0.04, 0.21], [x + 0.008, 0.015, 0.29], [x + 0.012, -0.075, tip - 0.045], [x + 0.013, -0.093, tip]], [radius * 1.1, radius, radius * 0.96, radius * 0.93, radius * 0.88, radius * 0.83, radius * 0.8, radius * 0.74, radius * 0.54, radius * 0.28, 0.001], ceramic);
    const nail = mesh(hand, sphere(1, 16, 8), nailMaterial, x + 0.01, -0.052, tip - 0.048); nail.scale.set(radius * 0.55, 0.004, 0.021); nail.rotation.x = 0.43;
    const knuckle = mesh(hand, sphere(radius, 16, 10), ceramic, x, 0.037, 0.17); knuckle.scale.set(1.06, 0.45, 1.25);
    tube(hand, [[x - 0.018, 0.057, 0.245], [x, 0.063, 0.25], [x + 0.018, 0.057, 0.245]], 0.0014, nailMaterial, 8);
  }
  taperedTube(hand, [[-0.14, -0.015, -0.07], [-0.215, -0.045, 0.025], [-0.255, -0.095, 0.12], [-0.225, -0.145, 0.20], [-0.202, -0.149, 0.225]], [0.06, 0.055, 0.05, 0.043, 0.039, 0.035, 0.033, 0.028, 0.015, 0.001], ceramic);
  const thumbNail = mesh(hand, sphere(1, 16, 8), nailMaterial, -0.237, -0.109, 0.173); thumbNail.scale.set(0.018, 0.005, 0.024); thumbNail.rotation.set(0.45, -0.45, 0);
  const wristCuff = mesh(hand, cylinder(0.086, 0.086, 0.022, 32), chrome, 0, 0, -0.38); wristCuff.rotation.x = Math.PI / 2; wristCuff.scale.z = 0.6;
  beam(reflexStation, [0, 0.95, -0.47], [0, 1.23, -0.47], 0.023, chrome);
  label(reflexStation, 'The click reflex', 0.83, 0.69, 0.79);
  screen(reflexStation, 'One click later', 'An exhibit in three very familiar movements.', 'reflex', 0, 2.12, -0.67);
  batch(hand); batch(reflexStation);

  const loopStation = exhibit('museum_repeat_reward_display', 306, -2.65);
  plinth(loopStation, 1.36, 1.1, 0.88);
  const loop = ring(loopStation, 0.43, 0.07, chrome, 1.46); loop.rotation.x = 0;
  for (let i = 0; i < 6; i++) {
    const angle = i / 6 * Math.PI * 2;
    mesh(loopStation, sphere(0.09), i % 2 ? pink : cyan, Math.sin(angle) * 0.43, 1.46 + Math.cos(angle) * 0.43, 0);
  }
  mesh(loopStation, cylinder(0.04, 0.05, 0.21), chrome, 0, 0.99, 0);
  label(loopStation, 'Again, for science', 0.84, 0.6, 0.88);
  batch(loopStation);

  const adStation = exhibit('museum_intermission_screen', 544, 2.8);
  screen(adStation, 'One more thing', 'Please remain comfortably curious.', 'intermission', 0, 1.72, 0);
  ring(adStation, 0.62, 0.022, pink, 0.08);
  batch(adStation);
  const sculptureSlots = Array.from({ length: lite ? 3 : 5 }, (_, index) => {
    const holder = new THREE.Group();
    holder.name = `museum_collection_${index}`;
    const furniture = new THREE.Group();
    holder.add(furniture);
    const stand = new THREE.Group();
    holder.add(stand);
    // Existing sculptures include a pedestal; a low island and halo frame each display.
    mesh(furniture, box(1.5, 0.07, 1.3, 0.03), materials.stone, 0, 0.035, 0);
    ring(furniture, 0.68, 0.014, index % 2 ? cyan : pink, 0.09);
    if (index % 2 === 0) caseFrame(furniture, 1.27, 1.05, 0.81, 2.02);
    else {
      const halo = ring(furniture, 0.72, 0.018, cyan, 1.35);
      halo.rotation.set(Math.PI / 2.7, 0, 0);
      halo.position.z = -0.36;
    }
    for (const x of [-0.87, 0.87]) stanchion(furniture, x, 0.82);
    rope(furniture, [-0.87, 0.71, 0.82], [0.87, 0.71, 0.82]);
    label(furniture, ['The Knight', 'The Queen', 'The Rook'][index % 3], 0.86, 0.59);
    batch(furniture);
    group.add(holder);
    return { holder, stand, index: null };
  });

  const grass = new THREE.Group();
  grass.name = 'museum_grass_under_glass';
  plinth(grass, 0.88, 0.86, 0.85);
  mesh(grass, cylinder(0.28, 0.3, 0.06, 40), enamel, 0, 0.91, 0);
  mesh(grass, cylinder(0.26, 0.27, 0.018, 40), chrome, 0, 0.95, 0);
  ring(grass, 0.3, 0.012, cyan, 0.95);
  const grassBody = new THREE.Group();
  grass.add(grassBody);
  for (let i = 0; i < 46; i++) {
    const angle = i * 2.39996, radius = 0.2 * Math.sqrt(i / 46);
    const x = Math.cos(angle) * radius, z = Math.sin(angle) * radius;
    const height = 0.17 + (Math.sin(i * 4.7) + 1) * 0.11;
    const blade = mesh(grassBody, geometry(`blade:${i % 7}`, () => {
      const shape = new THREE.BufferGeometry();
      const bend = 0.03 + i % 7 * 0.008;
      shape.setAttribute('position', new THREE.Float32BufferAttribute([-0.012, 0, 0, 0.012, 0, 0, bend, height, -0.02], 3));
      shape.computeVertexNormals();
      shape.setAttribute('uv', new THREE.Float32BufferAttribute([0, 0, 1, 0, 0.5, 1], 2));
      return shape;
    }), leafMaterial, x, 0.96, z);
    blade.rotation.y = angle;
  }
  leafMaterial.side = THREE.DoubleSide;
  batch(grassBody);
  const dome = lathe([[0.28, 0], [0.28, 0.38], [0.27, 0.48], [0.23, 0.57], [0.16, 0.63], [0.08, 0.665], [0, 0.68]], lite ? 20 : 40);
  mesh(grass, dome, materials.glass, 0, 0.95, 0);
  mesh(grass, sphere(0.027), chrome, 0, 1.65, 0);
  label(grass, 'Touch grass', 0.81, 0.46, 0.4);
  batch(grass);
  group.add(grass);

  const wake = new THREE.Group();
  wake.name = 'museum_wake_up_vitrine';
  plinth(wake, 1.5, 1.12, 0.9);
  caseFrame(wake, 1.34, 0.99, 0.95, 1.94);
  mesh(wake, box(0.97, 0.09, 0.58, 0.035), feltMaterial, 0, 1.02, 0);
  const realButton = mesh(wake, box(0.72, 0.12, 0.32, 0.045), buttonMaterial, 0, 1.14, 0.03);
  realButton.name = 'wake_up_specimen_button';
  realButton.userData.interactive = true;
  interactive.push({ mesh: realButton, onPress: onWake });
  label(wake, 'Wake Up', 1.245, 0.07, 0.53);
  label(wake, 'Specimen: unpressed', 0.88, 0.61, 0.72);
  batch(wake);
  group.add(wake);

  function positionAt(object, z, side = 0, offset = 0, height = 0) {
    const point = trackPoint(z);
    const yaw = trackYaw(z);
    object.position.set(point.x + Math.cos(yaw) * side, point.y + height, point.z - Math.sin(yaw) * side);
    object.rotation.y = yaw + offset;
  }
  positionAt(grass, 436, 2.65, -Math.PI / 2);
  positionAt(wake, 470, 2.8, -Math.PI / 2);
  const loader = new GLTFLoader();
  loader.setMeshoptDecoder(MeshoptDecoder);
  const failures = [];
  const prototypes = [];
  const ready = Promise.all(['knight', 'queen', 'rook'].map(async name => {
    try {
      const loaded = await loader.loadAsync(new URL(`${name}.glb`, COLLECTION_ROOT).href);
      const model = loaded.scene;
      model.traverse(node => {
        if (!node.isMesh) return;
        importedGeometries.add(node.geometry);
        const old = Array.isArray(node.material) ? node.material : [node.material];
        for (const surface of old) {
          importedMaterials.add(surface);
          for (const value of Object.values(surface)) if (value?.isTexture) importedTextures.add(value);
        }
        const pedestal = node.name === 'casino_pedestal' || node.parent?.name === 'casino_pedestal';
        if (pedestal) {
          const replacements = old.map(surface => {
            if (/brass/i.test(surface.name)) return chrome;
            if (/shadow/i.test(surface.name)) return materials.iron;
            return enamel;
          });
          node.material = Array.isArray(node.material) ? replacements : replacements[0];
          if (!node.geometry.attributes.uv) {
            const positions = node.geometry.attributes.position;
            const uv = [];
            for (let i = 0; i < positions.count; i++) {
              uv.push(Math.atan2(positions.getX(i), positions.getZ(i)) / (Math.PI * 2) + 0.5, positions.getY(i));
            }
            node.geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
          }
        }
        node.castShadow = node.receiveShadow = true;
      });
      const bounds = new THREE.Box3().setFromObject(model);
      const height = bounds.max.y - bounds.min.y;
      model.scale.setScalar(1.65 / height);
      model.position.y = 0.07 - bounds.min.y * model.scale.y;
      if (disposed) return null;
      return model;
    } catch (error) { failures.push({ name, message: String(error.message || error) }); return null; }
  })).then(models => {
    prototypes.push(...models);
    if (disposed) { releaseImported(); return { failures }; }
    sculptureSlots.forEach((slot, index) => {
      const model = models[index % models.length];
      if (model) slot.stand.add(model.clone(true));
    });
    return { failures };
  });
  function releaseImported() {
    importedGeometries.forEach(shape => shape.dispose());
    importedMaterials.forEach(surface => surface.dispose());
    importedTextures.forEach(texture => texture.dispose());
    importedGeometries.clear(); importedMaterials.clear(); importedTextures.clear();
  }
  function update(z) {
    const first = Math.floor(z / 18);
    sculptureSlots.forEach((slot, index) => {
      const count = sculptureSlots.length;
      const distanceIndex = first + (index - first % count + count) % count;
      const at = distanceIndex * 18 + 9;
      const style = trackStyle(at);
      const reserved = [49, 225, 306, 436, 470, 544].some(distance => Math.abs(at - distance) < 8);
      slot.holder.visible = !reserved && style.id !== 'connector' && style.id !== 'descent' && at < 550 && at - z < (lite ? 38 : 66);
      if (slot.index === distanceIndex) return;
      slot.index = distanceIndex;
      const side = distanceIndex % 2 ? -2.75 : 2.75;
      positionAt(slot.holder, at, side, side > 0 ? -Math.PI / 2 : Math.PI / 2);
    });
    grass.visible = z > 396 && z < 443;
    wake.visible = z > 430 && z < 478;
    fixedExhibits.forEach(({ station, at }) => { station.visible = at - z > -9 && at - z < (lite ? 38 : 57); });
  }
  update(0);
  return {
    group, cart, lever, pumpGrip, ready, update, interactive, pumpTargets: [lever],
    pump(angle) {
      lever.rotation.x = angle;
      kineticUpdates.forEach(updateMechanism => updateMechanism(angle));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      geometries.forEach(shape => shape.dispose());
      ownedMaterials.forEach(surface => surface.dispose());
      releaseImported();
      group.removeFromParent(); cart.removeFromParent();
      interactive.length = 0;
    },
  };
}
