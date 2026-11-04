// A layered toy-race showroom. One owned scenic kit, no media or frame-time allocations.
import { Group, Mesh, InstancedMesh, Object3D, Color, SphereGeometry, PlaneGeometry, CylinderGeometry,
  TorusGeometry, OctahedronGeometry, BufferGeometry, Float32BufferAttribute, CatmullRomCurve3, Vector3,
  MeshBasicNodeMaterial, MeshStandardNodeMaterial, BackSide, DoubleSide } from 'three/webgpu';
import { positionGeometry, positionWorld, normalWorld, normalView, positionView, vec3, float,
  mix, sin, dot, normalize, smoothstep, floor, mod, max, fwidth, uniform } from 'three/tsl';

export const GARAGE_BACKDROP = Object.freeze({ clouds: 72, bubbles: 28, stars: 36, maxDraws: 15 });

export function garageBackdropLayout() {
  let seed = 38492;
  const rnd = () => ((seed = seed * 16807 % 2147483647) - 1) / 2147483646;
  const clouds = [], bubbles = [], stars = [];
  for (let i = 0; i < GARAGE_BACKDROP.clouds; i++) {
    const side = i % 2 ? -1 : 1, bank = Math.floor(i / 6);
    const z = 7 + bank % 4 * 4.3, size = .65 + rnd() * 1.35;
    clouds.push({ p: [side * (4 + rnd() * 5.8), .1 + Math.floor(bank / 4) * .38 + rnd() * .45, z],
      size, phase: rnd() * Math.PI * 2, colour: bank % 3 });
  }
  for (let i = 0; i < GARAGE_BACKDROP.bubbles; i++) {
    const near = i < 8, side = i % 2 ? -1 : 1;
    bubbles.push({ p: [side * (near ? 2.1 + rnd() * .9 : 2 + rnd() * 7),
      near ? .4 + rnd() * 1.5 : .4 + rnd() * 1.4, near ? -.2 + rnd() * 3 : 5 + rnd() * 15],
      size: near ? .12 + rnd() * .2 : .16 + rnd() * .4, phase: rnd() * Math.PI * 2 });
  }
  for (let i = 0; i < GARAGE_BACKDROP.stars; i++) stars.push({
    p: [(i % 2 ? -1 : 1) * (2.2 + rnd() * 7), .3 + rnd() * 1.6, 2 + rnd() * 20],
    size: .018 + rnd() * .042, phase: rnd() * Math.PI * 2 });
  return { clouds, bubbles, stars };
}

function ribbonGeometry(points, width) {
  const curve = new CatmullRomCurve3(points.map(p => new Vector3(...p))), positions = [], index = [];
  const side = new Vector3(), up = new Vector3(0, 1, 0);
  for (let i = 0; i <= 72; i++) {
    const p = curve.getPoint(i / 72), tangent = curve.getTangent(i / 72);
    side.crossVectors(tangent, up).normalize().multiplyScalar(width / 2);
    for (const sign of [-1, 1]) positions.push(p.x + side.x * sign, p.y + side.y * sign, p.z + side.z * sign);
    if (i) { const a = (i - 1) * 2; index.push(a, a + 2, a + 1, a + 1, a + 2, a + 3); }
  }
  const geometry = new BufferGeometry(); geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setIndex(index); geometry.computeVertexNormals(); return geometry;
}

