import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8'))), field = track.fields[0];
for (const speed of [0, 8, 18]) for (const lateral of [-11, 0, 11]) for (const hopLead of [0, 4, 7, 10]) {
  test('Broad Keep decks: throttle from ' + speed + 'm/s at x' + lateral + ', hop ' + hopLead + 'm before lips', () => {
    const st = place(track, { s: field.fromS, speed, lateral }), ev = recorder(), feel = makeFeel(), a = acts({ throttle: 1 });
    const hopped = new Set();
    for (let tick = 0; tick < 60 * 35 && st[S.s] < field.toS + 5; tick++) {
      a.hopPressed = false;
      if (hopLead) for (const stone of field.stones.slice(0, -1)) {
        const until = stone.s + stone.len - st[S.s];
        if (until >= 0 && until <= hopLead && !hopped.has(stone.id)) { a.hopPressed = true; hopped.add(stone.id); break; }
      }
      ev.t += DT; step(st, a, track, feel, DT, ev); updateRespawn(st, track, feel, DT, ev);
    }
    assert.ok(st[S.s] >= field.toS + 5, 'crosses whole field');
    assert.equal(ev.all('drop').length, 0, 'no fall');
    assert.equal(ev.all('void').length, 0, 'never sinks below a gap');
    assert.ok(ev.all('stone').length >= 8, 'lands on broad decks');
  });
}
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { buildStones, stoneLayout } = await import('../render/stones.js');
const { Group, Mesh, BoxGeometry, MeshStandardMaterial, Vector3, Matrix4 } = await import('three/webgpu');
hooks.deregister();
function fixture() {
  const scene = new Group(), material = new MeshStandardMaterial(); material.name = 'surface';
  const geometry = new BoxGeometry(5, .55, 5).translate(0, .275, 0); scene.add(new Mesh(geometry, material));
  return { art: { model: id => id === 'stone-keep' ? { scene } : null }, geometry, material };
}
test('Broad decks render one coherent scaled platform per collision row and fallback agrees', () => {
  const rows = stoneLayout(track), f = fixture(), view = buildStones(track, f.art), fallback = buildStones(track, null);
  assert.equal(rows.length, 9); assert.equal(view.group.children.length, 9); assert.equal(fallback.group.children[0].count, 9);
  for (let i = 0; i < rows.length; i++) {
    const r = rows[i], o = view.group.children[i]; assert.equal(r.tiles, 1);
    assert.equal(o.scale.x * 5, 28); assert.equal(o.scale.z * 5, 26);
    const matrix = new Matrix4(); fallback.group.children[0].getMatrixAt(i, matrix);
    const scale = new Vector3().setFromMatrixScale(matrix);
    assert.ok(Math.abs(scale.x * 5 - 28) < .0001 && Math.abs(scale.z * 5 - 26) < .0001);
  }
  view.dispose(); fallback.dispose(); f.geometry.dispose(); f.material.dispose();
});
test('Platform landing rocks and sinks within 30cm, pause freezes, quiet mode reduces motion and restart restores', () => {
  const f = fixture(), view = buildStones(track, f.art), row = view.rows[0], object = view.group.children[0], initial = object.position.clone();
  assert.equal(view.hit({ id: row.stone }), row); let maxDepth = 0, rockSeen = false;
  for (let i = 0; i < 90; i++) {
    view.update(DT, {}); object.updateMatrix();
    for (const z of [-2.5, 2.5]) {
      const p = new Vector3(0, .55, z).applyMatrix4(object.matrix), depth = track.frameAt(row.s).p[1] - p.y;
      assert.ok(depth >= -.001 && depth <= .301, 'bounded visible movement ' + depth); maxDepth = Math.max(maxDepth, depth);
    }
    rockSeen ||= Math.abs(object.quaternion.x) > .001;
    if (i === 10) { const p = object.position.clone(), q = object.quaternion.clone(); view.update(2, { paused: true }); assert.deepEqual(object.position, p); assert.deepEqual(object.quaternion.toArray(), q.toArray()); }
  }
  assert.ok(maxDepth > .2 && rockSeen); assert.ok(object.position.distanceTo(initial) < .001);
  view.hit(row.stone); for (let i = 0; i < 20; i++) view.update(DT, { gentle: true });
  assert.ok(object.position.distanceTo(initial) <= .0401); view.reset(); assert.deepEqual(object.position, initial);
  view.hit(row.stone); view.update(.2, { reducedMotion: true }); assert.ok(object.position.distanceTo(initial) <= .0401);
  view.reset(); view.dispose(); f.geometry.dispose(); f.material.dispose();
});

