import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';
import { goonworksSetpieces, goonworksPose, apertureContains, apertureAngle } from '../track/goonworks-setpieces.js';
import { setMachineryBeat, machineryBeatAt } from '../track/machineryBeat.js';
import { createGoonworksHazards, goonworksOverlap } from '../sim/goonworksHazards.js';
import { S, RESPAWN, cloneState } from '../sim/state.js';
import { makeFeel, place, recorder } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { registerHooks } from 'node:module';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
const layout = getGoonworksLayout(track), rows = goonworksSetpieces(track, layout);
function racer(row, along, x = 0, h = 0) {
  const st = place(track, { s: row.s + along, lateral: x, height: h, speed: 30 });
  // Place in the exact shared toy frame to avoid interpolation tolerance at authored joints.
  for (let j = 0; j < 3; j++) st[S.posX + j] = row.p[j] + row.t[j] * along + row.b[j] * x + row.n[j] * h;
  return { i: 1, kind: 'local', st, feel: makeFeel(), events: recorder(), finished: false };
}
function crossing(row, time, x = 0, h = 0) {
  const toys = createGoonworksHazards(track, layout), r = racer(row, 7, x, h), previous = racer(row, -7, x, h).st;
  return { toys, r, hit: toys.apply(r, previous, time) };
}

test('authored toy halls contain three real wheels, four shutters and sixteen independent wave blocks with wide bypasses', () => {
  for (const [kind, count] of [['wheel', 3], ['shutter', 4], ['block', 16], ['flipper', 1]]) assert.equal(rows.filter(r => r.kind === kind).length, count);
  for (const row of rows) assert.ok(row.s > row.fromS && row.s < row.toS);
});

test('the wheel opening rotates through the actual disc and collision admits its missing wedge', () => {
  const row = rows.find(r => r.kind === 'wheel');
  assert.equal(apertureContains(row, goonworksPose(row, 0), 0, .8), false);
  assert.equal(apertureContains(row, goonworksPose(row, 5), 0, .8), true);
  assert.equal(crossing(row, 0).hit, null);
  assert.equal(crossing(row, 5).hit, row.id);
  for (const x of [-row.bypass, row.bypass]) assert.equal(crossing(row, 5, x).hit, null);
  for (const h of [-8, 36]) assert.equal(crossing(row, 5, 0, h).hit, null, 'another deck must not collide');
  const { r } = crossing(row, 5); assert.ok(r.st[S.s] < row.s); assert.ok(r.st[S.stalled] > 0);
});

test('wheel support and missing-sector containment stay bounded across a full revolution', () => {
  const row = rows.find(r => r.kind === 'wheel'); let clear = 0, blocked = 0;
  for (let i = 0; i < 180; i++) {
    const pose = goonworksPose(row, i * Math.PI * 2 / Math.abs(row.speed) / 180);
    assert.equal(goonworksOverlap(row, pose, { x: row.bypass, along: 0, h: 0 }), false);
    if (goonworksOverlap(row, pose, { x: 0, along: 0, h: 0 })) blocked++; else clear++;
  }
  assert.ok(clear > 30 && blocked > 60);
});

test('all four equally spaced windows pass a complete kart, and all four spokes block it', () => {
  const row = rows.find(r => r.kind === 'wheel');
  assert.equal(row.openings, 4);
  for (let i = 0; i < 4; i++) {
    const open = { angle: -Math.PI / 2 + i * Math.PI / 2 };
    assert.equal(goonworksOverlap(row, open, {x:0,along:0,h:0}), false, 'opening ' + i);
    assert.equal(goonworksOverlap(row, {angle:open.angle + Math.PI / 4}, {x:0,along:0,h:0}), true, 'spoke ' + i);
    for (const radius of [2,8,14]) {
      const angle = open.angle + i * Math.PI / 2;
      assert.equal(apertureContains(row, open, Math.cos(angle)*radius, row.centerY + Math.sin(angle)*radius), false);
    }
  }
});

