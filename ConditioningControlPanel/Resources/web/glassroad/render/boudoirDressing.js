/* The course lives inside a giant dressing room. Furniture is batched before shader warmup. */
import { Group, InstancedMesh, BoxGeometry, CylinderGeometry, SphereGeometry, TorusGeometry, CircleGeometry,
  OctahedronGeometry, MeshStandardMaterial, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, Color } from 'three/webgpu';
import { vec3, float, positionWorld, normalWorld, cameraPosition, normalize, dot, mix, smoothstep, fract, sin, cos, max, min, abs, fwidth, uv } from 'three/tsl';
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
  const swatches = { cream: '#ffe6ca', rose: '#e75c9e', lavender: '#a05ad4', gold: '#f0ba52',
    mirror: '#e4def7', pearl: '#fff4db', lip: '#f32679', glass: '#c893e4', floor: '#58314f', velvet: '#311635', mint: '#58d5be' };
  const materials = {};
  for (const [name, hex] of Object.entries(swatches)) {
    const source = new MeshStandardMaterial({ color: hex, metalness: name === 'gold' ? .85 : name === 'mirror' ? .97 : .12,
      roughness: name === 'gold' || name === 'mirror' ? .18 : name === 'pearl' ? .27 : .4 });
    source.name = 'boudoir-' + name;
    const material = createSoftMaterial(source, pal, { haze: .025, fill: .5, rim: vec3(.5, .13, .32), rimGain: .42, rimPow: 3 }).material;
    source.dispose(); materials[name] = material;
    if (['rose', 'lavender', 'cream', 'velvet'].includes(name)) {
      const N = normalize(normalWorld), weights = abs(N), p = positionWorld;
      const plane = p.xy.mul(weights.z).add(p.zy.mul(weights.x)).add(p.xz.mul(weights.y));
      const motif = sin(plane.x.mul(.43)).mul(cos(plane.y.mul(.43)));
      const footprint = max(fwidth(plane.x), fwidth(plane.y)), detailFade = float(1).sub(smoothstep(.6, 2, footprint));
      const lace = smoothstep(.72, .83, abs(motif)).mul(detailFade), pinstripe = smoothstep(.965, .992, cos(plane.x.mul(1.8))).mul(detailFade);
      const distance = cameraPosition.sub(p).length(), visible = float(1).sub(smoothstep(100, 340, distance));
      const weave = sin(plane.x.mul(9)).mul(sin(plane.y.mul(11))).mul(.025).mul(visible);
      material.colorNode = material.colorNode.mul(float(1).sub(lace.mul(name === 'velvet' ? .22 : .16)).add(weave))
        .add(vec3(.14, .055, .07).mul(pinstripe).mul(visible));
      // A fine edge separates adjacent lacquer boards without heavy black cartoon hulls.
      const u = uv(), pixels = min(min(u.x, float(1).sub(u.x)).div(fwidth(u.x).max(.00001)),
        min(u.y, float(1).sub(u.y)).div(fwidth(u.y).max(.00001)));
      material.colorNode = mix(material.colorNode.mul(.45), material.colorNode, smoothstep(.1, .8, pixels));
    }
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
  const X = new Vector3(), Y = new Vector3(), Z = new Vector3(), batches = new Map(), moving = [];
  const rotation = new Quaternion(), restQ = new Quaternion(), pivot = new Vector3(), axis = new Vector3();
  for (const row of plan.rows) {
    const key = row.shape + ':' + row.material + (row.motion ? ':moving' : '');
    if (!batches.has(key)) batches.set(key, []);
    batches.get(key).push(row);
  }
  for (const [key, rows] of batches) {
    const [shape, material] = key.split(':'), mesh = new InstancedMesh(geometries[shape], materials[material], rows.length);
    mesh.name = 'boudoir-' + key.replaceAll(':', '-');
    rows.forEach((row, i) => {
      B.makeBasis(X.fromArray(row.axes[0]), Y.fromArray(row.axes[1]), Z.fromArray(row.axes[2]));
      quaternion.setFromRotationMatrix(B);
      mesh.setMatrixAt(i, M.compose(P.fromArray(row.p), quaternion, S.fromArray(row.size)));
      if (row.motion) moving.push({ row, mesh, index: i, restQ: quaternion.clone() });
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh);
    if (key.endsWith(':moving') && mesh.boundingSphere) mesh.boundingSphere.radius += 16;
  }
  // Fixed luminance, drifting glitter. One draw covers the whole room, without point lights.
  const sparkleMaterial = new MeshBasicNodeMaterial({ color: new Color('#ffebc7'), fog: false });
  const count = Math.min(Q.name === 'phone' ? 420 : 1800, plan.particles.length * 7);
  const sparkle = new InstancedMesh(geometries.sparkle, sparkleMaterial, Math.max(1, count));
  sparkle.name = 'boudoir-pearl-sparkles'; sparkle.count = count; group.add(sparkle);
  let time = 0, disposed = false;
  function update({ dt = 0, gentle = false, paused = false } = {}) {
    if (disposed || paused) return;
    time += Math.max(0, Math.min(dt, 1)) * (gentle ? .5 : 1);
    for (const item of moving) {
      const { row, mesh, index } = item, m = row.motion;
      const angle = Math.sin(time * m.speed + m.phase) * m.amp;
      rotation.setFromAxisAngle(axis.fromArray(m.axis), angle);
      pivot.fromArray(m.pivot); P.fromArray(row.p).sub(pivot).applyQuaternion(rotation).add(pivot);
      P.y += Math.sin(time * .6 + m.phase) * m.rise;
      restQ.copy(rotation).multiply(item.restQ); S.fromArray(row.size);
      mesh.setMatrixAt(index, M.compose(P, restQ, S)); mesh.instanceMatrix.needsUpdate = true;
    }
    for (let i = 0; i < count; i++) {
      const seed = plan.particles[Math.floor(i / 7)], phase = seed.phase + i * 1.618;
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
    sparkles: count, furniture: plan.heroes.length, interiors: plan.interiors.length, rejected: plan.rejected,
    propClusters: plan.props.clusters.length, movingPieces: moving.length, features: plan.props.features };
  return { group, update, plan, stats, casters: [],
    dispose() {
      if (disposed) return; disposed = true; group.removeFromParent();
      group.traverse(object => { if (object.isInstancedMesh) object.dispose(); });
      Object.values(geometries).forEach(g => g.dispose()); Object.values(materials).forEach(m => m.dispose());
      sparkleMaterial.dispose();
    } };
}
