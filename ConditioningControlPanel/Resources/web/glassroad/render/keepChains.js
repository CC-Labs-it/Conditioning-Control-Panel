import { Group, Mesh, InstancedMesh, Object3D, BoxGeometry, TorusGeometry, IcosahedronGeometry, PlaneGeometry,
  MeshBasicNodeMaterial, Matrix4, Vector3, Color, DoubleSide } from 'three/webgpu';
import { vec3, normalWorld, dot, normalize, clamp, uniform } from 'three/tsl';
import { KEEP_CHAIN, keepChainPhase } from '../sim/keepChains.js';
import { LOOK } from './look.js';
export function createKeepChainView(scene, track, { chains = [] } = {}) {
  const group = new Group(); group.name = 'keep-chain-gauntlet'; scene.add(group);
  const owned = [], geometries = [], entries = [], K = KEEP_CHAIN;
  const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(.35).add(.65), 0, 1);
  function solid(hex) {
    const m = new MeshBasicNodeMaterial(), c = new Color(hex); m.colorNode = vec3(c.r, c.g, c.b).mul(shade); owned.push(m); return m;
  }
  const iron = solid('#9d86af'), gold = solid('#efbd73'), dark = solid('#120d20');
  const link = new TorusGeometry(.55, .14, 5, 10), weight = new IcosahedronGeometry(K.radiusM, 1), box = new BoxGeometry(1, 1, 1);
  const floorGeo = new PlaneGeometry(22, 6); geometries.push(link, weight, box, floorGeo);
  const matrix = new Matrix4(), axes = [new Vector3(), new Vector3(), new Vector3()], obj = new Object3D();
  for (const row of chains) {
    const f = track.frameAt(row.s), holder = new Group(); holder.name = 'chain:' + row.id;
    matrix.makeBasis(axes[0].fromArray(f.b), axes[1].fromArray(f.n), axes[2].fromArray(f.t).negate());
    holder.quaternion.setFromRotationMatrix(matrix); holder.position.fromArray(f.p); group.add(holder);
    const support = new InstancedMesh(box, dark, 3);
    [[-f.w / 2 - 1, 8, 0, 1.7, 16, 2], [f.w / 2 + 1, 8, 0, 1.7, 16, 2], [0, 16, 0, f.w + 4, 2, 2]].forEach((a, i) => {
      obj.position.set(...a.slice(0, 3)); obj.scale.set(...a.slice(3)); obj.rotation.set(0, 0, 0); obj.updateMatrix(); support.setMatrixAt(i, obj.matrix);
    }); holder.add(support);
    const pivot = new Group(); pivot.position.set(row.x || 0, K.pivotM, 0); holder.add(pivot);
    const links = new InstancedMesh(link, iron, 12);
    for (let i = 0; i < 12; i++) {
      obj.position.set(0, -i - .4, 0); obj.scale.set(1, 1.3, 1); obj.rotation.set(0, i % 2 * Math.PI / 2, 0);
      obj.updateMatrix(); links.setMatrixAt(i, obj.matrix);
    } pivot.add(links);
    const ball = new Mesh(weight, solid('#695174')); ball.scale.setScalar(.8); ball.name = 'forged-pendulum-core'; ball.position.y = -K.lengthM; pivot.add(ball);
    // Recessed panels and hammered studs stay inside the existing collision sphere.
    const panels = new InstancedMesh(box, iron, 12), studs = new InstancedMesh(weight, gold, 12);
    panels.name = 'pendulum-armour-panels'; studs.name = 'pendulum-rivets';
    for (let i = 0; i < 12; i++) {
      const a = i * Math.PI / 6, y = i % 2 ? .6 : -.6, radius = 2.13;
      obj.position.set(Math.sin(a) * radius, -K.lengthM + y, Math.cos(a) * radius);
      obj.rotation.set(0, a, 0); obj.scale.set(.88, .7, .24); obj.updateMatrix(); panels.setMatrixAt(i, obj.matrix);
      obj.position.set(Math.sin(a) * 2.31, -K.lengthM + y, Math.cos(a) * 2.31);
      obj.scale.setScalar(.04); obj.updateMatrix(); studs.setMatrixAt(i, obj.matrix);
    }
    pivot.add(panels, studs);
    const collarGeo = new TorusGeometry(.66, .18, 5, 12); geometries.push(collarGeo);
    for (const y of [-.3, -K.lengthM + 2]) {
      const collar = new Mesh(collarGeo, gold); collar.name = 'pendulum-chain-collar'; collar.position.y = y;
      collar.rotation.x = Math.PI / 2; pivot.add(collar);
    }
    const axle = new Mesh(box, gold); axle.name = 'pendulum-bearing'; axle.position.set(row.x || 0, K.pivotM, 0);
    axle.scale.set(2.3, 1.3, 3.2); holder.add(axle);
    const band = new Mesh(new TorusGeometry(K.radiusM + .02, .13, 5, 16), gold); geometries.push(band.geometry); band.position.y = -K.lengthM; pivot.add(band);
    const alpha = uniform(.15), warning = new MeshBasicNodeMaterial({ color: '#ffc58a', transparent: true, depthWrite: false, side: DoubleSide });
    warning.opacityNode = alpha; owned.push(warning);
    const floor = new Mesh(floorGeo, warning); floor.rotation.x = -Math.PI / 2; floor.position.y = .08; holder.add(floor);
    entries.push({ row, pivot, alpha });
  }
  function update(timeS) { for (const e of entries) { const p = keepChainPhase(e.row, timeS); e.pivot.rotation.z = p.angle; e.alpha.value = p.warning ? .24 : .08; } }
  update(0);
  return { group, update, inspect: () => ({ count: entries.length }), dispose() { group.removeFromParent(); group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); geometries.forEach(g => g.dispose()); owned.forEach(m => m.dispose()); } };
}
