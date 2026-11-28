import { GLTFLoader } from '../../../vendor/three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from '../../../vendor/three/addons/libs/meshopt_decoder.module.js';
import { mergeGeometries } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { trackPoint, trackYaw, trackStyle } from './track.js';

const COLLECTION_ROOT = new URL('../../../backroom/room/assets/customization/', import.meta.url);
export const SERVICE_END = Object.freeze({
  distance: 587,
  button: Object.freeze({ distance: 586.70, side: 0, elevation: 1.45, width: 0.62, height: 0.34 }),
});

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

  // A complete handcar tub sits beneath the original lever interaction coordinates.
  const hull = new THREE.Group(); hull.name = 'handcar_body'; cart.add(hull);
  const cartPaint = material(materials.lacquerBlack || enamel, { roughness: .43, metalness: .23 });
  cartPaint.color.set('#344b52');
  const cartInset = material(cartPaint, { roughness: .54 }); cartInset.color.set('#4b3949');
  const cartPadding = material(leather, { roughness: .77 }); cartPadding.color.set('#50616a');
  const cartDeck = material(rubber, { roughness: .89 }); cartDeck.color.set('#8b9395');

  // This opaque deck closes the entire footprint, including below the rider.
  mesh(hull, box(1.52, .12, 2.24, .045), cartPaint, 0, -1.22, -.56);
  mesh(hull, box(1.35, .035, 2.02, .025), chrome, 0, -1.145, -.55);
  mesh(hull, box(1.28, .027, 1.94, .025), cartDeck, 0, -1.115, -.55);
  for (const side of [-1, 1]) {
    mesh(hull, box(.51, .014, 1.30, .022), rubber, side * .34, -1.095, -.35);
    for (let i = 0; i < 17; i++) {
      mesh(hull, box(.46, .007, .017, .003), cartDeck, side * .34, -1.083, -.97 + i * .077);
    }
    mesh(hull, box(.024, .014, 1.75, .004), side < 0 ? cyan : pink, side * .624, -1.095, -.48);
  }
  mesh(hull, box(.32, .019, .39, .026), cartPaint, 0, -1.092, -.43);
  for (const x of [-.125, .125]) for (const z of [-.57, -.29]) fastener(hull, x, -1.078, z, .012, 'y');

  const sideShell = geometry('handcar:shaped-side-shell', () => {
    const outline = new THREE.Shape();
    outline.moveTo(-.52, -1.20); outline.lineTo(1.62, -1.20);
    outline.quadraticCurveTo(1.70, -1.20, 1.70, -1.10);
    outline.lineTo(1.70, -.75); outline.quadraticCurveTo(1.68, -.62, 1.49, -.61);
    outline.lineTo(-.35, -.65); outline.quadraticCurveTo(-.55, -.65, -.55, -.84);
    outline.lineTo(-.55, -1.15); outline.quadraticCurveTo(-.55, -1.20, -.52, -1.20);
    const result = new THREE.ExtrudeGeometry(outline, { depth: .058, bevelEnabled: true,
      bevelSize: .025, bevelThickness: .018, bevelSegments: lite ? 1 : 3, steps: 1, curveSegments: 8 });
    result.translate(0, 0, -.029); return result;
  });
  for (const side of [-1, 1]) {
    const cheek = mesh(hull, sideShell, cartPaint, side * .71, 0, 0); cheek.rotation.y = Math.PI / 2;
    mesh(hull, box(.036, .30, 1.72, .017), cartInset, side * .667, -.894, -.46);
    mesh(hull, box(.15, .095, 1.62, .035), cartPadding, side * .665, -.624, -.40);
    mesh(hull, box(.018, .012, 1.47, .004), chrome, side * .610, -.590, -.40);
    tube(hull, [[side * .65, -.672, -1.34], [side * .65, -.687, -.55], [side * .65, -.702, .29]], .006, side < 0 ? cyan : pink, 16);
    for (const z of [-1.28, -.62, .18]) {
      mesh(hull, box(.045, .36, .037, .009), chrome, side * .639, -.91, z);
      fastener(hull, side * .616, -.78, z, .011, 'x');
    }
    mesh(hull, box(.07, .047, .38, .018), chrome, side * .611, -.928, -.37);
    mesh(hull, box(.075, .035, .28, .014), rubber, side * .604, -.934, -.37);
    // Small running boards and flanged wheels establish a solid rail vehicle below the tub.
    mesh(hull, box(.18, .045, .54, .022), chrome, side * .79, -1.155, -.30);
    mesh(hull, box(.14, .012, .45, .012), rubber, side * .79, -1.124, -.30);
    for (const z of [-1.28, .27]) {
      const wheel = mesh(hull, cylinder(.145, .145, .10, 32), materials.iron, side * .58, -1.29, z);
      wheel.rotation.z = Math.PI / 2;
      const flange = mesh(hull, cylinder(.164, .164, .019, 32), chrome, side * .53, -1.29, z); flange.rotation.z = Math.PI / 2;
      fastener(hull, side * .641, -1.29, z, .028, 'x');
    }
  }
  // A padded rear bench and back rail complete the cockpit when the rider looks down.
  mesh(hull, box(1.27, .18, .44, .045), cartPadding, 0, -.902, .31);
  mesh(hull, box(1.28, .34, .095, .035), cartPadding, 0, -.67, .535);
  mesh(hull, box(1.34, .040, .13, .015), chrome, 0, -.464, .535);
  for (const x of [-.49, 0, .49]) {
    mesh(hull, box(.009, .012, .35, .003), cartInset, x, -.805, .31);
    mesh(hull, box(.009, .24, .011, .003), cartInset, x, -.67, .482);
  }
  mesh(hull, box(1.36, .47, .115, .035), cartPaint, 0, -.91, -1.61);
  mesh(hull, box(1.42, .105, .33, .04), cartPaint, 0, -.655, -1.52);
  mesh(hull, box(1.34, .018, .31, .008), chrome, 0, -.594, -1.52);
  mesh(hull, box(1.21, .012, .23, .009), cartPadding, 0, -.579, -1.52);
  mesh(hull, box(1.16, .010, .014, .003), cyan, 0, -.718, -1.338);
  mesh(hull, box(.30, .112, .020, .012), rubber, -.39, -.665, -1.339);
  mesh(hull, bezel(.32, .132, .012, .009, .018), chrome, -.39, -.665, -1.325);
  for (let i = 0; i < 5; i++) mesh(hull, box(.024, .021 + i * .010, .008, .003), i < 3 ? cyan : pink, -.49 + i * .048, -.667, -1.312);
  const dial = mesh(hull, cylinder(.069, .069, .025, 32), chrome, .43, -.659, -1.327); dial.rotation.x = Math.PI / 2;
  const dialFace = mesh(hull, cylinder(.057, .057, .004, 32), ivory, .43, -.659, -1.310); dialFace.rotation.x = Math.PI / 2;
  for (let i = 0; i < 9; i++) {
    const angle = -2.2 + i * .55;
    beam(hull, [.43 + Math.sin(angle) * .042, -.659 + Math.cos(angle) * .042, -1.305],
      [.43 + Math.sin(angle) * .050, -.659 + Math.cos(angle) * .050, -1.305], .0015, rubber);
  }
  const cartGauge = new THREE.Group(); cartGauge.position.set(.43, -.659, -1.300); hull.add(cartGauge);
  beam(cartGauge, [0, -.007, 0], [0, .043, 0], .0024, pink);
  mesh(cartGauge, sphere(.007, 10, 6), chrome);
  for (const x of [-.60, -.18, .18, .60]) fastener(hull, x, -.670, -1.319, .011);
  for (let i = 0; i < 7; i++) mesh(hull, box(.075, .008, .008, .002), rubber, .36, -.83 - i * .025, -1.545);

  // The pump is mounted to the deck through a cast gearbox and two axle bearings.
  mesh(hull, box(.42, .065, .42, .035), chrome, 0, -1.063, -1.23);
  mesh(hull, box(.32, .265, .29, .032), cartPaint, 0, -.914, -1.25);
  mesh(hull, box(.24, .18, .018, .018), cartInset, 0, -.922, -1.094);
  for (const x of [-.165, .165]) for (const z of [-1.38, -1.08]) fastener(hull, x, -1.017, z, .015, 'y');
  for (const x of [-.14, .14]) {
    mesh(hull, box(.055, .195, .13, .024), chrome, x, -.827, -1.25);
    const bearing = mesh(hull, cylinder(.064, .064, .067, 24), rubber, x, -.75, -1.25); bearing.rotation.z = Math.PI / 2;
    fastener(hull, x + Math.sign(x) * .040, -.75, -1.25, .029, 'x');
  }
  const axle = mesh(hull, cylinder(.037, .037, .37), chrome, 0, -.75, -1.25); axle.rotation.z = Math.PI / 2;
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
  beam(lever, [0, -.006, 0], [0, -.135, .035], .024, chrome);
  mesh(lever, sphere(.032), chrome, 0, -.135, .035);
  const pistonBase = new THREE.Vector3(0, -1.035, -.69);
  mesh(hull, box(.20, .08, .16, .024), cartPaint, 0, -1.065, -.69);
  for (const x of [-.075, .075]) {
    mesh(hull, box(.035, .115, .095, .015), chrome, x, -1.015, -.69);
    fastener(hull, x, -1.035, -.69, .018, 'x');
  }
  const pistonOuter = mesh(hull, cylinder(.043, .048, .22), chrome);
  const pistonCollar = mesh(hull, cylinder(.050, .050, .028), cartPaint);
  const pistonRod = mesh(hull, cylinder(.018, .018, 1, 16), chrome);
  const cartReturnSpring = spring(hull, 0, 0, 0, .032, .22, 7);
  for (const part of [pistonOuter, pistonCollar, pistonRod, cartReturnSpring]) part.userData.interactive = true;
  tube(hull, [[.06, -1.04, -.72], [.19, -1.06, -.72], [.24, -1.07, -.84], [.24, -1.07, -1.07], [.17, -1.02, -1.12]], .012, rubber, 24);
  const cartLinkPoint = new THREE.Vector3(), cartLinkAxis = new THREE.Vector3();
  const cartPumpAxis = new THREE.Vector3(1, 0, 0), cartCylinderAxis = new THREE.Vector3(0, 1, 0);
  function updateCartPump(angle) {
    cartLinkPoint.set(0, -.135, .035).applyAxisAngle(cartPumpAxis, angle).add(lever.position);
    cartLinkAxis.copy(cartLinkPoint).sub(pistonBase);
    const length = cartLinkAxis.length(); cartLinkAxis.normalize();
    for (const part of [pistonOuter, pistonCollar, pistonRod, cartReturnSpring]) part.quaternion.setFromUnitVectors(cartCylinderAxis, cartLinkAxis);
    pistonOuter.position.copy(pistonBase).addScaledVector(cartLinkAxis, .11);
    pistonCollar.position.copy(pistonBase).addScaledVector(cartLinkAxis, .208);
    const exposed = Math.max(.05, length - .22);
    pistonRod.scale.y = exposed;
    pistonRod.position.copy(pistonBase).addScaledVector(cartLinkAxis, .22 + exposed / 2);
    cartReturnSpring.position.copy(pistonBase).addScaledVector(cartLinkAxis, .224);
    cartReturnSpring.scale.y = Math.max(.10, length - .264) / .22;
    cartGauge.rotation.z = -.55 - angle * 1.6;
  }
  updateCartPump(0);
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

  const decorStops = [20, 68, 100, 196, 260, 284, 500];
  function castor(parent, x, z) {
    mesh(parent, box(0.08, 0.12, 0.11, 0.012), chrome, x, 0.17, z);
    const wheel = mesh(parent, cylinder(0.075, 0.075, 0.055, 20), rubber, x, 0.085, z);
    wheel.rotation.z = Math.PI / 2;
    fastener(parent, x + 0.03, 0.085, z, 0.019, 'x');
  }
  function grille(parent, x, y, z, width, height) {
    mesh(parent, box(width, height, 0.025, 0.01), rubber, x, y, z);
    for (let i = 0; i < 7; i++) mesh(parent, box(width - 0.035, 0.009, 0.018, 0.003), chrome, x, y - height * 0.4 + i * height * 0.8 / 6, z + 0.018);
  }
  function plaque(parent, text, width, height, x, y, z, background = '#e9e4d8', ink = '#292d32') {
    const canvas = document.createElement('canvas'); canvas.width = 512; canvas.height = Math.max(96, Math.round(512 * height / width));
    const context = canvas.getContext('2d');
    context.fillStyle = background; context.fillRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = ink; context.font = `600 ${Math.round(canvas.height * 0.49)}px Arial`;
    context.textAlign = 'center'; context.textBaseline = 'middle'; context.fillText(text, 256, canvas.height / 2, 470);
    const texture = new THREE.CanvasTexture(canvas); texture.colorSpace = THREE.SRGBColorSpace; importedTextures.add(texture);
    const surface = new THREE.MeshStandardMaterial({ map: texture, roughness: 0.72 }); ownedMaterials.add(surface);
    return mesh(parent, geometry(`flat-sign:${width}:${height}`, () => new THREE.PlaneGeometry(width, height)), surface, x, y, z);
  }

  // Standalone furniture appears at authored stops, rather than being repeated in every bay.
  const tickets = exhibit('museum_ticket_turnstile', 20, -3.25);
  for (const x of [-0.51, 0.51]) {
    mesh(tickets, box(0.37, 1.04, 0.60, 0.07), enamel, x, 0.56, 0);
    mesh(tickets, box(0.39, 0.08, 0.63, 0.035), chrome, x, 1.11, 0);
    mesh(tickets, box(0.38, 0.12, 0.62, 0.025), rubber, x, 0.12, 0);
    mesh(tickets, box(0.23, 0.018, 0.21, 0.015), rubber, x, 1.16, 0.08);
    mesh(tickets, box(0.14, 0.006, 0.035, 0.004), cyan, x, 1.172, 0.08);
    for (const z of [-0.21, 0.21]) fastener(tickets, x, 1.16, z, 0.018, 'y');
    grille(tickets, x, 0.42, 0.31, 0.23, 0.23);
  }
  const turnstileHub = mesh(tickets, cylinder(0.10, 0.10, 0.15, 28), chrome, -0.24, 0.85, 0); turnstileHub.rotation.z = Math.PI / 2;
  for (let i = 0; i < 3; i++) {
    const angle = i * Math.PI * 2 / 3;
    beam(tickets, [-0.19, 0.85, 0], [0.23, 0.85 + Math.cos(angle) * 0.30, Math.sin(angle) * 0.30], 0.026, chrome);
    beam(tickets, [0.23, 0.85 + Math.cos(angle) * 0.30, Math.sin(angle) * 0.30], [0.52, 0.85 + Math.cos(angle) * 0.30, Math.sin(angle) * 0.30], 0.036, rubber);
  }
  mesh(tickets, box(0.23, 0.04, 0.09, 0.008), ivory, -0.51, 1.175, -0.10);
  for (let i = 0; i < 5; i++) mesh(tickets, box(0.19, 0.008, 0.08, 0.002), materials.paper, -0.51, 1.203 + i * 0.009, -0.10);
  plaque(tickets, 'ADMIT ONE', 0.31, 0.08, -0.51, 0.89, 0.307);
  batch(tickets);

  const listening = exhibit('museum_headphones_listening_station', 68, 3.25);
  mesh(listening, cylinder(0.31, 0.38, 0.09, 32), enamel, 0, 0.06, 0);
  mesh(listening, cylinder(0.034, 0.055, 1.15), chrome, 0, 0.66, 0);
  mesh(listening, box(0.65, 0.15, 0.45, 0.035), enamel, 0, 1.13, 0.03);
  mesh(listening, box(0.60, 0.025, 0.39, 0.018), chrome, 0, 1.225, 0.03);
  for (const x of [-0.20, 0, 0.20]) {
    mesh(listening, cylinder(0.045, 0.045, 0.035, 20), rubber, x, 1.26, 0.10);
    mesh(listening, box(0.006, 0.005, 0.027, 0.001), ivory, x, 1.28, 0.11);
  }
  beam(listening, [0, 1.18, -0.12], [0, 1.95, -0.12], 0.025, chrome);
  beam(listening, [0, 1.93, -0.12], [0, 1.93, 0.04], 0.025, chrome);
  const headband = Array.from({ length: 19 }, (_, i) => { const angle = i / 18 * Math.PI; return [Math.cos(angle) * 0.24, 1.67 + Math.sin(angle) * 0.28, 0.07]; });
  tube(listening, headband, 0.033, leather, 32);
  tube(listening, headband.map(([x, y, z]) => [x, y + 0.027, z]), 0.009, chrome, 32);
  for (const side of [-1, 1]) {
    for (const [offset, radius, depth, surface] of [[0, 0.125, 0.07, enamel], [-side * 0.04, 0.10, 0.045, leather]]) {
      const cup = mesh(listening, cylinder(radius, radius, depth, 24), surface, side * 0.24 + offset, 1.63, 0.07); cup.rotation.z = Math.PI / 2;
    }
    beam(listening, [side * 0.24, 1.74, 0.06], [side * 0.24, 1.64, 0.06], 0.015, chrome);
  }
  tube(listening, [[0.26, 1.55, 0.07], [0.35, 1.34, 0.13], [0.35, 1.05, 0.15], [0.18, 0.93, 0.16], [0.16, 1.14, 0.17]], 0.009, rubber, 32);
  plaque(listening, 'LISTEN CLOSELY', 0.43, 0.085, 0, 1.13, 0.261);
  batch(listening);

  const clocks = exhibit('museum_clock_cluster', 100, -3.25);
  mesh(clocks, box(1.16, 0.10, 0.52, 0.035), enamel, 0, 0.07, 0);
  for (const x of [-0.43, 0.43]) beam(clocks, [x, 0.13, 0], [x, 2.27, 0], 0.028, chrome);
  mesh(clocks, box(1.18, 0.07, 0.08, 0.018), chrome, 0, 2.28, 0);
  for (const [x, y, radius, phase] of [[-0.29, 1.78, 0.27, 0.8], [0.34, 1.57, 0.22, 2.1], [-0.12, 1.02, 0.20, 4.4]]) {
    for (const [r, depth, z, surface] of [[radius + 0.035, 0.12, 0.02, enamel], [radius + 0.012, 0.024, 0.09, chrome], [radius, 0.008, 0.108, ivory]]) {
      const face = mesh(clocks, cylinder(r, r, depth, 40), surface, x, y, z); face.rotation.x = Math.PI / 2;
    }
    for (let tick = 0; tick < 12; tick++) {
      const angle = tick / 12 * Math.PI * 2;
      beam(clocks, [x + Math.sin(angle) * radius * 0.75, y + Math.cos(angle) * radius * 0.75, 0.117], [x + Math.sin(angle) * radius * 0.88, y + Math.cos(angle) * radius * 0.88, 0.117], 0.006, enamel);
    }
    for (const [angle, length] of [[phase, radius * 0.62], [phase * 3, radius * 0.40]]) beam(clocks, [x, y, 0.126], [x + Math.sin(angle) * length, y + Math.cos(angle) * length, 0.126], 0.009, enamel);
    mesh(clocks, sphere(0.025, 12, 8), chrome, x, y, 0.137);
  }
  plaque(clocks, 'TIME WELL SPENT', 0.73, 0.11, 0, 0.45, 0.08);
  batch(clocks);

  const lost = exhibit('museum_lost_and_found', 196, -3.25);
  mesh(lost, box(1.14, 1.38, 0.65, 0.055), materials.woodDark, 0, 0.76, 0);
  for (const x of [-0.46, 0.46]) for (const z of [-0.23, 0.23]) mesh(lost, cylinder(0.033, 0.04, 0.16, 12), chrome, x, 0.10, z);
  for (const x of [-0.27, 0.27]) for (const y of [0.48, 1.02]) {
    mesh(lost, box(0.47, 0.45, 0.035, 0.016), rubber, x, y, 0.34);
    mesh(lost, bezel(0.49, 0.47, 0.025, 0.04, 0.025), chrome, x, y, 0.37);
    for (let bar = 0; bar < 5; bar++) mesh(lost, box(0.008, 0.41, 0.012, 0.002), chrome, x - 0.18 + bar * 0.09, y, 0.40);
    for (const dy of [-0.12, 0.12]) mesh(lost, box(0.42, 0.008, 0.012, 0.002), chrome, x, y + dy, 0.405);
  }
  mesh(lost, lathe([[0, 0], [0.23, 0], [0.23, 0.025], [0.15, 0.035], [0.14, 0.13], [0.12, 0.17], [0, 0.18]], 28), feltMaterial, -0.25, 1.47, 0);
  for (let fold = 0; fold < 3; fold++) mesh(lost, box(0.38, 0.055, 0.30, 0.022), fold % 2 ? leather : materials.velvet, 0.25, 1.50 + fold * 0.055, 0);
  tube(lost, [[0.74, 0.10, 0.08], [0.74, 1.35, 0.08], [0.70, 1.45, 0.08], [0.61, 1.42, 0.08], [0.62, 1.34, 0.08]], 0.023, chrome, 28);
  const umbrella = mesh(lost, cylinder(0.035, 0.09, 0.81, 12), materials.velvet, 0.74, 0.60, 0.08);
  for (let rib = 0; rib < 6; rib++) { const angle = rib / 6 * Math.PI * 2; beam(lost, [0.74 + Math.cos(angle) * 0.03, 1.0, 0.08 + Math.sin(angle) * 0.03], [0.74 + Math.cos(angle) * 0.085, 0.20, 0.08 + Math.sin(angle) * 0.085], 0.003, chrome); }
  plaque(lost, 'LOST + FOUND', 0.78, 0.12, 0, 1.32, 0.36);
  batch(lost);

  const av = exhibit('museum_av_cart', 260, 3.25);
  for (const x of [-0.45, 0.45]) for (const z of [-0.27, 0.27]) {
    castor(av, x, z); beam(av, [x, 0.22, z], [x, 1.29, z], 0.022, chrome);
  }
  for (const y of [0.28, 0.67, 1.12]) mesh(av, box(1.02, 0.065, 0.68, 0.025), enamel, 0, y, 0);
  mesh(av, box(0.78, 0.20, 0.48, 0.025), ivory, 0, 0.43, 0);
  grille(av, 0, 0.43, 0.25, 0.62, 0.10);
  mesh(av, box(0.79, 0.18, 0.51, 0.025), enamel, 0, 0.79, 0);
  mesh(av, box(0.35, 0.035, 0.022, 0.004), rubber, -0.08, 0.81, 0.265);
  for (const x of [0.22, 0.30]) { const knob = mesh(av, cylinder(0.024, 0.024, 0.025, 16), chrome, x, 0.81, 0.27); knob.rotation.x = Math.PI / 2; }
  mesh(av, box(0.84, 0.64, 0.54, 0.06), ivory, 0, 1.47, 0);
  mesh(av, bezel(0.72, 0.49, 0.055, 0.06, 0.06), enamel, -0.035, 1.49, 0.295);
  mesh(av, box(0.60, 0.37, 0.025, 0.025), rubber, -0.035, 1.49, 0.30);
  const trace = Array.from({ length: 31 }, (_, i) => [-0.30 + i * 0.018, 1.49 + Math.sin(i * 0.6) * 0.08 + Math.sin(i * 1.7) * 0.02, 0.321]);
  tube(av, trace, 0.006, cyan, 36);
  for (const y of [1.28, 1.39]) { const dial = mesh(av, cylinder(0.025, 0.025, 0.026, 16), chrome, 0.345, y, 0.285); dial.rotation.x = Math.PI / 2; }
  tube(av, [[-0.25, 1.22, -0.29], [-0.49, 1.0, -0.35], [-0.49, 0.54, -0.35], [-0.10, 0.39, -0.34]], 0.012, rubber, 28);
  plaque(av, 'PLAYBACK UNIT 03', 0.49, 0.065, 0, 1.13, 0.354);
  batch(av);

  const rotary = exhibit('museum_rotary_call_station', 284, -3.25);
  mesh(rotary, box(0.70, 0.12, 0.61, 0.045), enamel, 0, 0.10, 0);
  mesh(rotary, box(0.45, 0.80, 0.40, 0.035), materials.woodDark, 0, 0.55, 0);
  mesh(rotary, box(0.78, 0.09, 0.65, 0.025), chrome, 0, 1.0, 0);
  mesh(rotary, box(0.56, 0.18, 0.44, 0.065), enamel, 0, 1.14, 0.04);
  mesh(rotary, cylinder(0.16, 0.17, 0.035, 40), chrome, 0, 1.247, 0.12);
  mesh(rotary, cylinder(0.072, 0.072, 0.041, 32), ivory, 0, 1.252, 0.12);
  for (let hole = 0; hole < 10; hole++) {
    const angle = hole / 10 * Math.PI * 2;
    mesh(rotary, cylinder(0.022, 0.022, 0.006, 16), rubber, Math.sin(angle) * 0.118, 1.268, 0.12 + Math.cos(angle) * 0.118);
  }
  tube(rotary, [[-0.25, 1.33, -0.12], [-0.18, 1.39, -0.12], [0, 1.42, -0.12], [0.18, 1.39, -0.12], [0.25, 1.33, -0.12]], 0.043, enamel, 28);
  for (const x of [-0.25, 0.25]) {
    mesh(rotary, cylinder(0.085, 0.075, 0.07, 28), enamel, x, 1.31, -0.12);
    mesh(rotary, cylinder(0.073, 0.073, 0.02, 28), rubber, x, 1.265, -0.12);
  }
  tube(rotary, [[-0.26, 1.30, -0.13], [-0.42, 1.17, -0.17], [-0.44, 0.94, -0.04], [-0.36, 0.86, 0.12], [-0.25, 1.10, 0.17]], 0.012, rubber, 32);
  grille(rotary, 0, 0.50, 0.209, 0.30, 0.29);
  plaque(rotary, 'OUTSIDE LINE', 0.43, 0.09, 0, 0.83, 0.215);
  batch(rotary);

  const reading = exhibit('museum_visitor_map_desk', 500, 3.25);
  for (const x of [-0.50, 0.50]) for (const z of [-0.28, 0.28]) beam(reading, [x, 0.03, z], [x, 0.94, z], 0.027, chrome);
  mesh(reading, box(1.18, 0.08, 0.73, 0.04), materials.wood, 0, 0.96, 0);
  mesh(reading, box(0.73, 0.012, 0.48, 0.01), materials.paper, -0.05, 1.01, 0);
  for (const x of [-0.25, 0, 0.25]) mesh(reading, box(0.003, 0.003, 0.43, 0.001), chrome, x, 1.018, 0);
  for (let road = 0; road < 4; road++) beam(reading, [-0.35, 1.02, -0.18 + road * 0.11], [0.25, 1.02, -0.12 + road * 0.09], 0.003, materials.plasterBlue);
  mesh(reading, cylinder(0.06, 0.05, 0.14, 20), enamel, 0.47, 1.065, -0.18);
  for (const dx of [-0.02, 0.015, 0.03]) beam(reading, [0.47 + dx, 1.07, -0.18], [0.48 + dx, 1.24, -0.17], 0.006, materials.wood);
  plaque(reading, 'YOU ARE HERE', 0.65, 0.10, 0, 0.89, 0.374);
  batch(reading);
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

  const service = new THREE.Group();
  service.name = 'museum_plain_service_ending';
  positionAt(service, SERVICE_END.distance);
  group.add(service);
  const serviceAd = new THREE.Group();
  serviceAd.name = 'museum_service_ad_control';
  service.add(serviceAd);
  mesh(serviceAd, box(0.84, 0.53, 0.07, 0.025), chrome, 0, 1.45, 0.16);
  mesh(serviceAd, box(0.74, 0.43, 0.025, 0.018), rubber, 0, 1.45, 0.21);
  const serviceButton = mesh(serviceAd, box(0.64, 0.36, 0.065, 0.035), buttonMaterial, 0, 1.45, 0.2675);
  serviceButton.name = 'museum_physical_ad_button';
  serviceButton.userData.interactive = true;
  plaque(serviceAd, 'AD', SERVICE_END.button.width, SERVICE_END.button.height, 0, 1.45, 0.301, '#ded9cd', '#292929');
  for (const x of [-0.36, 0.36]) for (const dy of [-0.20, 0.20]) fastener(serviceAd, x, 1.45 + dy, 0.201, 0.017);
  batch(serviceAd);

  const plant = new THREE.Group(); plant.position.set(-2.3, 0, 0.58); service.add(plant);
  mesh(plant, lathe([[0, 0], [0.19, 0], [0.20, 0.025], [0.25, 0.38], [0.255, 0.43], [0.23, 0.45], [0.205, 0.43], [0.18, 0.08], [0, 0.08]], 32), ceramic);
  mesh(plant, cylinder(0.21, 0.21, 0.025, 32), rubber, 0, 0.414, 0);
  mesh(plant, cylinder(0.23, 0.25, 0.04, 32), ceramic, 0, 0.022, 0);
  const plantLeaf = loft('service-plant-leaf', [[-0.19, 0.001, 0.001], [-0.12, 0.068, 0.009], [0, 0.10, 0.017], [0.13, 0.055, 0.010], [0.22, 0.001, 0.001]], 10);
  for (let branch = 0; branch < 7; branch++) {
    const angle = branch * 2.3999, reach = 0.18 + branch % 3 * 0.04, height = 0.85 + (branch % 4) * 0.17;
    const x = Math.cos(angle) * reach, z = Math.sin(angle) * reach;
    beam(plant, [0, 0.42, 0], [x, height, z], 0.012, leafMaterial);
    for (const sign of [-1, 1]) {
      const leaf = mesh(plant, plantLeaf, leafMaterial, x + Math.cos(angle) * 0.10 * sign, height - (sign + 1) * 0.13, z + Math.sin(angle) * 0.10 * sign);
      leaf.rotation.set(0.23 * sign, angle + Math.PI / 2, sign * 0.24);
    }
  }
  batch(plant);

  const cooler = new THREE.Group(); cooler.position.set(2.3, 0, 0.43); service.add(cooler);
  mesh(cooler, box(0.52, 1.02, 0.47, 0.055), ivory, 0, 0.55, 0);
  mesh(cooler, box(0.48, 0.09, 0.43, 0.025), rubber, 0, 0.075, 0);
  mesh(cooler, box(0.47, 0.08, 0.43, 0.025), chrome, 0, 1.085, 0);
  mesh(cooler, box(0.35, 0.28, 0.035, 0.016), rubber, 0, 0.79, 0.245);
  for (const [x, surface] of [[-0.10, pink], [0.10, cyan]]) {
    const tap = mesh(cooler, cylinder(0.025, 0.025, 0.08, 16), chrome, x, 0.87, 0.29); tap.rotation.x = Math.PI / 2;
    mesh(cooler, box(0.045, 0.055, 0.032, 0.012), surface, x, 0.92, 0.30);
    mesh(cooler, cylinder(0.012, 0.012, 0.045, 12), chrome, x, 0.84, 0.335);
  }
  mesh(cooler, box(0.37, 0.025, 0.18, 0.018), rubber, 0, 0.65, 0.285);
  for (let slat = 0; slat < 7; slat++) mesh(cooler, box(0.009, 0.010, 0.13, 0.002), chrome, -0.135 + slat * 0.045, 0.669, 0.30);
  grille(cooler, 0, 0.35, 0.244, 0.32, 0.25);
  const bottleGlass = material(materials.glass, { opacity: 0.45, roughness: 0.18, metalness: 0 }); bottleGlass.color.set('#96c3cc');
  const bottleWater = material(materials.glass, { opacity: 0.32, roughness: 0.24, metalness: 0 }); bottleWater.color.set('#5d9fad');
  mesh(cooler, lathe([[0.07, 0], [0.07, 0.06], [0.17, 0.10], [0.20, 0.17], [0.20, 0.46], [0.18, 0.53], [0.12, 0.56], [0, 0.56]], 36), bottleGlass, 0, 1.10, 0);
  mesh(cooler, cylinder(0.187, 0.187, 0.28, 32), bottleWater, 0, 1.39, 0);
  for (const y of [1.29, 1.43, 1.57]) ring(cooler, 0.20, 0.008, bottleGlass, y);
  mesh(cooler, cylinder(0.077, 0.077, 0.045, 24), cyan, 0, 1.115, 0);
  const cups = mesh(cooler, cylinder(0.065, 0.052, 0.37, 24), ivory, 0.325, 0.93, 0.02);
  for (const y of [0.78, 0.85, 0.92, 0.99, 1.06]) { const rim = ring(cooler, 0.065, 0.006, chrome, y); rim.position.x = 0.325; rim.position.z = 0.02; }
  batch(cooler);

  mesh(service, cylinder(0.12, 0.12, 0.035, 24), ceramic, 0, 3.765, 0.30);
  beam(service, [0, 3.75, 0.30], [0, 3.38, 0.30], 0.009, rubber);
  mesh(service, cylinder(0.052, 0.04, 0.13, 24), ceramic, 0, 3.32, 0.30);
  for (const y of [3.27, 3.29, 3.31]) ring(service, 0.047, 0.004, chrome, y).position.z = 0.30;
  const bulbMaterial = material(materials.lamp, { emissiveIntensity: 1.25, roughness: 0.30 });
  const bulb = mesh(service, lathe([[0, 0], [0.045, 0.012], [0.075, 0.05], [0.082, 0.09], [0.067, 0.14], [0.037, 0.18], [0.036, 0.205]], 28), bulbMaterial, 0, 3.06, 0.30);
  bulb.userData.interactive = true;
  const bulbLight = new THREE.PointLight(0xffe2ae, 10, 9, 2); bulbLight.position.set(0, 3.12, 0.40); service.add(bulbLight);
  batch(service);
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
  function update(z, { time = 0, gentle = true } = {}) {
    const first = Math.floor(z / 18);
    sculptureSlots.forEach((slot, index) => {
      const count = sculptureSlots.length;
      const distanceIndex = first + (index - first % count + count) % count;
      const at = distanceIndex * 18 + 9;
      const style = trackStyle(at);
      const reserved = [49, 225, 306, 436, 470, ...decorStops].some(distance => Math.abs(at - distance) < 8);
      slot.holder.visible = !reserved && style.id !== 'connector' && style.id !== 'descent' && at < 550 && at - z < (lite ? 38 : 66);
      if (slot.index === distanceIndex) return;
      slot.index = distanceIndex;
      const side = distanceIndex % 2 ? -2.75 : 2.75;
      positionAt(slot.holder, at, side, side > 0 ? -Math.PI / 2 : Math.PI / 2);
    });
    grass.visible = z > 396 && z < 443;
    wake.visible = z > 430 && z < 478;
    fixedExhibits.forEach(({ station, at }) => { station.visible = at - z > -18 && at - z < (lite ? 38 : 57); });
    service.visible = z > 525;
    const glow = gentle ? 1 : 0.96 + Math.sin(time * 0.83) * 0.035 + Math.sin(time * 2.1) * 0.012;
    bulbMaterial.emissiveIntensity = 1.25 * glow; bulbLight.intensity = 10 * glow;
  }
  update(0);
  return {
    group, cart, lever, pumpGrip, ready, update, interactive, serviceButton, serviceEnd: SERVICE_END, pumpTargets: [lever],
    setAdVisible(on) { serviceAd.visible = Boolean(on); },
    pump(angle) {
      lever.rotation.x = angle;
      updateCartPump(angle);
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
