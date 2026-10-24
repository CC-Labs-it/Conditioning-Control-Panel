import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN, cloneState } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { boudoirBoxesIntersect } from '../render/boudoirMedia.js';
import { roadSamples } from '../render/mediaMath.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { BOUDOIR_HAZARD as K, getBoudoirSetpieces, powderPressPhase, mascaraHeads, mascaraPhase, mirrorGatePhase, ribbonPhase } from '../track/boudoir-setpieces.js';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
const track = fakeTrack({ w: 36 }), course = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const press = { id: 'puff', kind: 'press', s: 110, x: 0, w: 14, depthM: 8, offsetS: 0 };
const sweeper = { id: 'brush', kind: 'sweeper', s: 110, x: 0, w: 8.1, depthM: 2.5, offsetS: 0 };
const ribbon = { id: 'belt', kind: 'conveyor', s0: 80, s1: 160, x: 0, w: 12, offsetS: 0 };
const mirror = lane => ({ id: 'mirror-' + lane, kind: 'mirror', s: 110, x: [-9, 0, 9][lane], w: 7, depthM: 1.2, lane, sequence: 0, offsetS: 0 });
const plan = (over = {}) => ({ presses: [], sweepers: [], conveyors: [], mirrorGates: [], ...over });
const racer = (s, x = 0, height = 0, speed = 30) => ({ i: 0, st: place(track, { s, lateral: x, height, speed }), events: recorder(), feel: makeFeel() });
const trapBounds = () => [...getBoudoirSetpieces(course).presses, ...getBoudoirSetpieces(course).sweepers].flatMap(row => {
  const f = course.frameAt(row.s), bound = (name, x, y, size, z = 0) => ({ name: row.id + '/' + name, s: row.s,
    p: f.p.map((v, i) => v + f.b[i] * x + f.n[i] * y + f.t[i] * z), axes: [f.b, f.n, f.t], size });
  const boxes = [];
  if (row.kind === 'press') boxes.push(bound('left-post', -f.w / 2 - 2, 8, [2, 16, 3]), bound('right-post', f.w / 2 + 2, 8, [2, 16, 3]),
    bound('top', 0, 16, [f.w + 6, 2, 3]), bound('puff-travel', 0, 6.7, [14, 13.4, 8]), bound('piston', 0, 13.6, [1, 5, 1]));
  else {
    const side = row.offsetS % 2 > 1 ? 1 : -1;
    boxes.push(bound('left-post', -f.w / 2 - 2, 6.45, [2.6, 12.9, 2.6]), bound('right-post', f.w / 2 + 2, 6.45, [2.6, 12.9, 2.6]),
      bound('top', 0, 11.5, [f.w + 6.2, 2.8, 1.3]), bound('bottle', side * (f.w / 2 + 6), 4.8, [4.8, 9.6, 4.8]));
    for (const head of row.heads) {
      const low = head.pivotY - head.armM, high = head.pivotY - Math.sqrt(head.armM ** 2 - head.amplitudeM ** 2);
      boxes.push(bound(head.id + '-travel', head.x, (low + high) / 2, [2 * (head.amplitudeM + head.radiusM), high - low + 2 * head.radiusM, head.depthM], head.longitudinalM),
        bound(head.id + '-arm', head.x, head.pivotY - head.armM / 2, [head.amplitudeM * 2 + .5, head.armM, .5], head.longitudinalM),
        bound(head.id + '-pivot', head.x, head.pivotY, [1.4, 1.4, 1.4], head.longitudinalM),
        bound(head.id + '-hanger', head.x, 10.9, [.7, 2, Math.abs(head.longitudinalM) + 1.4], head.longitudinalM / 2));
    }
  }
  return boxes;
});

