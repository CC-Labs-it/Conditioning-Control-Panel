import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { STONE_HEX, stoneHalfWidth, stoneLaneSpan, stoneIn, stoneDistance, fieldSupport } from '../track/courts.js';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { createAi } from '../sim/ai.js';
import { createRacers } from '../sim/racers.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createActions } from '../core/actions.js';
import { S } from '../sim/state.js';
import { fakeTrack, makeFeel, place, DT } from './sim-fake.mjs';

const h = { id: 'hex', shape: 'hex', s: 100, x: 0, w: 24, len: 34 };
const field = { id: 'hex-field', fromS: 100, toS: 140, lanes: [0], stones: [h] };
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);

test('hex support clips the six real faces, with perpendicular clearance and exact void distance', () => {
  near(stoneHalfWidth(h, 100), 6); near(stoneHalfWidth(h, 117), 12); near(stoneHalfWidth(h, 134), 6);
  assert.equal(stoneIn(field, 100.1, 10), null, 'the rectangular corner is void');
  assert.equal(stoneIn(field, 117, 11.9), h, 'the widest shoulder carries the kart');
  assert.equal(stoneIn(field, 117, 11.9, 0.7), null);
  assert.equal(stoneIn(field, 100.2, 0, 0.7), null, 'front face clearance');
  near(stoneDistance(field, 100, 12), 6 / Math.hypot(1, 24 / 68));
  near(stoneDistance(field, 97, 0), 3); near(stoneDistance(field, 117, 0), 0);
  assert.deepEqual(fieldSupport([field], 100, 52), [[-6, 6]]);
  assert.deepEqual(fieldSupport([field], 117, 52), [[-12, 12]]);
  assert.deepEqual(fieldSupport([field], 135, 52), []);
  for (const [x, z] of STONE_HEX) {
    const s = h.s + h.len * (z + 0.5);
    assert.equal(stoneIn(field, s, x * h.w), h);
    near(stoneDistance(field, s, x * h.w), 0);
  }
});

test('hex cross-links join shoulders while held-lane intervals account for tapered ends', () => {
  assert.deepEqual(stoneLaneSpan(h, 0), [100, 134]);
  near(stoneLaneSpan(h, 9)[0], 108.5); near(stoneLaneSpan(h, 9)[1], 125.5);
  assert.equal(stoneLaneSpan(h, 13), null);
  const joined = { ...field, stones: [h, { ...h, x: 18 }] };
  assert.deepEqual(fieldSupport([joined], 117, 52), [[-12, 30]]);
  assert.deepEqual(fieldSupport([joined], 100, 52), [[-6, 6], [12, 24]]);
  const tr = fakeTrack({ w: 104 });
  tr.fields = [{ ...field, toS: 134, lanes: [0, 18], stones: [h, { ...h, x: 8 }] }];
  assert.deepEqual(createAi(tr, makeFeel(), { seed: 1 }).fields()[0].safe, [0], 'bots reject a lane touching only the middle of a hex');
});

test('schema accepts giant hex platforms and broad galleries without enlarging legacy rectangles', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
  for (const n of json.nodes) n.w = 104;
  const f = json.fields[0]; f.lanes = [0];
  f.stones = [{ id: h.id, node: f.from, x: 0, w: 24, len: 34, shape: 'hex' }];
  assert.equal(validate(json).ok, true);
  const tr = loadTrack(json), fs = tr.fields[0], s = fs.fromS + 0.5, frame = tr.frameAt(s);
  const point = frame.p.map((v, i) => v + frame.b[i] * 10 + frame.n[i] * 0.2);
  const q = tr.query(...point, fs.from);
  assert.equal(q.onRoad, false); assert.equal(q.stone, null); assert.equal(q.hole, fs.void);
  delete f.stones[0].shape; assert.equal(validate(json).ok, false, 'rectangle bounds stay 8 by 12');
  f.stones[0].shape = 'circle'; assert.equal(validate(json).ok, false, 'unknown shape refused');
  f.stones[0].shape = 'hex'; f.stones[0].w = 33; assert.equal(validate(json).ok, false);
  f.stones[0].w = 24; f.stones[0].len = 41; assert.equal(validate(json).ok, false);
});

