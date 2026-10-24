/* The raised drawbridge follows the real launch ramp. Nothing spans its flight gap. */
import { Group, Mesh, InstancedMesh, BoxGeometry, TorusGeometry, CylinderGeometry, Matrix4, Quaternion, Vector3 } from 'three/webgpu';
import { getKeepLayout } from '../track/keep-layout.js';
import { keepStoneMaterial } from './keepFortress.js';

export function createKeepDrawbridge(scene, track, art = null) {
  const group = new Group(); group.name = 'keep-raised-drawbridge'; scene.add(group);
  const bridge = getKeepLayout(track).drawbridge, owned = [], geometry = [];
  const material = (hex) => { const m = keepStoneMaterial(hex, art, false); owned.push(m); return m; };
  const stone = material('#5d5364'), iron = material('#292333'), trim = material('#c25794');
  const box = new BoxGeometry(1, 1, 1), link = new TorusGeometry(.65, .18, 5, 8);
  geometry.push(box, link);
  const rows = [[], [], []], links = [], x = new Vector3(), y = new Vector3(), z = new Vector3();
  const matrix = new Matrix4(), rotation = new Quaternion(), position = new Vector3(), scale = new Vector3();
  function frame(s) {
    const f = track.frameAt(s); matrix.makeBasis(x.fromArray(f.b), y.fromArray(f.n), z.fromArray(f.t).negate());
    rotation.setFromRotationMatrix(matrix); return f;
  }
  function at(s, offset, up, size, slot = 1) {
    const f = frame(s);
    position.fromArray(f.p).addScaledVector(x, offset).addScaledVector(y, up);
    rows[slot].push(new Matrix4().compose(position, rotation, scale.fromArray(size)));
  }
  // The underside stays below the racing surface even where the curved ramp steepens.
  for (let s = bridge.rampS0 + 1; s < bridge.rampS1; s += 2) {
    at(s, 0, -1, [bridge.widthM, 1.25, 2.1]);
    at(s, 0, -.23, [bridge.widthM - .6, .18, .28], 2);
    for (const side of [-1, 1]) at(s, side * (bridge.widthM / 2 + .7), -.8, [1.25, 2, 2.1]);
  }
  const hinge = track.frameAt(bridge.rampS0), tip = track.frameAt(bridge.rampS1 - .4);
  for (const side of [-1, 1]) {
    // Twin gate towers anchor the suspension chains above the hinge.
    const offset = side * (bridge.widthM / 2 + 7);
    at(bridge.rampS0, offset, 14, [10, 34, 13], 0);
    at(bridge.rampS0, offset, 31.5, [12, 1.5, 15], 0);
    for (const d of [-4, 0, 4]) at(bridge.rampS0 + d, offset, 33.5, [11, 3.5, 2], 0);
    at(bridge.rampS0 + 7, offset, 11, [5, 3, 1.2]);
    const a = new Vector3(...hinge.p).addScaledVector(new Vector3(...hinge.b), side * (bridge.widthM / 2 + 1)).add(new Vector3(0, 28, 0));
    const b = new Vector3(...tip.p).addScaledVector(new Vector3(...tip.b), side * (bridge.widthM / 2 + 1)).add(new Vector3(0, .8, 0));
    const axis = b.clone().sub(a), count = Math.ceil(axis.length() / 1.15);
    const q = new Quaternion().setFromUnitVectors(new Vector3(0, 1, 0), axis.normalize());
    for (let i = 0; i <= count; i++) {
      position.copy(a).lerp(b, i / count);
      rotation.copy(q).multiply(new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), i % 2 * Math.PI / 2));
      links.push(new Matrix4().compose(position, rotation, scale.set(1, 1.15, 1)));
    }
    // The far bank is a ruined abutment with an unmistakable wide landing mouth.
    at(bridge.landingS0 + 10, side * (bridge.widthM / 2 + 4), -7, [6, 20, 24], 0);
    at(bridge.landingS0 + 15, side * (bridge.widthM / 2 + 4), 8, [6, 10, 8], 0);
  }
  at(bridge.rampS0, 0, 27, [bridge.widthM + 20, 4, 5], 0);
  const hingeGeo = new CylinderGeometry(1.35, 1.35, bridge.widthM + 5, 12); geometry.push(hingeGeo);
  const axle = new Mesh(hingeGeo, iron); frame(bridge.rampS0);
  axle.position.fromArray(hinge.p).addScaledVector(y, -1.6);
  axle.quaternion.setFromUnitVectors(new Vector3(0, 1, 0), x); group.add(axle);
  function batch(geo, mat, matrices, name) {
    const mesh = new InstancedMesh(geo, mat, matrices.length); mesh.name = name;
    matrices.forEach((m, i) => mesh.setMatrixAt(i, m)); mesh.computeBoundingSphere(); group.add(mesh);
  }
  rows.forEach((r, i) => batch(box, [stone, iron, trim][i], r, ['drawbridge-gatehouse', 'drawbridge-leaf', 'drawbridge-ribs'][i]));
  batch(link, trim, links, 'drawbridge-suspension-chains');
  return { group, stats: { gapM: bridge.gapM, riseM: bridge.riseM, chains: 2, links: links.length },
    dispose() { group.removeFromParent(); group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); geometry.forEach(g => g.dispose()); owned.forEach(m => m.dispose()); } };
}
