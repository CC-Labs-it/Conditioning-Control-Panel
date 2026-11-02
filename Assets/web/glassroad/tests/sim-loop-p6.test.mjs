/* tests/sim-loop-p6.test.mjs - polish 6 THE LOOP on the stacked fixture (tests/sim-fixture-p6.mjs): the player holds
 * the vertical loop at any speed (magnet), a hop at the top lands back on it, a bubble inside it pops, respawn never
 * anchors inside it, and the bots drive the whole stacked course, loop included, without a drop. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn, anchorNode } from '../sim/respawn.js';
import { createActions } from '../core/actions.js';
import { createItems } from '../sim/items.js';
import { qfromBasis } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';
import { fixtureJson } from './sim-fixture-p6.mjs';

const json = fixtureJson();
const track = loadTrack(json);
const loopNodes = json.nodes.map((n, i) => (n.flags.includes('magnet') ? i : -1)).filter((i) => i >= 0);
const L0 = loopNodes[0], L1 = loopNodes[loopNodes.length - 1] + 1;   // entry node, exit node
const loopS0 = track.nodeS[L0], loopS1 = track.nodeS[L1];

function kartAt(s, v) {
  const st = createState(), f = track.frameAt(s);
  setPos(st, f.p); setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, [-f.t[0], -f.t[1], -f.t[2]]));
  setVel(st, [f.t[0] * v, f.t[1] * v, f.t[2] * v]);
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.hopT] = 1e3;
  return st;
}

/** Drive from s0 at v with throttle `thr` until past the loop exit or maxS; hopAtTop presses hop once near the top. */
function through(v, { thr = 1, hopAtTop = false, maxS = 12, coastIn = false } = {}) {
  const feel = makeFeel(), st = kartAt(loopS0 - (coastIn ? 1 : 15), v), a = createActions(), ev = [];
  const sink = { emit: (name, p = {}) => ev.push({ name, p, s: st[S.s] }) };
  let maxY = -1e9, hopped = false, minUpY = 1;
  for (let i = 0; i < maxS / DT; i++) {
    a.throttle = thr; a.steer = 0; a.hop = false; a.hopPressed = false;
    const top = st[S.posY] > 2 * 15 - 2;
    if (hopAtTop && top && !hopped && st[S.grounded] > 0) { a.hop = a.hopPressed = true; hopped = true; }
    step(st, a, track, feel, DT, sink);
    updateRespawn(st, track, feel, DT, sink);
    maxY = Math.max(maxY, st[S.posY]);
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    minUpY = Math.min(minUpY, 1 - 2 * (qx * qx + qz * qz));
    if (st[S.s] > loopS1 + 10 && st[S.s] < loopS1 + 60) break;
  }
  return { st, ev, names: ev.map((e) => e.name), maxY, hopped, minUpY };
}

test('loop fixture: the frames go round the wheel (normal points down at the top, no flip)', () => {
  let topN = 1;
  for (let s = loopS0; s <= loopS1; s += 1) { const f = track.frameAt(s); if (f.p[1] > 28) topN = Math.min(topN, f.n[1]); }
  assert.ok(topN < -0.9, 'n.y at the top ' + topN);
});

for (const pct of [1, 0.6, 0.4, 0.2]) {
  test('loop: a kart at ' + Math.round(pct * 100) + '% of top goes all the way round and out, no drop', () => {
    // under 40% the player is off the throttle: the loop still carries the kart round
    const r = through(30 * pct, { thr: pct >= 0.4 ? 1 : 0, coastIn: pct < 0.4 });
    assert.ok(!r.names.includes('drop') && !r.names.includes('rescue-pull'), r.names.join(','));
    assert.ok(r.maxY > 27, 'went over the top, max y ' + r.maxY);
    assert.ok(r.minUpY < -0.8, 'upside down at the top ' + r.minUpY);
    assert.ok(r.st[S.s] > loopS1 + 5, 'out of the loop, s ' + r.st[S.s] + ' exit ' + loopS1);
    assert.ok(r.st[S.grounded] > 0 && Math.abs(r.st[S.posY]) < 1);
  });
}

test('loop: a hop at the top falls back onto the loop (magnet), not to the ground', () => {
  const r = through(24, { hopAtTop: true });
  assert.ok(r.hopped);
  assert.ok(!r.names.includes('drop'), r.names.join(','));
  assert.ok(r.st[S.s] > loopS1 + 5);
});

test('respawn: a latch inside the loop anchors before it, on the entry straight', () => {
  const st = createState();
  for (const n of loopNodes) {
    st[S.latchSeg] = n;
    const a = anchorNode(st, track);
    assert.ok(!track.nodeFlags(a).includes('magnet'), 'node ' + n + ' -> ' + a);
    assert.ok(a < L0 || a > L1 + 40, 'before the loop: ' + a);
  }
});

test('items: a pickup bubble inside the loop pops for a kart driving through', () => {
  const j = fixtureJson();
  j.items.push({ node: loopNodes[5], x: 0, kind: 'bubble' });   // at the top half
  const t = loadTrack(j), feel = makeFeel(), log = [];
  const ev = { emit: (name, p = {}) => log.push({ name, p }) };
  const it = createItems(t, feel, ev, { seed: 3 });
  const st = kartAt(loopS0 - 15, 24);
  const a = createActions();
  const r = { i: 0, st, item: { id: null, rolling: false, rollT: 0, heldT: 0 }, input: { item: false, back: false }, mods: {}, fxv: {}, finished: false, feel };
  const standings = { placeOf: () => 1, gapToLeader: () => 0, progressOf: () => 0, order: [0] };
  for (let i = 0; i < 6 / DT && !log.some((e) => e.name === 'bubble-pop'); i++) {
    a.throttle = 1;
    step(st, a, t, feel, DT, ev);
    it.update(DT, [r], standings, { phase: 'racing' });
  }
  assert.ok(log.some((e) => e.name === 'bubble-pop'), log.map((e) => e.name).join(','));
  // the bubble sits on the inside of the wheel: its point is on the road side of the loop
  const b = it.view().bubbles[0];
  assert.ok(b.p[1] > 15, 'bubble up in the loop, y ' + b.p[1]);
});

test('bots: four bots race two laps of the stacked course, loop included, with no drop', () => {
  const race = makeRace({ seed: 5, count: 4, track });
  race.run(150, () => race.racers.list.every((r) => (r.st[S.lap] | 0) >= 2));
  const laps = race.racers.list.map((r) => r.st[S.lap] | 0);
  assert.ok(laps.every((l) => l >= 2), 'laps ' + laps.join(','));
  const drops = race.events('drop').length + race.botEvents('drop').length;
  assert.equal(drops, 0, 'drops');
});