test('fast beat kicks catch a stationary racer when a solid spoke crosses the opening', () => {
  const row = rows.find(r => r.kind === 'wheel');
  for (const bpm of [96.75, 180, 240]) {
    setMachineryBeat(track, {enabled:true,songS:0,raceS:0,bpm,offsetS:0,playing:true});
    const toys = createGoonworksHazards(track, layout), r = racer(row, 0);
    toys.apply(r, cloneState(r.st), 0);
    let hit = null;
    for (let i = 1; i <= 24 && !hit; i++) hit = toys.apply(r, cloneState(r.st), i / 120);
    assert.equal(hit, row.id, 'closed moving spoke at ' + bpm + ' BPM');
    const pose0 = goonworksPose(row, 0, machineryBeatAt(track, 0));
    const pose1 = goonworksPose(row, 60/bpm, machineryBeatAt(track, 60/bpm));
    assert.ok(Math.abs(Math.abs(pose1.angle-pose0.angle)-Math.PI/6)<1e-9, '30 degrees per beat');
    setMachineryBeat(track, null);
  }
});

test('screen shutters sweep their true lateral position and leave both outside lanes open', () => {
  const row = rows.find(r => r.kind === 'shutter'), t = row.periodS / 4;
  const pose = goonworksPose(row, t);
  assert.ok(pose.x > 6);
  assert.equal(crossing(row, t, pose.x).hit, row.id);
  assert.equal(crossing(row, t, -9).hit, null);
  for (const x of [-row.bypass, row.bypass]) assert.equal(crossing(row, t, x).hit, null);
  const toys = createGoonworksHazards(track, layout), r = racer(row, 0, 5.95), p = cloneState(r.st);
  toys.apply(r, p, 0);
  assert.equal(toys.apply(r, p, .1), row.id, 'a moving screen can contact a stationary racer');
});

test('quiz tiles warn before rising, collide only inside their raised footprint, and allow high hops', () => {
  const row = rows.find(r => r.kind === 'block'), base = -row.offsetS;
  assert.equal(goonworksPose(row, base + 3.5).warning, true);
  assert.equal(goonworksPose(row, base + 3.5).height, 0);
  assert.equal(crossing(row, base + 3.5, row.x).hit, null);
  assert.equal(crossing(row, base + 5.5, row.x).hit, row.id);
  assert.equal(crossing(row, base + 5.5, row.x, 6).hit, null);
  for (const x of [-25, 25]) assert.equal(crossing(row, base + 5.5, x).hit, null);
  assert.notEqual(goonworksPose(row, 4.6).height, goonworksPose(rows.find(r => r.id === 'quiz-block-1-0'), 4.6).height);
});

test('pinball flipper adds one timed airborne kick and preserves the ordinary launch when at rest', () => {
  const row = rows.find(r => r.kind === 'flipper');
  assert.equal(crossing(row, 1).hit, null);
  const { r, toys, hit } = crossing(row, 4.85);
  assert.equal(hit, row.id); assert.equal(r.st[S.airborne], 1); assert.ok(r.st[S.velY] >= 8);
  const velocity = r.st[S.velY]; toys.apply(r, cloneState(r.st), 4.85); assert.equal(r.st[S.velY], velocity);
  assert.equal(toys.inspect(4.85).launches, 1);
});

