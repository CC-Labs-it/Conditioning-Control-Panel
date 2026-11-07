import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { boostArchPlan, safeArchStation } from '../track/boostArches.js';
import { archCrossed, createBoostArchRun, createArchOnlyItems } from '../sim/boostArches.js';
import { createItems } from '../sim/items.js';
import { createNetItems } from '../net/items.js';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S, BOOST } from '../sim/state.js';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const map = { 'three': '../vendor/three/build/three.webgpu.min.js', 'three/webgpu': '../vendor/three/build/three.webgpu.min.js', 'three/tsl': '../vendor/three/build/three.tsl.min.js' };
  if (specifier.startsWith('three/addons/')) return { url: new URL('../vendor/three/addons/' + specifier.slice(13), import.meta.url).href, shortCircuit: true };
  return map[specifier] ? { url: new URL(map[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene } = await import('three/webgpu');
const { createBoostArches } = await import('../render/boostArches.js');
hooks.deregister();

const course = id => loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')), { moving: true });
const tracks = Object.fromEntries(['goonworks', 'boudoir', 'keep'].map(id => [id, course(id)]));

test('themed arch runs replace plain strings and use supported curved-road corridors', () => {
  for (const [id, track] of Object.entries(tracks)) {
    const plan = boostArchPlan(track);
    assert.ok(plan.arches.length >= 3, id);
    assert.ok(new Set(plan.arches.map(a => a.run)).size >= 2, id);
    assert.equal(plan.replaced.length, id === 'goonworks' ? 0 : 10);
    for (const a of plan.arches) assert.ok(safeArchStation(track, a.s, a.x), id + ':' + a.id);
    for (const i of plan.replaced) assert.equal(track.items[i].kind, 'bubble');
    assert.equal(createItems(track, makeFeel(), recorder()).view().bubbles.length,
      track.items.filter(r => r.kind === 'bubble').length - plan.replaced.length);
  }
  assert.deepEqual(boostArchPlan(fakeTrack()), { arches: [], replaced: [] });
});

test('a box after replaced strings keeps its original authority and visible item id', () => {
  const base = tracks.boudoir, s = 9000, box = { kind: 'box', s, x: 0, node: base.frameAt(s).seg };
  const track = { ...base, items: [...base.items, box] }, id = base.items.length;
  const events = recorder(), items = createItems(track, makeFeel(), events);
  assert.ok(boostArchPlan(track).replaced.length > 0);
  const r = { i: 0, st: place(track, { s }), input: acts(), item: { id: null, rolling: false, rollT: 0, heldT: 0 },
    mods: {}, fxv: {}, finished: false };
  const standings = { placeOf: () => 1, gapToLeader: () => 0, progressOf: () => s };
  assert.ok(items.view().boxes.some(row => row.i === id));
  items.update(DT, [r], standings, { phase: 'racing' });
  assert.equal(events.first('item-box').payload.box, id);
  assert.equal(items.view().boxes.find(row => row.i === id).alive, false);
});

test('ramp lip footprints and moving hazards really exclude an arch corridor', () => {
  const base = tracks.goonworks, a = boostArchPlan(base).arches[0];
  assert.equal(safeArchStation({ ...base, ramps: [{ lipS: a.s + 15, lenM: 8 }] }, a.s, a.x), false);
  assert.equal(safeArchStation({ ...base, moving: { traps: [{ s: a.s + 20, len: 6 }] } }, a.s, a.x), false);
});

function setup() {
  const track = tracks.goonworks, a = boostArchPlan(track).arches[0], feel = makeFeel(), events = recorder();
  const run = createBoostArchRun(track, feel, events), r = { i: 0, st: place(track, { s: a.s - 2, lateral: a.x, speed: 35 }) };
  const move = (s, options = {}) => { r.st = place(track, { s, lateral: a.x, speed: 35, ...options }); };
  return { track, a, r, run, feel, events, move };
}

test('a forward no-song crossing rewards once per lap, reset rearms, and stronger boosts survive', () => {
  const { a, r, run, events, move, feel } = setup();
  run.update(DT, [r]); move(a.s + 2); run.update(DT, [r]);
  assert.equal(events.all('boost-arch').length, 1);
  assert.equal(r.st[S.boostAmount], Math.fround(feel.get('items.bubbleAmount')));
  move(a.s - 2); run.update(DT, [r]); move(a.s + 2); run.update(DT, [r]);
  assert.equal(events.all('boost-arch').length, 1);
  move(a.s - 2); r.st[S.lap] = 1; run.update(DT, [r]); move(a.s + 2); r.st[S.lap] = 1;
  r.st[S.boostKind] = BOOST.PAD; r.st[S.boostAmount] = .3; r.st[S.boostHoldS] = 1;
  run.update(DT, [r]);
  assert.equal(events.all('boost-arch').length, 2);
  assert.ok(r.st[S.boostAmount] > .29);
  run.reset(); move(a.s - 2); run.update(DT, [r]); move(a.s + 2); run.update(DT, [r]);
  assert.equal(events.all('boost-arch').length, 3);
});

test('solo time trials and online local racers get physical no-song rewards without item messages', () => {
  for (const online of [false, true]) {
    const { track, a, r, feel, events, move } = setup(), sent = [];
    Object.assign(r, { input: acts(), item: { id: null, rolling: false, rollT: 0, heldT: 0 }, mods: {}, fxv: {}, meta: { seat: 0 } });
    const remote = { ...r, i: 1, kind: 'remote', st: r.st.slice(), meta: { seat: 1 }, fxv: {}, item: { ...r.item } };
    const items = online ? createNetItems(track, feel, events, { send: p => sent.push(p) }) : createArchOnlyItems(track, feel, events);
    const standings = { placeOf: () => 1, gapToLeader: () => 0, progressOf: () => a.s };
    items.update(DT, [r, remote], standings, { phase: 'grid' });
    assert.equal(events.all('boost-arch').length, 0);
    items.update(DT, [r, remote], standings, { phase: 'racing' });
    move(a.s + 2); remote.st = r.st.slice();
    items.update(DT, [r, remote], standings, { phase: 'racing' });
    assert.equal(events.all('boost-arch').filter(e => e.payload.racer === 0).length, 1);
    if (online) assert.equal(events.all('boost-arch').filter(e => e.payload.racer === 1).length, 0);
    else assert.equal(items.view().boxes.length, 0);
    assert.equal(sent.length, 0);
    assert.equal(items.view().arches.length, 6);
  }
});

test('backward, side miss, high deck, airborne, fork and teleported crossings give no reward', () => {
  const { track, a } = setup();
  const prev = (s, options = {}) => {
    const st = place(track, { s, lateral: a.x, ...options });
    return { p: [st[S.posX], st[S.posY], st[S.posZ]], grounded: !options.airborne };
  };
  assert.equal(archCrossed(track, a, prev(a.s + 2), place(track, { s: a.s - 2, lateral: a.x })), false);
  for (const options of [{ lateral: a.x + 5 }, { height: 8 }, { airborne: true }]) {
    assert.equal(archCrossed(track, a, prev(a.s - 2, options), place(track, { s: a.s + 2, lateral: a.x, ...options })), false);
  }
  assert.equal(archCrossed(track, a, prev(a.s - 40), place(track, { s: a.s + 2, lateral: a.x })), false);
  assert.equal(archCrossed(track, a, { ...prev(a.s - 2), fork: true }, place(track, { s: a.s + 2, lateral: a.x })), false);
});

test('solid arches have open kart passages, share resources and retain pause/motion controls', () => {
  for (const track of Object.values(tracks)) {
    const scene = new Scene(), view = createBoostArches(scene, track);
    assert.ok(view.rows.length >= 3);
    const row = view.rows[0];
    for (const mesh of row.parts) {
      const p = mesh.geometry.attributes.position;
      for (let i = 0; i < p.count; i++) {
        const x = p.getX(i) * mesh.scale.x + mesh.position.x, y = p.getY(i) * mesh.scale.y + mesh.position.y;
        assert.ok(Math.abs(x) > 2.5 || y > 3.5 || y < .5, mesh.geometry.type);
      }
    }
    view.update([{ pulse: .5 }]); assert.equal(row.pulse.visible, true);
    const scale = row.pulse.scale.x;
    view.update([{ pulse: .1 }], { paused: true }); assert.equal(row.pulse.scale.x, scale);
    view.update([{ pulse: .5 }], { gentle: true }); assert.equal(row.pulse.visible, false);
    view.update([{ pulse: .5 }], { motion: 'off' }); assert.equal(row.pulse.visible, false);
    view.dispose(); assert.equal(scene.children.length, 0);
  }
});
