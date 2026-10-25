/* Physical toys, flat Snap stars and the live Loom wheel. No motion lives in the GLBs. */
import { Group, Mesh, MeshBasicNodeMaterial, RingGeometry, CircleGeometry, BufferGeometry, BufferAttribute, Vector3, Quaternion, DoubleSide } from 'three/webgpu';
import { texture, uv, uniform, vec2, sin, cos } from 'three/tsl';
import { acquireLoomAtlas, loomAtlasUv } from '../loomAtlas.js';
import { TOY_MODELS, TOYS, toyPose, toyEnvelope, snapRing, guardLook, toyLod, toyScale, toyLaunchPose } from './toyMath.js';
import { S } from '../../sim/state.js';
import { Q } from '../quality.js';

const UP = new Vector3(0, 1, 0), P = new Vector3(), N = new Vector3(), ROT = new Quaternion();
const FORWARD = new Vector3(0, 0, -1), FLIGHT = new Vector3(), RELEASE = {};
const point = (o, p) => o.position.set(p?.[0] || 0, p?.[1] || 0, p?.[2] || 0);
function starRing() {
  const p = [];
  for (let i = 0; i < 12; i++) {
    const a = i * Math.PI / 6, x = Math.cos(a), z = Math.sin(a);
    for (let j = 0; j < 8; j++) {
      const b = j * Math.PI / 4, c = (j + 1) * Math.PI / 4;
      const r = j % 2 ? .034 : .13, s = (j + 1) % 2 ? .034 : .13;
      p.push(x, 0, z, x + Math.cos(b) * r, 0, z + Math.sin(b) * r, x + Math.cos(c) * s, 0, z + Math.sin(c) * s);
    }
  }
  return new BufferGeometry().setAttribute('position', new BufferAttribute(new Float32Array(p), 3));
}

