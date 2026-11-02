import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { createBoudoirPerfume, perfumePhase, perfumeZones, PERFUME } from '../sim/boudoirPerfume.js';
import { S, RESPAWN } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createAi } from '../sim/ai.js';
import { createRacers } from '../sim/racers.js';
import { updateRespawn } from '../sim/respawn.js';
import { loadTrack } from '../track/load.js';
import { fakeTrack, makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

const track = fakeTrack({ w: 36 }), zone = { id: 'test', s0: 100, s1: 165, x: -5, w: 18, offsetS: 0 };
const make = () => { const events = recorder(); return { events, sim: createBoudoirPerfume(track, events, { zones: [zone] }) }; };
const racer = (x = -5, s = 120, h = 0) => ({ i: 0, kind: 'local', st: place(track, { s, lateral: x, height: h, speed: 35 }), finished: false });
const speed = r => Math.hypot(r.st[S.velX], r.st[S.velY], r.st[S.velZ]);

test('perfume shares a repeatable warning, spray, active and fade clock', () => {
  for (const [t, phase] of [[0, 'clear'], [3.3, 'warn'], [5.3, 'spray'], [6, 'active'], [9.5, 'fade'], [11, 'clear']]) {
    const a = perfumePhase(zone, t), b = perfumePhase(zone, t + PERFUME.cycleS);
    assert.equal(a.phase, phase); assert.equal(b.phase, phase); assert.ok(Math.abs(a.mist - b.mist) < 1e-10);
    assert.deepEqual(perfumePhase(zone, t), a);
  }
  assert.equal(perfumePhase(zone, 5.59).active, false);
  assert.equal(perfumePhase(zone, 5.61).active, true);
  assert.equal(perfumePhase(zone, 9).active, false);
});

test('active mist slows the short line while dry bypass, warning, flight and remote states remain untouched', () => {
  for (const mode of ['wet', 'dry', 'warn', 'flight', 'below', 'respawn', 'finished', 'remote']) {
    const { sim, events } = make(), r = racer(mode === 'dry' ? 12 : -5, 120, mode === 'flight' ? 4 : mode === 'below' ? -3 : 0);
    if (mode === 'respawn') r.st[S.respawnPhase] = RESPAWN.GRACE;
    if (mode === 'finished') r.finished = true;
    if (mode === 'remote') r.kind = 'remote';
    const before = speed(r); sim.update([r], mode === 'warn' ? 4 : 6, DT);
    if (mode === 'wet') { assert.ok(speed(r) < before); assert.equal(events.all('perfume-contact').length, 1); }
    else { assert.equal(speed(r), before, mode); assert.equal(events.all('perfume-contact').length, 0, mode); }
    assert.equal(r.st[S.stalled], 0); assert.ok([...r.st].every(Number.isFinite));
  }
});

test('swept entry slows a crossing and preserves vertical impulse without teleport hits', () => {
  for (const teleport of [false, true]) {
    const { sim, events } = make(), r = racer(-5, teleport ? 50 : 99);
    sim.update([r], 6, DT); r.st[S.s] = 102; r.st[S.velY] = 4;
    const before = r.st[S.engineV]; sim.update([r], 6 + DT, DT);
    assert.equal(r.st[S.velY], 4);
    assert.equal(events.all('perfume-contact').length, teleport ? 0 : 1);
    assert.equal(r.st[S.engineV] < before, !teleport);
  }
});

test('ongoing contact remains driveable, emits once and clears immediately on leaving', () => {
  const { sim, events } = make(), wet = racer(), dry = racer(12), feel = makeFeel(), a = acts({ throttle: 1 });
  for (let n = 0; n < 60; n++) {
    step(wet.st, a, track, feel, DT, events); step(dry.st, a, track, feel, DT, events);
    sim.update([wet, dry], 6 + n * DT, DT);
  }
  assert.ok(speed(wet) > 15 && speed(wet) < speed(dry) - 6);
  assert.equal(events.all('perfume-contact').length, 1); assert.equal(wet.st[S.stalled], 0);
  wet.st[S.lateral] = 12; const before = [...wet.st]; sim.update([wet], 7.1, DT);
  // The final partial crossing can drag once; the next fully dry tick cannot.
  const afterExit = [...wet.st]; sim.update([wet], 7.1 + DT, DT); assert.deepEqual([...wet.st], afterExit);
  assert.ok(wet.st[S.engineV] >= PERFUME.speedMps); assert.ok(before[S.engineV] >= wet.st[S.engineV]);
});

test('pause freezes contact, seek warning rearms, and reset removes event memory', () => {
  const { sim, events } = make(), r = racer(); sim.update([r], 0, DT);
  sim.update([r], 6, DT); assert.equal(sim.ready, false); assert.equal(events.all('perfume-contact').length, 0);
  const before = [...r.st]; sim.update([r], 8, DT, { paused: true }); assert.deepEqual([...r.st], before);
  for (let n = 1; n <= 121; n++) sim.update([r], 6 + n * DT, DT);
  assert.equal(sim.ready, true); assert.equal(events.all('perfume-contact').length, 1);
  sim.update([r], 6, DT); assert.equal(sim.ready, false);
  sim.reset(); assert.equal(sim.hits, 0); sim.update([r], 6, DT); assert.equal(sim.hits, 1);
});

test('real zones keep a measured dry bypass throughout their entire span', () => {
  const real = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
  const zones = perfumeZones(real); assert.equal(zones.length, 3);
  for (const z of zones) for (let s = z.s0; s < z.s1; s += 3) {
    assert.ok(Math.abs(z.dryX - z.x) > z.w / 2 + PERFUME.radiusM);
    assert.ok(Math.abs(z.dryX) + 2.5 < real.frameAt(s).w / 2);
  }
  assert.throws(() => perfumeZones(track, [{ ...zone, w: 34, x: 0 }]), /dry bypass/);
});

test('eight real bot controllers traverse every live perfume encounter without a fall or steering lock', () => {
  const real = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
  for (const z of perfumeZones(real)) {
    const feel = makeFeel(), events = recorder(), rs = createRacers(real, feel, events, { count: 8, seed: 241024 });
    const ai = createAi(real, feel, { seed: 241024 }), sim = createBoudoirPerfume(real, events);
    const race = { phase: 'racing', timeS: 0 }, startT = 6 - z.offsetS + PERFUME.cycleS;
    for (const r of rs.list) r.st.set(place(real, { s: z.s0 - 15 - r.i, lateral: z.x + (r.i % 3 - 1) * 2, speed: 28 }));
    let tick = 0;
    for (; tick < 60 * 20 && rs.list.some(r => r.st[S.s] < z.s1 + 10); tick++) {
      race.timeS = tick * DT;
      for (const r of rs.list) {
        ai.drive(r, { race, racers: rs.list }, DT, r.input); step(r.st, r.input, real, r.feel, DT, r.events);
        updateRespawn(r.st, real, r.feel, DT, r.events);
      }
      sim.update(rs.list, startT + tick * DT, DT);
    }
    const hits = events.all('perfume-contact');
    assert.ok(hits.length > 0, z.id + ' has actual driven contacts');
    assert.ok(tick < 60 * 20, z.id + ' all controllers exit');
    for (const r of rs.list) { assert.equal(r.falls, 0, z.id + ':' + r.i); assert.equal(r.st[S.stalled], 0); }
  }
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { createBoudoirPerfumeView } = await import('../render/boudoirPerfume.js');
const { Scene } = await import('three/webgpu'); hooks.deregister();

test('perfume materials exist before warming and pause/menu/Gentle keep soft visible boundaries', () => {
  const scene = new Scene(), view = createBoudoirPerfumeView(scene, track, { zones: [zone] });
  const names = []; view.group.traverse(o => { if (o.material) names.push(o.name); });
  assert.ok(names.includes('perfume-cloud:test')); assert.ok(names.includes('perfume-boundary:test'));
  assert.ok(names.includes('perfume-dry-bypass:test')); assert.ok(names.includes('perfume-footprint:test'));
  const count = view.inspect().materials;
  for (let i = 0; i < 60; i++) view.update(6, { gentle: true });
  assert.ok(view.inspect().cloudOpacity[0] > .1 && view.inspect().cloudOpacity[0] < .23);
  const before = view.inspect(); view.update(0, { paused: true }); assert.deepEqual(view.inspect(), before);
  for (let i = 0; i < 60; i++) view.update(6, { active: false });
  assert.equal(view.inspect().cloudOpacity[0], 0); assert.equal(view.inspect().materials, count);
  view.dispose(); view.dispose(); assert.equal(scene.children.length, 0);
});
