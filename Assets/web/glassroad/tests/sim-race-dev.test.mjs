/* tests/sim-race-dev.test.mjs - the ?dev=1 scripted drives (dev/hook.js): the query gate, the next bend, the
 * placement, a tier 3 drift boost on a bend and the drop shortcut flown to the catch, on the real Threshold. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, createState, cloneState } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { createActions } from '../core/actions.js';
import { createBus, createQueue } from '../core/events.js';
import { devAllowed, nextBend, placeKart, lastLineBefore, turnAt, createDevHook } from '../dev/hook.js';
import { makeFeel, DT } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));

test('dev hook: only ?dev=1 opens it', () => {
  assert.equal(devAllowed('?dev=1'), true);
  assert.equal(devAllowed('?bot=1&dev=1'), true);
  assert.equal(devAllowed(''), false);
  assert.equal(devAllowed('?dev=0'), false);
  assert.equal(devAllowed('?dev=true'), false);
  assert.equal(devAllowed(undefined), false);
});

test('dev hook: nextBend finds a real bend ahead with its side', () => {
  const b = nextBend(track, 0);
  assert.ok(b, 'a bend on Threshold');
  assert.ok(b.s >= 20 && b.s < track.lengthM);
  assert.ok(b.dir === 1 || b.dir === -1);
  assert.ok(b.deg >= 18);
  assert.equal(Math.sign(turnAt(track, b.s, 24)), b.dir);
  const later = nextBend(track, b.endS + 5);
  assert.ok(later && later.s !== b.s, 'scanning on from the end finds the next one');
});

test('dev hook: placeKart puts the kart on the road, along it, keeping the lap', () => {
  const feel = makeFeel(), st = createState();
  st[S.lap] = 1; st[S.driftDir] = 1; st[S.boostKind] = 3;
  placeKart(st, track, feel, 300, 2, 20);
  const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg], { normal: [0, 0, 0], p: [0, 0, 0] });
  assert.ok(q.onRoad);
  assert.ok(Math.abs(q.lateral - 2) < 0.05);
  assert.ok(Math.abs(q.s - 300) < 0.5);
  assert.equal(st[S.lap], 1);
  assert.equal(st[S.driftDir], 0);
  assert.equal(st[S.boostKind], 0);
  assert.equal(st[S.checkpoint], lastLineBefore(track, 300));
  assert.ok(Math.abs(st[S.speedPct] - 20 / feel.get('speed.topMps')) < 1e-6);
});

/** Run a hook job on a lone player with the real kart step until done or maxS. */
function runJob(start, maxS = 9) {
  const feel = makeFeel(), bus = createBus(), queue = createQueue(), names = [];
  bus.on('*', (p, name) => names.push(name));
  const st = createState();
  placeKart(st, track, feel, 0, 0, 0);
  const progress = createProgress(track, queue);
  progress.reset(st);
  const P = { st, prev: cloneState(st), view: cloneState(st), progress };
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() { this.phase = 'finished'; } };
  const hook = createDevHook({ track, feel, bus, player: () => P, race: () => race, emit: (n, p) => queue.emit(n, p) });
  const status = start(hook.api);
  queue.flush(bus);
  const a = createActions();
  let tail = 60;                                     // a second on after the job ends (the lap judge runs on the ground)
  for (let i = 0; i < maxS / DT && (hook.active || tail-- > 0); i++) {
    if (!hook.apply(a, DT)) Object.assign(a, { steer: 0, throttle: 1, hop: false, hopPressed: false });
    step(st, a, track, feel, DT, queue);
    updateRespawn(st, track, feel, DT, queue);
    race.timeS += DT; progress.update(st, race);
    queue.flush(bus);
  }
  return { status: hook.api.status, names, first: status, hook };
}

test('dev hook: drift() charges tier 3 on the next bend and pays a tier 3 boost', () => {
  const { status, names } = runJob((api) => api.drift());
  assert.equal(status.kind, 'drift');
  assert.equal(status.ok, true, JSON.stringify(status));
  assert.equal(status.phase, 'boost');
  assert.equal(status.tier, 3);
  assert.ok(names.includes('drift-boost'));
  assert.ok(status.events.includes('drift-boost 3'), status.events.join(', '));
});

test('dev hook: drift({ release: false }) keeps the slide charged and hands back', () => {
  const { status } = runJob((api) => api.drift({ holdS: 0.8, release: false }));
  assert.equal(status.phase, 'held', JSON.stringify(status));
  assert.equal(status.tier, 3);
  assert.ok(!status.events.some((e) => e.startsWith('drift-boost')), 'no boost while the script held the slide');
});

test('dev hook: drop() flies the kicker to the catch on the lower road', () => {
  const { status, names } = runJob((api) => api.drop());
  assert.equal(status.kind, 'drop');
  assert.equal(status.phase, 'caught', JSON.stringify(status));
  assert.ok(status.land && status.land.skipM > 100, JSON.stringify(status.land));
  assert.ok(names.includes('route-land'));
  assert.ok(names.includes('route-skip'), 'the skip is a registered one');
  assert.ok(!names.includes('drop'));
});

test('dev hook: refused when not racing or when boot refuses (online)', () => {
  const feel = makeFeel(), st = createState();
  const P = { st, prev: cloneState(st), view: cloneState(st) };
  let phase = 'countdown', why = null;
  const hook = createDevHook({ track, feel, bus: null, player: () => P, race: () => ({ phase }), emit: () => {}, refuse: () => why });
  assert.equal(hook.api.drift().phase, 'refused');
  assert.equal(hook.api.status.reason, 'not racing');
  phase = 'racing'; why = 'online';
  assert.equal(hook.api.drop().phase, 'refused');
  assert.equal(hook.api.status.reason, 'online');
  assert.equal(hook.active, false);
  assert.equal(hook.apply(createActions(), DT), false, 'no job: the hands are never touched');
});