export function createToyProps(root, art, feel, { photosafe = null, palette = () => '#db659e' } = {}) {
  const group = new Group(); group.name = 'physical-toys'; root.add(group);
  const atlas = acquireLoomAtlas(), style = uniform(0);
  const at = uv().sub(.5), angle = uniform(0);
  const spun = vec2(at.x.mul(cos(angle)).sub(at.y.mul(sin(angle))), at.x.mul(sin(angle)).add(at.y.mul(cos(angle)))).add(.5);
  const loom = new MeshBasicNodeMaterial({ side: DoubleSide, transparent: true, depthWrite: false });
  loom.colorNode = texture(atlas.texture, loomAtlasUv(spun, style)).rgb;
  loom.opacityNode = uniform(1).onObjectUpdate(({ object }) => object.userData.toyAlpha ?? 1);
  const ringGeo = new RingGeometry(.94, 1, 64).rotateX(-Math.PI / 2), stars = starRing();
  const iconGeo = new CircleGeometry(.5, 48);
  const pools = new Map(), used = new Map(), effects = [], marks = [], guards = [], slow = [];
  const landingStates = new Map();
  const slowAge = new Float32Array(8), slowFade = new Float32Array(8);
  let time = 0, still = false, camera = null;
  function make(id) {
    const source = art?.model?.(id)?.scene;
    if (!source) return null;
    const obj = source.clone(true), mats = [];
    obj.traverse(o => {
      if (!o.isMesh) return;
      o.frustumCulled = false;
      if (o.name === 'toy_loom_face') { o.material = loom; return; }
      o.material = (Array.isArray(o.material) ? o.material : [o.material]).map(m => {
        const c = m.clone(); c.transparent = true; c.depthWrite = true; mats.push(c); return c;
      });
      if (o.material.length === 1) o.material = o.material[0];
    });
    const carrier = new Group(), rotator = new Group(); rotator.add(obj); carrier.add(rotator); group.add(carrier); carrier.visible = false;
    return { carrier, rotator, obj, mats, pivot: obj.getObjectByName('toy_pendulum_pivot'), face: obj.getObjectByName('toy_loom_face') };
  }
  // All geometry/material variants exist before the loading cover lifts.
  for (const id of [...Object.values(TOY_MODELS), 'toy-pendulum-lod1']) {
    const rows = [];
    for (let i = 0; i < 12; i++) { const e = make(id); if (e) rows.push(e); }
    pools.set(id, rows);
  }
  function effectMesh(geometry) {
    const material = new MeshBasicNodeMaterial({ color: '#f1b760', transparent: true, opacity: 0, depthWrite: false, side: DoubleSide });
    const mesh = new Mesh(geometry, material); mesh.frustumCulled = false; mesh.renderOrder = 3; mesh.visible = false; group.add(mesh); return mesh;
  }
  for (let i = 0; i < 24; i++) marks.push({ ring: effectMesh(ringGeo), stars: effectMesh(stars) });
  for (let i = 0; i < 8; i++) {
    guards.push(effectMesh(stars));
    const g = new Mesh(iconGeo, loom); g.visible = false; g.frustumCulled = false; group.add(g); slow.push(g);
  }
  const setAlpha = (e, a) => { for (const m of e.mats) m.opacity = a; if (e.face) e.face.userData.toyAlpha = a; };
  function place(kind, p, { age = 1, held = false, rotation = null, velocity = null } = {}) {
    let id = TOY_MODELS[kind];
    if (!id) return false;
    if (kind === 'pendulum') {
      const distance = camera?.position ? P.fromArray(p).distanceTo(camera.position) : 0;
      id = 'toy-pendulum-lod' + toyLod(distance, Q.name === 'phone');
    }
    const rows = pools.get(id), index = used.get(id) || 0, e = rows?.[index];
    if (!e) return false;
    used.set(id, index + 1); e.carrier.visible = true; point(e.carrier, p);
    e.carrier.quaternion.identity();
    if (rotation) e.carrier.quaternion.copy(rotation);
    else if (velocity && Math.hypot(...velocity) > .01) e.carrier.quaternion.setFromUnitVectors(FORWARD, FLIGHT.fromArray(velocity).normalize());
    const pose = toyPose(kind, time, still);
    e.rotator.rotation.set(0, 0, 0); e.obj.position.set(0, kind === 'pendulum' ? -.45 : kind === 'bigSpiral' ? -.8 : 0, 0);
    if (e.pivot) e.pivot.rotation.z = pose.swing;
    if (kind === 'pendulum') e.rotator.rotation.y = Math.PI;
    if (kind === 'disc') e.rotator.rotation.set(pose.bank, pose.spin, .16);
    if (kind === 'bigSpiral') e.rotator.rotation.z = pose.spin;
    // The held watch's top chain anchor stays above the kart, at the shared held socket.
    const size = toyScale(kind, held), release = toyLaunchPose(held ? 1 : age, still, RELEASE);
    e.carrier.scale.set(size * release.cross, size * release.cross, size * release.along);
    setAlpha(e, held ? 1 : release.alpha);
    return true;
  }
  function ring(row, p, n, r, a, star = true, col = palette()) {
    point(row.ring, p); N.fromArray(n || [0, 1, 0]); if (N.lengthSq() < .01) N.copy(UP); N.normalize();
    row.ring.position.addScaledVector(N, .12); row.ring.quaternion.setFromUnitVectors(UP, N); row.ring.scale.setScalar(r);
    row.ring.visible = a > .001; row.ring.material.opacity = a * .65; row.ring.material.color.set(col);
    row.stars.position.copy(row.ring.position); row.stars.quaternion.copy(row.ring.quaternion); row.stars.scale.setScalar(r);
    row.stars.visible = star && a > .001; row.stars.material.opacity = a; row.stars.material.color.set('#ffe3a2');
  }
  function begin(view, dt, karts, cam, quiet = false) {
    camera = cam; still = quiet; time += quiet ? 0 : dt; used.clear();
    angle.value = time * .45; style.value = 0;
    for (const rows of pools.values()) for (const e of rows) e.carrier.visible = false;
    for (const m of marks) { m.ring.visible = false; m.stars.visible = false; }
    for (const g of guards) g.visible = false;
    for (const g of slow) g.visible = false;
    let mi = 0;
    for (const s of landingStates.values()) s.seen = false;
    for (const pr of view?.projectiles || []) if (pr.kind === 'bigSpiral' && pr.landingP) {
      let s = landingStates.get(pr.id);
      if (!s && landingStates.size < 12) { s = { alpha: 0 }; landingStates.set(pr.id, s); }
      if (!s) continue;
      s.seen = true; s.p = [...pr.landingP]; s.n = pr.landingN ? [...pr.landingN] : null;
      s.alpha = Math.min(1, s.alpha + dt / .3);
    }
    for (const [id, s] of landingStates) {
      if (!s.seen) s.alpha = Math.max(0, s.alpha - dt / .3);
      if (!s.seen && s.alpha <= 0) { landingStates.delete(id); continue; }
      if (mi < marks.length) ring(marks[mi++], s.p, s.n, 1.65, .7 * s.alpha);
    }
    for (const g of view?.guards || []) {
      const k = karts[g.racer], mesh = guards[g.racer]; if (!k?.st || !mesh) continue;
      const v = guardLook(g.k, quiet); point(mesh, [k.st[S.posX], k.st[S.posY] + .8, k.st[S.posZ]]);
      mesh.quaternion.set(k.st[S.quatX], k.st[S.quatY], k.st[S.quatZ], k.st[S.quatW]);
      mesh.scale.setScalar(v.radius); mesh.visible = v.alpha > .001; mesh.material.opacity = v.alpha; mesh.material.color.set(palette());
    }
    for (let i = 0; i < Math.min(karts.length, 8); i++) {
      const k = karts[i], g = slow[i], active = k?.fxv?.effect === 'bigSpiral';
      slowAge[i] = active ? slowAge[i] + dt : 0;
      slowFade[i] = active ? Math.min(1, slowAge[i] / .3) : Math.max(0, slowFade[i] - dt / .3);
      if (!k?.st || slowFade[i] <= .001) continue;
      point(g, [k.st[S.posX], k.st[S.posY] + .16, k.st[S.posZ]]);
      ROT.set(k.st[S.quatX], k.st[S.quatY], k.st[S.quatZ], k.st[S.quatW]);
      g.quaternion.copy(ROT); g.rotateX(-Math.PI / 2); g.scale.setScalar(3.4); g.visible = true; g.userData.toyAlpha = .72 * slowFade[i];
    }
    for (let i = effects.length - 1; i >= 0; i--) {
      const e = effects[i]; e.age += dt; if (e.age >= e.duration) { effects.splice(i, 1); continue; }
      if (mi >= marks.length) continue;
      const snap = e.name === 'snap-burst', a = toyEnvelope(e.age, e.duration, quiet);
      const state = snap ? snapRing(e.age, quiet) : { radius: e.radius * (quiet ? 1 : .4 + .6 * Math.min(1, e.age / .4)), alpha: a };
      ring(marks[mi++], e.p, e.n, state.radius, state.alpha * (e.bright ? 1 : .4), e.bright, e.col);
      if (e.name === 'disc-bounce') {
        const mark = marks[mi - 1]; mark.ring.visible = false;
        mark.stars.position.fromArray(e.p); mark.stars.rotation.x = Math.PI / 2;
      }
      if (e.name === 'item-flyby') {
        const mark = marks[mi - 1];
        N.fromArray(e.v || [0, 0, -1]); if (N.lengthSq() < .01) N.set(0, 0, -1); N.normalize();
        for (const mesh of [mark.ring, mark.stars]) {
          mesh.position.fromArray(e.p); mesh.quaternion.setFromUnitVectors(UP, N);
        }
      }
    }
  }
  function event(name, e = {}) {
    if (!['disc-bounce', 'snap-burst', 'item-hit', 'item-fire', 'item-flyby', 'tether-hook'].includes(name) || !e.p) return;
    if (name === 'item-fire' && !TOYS.has(e.id)) return;
    if (name === 'item-hit' && !['pendulum','disc','bigSpiral','melt','snap','surge'].includes(e.kind)) return;
    const quiet = !!feel.get('gentle.on') || (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
    const bright = !quiet && !!photosafe?.request('item-toy');
    if (effects.length >= 16) effects.shift();
    effects.push({ name, age: 0, p: [...e.p], n: e.n ? [...e.n] : null, v: e.v ? [...e.v] : null, col: palette(), bright,
      radius: name === 'disc-bounce' ? 1.7 : name === 'item-flyby' ? 1.5 : name === 'item-fire' ? 3.2 : e.kind === 'bigSpiral' ? 4.1 : 3,
      duration: name === 'snap-burst' ? (quiet ? .9 : .6) : quiet ? .9 : .72 });
  }
  function clear() {
    time = 0; effects.length = 0; landingStates.clear(); slowAge.fill(0); slowFade.fill(0);
    for (const rows of pools.values()) for (const e of rows) e.carrier.visible = false;
    for (const m of marks) { m.ring.visible = false; m.stars.visible = false; }
    for (const g of [...guards, ...slow]) g.visible = false;
  }
  return { begin, place, event, clear,
    dispose() {
      root.remove(group); atlas.dispose(); loom.dispose(); ringGeo.dispose(); stars.dispose(); iconGeo.dispose();
      for (const rows of pools.values()) for (const e of rows) for (const m of e.mats) m.dispose();
      for (const m of marks) { m.ring.material.dispose(); m.stars.material.dispose(); }
      for (const g of guards) g.material.dispose();
    } };
}
