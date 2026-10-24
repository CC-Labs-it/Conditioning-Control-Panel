/* The course lives inside a giant dressing room. Furniture is batched before shader warmup. */
import { Group, InstancedMesh, BoxGeometry, CylinderGeometry, SphereGeometry, TorusGeometry, CircleGeometry,
  OctahedronGeometry, MeshStandardMaterial, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, Color } from 'three/webgpu';
import { vec3, float, positionWorld, normalWorld, cameraPosition, normalize, dot, mix, smoothstep, fract, sin, max, abs } from 'three/tsl';
import { createSoftMaterial } from './materials/colossus.js';
import { boudoirWorldPlan } from './boudoirMedia.js';
import { Q } from './quality.js';

function lipstickGeometry() {
  const geometry = new CylinderGeometry(.5, .5, 1, 20, 1), positions = geometry.attributes.position;
  // A diagonal lipstick cut reads as wax, distinct from the straight metal case beneath it.
  for (let i = 0; i < positions.count; i++) if (positions.getY(i) > 0)
    positions.setY(i, .34 + positions.getX(i) * .3);
  positions.needsUpdate = true; geometry.computeVertexNormals(); return geometry;
}

export function buildBoudoirDressing(track, art, pal, samples) {
  const plan = boudoirWorldPlan(track), group = new Group(); group.name = 'boudoir-dressing-room';
  if (!plan) return { group, update() {}, dispose() { group.removeFromParent(); }, casters: [] };
  const geometries = {
    box: new BoxGeometry(1, 1, 1), sphere: new SphereGeometry(.5, 16, 10),
    cylinder: new CylinderGeometry(.5, .5, 1, 20), cone: new CylinderGeometry(.22, .5, 1, 20),
    torus: new TorusGeometry(.43, .07, 8, 40), disc: new CircleGeometry(.5, 40), lipstick: lipstickGeometry(),
    sparkle: new OctahedronGeometry(.5),
  };
  const swatches = { cream: '#f3dcd1', rose: '#bd6e96', lavender: '#8972aa', gold: '#d7af65',
    mirror: '#d7d6ec', pearl: '#fff1e4', lip: '#e33982', glass: '#b889c9', floor: '#65485b' };
  const materials = {};
  for (const [name, hex] of Object.entries(swatches)) {
    const source = new MeshStandardMaterial({ color: hex, metalness: name === 'gold' ? .85 : name === 'mirror' ? .97 : .12,
      roughness: name === 'gold' || name === 'mirror' ? .18 : name === 'pearl' ? .27 : .4 });
    source.name = 'boudoir-' + name;
    const material = createSoftMaterial(source, pal, { haze: .05, fill: .44, rim: vec3(.29, .09, .22), rimGain: .25, rimPow: 3 }).material;
    source.dispose(); materials[name] = material;
    if (name === 'mirror') {
      const V = normalize(cameraPosition.sub(positionWorld)), N = normalize(normalWorld), edge = float(1).sub(abs(dot(N, V)));
      const line = smoothstep(.72, .85, sin(positionWorld.x.mul(.028).add(positionWorld.y.mul(.035))).mul(.5).add(.5));
      material.colorNode = mix(vec3(.2, .15, .31), vec3(.77, .65, .8), smoothstep(.08, .92, edge))
        .add(vec3(.36, .26, .3).mul(line));
    }
    if (name === 'floor') {
      const x = fract(positionWorld.x.div(24)), z = fract(positionWorld.z.div(120));
      const seams = max(float(1).sub(smoothstep(.006, .014, x)), float(1).sub(smoothstep(.003, .006, z)));
      const grain = sin(positionWorld.z.mul(.2).add(sin(positionWorld.x.mul(.38)))).mul(.025).add(.975);
      material.colorNode = material.colorNode.mul(grain).mul(float(1).sub(seams.mul(.5)));
    }
  }
  const M = new Matrix4(), B = new Matrix4(), P = new Vector3(), S = new Vector3(), quaternion = new Quaternion();
  const X = new Vector3(), Y = new Vector3(), Z = new Vector3(), batches = new Map();
  for (const row of plan.rows) {
    const key = row.shape + ':' + row.material;
    if (!batches.has(key)) batches.set(key, []);
    batches.get(key).push(row);
  }
  for (const [key, rows] of batches) {
    const [shape, material] = key.split(':'), mesh = new InstancedMesh(geometries[shape], materials[material], rows.length);
    mesh.name = 'boudoir-' + shape + '-' + material;
    rows.forEach((row, i) => {
      B.makeBasis(X.fromArray(row.axes[0]), Y.fromArray(row.axes[1]), Z.fromArray(row.axes[2]));
      quaternion.setFromRotationMatrix(B);
      mesh.setMatrixAt(i, M.compose(P.fromArray(row.p), quaternion, S.fromArray(row.size)));
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh);
  }
  // Fixed luminance, drifting glitter. One draw covers the whole room, without point lights.
  const sparkleMaterial = new MeshBasicNodeMaterial({ color: new Color('#ffebc7'), fog: false });
  const count = Math.min(Q.name === 'phone' ? 240 : 720, plan.particles.length * 4);
  const sparkle = new InstancedMesh(geometries.sparkle, sparkleMaterial, Math.max(1, count));
  sparkle.name = 'boudoir-pearl-sparkles'; sparkle.count = count; group.add(sparkle);
  let time = 0, disposed = false;
  function update({ dt = 0, gentle = false, paused = false } = {}) {
    if (disposed || paused) return;
    time += Math.max(0, Math.min(dt, 1)) * (gentle ? .5 : 1);
    for (let i = 0; i < count; i++) {
      const seed = plan.particles[Math.floor(i / 4)], phase = seed.phase + i * 1.618;
      const rise = ((time * 1.2 + phase * 2) % 16 + 16) % 16, radius = .16 + (i % 4) * .07;
      P.fromArray(seed.p); P.x += Math.sin(time * .17 + phase) * 5; P.z += Math.cos(time * .13 + phase) * 5; P.y += rise;
      quaternion.setFromAxisAngle(Y.set(0, 1, 0), time * .35 + phase);
      const fade = Math.min(1, rise / 2, (16 - rise) / 2);
      S.set(radius * fade, radius * (i % 3 ? 1 : 3) * fade, radius * fade);
      sparkle.setMatrixAt(i, M.compose(P, quaternion, S));
    }
    sparkle.instanceMatrix.needsUpdate = true;
  }
  update(); sparkle.computeBoundingSphere();
  // The bounds include all drift, so the shared batch never disappears at a camera edge.
  if (sparkle.boundingSphere) sparkle.boundingSphere.radius += 24;
  const stats = { draws: group.children.length, solids: plan.rows.length, mirrors: plan.mounts.length,
    sparkles: count, furniture: plan.heroes.length, interiors: plan.interiors.length, rejected: plan.rejected };
  return { group, update, plan, stats, casters: [],
    dispose() {
      if (disposed) return; disposed = true; group.removeFromParent();
      group.traverse(object => { if (object.isInstancedMesh) object.dispose(); });
      Object.values(geometries).forEach(g => g.dispose()); Object.values(materials).forEach(m => m.dispose());
      sparkleMaterial.dispose();
    } };
}