test('Both long spirals contain four staggered sweepers with generous stable outside lanes', () => {
  const layout = getBoudoirLayout(course), rows = getBoudoirSetpieces(course).sweepers;
  assert.equal(rows.length, 14);
  assert.equal(rows.flatMap(mascaraHeads).length, 35);
  assert.deepEqual([...new Set(rows.map(row => row.heads.length))].sort(), [2, 3]);
  for (const id of ['shelf-climb', 'wardrobe-descent']) {
    const span = layout.landmarks.find(p => p.id === id), spiral = rows.filter(row => row.s > span.s0 && row.s < span.s1);
    assert.equal(spiral.length, 4);
    for (const row of spiral) assert.ok((course.frameAt(row.s).w - row.w) / 2 >= 7, 'at least 7 m on each stable outside path');
  }
});
test('Independent pendulums follow true fixed-length arcs and leave clear outer paths through every phase', () => {
  for (const row of getBoudoirSetpieces(course).sweepers) {
    assert.equal(new Set(row.heads.map(head => head.cycleS)).size, row.heads.length);
    for (let t = 0; t < 30; t += .125) for (const head of row.heads) {
      const pose = mascaraPhase(head, t);
      assert.ok(Math.abs(Math.hypot(pose.x - head.x, pose.y - head.pivotY) - head.armM) < 1e-9);
      assert.ok(Math.abs(pose.x) + pose.radiusM <= row.w / 2, 'station width contains the entire moving brush');
      assert.ok(pose.y - pose.radiusM > .4, 'brush never intersects the road');
      for (const other of row.heads) if (other !== head) assert.ok(Math.abs(pose.z - other.longitudinalM) > (pose.depthM + other.depthM) / 2, 'independent brushes cannot interpenetrate');
    }
  }
});
test('All trap gantries, bottles and full moving envelopes clear every other road level', () => {
  const hits = [], road = roadSamples(course, 4);
  for (const box of trapBounds()) for (const f of road) {
    const delta = Math.abs(box.s - f.s), near = Math.min(delta, course.lengthM - delta) < 32;
    if (near) continue;
    const corridor = { p: f.p.map((v, i) => v + f.n[i] * 1.5), size: [f.w + 2, 5, 5], axes: [f.b, f.n, f.t] };
    if (boudoirBoxesIntersect(box, corridor)) hits.push(box.name + ' / ' + f.s.toFixed(1));
  }
  assert.deepEqual(hits, []);
});
test('Both shortcut flights clear the full animated trap envelopes at slow, normal and fast entry', () => {
  const boxes = trapBounds(), hits = [];
  for (const route of course.routes) for (const speed of [20, 30, 40]) {
    const s = route.openS0 + 3, st = place(course, { s, lateral: course.frameAt(s).w / 2 - 1, speed, headingDeg: 15 });
    const events = recorder(), feel = makeFeel(), action = acts({ throttle: 1 });
    for (let i = 0; i < 420 && !events.all('route-land').length; i++) {
      step(st, action, course, feel, DT, events); updateRespawn(st, course, feel, DT, events);
      const kart = { p: [st[S.posX], st[S.posY] + 1, st[S.posZ]], size: [3, 3, 3] };
      for (const box of boxes) if (boudoirBoxesIntersect(kart, box)) hits.push(route.id + '/' + speed + ':' + box.name);
    }
    assert.ok(events.all('route-land').length, 'physical shortcut landing');
  }
  assert.deepEqual([...new Set(hits)], []);
});