test('Mainframe bots traverse every giant-platform lane at ordinary and boosted entry speed', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8'))), f = track.fields[0];
  assert.equal(f.lanes.length, 5);
  assert.ok(f.stones.every((st) => st.shape === 'hex'));
  for (const speed of [20, 34]) for (let i = 0; i < f.lanes.length; i++) {
    const feel = makeFeel(), events = [], bus = { emit: (name) => events.push(name) };
    const r = createRacers(track, feel, bus, { count: 8, seed: 1 }).list[i + 1], lane = f.lanes[i];
    r.st.set(place(track, { s: f.fromS - 24, lateral: lane + 0.5, speed }));
    const ai = createAi(track, feel, { seed: 1 }), actions = createActions(), race = { phase: 'racing', timeS: 10 };
    let worst = 0;
    for (let tick = 0; tick < 35 / DT; tick++) {
      ai.drive(r, { race, racers: [r] }, DT, actions);
      step(r.st, actions, track, feel, DT, bus); updateRespawn(r.st, track, feel, DT, bus); race.timeS += DT;
      if (r.st[S.s] >= f.fromS && r.st[S.s] < f.toS) worst = Math.max(worst, Math.abs(r.st[S.lateral] - lane));
      if (r.st[S.s] > f.toS + 10 && r.st[S.grounded]) break;
      if (r.st[S.respawnPhase]) break;
    }
    const label = JSON.stringify({ lane, speed, s: r.st[S.s], lateral: r.st[S.lateral], worst });
    assert.ok(r.st[S.s] > f.toS + 10, 'completed field: ' + label);
    assert.equal(r.st[S.respawnPhase], 0, 'no rescue: ' + label);
    assert.ok(!events.includes('drop') && !events.includes('fall'), 'no fall: ' + label);
    assert.ok(worst < 1.5, 'held lane: ' + label);
  }
});

test('Mainframe gallery approach keeps its outer shoulder above the lower reactor helix', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8'))), field = track.fields[0];
  for (let s = field.fromS - 70; s < field.fromS; s += 2) {
    const f = track.frameAt(s);
    for (let x = 26; x < f.w / 2 - 0.5; x += 2) {
      const p = f.p.map((v, k) => v + f.b[k] * x + f.n[k] * 0.2);
      for (const hint of [f.seg, -1]) {
        const q = track.query(...p, hint);
        assert.ok(Math.abs(q.s - s) < 1e-3, `gallery s=${s}, x=${x} selected s=${q.s}`);
        assert.ok(q.onRoad && Math.abs(q.height - 0.2) < 1e-3);
      }
    }
  }
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { buildStones, stoneLayout } = await import('../render/stones.js');
const { Matrix4, Vector3 } = await import('three/webgpu');
hooks.deregister();

test('giant hex rendering uses one solid platform per stone with the physical outline and thick bevelled sides', () => {
  const track = { id: 'mainframe', fields: [field], frameAt: (s) => ({ p: [s, 76, 0], b: [0, 0, 1], n: [0, 1, 0], t: [1, 0, 0], w: 104 }) };
  const rows = stoneLayout(track);
  assert.equal(rows.length, 1); assert.deepEqual(rows[0].p, [117, 76, 0]);
  const built = buildStones(track, { model() { assert.fail('hex platforms use their exact native mesh'); } }, null);
  assert.equal(built.mode, 'hex'); assert.equal(built.count, 1); assert.equal(built.group.children.length, 4);
  const hull = built.group.children.find((m) => m.name === 'stone-hex-hull');
  near(hull.geometry.boundingBox.min.y, -3.2); near(hull.geometry.boundingBox.max.y, 0);
  const matrix = new Matrix4(), p = new Vector3(), corners = new Set();
  let disposed = 0;
  for (const mesh of built.group.children) {
    assert.equal(mesh.count, 1); mesh.getMatrixAt(0, matrix);
    mesh.geometry.addEventListener('dispose', () => disposed++);
    const pos = mesh.geometry.attributes.position;
    for (let i = 0; i < pos.count; i++) {
      p.fromBufferAttribute(pos, i).applyMatrix4(matrix);
      assert.ok(Math.abs(p.z) <= stoneHalfWidth(h, p.x) + 1e-5, 'render stays inside support');
      if (mesh === hull && Math.abs(p.y - 76) < 1e-5 && Math.abs(p.z) > 0.01) corners.add(p.x.toFixed(3) + ':' + p.z.toFixed(3));
    }
  }
  assert.equal(corners.size, 6, 'exactly six top corners');
  built.dispose(); built.dispose(); assert.equal(disposed, 4, 'geometry disposed once');
});
