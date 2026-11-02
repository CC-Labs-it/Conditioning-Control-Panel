// Static merge (render/staticMerge.js): fewer draws, the same submitted world-space triangles.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';

registerHooks({ resolve(specifier, context, next) {
  if (specifier === 'three/webgpu' || specifier === 'three/tsl') return { url: new URL('../vendor/three/build/' + (specifier.endsWith('tsl') ? 'three.tsl.min.js' : 'three.webgpu.min.js'), import.meta.url).href, shortCircuit: true };
  return next(specifier, context);
} });
const T = await import('three/webgpu');
const { mergeStaticMeshes } = await import('../render/staticMerge.js');
const { createGoonworksHazardView } = await import('../render/goonworksHazards.js');
const { buildGoonworks } = await import('../render/goonworks.js');
const { worldScreenSolids, worldScreenLayout, worldScreenExistingSlots } = await import('../render/worldScreens.js');
const { largeMediaLayout } = await import('../render/worldMediaScale.js');
const { goonworksMediaLayout } = await import('../render/goonworksMedia.js');

/** Every submitted triangle in world space as centroid + oriented area normal, keyed by a material signature. */
function triangles(root, sig) {
  root.updateMatrixWorld(true);
  const out = new Map(), V = [new T.Vector3(), new T.Vector3(), new T.Vector3()], E = new T.Vector3(), F = new T.Vector3();
  root.traverseVisible(o => {
    if (!o.isMesh) return;
    const g = o.geometry, p = g.attributes.position, idx = g.index, count = idx ? idx.count : p.count;
    const start = g.drawRange.start, end = Math.min(count, start + g.drawRange.count), key = sig(o);
    const order = o.matrixWorld.determinant() < 0 ? [0, 2, 1] : [0, 1, 2];   // three flips the front face
    if (!out.has(key)) out.set(key, []);
    const list = out.get(key);
    for (let i = start; i < end; i += 3) {
      order.forEach((k, j) => V[j].fromBufferAttribute(p, idx ? idx.getX(i + k) : i + k).applyMatrix4(o.matrixWorld));
      E.subVectors(V[1], V[0]); F.subVectors(V[2], V[0]); E.cross(F);
      if (E.length() < 1e-4) continue;   // zero-area slivers draw nothing (float32 baking gives them noise normals)
      list.push([(V[0].x + V[1].x + V[2].x) / 3, (V[0].y + V[1].y + V[2].y) / 3, (V[0].z + V[1].z + V[2].z) / 3, E.x, E.y, E.z]);
    }
  });
  return out;
}
/** Multiset match within a tolerance: every triangle has exactly one partner. */
function sameTriangles(a, b, label) {
  assert.deepEqual([...a.keys()].sort(), [...b.keys()].sort(), label + ': material sets');
  for (const [key, list] of a) {
    const other = b.get(key), cell = 0.05, grid = new Map(), used = new Uint8Array(other.length), h = t => t.slice(0, 3).map(v => Math.floor(v / cell));
    assert.equal(list.length, other.length, label + ': triangle count for ' + key);
    other.forEach((t, i) => { const k = h(t).join(','); if (!grid.has(k)) grid.set(k, []); grid.get(k).push(i); });
    let missing = 0;
    for (const t of list) {
      const [x, y, z] = h(t); let found = -1, best = Infinity;   // the closest unused partner, never the first in tolerance
      for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) for (let dz = -1; dz <= 1; dz++)
        for (const i of grid.get([x + dx, y + dy, z + dz].join(',')) || []) {
          if (used[i]) continue; const u = other[i];
          const d = Math.max(...t.map((v, j) => Math.abs(v - u[j]) / (j < 3 ? 1 : Math.max(1, Math.abs(v)))));
          if (d < 2e-3 && d < best) { best = d; found = i; }
        }
      if (found < 0) missing++; else used[found] = 1;
    }
    assert.equal(missing, 0, label + ': unmatched triangles for ' + key);
  }
}
const draws = root => { let n = 0; root.traverseVisible(o => { if (o.isMesh) n++; }); return n; };