test('clock freeze, reset, rescue, remote and post-finish behavior are deterministic', () => {
  const row = rows.find(r => r.kind === 'wheel'), toys = createGoonworksHazards(track, layout);
  for (let i = 0; i < 2; i++) {
    const r = racer(row, 7), previous = racer(row, -7).st;
    assert.equal(toys.apply(r, previous, 5), row.id);
    const frozen = [...r.st]; assert.equal(toys.apply(r, cloneState(r.st), 5), null); assert.deepEqual([...r.st], frozen);
    assert.equal(toys.inspect(5).hits, 1); toys.reset(); assert.equal(toys.inspect(5).hits, 0);
  }
  for (const mutate of [r => r.finished = true, r => r.kind = 'remote', r => r.st[S.respawnPhase] = RESPAWN.PULL]) {
    const r = racer(row, 7); mutate(r); assert.equal(toys.apply(r, racer(row, -7).st, 5), null);
  }
  const bot = racer(row, -40); bot.kind = 'bot';
  const actions = {}; assert.equal(toys.steer(bot, actions), true);
  assert.ok(Number.isFinite(actions.steer) && Math.abs(actions.steer) <= 1);
  assert.equal(actions.hop, false);
});

test('a rear bump after resolution is swept before the next movement and cannot bypass a closed wheel', () => {
  const row = rows.find(r => r.kind === 'wheel'), toys = createGoonworksHazards(track, layout), r = racer(row, 7);
  assert.equal(toys.apply(r, racer(row, -7).st, 5), row.id);
  r.st.set(racer(row, 0).st);
  const p = cloneState(r.st); r.st.set(racer(row, 2).st);
  assert.equal(toys.apply(r, p, 5 + 1 / 60), row.id);
  assert.ok(r.st[S.s] < row.s - 1.8);
  assert.equal(toys.inspect(5 + 1 / 60).hits, 1, 'event and stall do not repeat inside the cooldown');
});

test('visible disc geometry has the same road-level opening and closes on the same clock', async () => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    if (specifier === 'three/webgpu' || specifier === 'three/tsl') return { url: new URL('../vendor/three/build/' + (specifier.endsWith('tsl') ? 'three.tsl.min.js' : 'three.webgpu.min.js'), import.meta.url).href, shortCircuit: true };
    return next(specifier, context);
  } });
  try {
    const { Scene, Raycaster, Vector3 } = await import('three/webgpu');
    const { createGoonworksHazardView } = await import('../render/goonworksHazards.js');
    const scene = new Scene(), view = createGoonworksHazardView(scene, track, layout), row = rows.find(r => r.kind === 'wheel');
    const wheel = view.group.getObjectByName(row.id);
    const hitAt = x => {
      scene.updateMatrixWorld(true);
      const p = row.p.map((v, j) => v - row.t[j] * 7 + row.n[j] * .8 + row.b[j] * x);
      return new Raycaster(new Vector3(...p), new Vector3(...row.t), 0, 14).intersectObject(wheel, true);
    };
    view.update(0); assert.equal(hitAt(0).length, 0);
    view.update(5); assert.ok(hitAt(0).length > 0); assert.equal(hitAt(row.bypass).length, 0);
    const q = wheel.quaternion.toArray(), before = view.group.children.length;
    view.update(5); assert.deepEqual(wheel.quaternion.toArray(), q); assert.equal(view.group.children.length, before);
    assert.equal(view.inspect().wheels, 3); view.dispose(); view.dispose(); assert.equal(scene.children.length, 0);
  } finally { hooks.deregister(); }
});