test('Broad platform edge assistance is one bounded crossing, never a repeated midair launch or another course', async () => {
  const { broadPlatformEdgeKick } = await import('../sim/stones.js');
  const on = { onRoad: true, stone: { s: 100, x: 0, w: 28, len: 26 }, s: 125.8, lateral: 0, height: .1 };
  const voidQ = { onRoad: false, stone: null, hole: { lip: true }, s: 126.2, lateral: 0, height: .05 };
  assert.equal(broadPlatformEdgeKick(track, on, voidQ, -2, 3.5), 5.5);
  assert.equal(broadPlatformEdgeKick(track, voidQ, voidQ, -2, 3.5), 0);
  assert.equal(broadPlatformEdgeKick({ id: 'threshold' }, on, voidQ, -2, 3.5), 0);
  assert.equal(broadPlatformEdgeKick(track, { ...on, stone: { w: 5, len: 5 } }, voidQ, -2, 3.5), 0);
  assert.equal(broadPlatformEdgeKick(track, on, { ...voidQ, height: -1 }, -2, 3.5), 0);
  assert.equal(broadPlatformEdgeKick(track, on, { ...voidQ, height: 2 }, -2, 3.5), 0);
  assert.equal(broadPlatformEdgeKick(track, on, voidQ, 6, 3.5), 0);
  assert.equal(broadPlatformEdgeKick(track, { ...on, s: 110, lateral: 13.9 }, { ...voidQ, s: 110.2, lateral: 14.2 }, -2, 3.5), 0, 'side exit');
  assert.equal(broadPlatformEdgeKick(track, { ...on, s: 100.2 }, { ...voidQ, s: 99.8 }, -2, 3.5), 0, 'back exit');
  assert.equal(broadPlatformEdgeKick(track, on, { ...voidQ, lateral: -14.2 }, -2, 3.5), 0, 'front corner outside landing width');
  assert.equal(broadPlatformEdgeKick(track, { ...on, s: 126.4 }, voidQ, -2, 3.5), 0, 'not a forward crossing');
});

test('Keep platforms share thin bevel outlines, alternate slate and plum faces, and own only their style resources', () => {
  const f = fixture(), view = buildStones(track, f.art), holders = view.group.children;
  const outlines = holders.map(h => h.children.find(o => o.name === 'keep-platform-outline'));
  assert.ok(outlines.every(o => o?.isLineSegments && o.material.linewidth === 1));
  assert.equal(new Set(outlines.map(o => o.geometry)).size, 1);
  assert.equal(new Set(outlines.map(o => o.material)).size, 1);
  assert.equal(outlines[0].material.color.getHexString(), '0d0a14');
  const faces = holders.map(h => h.children[0].children[0].material);
  assert.equal(faces[0].color.getHexString(), '617faf'); assert.equal(faces[1].color.getHexString(), '8e557d');
  assert.equal(f.material.color.getHexString(), 'ffffff', 'source material is unchanged');
  const line = outlines[0], local = line.position.clone();
  holders[0].updateMatrixWorld(true); const before = new Vector3().setFromMatrixPosition(line.matrixWorld);
  view.hit(view.rows[0].stone); view.update(.22); view.group.updateMatrixWorld(true);
  const after = new Vector3().setFromMatrixPosition(line.matrixWorld);
  assert.ok(before.y - after.y > .27); assert.deepEqual(line.position, local, 'outline inherits platform motion');
  view.reset(); view.group.updateMatrixWorld(true);
  assert.ok(new Vector3().setFromMatrixPosition(line.matrixWorld).distanceTo(before) < .0001);
  let geometryDisposed = 0, inkDisposed = 0, sourceDisposed = 0;
  line.geometry.addEventListener('dispose', () => geometryDisposed++);
  line.material.addEventListener('dispose', () => inkDisposed++);
  f.geometry.addEventListener('dispose', () => sourceDisposed++);
  view.dispose(); view.dispose(); assert.equal(geometryDisposed, 1); assert.equal(inkDisposed, 1); assert.equal(sourceDisposed, 0);
  f.geometry.dispose(); f.material.dispose();
});