test('All Boudoir mechanics expose deterministic poses, warnings and a permanently open mirror lane', () => {
  assert.equal(powderPressPhase(press, 3.4).phase, 'warn'); assert.equal(powderPressPhase(press, 3.4).blocking, false);
  assert.equal(powderPressPhase(press, 6).blocking, true);
  assert.equal(ribbonPhase(ribbon, 1).direction, 1); assert.equal(ribbonPhase(ribbon, 6).phase, 'warn'); assert.equal(ribbonPhase(ribbon, 9).direction, -1);
  for (let t = 0; t < 40; t += .1) {
    const rows = [0, 1, 2].map(lane => mirrorGatePhase(mirror(lane), t)); assert.ok(rows.some(r => r.liftM >= 8.39), 'full open lane at ' + t);
    assert.deepEqual(mascaraPhase(sweeper, t), mascaraPhase(sweeper, t));
  }
});
test('Powder press blocks a fast real crossing but warning, wide bypasses and high flight remain clear', () => {
  for (const [x, time, height, expected] of [[0, 6, 0, true], [-12, 6, 0, false], [12, 6, 0, false], [0, 3.4, 0, false], [0, 6, 7, false]]) {
    const h = createBoudoirHazards(track, plan({ presses: [press] })), r = racer(119, x, height), prev = place(track, { s: 101, lateral: x, height, speed: 40 });
    assert.equal(!!h.apply(r, prev, time), expected);
    if (expected) { assert.ok(r.st[S.s] < 106); assert.equal(r.events.all('boudoir-press-hit').length, 1); assert.ok(r.st[S.stalled] <= .351); }
  }
});
test('Sustained throttle cannot pass a held powder press, and a racer already below may leave', () => {
  const h = createBoudoirHazards(track, plan({ presses: [press] })), r = racer(104), a = acts({ throttle: 1 });
  for (let i = 0; i < 60; i++) { const prev = cloneState(r.st); step(r.st, a, track, r.feel, DT, r.events); h.apply(r, prev, 6 + i * DT); assert.ok(r.st[S.s] < 106); }
  assert.ok(r.events.all('boudoir-press-hit').length <= 2);
  const under = racer(116); h.reset(); assert.equal(h.apply(under, place(track, { s: 110, speed: 30 }), 6), null);
});
test('Sweeping mascara catches a fast pass and applies a bounded real lateral impulse', () => {
  const h = createBoudoirHazards(track, plan({ sweepers: [sweeper] })), r = racer(117), prev = place(track, { s: 103, speed: 40 });
  assert.equal(h.apply(r, prev, .01), 'brush'); const f = track.frameAt(110), sideV = f.b.reduce((sum, v, i) => sum + v * r.st[S.velX + i], 0);
  assert.ok(sideV > 5 && sideV < 5.3); assert.ok(r.st[S.engineV] < 22); assert.equal(r.events.all('boudoir-sweeper-hit').length, 1);
  for (const x of [-14, 14]) { const bypass = racer(117, x); assert.equal(h.apply(bypass, place(track, { s: 103, lateral: x, speed: 40 }), .01), null); }
});
test('A moving brush can hit a stationary kart, but does not repeat during cooldown', () => {
  const h = createBoudoirHazards(track, plan({ sweepers: [sweeper] })), r = racer(110, 3.1, 0, 0);
  let hit = false;
  for (let i = 0; i < 70; i++) { const previous = cloneState(r.st); hit = !!h.apply(r, previous, i * DT) || hit; }
  assert.equal(hit, true); assert.equal(r.events.all('boudoir-sweeper-hit').length, 1);
});
test('Every independent head collides at its rendered location, while outside lanes and one station cooldown stay fair', () => {
  const station = getBoudoirSetpieces(course).sweepers[1], local = { ...station, s: 110,
    heads: station.heads.map(head => ({ ...head, s: 110 })) };
  for (const head of local.heads) {
    const pose = mascaraPhase(head, 1), h = createBoudoirHazards(track, plan({ sweepers: [{ ...local, heads: [head] }] }));
    const r = racer(119, pose.x), prev = place(track, { s: 101, lateral: pose.x, speed: 40 });
    assert.equal(h.apply(r, prev, 1), station.id); assert.equal(r.events.all('boudoir-sweeper-hit')[0].payload.headId, head.id);
    assert.equal(r.events.all('boudoir-sweeper-hit')[0].payload.impulseMps, K.brushImpulseMps);
  }
  for (const x of [-13, 13]) for (let time = .1; time < 12; time += .3) {
    const h = createBoudoirHazards(track, plan({ sweepers: [local] })), r = racer(119, x);
    assert.equal(h.apply(r, place(track, { s: 101, lateral: x, speed: 40 }), time), null);
  }
  const heads = [{ ...sweeper, id: 'first', stationId: 'pair', longitudinalM: -2.5 }, { ...sweeper, id: 'second', stationId: 'pair', longitudinalM: 2.5 }];
  const h = createBoudoirHazards(track, plan({ sweepers: [{ ...sweeper, id: 'pair', heads }] })), r = racer(109);
  assert.equal(h.apply(r, place(track, { s: 104, speed: 30 }), .01), 'pair');
  const previous = cloneState(r.st); r.st.set(place(track, { s: 116, speed: 30 }));
  assert.equal(h.apply(r, previous, .02), null); assert.equal(r.events.all('boudoir-sweeper-hit').length, 1);
});
test('Mirror lane shutters use the shared visible lift and always allow their open choice', () => {
  for (const lane of [0, 1, 2]) {
    const rows = [0, 1, 2].map(mirror), h = createBoudoirHazards(track, plan({ mirrorGates: rows })), x = [-9, 0, 9][lane], r = racer(115, x);
    assert.equal(!!h.apply(r, place(track, { s: 105, lateral: x, speed: 40 }), 3), lane !== 0);
  }
});
test('Conveyor centre accelerates or brakes actual velocity while its stable outside lane stays unchanged', () => {
  for (const [time, x, sign] of [[1, 0, 1], [9, 0, -1], [1, 10, 0], [9, 10, 0]]) {
    const h = createBoudoirHazards(track, plan({ conveyors: [ribbon] })), r = racer(110, x, 0, 28), previous = cloneState(r.st), start = r.st[S.engineV];
    h.apply(r, previous, time); assert.equal(Math.sign(r.st[S.engineV] - start), sign);
    if (sign) assert.equal(r.events.all('boudoir-ribbon').length, 1);
  }
  const h = createBoudoirHazards(track, plan({ conveyors: [ribbon] })), boosted = racer(110, 0, 0, 55), old = cloneState(boosted.st);
  h.apply(boosted, old, 1); assert.equal(boosted.st[S.engineV], 55, 'forward belt preserves an already faster boost');
});
test('Taking a forward ribbon makes physical progress faster than its stable outside bypass', () => {
  const finish = [];
  for (const x of [0, 10]) {
    const h = createBoudoirHazards(track, plan({ conveyors: [{ ...ribbon, s0: 50, s1: 250 }] })), r = racer(80, x, 0, 28), action = acts({ throttle: 1 });
    for (let i = 0; i < 120; i++) { const prev = cloneState(r.st); step(r.st, action, track, r.feel, DT, r.events); h.apply(r, prev, 1 + i * DT); }
    finish.push(r.st[S.s]);
  }
  assert.ok(finish[0] - finish[1] > 8, 'central ribbon gains real distance: ' + finish);
});
test('Reset, frozen clocks, discontinuous clock seeks, remote and rescued racers do not replay trap hits', () => {
  const h = createBoudoirHazards(track, plan({ presses: [press] })), r = racer(119), previous = place(track, { s: 101, speed: 40 });
  h.apply(r, previous, 6); const state = [...r.st]; h.apply(r, previous, 6); assert.deepEqual([...r.st], state);
  h.reset(); assert.equal(h.inspect().hits, 0);
  for (const mutate of [r => r.kind = 'remote', r => r.finished = true, r => r.st[S.respawnPhase] = RESPAWN.PULL]) { const a = racer(119); mutate(a); assert.equal(h.apply(a, previous, 6), null); }
  const a = racer(101); h.apply(a, cloneState(a.st), 1); a.st.set(racer(119).st); assert.equal(h.apply(a, previous, 6), null);
});
for (const seed of [241024, 42]) test('All eight racers finish two Boudoir laps with every physical hazard active, seed ' + seed, () => {
  const r = makeRace({ track: course, seed, items: createItems }), h = createBoudoirHazards(course);
  const drive = r.ai.drive.bind(r.ai), original = r.racers.step.bind(r.racers);
  r.ai.drive = (kart, ctx, dt, out) => { drive(kart, ctx, dt, out); if (ctx.race.phase === 'racing') h.steer(kart, out); };
  r.racers.step = (dt, a, race, ctx) => original(dt, a, race, { ...ctx, hazards: h });
  r.run(800, () => r.racers.list.every(k => k.finished));
  assert.ok(r.racers.list.every(k => k.finished), JSON.stringify(r.racers.list.map(k => ({ i: k.i, s: k.st[S.s], finish: k.finishS }))));
  assert.deepEqual(r.racers.list.map(k => k.falls), Array(8).fill(0));
});
const hooks = registerHooks({ resolve(specifier, context, next) { const names = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return names[specifier] ? { url: new URL('../vendor/three/build/' + names[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context); } });
const { createBoudoirHazardView } = await import('../render/boudoirHazards.js'), { Group, Matrix4, Vector3 } = await import('three/webgpu'); hooks.deregister();
test('Sculpted trap meshes match their collision poses, pause freezes all motion and teardown releases batches once', () => {
  const scene = new Group(), view = createBoudoirHazardView(scene, course), plan = getBoudoirSetpieces(course);
  assert.equal(view.entries.length, 22); assert.equal(view.belts.length, 3);
  view.update(6); const pressEntry = view.entries.find(e => e.row.kind === 'press'), brush = view.entries.find(e => e.row.kind === 'sweeper');
  assert.equal(pressEntry.moving.position.y, powderPressPhase(pressEntry.row, 6).liftM);
  view.group.updateMatrixWorld(true);
  for (const station of view.entries.filter(e => e.heads)) {
    assert.equal(station.holder.children.filter(c => c.name.endsWith('-shared-support')).length, 1);
    for (const head of station.heads) {
      const pose = mascaraPhase(head.row, 6), centre = station.holder.worldToLocal(head.brush.getWorldPosition(new Vector3()));
      assert.ok(Math.hypot(centre.x - pose.x, centre.y - pose.y, centre.z + pose.z) < 1e-5, 'visible compact brush centre equals collision pose');
      const top = station.holder.worldToLocal(head.arm.localToWorld(new Vector3(0, .5, 0)));
      const bottom = station.holder.worldToLocal(head.arm.localToWorld(new Vector3(0, -.5, 0)));
      assert.ok(top.distanceTo(head.pivot.position) < 1e-5, 'arm stays physically attached to fixed pivot');
      assert.ok(bottom.distanceTo(centre) < 1e-5, 'arm stays attached to brush head');
    }
  }
  const firstHead = brush.heads[0], ends = firstHead.brush.getObjectByName('mascara-radial-end-bristles'), vertex = new Vector3(), matrix = new Matrix4(), positions = ends.geometry.attributes.position;
  assert.equal(ends.count, 144);
  for (let i = 0; i < ends.count; i++) {
    ends.getMatrixAt(i, matrix);
    for (let j = 0; j < positions.count; j++) { vertex.fromBufferAttribute(positions, j).applyMatrix4(matrix).multiply(firstHead.brush.scale);
      assert.ok(Math.hypot(vertex.x, vertex.y) < firstHead.row.radiusM && Math.abs(vertex.z) < firstHead.row.depthM / 2, 'visible end bristles fit the collision envelope'); }
  }
  const poses = () => view.entries.map(e => e.heads ? e.heads.map(h => h.pivot.rotation.z) : e.moving.position.toArray());
  const before = poses(), arrows = [...view.belts[0].arrows.instanceMatrix.array], colours = view.belts.map(b => b.colour.value.toArray());
  view.update(9, { paused: true }); assert.deepEqual(poses(), before); assert.deepEqual([...view.belts[0].arrows.instanceMatrix.array], arrows);
  assert.deepEqual(view.belts.map(b => b.colour.value.toArray()), colours);
  view.hit({ id: plan.presses[0].id }); view.update(6.1); assert.ok(view.group.getObjectByName('boudoir-impact-powder').count > 0);
  view.reset(); assert.equal(view.group.getObjectByName('boudoir-impact-powder').count, 0);
  let expected = 0, disposed = 0; view.group.traverse(o => { if (o.isInstancedMesh) { expected++; o.addEventListener('dispose', () => disposed++); } });
  view.dispose(); view.dispose(); assert.equal(scene.children.length, 0); assert.equal(disposed, expected);
});