export function createGarageBackdrop(scene) {
  const object = new Group(); object.name = 'garage-bubblegum-vista'; scene.add(object);
  const geometries = new Set(), materials = new Set(), batches = [], pose = new Object3D();
  const ownGeo = geometry => { geometries.add(geometry); return geometry; };
  const ownMat = material => { materials.add(material); return material; };
  const add = (geometry, material, name, p = [0, 0, 0]) => {
    const mesh = new Mesh(geometry, material); mesh.name = name; mesh.position.set(...p); object.add(mesh); return mesh;
  };
  const sky = ownMat(new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, fog: false }));
  // The accepted down-tilted camera sees a low skyline, so grade that whole visible band.
  const y = positionGeometry.y.div(3).add(.18);
  const wash = sin(positionGeometry.x.mul(.15).add(positionGeometry.z.mul(.11))).mul(.5).add(.5);
  sky.colorNode = mix(vec3(.74, .38, .60), vec3(1, .91, .73), smoothstep(-.28, .65, y))
    .add(vec3(.07, .015, .04).mul(wash));
  add(ownGeo(new SphereGeometry(28, 32, 16)), sky, 'garage-painted-sky', [0, 0, 3]).renderOrder = -20;

  const clock = uniform(0), floorMat = ownMat(new MeshStandardNodeMaterial({ color: '#f2b4ce', roughness: .3, metalness: .12 }));
  const cell = floor(positionWorld.xz.div(.85)), parity = mod(cell.x.add(cell.y), 2);
  const detail = float(1).sub(smoothstep(.55, 1.6, max(fwidth(positionWorld.x), fwidth(positionWorld.z))));
  const sheen = sin(positionWorld.z.mul(.5).add(positionWorld.x.mul(.3)).sub(clock.mul(.15))).mul(.016);
  floorMat.colorNode = mix(vec3(.79, .43, .59), vec3(.96, .72, .80), mix(.5, parity, detail)).add(sheen);
  const floorMesh = add(ownGeo(new PlaneGeometry(70, 70)), floorMat, 'garage-gloss-checker-floor', [0, -.17, 6]);
  floorMesh.rotation.x = -Math.PI / 2;
  const cream = ownMat(new MeshStandardNodeMaterial({ color: '#fff0df', roughness: .27, metalness: .12 }));
  const pink = ownMat(new MeshBasicNodeMaterial({ color: '#ed559f', side: DoubleSide }));
  const ink = ownMat(new MeshBasicNodeMaterial({ color: '#71335b', side: DoubleSide }));
  const pearl = ownMat(new MeshBasicNodeMaterial({ color: '#fff4df', side: DoubleSide }));
  add(ownGeo(new CylinderGeometry(4.35, 4.42, .16, 80)), cream, 'garage-showroom-island', [0, -.105, 2.3]);
  const rim = add(ownGeo(new TorusGeometry(4.3, .045, 8, 96)), pink, 'garage-showroom-rim', [0, -.005, 2.3]);
  rim.rotation.x = Math.PI / 2;
  add(ownGeo(new TorusGeometry(1.35, .12, 10, 64, Math.PI)), pink, 'garage-candy-race-arch', [0, .15, 10]);
  add(ownGeo(new TorusGeometry(1.16, .035, 6, 64, Math.PI)), pearl, 'garage-arch-inlay', [0, .15, 9.87]);
  for (const [i, points] of [
    [[-11, .1, 10], [-6, .5, 14], [-6, 1.15, 19], [-2, 1.5, 23], [4, .85, 23], [9, .55, 17]],
    [[10, .15, 8], [6, .45, 12], [6, 1.25, 18], [9, 1.45, 23], [14, 1.05, 24]],
  ].entries()) {
    const g = ownGeo(ribbonGeometry(points, .82));
    add(g, i ? pearl : pink, 'garage-distant-track-' + i);
    const underside = add(g, ink, 'garage-track-underside-' + i, [0, -.06, 0]); underside.renderOrder = -1;
  }

  const layout = garageBackdropLayout(), sphere = ownGeo(new SphereGeometry(1, 14, 10));
  const cloudMat = ownMat(new MeshBasicNodeMaterial({ color: '#ffffff' }));
  cloudMat.colorNode = vec3(1).mul(dot(normalize(normalWorld), normalize(vec3(-.4, .85, -.2))).mul(.16).add(.84));
  const cloudMesh = new InstancedMesh(sphere, cloudMat, layout.clouds.length); cloudMesh.name = 'garage-scalloped-cloud-banks';
  const colours = ['#f6a6cc', '#fff0df', '#e57cb5'];
  layout.clouds.forEach((row, i) => cloudMesh.setColorAt(i, new Color(colours[row.colour])));
  const bubbleMat = ownMat(new MeshBasicNodeMaterial({ transparent: true, depthWrite: false }));
  const rimLight = dot(normalize(normalView), normalize(positionView.negate())).abs().oneMinus();
  bubbleMat.colorNode = mix(vec3(.94, .35, .67), vec3(1, .93, .76), rimLight.pow(2));
  bubbleMat.opacityNode = rimLight.pow(2).mul(.58).add(.025);
  const bubbleMesh = new InstancedMesh(sphere, bubbleMat, layout.bubbles.length); bubbleMesh.name = 'garage-near-far-soap-bubbles';
  const starMat = ownMat(new MeshBasicNodeMaterial({ color: '#fff6d4' }));
  const starMesh = new InstancedMesh(ownGeo(new OctahedronGeometry(1)), starMat, layout.stars.length); starMesh.name = 'garage-floating-sparkles';
  for (const batch of [cloudMesh, bubbleMesh, starMesh]) { batch.frustumCulled = false; object.add(batch); batches.push(batch); }
  let time = 0, disposed = false;
  function place() {
    for (let i = 0; i < layout.clouds.length; i++) {
      const row = layout.clouds[i]; pose.position.fromArray(row.p); pose.position.y += Math.sin(time * .14 + row.phase) * .12;
      pose.rotation.set(0, 0, 0); pose.scale.set(row.size * 1.5, row.size * .65, row.size);
      pose.updateMatrix(); cloudMesh.setMatrixAt(i, pose.matrix);
    }
    for (let i = 0; i < layout.bubbles.length; i++) {
      const row = layout.bubbles[i]; pose.position.fromArray(row.p);
      pose.position.x += Math.sin(time * .22 + row.phase) * .15; pose.position.y += Math.sin(time * .33 + row.phase) * .24;
      pose.rotation.set(0, 0, 0); pose.scale.setScalar(row.size); pose.updateMatrix(); bubbleMesh.setMatrixAt(i, pose.matrix);
    }
    for (let i = 0; i < layout.stars.length; i++) {
      const row = layout.stars[i]; pose.position.fromArray(row.p); pose.position.y += Math.sin(time * .25 + row.phase) * .15;
      pose.rotation.set(.3, time * .18 + row.phase, time * .12); pose.scale.set(row.size, row.size * 1.5, row.size * .7);
      pose.updateMatrix(); starMesh.setMatrixAt(i, pose.matrix);
    }
    for (const batch of batches) batch.instanceMatrix.needsUpdate = true;
    clock.value = time;
  }
  place();
  return { object, layout,
    update(dt, { reducedMotion = false, gentle = false, paused = false } = {}) {
      if (disposed || paused || reducedMotion || gentle) return;
      time += Math.max(0, Math.min(.05, Number.isFinite(dt) ? dt : 0)); place();
    },
    inspect: () => ({ time, draws: object.children.length, clouds: layout.clouds.length, bubbles: layout.bubbles.length, stars: layout.stars.length }),
    dispose() { if (disposed) return; disposed = true; object.removeFromParent();
      for (const batch of batches) batch.dispose(); for (const g of geometries) g.dispose(); for (const m of materials) m.dispose(); },
  };
}
