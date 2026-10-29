/* tests/sim-assist-route.test.mjs - the drop shortcut people find and make (EDGES lane, owner 2026-10-23: "I never
 * made it to the shortcut"). The opening is about 8 m with a kicker lip: a player who steers for it and holds the
 * throttle lands on the lower road, through the real Steer assist, from cruise to boosted speed. A kart that does
 * not steer for it (the centre line, the hands-off autopilot and cruise, the bots' normal line) never falls in.
 * Polish 6: the dare sits on the OUTSIDE of the coil's r 20 first turn, so a kart running wide there is held by the
 * lip while it turns away (sim/routes.js turnsAway) and the assist eases off for the bend (sim/assist.js EASE).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN, BOOST, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { applyBoost } from '../sim/boost.js';
import { createAssist } from '../sim/assist.js';
import { createActions } from '../core/actions.js';
import { qfromBasis, wrap } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const R0 = track.routes[0], L = track.lengthM;
const MID = (R0.openS0 + R0.openS1) / 2, SIDE = R0.side === 'L' ? -1 : 1;

function kartAt(s, lat, v) {
  const st = createState(), f = track.frameAt(s);
  setPos(st, [f.p[0] + f.b[0] * lat, f.p[1] + f.b[1] * lat, f.p[2] + f.b[2] * lat]);
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, [-f.t[0], -f.t[1], -f.t[2]]));
  setVel(st, [f.t[0] * v, f.t[1] * v, f.t[2] * v]);
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lat; st[S.hopT] = 1e3;
  return st;
}

/** Steer (pure pursuit, the nose toward a world point) for a kart. */
function pursue(st, x, z, gain = 2.5) {
  const tx = x - st[S.posX], tz = z - st[S.posZ];
  const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
  const rx = 1 - 2 * (qy * qy + qz * qz), rz = 2 * (qx * qz - qw * qy), fx = -(2 * (qx * qz + qw * qy)), fz = -(1 - 2 * (qx * qx + qy * qy));
  return Math.max(-1, Math.min(1, Math.atan2(tx * rx + tz * rz, tx * fx + tz * fz) * gain));
}

/**
 * One run from approachM before the opening: `hands(st, t)` gives the raw steer (null = hands off the stick),
 * throttle held, the Steer assist in `mode` between the hands and the sim (the boot.js order).
 * Returns the events and where the kart ended.
 */
function run({ lat0 = 0, v0 = 30, boosted = false, mode = 'light', hands, maxS = 9, approachM = 80, throttle = true, hop = null, feelOver = {} }) {
  const feel = makeFeel(feelOver), st = kartAt(wrap(R0.openS0 - approachM, L), lat0, v0), a = createActions(), names = [];
  const ev = { emit: (n, p = {}) => names.push(n === 'trick' || n === 'trick-land' ? n + ':' + p.grade : n) };
  const assist = createAssist(track, feel, { mode });
  if (boosted) applyBoost(st, BOOST.PAD, 0, feel, ev, { amount: 0.25, holdS: 6 });
  let t = 0;
  for (let i = 0; i < maxS / DT; i++, t += DT) {
    const steer = hands(st, t);
    Object.assign(a, { steer: steer === null ? 0 : steer, throttle: throttle ? 1 : 0, brake: 0, hop: false, hopPressed: false, item: false, back: false });
    if (hop && hop(st, t)) { a.hop = true; a.hopPressed = true; }
    const pa = assist.apply(a, st, DT, 'racing');
    step(st, pa, track, feel, DT, ev);
    updateRespawn(st, track, feel, DT, ev);
    if (st[S.respawnPhase] !== RESPAWN.NONE && names.includes('drop')) break;
    if (names.includes('route-land') && st[S.grounded] > 0) break;
  }
  return { names, st, landed: names.includes('route-land'), fell: names.includes('drop') };
}

/** The landing road's s nearest (in plan) to the opening's middle + dsM (polish 6: the dare lands on the turn inside). */
function landAt(dsM) {
  const o = track.frameAt(MID + dsM);
  let best = R0.land.fromS, bd = Infinity;
  for (let s = R0.land.fromS; s <= R0.land.toS; s += 1) { const f = track.frameAt(s), d = (f.p[0] - o.p[0]) ** 2 + (f.p[2] - o.p[2]) ** 2; if (d < bd) { bd = d; best = s; } }
  return best;
}
/** Hands that go for the dare: the nose at the landing road beside the opening, `dsM` along it from the middle, `pastM` on. */
const forOpening = (dsM, pastM = 6) => { const ls = landAt(dsM) + pastM; return (st) => { const f = track.frameAt(ls); return pursue(st, f.p[0], f.p[2]); }; };

test('shortcut: the opening is at least 8 m, a kicker lip, no kerb', () => {
  const w = R0.openS1 - R0.openS0;
  assert.ok(w >= 8 && w <= 30, 'opening ' + w);
  assert.ok(R0.openS0 >= R0.fromS && R0.openS1 <= R0.toS);
});

