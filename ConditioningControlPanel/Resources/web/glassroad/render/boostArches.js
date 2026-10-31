import { Group, Mesh, BoxGeometry, TorusGeometry, SphereGeometry, RingGeometry, MeshBasicNodeMaterial, Color } from 'three/webgpu';
import { positionLocal, normalWorld, normalize, vec3, dot, max, sin, uniform } from 'three/tsl';
import { placeOnFrame } from './cueKit.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { boostArchPlan } from '../track/boostArches.js';

/** Solid road furniture with a shared brushed finish, open through the entire kart envelope. */
export function createBoostArches(scene, track) {
  const object = new Group(); object.name = 'boost-arches'; scene.add(object);
  const plan = boostArchPlan(track);
  if (!plan.arches.length) return { object, rows: [], update() {}, reset() {}, dispose() { object.removeFromParent(); } };
  const vanity = track.id === 'boudoir', keep = track.id === 'keep';
  const materials = [], geometries = [];
  function finish(hex, grain = .025) {
    const m = new MeshBasicNodeMaterial(), colour = uniform(new Color(hex));
    const weave = sin(positionLocal.y.mul(34).add(positionLocal.z.mul(7))).mul(grain).add(1 - grain);
    const lamp = max(dot(normalWorld, normalize(vec3(-.45, .8, .35))), 0).mul(.36).add(.64);
    m.colorNode = colour.mul(lamp).mul(weave); materials.push(m); return m;
  }
  const body = finish(vanity ? '#bf3b71' : keep ? '#64515c' : '#e42c94', .04);
  const trim = finish(vanity || keep ? '#e7bd69' : '#fff6fc');
  const ink = finish(vanity ? '#6b2448' : '#31132f');
  const box = new BoxGeometry(1, 1, 1), arc = new TorusGeometry(3.7, .44, 8, 28, Math.PI);
  const fine = new TorusGeometry(3.7, .10, 6, 28, Math.PI), sphere = new SphereGeometry(1, 12, 8);
  const halo = new RingGeometry(2.7, 3, 32); halo.rotateX(-Math.PI / 2);
  geometries.push(box, arc, fine, sphere, halo);
  const rows = [], batches = new Map();
  for (const a of plan.arches) {
    const root = new Group(); root.name = (vanity ? 'vanity-arch-' : 'sunken-lock-') + a.id;
    placeOnFrame(root, track.frameAt(a.s)); root.translateX(a.x); object.add(root);
    const add = (g, m, p, scale = [1, 1, 1]) => { const mesh = new Mesh(g, m); mesh.position.set(...p); mesh.scale.set(...scale); root.add(mesh); return mesh; };
    add(arc, body, [0, 1.8, 0]);
    add(fine, trim, [0, 1.8, .46]); add(fine, trim, [0, 1.8, -.46]);
    for (const side of [-1, 1]) {
      add(box, ink, [side * 3.7, .15, 0], [1.45, .7, 1.5]);
      add(box, body, [side * 3.7, .55, 0], [.88, 2.5, .88]);
      add(box, trim, [side * 3.7, .2, 0], [1.18, .18, 1.18]);
      add(box, trim, [side * 3.7, 1.65, .46], [.4, .35, .1]);
      if (vanity) {
        const bow = add(sphere, body, [side * .65, 5.65, 0], [.82, .42, .35]); bow.rotation.z = side * .35;
        add(sphere, trim, [side * 3.7, 2.15, 0], [.58, .18, .58]);
      } else {
        // The lock case is below road level; its two shoulders show where the shackle enters.
        add(box, body, [side * 3.7, -.45, 0], [2.2, 1.3, 1.6]);
        for (const z of [-.83, .83]) add(sphere, trim, [side * 3.7, .06, z], [.13, .13, .07]);
      }
    }
    if (vanity) add(sphere, trim, [0, 5.65, .12], [.3, .3, .4]);
    const parts = root.children.slice();
    root.updateWorldMatrix(true, true);
    for (const mesh of parts) {
      const key = a.run + ':' + mesh.material.uuid;
      if (!batches.has(key)) batches.set(key, { material: mesh.material, parts: [] });
      batches.get(key).parts.push(mesh.geometry.clone().applyMatrix4(mesh.matrixWorld));
      root.remove(mesh);
    }
    const pulse = add(halo, trim, [0, .08, 0]); pulse.visible = false;
    root.updateMatrix(); rows.push({ root, pulse, parts, id: a.id });
  }
  // Three static draws per short run, rather than a draw for every bolt and trim.
  for (const batch of batches.values()) {
    const geometry = mergeGeometries(batch.parts); geometries.push(geometry);
    object.add(new Mesh(geometry, batch.material));
    for (const g of batch.parts) g.dispose();
  }
  return { object, rows,
    update(views = [], { paused = false, gentle = false, motion = 'full' } = {}) {
      if (paused) return;
      for (const row of rows) {
        const age = .7 - (views[row.id]?.pulse || 0), active = age > 0 && age < .7;
        row.pulse.visible = active && motion !== 'off' && !gentle;
        const p = Math.max(0, Math.min(1, age / .7));
        row.pulse.scale.setScalar(Math.sin(p * Math.PI) * (motion === 'less' ? .65 : 1.1));
      }
    },
    reset() { for (const row of rows) row.pulse.visible = false; },
    dispose() { object.removeFromParent(); for (const g of geometries) g.dispose(); for (const m of materials) m.dispose(); }
  };
}
