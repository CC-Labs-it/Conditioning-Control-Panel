import { Group, InstancedMesh, ShapeGeometry, Shape, PlaneGeometry, BufferGeometry, Float32BufferAttribute,
  MeshBasicNodeMaterial, DoubleSide, Object3D, Color, Vector3, DynamicDrawUsage } from 'three/webgpu';
import { rng, hashId } from './cueKit.js';
import { matrixInto } from './instanceWrite.js';

// BURST: world particles for hearts, glass shards, falling road tiles and confetti. One pooled instanced mesh
// per shape, no per-burst allocation. Gentle halves the count and drops spin. Particles never collide.
const CAP = { hearts: 160, shards: 220, tiles: 120, confetti: 260 };
function heart() {
  const s = new Shape(); s.moveTo(0, -.6);
  s.bezierCurveTo(-.9, -.05, -.75, .65, -.25, .62); s.bezierCurveTo(-.05, .6, 0, .42, 0, .35);
  s.bezierCurveTo(0, .42, .05, .6, .25, .62); s.bezierCurveTo(.75, .65, .9, -.05, 0, -.6);
  return new ShapeGeometry(s, 6);
}
function shard() {
  const g = new BufferGeometry(); g.setAttribute('position', new Float32BufferAttribute([-.5, -.4, 0, .55, -.2, 0, -.05, .7, 0], 3));
  g.computeVertexNormals(); return g;
}
export const BURST_PALETTES = Object.freeze({
  hearts: ['#ff2f8f', '#ff74b8', '#ffd1e8', '#e0115f'], shards: ['#7dff9b', '#c9ffd6', '#2cff6a', '#effff2'],
  tiles: ['#ff4fb0', '#ffc1e6', '#b45cff', '#ffffff'], confetti: ['#ff4fb0', '#7af3ff', '#fff27a', '#b45cff', '#ffffff'],
});

export function createCueParticles(scene) {
  const object = new Group(); object.name = 'cue-particles'; scene.add(object);
  const geos = { hearts: heart(), shards: shard(), tiles: new PlaneGeometry(1, 1), confetti: new PlaneGeometry(.6, 1) };
  const pools = {}, dummy = new Object3D(), color = new Color();
  for (const kind of Object.keys(CAP)) {
    const material = new MeshBasicNodeMaterial({ side: DoubleSide, fog: false });
    const mesh = new InstancedMesh(geos[kind], material, CAP[kind]);
    mesh.instanceMatrix.setUsage(DynamicDrawUsage); mesh.frustumCulled = false; mesh.count = 0;
    for (let i = 0; i < CAP[kind]; i++) mesh.setColorAt(i, color.set('#ffffff'));
    object.add(mesh);
    pools[kind] = { mesh, material, parts: Array.from({ length: CAP[kind] }, () => ({ life: 0, age: 0, p: new Vector3(), v: new Vector3(),
      spin: new Vector3(), r: new Vector3(), size: 1, shown: true })), next: 0 };
  }
  let seq = 0;
  /** Emit count particles from origin with base velocity dir (m/s), a cone spread and an upward kick. */
  function burst(kind, origin, { count = 40, dir = [0, 1, 0], speed = 14, spread = .6, up = 6, size = 1, life = 2.6,
    gravity = 14, palette = BURST_PALETTES[kind], gentle = false, seed = ++seq } = {}) {
    const pool = pools[kind]; if (!pool) return 0;
    const r = rng(hashId(kind + seed)), n = Math.max(1, Math.round(count * (gentle ? .5 : 1)));
    for (let i = 0; i < n; i++) {
      const q = pool.parts[pool.next]; pool.next = (pool.next + 1) % pool.parts.length;
      q.p.set(origin[0] ?? origin.x, origin[1] ?? origin.y, origin[2] ?? origin.z);
      q.v.set(dir[0] + (r() - .5) * 2 * spread, dir[1] + (r() - .5) * 2 * spread, dir[2] + (r() - .5) * 2 * spread)
        .normalize().multiplyScalar(speed * (.6 + r() * .6)); q.v.y += up * (.5 + r());
      q.spin.set(gentle ? 0 : (r() - .5) * 9, gentle ? 0 : (r() - .5) * 9, gentle ? 0 : (r() - .5) * 6);
      q.r.set(r() * 6, r() * 6, r() * 6); q.size = size * (.6 + r() * .8); q.age = 0; q.life = life * (.75 + r() * .5); q.g = gravity;
      pool.mesh.setColorAt(pool.parts.indexOf(q), color.set(palette[Math.floor(r() * palette.length)]));
    }
    pool.mesh.instanceColor.needsUpdate = true;
    return n;
  }
  // A finished particle writes its zero-scale matrix once; a pool with nothing alive and nothing left to
  // hide does no matrix work and no upload (the four pools idle through most of a race).
  function update(dt, { paused = false } = {}) {
    if (paused) return;
    const step = Math.min(.1, Math.max(0, dt || 0));
    for (const kind in pools) {
      const pool = pools[kind], parts = pool.parts, array = pool.mesh.instanceMatrix.array;
      let live = 0, wrote = false;
      for (let i = 0; i < parts.length; i++) {
        const q = parts[i];
        if (q.age >= q.life) {
          if (!q.shown) continue;
          dummy.scale.setScalar(0); q.shown = false;
        } else {
          q.age += step; q.v.y -= q.g * step; q.v.multiplyScalar(1 - .35 * step); q.p.addScaledVector(q.v, step);
          q.r.addScaledVector(q.spin, step); live++; q.shown = true;
          const k = q.age / q.life, s = q.size * (k < .08 ? k / .08 : k > .7 ? Math.max(0, (1 - k) / .3) : 1);
          dummy.position.copy(q.p); dummy.rotation.set(q.r.x, q.r.y, q.r.z); dummy.scale.setScalar(s);
        }
        dummy.updateMatrix(); matrixInto(array, i, dummy.matrix); wrote = true;
      }
      pool.mesh.count = live ? parts.length : 0;
      if (wrote) pool.mesh.instanceMatrix.needsUpdate = true;
    }
  }
  function clear() { for (const pool of Object.values(pools)) { for (const q of pool.parts) q.age = q.life = 0; pool.mesh.count = 0; } }
  return { object, burst, update, clear,
    get stats() { const out = {}; for (const [k, p] of Object.entries(pools)) out[k] = p.parts.filter(q => q.age < q.life).length; return out; },
    dispose() { clear(); object.removeFromParent(); for (const p of Object.values(pools)) { p.mesh.dispose(); p.material.dispose(); } for (const g of Object.values(geos)) g.dispose(); } };
}
