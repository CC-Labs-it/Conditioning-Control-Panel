import { GLTFLoader } from '../../../vendor/three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from '../../../vendor/three/addons/libs/meshopt_decoder.module.js';
import { mergeGeometries } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { trackPoint, trackYaw } from './track.js';

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
  const leather = material(materials.woodDark, { roughness: 0.9, metalness: 0 });
  leather.color.set('#4b2c1d');
  const leafMaterial = material(materials.plaster, { roughness: 0.95 });
  leafMaterial.color.set('#536b39');
  const feltMaterial = material(materials.velvet, { roughness: 1 });
  feltMaterial.color.set('#4b2630');
  const buttonMaterial = material(materials.paper, { roughness: 0.75 });
  buttonMaterial.color.set('#e5dbc0');

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

  // Real joinery and fittings, close enough to inspect from the seated viewpoint.
  const hull = new THREE.Group();
  hull.position.set(0, 0.16, -0.52);
  cart.add(hull);
  mesh(hull, box(1.45, 0.13, 0.15, 0.035), materials.wood, 0, -0.78, -0.98);
  mesh(hull, box(1.49, 0.025, 0.17, 0.012), materials.brass, 0, -0.697, -0.98);
  mesh(hull, box(1.39, 0.22, 0.05, 0.018), materials.woodDark, 0, -0.93, -0.94);
  for (const side of [-1, 1]) {
    mesh(hull, box(0.1, 0.15, 0.82, 0.035), materials.wood, side * 0.7, -0.83, -0.56);
    mesh(hull, box(0.13, 0.018, 0.86, 0.009), materials.brass, side * 0.7, -0.743, -0.56);
    for (const z of [-0.87, -0.55, -0.27]) {
      const bolt = mesh(hull, cylinder(0.015, 0.015, 0.012, 12), materials.brass, side * 0.7, -0.725, z);
      bolt.name = 'handcar_rail_bolt';
    }
  }
  mesh(hull, box(0.28, 0.06, 0.18, 0.015), materials.iron, 0, -0.94, -0.73);
  const axle = mesh(hull, cylinder(0.055, 0.055, 0.23), materials.brass, 0, -0.91, -0.73);
  axle.rotation.z = Math.PI / 2;
  for (const x of [-0.15, 0.15]) mesh(hull, sphere(0.024), materials.brass, x, -0.89, -0.73);
  const lever = new THREE.Group();
  lever.name = 'handcar_lever_pivot';
  lever.position.set(0, -0.75, -1.25);
  cart.add(lever);
  mesh(lever, cylinder(0.019, 0.028, 0.27), materials.iron, 0, 0.135, 0);
  mesh(lever, cylinder(0.036, 0.036, 0.055), materials.brass, 0, 0.24, 0);
  const crossbar = mesh(lever, cylinder(0.028, 0.028, 0.63), materials.woodDark, 0, 0.27, 0);
  crossbar.rotation.z = Math.PI / 2;
  for (const sign of [-1, 1]) {
    const grip = mesh(lever, cylinder(0.042, 0.037, 0.22), leather, sign * 0.205, 0.27, 0);
    grip.rotation.z = Math.PI / 2;
    const cap = mesh(lever, cylinder(0.045, 0.045, 0.025), materials.brass, sign * 0.325, 0.27, 0);
    cap.rotation.z = Math.PI / 2;
    for (const offset of [-0.06, -0.02, 0.02, 0.06]) {
      const stitch = mesh(lever, cylinder(0.043, 0.043, 0.002, 24), materials.woodDark, sign * 0.205 + offset, 0.27, 0);
      stitch.rotation.z = Math.PI / 2;
    }
  }
  batch(hull);
  batch(lever);

  function plinth(parent, width = 1.22, depth = 1.05, height = 0.82) {
    mesh(parent, box(width, 0.13, depth, 0.025), materials.woodDark, 0, 0.065, 0);
    mesh(parent, box(width - 0.13, height - 0.18, depth - 0.13, 0.025), materials.wood, 0, height / 2, 0);
    mesh(parent, box(width + 0.05, 0.09, depth + 0.05, 0.018), materials.woodDark, 0, height - 0.045, 0);
    mesh(parent, box(width + 0.08, 0.025, depth + 0.08, 0.01), materials.brass, 0, height + 0.01, 0);
    for (const x of [-width / 2 + 0.12, width / 2 - 0.12]) {
      mesh(parent, box(0.045, height - 0.22, 0.025, 0.008), materials.brass, x, height / 2, depth / 2 - 0.06);
    }
    // Inset panels establish scale and keep the cabinet legible under grazing light.
    mesh(parent, box(width - 0.35, height - 0.32, 0.028, 0.018), materials.woodDark, 0, height / 2, depth / 2 - 0.055);
    mesh(parent, box(width - 0.41, height - 0.39, 0.034, 0.015), materials.wood, 0, height / 2, depth / 2 - 0.034);
  }
  function caseFrame(parent, width, depth, bottom, top) {
    const tube = 0.022;
    for (const x of [-width / 2, width / 2]) for (const z of [-depth / 2, depth / 2]) {
      mesh(parent, box(tube, top - bottom, tube, 0.005), materials.brass, x, (top + bottom) / 2, z);
    }
    for (const y of [bottom, top]) {
      for (const x of [-width / 2, width / 2]) mesh(parent, box(tube, tube, depth, 0.005), materials.brass, x, y, 0);
      for (const z of [-depth / 2, depth / 2]) mesh(parent, box(width, tube, tube, 0.005), materials.brass, 0, y, z);
    }
    // Two side panes and a roof produce a legible case without stacking many transparent faces.
    const pane = box(0.008, top - bottom, depth - 0.035, 0.002);
    for (const side of [-1, 1]) mesh(parent, pane, materials.glass, side * width / 2, (top + bottom) / 2, 0);
    mesh(parent, box(width, 0.008, depth, 0.002), materials.glass, 0, top, 0);
  }
  function label(parent, text, y = 0.93, z = 0.56, width = 0.46) {
    const holder = mesh(parent, box(width, 0.13, 0.014, 0.007), materials.brass, 0, y, z);
    holder.rotation.x = -Math.PI / 8;
    const canvas = document.createElement('canvas');
    canvas.width = 512; canvas.height = 144;
    const paint = canvas.getContext('2d');
    paint.fillStyle = '#e6ddc8'; paint.fillRect(0, 0, canvas.width, canvas.height);
    paint.strokeStyle = '#9c8054'; paint.lineWidth = 5; paint.strokeRect(9, 9, 494, 126);
    paint.fillStyle = '#33291f'; paint.font = '36px Georgia'; paint.textAlign = 'center'; paint.textBaseline = 'middle';
    paint.fillText(text, 256, 73, 465);
    const map = new THREE.CanvasTexture(canvas);
    map.colorSpace = THREE.SRGBColorSpace;
    importedTextures.add(map);
    const surface = new THREE.MeshStandardMaterial({ map, roughness: 0.85 });
    ownedMaterials.add(surface);
    const face = mesh(parent, geometry(`label-plane:${width}`, () => new THREE.PlaneGeometry(width - 0.02, 0.11)), surface, 0, y, z + 0.009);
    face.rotation.x = holder.rotation.x;
  }
  function stanchion(parent, x, z) {
    const profile = [[0, 0], [0.115, 0], [0.135, 0.025], [0.135, 0.04], [0.07, 0.075], [0.025, 0.12], [0.022, 0.62], [0.044, 0.65], [0.044, 0.68], [0.025, 0.71], [0, 0.71]];
    mesh(parent, lathe(profile, lite ? 16 : 28), materials.brass, x, 0, z);
    mesh(parent, sphere(0.052), materials.brass, x, 0.745, z);
  }
  function rope(parent, a, b) {
    const curve = new THREE.CatmullRomCurve3([
      new THREE.Vector3(...a), new THREE.Vector3((a[0] + b[0]) / 2, Math.min(a[1], b[1]) - 0.14, (a[2] + b[2]) / 2), new THREE.Vector3(...b),
    ]);
    const tube = new THREE.TubeGeometry(curve, lite ? 8 : 14, 0.023, 6, false);
    geometries.add(tube);
    mesh(parent, tube, feltMaterial);
  }
  const sculptureSlots = Array.from({ length: lite ? 3 : 5 }, (_, index) => {
    const holder = new THREE.Group();
    holder.name = `museum_collection_${index}`;
    const furniture = new THREE.Group();
    holder.add(furniture);
    const stand = new THREE.Group();
    holder.add(stand);
    // Existing sculptures include a pedestal. A thin stone island grounds it in the gallery.
    mesh(furniture, box(1.5, 0.07, 1.3, 0.03), materials.stone, 0, 0.035, 0);
    caseFrame(furniture, 1.27, 1.05, 0.81, 2.02);
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
  mesh(grass, cylinder(0.28, 0.3, 0.06, 40), materials.woodDark, 0, 0.91, 0);
  mesh(grass, cylinder(0.26, 0.27, 0.018, 40), materials.brass, 0, 0.95, 0);
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
  mesh(grass, sphere(0.027), materials.brass, 0, 1.65, 0);
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

  function positionAt(object, z, side = 0, offset = 0) {
    const point = trackPoint(z);
    const yaw = trackYaw(z);
    object.position.set(point.x + Math.cos(yaw) * side, point.y, point.z - Math.sin(yaw) * side);
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
            if (/brass/i.test(surface.name)) return materials.brass;
            if (/shadow/i.test(surface.name)) return materials.iron;
            return materials.woodDark;
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
      slot.holder.visible = at < 550 && at - z < (lite ? 38 : 66);
      if (slot.index === distanceIndex) return;
      slot.index = distanceIndex;
      const side = distanceIndex % 2 ? -2.75 : 2.75;
      positionAt(slot.holder, at, side, side > 0 ? -Math.PI / 2 : Math.PI / 2);
    });
    grass.visible = z > 396 && z < 443;
    wake.visible = z > 430 && z < 478;
  }
  update(0);
  return {
    group, cart, lever, ready, update, interactive,
    pump(angle) { lever.rotation.x = angle; },
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
