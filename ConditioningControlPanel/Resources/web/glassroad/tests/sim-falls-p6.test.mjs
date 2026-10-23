/* tests/sim-falls-p6.test.mjs - polish 6 falls on a stacked course: the road-beneath query, a fall off an open
 * edge landing on the lower deck (earlier in the lap: lands, loses ground, no credit; later and registered:
 * credited as the route), a short sideways gap jump, nothing beneath keeps the drop and rescue. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { createActions } from '../core/actions.js';
import { qfromBasis } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { fixtureJson } from './sim-fixture-p6.mjs';

const fwd = loadTrack(fixtureJson());
const rev = loadTrack(fixtureJson({ reverse: true }));

/** s on track t of the centreline point nearest (x, y, z). */
const sAt = (t, x, y, z) => t.query(x, y, z).s;

function kartAt(t, s, lat, v, yawDeg) {
  const st = createState(), f = t.frameAt(s), c = Math.cos(yawDeg * Math.PI / 180), sn = Math.sin(yawDeg * Math.PI / 180);
  const tt = [0, 1, 2].map((k) => f.t[k] * c + f.b[k] * sn), b = [0, 1, 2].map((k) => f.b[k] * c - f.t[k] * sn);
  setPos(st, [f.p[0] + f.b[0] * lat, f.p[1] + f.b[1] * lat, f.p[2] + f.b[2] * lat]);
  setQuat(st, qfromBasis([0, 0, 0, 1], b, f.n, [-tt[0], -tt[1], -tt[2]]));
  setVel(st, [tt[0] * v, tt[1] * v, tt[2] * v]);
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lat; st[S.hopT] = 1e3;
  return st;
}

/** Run with throttle (and an optional hop on the first tick) for maxS; returns events and the state. */
function run(t, st, { maxS = 4, checkpoint = 0, hop = false, until = null } = {}) {
  const feel = makeFeel(), a = createActions(), ev = [];
  const sink = { emit: (name, p = {}) => ev.push({ name, p, s: st[S.s] }) };
  const prog = createProgress(t, sink);
  prog.reset(st); st[S.checkpoint] = checkpoint;
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() {} };
  for (let i = 0; i < maxS / DT; i++) {
    a.throttle = 1; a.steer = 0; a.hop = hop && i === 0; a.hopPressed = a.hop;
    step(st, a, t, feel, DT, sink);
    updateRespawn(st, t, feel, DT, sink);
    prog.update(st, race);
    if (until && until(ev, st)) break;
  }
  return { ev, st, names: ev.map((e) => e.name) };
}

test('below: the lower straight under the crossing, the highest road wins, nothing off the course', () => {
  const lowS = sAt(fwd, 120, 0, -16), upS = sAt(fwd, 110, 14, -16);
  let q = fwd.query.below(120, 20, -16);
  assert.ok(q && Math.abs(q.s - lowS) < 0.5 && Math.abs(q.height - 20) < 0.05, 'lower straight beneath');
  q = fwd.query.below(110, 20, -16);                 // over the crossing: the upper deck is nearer
  assert.ok(q && Math.abs(q.s - upS) < 0.5 && Math.abs(q.height - 6) < 0.05);
  q = fwd.query.below(110, 10, -16);                 // between the decks: the lower one
  assert.ok(q && Math.abs(q.s - lowS) < 11 && Math.abs(q.height - 10) < 0.05);
  assert.equal(fwd.query.below(400, 10, 300), null);
  assert.equal(fwd.query.below(120, -3, -16), null, 'a kart already under a road does not land on it');
});

test('fall off the open edge onto the lower deck: lands there, no rescue, no credit, ground lost', () => {
  const s0 = sAt(fwd, 110, 14, 2), lowS = sAt(fwd, 125, 0, -16);
  // nose 35 deg right toward the open side, 18 m/s: out over the lower straight
  const st = kartAt(fwd, s0, 5, 18, 35);
  const r = run(fwd, st, { checkpoint: 2, until: (ev) => ev.some((e) => e.name === 'fall-land') });
  assert.ok(r.names.includes('fall'), r.names.join(','));
  assert.ok(r.names.includes('fall-land'));
  assert.ok(!r.names.includes('drop') && !r.names.includes('rescue-pull'));
  assert.ok(Math.abs(st[S.posY]) < 0.5, 'down on the lower deck, y ' + st[S.posY]);
  assert.ok(Math.abs(st[S.s] - lowS) < 30, 's ' + st[S.s] + ' near ' + lowS);
  assert.ok(st[S.s] < s0 - 200, 'earlier in the lap');
  assert.equal(st[S.checkpoint], 2, 'no checkpoint credited or taken');
  assert.ok(!r.names.includes('checkpoint') && !r.names.includes('route-skip'));
  assert.ok(st[S.latchSeg] === st[S.seg]);
  // and it drives on from there
  run(fwd, st, { maxS: 1, checkpoint: 2 });
  assert.ok(st[S.grounded] > 0 && st[S.respawnPhase] === RESPAWN.NONE && st[S.speedPct] > 0.1);
});

test('nothing beneath: the open edge past the lower deck still drops and rescues', () => {
  const s0 = sAt(fwd, 110, 14, -32);                 // near the end of the open stretch, past the lower road
  const st = kartAt(fwd, s0, 5, 18, 35);
  const r = run(fwd, st, { maxS: 3 });
  assert.ok(!r.names.includes('fall-land'), r.names.join(','));
  assert.ok(r.names.includes('drop'));
});

