import { mergeGeometries } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { trackPoint, trackYaw } from './track.js';

const BAY_LENGTH = 12;
const HALF_WIDTH = 4.6;
const SPRING = 3.65;
const VAULT_RISE = 2.9;

/** Recycled, solid museum architecture. All materials and textures belong to the caller. */
export function createMuseum(THREE, materials, { lite = false } = {}) {
  const group = new THREE.Group();
  group.name = 'Museum vaulted galleries';
  const ownedGeometry = new Set();
  const temporaryGeometry = new Set();
  const batches = new Map();
  const matrix = new THREE.Matrix4();
  const rotation = new THREE.Quaternion();
  const unitBox = new THREE.BoxGeometry(1, 1, 1);
  const unitCylinder = new THREE.CylinderGeometry(1, 1, 1, lite ? 12 : 20);
  const unitSphere = new THREE.SphereGeometry(1, lite ? 12 : 20, lite ? 8 : 12);
  temporaryGeometry.add(unitBox);
  temporaryGeometry.add(unitCylinder);
  temporaryGeometry.add(unitSphere);

  function meterUV(geometry, density = 1) {
    const positions = geometry.attributes.position;
    const normals = geometry.attributes.normal;
    const uv = geometry.attributes.uv;
    for (let i = 0; i < positions.count; i++) {
      const x = Math.abs(normals.getX(i));
      const y = Math.abs(normals.getY(i));
      const z = Math.abs(normals.getZ(i));
      if (y >= x && y >= z) uv.setXY(i, positions.getX(i) * density, positions.getZ(i) * density);
      else if (x >= z) uv.setXY(i, positions.getZ(i) * density, positions.getY(i) * density);
      else uv.setXY(i, positions.getX(i) * density, positions.getY(i) * density);
    }
    return geometry;
  }

  function add(geometry, material, x, y, z, sx = 1, sy = 1, sz = 1, rx = 0, ry = 0, rz = 0, uv = true) {
    const name = materials[material] ? material : 'stone';
    rotation.setFromEuler(new THREE.Euler(rx, ry, rz));
    matrix.compose(new THREE.Vector3(x, y, z), rotation, new THREE.Vector3(sx, sy, sz));
    let copy = geometry.clone();
    copy.applyMatrix4(matrix);
    if (uv) meterUV(copy, ['stone', 'plaster', 'plasterBlue', 'floor'].includes(name) ? 0.5 : 1);
    if (copy.index) {
      const unindexed = copy.toNonIndexed();
      copy.dispose();
      copy = unindexed;
    }
    if (!batches.has(name)) batches.set(name, []);
    batches.get(name).push(copy);
  }
  const box = (material, x, y, z, width, height, depth, ry = 0) => add(unitBox, material, x, y, z, width, height, depth, 0, ry);
  const cylinder = (material, x, y, z, radius, height, ry = 0, rz = 0) => add(unitCylinder, material, x, y, z, radius, height, radius, 0, ry, rz);
  const sphere = (material, x, y, z, radius) => add(unitSphere, material, x, y, z, radius, radius, radius);

  function archGeometry(radiusX, radiusY, thickness, depth, segments = 36) {
    const shape = new THREE.Shape();
    for (let i = 0; i <= segments; i++) {
      const angle = i / segments * Math.PI;
      const x = Math.cos(angle) * (radiusX + thickness);
      const y = Math.sin(angle) * (radiusY + thickness);
      if (i === 0) shape.moveTo(x, y); else shape.lineTo(x, y);
    }
    for (let i = segments; i >= 0; i--) {
      const angle = i / segments * Math.PI;
      shape.lineTo(Math.cos(angle) * radiusX, Math.sin(angle) * radiusY);
    }
    shape.closePath();
    const geometry = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: false, steps: 1, curveSegments: segments });
    geometry.translate(0, 0, -depth / 2);
    temporaryGeometry.add(geometry);
    return geometry;
  }

  function vaultGeometry() {
    const positions = [], normals = [], uv = [], indices = [];
    const segments = lite ? 28 : 48;
    for (let row = 0; row < 2; row++) for (let i = 0; i <= segments; i++) {
      const angle = i / segments * Math.PI;
      const x = Math.cos(angle), y = Math.sin(angle);
      positions.push(x * HALF_WIDTH, SPRING + y * VAULT_RISE, (row - 0.5) * BAY_LENGTH);
      const normal = new THREE.Vector3(-x / HALF_WIDTH, -y / VAULT_RISE, 0).normalize();
      normals.push(normal.x, normal.y, 0);
      uv.push(i / segments * 12, row * BAY_LENGTH);
    }
    for (let i = 0; i < segments; i++) {
      const a = i, b = i + 1, c = i + segments + 1, d = c + 1;
      indices.push(a, c, b, b, c, d);
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
    geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    geometry.setIndex(indices);
    temporaryGeometry.add(geometry);
    return geometry;
  }

  // Broad surfaces have world-sized UVs. The recesses add real depth behind the display route.
  box('floor', 0, -0.12, 0, 12.2, 0.24, BAY_LENGTH + 0.14);
  box('stone', 0, -0.025, 0, 1.55, 0.035, BAY_LENGTH + 0.16);
  box('woodDark', -HALF_WIDTH, 0.08, 0, 0.22, 0.16, BAY_LENGTH + 0.14);
  box('woodDark', HALF_WIDTH, 0.08, 0, 0.22, 0.16, BAY_LENGTH + 0.14);
  add(vaultGeometry(), 'brick', 0, 0, 0, 1, 1, 1, 0, 0, 0, false);

  const vaultRib = archGeometry(4.35, 2.73, 0.19, 0.38);
  const innerRib = archGeometry(4.31, 2.68, 0.045, 0.48);
  const alcoveArch = archGeometry(2.55, 1.02, 0.24, 0.38, 28);
  const alcoveBead = archGeometry(2.49, 0.97, 0.055, 0.48, 28);
  const spandrelShape = new THREE.Shape();
  spandrelShape.moveTo(-2.8, 4.0);
  spandrelShape.lineTo(2.8, 4.0);
  spandrelShape.lineTo(2.8, 2.65);
  for (let i = 0; i <= 32; i++) {
    const angle = i / 32 * Math.PI;
    spandrelShape.lineTo(Math.cos(angle) * 2.55, 2.65 + Math.sin(angle) * 1.02);
  }
  spandrelShape.lineTo(-2.8, 2.65);
  spandrelShape.closePath();
  const spandrel = new THREE.ExtrudeGeometry(spandrelShape, { depth: 0.32, bevelEnabled: false });
  spandrel.translate(0, 0, -0.16);
  temporaryGeometry.add(spandrel);
  for (const z of [-6, 0]) {
    add(vaultRib, 'stone', 0, SPRING, z);
    add(innerRib, 'plaster', 0, SPRING, z);
  }
  // Iron tie rods and hangers sit below the brickwork, where their supports remain readable.
  for (const z of [-5.5, 0.5]) {
    cylinder('iron', 0, 5.05, z, 0.025, 8.2, 0, Math.PI / 2);
    cylinder('iron', 0, 5.7, z, 0.02, 1.28);
    sphere('brass', 0, 5.05, z, 0.075);
  }

  for (const side of [-1, 1]) {
    const x = side * HALF_WIDTH;
    // Each twelve-metre gallery has a five-metre-wide recessed exhibition alcove.
    for (const z of [-4.6, 4.6]) {
      box('plasterBlue', x + side * 0.12, 2.43, z, 0.3, 2.42, 2.8);
      box('wood', x - side * 0.04, 0.72, z, 0.22, 1.34, 2.8);
      for (let panel = 0; panel < 3; panel++) {
        const pz = z + (panel - 1) * 0.86;
        box('woodDark', x - side * 0.17, 0.72, pz, 0.035, 0.92, 0.69);
        box('wood', x - side * 0.192, 0.72, pz, 0.028, 0.82, 0.59);
        box('brass', x - side * 0.21, 1.16, pz, 0.018, 0.014, 0.63);
      }
    }
    box('plasterBlue', x + side * 0.14, 4.17, 0, 0.32, 1.08, BAY_LENGTH + 0.05);
    box('woodDark', x - side * 0.1, 1.38, -4.6, 0.3, 0.12, 2.8);
    box('woodDark', x - side * 0.1, 1.38, 4.6, 0.3, 0.12, 2.8);
    box('brass', x - side * 0.27, 1.42, -4.6, 0.018, 0.018, 2.8);
    box('brass', x - side * 0.27, 1.42, 4.6, 0.018, 0.018, 2.8);
    box('stone', x - side * 0.06, 3.62, 0, 0.38, 0.16, BAY_LENGTH + 0.03);
    box('plaster', x - side * 0.12, 3.76, 0, 0.48, 0.1, BAY_LENGTH + 0.03);
    box('stone', x - side * 0.15, 3.87, 0, 0.54, 0.1, BAY_LENGTH + 0.03);

    // Recess backing and returns are substantial walls, not a painted opening.
    box(side < 0 ? 'velvet' : 'plaster', side * 5.94, 1.95, 0, 0.18, 3.9, 5.6);
    box('woodDark', side * 5.83, 0.15, 0, 0.12, 0.3, 5.6);
    for (const z of [-2.85, 2.85]) box('plaster', side * 5.23, 1.92, z, 1.55, 3.84, 0.22);
    box('dark', side * 5.2, 3.97, 0, 1.6, 0.18, 5.6);
    add(alcoveArch, 'stone', x - side * 0.035, 2.65, 0, 1, 1, 1, 0, Math.PI / 2);
    add(alcoveBead, 'plaster', x - side * 0.065, 2.65, 0, 1, 1, 1, 0, Math.PI / 2);
    add(spandrel, 'plasterBlue', x + side * 0.13, 0, 0, 1, 1, 1, 0, Math.PI / 2);
    for (const z of [-2.68, 2.68]) {
      box('stone', x - side * 0.025, 1.32, z, 0.45, 2.64, 0.36);
      box('stone', x - side * 0.06, 0.14, z, 0.62, 0.28, 0.56);
      box('plaster', x - side * 0.07, 2.57, z, 0.58, 0.18, 0.52);
    }

    for (const z of [-6, 0]) {
      const pierX = side * 4.25;
      // Main piers line up with each vault rib; the centre pier separates twin exhibit bays.
      if (z === 0) continue;
      box('stone', pierX, 1.74, z, 0.58, 3.48, 0.68);
      box('stone', pierX, 0.12, z, 0.9, 0.24, 0.94);
      box('plaster', pierX, 0.29, z, 0.77, 0.1, 0.82);
      box('stone', pierX, 3.43, z, 0.8, 0.22, 0.88);
      box('plaster', pierX, 3.6, z, 0.93, 0.12, 1.0);
      // Small carved flutes and rosettes give the stone a readable secondary scale.
      for (const offset of [-0.18, 0, 0.18]) box('plaster', pierX - side * 0.3, 1.89, z + offset, 0.035, 2.66, 0.055);
      for (const offset of [-0.22, 0.22]) sphere('plaster', pierX - side * 0.34, 3.38, z + offset, 0.095);
    }

    // Warm wall sconces with brackets, opaque brass cups and frosted glass shades.
    for (const z of [-4.1, 4.1]) {
      box('brass', x - side * 0.21, 2.62, z, 0.09, 0.32, 0.17);
      cylinder('brass', x - side * 0.42, 2.53, z, 0.032, 0.43, 0, Math.PI / 2);
      cylinder('brass', x - side * 0.62, 2.67, z, 0.035, 0.31);
      cylinder('brass', x - side * 0.62, 2.77, z, 0.16, 0.065);
      const shade = new THREE.CylinderGeometry(0.21, 0.12, 0.24, lite ? 12 : 24);
      temporaryGeometry.add(shade);
      add(shade, 'lamp', x - side * 0.62, 2.93, z);
      cylinder('brass', x - side * 0.62, 3.065, z, 0.215, 0.025);
    }
  }

  // Cabinet in the right-hand recess, complete with inset drawer fronts and brass pulls.
  box('woodDark', 5.43, 0.64, 0, 0.82, 1.12, 3.1);
  box('wood', 5.38, 1.24, 0, 1.03, 0.14, 3.35);
  box('woodDark', 5.42, 0.1, 0, 0.96, 0.2, 3.24);
  for (let row = 0; row < 3; row++) for (let col = 0; col < 5; col++) {
    const y = 0.35 + row * 0.3, z = (col - 2) * 0.59;
    box('wood', 4.995, y, z, 0.045, 0.25, 0.54);
    box('brass', 4.963, y + 0.055, z, 0.02, 0.047, 0.14);
    cylinder('brass', 4.93, y - 0.035, z, 0.022, 0.11, 0, Math.PI / 2);
  }
  // The left recess provides a bench and its carved feet, useful human-scale cues.
  box('velvet', -5.22, 0.53, 0, 0.67, 0.15, 2.65);
  box('wood', -5.22, 0.42, 0, 0.73, 0.13, 2.78);
  box('woodDark', -5.56, 0.89, 0, 0.085, 0.76, 2.74);
  box('wood', -5.55, 1.29, 0, 0.15, 0.13, 2.86);
  for (const z of [-1.14, 1.14]) for (const x of [-5.46, -4.99]) {
    cylinder('woodDark', x, 0.23, z, 0.065, 0.43);
    sphere('wood', x, 0.11, z, 0.08);
  }
  for (const z of [-1.36, 1.36]) {
    box('wood', -5.21, 0.83, z, 0.77, 0.1, 0.12);
    cylinder('woodDark', -4.94, 0.65, z, 0.05, 0.33);
  }

  // A reading stand brings a human-scale object into the room, clear of the cart route.
  box('stone', -3.32, 0.08, -3.75, 0.66, 0.16, 0.65);
  cylinder('brass', -3.32, 0.57, -3.75, 0.07, 0.92);
  cylinder('brass', -3.32, 0.2, -3.75, 0.17, 0.09);
  add(unitBox, 'wood', -3.32, 1.08, -3.75, 0.96, 0.09, 0.68, 0.32);
  add(unitBox, 'paper', -3.52, 1.147, -3.75, 0.39, 0.035, 0.53, 0.32, 0, -0.06);
  add(unitBox, 'paper', -3.12, 1.147, -3.75, 0.39, 0.035, 0.53, 0.32, 0, 0.06);
  add(unitBox, 'brass', -3.32, 0.99, -3.43, 0.99, 0.045, 0.04, 0.32);

  function flushBatches(label) {
    const result = new THREE.Group();
    for (const [name, geometryParts] of batches) {
      const geometry = mergeGeometries(geometryParts, false);
      for (const part of geometryParts) part.dispose();
      if (!geometry) throw new Error(`Museum material batch failed: ${name}`);
      geometry.computeBoundingSphere();
      ownedGeometry.add(geometry);
      const mesh = new THREE.Mesh(geometry, materials[name]);
      mesh.name = `${label} ${name}`;
      mesh.castShadow = name !== 'lamp';
      mesh.receiveShadow = true;
      result.add(mesh);
    }
    batches.clear();
    return result;
  }
  const prototype = flushBatches('Gallery');

  // Cross-gallery portals make a sequence of rooms rather than an uninterrupted nave.
  const portalShape = new THREE.Shape();
  portalShape.moveTo(-HALF_WIDTH, 0);
  portalShape.lineTo(-1.85, 0);
  for (let i = 32; i >= 0; i--) {
    const angle = i / 32 * Math.PI;
    portalShape.lineTo(Math.cos(angle) * 1.85, 2.45 + Math.sin(angle) * 1.6);
  }
  portalShape.lineTo(1.85, 0);
  portalShape.lineTo(HALF_WIDTH, 0);
  for (let i = 0; i <= 48; i++) {
    const angle = i / 48 * Math.PI;
    portalShape.lineTo(Math.cos(angle) * HALF_WIDTH, SPRING + Math.sin(angle) * VAULT_RISE);
  }
  portalShape.closePath();
  const upperOpening = new THREE.Path();
  upperOpening.moveTo(-1.9, 4.43);
  upperOpening.lineTo(1.9, 4.43);
  for (let i = 0; i <= 32; i++) {
    const angle = i / 32 * Math.PI;
    upperOpening.lineTo(Math.cos(angle) * 1.9, 5.2 + Math.sin(angle) * 1.0);
  }
  upperOpening.closePath();
  portalShape.holes.push(upperOpening);
  const portalWall = new THREE.ExtrudeGeometry(portalShape, { depth: 0.42, bevelEnabled: false });
  portalWall.translate(0, 0, -0.21);
  temporaryGeometry.add(portalWall);
  add(portalWall, 'plasterBlue', 0, 0, 0);
  add(archGeometry(1.85, 1.6, 0.24, 0.58), 'stone', 0, 2.45, 0);
  add(archGeometry(1.81, 1.55, 0.045, 0.65), 'plaster', 0, 2.45, 0);
  add(archGeometry(1.9, 1.0, 0.13, 0.55), 'stone', 0, 5.2, 0);
  for (const side of [-1, 1]) {
    box('stone', side * 1.98, 1.22, 0, 0.26, 2.44, 0.58);
    box('stone', side * 1.99, 0.14, 0, 0.43, 0.28, 0.7);
    box('plaster', side * 1.98, 2.42, 0, 0.4, 0.16, 0.68);
    box('stone', side * 1.98, 4.84, 0, 0.17, 0.82, 0.55);
    // Small projecting corbels visibly support the balcony floor.
    box('stone', side * 2.22, 4.11, 0.34, 0.24, 0.38, 0.54);
    box('plaster', side * 2.22, 4.24, 0.48, 0.35, 0.18, 0.76);
  }
  box('woodDark', 0, 4.36, 0.18, 4.75, 0.16, 1.15);
  box('stone', 0, 4.25, 0.45, 4.84, 0.13, 0.73);
  box('wood', 0, 5.18, 0.65, 4.71, 0.11, 0.13);
  box('woodDark', 0, 4.49, 0.65, 4.67, 0.09, 0.1);
  for (let x = -2.22; x <= 2.22; x += 0.27) {
    cylinder('iron', x, 4.83, 0.65, 0.019, 0.67);
    sphere('brass', x, 4.98, 0.65, 0.035);
  }
  const portalPrototype = flushBatches('Cross gallery');
  for (const geometry of temporaryGeometry) geometry.dispose();

  const railMaterial = materials.iron;
  const bays = Array.from({ length: lite ? 5 : 7 }, () => {
    const bay = prototype.clone();
    const rails = new THREE.Group();
    rails.name = 'Continuous railway';
    const portal = portalPrototype.clone();
    portal.name = 'Cross gallery landing';
    group.add(bay, rails, portal);
    const light = new THREE.PointLight(0xffd7a0, lite ? 12 : 18, 17, 2);
    light.position.set(0, 3.7, 0);
    bay.add(light);
    return { bay, rails, portal, index: null, light };
  });

  function rebuildRails(entry, centerZ) {
    for (const child of [...entry.rails.children]) {
      child.geometry.dispose();
      entry.rails.remove(child);
    }
    for (const side of [-1, 1]) {
      const points = [];
      for (let i = 0; i <= 18; i++) {
        const distance = centerZ - BAY_LENGTH / 2 + i / 18 * BAY_LENGTH;
        const point = trackPoint(distance);
        const angle = trackYaw(distance);
        points.push(new THREE.Vector3(point.x + Math.cos(angle) * side * 0.58, 0.035, point.z - Math.sin(angle) * side * 0.58));
      }
      const curve = new THREE.CatmullRomCurve3(points);
      const rail = new THREE.Mesh(new THREE.TubeGeometry(curve, 36, 0.034, lite ? 5 : 8, false), railMaterial);
      rail.name = side < 0 ? 'Left running rail' : 'Right running rail';
      rail.receiveShadow = true;
      entry.rails.add(rail);
    }
  }

  function update(z) {
    const first = Math.floor(z / BAY_LENGTH) - 1;
    for (let i = 0; i < bays.length; i++) {
      const index = first + i;
      const entry = bays[((index % bays.length) + bays.length) % bays.length];
      if (entry.index !== index) {
        entry.index = index;
        const centerZ = index * BAY_LENGTH + BAY_LENGTH / 2;
        const point = trackPoint(centerZ);
        entry.bay.position.set(point.x, 0, point.z);
        entry.bay.rotation.y = trackYaw(centerZ);
        entry.bay.scale.x = index % 2 === 0 ? 1 : -1;
        entry.portal.position.copy(entry.bay.position);
        entry.portal.rotation.y = trackYaw(centerZ);
        rebuildRails(entry, centerZ);
      }
      entry.bay.visible = index >= -1 && index * BAY_LENGTH < 600;
      entry.rails.visible = entry.bay.visible;
      entry.portal.visible = entry.bay.visible && ((index % 3) + 3) % 3 === 1;
    }
  }
  update(0);
  return {
    group, update,
    /** DOM exhibit faces use this alongside the WebGL room walls to preserve occlusion. */
    occludes(point, origin) {
      for (const entry of bays) {
        if (!entry.portal.visible) continue;
        const position = entry.portal.position;
        const angle = entry.portal.rotation.y;
        const c = Math.cos(angle), s = Math.sin(angle);
        const originZ = s * (origin.x - position.x) + c * (origin.z - position.z);
        const targetZ = s * (point.x - position.x) + c * (point.z - position.z);
        const denominator = targetZ - originZ;
        if (Math.abs(denominator) < 0.00001) continue;
        const t = -originZ / denominator;
        if (t <= 0.001 || t >= 0.999) continue;
        const x = origin.x + (point.x - origin.x) * t - position.x;
        const z = origin.z + (point.z - origin.z) * t - position.z;
        const localX = c * x - s * z;
        const y = origin.y + (point.y - origin.y) * t;
        const doorTop = 2.45 + 1.6 * Math.sqrt(Math.max(0, 1 - (localX / 1.85) ** 2));
        const upperTop = 5.2 + Math.sqrt(Math.max(0, 1 - (localX / 1.9) ** 2));
        const throughDoor = Math.abs(localX) < 1.85 && y >= 0 && y < doorTop;
        const throughUpper = Math.abs(localX) < 1.9 && y > 4.43 && y < upperTop;
        if (!throughDoor && !throughUpper) return true;
      }
      return false;
    },
    dispose() {
      for (const geometry of ownedGeometry) geometry.dispose();
      for (const entry of bays) for (const child of entry.rails.children) child.geometry.dispose();
      group.removeFromParent();
    },
  };
}