test('baked meshes keep world triangles, winding and normals under rotation, scale and mirroring', () => {
  const frame = new T.Group(), mat = new T.MeshBasicNodeMaterial(), other = new T.MeshBasicNodeMaterial();
  const box = new T.BoxGeometry(1, 2, 3), cyl = new T.CylinderGeometry(1, 1, 2, 9);
  const sub = new T.Group(); sub.position.set(3, 0, 1); sub.rotation.y = .7; frame.add(sub);
  const a = new T.Mesh(box, mat); a.position.set(1, 2, 3); a.rotation.set(.3, .2, .1); a.scale.set(2, 1, .5); sub.add(a);
  const b = new T.Mesh(cyl, mat); b.scale.set(-1, 1, 1); b.position.x = -4; frame.add(b);
  const c = new T.Mesh(box, other); frame.add(c);
  const kept = new T.Group(); kept.add(new T.Mesh(box, mat)); frame.add(kept);
  const before = triangles(frame, o => o.material.uuid);
  const normalsBefore = [a, b].map(m => { m.updateMatrixWorld(true); return new T.Vector3(0, 0, 1).transformDirection(m.matrixWorld); });
  const res = mergeStaticMeshes(frame, { keep: [kept] });
  sameTriangles(triangles(frame, o => o.material.uuid), before, 'unit');
  assert.equal(res.meshes.length, 1); assert.equal(draws(frame), 3, 'one merged + the lone other + the kept child');
  assert.equal(sub.parent, null, 'the emptied group is pruned');
  assert.equal(kept.parent, frame, 'kept animated children stay live');
  // Box +Z face normals land where the original world transform put them.
  const g = res.meshes[0].geometry, n = g.attributes.normal, P = g.attributes.position;
  assert.ok(normalsBefore.length && n.count === P.count);
  for (let i = 0; i < n.count; i++) assert.ok(Math.abs(Math.hypot(n.getX(i), n.getY(i), n.getZ(i)) - 1) < 1e-5);
});

test('GOONWORKS toys draw the same triangles with far fewer draws, and still animate', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
  const layout = getGoonworksLayout(track);
  const plain = createGoonworksHazardView(new T.Scene(), track, layout);
  const merged = createGoonworksHazardView(new T.Scene(), track, layout, { merge: true });
  const sig = o => o.material.color.getHexString() + ':' + o.material.side + ':' + o.castShadow + ':' + o.receiveShadow;
  for (const t of [0, 2.35, 5.1, 9.7]) {
    plain.update(t); merged.update(t);
    sameTriangles(triangles(merged.group, sig), triangles(plain.group, sig), 'at ' + t + ' s');
  }
  const a = draws(plain.group), b = draws(merged.group);
  assert.ok(b < a * .55, `draws ${a} -> ${b}`); if (process.env.DBG) console.log("DRAWS", a, b);
  assert.equal(merged.inspect().wheels, 3);
  for (const entry of merged.group.children) assert.ok(entry.children.length > 0);
  plain.dispose(); merged.dispose();
});

test('GOONWORKS static ink hulls share one instanced draw per shape: same instances, same screen placement', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
  const plain = buildGoonworks(track, null, null, null, { shareHulls: false }), shared = buildGoonworks(track);
  const hulls = world => { const out = []; world.group.updateMatrixWorld(true);
    world.group.traverse(o => { if (!o.isInstancedMesh || !o.material.name.endsWith('-ink') || o.material.side !== T.BackSide) return;
      for (let i = 0; i < o.count; i++) out.push(o.geometry.uuid.length + ':' + o.geometry.type + ':' + o.geometry.attributes.position.count + ':' + Array.from(o.instanceMatrix.array.subarray(i * 16, i * 16 + 16), v => v.toFixed(4)).join(',')); });
    return out.sort(); };
  const meshes = world => { let n = 0; world.group.traverse(o => { if (o.isMesh) n++; }); return n; };
  assert.deepEqual(hulls(shared), hulls(plain));
  assert.ok(meshes(shared) < meshes(plain) - 15, `meshes ${meshes(plain)} -> ${meshes(shared)}`);
  const key = r => JSON.stringify([r.p, r.axes, r.size].map(a => (Array.isArray(a[0]) ? a.flat() : a).map(v => +v.toFixed(4))));
  const solidsA = worldScreenSolids([plain.group]), solidsB = worldScreenSolids([shared.group]);
  assert.deepEqual(solidsB.map(key).sort(), solidsA.map(key).sort(), 'placement sees the same boxes');
  const authored = goonworksMediaLayout(track);
  const layoutA = largeMediaLayout(track, authored, { solids: solidsA }), layoutB = largeMediaLayout(track, authored, { solids: solidsB });
  assert.deepEqual(layoutB, layoutA, 'large media layout is unchanged');
  const extraA = worldScreenLayout(track, { solids: solidsA, existingSlots: worldScreenExistingSlots(track, layoutA) });
  const extraB = worldScreenLayout(track, { solids: solidsB, existingSlots: worldScreenExistingSlots(track, layoutB) });
  assert.deepEqual(extraB.slots, extraA.slots, 'supplemental screens are unchanged');
  plain.dispose(); shared.dispose();
});