test('reverse course: the fall lands LATER on a registered route and is credited', () => {
  const s0 = sAt(rev, 110, 14, -32), lowS = sAt(rev, 100, 0, -16);
  assert.ok(lowS > s0 + 100, 'lower straight later in the lap');
  const st = kartAt(rev, s0, -5, 18, -35);            // the open side is the left now
  const r = run(rev, st, { checkpoint: 2, until: (ev) => ev.some((e) => e.name === 'route-skip' || e.name === 'drop') });
  assert.ok(r.names.includes('route-land') || r.names.includes('fall-land'), r.names.join(','));
  assert.ok(r.names.includes('route-skip'), 'progress credits the registered route');
  run(rev, st, { maxS: 0.2, checkpoint: st[S.checkpoint] });
  assert.ok(Math.abs(st[S.posY]) < 0.6);
  assert.ok(st[S.s] > s0 + 100);
  assert.ok(!r.names.includes('drop'));
});

test('a short sideways gap jump onto a road alongside, a little lower, is caught and credited as its route', () => {
  const t = loadTrack(gapCourse(), { validate: false });
  const s0 = 185, st = kartAt(t, s0, 4, 24, 40);
  const r = run(t, st, { hop: true, checkpoint: 0, until: (ev) => ev.some((e) => e.name === 'route-skip' || e.name === 'drop') });
  assert.ok(r.names.includes('route-land') || r.names.includes('fall-land'), r.names.join(','));
  assert.ok(!r.names.includes('drop'));
  assert.ok(r.names.includes("route-skip"), "progress credits the gap route " + r.ev.map((e) => e.name + ":" + Math.round(e.s)).join(","));
  run(t, st, { maxS: 0.3, checkpoint: st[S.checkpoint] });
  assert.ok(Math.abs(st[S.posY] + 2) < 0.6, 'on the lower road, y ' + st[S.posY]);
  assert.ok(st[S.s] > t.lengthM / 2, 'on the later stretch, s ' + st[S.s]);
});

/** A hairpin course: out along z 0 (open on its right, +z, over x 20..70), back along z 20 (x 150 .. -140) two metres lower
 * (later in the lap), 4 m of air between the two road edges; a route over the gap. */
function gapCourse() {
  const nodes = [];
  const add = (x, y, z, flags) => nodes.push({ p: [x, y, z], roll: 0, w: 16, flags, speed: 1, surface: 'glass' });
  for (let x = -150; x <= 150; x += 10) add(x, 0, 0, x === -150 ? ['start', 'rail'] : x >= 20 && x <= 70 ? ['railL'] : ['rail']);
  for (let k = 1; k < 6; k++) { const a = -Math.PI / 2 + Math.PI * k / 6; add(150 + 10 * Math.cos(a), -2 * k / 6, 10 + 10 * Math.sin(a), ['rail']); }
  for (let x = 150; x >= -140; x -= 10) add(x, -2, 20, ['rail']);
  for (let k = 1; k < 6; k++) { const a = Math.PI / 2 + Math.PI * k / 6; add(-150 + 10 * Math.cos(a), -2 + 2 * k / 6, 10 + 10 * Math.sin(a), ['rail']); }
  const N = nodes.length, back = nodes.findIndex((n) => n.p[2] === 20);
  const land = nodes.map((n, i) => (n.p[2] === 20 && n.p[0] <= 90 && n.p[0] >= 0 ? i : -1)).filter((i) => i >= 0);
  return {
    format: 'glassroad-track', version: 1, id: 'gap-fixture', name: { en: 'gap' }, loop: true, laps: 3, units: 'm', nodes,
    sections: [{ id: 'a', act: 'induction', palette: 'dawn', sky: 'x', bandSpeed: 1, from: 0, to: N }],
    checkpoints: [0, back], spawn: { node: 0, grid: [[0, -5]] }, items: [], boosts: [], jumps: [], hazards: [], media: [],
    routes: [{ id: 'gap', kind: 'drop', side: 'R', from: nodes.findIndex((n) => n.p[0] === 20), to: nodes.findIndex((n) => n.p[0] === 80), land: { from: land[0], to: land[land.length - 1] }, pad: { node: land[2], x: 0 } }],
    ai: { lines: [], pace: [1] }, beat: { bpm: null, offsetMs: 0, map: [] }, limits: { fallY: -30, edgeGraceM: 1.2, edgeGraceS: 0.8 },
  };
}

test('a gap route keeps its speed (no soft catch); a drop route still catches', () => {
  for (const kind of ['gap', 'drop']) {
    const json = gapCourse();
    json.routes[0].kind = kind;
    const t = loadTrack(json, { validate: false }), st = kartAt(t, 185, 4, 24, 40);
    const r = run(t, st, { hop: true, until: (ev) => ev.some((e) => e.name === 'route-land' || e.name === 'drop') });
    assert.ok(r.names.includes('route-land'), kind + ': ' + r.names.join(','));
    if (kind === 'gap') assert.ok(st[S.engineV] > 15, 'gap keeps speed ' + st[S.engineV]);
    else assert.ok(st[S.engineV] <= 0.25 * 30 + 1e-6, 'drop catches ' + st[S.engineV]);
  }
});