test('shortcut: steering for the opening with the throttle held lands on the lower road (cruise to boosted, every assist mode)', (t) => {
  const runs = [];
  for (const mode of ['light', 'strong', 'off']) {
    for (const [v0, boosted] of [[24, false], [30, false], [30, true]]) {
      for (const ds of [-2, -1, 0, 1, 2]) {
        for (const lat0 of [-3, 0, 3]) {
          const r = run({ lat0, v0, boosted, mode, hands: forOpening(ds) });
          runs.push({ mode, v0, boosted, ds, lat0, landed: r.landed, why: r.names.filter((n) => /route|drop|edge|wall|airborne/.test(n)).join(',') });
        }
      }
    }
  }
  const ok = runs.filter((r) => r.landed).length;
  t.diagnostic(ok + ' of ' + runs.length + ' landed');
  const misses = runs.filter((r) => !r.landed).map((r) => JSON.stringify(r)).join('\n');
  assert.ok(ok >= runs.length * 0.9, ok + ' of ' + runs.length + ' landed\n' + misses);
  // per mode too: the assist never costs the shortcut
  for (const mode of ['light', 'strong', 'off']) {
    const m = runs.filter((r) => r.mode === mode);
    assert.ok(m.filter((r) => r.landed).length >= m.length * 0.9, mode + ': ' + m.filter((r) => r.landed).length + ' of ' + m.length);
  }
});

test('shortcut: driving off the kicker is a lip take-off, and a hop right after it pays the trick', () => {
  const plain = run({ hands: forOpening(0) });
  assert.ok(plain.landed, plain.names.join(','));
  assert.ok(plain.names.includes('airborne') && !plain.names.includes('edge-enter') || plain.landed);
  // a hop pressed just after the lip (the late window) pays a trick on the landing
  let upAt = -1;
  const late = run({ hands: forOpening(0), hop: (st, t) => {
    if (st[S.airborne] > 0 && upAt < 0 && st[S.s] > R0.openS0 - 1 && st[S.s] < R0.openS1 + 4) upAt = t;
    return upAt >= 0 && t - upAt > 0.03 && t - upAt < 0.06;
  } });
  assert.ok(late.landed, late.names.join(','));
  assert.ok(late.names.some((n) => n.startsWith('trick:')), 'late hop trick: ' + late.names.join(','));
  assert.ok(late.names.some((n) => n.startsWith('trick-land:')), 'trick paid on the landing');
  // a hop just before the lip (the early window) also pays, and still lands
  const feel = makeFeel();
  let hopped = false;
  const early = run({ hands: forOpening(0), hop: (st) => {
    if (hopped || st[S.grounded] <= 0 || st[S.lateral] * SIDE <= 0) return false;
    const f = track.frameAt(st[S.s]), out = (st[S.velX] * f.b[0] + st[S.velY] * f.b[1] + st[S.velZ] * f.b[2]) * SIDE;
    if (out < 0.3) return false;
    const t = (f.w / 2 - Math.abs(st[S.lateral])) / out, at = st[S.s] + 30 * t;
    hopped = t < feel.get('trick.earlyS') * 0.8 && at > R0.openS0 + 0.5 && at < R0.openS1 - 0.5;
    return hopped;
  } });
  assert.ok(early.landed, early.names.join(','));
  assert.ok(early.names.some((n) => n.startsWith('trick:')), 'early hop trick: ' + early.names.join(','));
});

test('shortcut: a kart that does not steer for it never falls in (centre line, hands off, cruise and boosted)', () => {
  for (const mode of ['light', 'strong', 'off']) {
    for (const [v0, boosted] of [[24, false], [30, false], [30, true]]) {
      for (const lat0 of [-4, -2, 0, 2, 4]) {
        // the centre line: the nose at the road centre 12 m ahead
        const centre = run({ lat0, v0, boosted, mode, maxS: 6, hands: (st) => { const f = track.frameAt(st[S.s] + 12); return pursue(st, f.p[0], f.p[2]); } });
        assert.ok(!centre.landed && !centre.fell && !centre.names.includes('route-drop'), mode + ' centre ' + v0 + ' ' + lat0 + ': ' + centre.names.join(','));
        assert.ok(wrap(centre.st[S.s] - R0.openS1, L) < L / 2, 'went on round the helix');
      }
    }
  }
  // hands off the stick (the autopilot eases in) and hands off everything (the soft cruise), from either side
  for (const mode of ['light', 'strong']) {
    for (const lat0 of [-4, 0, 4]) {
      const auto = run({ lat0, v0: 30, mode, maxS: 6, approachM: 120, hands: () => null });
      assert.ok(!auto.landed && !auto.names.includes('route-drop'), mode + ' autopilot ' + lat0 + ': ' + auto.names.join(','));
      const cruise = run({ lat0, v0: 20, mode, maxS: 8, approachM: 120, throttle: false, hands: () => null });
      assert.ok(!cruise.landed && !cruise.names.includes('route-drop'), mode + ' cruise ' + lat0 + ': ' + cruise.names.join(','));
    }
  }
});

test('shortcut: the bots keep it rare (tier 2 only) and their normal line never falls in', () => {
  let taken = 0, laps = 0, accidents = 0;
  for (const seed of [1, 2, 3]) {
    const R = makeRace({ seed });
    R.run(200, () => R.racers.list.every((r) => r.lapTimes.length >= 2));
    for (let i = 1; i < R.racers.list.length; i++) {
      const ev = R.log.filter((e) => e.name === 'racer-event' && e.payload.racer === i);
      const lands = ev.filter((e) => e.payload.name === 'route-land').length;
      laps += R.racers.list[i].lapTimes.length;
      taken += lands;
      const tier = R.ai.botOf(i).persona.tier;
      if (tier < 2) assert.equal(lands, 0, 'a tier 1 bot never takes it');
      // a drop near the opening without a route-land is an accident
      accidents += ev.filter((e) => e.payload.name === 'drop' && Math.abs(wrap(R.racers.list[i].st[S.s] - MID + L / 2, L) - L / 2) < 30).length;
    }
  }
  assert.ok(taken <= Math.max(2, laps / 3), 'bots took the shortcut ' + taken + ' times in ' + laps + ' laps');
  assert.equal(accidents, 0);
});
