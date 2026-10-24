import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getBoudoirBoostPlan } from '../track/boudoir-boosts.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getBoudoirSetpieces } from '../track/boudoir-setpieces.js';
import { qlook, wrap } from '../core/math.js';
import { S, RESPAWN, cloneState } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createAi } from '../sim/ai.js';
import { createItems } from '../sim/items.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { createBoudoirPerfume } from '../sim/boudoirPerfume.js';
import { steerTo } from '../dev/hook.js';
import { makeFeel, place, recorder, acts, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';
import { placeOnFork } from './forks-fixture.mjs';

const json = JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8'));
const track = loadTrack(json), plan = getBoudoirBoostPlan(track), layout = getBoudoirLayout(track);
const speed = st => Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
const indexOf = pad => track.boosts.findIndex(p => p.node === pad.node && p.x === pad.x);
const contacts = events => events.all('pad').map(e => e.payload.index);
const damage = events => events.list.filter(e => ['wall', 'bump', 'drop', 'rescue-pull'].includes(e.name)).map(e => e.name);
const intersects = (a0, a1, b0, b1) => a1 > b0 && a0 < b1;

// The bath's curved floor is above the flat spline at an offset lane. Starting at
// frameAt(s).p + b*x would put a test kart below its real support and fake a fall.
function supported(s, x, initialSpeed = 25) {
  const st = place(track, { s, lateral: x, speed: initialSpeed }), f = track.surfaceAt(s, x);
  st.set(f.p, S.posX); st.set(qlook([0, 0, 0, 1], track.frameAt(s).t, f.n), S.quatX);
  return st;
}

function driveRun(run, { gentle = false, brake = false, lane = run.pads[0].x, runoff = 100 } = {}) {
  const first = run.pads[0], last = run.pads.at(-1), start = first.s - 10;
  const st = supported(start, lane), feel = makeFeel({ 'gentle.on': gentle }), events = recorder();
  const target = last.s + last.len + runoff - start;
  let travel = 0, maxError = 0, minMargin = Infinity, maxSpeed = 0, maxEngine = 0, brakeStart = null, stopped = false;
  for (let tick = 0; tick < 1800 && travel < target; tick++) {
    const oldS = st[S.s], braking = brake && travel > last.s + last.len - start;
    if (braking && brakeStart === null) brakeStart = travel;
    events.t += DT;
    const beforeEvents = events.list.length;
    step(st, acts({ throttle: braking ? 0 : 1, brake: braking ? 1 : 0,
      steer: steerTo(st, track, st[S.s] + 18, lane, 2.2) }), track, feel, DT, events);
    updateRespawn(st, track, feel, DT, events);
    travel += wrap(st[S.s] - oldS, track.lengthM);
    // Home can continue into the next lap's parallel vanity line. A new real
    // contact restarts the brake floor, so measure from that final pad's exit.
    if (braking) for (const event of events.list.slice(beforeEvents)) if (event.name === 'pad') {
      const pad = track.boosts[event.payload.index];
      brakeStart = travel + wrap(pad.s + pad.len - st[S.s], track.lengthM);
    }
    maxError = Math.max(maxError, Math.abs(st[S.lateral] - lane));
    minMargin = Math.min(minMargin, track.frameAt(st[S.s]).w / 2 - Math.abs(st[S.lateral]));
    maxSpeed = Math.max(maxSpeed, speed(st)); maxEngine = Math.max(maxEngine, st[S.engineV]);
    if (braking && speed(st) < 22 && st[S.engineV] < 22) { stopped = true; break; }
    if (st[S.respawnPhase] !== RESPAWN.NONE) break;
  }
  return { st, events, travel, target, maxError, minMargin, maxSpeed, maxEngine, stopped,
    brakeDistance: brakeStart === null ? null : travel - brakeStart };
}
const detail = (run, result) => JSON.stringify({ run: run.id, contacts: contacts(result.events), damage: damage(result.events),
  maxError: result.maxError, minMargin: result.minMargin, maxSpeed: result.maxSpeed, brakeDistance: result.brakeDistance });

test('Dense authored sequences are real distinct triggers with supported full footprints', () => {
  assert.ok(track.boosts.length >= 40 && track.boosts.length <= 60);
  assert.deepEqual(plan.pads.map(({ node, x, w, len }) => ({ node, x, w, len })), json.boosts);
  assert.equal(new Set(track.boosts.map(p => p.node + '/' + p.x)).size, track.boosts.length);
  for (const run of plan.runs) {
    assert.ok(run.pads.length >= 2 && run.pads.length <= 4, run.id);
    for (let i = 1; i < run.pads.length; i++) {
      const gap = run.pads[i].s - run.pads[i - 1].s;
      assert.ok(gap >= run.pads[i - 1].len + 5 && gap <= 32, run.id + ' readable, separate entries');
    }
    for (const p of run.pads) for (let ds = 0; ds <= p.len; ds += 2) for (const x of [p.x - p.w / 2, p.x, p.x + p.w / 2]) {
      const f = track.surfaceAt(p.s + ds, x), q = track.query(...f.p, f.seg);
      assert.ok(q.onRoad && !q.forkId && Math.abs(q.height) < .06, run.id + ' full painted footprint');
      assert.ok(q.edgeDist >= 2.4, run.id + ' shoulder clearance');
    }
  }
});

test('Boost carry leaves braking space before forks, traps, drops, platform gaps and the mirror launch', () => {
  const stations = getBoudoirSetpieces(track), ranges = [];
  for (const h of [...stations.presses, ...stations.sweepers, ...stations.mirrorGates])
    ranges.push([h.id, h.s - h.depthM / 2 - 125, h.s + h.depthM / 2 + 18]);
  for (const f of track.forks) ranges.push([f.id + ' entry', f.fromS - 125, f.fromS + 80], [f.id + ' merge', f.toS - 80, f.toS + 45]);
  for (const r of track.routes) ranges.push([r.id + ' opening', r.openS0 - 125, r.openS1 + 35], [r.id + ' catch', r.land.fromS - 20, r.land.toS + 25]);
  for (const f of track.fields) ranges.push([f.id, f.fromS - 125, f.toS + 18]);
  ranges.push(['mirror takeoff and landing', layout.mirrorJump.gapS0 - 125, layout.mirrorJump.landingS0 + 60]);
  for (const p of track.boosts) {
    const currentMargin = track.frameAt(p.s).surface === 'stream' ? 10 : 0;
    for (const [id, s0, s1] of ranges) assert.ok(!intersects(p.s, p.s + p.len, s0 - currentMargin, s1), id + ' / pad ' + indexOf(p));
    for (const row of [...stations.conveyors, ...layout.perfume]) if (intersects(p.s, p.s + p.len, row.s0, row.s1))
      assert.ok(Math.abs(p.x - row.x) >= (p.w + row.w) / 2 + .7, 'boost stays on permanent bypass');
  }
});

for (const run of plan.runs) test('Actual throttle drive chains every ' + run.id + ' pad without a rail hit', () => {
  const result = driveRun(run), info = detail(run, result);
  const expected = run.pads.map(indexOf);
  assert.deepEqual(contacts(result.events).filter(i => expected.includes(i)), expected, info);
  assert.equal(result.events.all('boost-start').filter(e => e.payload.kind === 'pad').length, contacts(result.events).length, info);
  assert.ok(result.maxEngine >= 35.8 && result.maxSpeed >= 35.5, info);
  assert.deepEqual(damage(result.events), [], info);
  assert.ok(result.travel >= result.target, info);
  assert.ok(result.maxError <= 6 && result.minMargin >= 2, info);
});

test('Each real pad has an unboosted neighboring lane and does not trigger from merely nearby wheels', () => {
  for (const run of plan.runs) {
    const p = run.pads[0], lane = p.x + (p.x <= 0 ? 1 : -1) * (p.w / 2 + 1.2);
    const result = driveRun({ ...run, pads: [p] }, { lane, runoff: 2 });
    assert.equal(contacts(result.events).includes(indexOf(p)), false, run.id);
  }
});

test('Full brake reaches approach speed within dry and flowing-water runoff margins', () => {
  for (const run of plan.runs) {
    const result = driveRun({ ...run, pads: [run.pads.at(-1)] }, { brake: true, runoff: 350 }), info = detail(run, result);
    const limit = track.frameAt(run.pads.at(-1).s).surface === 'stream' ? 135 : 125;
    assert.ok(result.stopped && result.brakeDistance < limit, info);
    assert.deepEqual(damage(result.events), [], info);
  }
});

test('Gentle changes no boost contact, control input or physical acceleration on dry and wet sequences', () => {
  for (const id of ['vanity-left', 'bath-entry']) {
    const run = plan.runs.find(r => r.id === id), ordinary = driveRun(run), gentle = driveRun(run, { gentle: true });
    assert.deepEqual([...gentle.st], [...ordinary.st]);
    assert.deepEqual(gentle.events.list, ordinary.events.list);
  }
});

test('Driving a branch past the same lap distances never collects a remote main-road boost', () => {
  let checked = 0;
  for (const fork of track.forks) {
    const pad = track.boosts.find(p => p.s > fork.fromS + 80 && p.s + p.len < fork.toS - 80);
    if (!pad) continue;
    const st = placeOnFork(fork, { localS: fork.localS(pad.s) - 10, speed: 25 }), feel = makeFeel(), events = recorder();
    const path = { frameAt: (s, out) => fork.frameAt(fork.localS(s), out) };
    for (let tick = 0; tick < 600 && st[S.s] <= pad.s + pad.len + 3; tick++) {
      const ahead = fork.mainS(fork.localS(st[S.s]) + 18);
      step(st, acts({ throttle: 1, steer: steerTo(st, path, ahead, 0) }), track, feel, DT, events);
    }
    assert.ok(st[S.s] > pad.s + pad.len, fork.id);
    assert.equal(contacts(events).length, 0, fork.id); checked++;
  }
  assert.ok(checked > 0, 'at least one real branch passes mapped main pads');
});

for (const held of [false, true]) test('Actual run-up pads leave a landable mirror flight, held rotor ' + held, () => {
  const run = plan.runs.find(r => r.id === 'mirror-runup'), st = supported(run.pads[0].s - 10, 0), feel = makeFeel(), events = recorder();
  let launched = false, landed = false, takeoff = 0;
  for (let tick = 0; tick < 1200; tick++) {
    step(st, acts({ throttle: 1, hop: held, steer: steerTo(st, track, st[S.s] + 18, 0) }), track, feel, DT, events);
    updateRespawn(st, track, feel, DT, events);
    if (!launched && st[S.airborne]) { launched = true; takeoff = st[S.engineV]; }
    if (launched && st[S.grounded]) { landed = true; break; }
  }
  assert.deepEqual(contacts(events), run.pads.map(indexOf));
  assert.ok(landed && st[S.s] >= layout.mirrorJump.landingS0 && st[S.s] < layout.mirrorJump.landingS1);
  assert.ok(takeoff < 31, 'unboosted takeoff after the readable run-up');
  assert.deepEqual(damage(events), []);
});

function followAfterRun(run, { fork = null, stopS }) {
  const st = supported(run.pads[0].s - 10, run.pads[0].x), feel = makeFeel(), events = recorder();
  const ai = createAi(track, feel, { seed: 42, forkChoice: fork ? 'all' : 'none' }), h = createBoudoirHazards(track);
  const racer = { i: 0, kind: 'local', st, feel, events, meta: { isBot: false } }, race = { phase: 'racing', timeS: 0 }, action = acts();
  let used = false;
  for (let tick = 0; tick < 60 * 200; tick++) {
    const previous = cloneState(st), final = run.pads.at(-1);
    if (st[S.s] <= final.s + final.len) Object.assign(action, acts({ throttle: 1, steer: steerTo(st, track, st[S.s] + 18, run.pads[0].x) }));
    else { ai.drive(racer, { race, racers: [racer] }, DT, action); h.steer(racer, action); }
    step(st, action, track, feel, DT, events); h.apply(racer, previous, race.timeS); updateRespawn(st, track, feel, DT, events);
    race.timeS += DT; events.t += DT;
    if (track.forkAtSeg(st[S.seg]) === fork && fork) used = true;
    if (st[S.s] >= stopS && (!fork || used && !track.forkAtSeg(st[S.seg]))) return { st, events, used, done: true };
  }
  return { st, events, used, done: false };
}

for (const fork of track.forks) test('Boosted approach still permits a physical ' + fork.label + ' choice and merge', () => {
  const run = plan.runs.filter(r => r.pads.at(-1).s < fork.fromS).at(-1);
  assert.ok(run, fork.id);
  const result = followAfterRun(run, { fork, stopS: fork.toS + 20 });
  assert.ok(result.done && result.used, fork.id + ' / ' + result.st[S.s]);
  for (const pad of run.pads) assert.ok(contacts(result.events).includes(indexOf(pad)), run.id);
  assert.equal(result.events.all('drop').length, 0, fork.id);
  assert.equal(result.events.all('rescue-pull').length, 0, fork.id);
});

test('Channel boosts still allow the complete compact platform route and exit', () => {
  const run = plan.runs.find(r => r.id === 'bath-entry'), field = track.fields[0];
  const result = followAfterRun(run, { stopS: field.toS + 20 });
  assert.ok(result.done);
  for (const pad of run.pads) assert.ok(contacts(result.events).includes(indexOf(pad)));
  assert.ok(new Set(result.events.all('stone').map(e => e.payload.id)).size >= 8);
  assert.equal(result.events.all('drop').length, 0);
  assert.equal(result.events.all('rescue-pull').length, 0);
});

for (const [seed, gentle] of [[241024, false], [42, true]]) test('All eight racers finish boosted laps with hazards, perfume and items, seed ' + seed, t => {
  const r = makeRace({ track: loadTrack(json), seed, items: createItems, feelOver: { 'gentle.on': gentle } });
  const h = createBoudoirHazards(r.track), perfume = createBoudoirPerfume(r.track), drive = r.ai.drive.bind(r.ai), original = r.racers.step.bind(r.racers);
  r.ai.drive = (kart, ctx, dt, out) => { drive(kart, ctx, dt, out); if (ctx.race.phase === 'racing') h.steer(kart, out); };
  r.racers.step = (dt, a, race, ctx) => { original(dt, a, race, { ...ctx, hazards: h });
    if (race.phase === 'racing' || race.phase === 'finished') perfume.update(r.racers.list, r.racers.clockS, dt); };
  r.run(800, () => r.racers.list.every(k => k.finished));
  const counts = r.racers.list.map(k => ({ racer: k.i, pads: 0, walls: 0 }));
  for (const event of r.log) {
    const bot = event.name === 'racer-event', i = bot ? event.payload.racer : 0, name = bot ? event.payload.name : event.name;
    if (name === 'pad') counts[i].pads++; if (name === 'wall') counts[i].walls++;
  }
  t.diagnostic(JSON.stringify({ seed, gentle, counts, finish: r.racers.list.map(k => k.finishS), perfumeContacts: perfume.hits }));
  assert.ok(r.racers.list.every(k => k.finished && k.lapTimes.length === 2));
  assert.deepEqual(r.racers.list.map(k => k.falls), Array(8).fill(0));
  assert.ok(counts.every(k => k.pads >= 6), 'every actual racing line reaches multiple boost sequences');
  assert.ok(perfume.hits > 0 && h.inspect().ribbonPasses > 0, 'real environmental effects remain active');
});