test('wheel face colours tile one opaque surface without hidden caps at either viewing side or distance', async () => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    if (specifier === 'three/webgpu' || specifier === 'three/tsl') return { url: new URL('../vendor/three/build/' + (specifier.endsWith('tsl') ? 'three.tsl.min.js' : 'three.webgpu.min.js'), import.meta.url).href, shortCircuit: true };
    return next(specifier, context);
  } });
  try {
    const { Scene, Raycaster, Vector3, FrontSide } = await import('three/webgpu');
    const { createGoonworksHazardView } = await import('../render/goonworksHazards.js');
    const scene = new Scene(), view = createGoonworksHazardView(scene, track, layout);
    for (const row of rows.filter(r => r.kind === 'wheel')) {
      const rotor = view.group.getObjectByName(row.id).getObjectByName('wheel-rotor');
      const surface = rotor.children.filter(m => m.name.startsWith('wheel-face-') || m.name === 'wheel-cut-walls');
      for (const m of surface) {
        assert.equal(m.material.side, FrontSide); assert.equal(m.material.depthTest, true);
        assert.equal(m.material.depthWrite, true); assert.equal(m.material.transparent, false);
      }
      // Test actual submitted triangles. A filled ink cap beneath white would violate both the
      // area equality and ray uniqueness, even when its millimetre offset happens to render cleanly.
      const area = [0, 0];
      for (const m of surface) {
        const g = m.geometry, p = g.getAttribute('position'), ids = g.index;
        const begin = g.drawRange.start, end = Math.min(ids?.count ?? p.count, begin + g.drawRange.count);
        for (let i = begin; i < end; i += 3) {
          const abc = [0, 1, 2].map(k => new Vector3().fromBufferAttribute(p, ids ? ids.getX(i + k) : i + k));
          const cross = abc[1].clone().sub(abc[0]).cross(abc[2].clone().sub(abc[0]));
          if (Math.abs(cross.z) < 1e-6) continue;
          assert.ok(m.name.startsWith('wheel-face-'), 'cut walls must not submit another filled cap');
          assert.ok(abc.every(v => Math.abs(v.z) < 1e-6), 'all face colours share the same plane');
          assert.ok(cross.z * Math.sign(m.position.z) > 0, 'front and back have outward winding');
          area[m.position.z > 0 ? 0 : 1] += Math.abs(cross.z) / 2;
        }
      }
      const expected = row.radius ** 2 * (Math.PI * 2 - row.gap * row.openings) / 2;
      for (const value of area) assert.ok(Math.abs(value - expected) / expected < .001, `cap colors cover ${value}, expected ${expected}`);
      for (const time of [0, 2.35, 5.1]) {
        view.update(time); scene.updateMatrixWorld(true);
        for (const side of [-1, 1]) for (const distance of [7, 140, 1400]) {
          const direction = new Vector3(0, 0, -side).transformDirection(rotor.matrixWorld);
          for (let i = 0; i < 49; i++) for (const radius of [.31, 6.13, 9.91, 13.69, 15.39]) {
            const angle = (i + .371) * Math.PI * 2 / 49;
            const origin = new Vector3(Math.cos(angle) * radius, Math.sin(angle) * radius, side * distance).applyMatrix4(rotor.matrixWorld);
            const hits = new Raycaster(origin, direction, 0, distance + row.depth).intersectObjects(surface, false);
            const solid = apertureAngle(row, angle) >= row.gap / 2;
            assert.equal(hits.length, solid ? 1 : 0, `${row.id} at ${time}s, side ${side}, ${distance}m, radius ${radius}, angle ${angle}`);
            if (solid) assert.ok(Math.abs(hits[0].distance - (distance - row.depth / 2)) < .0001);
          }
        }
      }
    }
    view.dispose(); assert.equal(scene.children.length, 0);
  } finally { hooks.deregister(); }
});

test('all eight racers finish two laps with active toys, items and safe bypass steering', () => {
  const race = makeRace({ track, seed: 20261024, items: createItems }), toys = createGoonworksHazards(track, layout);
  const drive = race.ai.drive.bind(race.ai), step = race.racers.step.bind(race.racers);
  race.ai.drive = (r, ctx, dt, actions) => { drive(r, ctx, dt, actions); toys.steer(r, actions); };
  race.racers.step = (dt, actions, state, ctx) => step(dt, actions, state, { ...ctx, hazards: toys });
  race.run(850, () => race.racers.list.every(r => r.finished));
  assert.ok(race.racers.list.every(r => r.finished), 'finish times ' + race.racers.list.map(r => r.finishS));
  assert.deepEqual(race.racers.list.map(r => r.falls), Array(8).fill(0));
});
